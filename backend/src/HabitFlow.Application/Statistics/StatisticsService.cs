using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Statistics;

/// <summary>Day-based scheduled occurrences on one day (weekly quotas are reported per week).</summary>
public sealed record DailyPointDto(DateOnly Date, int Scheduled, int Completed, double? Rate);

/// <summary>Combined compliance (day-based + weekly quotas) for a week or month.</summary>
public sealed record PeriodPointDto(DateOnly Start, DateOnly End, int Expected, int Completed, double? Rate);

public sealed record WeekdayPointDto(DayOfWeek Weekday, int Scheduled, int Completed, double? Rate, int Completions);

public sealed record HabitStatsRowDto(
    Guid HabitId,
    string Name,
    string Icon,
    HabitColor Color,
    HabitType Type,
    string? Unit,
    bool IsArchived,
    ComplianceDto Compliance,
    StreakBriefDto Streak);

public sealed record GoalStatsDto(int Active, int Paused, int Completed, int Cancelled, int CompletedInRange);

public sealed record ComparisonDto(DateOnly PreviousFrom, DateOnly PreviousTo, ComplianceDto Previous, double? CombinedRateDelta);

public sealed record StatisticsDto(
    DateOnly From,
    DateOnly To,
    ComplianceDto Summary,
    ComparisonDto Comparison,
    IReadOnlyList<DailyPointDto> Daily,
    IReadOnlyList<PeriodPointDto> Weekly,
    IReadOnlyList<PeriodPointDto> Monthly,
    IReadOnlyList<WeekdayPointDto> Weekdays,
    IReadOnlyList<HabitStatsRowDto> Habits,
    GoalStatsDto Goals);

public sealed record HabitStatisticsDto(
    DateOnly From,
    DateOnly To,
    ComplianceDto Compliance,
    StreakDto Streak,
    IReadOnlyList<StreakPoint> StreakSeries,
    IReadOnlyList<WeekdayPointDto> Weekdays,
    IReadOnlyList<HabitQuantityPointDto> Quantities,
    IReadOnlyList<PeriodPointDto> Weekly);

public sealed record HabitQuantityPointDto(DateOnly Date, decimal? Quantity, decimal? Target, bool Completed);

/// <summary>
/// Aggregates computed on the server from the source data (docs/ARCHITECTURE.md §3.6); the client never receives the
/// raw history. Default range: the last 30 days.
/// </summary>
public sealed class StatisticsService(IAppDbContext db, IUserClock clock, HabitTracker tracker)
{
    public async Task<StatisticsDto> OverviewAsync(DateRangeQuery range, CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var (from, to) = DateRanges.Resolve(range, today, 30);
        var habits = await tracker.LoadAsync(null, null, ct);

        var summary = Sum(habits, t => t.Timeline.Compliance(from, to));
        var length = to.DayNumber - from.DayNumber + 1;
        var (previousFrom, previousTo) = (from.AddDays(-length), from.AddDays(-1));
        var previous = Sum(habits, t => t.Timeline.Compliance(previousFrom, previousTo));

        var goals = await db.Goals.AsNoTracking()
            .GroupBy(g => g.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var zone = await clock.GetTimeZoneAsync(ct);
        var (fromInstant, toInstant) = (ZonedTime.StartOfDay(from, zone), ZonedTime.StartOfDay(to.AddDays(1), zone));
        var completedInRange = await db.Goals.CountAsync(
            g => g.Status == GoalStatus.Completed && g.CompletedAt >= fromInstant && g.CompletedAt < toInstant, ct);

        return new StatisticsDto(
            from,
            to,
            summary.ToDto(),
            new ComparisonDto(previousFrom, previousTo, previous.ToDto(),
                summary.CombinedRate is { } now && previous.CombinedRate is { } before
                    ? Math.Round((now - before) * 100, 1)
                    : null),
            Daily(habits, from, to),
            Weeks(from, to).Select(w => Point(habits, w.Start, w.End)).ToList(),
            Months(from, to).Select(m => Point(habits, m.Start, m.End)).ToList(),
            WeekdayBreakdown(habits.Select(t => t.Timeline), from, to),
            habits
                .Where(t => !t.Habit.IsArchived || t.Timeline.Logs.Any(l => l.Date >= from && l.Date <= to))
                .Select(t => new HabitStatsRowDto(t.Habit.Id, t.Habit.Name, t.Habit.Icon, t.Habit.Color, t.Habit.Type,
                    t.Habit.Unit, t.Habit.IsArchived, t.Timeline.Compliance(from, to).ToDto(), t.Timeline.Brief()))
                .OrderByDescending(r => r.Compliance.CombinedRate ?? -1).ThenBy(r => r.Name)
                .ToList(),
            new GoalStatsDto(
                goals.GetValueOrDefault(GoalStatus.Active),
                goals.GetValueOrDefault(GoalStatus.Paused),
                goals.GetValueOrDefault(GoalStatus.Completed),
                goals.GetValueOrDefault(GoalStatus.Cancelled),
                completedInRange));
    }

    public async Task<HabitStatisticsDto> HabitAsync(Guid habitId, DateRangeQuery range, CancellationToken ct)
    {
        var tracked = await tracker.LoadOneAsync(habitId, null, ct);
        var timeline = tracked.Timeline;
        var (from, to) = DateRanges.Resolve(range, tracked.Today, 30);

        var quantities = tracked.Habit.Type == HabitType.Quantity
            ? DateRanges.Days(from, to < tracked.Today ? to : tracked.Today)
                .Select(d =>
                {
                    var log = timeline.LogOn(d);
                    return new HabitQuantityPointDto(d, log?.Quantity, timeline.SpecOn(d).TargetValue, log?.Completed ?? false);
                })
                .ToList()
            : [];

        return new HabitStatisticsDto(
            from,
            to,
            timeline.Compliance(from, to).ToDto(),
            timeline.ToStreakDto(),
            timeline.StreakSeries(from, to),
            WeekdayBreakdown([timeline], from, to),
            quantities,
            Weeks(from, to).Select(w => Point([tracked], w.Start, w.End)).ToList());
    }

    internal static IReadOnlyList<DailyPointDto> Daily(IReadOnlyList<TrackedHabit> habits, DateOnly from, DateOnly to)
    {
        var scheduled = new Dictionary<DateOnly, int>();
        var completed = new Dictionary<DateOnly, int>();
        foreach (var period in habits.SelectMany(t => t.Timeline.Periods))
        {
            if (period.Unit != PeriodUnit.Day || period.Start < from || period.Start > to)
            {
                continue;
            }

            scheduled[period.Start] = scheduled.GetValueOrDefault(period.Start) + 1;
            completed[period.Start] = completed.GetValueOrDefault(period.Start) + period.Completed;
        }

        return DateRanges.Days(from, to)
            .Select(d => new DailyPointDto(d, scheduled.GetValueOrDefault(d), completed.GetValueOrDefault(d),
                Percent.Of(completed.GetValueOrDefault(d), scheduled.GetValueOrDefault(d))))
            .ToList();
    }

    internal static ComplianceSummary Sum(IEnumerable<TrackedHabit> habits, Func<TrackedHabit, ComplianceSummary> selector) =>
        habits.Select(selector).Aggregate(ComplianceSummary.Empty, (a, b) => a + b);

    private static PeriodPointDto Point(IReadOnlyList<TrackedHabit> habits, DateOnly start, DateOnly end)
    {
        var c = Sum(habits, t => t.Timeline.Compliance(start, end));
        return new PeriodPointDto(start, end, c.ExpectedOccurrences, c.CompletedOccurrences, Percent.Of(c.CombinedRate));
    }

    private static IReadOnlyList<WeekdayPointDto> WeekdayBreakdown(IEnumerable<HabitTimeline> timelines, DateOnly from, DateOnly to)
    {
        var scheduled = new int[7];
        var completed = new int[7];
        var completions = new int[7];
        foreach (var timeline in timelines)
        {
            foreach (var period in timeline.Periods)
            {
                if (period.Unit != PeriodUnit.Day || period.Start < from || period.Start > to ||
                    period.Outcome == PeriodOutcome.Open)
                {
                    continue;
                }

                var index = Weekdays.IsoIndex(period.Start.DayOfWeek);
                scheduled[index]++;
                completed[index] += period.Completed;
            }

            foreach (var log in timeline.Logs.Where(l => l.Completed && l.Date >= from && l.Date <= to))
            {
                completions[Weekdays.IsoIndex(log.Date.DayOfWeek)]++;
            }
        }

        return Enumerable.Range(0, 7)
            .Select(i => new WeekdayPointDto((DayOfWeek)((i + 1) % 7), scheduled[i], completed[i],
                Percent.Of(completed[i], scheduled[i]), completions[i]))
            .ToList();
    }

    private static IEnumerable<(DateOnly Start, DateOnly End)> Weeks(DateOnly from, DateOnly to)
    {
        for (var start = IsoWeek.StartOf(from); start <= to; start = start.AddDays(7))
        {
            yield return (start, start.AddDays(6));
        }
    }

    private static IEnumerable<(DateOnly Start, DateOnly End)> Months(DateOnly from, DateOnly to)
    {
        for (var start = new DateOnly(from.Year, from.Month, 1); start <= to; start = start.AddMonths(1))
        {
            yield return (start, start.AddMonths(1).AddDays(-1));
        }
    }
}
