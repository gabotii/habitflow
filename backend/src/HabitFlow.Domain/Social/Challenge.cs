using System.Security.Cryptography;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Habits;

namespace HabitFlow.Domain.Social;

public enum ChallengeRule
{
    /// <summary>Complete the linked habit N times between the start and end dates.</summary>
    CompletionCount,

    /// <summary>Accumulate N units (pages, minutes…) in a quantity habit with the same unit.</summary>
    QuantityTotal,

    /// <summary>Complete the habit every day of the challenge (the target is the number of days).</summary>
    DailyCompletion,
}

public enum ChallengePhase
{
    Upcoming,
    Active,
    Finished,
}

public enum ChallengeResult
{
    InProgress,
    Completed,
    NotCompleted,
    Abandoned,
}

/// <summary>
/// A time-boxed challenge (up to 31 days, typically a week) that users join with a code. Each participant links one of
/// their own habits; progress is always derived from that participant's own logs, so nobody can change anyone else's
/// result.
/// </summary>
public sealed class Challenge : Entity
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;
    public const int MaxDays = 31;
    public const int MaxParticipants = 50;

    private readonly List<ChallengeParticipant> _participants = [];

    private Challenge()
    {
    }

    /// <summary>Null if the creator deleted their account (the challenge stays for the other participants).</summary>
    public Guid? CreatorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public ChallengeRule Rule { get; private set; }
    public decimal TargetValue { get; private set; }
    public string? Unit { get; private set; }

    /// <summary>Random code shared out of band to join (8 characters, no ambiguous letters).</summary>
    public string JoinCode { get; private set; } = string.Empty;

    public IReadOnlyList<ChallengeParticipant> Participants => _participants;

    public int Days => EndDate.DayNumber - StartDate.DayNumber + 1;

    public static Challenge Create(
        Guid creatorId,
        string name,
        string? description,
        DateOnly startDate,
        DateOnly endDate,
        ChallengeRule rule,
        decimal? targetValue,
        string? unit,
        DateOnly today)
    {
        var challenge = new Challenge
        {
            CreatorId = creatorId,
            Name = Guard.Required(name, NameMaxLength, "nombre"),
            Description = Guard.Optional(description, DescriptionMaxLength, "descripción"),
            JoinCode = NewCode(),
        };
        var rules = ValidRules(startDate, endDate, rule, targetValue, unit);
        EnsureNotInPast(rules, today);
        challenge.Apply(rules);
        return challenge;
    }

    public ChallengePhase PhaseOn(DateOnly today) =>
        today < StartDate ? ChallengePhase.Upcoming
        : today > EndDate ? ChallengePhase.Finished
        : ChallengePhase.Active;

    /// <summary>
    /// Rules (dates, kind, target, unit) can change only before the start and while nobody but the creator has
    /// joined: nobody's challenge changes under their feet.
    /// </summary>
    public bool RulesEditableOn(DateOnly today) =>
        PhaseOn(today) == ChallengePhase.Upcoming && _participants.All(p => p.UserId == CreatorId);

    /// <summary>
    /// Edits a challenge (the caller checks it is the creator). Name and description can change until it finishes;
    /// rules only while <see cref="RulesEditableOn"/>. <paramref name="creatorHabit"/> is the creator's linked habit,
    /// if they take part, re-checked against the new rules.
    /// </summary>
    public void Update(string name, string? description, DateOnly startDate, DateOnly endDate, ChallengeRule rule,
        decimal? targetValue, string? unit, DateOnly today, Habit? creatorHabit)
    {
        if (PhaseOn(today) == ChallengePhase.Finished)
        {
            throw new DomainException("challenge.finished", "El reto ya ha terminado.");
        }

        var newName = Guard.Required(name, NameMaxLength, "nombre");
        var newDescription = Guard.Optional(description, DescriptionMaxLength, "descripción");
        var rules = ValidRules(startDate, endDate, rule, targetValue, unit);
        if (rules != CurrentRules)
        {
            if (!RulesEditableOn(today))
            {
                throw new DomainException("challenge.rules_locked",
                    "Las reglas solo se pueden cambiar antes de que empiece el reto y mientras nadie más se haya unido.");
            }

            EnsureNotInPast(rules, today);
            if (creatorHabit is not null)
            {
                EnsureHabitFits(creatorHabit, rules);
            }

            Apply(rules);
        }

        Name = newName;
        Description = newDescription;
    }

    private ChallengeRules CurrentRules => new(StartDate, EndDate, Rule, TargetValue, Unit);

    /// <summary>Validates and normalizes rules (daily challenges target every day; only quantity ones have a unit).</summary>
    private static ChallengeRules ValidRules(DateOnly startDate, DateOnly endDate, ChallengeRule rule,
        decimal? targetValue, string? unit)
    {
        if (endDate < startDate || endDate.DayNumber - startDate.DayNumber + 1 > MaxDays)
        {
            throw new DomainException("challenge.invalid_dates", $"El reto debe durar entre 1 y {MaxDays} días.");
        }

        var days = endDate.DayNumber - startDate.DayNumber + 1;
        var target = rule == ChallengeRule.DailyCompletion
            ? days
            : targetValue is > 0 and <= 1_000_000 && decimal.Round(targetValue.Value, 2) == targetValue
                ? targetValue.Value
                : throw new DomainException("challenge.invalid_target", "Indica una meta mayor que 0.");
        if (rule == ChallengeRule.CompletionCount && target > days)
        {
            throw new DomainException("challenge.unreachable_target",
                "La meta no puede superar el número de días del reto.");
        }

        var validUnit = rule == ChallengeRule.QuantityTotal ? Guard.Required(unit, Habit.UnitMaxLength, "unidad") : null;
        return new ChallengeRules(startDate, endDate, rule, target, validUnit);
    }

    private static void EnsureNotInPast(ChallengeRules rules, DateOnly today)
    {
        if (rules.StartDate < today)
        {
            throw new DomainException("challenge.start_in_past", "El reto no puede empezar en el pasado.");
        }
    }

    private void Apply(ChallengeRules rules) =>
        (StartDate, EndDate, Rule, TargetValue, Unit) =
        (rules.StartDate, rules.EndDate, rules.Rule, rules.TargetValue, rules.Unit);

    /// <summary>Quantity challenges need a quantity habit in the same unit.</summary>
    private static void EnsureHabitFits(Habit habit, ChallengeRules rules)
    {
        if (rules.Rule == ChallengeRule.QuantityTotal &&
            (habit.Type != HabitType.Quantity ||
             !string.Equals(habit.Unit?.Trim(), rules.Unit, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("challenge.unit_mismatch",
                $"Este reto se mide en «{rules.Unit}»: elige un hábito cuantificable con esa unidad.");
        }
    }

    private readonly record struct ChallengeRules(
        DateOnly StartDate, DateOnly EndDate, ChallengeRule Rule, decimal TargetValue, string? Unit);

    /// <summary>
    /// Validates dates and rules before accepting a participant: the challenge must not be over, the habit must be
    /// the joiner's own active habit and fit the rule (quantity challenges need a quantity habit in the same unit).
    /// </summary>
    public ChallengeParticipant Join(Guid userId, Habit habit, DateOnly today, DateTimeOffset now)
    {
        if (PhaseOn(today) == ChallengePhase.Finished)
        {
            throw new DomainException("challenge.finished", "El reto ya ha terminado.");
        }

        if (_participants.Any(p => p.UserId == userId))
        {
            throw new DomainException("challenge.already_joined", "Ya participas (o participaste) en este reto.");
        }

        if (_participants.Count(p => p.LeftOn is null) >= MaxParticipants)
        {
            throw new DomainException("challenge.full", $"El reto ya tiene {MaxParticipants} participantes.");
        }

        if (habit.UserId != userId || habit.IsArchived)
        {
            throw new DomainException("challenge.invalid_habit", "Elige uno de tus hábitos activos.");
        }

        EnsureHabitFits(habit, CurrentRules);

        var participant = new ChallengeParticipant(Id, userId, habit.Id, now);
        _participants.Add(participant);
        return participant;
    }

    public void Leave(Guid userId, DateOnly today)
    {
        var participant = _participants.SingleOrDefault(p => p.UserId == userId && p.LeftOn is null)
                          ?? throw new DomainException("challenge.not_participant", "No participas en este reto.");
        if (PhaseOn(today) == ChallengePhase.Finished)
        {
            throw new DomainException("challenge.finished", "El reto ya ha terminado: tu resultado es definitivo.");
        }

        // Progress is frozen at the day before leaving (or nothing if the challenge had not started).
        participant.LeaveOn(today);
    }

    /// <summary>Result of a participant given their progress value and the viewer's "today".</summary>
    public ChallengeResult ResultFor(ChallengeParticipant participant, decimal progress, DateOnly today) =>
        participant.LeftOn is not null ? ChallengeResult.Abandoned
        : progress >= TargetValue ? ChallengeResult.Completed
        : PhaseOn(today) == ChallengePhase.Finished ? ChallengeResult.NotCompleted
        : ChallengeResult.InProgress;

    /// <summary>Last date whose logs count for this participant.</summary>
    public DateOnly CountsUntil(ChallengeParticipant participant) =>
        participant.LeftOn is { } left && left.AddDays(-1) < EndDate ? left.AddDays(-1) : EndDate;

    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(8, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
    }
}

public sealed class ChallengeParticipant
{
    private ChallengeParticipant()
    {
    }

    internal ChallengeParticipant(Guid challengeId, Guid userId, Guid habitId, DateTimeOffset joinedAt)
    {
        ChallengeId = challengeId;
        UserId = userId;
        HabitId = habitId;
        JoinedAt = joinedAt;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid ChallengeId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>The participant's own habit whose logs make up their progress. Never exposed to other users. Null if the
    /// habit was later deleted (its logs are gone, so nothing more counts).</summary>
    public Guid? HabitId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
    public DateOnly? LeftOn { get; private set; }

    internal void LeaveOn(DateOnly date) => LeftOn = date;
}
