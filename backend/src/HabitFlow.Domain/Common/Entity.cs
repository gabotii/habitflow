namespace HabitFlow.Domain.Common;

/// <summary>Base type for persisted entities. Ids are time-ordered (UUIDv7) for index locality.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An entity that belongs to exactly one user. Ownership is assigned on creation and never changes.</summary>
public abstract class OwnedEntity : Entity
{
    public Guid UserId { get; protected set; }
}
