using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Application.Goals;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Statistics;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Dashboard;

/// <summary>Today's day-based habits only (weekly quotas are shown in their own list with x/N progress).</summary>
public sealed record TodaySummaryDto(int Scheduled, int Completed, double? Percent);

public sealed record StreakHighlightDto(Guid HabitId, string Name, string Icon, HabitColor Color, int Length, PeriodUnit Unit);

public sealed record UpcomingReminderDto(Guid Id, string Title, DateTimeOffset NextFireAt, string? HabitName, string? GoalName);

public sealed record ActivityDto(
    Guid HabitId,
    string HabitName,
    string Icon,
    HabitColor Color,
    string? Unit,
    DateOnly Date,
    LogChangeAction Action,
    bool? Completed,
    decimal? Quantity,
    DateTimeOffset At);

public sealed record WeekSummaryDto(DateOnly From, DateOnly To, ComplianceDto Compliance, double? PreviousCombinedRate);

public sealed record DashboardDto(
    string DisplayName,
    DateOnly Today,
    string TimeZoneId,
    bool HasHabits,
    TodaySummaryDto TodaySummary,
    IReadOnlyList<HabitListItemDto> Pending,
    IReadOnlyList<HabitListItemDto> Completed,
    IReadOnlyList<HabitListItemDto> Weekly,
    StreakHighlightDto? TopCurrentStreak,
    StreakHighlightDto? BestStreak,
    IReadOnlyList<GoalDto> ActiveGoals,
    WeekSummaryDto Week,
    IReadOnlyList<DailyPointDto> Last30Days,
    IReadOnlyList<UpcomingReminderDto> UpcomingReminders,
    IReadOnlyList<ActivityDto> RecentActivity,
    int UnreadNotifications);

public sealed class DashboardService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IUserClock clock,
    IUserDirectory users,
    HabitTracker tracker,
    HabitService habitService,
    GoalService goalService)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var profile = await users.FindAsync(currentUser.UserId, ct) ?? throw new NotFoundException("Usuario");
        var today = await clock.GetTodayAsync(ct);
        var all = await tracker.LoadAsync(null, null, ct);
        var active = all.Where(t => !t.Habit.IsArchived && t.Habit.StartDate <= t.Today).ToList();
        var categories = await habitService.CategoryNamesAsync(ct);

        List<HabitListItemDto> pending = [], completed = [], weekly = [];
        int scheduled = 0, done = 0;
        foreach (var t in active.OrderBy(t => t.Habit.Name))
        {
            var item = HabitService.ToListItem(t, categories);
            var isWeekly = t.Timeline.SpecOn(t.Today).Unit == PeriodUnit.Week;
            if (isWeekly)
            {
                weekly.Add(item);
                continue;
            }

            if (t.Timeline.IsScheduled(t.Today))
            {
                scheduled++;
                done += item.Today.Completed ? 1 : 0;
                (item.Today.Completed ? completed : pending).Add(item);
            }
            else if (item.Today.Completed)
            {
                completed.Add(item);
            }
        }

        var streaks = all.Select(t => (Tracked: t, Streak: t.Timeline.Streak())).ToList();
        var top = streaks.Where(s => !s.Tracked.Habit.IsArchived && s.Streak.Current > 0)
            .OrderByDescending(s => s.Streak.Current).FirstOrDefault();
        var best = streaks.Where(s => s.Streak.Best is not null)
            .OrderByDescending(s => s.Streak.Best!.Length).FirstOrDefault();

        var weekStart = IsoWeek.StartOf(today);
        var week = StatisticsService.Sum(all, t => t.Timeline.Compliance(weekStart, today));
        var previousWeek = StatisticsService.Sum(all, t => t.Timeline.Compliance(weekStart.AddDays(-7), weekStart.AddDays(-1)));

        var goals = await db.Goals.AsNoTracking().Include(g => g.Habits)
            .Where(g => g.Status == GoalStatus.Active)
            .OrderBy(g => g.Deadline == null).ThenBy(g => g.Deadline).ThenBy(g => g.Name)
            .Take(5)
            .ToListAsync(ct);

        return new DashboardDto(
            profile.DisplayName,
            today,
            profile.TimeZoneId,
            all.Count > 0,
            new TodaySummaryDto(scheduled, done, Percent.Of(done, scheduled)),
            pending,
            completed,
            weekly,
            top.Tracked is null ? null : Highlight(top.Tracked, top.Streak.Current, top.Streak.Unit),
            best.Tracked is null ? null : Highlight(best.Tracked, best.Streak.Best!.Length, best.Streak.Best.Unit),
            await goalService.ToDtosAsync(goals, ct),
            new WeekSummaryDto(weekStart, weekStart.AddDays(6), week.ToDto(), Percent.Of(previousWeek.CombinedRate)),
            StatisticsService.Daily(all, today.AddDays(-29), today),
            await UpcomingRemindersAsync(ct),
            await RecentActivityAsync(ct),
            await db.Notifications.CountAsync(n => n.ReadAt == null, ct));
    }

    private static StreakHighlightDto Highlight(TrackedHabit t, int length, PeriodUnit unit) =>
        new(t.Habit.Id, t.Habit.Name, t.Habit.Icon, t.Habit.Color, length, unit);

    private async Task<IReadOnlyList<UpcomingReminderDto>> UpcomingRemindersAsync(CancellationToken ct) =>
        await db.Reminders.AsNoTracking()
            .Where(r => r.IsActive && r.NextFireAt != null)
            .OrderBy(r => r.NextFireAt)
            .Take(5)
            .Select(r => new UpcomingReminderDto(
                r.Id,
                r.Title,
                r.NextFireAt!.Value,
                db.Habits.Where(h => h.Id == r.HabitId).Select(h => h.Name).FirstOrDefault(),
                db.Goals.Where(g => g.Id == r.GoalId).Select(g => g.Name).FirstOrDefault()))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<ActivityDto>> RecentActivityAsync(CancellationToken ct) =>
        await db.HabitLogChanges.AsNoTracking()
            .Join(db.Habits, c => c.HabitId, h => h.Id, (c, h) => new { c, h })
            .OrderByDescending(x => x.c.CreatedAt).ThenByDescending(x => x.c.Id)
            .Take(8)
            .Select(x => new ActivityDto(x.h.Id, x.h.Name, x.h.Icon, x.h.Color, x.h.Unit, x.c.LocalDate, x.c.Action,
                x.c.NewCompleted, x.c.NewQuantity, x.c.CreatedAt))
            .ToListAsync(ct);
}
