using HabitFlow.Domain.Common;

namespace HabitFlow.Domain.Habits;

public sealed record HabitDetails(
    string Name,
    string? Description,
    Guid CategoryId,
    HabitColor Color,
    string Icon,
    string? Unit,
    string? TimeZoneId);

/// <summary>
/// A habit with its versioned rules (<see cref="Schedules"/>) and archive pauses (<see cref="Pauses"/>).
/// Rule changes only ever touch the version starting "today" (or the start date for habits that have not started),
/// never past versions, so historical results are never rewritten.
/// </summary>
public sealed class Habit : OwnedEntity
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;
    public const int UnitMaxLength = 24;

    private readonly List<HabitSchedule> _schedules = [];
    private readonly List<HabitPause> _pauses = [];

    private Habit()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CategoryId { get; private set; }
    public HabitColor Color { get; private set; }
    public string Icon { get; private set; } = HabitIcons.Default;
    public HabitType Type { get; private set; }
    public string? Unit { get; private set; }
    public DateOnly StartDate { get; private set; }

    /// <summary>Optional override of the owner's time zone (IANA id). Null = follow the user's zone.</summary>
    public string? TimeZoneId { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<HabitSchedule> Schedules => _schedules;
    public IReadOnlyList<HabitPause> Pauses => _pauses;

    public bool IsArchived => ArchivedAt is not null;

    public static Habit Create(
        Guid userId,
        HabitDetails details,
        HabitType type,
        DateOnly startDate,
        ScheduleSpec schedule,
        DateTimeOffset now)
    {
        var habit = new Habit { UserId = userId, Type = type, StartDate = startDate };
        habit.ApplyDetails(details);
        habit.EnsureTargetMatchesType(schedule);
        habit._schedules.Add(new HabitSchedule(habit.Id, startDate, schedule, now));
        return habit;
    }

    public void Update(HabitDetails details)
    {
        EnsureNotArchived();
        ApplyDetails(details);
    }

    /// <summary>
    /// Changes the frequency and/or daily target from <paramref name="today"/> onwards. Several edits on the same day
    /// collapse into one version; reverting a same-day edit removes it so the previous version simply continues.
    /// </summary>
    /// <returns>True when the rules in force today changed.</returns>
    public bool ChangeSchedule(ScheduleSpec spec, DateOnly today, DateTimeOffset now)
    {
        EnsureNotArchived();
        EnsureTargetMatchesType(spec);

        var effectiveFrom = today < StartDate ? StartDate : today;

        // Versions dated after "today" can only exist if the zone moved backwards; the latest edit wins.
        _schedules.RemoveAll(s => s.EffectiveFrom > effectiveFrom);

        var current = ScheduleOn(effectiveFrom)!;
        if (current.Spec == spec)
        {
            return false;
        }

        if (current.EffectiveFrom == effectiveFrom)
        {
            var previous = _schedules.Where(s => s.EffectiveFrom < effectiveFrom).MaxBy(s => s.EffectiveFrom);
            if (previous is not null && previous.Spec == spec)
            {
                // Keeps the EveryNDays cadence anchored where it was.
                _schedules.Remove(current);
            }
            else
            {
                current.Apply(spec);
            }
        }
        else
        {
            _schedules.Add(new HabitSchedule(Id, effectiveFrom, spec, now));
        }

        return true;
    }

    /// <summary>The start date can only move while there is no history; the schedule history then restarts.</summary>
    public void ChangeStartDate(DateOnly startDate, bool hasLogs, DateOnly today, DateTimeOffset now)
    {
        if (startDate == StartDate)
        {
            return;
        }

        EnsureNotArchived();
        if (hasLogs)
        {
            throw new DomainException("habit.start_date_locked",
                "No puedes cambiar la fecha de inicio de un hábito que ya tiene registros.");
        }

        var spec = SpecOn(today);
        _schedules.Clear();
        _pauses.Clear();
        StartDate = startDate;
        _schedules.Add(new HabitSchedule(Id, startDate, spec, now));
    }

    /// <summary>Archives from today: today and later days are paused until the habit is restored.</summary>
    public void Archive(DateOnly today, DateTimeOffset now)
    {
        EnsureNotArchived();
        _pauses.Add(new HabitPause(Id, today));
        ArchivedAt = now;
    }

    /// <summary>Restores from today: the archived span stays paused (not counted as missed).</summary>
    public void Restore(DateOnly today)
    {
        if (!IsArchived)
        {
            throw new DomainException("habit.not_archived", "El hábito no está archivado.");
        }

        var open = _pauses.Single(p => p.EndDate is null);
        if (open.StartDate >= today)
        {
            // Archived and restored on the same day: no day was actually paused.
            _pauses.Remove(open);
        }
        else
        {
            open.Close(today.AddDays(-1));
        }

        ArchivedAt = null;
    }

    /// <summary>The version in force on <paramref name="date"/>, or null before the start date.</summary>
    public HabitSchedule? ScheduleOn(DateOnly date) =>
        date < StartDate
            ? null
            : _schedules.Where(s => s.EffectiveFrom <= date).MaxBy(s => s.EffectiveFrom);

    /// <summary>The rules in force on <paramref name="date"/> (the initial ones before the start date).</summary>
    public ScheduleSpec SpecOn(DateOnly date) =>
        (ScheduleOn(date) ?? _schedules.MinBy(s => s.EffectiveFrom)
            ?? throw new InvalidOperationException("A habit always has at least one schedule version.")).Spec;

    public bool IsPausedOn(DateOnly date) => _pauses.Any(p => p.Covers(date));

    public void EnsureCanLog(DateOnly date, DateOnly today)
    {
        if (IsArchived)
        {
            throw new DomainException("habit.archived", "Restaura el hábito para poder registrar progreso.");
        }

        if (date < StartDate)
        {
            throw new DomainException("log.before_start", "La fecha es anterior al inicio del hábito.");
        }

        if (date > today)
        {
            throw new DomainException("log.future_date", "No puedes registrar progreso en días futuros.");
        }
    }

    /// <summary>
    /// Whether a log with these values completes the habit on <paramref name="date"/>, using the daily target that was
    /// in force that day. Binary habits use the explicit flag; quantity habits compare against the target
    /// (no target = any amount above zero).
    /// </summary>
    public bool EvaluateCompletion(DateOnly date, decimal? quantity, bool completed)
    {
        if (Type == HabitType.Binary)
        {
            return completed;
        }

        var amount = quantity ?? 0;
        var target = SpecOn(date).TargetValue;
        return target is null ? amount > 0 : amount >= target;
    }

    private void ApplyDetails(HabitDetails details)
    {
        Name = Guard.Required(details.Name, NameMaxLength, "nombre");
        Description = Guard.Optional(details.Description, DescriptionMaxLength, "descripción");
        CategoryId = details.CategoryId;
        Color = details.Color;
        Icon = HabitIcons.All.Contains(details.Icon)
            ? details.Icon
            : throw new DomainException("habit.invalid_icon", "Icono no válido.");
        TimeZoneId = string.IsNullOrWhiteSpace(details.TimeZoneId) ? null : details.TimeZoneId.Trim();

        var unit = Guard.Optional(details.Unit, UnitMaxLength, "unidad");
        Unit = Type switch
        {
            HabitType.Quantity when unit is null => throw new DomainException("habit.unit_required",
                "Indica la unidad de medida (por ejemplo, minutos o páginas)."),
            HabitType.Quantity => unit,
            _ => null,
        };
    }

    private void EnsureTargetMatchesType(ScheduleSpec spec)
    {
        if (Type == HabitType.Binary && spec.TargetValue is not null)
        {
            throw new DomainException("schedule.target_not_allowed",
                "Los hábitos de sí/no no tienen meta diaria numérica.");
        }
    }

    private void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new DomainException("habit.archived", "Restaura el hábito para poder modificarlo.");
        }
    }
}
