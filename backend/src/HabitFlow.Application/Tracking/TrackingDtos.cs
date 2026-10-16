using HabitFlow.Application.Common;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;

namespace HabitFlow.Application.Tracking;

public sealed record ScheduleDto(
    ScheduleKind Kind,
    IReadOnlyList<DayOfWeek>? Weekdays,
    int? TimesPerWeek,
    int? IntervalDays,
    decimal? TargetValue,
    PeriodUnit Unit);

public sealed record StreakRunDto(int Length, DateOnly Start, DateOnly End);

/// <summary>Compact streak info for cards and lists.</summary>
public sealed record StreakBriefDto(PeriodUnit Unit, int Current, int Best);

public sealed record WeekProgressDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int Completed,
    int Required,
    int DaysLeft,
    bool Achieved);

/// <summary>
/// Separate indicators, never mixed into one number (docs/ARCHITECTURE.md §3.6). Percentages are null when there
/// is nothing to measure yet.
/// </summary>
public sealed record ComplianceDto(
    double? ScheduledDayRate,
    double? WeeklyRate,
    double? CombinedRate,
    double? LogCompletionRate,
    int ScheduledDays,
    int CompletedScheduledDays,
    int Weeks,
    int WeeksAchieved,
    int ExpectedOccurrences,
    int CompletedOccurrences,
    int ExtraCompletions,
    int LogsRecorded,
    int LogsCompleted,
    decimal TotalQuantity);

public sealed record StreakDto(
    PeriodUnit Unit,
    int Current,
    DateOnly? CurrentStart,
    StreakRunDto? Best,
    DateOnly? LastCompletedDate,
    WeekProgressDto? CurrentWeek,
    ComplianceDto AllTime);

/// <summary>A habit's state on one day, as shown on cards, the calendar and after logging.</summary>
public sealed record HabitDayDto(
    DateOnly Date,
    HabitDayStatus Status,
    bool Scheduled,
    decimal? Quantity,
    bool Completed,
    decimal? Target,
    string? Note);

public static class TrackingMapping
{
    public static ScheduleDto ToDto(this ScheduleSpec spec) => new(
        spec.Kind,
        spec.WeekdaysMask is { } mask ? Weekdays.ToDays(mask) : null,
        spec.TimesPerWeek,
        spec.IntervalDays,
        spec.TargetValue,
        spec.Unit);

    public static ComplianceDto ToDto(this ComplianceSummary c) => new(
        Percent.Of(c.ScheduledDayRate),
        Percent.Of(c.WeeklyRate),
        Percent.Of(c.CombinedRate),
        Percent.Of(c.LogCompletionRate),
        c.ScheduledDays,
        c.CompletedScheduledDays,
        c.Weeks,
        c.WeeksAchieved,
        c.ExpectedOccurrences,
        c.CompletedOccurrences,
        c.ExtraCompletions,
        c.LogsRecorded,
        c.LogsCompleted,
        c.TotalQuantity);

    public static WeekProgressDto? ToDto(this WeekProgress? w) =>
        w is null ? null : new WeekProgressDto(w.WeekStart, w.WeekEnd, w.Completed, w.Required, w.DaysLeft, w.Achieved);

    public static StreakBriefDto Brief(this HabitTimeline timeline)
    {
        var streak = timeline.Streak();
        return new StreakBriefDto(streak.Unit, streak.Current, streak.Best?.Length ?? 0);
    }

    public static StreakDto ToStreakDto(this HabitTimeline timeline)
    {
        var streak = timeline.Streak();
        var allTime = timeline.Compliance(timeline.StartDate, timeline.Today);
        return new StreakDto(
            streak.Unit,
            streak.Current,
            streak.CurrentStart,
            streak.Best is { } best ? new StreakRunDto(best.Length, best.Start, best.End) : null,
            streak.LastCompletedDate,
            timeline.CurrentWeek().ToDto(),
            allTime.ToDto());
    }

    public static HabitDayDto Day(this HabitTimeline timeline, DateOnly date, string? note = null)
    {
        var log = timeline.LogOn(date);
        return new HabitDayDto(
            date,
            timeline.StatusOn(date),
            timeline.IsScheduled(date),
            log?.Quantity,
            log?.Completed ?? false,
            timeline.Type == HabitType.Quantity ? timeline.SpecOn(date).TargetValue : null,
            note);
    }
}
