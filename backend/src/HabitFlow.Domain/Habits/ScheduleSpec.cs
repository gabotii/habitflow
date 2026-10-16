using HabitFlow.Domain.Common;
using HabitFlow.Domain.Time;

namespace HabitFlow.Domain.Habits;

public enum PeriodUnit
{
    Day,
    Week,
}

/// <summary>
/// What a habit expects: how often (frequency) and, for quantity habits, how much per day (daily target).
/// Parameters that do not apply to <see cref="Kind"/> are always null, so value equality is meaningful.
/// </summary>
public sealed record ScheduleSpec
{
    public const decimal MaxTarget = 1_000_000m;

    private ScheduleSpec(ScheduleKind kind, int? weekdaysMask, int? timesPerWeek, int? intervalDays, decimal? targetValue)
    {
        Kind = kind;
        WeekdaysMask = weekdaysMask;
        TimesPerWeek = timesPerWeek;
        IntervalDays = intervalDays;
        TargetValue = targetValue;
    }

    public ScheduleKind Kind { get; }
    public int? WeekdaysMask { get; }
    public int? TimesPerWeek { get; }
    public int? IntervalDays { get; }
    public decimal? TargetValue { get; private init; }

    public PeriodUnit Unit => Kind == ScheduleKind.TimesPerWeek ? PeriodUnit.Week : PeriodUnit.Day;

    /// <summary>Builds a validated schedule.</summary>
    public static ScheduleSpec Create(
        HabitType habitType,
        ScheduleKind kind,
        int? weekdaysMask = null,
        int? timesPerWeek = null,
        int? intervalDays = null,
        decimal? targetValue = null)
    {
        var spec = kind switch
        {
            ScheduleKind.Daily => new ScheduleSpec(kind, null, null, null, null),
            ScheduleKind.Weekdays when weekdaysMask is { } mask && Weekdays.IsValid(mask) =>
                new ScheduleSpec(kind, mask, null, null, null),
            ScheduleKind.Weekdays => throw new DomainException("schedule.invalid_weekdays",
                "Elige al menos un día de la semana."),
            ScheduleKind.TimesPerWeek when timesPerWeek is >= 1 and <= 7 =>
                new ScheduleSpec(kind, null, timesPerWeek, null, null),
            ScheduleKind.TimesPerWeek => throw new DomainException("schedule.invalid_times_per_week",
                "Las veces por semana deben estar entre 1 y 7."),
            ScheduleKind.EveryNDays when intervalDays is >= 2 and <= 365 =>
                new ScheduleSpec(kind, null, null, intervalDays, null),
            ScheduleKind.EveryNDays => throw new DomainException("schedule.invalid_interval",
                "El intervalo debe estar entre 2 y 365 días."),
            _ => throw new DomainException("schedule.invalid_kind", "Tipo de frecuencia no válido."),
        };

        if (targetValue is null)
        {
            return spec;
        }

        if (habitType == HabitType.Binary)
        {
            throw new DomainException("schedule.target_not_allowed",
                "Los hábitos de sí/no no tienen meta diaria numérica.");
        }

        if (targetValue <= 0 || targetValue > MaxTarget || decimal.Round(targetValue.Value, 2) != targetValue)
        {
            throw new DomainException("schedule.invalid_target",
                "La meta diaria debe ser mayor que 0, con 2 decimales como máximo.");
        }

        return spec with { TargetValue = targetValue };
    }

    /// <summary>Rehydrates a stored version without re-validating (the database enforces the same rules).</summary>
    public static ScheduleSpec FromStorage(ScheduleKind kind, int? weekdaysMask, int? timesPerWeek, int? intervalDays,
        decimal? targetValue) => new(kind, weekdaysMask, timesPerWeek, intervalDays, targetValue);
}
