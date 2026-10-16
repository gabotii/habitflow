using HabitFlow.Domain.Habits;

namespace HabitFlow.Infrastructure.Persistence;

/// <summary>Default categories, seeded by migration with fixed ids so they are identical in every environment.</summary>
internal static class BuiltInCategories
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    public static readonly Guid Health = Guid.Parse("01990000-0000-7000-8000-000000000001");
    public static readonly Guid Exercise = Guid.Parse("01990000-0000-7000-8000-000000000002");
    public static readonly Guid Learning = Guid.Parse("01990000-0000-7000-8000-000000000003");
    public static readonly Guid Mind = Guid.Parse("01990000-0000-7000-8000-000000000004");
    public static readonly Guid Productivity = Guid.Parse("01990000-0000-7000-8000-000000000005");
    public static readonly Guid Rest = Guid.Parse("01990000-0000-7000-8000-000000000006");
    public static readonly Guid Nutrition = Guid.Parse("01990000-0000-7000-8000-000000000007");
    public static readonly Guid Finance = Guid.Parse("01990000-0000-7000-8000-000000000008");
    public static readonly Guid Relationships = Guid.Parse("01990000-0000-7000-8000-000000000009");
    public static readonly Guid Other = Guid.Parse("01990000-0000-7000-8000-00000000000a");

    public static HabitCategory[] All =>
    [
        Seed(Health, "Salud", HabitColor.Emerald, "heart", 1),
        Seed(Exercise, "Ejercicio", HabitColor.Orange, "dumbbell", 2),
        Seed(Learning, "Aprendizaje", HabitColor.Indigo, "graduation-cap", 3),
        Seed(Mind, "Mente", HabitColor.Violet, "brain", 4),
        Seed(Productivity, "Productividad", HabitColor.Sky, "list-checks", 5),
        Seed(Rest, "Descanso", HabitColor.Slate, "moon", 6),
        Seed(Nutrition, "Alimentación", HabitColor.Lime, "salad", 7),
        Seed(Finance, "Finanzas", HabitColor.Amber, "wallet", 8),
        Seed(Relationships, "Relaciones", HabitColor.Rose, "users", 9),
        Seed(Other, "Otros", HabitColor.Teal, "circle-check", 10),
    ];

    private static HabitCategory Seed(Guid id, string name, HabitColor color, string icon, int order)
    {
        var category = HabitCategory.BuiltIn(id, name, color, icon, order);
        category.CreatedAt = SeededAt;
        category.UpdatedAt = SeededAt;
        return category;
    }
}
