using HabitFlow.Domain.Common;

namespace HabitFlow.Domain.Habits;

/// <summary>
/// The single effective record of a habit on one local calendar day (unique per habit and date). Quantities are a
/// daily total, not increments. <see cref="IsCompleted"/> is always computed by <see cref="Habit.EvaluateCompletion"/>.
/// </summary>
public sealed class HabitLog : OwnedEntity
{
    public const int NoteMaxLength = 500;
    public const decimal MaxQuantity = 1_000_000m;

    private HabitLog()
    {
    }

    public Guid HabitId { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public decimal? Quantity { get; private set; }
    public bool IsCompleted { get; private set; }
    public string? Note { get; private set; }

    public static HabitLog Create(Habit habit, DateOnly date, decimal? quantity, bool completed, string? note)
    {
        var log = new HabitLog { UserId = habit.UserId, HabitId = habit.Id, LocalDate = date };
        log.Apply(habit, quantity, completed, note);
        return log;
    }

    public void Update(Habit habit, decimal? quantity, bool completed, string? note) =>
        Apply(habit, quantity, completed, note);

    /// <summary>Re-evaluates completion after the rules for this day changed (only possible for "today").</summary>
    public void Reevaluate(Habit habit) => IsCompleted = habit.EvaluateCompletion(LocalDate, Quantity, IsCompleted);

    private void Apply(Habit habit, decimal? quantity, bool completed, string? note)
    {
        if (habit.Type == HabitType.Binary)
        {
            if (quantity is not null)
            {
                throw new DomainException("log.quantity_not_allowed", "Los hábitos de sí/no no admiten cantidad.");
            }
        }
        else if (quantity is null || quantity < 0 || quantity > MaxQuantity ||
                 decimal.Round(quantity.Value, 2) != quantity)
        {
            throw new DomainException("log.invalid_quantity",
                "Indica una cantidad entre 0 y 1.000.000, con 2 decimales como máximo.");
        }

        Quantity = quantity;
        Note = Guard.Optional(note, NoteMaxLength, "nota");
        IsCompleted = habit.EvaluateCompletion(LocalDate, quantity, completed);
    }
}
