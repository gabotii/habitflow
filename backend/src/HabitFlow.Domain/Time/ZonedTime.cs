namespace HabitFlow.Domain.Time;

/// <summary>
/// Conversions between global instants and wall-clock time in an IANA zone, with explicit daylight-saving rules:
/// <list type="bullet">
/// <item>A local time that does not exist (clocks jump forward) is shifted forward by the length of the gap
/// (02:30 in a 02:00→03:00 gap resolves to 03:30).</item>
/// <item>A local time that happens twice (clocks fall back) resolves to its first occurrence.</item>
/// </list>
/// </summary>
public static class ZonedTime
{
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The UTC instant for <paramref name="date"/> at <paramref name="time"/> in <paramref name="zone"/>.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local))
        {
            // Interpret the skipped wall time with the offset in force just before the gap: that moves it forward by
            // exactly the gap length.
            offset = zone.GetUtcOffset(LastValidBefore(local, zone));
        }
        else if (zone.IsAmbiguousTime(local))
        {
            // The larger offset yields the earlier instant (the first time the wall clock shows this time).
            offset = zone.GetAmbiguousTimeOffsets(local).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }

        return new DateTimeOffset(local - offset, TimeSpan.Zero);
    }

    /// <summary>The instant at which <paramref name="date"/> starts in <paramref name="zone"/>.</summary>
    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone) => ToInstant(date, TimeOnly.MinValue, zone);

    private static DateTime LastValidBefore(DateTime local, TimeZoneInfo zone)
    {
        var candidate = local;
        do
        {
            candidate = candidate.AddMinutes(-15);
        }
        while (zone.IsInvalidTime(candidate));

        return candidate;
    }
}
