using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Tracking;

/// <summary>A habit with its evaluated timeline. <see cref="Today"/> is the local date in the habit's effective zone.</summary>
public sealed record TrackedHabit(Habit Habit, HabitTimeline Timeline, DateOnly Today, string TimeZoneId);

/// <summary>
/// Loads habits (with schedule versions and pauses) plus their daily logs in two queries and builds one
/// <see cref="HabitTimeline"/> per habit. Derived values are always computed from this source data (ADR-05).
/// </summary>
public sealed class HabitTracker(IAppDbContext db, IUserClock clock)
{
    /// <summary>"Today" for a habit: the current date in its own zone, or the user's zone when it has none.</summary>
    public async Task<(DateOnly Today, string ZoneId)> TodayForAsync(Habit habit, CancellationToken ct)
    {
        if (TimeZoneIds.TryResolve(habit.TimeZoneId, out var own))
        {
            return (ZonedTime.LocalDate(clock.UtcNow, own), habit.TimeZoneId!);
        }

        var zone = await clock.GetTimeZoneAsync(ct);
        return (ZonedTime.LocalDate(clock.UtcNow, zone), zone.Id);
    }

    public async Task<TrackedHabit> LoadOneAsync(Guid habitId, DateOnly? logsFrom, CancellationToken ct) =>
        (await LoadAsync(q => q.Where(h => h.Id == habitId), logsFrom, ct)).SingleOrDefault()
        ?? throw new NotFoundException("Hábito");

    /// <param name="filter">Restricts which habits are loaded (always within the caller's own habits).</param>
    /// <param name="logsFrom">Earliest log date needed; null loads the full history (required for streaks).</param>
    public async Task<IReadOnlyList<TrackedHabit>> LoadAsync(
        Func<IQueryable<Habit>, IQueryable<Habit>>? filter,
        DateOnly? logsFrom,
        CancellationToken ct)
    {
        IQueryable<Habit> query = db.Habits.AsNoTracking().Include(h => h.Schedules).Include(h => h.Pauses);
        if (filter is not null)
        {
            query = filter(query);
        }

        var habits = await query.ToListAsync(ct);
        if (habits.Count == 0)
        {
            return [];
        }

        var ids = habits.Select(h => h.Id).ToList();
        var logQuery = db.HabitLogs.AsNoTracking().Where(l => ids.Contains(l.HabitId));
        if (logsFrom is { } from)
        {
            logQuery = logQuery.Where(l => l.LocalDate >= from);
        }

        var logs = (await logQuery
                .Select(l => new { l.HabitId, l.LocalDate, l.Quantity, l.IsCompleted })
                .ToListAsync(ct))
            .ToLookup(l => l.HabitId, l => new DayLog(l.LocalDate, l.Quantity, l.IsCompleted));

        var userZone = await clock.GetTimeZoneAsync(ct);
        var now = clock.UtcNow;
        return habits.Select(habit =>
        {
            var zone = TimeZoneIds.TryResolve(habit.TimeZoneId, out var own) ? own : userZone;
            var today = ZonedTime.LocalDate(now, zone);
            return new TrackedHabit(habit, HabitTimeline.For(habit, logs[habit.Id], today), today,
                habit.TimeZoneId ?? userZone.Id);
        }).ToList();
    }
}
