using HabitFlow.Domain.Common;

namespace HabitFlow.Domain.Habits;

/// <summary>Built-in categories have no owner (<see cref="UserId"/> null) and are shared read-only by everyone.</summary>
public sealed class HabitCategory : Entity
{
    public const int NameMaxLength = 40;

    private HabitCategory()
    {
    }

    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public HabitColor Color { get; private set; }
    public string Icon { get; private set; } = HabitIcons.Default;
    public int SortOrder { get; private set; }

    public bool IsBuiltIn => UserId is null;

    public static HabitCategory Create(Guid userId, string name, HabitColor color, string icon)
    {
        var category = new HabitCategory { UserId = userId, SortOrder = 100 };
        category.Update(name, color, icon);
        return category;
    }

    public static HabitCategory BuiltIn(Guid id, string name, HabitColor color, string icon, int sortOrder) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = Normalize(name),
        Color = color,
        Icon = icon,
        SortOrder = sortOrder,
    };

    public void Update(string name, HabitColor color, string icon)
    {
        if (IsBuiltIn)
        {
            throw new DomainException("category.built_in", "Las categorías predeterminadas no se pueden modificar.");
        }

        Name = Guard.Required(name, NameMaxLength, "nombre");
        NormalizedName = Normalize(Name);
        Color = color;
        Icon = HabitIcons.All.Contains(icon)
            ? icon
            : throw new DomainException("habit.invalid_icon", "Icono no válido.");
    }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
