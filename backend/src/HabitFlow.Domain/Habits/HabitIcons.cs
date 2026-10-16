namespace HabitFlow.Domain.Habits;

/// <summary>Icon keys the client knows how to render (Lucide icon names). Anything else is rejected.</summary>
public static class HabitIcons
{
    public const string Default = "circle-check";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "circle-check", "book-open", "dumbbell", "droplet", "brain", "code", "moon", "languages", "heart", "leaf",
        "pen-line", "music", "sun", "bike", "footprints", "apple", "bed", "coffee", "target", "wallet", "smile",
        "graduation-cap", "flower", "timer", "users", "sparkles", "salad", "cigarette-off", "list-checks", "phone-off",
    };
}
