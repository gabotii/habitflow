using FluentValidation;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Calendar;

/// <summary>
/// How a calendar day looks, based only on day-based habits (weekly quotas never make a single day "missed"):
/// empty days are not failures, future days are not failures, and today is pending until it ends.
/// </summary>
public enum CalendarDayState
{
    /// <summary>No day-based habit was scheduled.</summary>
    Empty,
    Future,
    Pending,
    Complete,
    Partial,
    Missed,
}

public sealed record CalendarDayDto(
    DateOnly Date,
    CalendarDayState State,
    int Scheduled,
    int Completed,
    int Extra,
    int WeeklyCompletions,
    double? Percent);

public sealed record CalendarRangeDto(DateOnly From, DateOnly To, DateOnly Today, IReadOnlyList<CalendarDayDto> Days);

public sealed record CalendarHabitRefDto(
    Guid Id,
    string Name,
    string Icon,
    HabitColor Color,
    HabitType Type,
    string? Unit,
    bool IsArchived,
    ScheduleDto Schedule);

public sealed record CalendarHabitDayDto(CalendarHabitRefDto Habit, HabitDayDto Day, WeekProgressDto? Week, bool CanLog);

/// <summary>
/// One habit across a week. <see cref="LoggableFrom"/> is null for archived habits (read-only); logging is allowed
/// from it up to <see cref="Today"/> (the habit's own local date).
/// </summary>
public sealed record CalendarWeekRowDto(
    CalendarHabitRefDto Habit,
    IReadOnlyList<HabitDayDto> Days,
    WeekProgressDto? Week,
    DateOnly? LoggableFrom,
    DateOnly Today);

public sealed record CalendarWeekDto(
    DateOnly From,
    DateOnly To,
    DateOnly Today,
    IReadOnlyList<CalendarDayDto> Days,
    IReadOnlyList<CalendarWeekRowDto> Habits);

public sealed record CalendarDayDetailDto(DateOnly Date, DateOnly Today, CalendarDayDto Summary, IReadOnlyList<CalendarHabitDayDto> Habits);

public sealed class CalendarQuery
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
}

public sealed class CalendarQueryValidator : AbstractValidator<CalendarQuery>
{
    public const int MaxDays = 42;

    public CalendarQueryValidator()
    {
        RuleFor(q => q.From).NotEmpty();
        RuleFor(q => q.To).NotEmpty().GreaterThanOrEqualTo(q => q.From)
            .WithMessage("La fecha final no puede ser anterior a la inicial.");
        RuleFor(q => q).Must(q => q.To.DayNumber - q.From.DayNumber < MaxDays).WithName("to")
            .WithMessage($"El calendario muestra como máximo {MaxDays} días (6 semanas).");
    }
}

public sealed class CalendarService(IAppDbContext db, IUserClock clock, HabitTracker tracker)
{
    /// <summary>Summary per day for a month (or week) grid. Archived habits count only where they were active.</summary>
    public async Task<CalendarRangeDto> RangeAsync(CalendarQuery query, CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var habits = await tracker.LoadAsync(null, query.From, ct);
        var days = DateRanges.Days(query.From, query.To).Select(d => Summarize(d, today, habits)).ToList();
        return new CalendarRangeDto(query.From, query.To, today, days);
    }

    public async Task<CalendarDayDetailDto> DayAsync(DateOnly date, CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var habits = await tracker.LoadAsync(null, IsoWeekStart(date), ct);
        var notes = await db.HabitLogs.AsNoTracking()
            .Where(l => l.LocalDate == date && l.Note != null)
            .ToDictionaryAsync(l => l.HabitId, l => l.Note, ct);

        var items = habits
            .Select(t => (Tracked: t, Day: t.Timeline.Day(date, notes.GetValueOrDefault(t.Habit.Id))))
            .Where(x => x.Day.Status != HabitDayStatus.NotScheduled || x.Day.Quantity is not null || x.Day.Completed)
            .OrderBy(x => x.Tracked.Habit.Name)
            .Select(x => new CalendarHabitDayDto(
                ToRef(x.Tracked),
                x.Day,
                WeekFor(x.Tracked.Timeline, date),
                !x.Tracked.Habit.IsArchived && date >= x.Tracked.Habit.StartDate && date <= x.Tracked.Today))
            .ToList();

        return new CalendarDayDetailDto(date, today, Summarize(date, today, habits), items);
    }

    /// <summary>
    /// The ISO week containing <paramref name="date"/> as a habit × day matrix (one request for the week view).
    /// Habits appear when they were active on at least one day of the week or have a log in it.
    /// </summary>
    public async Task<CalendarWeekDto> WeekAsync(DateOnly date, CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var start = IsoWeekStart(date);
        var days = DateRanges.Days(start, start.AddDays(6)).ToList();
        var habits = await tracker.LoadAsync(null, start, ct);

        var rows = habits
            .Select(t => (Tracked: t, Days: days.Select(d => t.Timeline.Day(d)).ToList()))
            .Where(x => x.Days.Any(d => d.Status != HabitDayStatus.NotScheduled || d.Quantity is not null || d.Completed))
            .OrderBy(x => x.Tracked.Habit.Name)
            .Select(x => new CalendarWeekRowDto(
                ToRef(x.Tracked),
                x.Days,
                WeekFor(x.Tracked.Timeline, start.AddDays(6) < x.Tracked.Today ? start.AddDays(6) : x.Tracked.Today),
                !x.Tracked.Habit.IsArchived ? x.Tracked.Habit.StartDate : null,
                x.Tracked.Today))
            .ToList();

        return new CalendarWeekDto(start, start.AddDays(6), today, days.Select(d => Summarize(d, today, habits)).ToList(), rows);
    }

    internal static CalendarDayDto Summarize(DateOnly date, DateOnly today, IReadOnlyList<TrackedHabit> habits)
    {
        int scheduled = 0, completed = 0, extra = 0, weekly = 0;
        foreach (var t in habits)
        {
            var timeline = t.Timeline;
            var done = timeline.LogOn(date)?.Completed == true;
            if (!timeline.IsScheduled(date))
            {
                extra += done ? 1 : 0;
                continue;
            }

            if (timeline.SpecOn(date).Unit == PeriodUnit.Week)
            {
                weekly += done ? 1 : 0;
                continue;
            }

            scheduled++;
            completed += done ? 1 : 0;
        }

        var state = scheduled == 0 ? (date > today ? CalendarDayState.Future : CalendarDayState.Empty)
            : date > today ? CalendarDayState.Future
            : completed == scheduled ? CalendarDayState.Complete
            : date == today ? CalendarDayState.Pending
            : completed == 0 ? CalendarDayState.Missed
            : CalendarDayState.Partial;

        // A future day with nothing scheduled is simply empty.
        if (state == CalendarDayState.Future && scheduled == 0)
        {
            state = CalendarDayState.Empty;
        }

        return new CalendarDayDto(date, state, scheduled, completed, extra, weekly,
            date > today ? null : Percent.Of(completed, scheduled));
    }

    internal static CalendarHabitRefDto ToRef(TrackedHabit t) => new(
        t.Habit.Id, t.Habit.Name, t.Habit.Icon, t.Habit.Color, t.Habit.Type, t.Habit.Unit, t.Habit.IsArchived,
        t.Habit.SpecOn(t.Today).ToDto());

    private static WeekProgressDto? WeekFor(HabitTimeline timeline, DateOnly date)
    {
        if (timeline.WeekContaining(date) is not { } week)
        {
            return null;
        }

        var daysLeft = week.End < timeline.Today ? 0 : week.End.DayNumber - timeline.Today.DayNumber + 1;
        return new WeekProgressDto(week.Start, week.End, week.Completed, week.Required, daysLeft,
            week.Completed >= week.Required);
    }

    private static DateOnly IsoWeekStart(DateOnly date) => Domain.Time.IsoWeek.StartOf(date);
}
