namespace HabitFlow.Domain.Time;

/// <summary>
/// Set of weekdays stored as a 7-bit mask: Monday = bit 0 … Sunday = bit 6 (ISO order). Valid masks are 1–127.
/// </summary>
public static class Weekdays
{
    public const int All = 0b111_1111;

    public static int Bit(DayOfWeek day) => 1 << IsoIndex(day);

    /// <summary>0 = Monday … 6 = Sunday.</summary>
    public static int IsoIndex(DayOfWeek day) => ((int)day + 6) % 7;

    public static bool Contains(int mask, DayOfWeek day) => (mask & Bit(day)) != 0;

    public static bool IsValid(int mask) => mask is >= 1 and <= All;

    public static int FromDays(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, day) => mask | Bit(day));

    public static IReadOnlyList<DayOfWeek> ToDays(int mask) =>
        Enumerable.Range(0, 7).Where(i => (mask & (1 << i)) != 0).Select(i => (DayOfWeek)((i + 1) % 7)).ToList();
}
