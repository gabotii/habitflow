using HabitFlow.Domain.Common;

namespace HabitFlow.Domain.Goals;

public enum GoalMetric
{
    /// <summary>Progress = sum of manual progress entries.</summary>
    Manual,

    /// <summary>Progress = completed days of the linked habits inside the goal window.</summary>
    HabitCompletions,

    /// <summary>Progress = sum of quantities logged in the linked quantity habits (same unit) inside the window.</summary>
    HabitQuantity,

    /// <summary>Progress = longest streak of the single linked habit inside the window.</summary>
    HabitStreak,
}

public enum GoalStatus
{
    Active,
    Paused,
    Completed,
    Cancelled,
}

public sealed class Goal : OwnedEntity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int UnitMaxLength = 24;
    public const decimal MaxTarget = 10_000_000m;
    public const int MaxLinkedHabits = 10;

    private readonly List<GoalHabit> _habits = [];

    private Goal()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public GoalMetric Metric { get; private set; }
    public decimal TargetValue { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly? Deadline { get; private set; }
    public GoalStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyList<GoalHabit> Habits => _habits;

    public bool IsHabitBased => Metric != GoalMetric.Manual;

    public static Goal Create(
        Guid userId,
        string name,
        string? description,
        GoalMetric metric,
        decimal targetValue,
        string unit,
        DateOnly startDate,
        DateOnly? deadline,
        IReadOnlyCollection<Guid> habitIds)
    {
        var goal = new Goal { UserId = userId, Metric = metric, Status = GoalStatus.Active, StartDate = startDate };
        goal.ApplyDetails(name, description, targetValue, unit, deadline);

        var distinct = habitIds.Distinct().ToList();
        switch (metric)
        {
            case GoalMetric.Manual when distinct.Count > 0:
                throw new DomainException("goal.habits_not_allowed", "Un objetivo manual no se vincula a hábitos.");
            case GoalMetric.HabitStreak when distinct.Count != 1:
                throw new DomainException("goal.single_habit_required",
                    "Un objetivo de racha se vincula exactamente a un hábito.");
            case GoalMetric.HabitCompletions or GoalMetric.HabitQuantity when distinct.Count == 0:
                throw new DomainException("goal.habit_required", "Vincula al menos un hábito.");
        }

        if (distinct.Count > MaxLinkedHabits)
        {
            throw new DomainException("goal.too_many_habits", $"Puedes vincular como máximo {MaxLinkedHabits} hábitos.");
        }

        goal._habits.AddRange(distinct.Select(id => new GoalHabit(goal.Id, id)));
        return goal;
    }

    /// <summary>
    /// Name, description, target, unit (manual goals only) and deadline can change after progress exists; the
    /// percentage is simply recomputed. The metric and the linked habits cannot (they define what progress means).
    /// </summary>
    public void Update(string name, string? description, decimal targetValue, string unit, DateOnly? deadline)
    {
        if (Status == GoalStatus.Cancelled)
        {
            throw new DomainException("goal.cancelled", "Un objetivo cancelado no se puede modificar.");
        }

        ApplyDetails(name, description, targetValue, IsHabitBased ? Unit : unit, deadline);
    }

    public void ChangeStatus(GoalStatus target, DateTimeOffset now)
    {
        var allowed = (Status, target) switch
        {
            (GoalStatus.Active, GoalStatus.Paused or GoalStatus.Completed or GoalStatus.Cancelled) => true,
            (GoalStatus.Paused, GoalStatus.Active or GoalStatus.Completed or GoalStatus.Cancelled) => true,
            (GoalStatus.Completed, GoalStatus.Active) => true,
            _ => false,
        };

        if (!allowed)
        {
            throw new DomainException("goal.invalid_transition",
                $"No se puede pasar de «{Describe(Status)}» a «{Describe(target)}».");
        }

        Status = target;
        CompletedAt = target == GoalStatus.Completed ? now : null;
    }

    public void EnsureAcceptsEntries(DateOnly date, DateOnly today)
    {
        if (Metric != GoalMetric.Manual)
        {
            throw new DomainException("goal.not_manual",
                "El progreso de este objetivo se calcula a partir de sus hábitos.");
        }

        if (Status != GoalStatus.Active)
        {
            throw new DomainException("goal.not_active", "Solo puedes registrar progreso en objetivos activos.");
        }

        if (date < StartDate || date > today)
        {
            throw new DomainException("goal.entry_date_out_of_range",
                "La fecha debe estar entre el inicio del objetivo y hoy.");
        }
    }

    /// <summary>Last day that counts towards progress: the deadline, or today if earlier.</summary>
    public DateOnly WindowEnd(DateOnly today) => Deadline is { } deadline && deadline < today ? deadline : today;

    private void ApplyDetails(string name, string? description, decimal targetValue, string unit, DateOnly? deadline)
    {
        Name = Guard.Required(name, NameMaxLength, "nombre");
        Description = Guard.Optional(description, DescriptionMaxLength, "descripción");
        Unit = Guard.Required(unit, UnitMaxLength, "unidad");
        if (targetValue <= 0 || targetValue > MaxTarget || decimal.Round(targetValue, 2) != targetValue)
        {
            throw new DomainException("goal.invalid_target", "La meta debe ser mayor que 0, con 2 decimales como máximo.");
        }

        if (deadline is { } d && d < StartDate)
        {
            throw new DomainException("goal.invalid_deadline", "La fecha límite no puede ser anterior al inicio.");
        }

        TargetValue = targetValue;
        Deadline = deadline;
    }

    private static string Describe(GoalStatus status) => status switch
    {
        GoalStatus.Active => "activo",
        GoalStatus.Paused => "en pausa",
        GoalStatus.Completed => "completado",
        _ => "cancelado",
    };
}

public sealed class GoalHabit
{
    private GoalHabit()
    {
    }

    internal GoalHabit(Guid goalId, Guid habitId)
    {
        GoalId = goalId;
        HabitId = habitId;
    }

    public Guid GoalId { get; private set; }
    public Guid HabitId { get; private set; }
}

public sealed class GoalProgressEntry : OwnedEntity
{
    public const int NoteMaxLength = 300;
    public const decimal MaxAmount = 1_000_000m;

    private GoalProgressEntry()
    {
    }

    public Guid GoalId { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Note { get; private set; }

    public static GoalProgressEntry Create(Goal goal, DateOnly date, decimal amount, string? note)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("goal.invalid_amount", "La cantidad debe ser mayor que 0, con 2 decimales como máximo.");
        }

        return new GoalProgressEntry
        {
            UserId = goal.UserId,
            GoalId = goal.Id,
            LocalDate = date,
            Amount = amount,
            Note = Guard.Optional(note, NoteMaxLength, "nota"),
        };
    }
}

/// <summary>How progress is presented: the percentage never exceeds 100; any surplus is reported separately.</summary>
public sealed record GoalProgress(decimal Current, decimal Target, decimal Percent, decimal ExceededBy, bool TargetReached)
{
    public static GoalProgress Compute(decimal current, decimal target)
    {
        var percent = target <= 0 ? 0 : Math.Min(100m, Math.Round(current / target * 100m, 1, MidpointRounding.ToZero));
        return new GoalProgress(current, target, percent, Math.Max(0, current - target), current >= target);
    }
}
