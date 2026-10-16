namespace HabitFlow.Domain.Habits;

public enum HabitType
{
    /// <summary>Done / not done.</summary>
    Binary,

    /// <summary>A daily amount (minutes, pages, litres…), optionally with a daily target.</summary>
    Quantity,
}

public enum ScheduleKind
{
    Daily,
    Weekdays,
    EveryNDays,
    TimesPerWeek,
}

/// <summary>Palette keys. The frontend maps each one to accessible light/dark tokens.</summary>
public enum HabitColor
{
    Teal,
    Emerald,
    Lime,
    Amber,
    Orange,
    Rose,
    Pink,
    Violet,
    Indigo,
    Sky,
    Slate,
}

public enum LogChangeAction
{
    Created,
    Updated,
    Deleted,
}
