using HabitFlow.Domain.Common;
using HabitFlow.Domain.Time;

namespace HabitFlow.Domain.Reminders;

public enum ReminderChannel
{
    /// <summary>Notification inside the app only.</summary>
    InApp,

    /// <summary>In-app notification plus e-mail (only delivered when SMTP is configured and the user allows it).</summary>
    InAppAndEmail,
}

/// <summary>
/// A recurring rule (wall-clock time + weekdays + IANA zone). <see cref="NextFireAt"/> is the materialised next
/// instant, recomputed whenever the rule changes and after every firing.
/// </summary>
public sealed class Reminder : OwnedEntity
{
    public const int TitleMaxLength = 100;
    public const int DescriptionMaxLength = 300;

    private Reminder()
    {
    }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TimeOnly LocalTime { get; private set; }
    public int DaysMask { get; private set; }
    public string TimeZoneId { get; private set; } = TimeZoneIds.Default;
    public bool IsActive { get; private set; }
    public ReminderChannel Channel { get; private set; }
    public Guid? HabitId { get; private set; }
    public Guid? GoalId { get; private set; }
    public DateTimeOffset? NextFireAt { get; private set; }
    public DateTimeOffset? LastFiredAt { get; private set; }

    public static Reminder Create(
        Guid userId,
        string title,
        string? description,
        TimeOnly localTime,
        int daysMask,
        string timeZoneId,
        ReminderChannel channel,
        Guid? habitId,
        Guid? goalId,
        DateTimeOffset now)
    {
        var reminder = new Reminder { UserId = userId, IsActive = true };
        reminder.Update(title, description, localTime, daysMask, timeZoneId, channel, habitId, goalId, now);
        return reminder;
    }

    public void Update(
        string title,
        string? description,
        TimeOnly localTime,
        int daysMask,
        string timeZoneId,
        ReminderChannel channel,
        Guid? habitId,
        Guid? goalId,
        DateTimeOffset now)
    {
        if (habitId is not null && goalId is not null)
        {
            throw new DomainException("reminder.single_target",
                "Un recordatorio puede vincularse a un hábito o a un objetivo, no a ambos.");
        }

        if (!Weekdays.IsValid(daysMask))
        {
            throw new DomainException("reminder.invalid_days", "Elige al menos un día de la semana.");
        }

        if (!TimeZoneIds.TryResolve(timeZoneId, out _))
        {
            throw new DomainException("reminder.invalid_time_zone", "Zona horaria desconocida.");
        }

        Title = Guard.Required(title, TitleMaxLength, "título");
        Description = Guard.Optional(description, DescriptionMaxLength, "descripción");
        LocalTime = new TimeOnly(localTime.Hour, localTime.Minute);
        DaysMask = daysMask;
        TimeZoneId = timeZoneId;
        Channel = channel;
        HabitId = habitId;
        GoalId = goalId;
        Reschedule(now);
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        Reschedule(now);
    }

    /// <summary>Marks the occurrence at <paramref name="occurrence"/> as fired and moves to the next one.</summary>
    public void MarkFired(DateTimeOffset occurrence, DateTimeOffset now)
    {
        LastFiredAt = occurrence;
        Reschedule(now > occurrence ? now : occurrence);
    }

    /// <summary>Recomputes the next firing strictly after <paramref name="after"/> (null when inactive).</summary>
    public void Reschedule(DateTimeOffset after) =>
        NextFireAt = IsActive ? NextOccurrence(LocalTime, DaysMask, TimeZoneIds.Resolve(TimeZoneId), after) : null;

    /// <summary>
    /// First instant strictly after <paramref name="after"/> whose local date is one of the selected weekdays at the
    /// given wall time, applying the DST rules of <see cref="ZonedTime.ToInstant"/>.
    /// </summary>
    public static DateTimeOffset NextOccurrence(TimeOnly localTime, int daysMask, TimeZoneInfo zone, DateTimeOffset after)
    {
        var date = ZonedTime.LocalDate(after, zone).AddDays(-1);
        for (var i = 0; i < 9; i++, date = date.AddDays(1))
        {
            if (!Weekdays.Contains(daysMask, date.DayOfWeek))
            {
                continue;
            }

            var candidate = ZonedTime.ToInstant(date, localTime, zone);
            if (candidate > after)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A valid weekday mask always yields an occurrence within 8 days.");
    }
}
