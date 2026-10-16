namespace HabitFlow.Domain.Habits.Tracking;

/// <summary>The stored state of one day: the daily total (quantity habits) and whether it met that day's rules.</summary>
public readonly record struct DayLog(DateOnly Date, decimal? Quantity, bool Completed);

public sealed record ScheduleVersion(DateOnly EffectiveFrom, ScheduleSpec Spec);

public sealed record PauseSpan(DateOnly Start, DateOnly? End)
{
    public bool Covers(DateOnly date) => date >= Start && (End is null || date <= End);
}

/// <summary>What a habit looks like on one calendar day (see docs/ARCHITECTURE.md §3.3).</summary>
public enum HabitDayStatus
{
    /// <summary>Before the start date, paused, or not one of the scheduled days.</summary>
    NotScheduled,

    /// <summary>A scheduled (or weekly-eligible) day after today.</summary>
    Future,

    /// <summary>Today, scheduled and not completed yet. Today is never a failure until it ends.</summary>
    Pending,

    Completed,

    /// <summary>Some quantity logged but below the daily target.</summary>
    Partial,

    /// <summary>A past scheduled day that was not completed.</summary>
    Missed,

    /// <summary>A day of a weekly-quota habit without a completion: neither required nor failed on its own.</summary>
    Optional,
}

public enum PeriodOutcome
{
    Success,
    Missed,

    /// <summary>Still running: today's pending day, or the current week before its quota is met.</summary>
    Open,
}

/// <summary>
/// The unit streaks are counted in: one scheduled day for day-based frequencies, or one (possibly partial) ISO week
/// segment for weekly quotas. For days, <see cref="Required"/> is 1.
/// </summary>
public sealed record Period(
    PeriodUnit Unit,
    DateOnly Start,
    DateOnly End,
    PeriodOutcome Outcome,
    int Completed,
    int Required);

public sealed record StreakRun(PeriodUnit Unit, int Length, DateOnly Start, DateOnly End);

public sealed record StreakSummary(
    PeriodUnit Unit,
    int Current,
    DateOnly? CurrentStart,
    StreakRun? Best,
    DateOnly? LastCompletedDate);

public sealed record WeekProgress(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int Completed,
    int Required,
    int DaysLeft,
    bool Achieved);

public sealed record StreakPoint(DateOnly Date, int Length, PeriodUnit Unit);

/// <summary>
/// Raw counts for a date range. The rates are deliberately separate indicators (docs/ARCHITECTURE.md §3.6):
/// scheduled-day compliance, weekly-quota compliance, the combined rate and log-level completion.
/// </summary>
public sealed record ComplianceSummary(
    int ScheduledDays,
    int CompletedScheduledDays,
    int Weeks,
    int WeeksAchieved,
    int WeeklyRequired,
    int WeeklyCompletedCapped,
    int ExtraCompletions,
    int LogsRecorded,
    int LogsCompleted,
    decimal TotalQuantity)
{
    public static ComplianceSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public int ExpectedOccurrences => ScheduledDays + WeeklyRequired;
    public int CompletedOccurrences => CompletedScheduledDays + WeeklyCompletedCapped;

    public double? ScheduledDayRate => Ratio(CompletedScheduledDays, ScheduledDays);
    public double? WeeklyRate => Ratio(WeeksAchieved, Weeks);
    public double? CombinedRate => Ratio(CompletedOccurrences, ExpectedOccurrences);
    public double? LogCompletionRate => Ratio(LogsCompleted, LogsRecorded);

    public static ComplianceSummary operator +(ComplianceSummary a, ComplianceSummary b) => new(
        a.ScheduledDays + b.ScheduledDays,
        a.CompletedScheduledDays + b.CompletedScheduledDays,
        a.Weeks + b.Weeks,
        a.WeeksAchieved + b.WeeksAchieved,
        a.WeeklyRequired + b.WeeklyRequired,
        a.WeeklyCompletedCapped + b.WeeklyCompletedCapped,
        a.ExtraCompletions + b.ExtraCompletions,
        a.LogsRecorded + b.LogsRecorded,
        a.LogsCompleted + b.LogsCompleted,
        a.TotalQuantity + b.TotalQuantity);

    private static double? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;
}
