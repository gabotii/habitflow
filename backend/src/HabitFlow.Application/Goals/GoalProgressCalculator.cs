using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Goals;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Goals;

/// <summary>
/// The single implementation of goal progress (docs/ARCHITECTURE.md §3.7); the client only displays its output.
/// Habit-based goals only count days inside [start, min(deadline, today)].
/// </summary>
public sealed class GoalProgressCalculator(IAppDbContext db, HabitTracker tracker, IUserClock clock)
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> CurrentValuesAsync(
        IReadOnlyCollection<Goal> goals,
        CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var result = new Dictionary<Guid, decimal>();

        var manualIds = goals.Where(g => g.Metric == GoalMetric.Manual).Select(g => g.Id).ToList();
        if (manualIds.Count > 0)
        {
            var sums = await db.GoalEntries.AsNoTracking()
                .Where(e => manualIds.Contains(e.GoalId))
                .GroupBy(e => e.GoalId)
                .Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
            foreach (var id in manualIds)
            {
                result[id] = sums.GetValueOrDefault(id);
            }
        }

        var habitGoals = goals.Where(g => g.IsHabitBased).ToList();
        if (habitGoals.Count > 0)
        {
            var timelines = await LoadTimelinesAsync(habitGoals, ct);
            foreach (var goal in habitGoals)
            {
                result[goal.Id] = Compute(goal, timelines, goal.WindowEnd(today));
            }
        }

        return result;
    }

    /// <summary>Cumulative progress per day from the start to the end of the window (for the history chart).</summary>
    public async Task<IReadOnlyList<GoalHistoryPointDto>> HistoryAsync(Goal goal, CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var end = goal.WindowEnd(today);
        if (end < goal.StartDate)
        {
            return [];
        }

        // At most one year of daily points; longer goals show their most recent year.
        var from = end.DayNumber - goal.StartDate.DayNumber > 365 ? end.AddDays(-365) : goal.StartDate;
        var daily = new Dictionary<DateOnly, decimal>();
        IReadOnlyList<GoalHistoryPointDto>? streakSeries = null;

        if (goal.Metric == GoalMetric.Manual)
        {
            var entries = await db.GoalEntries.AsNoTracking()
                .Where(e => e.GoalId == goal.Id)
                .GroupBy(e => e.LocalDate)
                .Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) })
                .ToListAsync(ct);
            foreach (var entry in entries)
            {
                daily[entry.Key] = entry.Sum;
            }
        }
        else
        {
            var timelines = await LoadTimelinesAsync([goal], ct);
            var linked = goal.Habits.Select(h => timelines.GetValueOrDefault(h.HabitId)).OfType<TrackedHabit>().ToList();
            if (goal.Metric == GoalMetric.HabitStreak)
            {
                streakSeries = linked.FirstOrDefault()?.Timeline.StreakSeries(from, end)
                    .Select(p => new GoalHistoryPointDto(p.Date, p.Length)).ToList() ?? [];
            }
            else
            {
                foreach (var log in linked.SelectMany(t => t.Timeline.Logs))
                {
                    var value = goal.Metric == GoalMetric.HabitQuantity ? log.Quantity ?? 0 : log.Completed ? 1 : 0;
                    daily[log.Date] = daily.GetValueOrDefault(log.Date) + value;
                }
            }
        }

        if (streakSeries is not null)
        {
            return streakSeries;
        }

        var cumulative = daily.Where(d => d.Key >= goal.StartDate && d.Key < from).Sum(d => d.Value);
        var points = new List<GoalHistoryPointDto>();
        for (var day = from; day <= end; day = day.AddDays(1))
        {
            cumulative += daily.GetValueOrDefault(day);
            points.Add(new GoalHistoryPointDto(day, cumulative));
        }

        return points;
    }

    private async Task<Dictionary<Guid, TrackedHabit>> LoadTimelinesAsync(IReadOnlyCollection<Goal> goals, CancellationToken ct)
    {
        var habitIds = goals.SelectMany(g => g.Habits.Select(h => h.HabitId)).Distinct().ToList();
        var tracked = await tracker.LoadAsync(q => q.Where(h => habitIds.Contains(h.Id)), null, ct);
        return tracked.ToDictionary(t => t.Habit.Id);
    }

    private static decimal Compute(Goal goal, IReadOnlyDictionary<Guid, TrackedHabit> timelines, DateOnly end)
    {
        var linked = goal.Habits.Select(h => timelines.GetValueOrDefault(h.HabitId)).OfType<TrackedHabit>().ToList();
        if (end < goal.StartDate || linked.Count == 0)
        {
            return 0;
        }

        bool InWindow(DateOnly d) => d >= goal.StartDate && d <= end;
        return goal.Metric switch
        {
            GoalMetric.HabitCompletions => linked.Sum(t => t.Timeline.Logs.Count(l => l.Completed && InWindow(l.Date))),
            GoalMetric.HabitQuantity => linked.Sum(t => t.Timeline.Logs.Where(l => InWindow(l.Date)).Sum(l => l.Quantity ?? 0)),
            GoalMetric.HabitStreak => linked[0].Timeline.LongestRun(goal.StartDate, end),
            _ => 0,
        };
    }
}
