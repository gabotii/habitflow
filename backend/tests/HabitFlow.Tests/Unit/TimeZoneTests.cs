using HabitFlow.Domain.Reminders;
using HabitFlow.Domain.Time;

namespace HabitFlow.Tests.Unit;

/// <summary>
/// Local dates and wall-clock times around daylight-saving transitions. 2026: Europe springs forward on 29 March and
/// falls back on 25 October; the US springs forward on 8 March.
/// </summary>
public sealed class TimeZoneTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneIds.Resolve("Europe/Madrid");
    private static readonly TimeZoneInfo NewYork = TimeZoneIds.Resolve("America/New_York");
    private static readonly TimeZoneInfo Bogota = TimeZoneIds.Resolve("America/Bogota");

    [Fact]
    public void The_same_instant_is_a_different_local_date_in_each_zone()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 1, 2), ZonedTime.LocalDate(instant, Madrid));
        Assert.Equal(new DateOnly(2026, 1, 1), ZonedTime.LocalDate(instant, Bogota));
        Assert.Equal(new DateOnly(2026, 1, 2), ZonedTime.LocalDate(instant, TimeZoneIds.Resolve("Pacific/Kiritimati")));
        Assert.Equal(new DateOnly(2026, 1, 1), ZonedTime.LocalDate(instant, TimeZoneIds.Resolve("Pacific/Pago_Pago")));
    }

    [Fact]
    public void A_skipped_local_time_moves_forward_by_the_gap()
    {
        // 02:30 does not exist in Madrid on 29 March 2026 → 03:30 CEST = 01:30 UTC.
        var instant = ZonedTime.ToInstant(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), Madrid);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), instant);

        // 02:30 in New York on 8 March 2026 → 03:30 EDT = 07:30 UTC.
        var ny = ZonedTime.ToInstant(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), NewYork);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), ny);
    }

    [Fact]
    public void An_ambiguous_local_time_resolves_to_its_first_occurrence()
    {
        // 02:30 happens twice in Madrid on 25 October 2026; the first is still CEST (+2) = 00:30 UTC.
        var instant = ZonedTime.ToInstant(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Madrid);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), instant);
    }

    [Fact]
    public void Daily_reminder_keeps_its_wall_time_across_dst()
    {
        var everyDay = Weekdays.All;
        var before = Reminder.NextOccurrence(new TimeOnly(8, 0), everyDay, Madrid,
            new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero));
        var after = Reminder.NextOccurrence(new TimeOnly(8, 0), everyDay, Madrid, before);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 6, 0, 0, TimeSpan.Zero), before); // 08:00 CEST
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 6, 0, 0, TimeSpan.Zero), after);

        var autumn = Reminder.NextOccurrence(new TimeOnly(8, 0), everyDay, Madrid,
            new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.Zero), autumn); // 08:00 CET
    }

    [Fact]
    public void Reminder_in_the_dst_gap_fires_once_at_the_shifted_time()
    {
        var mask = Weekdays.Bit(DayOfWeek.Sunday);
        var next = Reminder.NextOccurrence(new TimeOnly(2, 30), mask, Madrid,
            new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), next);
        var following = Reminder.NextOccurrence(new TimeOnly(2, 30), mask, Madrid, next);
        Assert.Equal(new DateTimeOffset(2026, 4, 5, 0, 30, 0, TimeSpan.Zero), following);
    }

    [Fact]
    public void Reminder_skips_unselected_weekdays_and_the_current_minute()
    {
        var weekdays = Weekdays.FromDays([DayOfWeek.Monday, DayOfWeek.Friday]);
        // Monday 12 Oct 2026, 09:00 Madrid (07:00 UTC) exactly → next is Friday 16 Oct.
        var at = new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);

        var next = Reminder.NextOccurrence(new TimeOnly(9, 0), weekdays, Madrid, at);

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 7, 0, 0, TimeSpan.Zero), next);
    }

    [Theory]
    [InlineData("Europe/Madrid", true)]
    [InlineData("America/Argentina/Buenos_Aires", true)]
    [InlineData("UTC", true)]
    [InlineData("Romance Standard Time", false)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    public void Only_iana_zone_ids_are_accepted(string id, bool valid) =>
        Assert.Equal(valid, TimeZoneIds.TryResolve(id, out _));
}
