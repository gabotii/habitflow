using HabitFlow.Domain.Time;

namespace HabitFlow.Domain.Habits.Tracking;

/// <summary>
/// Pure, deterministic evaluation of one habit: schedule versions + pauses + daily logs + "today" (the habit's local
/// date) → day statuses, periods, streaks and compliance. No I/O, no clock: identical inputs give identical results,
/// so history can always be recomputed (docs/ARCHITECTURE.md §3.3–§3.6).
/// </summary>
public sealed class HabitTimeline
{
    private readonly ScheduleVersion[] _versions;
    private readonly PauseSpan[] _pauses;
    private readonly Dictionary<DateOnly, DayLog> _logs;
    private IReadOnlyList<Period>? _periods;

    public HabitTimeline(
        HabitType type,
        DateOnly startDate,
        IEnumerable<ScheduleVersion> versions,
        IEnumerable<PauseSpan> pauses,
        IEnumerable<DayLog> logs,
        DateOnly today)
    {
        Type = type;
        StartDate = startDate;
        Today = today;
        _versions = versions.OrderBy(v => v.EffectiveFrom).ToArray();
        if (_versions.Length == 0)
        {
            throw new ArgumentException("A habit needs at least one schedule version.", nameof(versions));
        }

        _pauses = pauses.ToArray();
        _logs = logs.ToDictionary(l => l.Date);
    }

    public HabitType Type { get; }
    public DateOnly StartDate { get; }
    public DateOnly Today { get; }

    /// <summary>Chronological periods from the start date up to today (including the open current week).</summary>
    public IReadOnlyList<Period> Periods => _periods ??= BuildPeriods();

    public static HabitTimeline For(Habit habit, IEnumerable<DayLog> logs, DateOnly today) => new(
        habit.Type,
        habit.StartDate,
        habit.Schedules.Select(s => new ScheduleVersion(s.EffectiveFrom, s.Spec)),
        habit.Pauses.Select(p => new PauseSpan(p.StartDate, p.EndDate)),
        logs,
        today);

    /// <summary>The rules in force on <paramref name="date"/> (the initial ones before the start date).</summary>
    public ScheduleSpec SpecOn(DateOnly date) => VersionOn(date).Spec;

    public bool IsPaused(DateOnly date) => _pauses.Any(p => p.Covers(date));

    /// <summary>Whether the habit is expected on that day: a scheduled day, or any day for weekly quotas.</summary>
    public bool IsScheduled(DateOnly date)
    {
        if (date < StartDate || IsPaused(date))
        {
            return false;
        }

        var version = VersionOn(date);
        return IsScheduledByVersion(version, date);
    }

    public DayLog? LogOn(DateOnly date) => _logs.TryGetValue(date, out var log) ? log : null;

    public HabitDayStatus StatusOn(DateOnly date)
    {
        var log = LogOn(date);
        var completed = log?.Completed == true;
        var partial = !completed && Type == HabitType.Quantity && log?.Quantity > 0;

        if (!IsScheduled(date))
        {
            // Logs on unscheduled or paused days are shown as extra progress, never as failures.
            return completed ? HabitDayStatus.Completed
                : partial ? HabitDayStatus.Partial
                : HabitDayStatus.NotScheduled;
        }

        if (date > Today)
        {
            return HabitDayStatus.Future;
        }

        if (completed)
        {
            return HabitDayStatus.Completed;
        }

        if (partial)
        {
            return HabitDayStatus.Partial;
        }

        if (SpecOn(date).Unit == PeriodUnit.Week)
        {
            return HabitDayStatus.Optional;
        }

        return date == Today ? HabitDayStatus.Pending : HabitDayStatus.Missed;
    }

    /// <summary>
    /// Streak rules: consecutive successful periods of the same unit; a missed period breaks the run; open periods,
    /// unscheduled and paused days are neutral; switching between day- and week-based rules starts a new run.
    /// </summary>
    public StreakSummary Streak()
    {
        var unit = SpecOn(Today < StartDate ? StartDate : Today).Unit;
        StreakRun? run = null;
        StreakRun? best = null;

        foreach (var period in Periods)
        {
            switch (period.Outcome)
            {
                case PeriodOutcome.Open:
                    continue;
                case PeriodOutcome.Missed:
                    run = null;
                    continue;
            }

            run = run is not null && run.Unit == period.Unit
                ? run with { Length = run.Length + 1, End = period.End }
                : new StreakRun(period.Unit, 1, period.Start, period.End);

            if (run.Unit == unit && (best is null || run.Length > best.Length))
            {
                best = run;
            }
        }

        var current = run is not null && run.Unit == unit ? run : null;
        var lastCompleted = _logs.Values
            .Where(l => l.Completed && l.Date >= StartDate && l.Date <= Today)
            .Select(l => (DateOnly?)l.Date)
            .Max();

        return new StreakSummary(unit, current?.Length ?? 0, current?.Start, best, lastCompleted);
    }

    /// <summary>Progress of the current ISO week, when today's rules are a weekly quota.</summary>
    public WeekProgress? CurrentWeek()
    {
        if (Today < StartDate || IsPaused(Today) || SpecOn(Today).Unit != PeriodUnit.Week)
        {
            return null;
        }

        var period = Periods.LastOrDefault(p => p.Unit == PeriodUnit.Week && p.Start <= Today && Today <= p.End);
        if (period is null)
        {
            return null;
        }

        return new WeekProgress(
            period.Start,
            period.End,
            period.Completed,
            period.Required,
            period.End.DayNumber - Today.DayNumber + 1,
            period.Completed >= period.Required);
    }

    /// <summary>
    /// Counts for periods that start inside [<paramref name="from"/>, <paramref name="to"/>]. Open periods only count
    /// once they have succeeded, so a pending today or an unfinished week never lowers the rate.
    /// </summary>
    public ComplianceSummary Compliance(DateOnly from, DateOnly to)
    {
        int scheduled = 0, scheduledDone = 0, weeks = 0, weeksDone = 0, weeklyRequired = 0, weeklyDone = 0;
        foreach (var period in Periods)
        {
            if (period.Start < from || period.Start > to || period.Outcome == PeriodOutcome.Open)
            {
                continue;
            }

            var success = period.Outcome == PeriodOutcome.Success;
            if (period.Unit == PeriodUnit.Day)
            {
                scheduled++;
                scheduledDone += success ? 1 : 0;
            }
            else
            {
                weeks++;
                weeksDone += success ? 1 : 0;
                weeklyRequired += period.Required;
                weeklyDone += Math.Min(period.Completed, period.Required);
            }
        }

        int logs = 0, logsDone = 0, extra = 0;
        decimal quantity = 0;
        foreach (var log in _logs.Values)
        {
            if (log.Date < from || log.Date > to || log.Date < StartDate || log.Date > Today)
            {
                continue;
            }

            logs++;
            quantity += log.Quantity ?? 0;
            if (!log.Completed)
            {
                continue;
            }

            logsDone++;
            if (!IsScheduled(log.Date))
            {
                extra++;
            }
        }

        return new ComplianceSummary(scheduled, scheduledDone, weeks, weeksDone, weeklyRequired, weeklyDone, extra,
            logs, logsDone, quantity);
    }

    /// <summary>Stored logs up to today, in date order.</summary>
    public IEnumerable<DayLog> Logs =>
        _logs.Values.Where(l => l.Date >= StartDate && l.Date <= Today).OrderBy(l => l.Date);

    /// <summary>The weekly-quota period (ISO week segment) that contains <paramref name="date"/>, if any.</summary>
    public Period? WeekContaining(DateOnly date) =>
        Periods.FirstOrDefault(p => p.Unit == PeriodUnit.Week && p.Start <= date && date <= p.End);

    /// <summary>
    /// Longest run (in the current unit) made only of periods that lie inside [<paramref name="from"/>,
    /// <paramref name="to"/>]. Used by streak goals: "keep the routine for 30 days" within the goal window.
    /// </summary>
    public int LongestRun(DateOnly from, DateOnly to)
    {
        var unit = SpecOn(Today < StartDate ? StartDate : Today).Unit;
        int best = 0, run = 0;
        foreach (var period in Periods)
        {
            if (period.Start < from || period.Start > to || period.Unit != unit || period.Outcome == PeriodOutcome.Open)
            {
                continue;
            }

            run = period.Outcome == PeriodOutcome.Success ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return best;
    }

    /// <summary>Length of the running streak at the end of each day in the range (for the evolution chart).</summary>
    public IReadOnlyList<StreakPoint> StreakSeries(DateOnly from, DateOnly to)
    {
        var last = to > Today ? Today : to;
        var points = new List<StreakPoint>();
        if (last < from)
        {
            return points;
        }

        var unit = SpecOn(from < StartDate ? StartDate : from).Unit;
        var length = 0;
        var index = 0;
        var periods = Periods;
        for (var day = StartDate < from ? StartDate : from; day <= last; day = day.AddDays(1))
        {
            // Apply every period that closed on this day (successes at their end, or today if achieved early).
            while (index < periods.Count && ClosingDay(periods[index]) <= day)
            {
                var period = periods[index++];
                if (period.Outcome == PeriodOutcome.Missed)
                {
                    length = 0;
                    unit = period.Unit;
                }
                else if (period.Outcome == PeriodOutcome.Success)
                {
                    length = period.Unit == unit ? length + 1 : 1;
                    unit = period.Unit;
                }
            }

            if (day >= from)
            {
                points.Add(new StreakPoint(day, length, unit));
            }
        }

        return points;
    }

    private DateOnly ClosingDay(Period period) =>
        period.Outcome == PeriodOutcome.Open ? DateOnly.MaxValue
        : period.End > Today ? Today
        : period.End;

    private ScheduleVersion VersionOn(DateOnly date)
    {
        var match = _versions[0];
        foreach (var version in _versions)
        {
            if (version.EffectiveFrom > date)
            {
                break;
            }

            match = version;
        }

        return match;
    }

    private static bool IsScheduledByVersion(ScheduleVersion version, DateOnly date) => version.Spec.Kind switch
    {
        ScheduleKind.Daily or ScheduleKind.TimesPerWeek => true,
        ScheduleKind.Weekdays => Weekdays.Contains(version.Spec.WeekdaysMask ?? 0, date.DayOfWeek),
        ScheduleKind.EveryNDays => (date.DayNumber - version.EffectiveFrom.DayNumber) % version.Spec.IntervalDays!.Value == 0,
        _ => false,
    };

    private List<Period> BuildPeriods()
    {
        var periods = new List<Period>();
        for (var i = 0; i < _versions.Length; i++)
        {
            var version = _versions[i];
            var segmentStart = version.EffectiveFrom < StartDate ? StartDate : version.EffectiveFrom;
            var segmentEnd = i + 1 < _versions.Length ? _versions[i + 1].EffectiveFrom.AddDays(-1) : DateOnly.MaxValue;
            if (segmentStart > segmentEnd || segmentStart > Today)
            {
                continue;
            }

            if (version.Spec.Unit == PeriodUnit.Day)
            {
                AddDayPeriods(periods, version, segmentStart, segmentEnd);
            }
            else
            {
                AddWeekPeriods(periods, version, segmentStart, segmentEnd);
            }
        }

        return periods;
    }

    private void AddDayPeriods(List<Period> periods, ScheduleVersion version, DateOnly start, DateOnly end)
    {
        var last = end < Today ? end : Today;
        for (var day = start; day <= last; day = day.AddDays(1))
        {
            if (IsPaused(day) || !IsScheduledByVersion(version, day))
            {
                continue;
            }

            var done = LogOn(day)?.Completed == true;
            var outcome = done ? PeriodOutcome.Success
                : day == Today ? PeriodOutcome.Open
                : PeriodOutcome.Missed;
            periods.Add(new Period(PeriodUnit.Day, day, day, outcome, done ? 1 : 0, 1));
        }
    }

    /// <summary>
    /// Splits the segment into ISO-week chunks. A chunk's quota is prorated by the days actually available in it
    /// (not paused, inside the segment): <c>ceil(timesPerWeek × available / 7)</c>, so incomplete weeks — the first
    /// week of a habit, a week where the rules changed, a week with a pause — ask for a fair share.
    /// </summary>
    private void AddWeekPeriods(List<Period> periods, ScheduleVersion version, DateOnly start, DateOnly end)
    {
        var timesPerWeek = version.Spec.TimesPerWeek!.Value;
        var chunkStart = start;
        while (chunkStart <= end && chunkStart <= Today)
        {
            var weekEnd = IsoWeek.EndOf(chunkStart);
            var chunkEnd = weekEnd < end ? weekEnd : end;

            int available = 0, completed = 0;
            for (var day = chunkStart; day <= chunkEnd; day = day.AddDays(1))
            {
                if (IsPaused(day))
                {
                    continue;
                }

                available++;
                if (day <= Today && LogOn(day)?.Completed == true)
                {
                    completed++;
                }
            }

            if (available > 0)
            {
                var required = (int)Math.Ceiling(timesPerWeek * available / 7.0);
                var outcome = completed >= required ? PeriodOutcome.Success
                    : chunkEnd < Today ? PeriodOutcome.Missed
                    : PeriodOutcome.Open;
                periods.Add(new Period(PeriodUnit.Week, chunkStart, chunkEnd, outcome, completed, required));
            }

            if (chunkEnd == DateOnly.MaxValue)
            {
                break;
            }

            chunkStart = chunkEnd.AddDays(1);
        }
    }
}
