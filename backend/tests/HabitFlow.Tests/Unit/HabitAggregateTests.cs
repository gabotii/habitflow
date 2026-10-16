using HabitFlow.Domain.Common;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Domain.Time;

namespace HabitFlow.Tests.Unit;

public sealed class HabitAggregateTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private static Habit NewHabit(HabitType type = HabitType.Binary, ScheduleSpec? spec = null, DateOnly? start = null) =>
        Habit.Create(Guid.NewGuid(),
            new HabitDetails("Leer", null, Guid.NewGuid(), HabitColor.Teal, "book-open",
                type == HabitType.Quantity ? "páginas" : null, null),
            type, start ?? Start, spec ?? ScheduleSpec.Create(type, ScheduleKind.Daily), Now);

    private static ScheduleSpec Weekly(int n) => ScheduleSpec.Create(HabitType.Binary, ScheduleKind.TimesPerWeek, timesPerWeek: n);

    [Fact]
    public void Changing_the_schedule_adds_a_version_from_today_only()
    {
        var habit = NewHabit();
        var today = Start.AddDays(5);

        Assert.True(habit.ChangeSchedule(Weekly(3), today, Now));

        Assert.Equal(2, habit.Schedules.Count);
        Assert.Equal(ScheduleKind.Daily, habit.SpecOn(today.AddDays(-1)).Kind);
        Assert.Equal(ScheduleKind.TimesPerWeek, habit.SpecOn(today).Kind);
    }

    [Fact]
    public void Several_changes_on_the_same_day_collapse_and_reverting_removes_the_version()
    {
        var habit = NewHabit();
        var today = Start.AddDays(5);

        habit.ChangeSchedule(Weekly(3), today, Now);
        habit.ChangeSchedule(Weekly(4), today, Now);
        Assert.Equal(2, habit.Schedules.Count);
        Assert.Equal(4, habit.SpecOn(today).TimesPerWeek);

        habit.ChangeSchedule(ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Daily), today, Now);
        Assert.Single(habit.Schedules);
    }

    [Fact]
    public void Unchanged_schedule_is_a_no_op()
    {
        var habit = NewHabit();

        Assert.False(habit.ChangeSchedule(ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Daily), Start.AddDays(3), Now));
        Assert.Single(habit.Schedules);
    }

    [Fact]
    public void Changing_rules_before_the_start_replaces_the_initial_version()
    {
        var habit = NewHabit(start: Start.AddDays(10));

        habit.ChangeSchedule(Weekly(2), Start, Now);

        var version = Assert.Single(habit.Schedules);
        Assert.Equal(Start.AddDays(10), version.EffectiveFrom);
        Assert.Equal(ScheduleKind.TimesPerWeek, version.Kind);
    }

    [Fact]
    public void Archive_and_restore_record_a_closed_pause()
    {
        var habit = NewHabit();

        habit.Archive(Start.AddDays(3), Now);
        Assert.True(habit.IsArchived);
        Assert.Throws<DomainException>(() => habit.EnsureCanLog(Start.AddDays(2), Start.AddDays(3)));

        habit.Restore(Start.AddDays(7));
        var pause = Assert.Single(habit.Pauses);
        Assert.Equal((Start.AddDays(3), (DateOnly?)Start.AddDays(6)), (pause.StartDate, pause.EndDate));
        Assert.True(habit.IsPausedOn(Start.AddDays(5)));
        Assert.False(habit.IsPausedOn(Start.AddDays(7)));
    }

    [Fact]
    public void Archiving_on_the_start_day_and_restoring_later_keeps_the_frequency()
    {
        var habit = NewHabit(spec: Weekly(3));

        habit.Archive(Start, Now);
        habit.Restore(Start.AddDays(4));

        Assert.Equal(3, habit.SpecOn(Start.AddDays(4)).TimesPerWeek);
        var timeline = HabitTimeline.For(habit, [], Start.AddDays(4));
        Assert.Equal(HabitDayStatus.NotScheduled, timeline.StatusOn(Start.AddDays(1)));
    }

    [Fact]
    public void Archive_and_restore_on_the_same_day_leaves_no_pause()
    {
        var habit = NewHabit();

        habit.Archive(Start.AddDays(2), Now);
        habit.Restore(Start.AddDays(2));

        Assert.Empty(habit.Pauses);
        Assert.False(habit.IsArchived);
    }

    [Fact]
    public void Start_date_is_locked_once_there_is_history()
    {
        var habit = NewHabit();

        var error = Assert.Throws<DomainException>(() => habit.ChangeStartDate(Start.AddDays(-3), true, Start, Now));
        Assert.Equal("habit.start_date_locked", error.Code);

        habit.ChangeStartDate(Start.AddDays(-3), false, Start, Now);
        Assert.Equal(Start.AddDays(-3), Assert.Single(habit.Schedules).EffectiveFrom);
    }

    [Fact]
    public void Completion_uses_the_target_in_force_on_that_day()
    {
        var habit = NewHabit(HabitType.Quantity, ScheduleSpec.Create(HabitType.Quantity, ScheduleKind.Daily, targetValue: 20));
        habit.ChangeSchedule(ScheduleSpec.Create(HabitType.Quantity, ScheduleKind.Daily, targetValue: 30), Start.AddDays(5), Now);

        Assert.True(habit.EvaluateCompletion(Start.AddDays(4), 25, false));
        Assert.False(habit.EvaluateCompletion(Start.AddDays(5), 25, false));
    }

    [Fact]
    public void Quantity_habit_without_target_completes_with_any_positive_amount()
    {
        var habit = NewHabit(HabitType.Quantity);

        Assert.True(habit.EvaluateCompletion(Start, 0.5m, false));
        Assert.False(habit.EvaluateCompletion(Start, 0, true));
    }

    [Fact]
    public void Logs_validate_type_and_dates()
    {
        var binary = NewHabit();
        Assert.Equal("log.quantity_not_allowed",
            Assert.Throws<DomainException>(() => HabitLog.Create(binary, Start, 3, true, null)).Code);
        Assert.Equal("log.future_date",
            Assert.Throws<DomainException>(() => binary.EnsureCanLog(Start.AddDays(2), Start.AddDays(1))).Code);
        Assert.Equal("log.before_start",
            Assert.Throws<DomainException>(() => binary.EnsureCanLog(Start.AddDays(-1), Start)).Code);

        var quantity = NewHabit(HabitType.Quantity);
        Assert.Equal("log.invalid_quantity",
            Assert.Throws<DomainException>(() => HabitLog.Create(quantity, Start, 1.234m, false, null)).Code);
    }

    [Theory]
    [InlineData(ScheduleKind.Weekdays, null, null, "schedule.invalid_weekdays")]
    [InlineData(ScheduleKind.TimesPerWeek, null, 8, "schedule.invalid_times_per_week")]
    [InlineData(ScheduleKind.EveryNDays, null, null, "schedule.invalid_interval")]
    public void Invalid_schedules_are_rejected(ScheduleKind kind, int? mask, int? times, string code)
    {
        var error = Assert.Throws<DomainException>(() =>
            ScheduleSpec.Create(HabitType.Binary, kind, weekdaysMask: mask, timesPerWeek: times));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void Binary_habits_cannot_have_a_numeric_target()
    {
        Assert.Throws<DomainException>(() =>
            ScheduleSpec.Create(HabitType.Binary, ScheduleKind.Daily, targetValue: 5));
    }

    [Fact]
    public void Weekday_mask_round_trips_in_iso_order()
    {
        var mask = Weekdays.FromDays([DayOfWeek.Sunday, DayOfWeek.Monday]);

        Assert.Equal(0b100_0001, mask);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Sunday], Weekdays.ToDays(mask));
    }
}
