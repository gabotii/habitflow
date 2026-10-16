using HabitFlow.Domain.Common;

namespace HabitFlow.Domain.Habits;

/// <summary>
/// Audit trail of log writes (append-only). Notes are not copied: the trail records what changed, not private text.
/// Feeds "recent activity" and makes retroactive corrections reviewable.
/// </summary>
public sealed class HabitLogChange : OwnedEntity
{
    private HabitLogChange()
    {
    }

    public Guid HabitId { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public LogChangeAction Action { get; private set; }
    public decimal? PreviousQuantity { get; private set; }
    public decimal? NewQuantity { get; private set; }
    public bool? PreviousCompleted { get; private set; }
    public bool? NewCompleted { get; private set; }

    /// <summary>Records a write. <paramref name="before"/> is the state before the change (null when created).</summary>
    public static HabitLogChange Record(
        Guid userId,
        Guid habitId,
        DateOnly date,
        (decimal? Quantity, bool Completed)? before,
        HabitLog? after) => new()
        {
            UserId = userId,
            HabitId = habitId,
            LocalDate = date,
            Action = (before, after) switch
            {
                (null, not null) => LogChangeAction.Created,
                (not null, null) => LogChangeAction.Deleted,
                _ => LogChangeAction.Updated,
            },
            PreviousQuantity = before?.Quantity,
            PreviousCompleted = before?.Completed,
            NewQuantity = after?.Quantity,
            NewCompleted = after?.IsCompleted,
        };
}
