namespace HabitFlow.Domain.Time;

/// <summary>Weeks run Monday–Sunday (ISO 8601) everywhere in the product: streaks, statistics, goals and challenges.</summary>
public static class IsoWeek
{
    public static DateOnly StartOf(DateOnly date) => date.AddDays(-Weekdays.IsoIndex(date.DayOfWeek));

    public static DateOnly EndOf(DateOnly date) => StartOf(date).AddDays(6);
}
