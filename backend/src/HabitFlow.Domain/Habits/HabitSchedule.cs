namespace HabitFlow.Domain.Habits;

/// <summary>
/// One version of a habit's rules, in force from <see cref="EffectiveFrom"/> until the next version. Past days are
/// always evaluated with the version that was in force on that day, so editing a habit never rewrites history.
/// </summary>
public sealed class HabitSchedule
{
    private HabitSchedule()
    {
    }

    internal HabitSchedule(Guid habitId, DateOnly effectiveFrom, ScheduleSpec spec, DateTimeOffset createdAt)
    {
        HabitId = habitId;
        EffectiveFrom = effectiveFrom;
        CreatedAt = createdAt;
        Apply(spec);
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid HabitId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public ScheduleKind Kind { get; private set; }
    public int? WeekdaysMask { get; private set; }
    public int? TimesPerWeek { get; private set; }
    public int? IntervalDays { get; private set; }
    public decimal? TargetValue { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public ScheduleSpec Spec => ScheduleSpec.FromStorage(Kind, WeekdaysMask, TimesPerWeek, IntervalDays, TargetValue);

    internal void Apply(ScheduleSpec spec)
    {
        Kind = spec.Kind;
        WeekdaysMask = spec.WeekdaysMask;
        TimesPerWeek = spec.TimesPerWeek;
        IntervalDays = spec.IntervalDays;
        TargetValue = spec.TargetValue;
    }
}
