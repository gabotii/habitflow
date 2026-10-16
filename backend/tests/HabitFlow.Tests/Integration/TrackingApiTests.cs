using System.Net;
using HabitFlow.Application.Calendar;
using HabitFlow.Application.Dashboard;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Statistics;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class TrackingApiTests(ApiFactory factory)
{
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task Calendar_distinguishes_empty_future_pending_complete_partial_and_missed_days()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var start = today.AddDays(-3);
        var a = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(start, name: "A"));
        var b = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(start, name: "B"));
        await user.Client.LogAsync(a.Id, start);
        await user.Client.LogAsync(b.Id, start);          // start: complete
        await user.Client.LogAsync(a.Id, start.AddDays(1)); // start+1: partial
        //                                                  start+2: missed
        await user.Client.LogAsync(a.Id, today);            // today: pending (1 of 2)

        var range = await user.Client.GetAsync($"/api/v1/calendar?from={Iso(start.AddDays(-1))}&to={Iso(today.AddDays(1))}")
            .OkAsync<CalendarRangeDto>();

        Assert.Equal(
            [CalendarDayState.Empty, CalendarDayState.Complete, CalendarDayState.Partial, CalendarDayState.Missed,
                CalendarDayState.Pending, CalendarDayState.Future],
            range.Days.Select(d => d.State));
        Assert.Equal(50, range.Days[2].Percent);
        Assert.Null(range.Days[5].Percent);
    }

    [Fact]
    public async Task Calendar_range_is_limited_to_six_weeks()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();

        var response = await user.Client.GetAsync($"/api/v1/calendar?from={Iso(today.AddDays(-60))}&to={Iso(today)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Calendar_day_detail_lists_habits_with_notes_and_weekly_progress()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var daily = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today.AddDays(-7)));
        var weekly = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-7),
            new ScheduleInput(ScheduleKind.TimesPerWeek, TimesPerWeek: 2), "Correr"));
        await user.Client.LogAsync(daily.Id, today, completed: null, quantity: 8, note: "En el tren");
        await user.Client.LogAsync(weekly.Id, today);

        var detail = await user.Client.GetAsync($"/api/v1/calendar/{Iso(today)}").OkAsync<CalendarDayDetailDto>();

        var reading = detail.Habits.Single(h => h.Habit.Id == daily.Id);
        Assert.Equal(HabitDayStatus.Partial, reading.Day.Status);
        Assert.Equal("En el tren", reading.Day.Note);
        Assert.True(reading.CanLog);
        var running = detail.Habits.Single(h => h.Habit.Id == weekly.Id);
        Assert.Equal(1, running.Week!.Completed);
        Assert.Equal(CalendarDayState.Pending, detail.Summary.State);
        Assert.Equal(1, detail.Summary.WeeklyCompletions);
    }

    [Fact]
    public async Task Calendar_week_is_an_iso_week_matrix_of_habits_and_days()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var monday = IsoWeek.StartOf(today);
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(monday.AddDays(-7)));
        await user.Client.LogAsync(habit.Id, monday);

        var week = await user.Client.GetAsync($"/api/v1/calendar/week/{Iso(today)}").OkAsync<CalendarWeekDto>();

        Assert.Equal((monday, monday.AddDays(6)), (week.From, week.To));
        Assert.Equal(7, week.Days.Count);
        var row = Assert.Single(week.Habits);
        Assert.Equal(7, row.Days.Count);
        Assert.Equal(HabitDayStatus.Completed, row.Days[0].Status);
        Assert.Equal(habit.StartDate, row.LoggableFrom);
        Assert.All(row.Days.Where(d => d.Date > today), d => Assert.Equal(HabitDayStatus.Future, d.Status));
    }

    [Fact]
    public async Task Statistics_accept_a_number_of_days_anchored_to_the_users_today()
    {
        var user = await factory.RegisterUserAsync("Pacific/Kiritimati");

        var stats = await user.Client.GetAsync("/api/v1/statistics?days=7").OkAsync<StatisticsDto>();

        Assert.Equal(factory.Today("Pacific/Kiritimati"), stats.To);
        Assert.Equal(stats.To.AddDays(-6), stats.From);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync("/api/v1/statistics?days=900")).StatusCode);
    }

    [Fact]
    public async Task Archived_habits_keep_their_calendar_history_but_disappear_afterwards()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-2)));
        await user.Client.LogAsync(habit.Id, today.AddDays(-2));
        await user.Client.PostAsync($"/api/v1/habits/{habit.Id}/archive", null);

        var range = await user.Client.GetAsync($"/api/v1/calendar?from={Iso(today.AddDays(-2))}&to={Iso(today)}")
            .OkAsync<CalendarRangeDto>();

        Assert.Equal(CalendarDayState.Complete, range.Days[0].State);
        Assert.Equal(CalendarDayState.Missed, range.Days[1].State);
        Assert.Equal(CalendarDayState.Empty, range.Days[2].State); // paused from today
    }

    [Fact]
    public async Task Statistics_report_separate_indicators_and_compare_periods()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var daily = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-13), name: "Diario"));
        var weekly = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(IsoWeek.StartOf(today).AddDays(-7),
            new ScheduleInput(ScheduleKind.TimesPerWeek, TimesPerWeek: 2), "Semanal"));
        for (var i = -6; i <= -1; i++)
        {
            await user.Client.LogAsync(daily.Id, today.AddDays(i)); // 6 of 7 in the last week (today pending)
        }

        await user.Client.LogAsync(daily.Id, today.AddDays(-13));
        var lastWeek = IsoWeek.StartOf(today).AddDays(-7);
        await user.Client.LogAsync(weekly.Id, lastWeek);
        await user.Client.LogAsync(weekly.Id, lastWeek.AddDays(1));

        var stats = await user.Client.GetAsync($"/api/v1/statistics?from={Iso(today.AddDays(-6))}&to={Iso(today)}")
            .OkAsync<StatisticsDto>();

        Assert.Equal(6, stats.Summary.ScheduledDays);
        Assert.Equal(100, stats.Summary.ScheduledDayRate);
        Assert.Equal(7, stats.Daily.Count);
        Assert.Equal(1, stats.Daily[^1].Scheduled); // today counts as scheduled in the daily series…
        Assert.Equal(0, stats.Daily[^1].Completed); // …but not completed yet
        Assert.Equal(1.0 / 7 * 100, stats.Comparison.Previous.ScheduledDayRate!.Value, 1);
        Assert.NotNull(stats.Comparison.CombinedRateDelta);
        Assert.Equal(7, stats.Weekdays.Count);
        Assert.Equal(2, stats.Habits.Count);
    }

    [Fact]
    public async Task Habit_statistics_include_streak_series_and_quantities()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today.AddDays(-2), target: 10));
        await user.Client.LogAsync(habit.Id, today.AddDays(-2), completed: null, quantity: 12);
        await user.Client.LogAsync(habit.Id, today.AddDays(-1), completed: null, quantity: 4);

        var stats = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/statistics?from={Iso(today.AddDays(-2))}&to={Iso(today)}")
            .OkAsync<HabitStatisticsDto>();

        Assert.Equal([1, 0, 0], stats.StreakSeries.Select(p => p.Length));
        Assert.Equal([12m, 4m], stats.Quantities.Where(q => q.Quantity != null).Select(q => q.Quantity!.Value));
        Assert.Equal(16, stats.Compliance.TotalQuantity);
    }

    [Fact]
    public async Task Dashboard_uses_persisted_data_and_supports_new_users()
    {
        var fresh = await factory.RegisterUserAsync();
        var empty = await fresh.Client.GetAsync("/api/v1/dashboard").OkAsync<DashboardDto>();
        Assert.False(empty.HasHabits);
        Assert.Null(empty.TodaySummary.Percent);
        Assert.Equal(30, empty.Last30Days.Count);

        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var a = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-2), name: "A"));
        var b = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-2), name: "B"));
        var weekly = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today,
            new ScheduleInput(ScheduleKind.TimesPerWeek, TimesPerWeek: 3), "C"));
        foreach (var offset in new[] { -2, -1, 0 })
        {
            await user.Client.LogAsync(a.Id, today.AddDays(offset));
        }

        var dashboard = await user.Client.GetAsync("/api/v1/dashboard").OkAsync<DashboardDto>();

        Assert.Equal("Test User", dashboard.DisplayName);
        Assert.Equal(today, dashboard.Today);
        Assert.Equal(new TodaySummaryDto(2, 1, 50), dashboard.TodaySummary);
        Assert.Equal([b.Id], dashboard.Pending.Select(h => h.Id));
        Assert.Equal([a.Id], dashboard.Completed.Select(h => h.Id));
        Assert.Equal([weekly.Id], dashboard.Weekly.Select(h => h.Id));
        Assert.Equal((a.Id, 3, PeriodUnit.Day), (dashboard.TopCurrentStreak!.HabitId, dashboard.TopCurrentStreak.Length,
            dashboard.TopCurrentStreak.Unit));
        Assert.Equal(3, dashboard.RecentActivity.Count);
        Assert.Equal(today, dashboard.RecentActivity[0].Date);
    }
}
