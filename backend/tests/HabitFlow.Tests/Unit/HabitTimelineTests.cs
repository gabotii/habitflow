using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;

namespace HabitFlow.Tests.Unit;

/// <summary>
/// The streak engine is the most important business rule (docs/ARCHITECTURE.md §3.5). Dates are local calendar
/// dates; 2026-10-12 is a Monday.
/// </summary>
public sealed class HabitTimelineTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static ScheduleSpec Daily(decimal? target = null) =>
        ScheduleSpec.Create(target is null ? HabitType.Binary : HabitType.Quantity, ScheduleKind.Daily, targetValue: target);

    private static ScheduleSpec On(params DayOfWeek[] days) =>
        ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Weekdays, weekdaysMask: Weekdays.FromDays(days));

    private static ScheduleSpec TimesPerWeek(int n) =>
        ScheduleSpec.Create(HabitType.Binary, ScheduleKind.TimesPerWeek, timesPerWeek: n);

    private static ScheduleSpec Every(int days) =>
        ScheduleSpec.Create(HabitType.Binary, ScheduleKind.EveryNDays, intervalDays: days);

    private static HabitTimeline Timeline(
        DateOnly start,
        DateOnly today,
        IEnumerable<DateOnly> completed,
        ScheduleSpec? spec = null,
        IEnumerable<ScheduleVersion>? laterVersions = null,
        IEnumerable<PauseSpan>? pauses = null,
        HabitType type = HabitType.Binary) =>
        new(type, start,
            new[] { new ScheduleVersion(start, spec ?? Daily()) }.Concat(laterVersions ?? []),
            pauses ?? [],
            completed.Select(d => new DayLog(d, null, true)),
            today);

    private static IEnumerable<DateOnly> Range(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays);

    [Fact]
    public void Daily_streak_counts_consecutive_days_and_today_pending_does_not_break_it()
    {
        var start = Monday.AddDays(-10);
        var timeline = Timeline(start, Monday, Range(Monday.AddDays(-5), Monday.AddDays(-1)));

        var streak = timeline.Streak();

        Assert.Equal(5, streak.Current);
        Assert.Equal(Monday.AddDays(-5), streak.CurrentStart);
        Assert.Equal(PeriodUnit.Day, streak.Unit);
        Assert.Equal(HabitDayStatus.Pending, timeline.StatusOn(Monday));
    }

    [Fact]
    public void Completing_today_extends_the_streak()
    {
        var timeline = Timeline(Monday.AddDays(-3), Monday, Range(Monday.AddDays(-3), Monday));

        Assert.Equal(4, timeline.Streak().Current);
        Assert.Equal(HabitDayStatus.Completed, timeline.StatusOn(Monday));
    }

    [Fact]
    public void A_missed_scheduled_day_breaks_the_streak_and_best_is_kept()
    {
        var start = Monday.AddDays(-9);
        var done = Range(start, start.AddDays(4)).Concat(Range(Monday.AddDays(-2), Monday.AddDays(-1)));
        var timeline = Timeline(start, Monday, done);

        var streak = timeline.Streak();

        Assert.Equal(2, streak.Current);
        Assert.Equal(5, streak.Best!.Length);
        Assert.Equal(start, streak.Best.Start);
        Assert.Equal(HabitDayStatus.Missed, timeline.StatusOn(start.AddDays(5)));
    }

    [Fact]
    public void Unscheduled_days_do_not_break_a_weekday_streak()
    {
        // Mon/Wed/Fri over two weeks, all done; weekends and Tue/Thu are not scheduled.
        var start = Monday.AddDays(-14);
        var done = Range(start, Monday.AddDays(-1))
            .Where(d => d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday);
        var timeline = Timeline(start, Monday, done, On(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday));

        Assert.Equal(6, timeline.Streak().Current);
        Assert.Equal(HabitDayStatus.NotScheduled, timeline.StatusOn(Monday.AddDays(-2)));
        Assert.Equal(HabitDayStatus.Pending, timeline.StatusOn(Monday));
    }

    [Fact]
    public void Missing_a_scheduled_weekday_breaks_the_streak()
    {
        var start = Monday.AddDays(-7);
        // Done Mon and Fri of last week, missed Wednesday.
        var timeline = Timeline(start, Monday, [start, start.AddDays(4)], On(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday));

        Assert.Equal(1, timeline.Streak().Current);
        Assert.Equal(HabitDayStatus.Missed, timeline.StatusOn(start.AddDays(2)));
    }

    [Theory]
    [InlineData("2026-01-29", "2026-02-02")] // month change
    [InlineData("2025-12-29", "2026-01-02")] // year change
    [InlineData("2028-02-27", "2028-03-01")] // leap day
    public void Streaks_cross_month_and_year_boundaries(string from, string to)
    {
        var first = DateOnly.Parse(from);
        var last = DateOnly.Parse(to);
        var timeline = Timeline(first, last.AddDays(1), Range(first, last));

        Assert.Equal(last.DayNumber - first.DayNumber + 1, timeline.Streak().Current);
    }

    [Fact]
    public void Every_n_days_is_anchored_to_the_start_and_extra_logs_do_not_count()
    {
        var start = Monday.AddDays(-12); // scheduled: -12, -9, -6, -3, 0
        var done = new[] { start, start.AddDays(3), start.AddDays(4), start.AddDays(6), start.AddDays(9) };
        var timeline = Timeline(start, Monday, done, Every(3));

        Assert.Equal(4, timeline.Streak().Current);
        Assert.Equal(HabitDayStatus.Completed, timeline.StatusOn(start.AddDays(4))); // extra, shown as done
        Assert.False(timeline.IsScheduled(start.AddDays(4)));
        Assert.Equal(1, timeline.Compliance(start, Monday).ExtraCompletions);
        Assert.Equal(HabitDayStatus.Pending, timeline.StatusOn(Monday));
    }

    [Fact]
    public void Weekly_quota_counts_weeks_not_days()
    {
        // Three full weeks, 3 completions each (any days), current week has 1 so far.
        var start = Monday.AddDays(-21);
        var done = new List<DateOnly>();
        for (var w = 0; w < 3; w++)
        {
            var weekStart = start.AddDays(7 * w);
            done.AddRange([weekStart.AddDays(1), weekStart.AddDays(3), weekStart.AddDays(6)]);
        }

        done.Add(Monday);
        var timeline = Timeline(start, Monday, done, TimesPerWeek(3));
        var streak = timeline.Streak();

        Assert.Equal(PeriodUnit.Week, streak.Unit);
        Assert.Equal(3, streak.Current);
        var week = timeline.CurrentWeek()!;
        Assert.Equal((1, 3, 7, false), (week.Completed, week.Required, week.DaysLeft, week.Achieved));
        Assert.Equal(HabitDayStatus.Optional, timeline.StatusOn(Monday.AddDays(-1).AddDays(-1)));
    }

    [Fact]
    public void Weekly_quota_failure_breaks_the_weekly_streak()
    {
        var start = Monday.AddDays(-14);
        // Week 1: 2 of 3 (fail). Week 2: 3 of 3.
        DateOnly[] done = [start, start.AddDays(1), start.AddDays(7), start.AddDays(8), start.AddDays(9)];
        var timeline = Timeline(start, Monday, done, TimesPerWeek(3));

        var streak = timeline.Streak();

        Assert.Equal(1, streak.Current);
        Assert.Equal(1, streak.Best!.Length);
        var compliance = timeline.Compliance(start, Monday);
        Assert.Equal((2, 1), (compliance.Weeks, compliance.WeeksAchieved));
        Assert.Equal(6, compliance.WeeklyRequired);
        Assert.Equal(5, compliance.WeeklyCompletedCapped);
    }

    [Fact]
    public void An_incomplete_first_week_asks_for_a_prorated_quota()
    {
        // Starts on Friday: 3 of 7 days available, 3/week → ceil(3*3/7) = 2 required.
        var friday = Monday.AddDays(-3);
        var timeline = Timeline(friday, Monday, [friday, friday.AddDays(2)], TimesPerWeek(3));

        var first = timeline.Periods[0];
        Assert.Equal((friday, friday.AddDays(2), 2, PeriodOutcome.Success), (first.Start, first.End, first.Required, first.Outcome));
        Assert.Equal(1, timeline.Streak().Current);
    }

    [Fact]
    public void The_current_week_is_successful_as_soon_as_the_quota_is_met()
    {
        var timeline = Timeline(Monday.AddDays(-7), Monday.AddDays(2), [Monday, Monday.AddDays(1)], TimesPerWeek(2));

        Assert.True(timeline.CurrentWeek()!.Achieved);
        Assert.Equal(1, timeline.Streak().Current);
    }

    [Fact]
    public void Weekly_quotas_span_the_year_change_with_iso_weeks()
    {
        // ISO week Mon 2025-12-29 … Sun 2026-01-04.
        var start = new DateOnly(2025, 12, 29);
        Assert.Equal(start, IsoWeek.StartOf(new DateOnly(2026, 1, 4)));
        var timeline = Timeline(start, new DateOnly(2026, 1, 5), [new(2025, 12, 31), new(2026, 1, 2)], TimesPerWeek(2));

        var week = Assert.Single(timeline.Periods, p => p.Unit == PeriodUnit.Week && p.Outcome != PeriodOutcome.Open);
        Assert.Equal((start, new DateOnly(2026, 1, 4), PeriodOutcome.Success), (week.Start, week.End, week.Outcome));
    }

    [Fact]
    public void Changing_the_frequency_does_not_rewrite_history()
    {
        var start = Monday.AddDays(-14);
        var done = Range(start, Monday.AddDays(-8)).ToList(); // first week fully done, then nothing
        var before = Timeline(start, Monday, done);
        var after = Timeline(start, Monday, done,
            laterVersions: [new ScheduleVersion(Monday.AddDays(-3), On(DayOfWeek.Saturday))]);

        // Days before the change are evaluated with the old rules in both cases.
        Assert.Equal(before.Compliance(start, Monday.AddDays(-4)), after.Compliance(start, Monday.AddDays(-4)));
        Assert.Equal(before.Streak().Best, after.Streak().Best);
        // From the change on, only Saturdays are expected: Friday the 9th (−3) is no longer scheduled.
        Assert.Equal(HabitDayStatus.NotScheduled, after.StatusOn(Monday.AddDays(-3)));
        Assert.Equal(HabitDayStatus.Missed, before.StatusOn(Monday.AddDays(-3)));
    }

    [Fact]
    public void Switching_from_daily_to_weekly_starts_a_new_run_of_the_new_unit()
    {
        var start = Monday.AddDays(-14);
        var done = Range(start, Monday.AddDays(-8)).Concat([Monday.AddDays(-6), Monday.AddDays(-5)]);
        var timeline = Timeline(start, Monday, done,
            laterVersions: [new ScheduleVersion(Monday.AddDays(-7), TimesPerWeek(2))]);

        var streak = timeline.Streak();

        Assert.Equal(PeriodUnit.Week, streak.Unit);
        Assert.Equal(1, streak.Current);
        Assert.Equal(1, streak.Best!.Length); // the 7-day run is a different unit
    }

    [Fact]
    public void Paused_days_are_neutral_and_the_streak_survives_an_archive()
    {
        var start = Monday.AddDays(-10);
        var done = Range(start, start.AddDays(2)).Concat(Range(Monday.AddDays(-2), Monday.AddDays(-1)));
        var pause = new PauseSpan(start.AddDays(3), Monday.AddDays(-3));
        var timeline = Timeline(start, Monday, done, pauses: [pause]);

        Assert.Equal(5, timeline.Streak().Current);
        Assert.Equal(HabitDayStatus.NotScheduled, timeline.StatusOn(start.AddDays(4)));
        Assert.Equal(5, timeline.Compliance(start, Monday).ScheduledDays);
    }

    [Fact]
    public void A_pause_inside_a_week_reduces_the_weekly_quota()
    {
        // 4/week; Thu–Sun paused → 3 available days → ceil(4*3/7) = 2.
        var start = Monday.AddDays(-7);
        var timeline = Timeline(start, Monday, [start, start.AddDays(1)], TimesPerWeek(4),
            pauses: [new PauseSpan(start.AddDays(3), start.AddDays(6))]);

        var week = timeline.Periods.First();
        Assert.Equal((2, PeriodOutcome.Success), (week.Required, week.Outcome));
    }

    [Fact]
    public void Quantity_days_below_target_are_partial_and_do_not_count()
    {
        var start = Monday.AddDays(-2);
        var timeline = new HabitTimeline(HabitType.Quantity, start, [new ScheduleVersion(start, Daily(20))], [],
            [new DayLog(start, 25, true), new DayLog(start.AddDays(1), 12, false), new DayLog(Monday, 5, false)],
            Monday);

        Assert.Equal(HabitDayStatus.Partial, timeline.StatusOn(start.AddDays(1)));
        Assert.Equal(HabitDayStatus.Partial, timeline.StatusOn(Monday));
        Assert.Equal(0, timeline.Streak().Current);
        Assert.Equal(42, timeline.Compliance(start, Monday).TotalQuantity);
    }

    [Fact]
    public void Statuses_before_start_and_in_the_future()
    {
        var timeline = Timeline(Monday, Monday, []);

        Assert.Equal(HabitDayStatus.NotScheduled, timeline.StatusOn(Monday.AddDays(-1)));
        Assert.Equal(HabitDayStatus.Future, timeline.StatusOn(Monday.AddDays(1)));
        Assert.Equal(0, timeline.Streak().Current);
        Assert.Null(timeline.Compliance(Monday, Monday).ScheduledDayRate);
    }

    [Fact]
    public void A_habit_that_has_not_started_has_no_periods()
    {
        var timeline = Timeline(Monday.AddDays(5), Monday, []);

        Assert.Empty(timeline.Periods);
        Assert.Equal(0, timeline.Streak().Current);
        Assert.Equal(HabitDayStatus.NotScheduled, timeline.StatusOn(Monday));
    }

    [Fact]
    public void Results_are_deterministic_regardless_of_input_order()
    {
        var start = Monday.AddDays(-30);
        var done = Range(start, Monday).Where(d => d.Day % 3 != 0).ToList();
        var a = Timeline(start, Monday, done, TimesPerWeek(4));
        var b = Timeline(start, Monday, Enumerable.Reverse(done), TimesPerWeek(4));

        Assert.Equal(a.Streak(), b.Streak());
        Assert.Equal(a.Periods, b.Periods);
        Assert.Equal(a.Compliance(start, Monday), b.Compliance(start, Monday));
    }

    [Fact]
    public void Open_periods_do_not_lower_compliance()
    {
        var start = Monday.AddDays(-2);
        var timeline = Timeline(start, Monday, Range(start, Monday.AddDays(-1)));

        var compliance = timeline.Compliance(start, Monday);

        Assert.Equal(2, compliance.ScheduledDays);
        Assert.Equal(1.0, compliance.ScheduledDayRate);
    }

    [Fact]
    public void Streak_series_tracks_growth_and_breaks()
    {
        var start = Monday.AddDays(-4);
        var timeline = Timeline(start, Monday, [start, start.AddDays(1), start.AddDays(3)]);

        var series = timeline.StreakSeries(start, Monday).Select(p => p.Length).ToArray();

        Assert.Equal([1, 2, 0, 1, 1], series);
    }
}
