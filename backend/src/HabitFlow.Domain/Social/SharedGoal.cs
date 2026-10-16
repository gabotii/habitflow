using HabitFlow.Domain.Common;
using HabitFlow.Domain.Goals;

namespace HabitFlow.Domain.Social;

public enum SharedGoalRole
{
    /// <summary>Edits the goal, manages invitations and members, completes or cancels it.</summary>
    Owner,

    /// <summary>Sees the goal and everyone's totals, adds and removes their own contributions, can leave.</summary>
    Member,
}

public enum SharedGoalStatus
{
    Active,
    Completed,
    Cancelled,
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Declined,
    Revoked,
}

/// <summary>
/// A goal several users work towards together. Each participant's progress is the sum of their own contributions;
/// the collective progress is the sum of everyone's. Permissions are explicit per role (see <see cref="SharedGoalRole"/>).
/// </summary>
public sealed class SharedGoal : Entity
{
    public const int MaxParticipants = 20;

    private readonly List<SharedGoalParticipant> _participants = [];

    private SharedGoal()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal CollectiveTarget { get; private set; }

    /// <summary>Optional personal target each participant aims for.</summary>
    public decimal? IndividualTarget { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly? Deadline { get; private set; }
    public SharedGoalStatus Status { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyList<SharedGoalParticipant> Participants => _participants;

    public static SharedGoal Create(
        Guid ownerId,
        string name,
        string? description,
        string unit,
        decimal collectiveTarget,
        decimal? individualTarget,
        DateOnly startDate,
        DateOnly? deadline,
        DateTimeOffset now)
    {
        var goal = new SharedGoal { StartDate = startDate, Status = SharedGoalStatus.Active };
        goal.Update(name, description, unit, collectiveTarget, individualTarget, deadline);
        goal._participants.Add(new SharedGoalParticipant(goal.Id, ownerId, SharedGoalRole.Owner, now));
        return goal;
    }

    public void Update(string name, string? description, string unit, decimal collectiveTarget,
        decimal? individualTarget, DateOnly? deadline)
    {
        EnsureActive();
        Name = Guard.Required(name, Goal.NameMaxLength, "nombre");
        Description = Guard.Optional(description, Goal.DescriptionMaxLength, "descripción");
        Unit = Guard.Required(unit, Goal.UnitMaxLength, "unidad");
        CollectiveTarget = ValidTarget(collectiveTarget);
        IndividualTarget = individualTarget is null ? null : ValidTarget(individualTarget.Value);
        if (deadline is { } d && d < StartDate)
        {
            throw new DomainException("goal.invalid_deadline", "La fecha límite no puede ser anterior al inicio.");
        }

        Deadline = deadline;
    }

    public SharedGoalParticipant? ActiveParticipant(Guid userId) =>
        _participants.SingleOrDefault(p => p.UserId == userId && p.LeftAt is null);

    public void EnsureOwner(Guid userId)
    {
        if (ActiveParticipant(userId)?.Role != SharedGoalRole.Owner)
        {
            throw new DomainException("shared_goal.owner_only", "Solo la persona propietaria puede hacer esto.");
        }
    }

    public void AddMember(Guid userId, DateTimeOffset now)
    {
        EnsureActive();
        if (ActiveParticipant(userId) is not null)
        {
            throw new DomainException("shared_goal.already_member", "Ya participas en este objetivo.");
        }

        if (_participants.Count(p => p.LeftAt is null) >= MaxParticipants)
        {
            throw new DomainException("shared_goal.full", $"El objetivo ya tiene {MaxParticipants} participantes.");
        }

        var previous = _participants.SingleOrDefault(p => p.UserId == userId);
        if (previous is not null)
        {
            previous.Rejoin(now);
        }
        else
        {
            _participants.Add(new SharedGoalParticipant(Id, userId, SharedGoalRole.Member, now));
        }
    }

    /// <summary>A member leaves (or is removed). Their past contributions keep counting for the group.</summary>
    public void RemoveMember(Guid userId, DateTimeOffset now)
    {
        var participant = ActiveParticipant(userId)
                          ?? throw new DomainException("shared_goal.not_member", "No participa en este objetivo.");
        if (participant.Role == SharedGoalRole.Owner)
        {
            throw new DomainException("shared_goal.owner_cannot_leave",
                "La persona propietaria no puede salir: puede completar o cancelar el objetivo.");
        }

        participant.Leave(now);
    }

    /// <summary>The owner hands the goal over to another active member and stays as a regular member.</summary>
    public SharedGoalParticipant TransferOwnership(Guid ownerUserId, Guid participantId)
    {
        EnsureActive();
        EnsureOwner(ownerUserId);
        var successor = _participants.SingleOrDefault(p => p.Id == participantId && p.LeftAt is null)
                        ?? throw new DomainException("shared_goal.not_member", "No participa en este objetivo.");
        if (successor.UserId == ownerUserId)
        {
            throw new DomainException("shared_goal.already_owner", "Ya eres la persona propietaria.");
        }

        ActiveParticipant(ownerUserId)!.ChangeRole(SharedGoalRole.Member);
        successor.ChangeRole(SharedGoalRole.Owner);
        return successor;
    }

    /// <summary>
    /// Before the owner's account is deleted, the active member who joined first becomes the owner. Returns null when
    /// nobody else is active: the caller then deletes the goal, since no one could ever open it again.
    /// </summary>
    public SharedGoalParticipant? PromoteSuccessorOf(Guid leavingOwnerId)
    {
        if (ActiveParticipant(leavingOwnerId)?.Role != SharedGoalRole.Owner)
        {
            throw new InvalidOperationException("Only the active owner has a successor.");
        }

        var successor = _participants
            .Where(p => p.LeftAt is null && p.UserId != leavingOwnerId)
            .OrderBy(p => p.JoinedAt).ThenBy(p => p.Id)
            .FirstOrDefault();
        successor?.ChangeRole(SharedGoalRole.Owner);
        return successor;
    }

    public void ChangeStatus(SharedGoalStatus status)
    {
        if (Status != SharedGoalStatus.Active || status == SharedGoalStatus.Active)
        {
            throw new DomainException("goal.invalid_transition", "Solo un objetivo activo puede completarse o cancelarse.");
        }

        Status = status;
    }

    public void EnsureActive()
    {
        if (Status != SharedGoalStatus.Active)
        {
            throw new DomainException("shared_goal.closed", "El objetivo compartido ya está cerrado.");
        }
    }

    private static decimal ValidTarget(decimal value) =>
        value > 0 && value <= Goal.MaxTarget && decimal.Round(value, 2) == value
            ? value
            : throw new DomainException("goal.invalid_target", "La meta debe ser mayor que 0, con 2 decimales como máximo.");
}

public sealed class SharedGoalParticipant
{
    private SharedGoalParticipant()
    {
    }

    internal SharedGoalParticipant(Guid sharedGoalId, Guid userId, SharedGoalRole role, DateTimeOffset joinedAt)
    {
        SharedGoalId = sharedGoalId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid SharedGoalId { get; private set; }
    public Guid UserId { get; private set; }
    public SharedGoalRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    internal void Leave(DateTimeOffset now) => LeftAt = now;

    internal void ChangeRole(SharedGoalRole role) => Role = role;

    internal void Rejoin(DateTimeOffset now)
    {
        LeftAt = null;
        JoinedAt = now;
    }
}

/// <summary>
/// Invitation addressed to an e-mail. Whether an account exists for it is never revealed to the inviter; the invitee
/// sees it when signed in with that e-mail.
/// </summary>
public sealed class SharedGoalInvitation : Entity
{
    private SharedGoalInvitation()
    {
    }

    public Guid SharedGoalId { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public string NormalizedEmail { get; private set; } = string.Empty;
    public InvitationStatus Status { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    public static SharedGoalInvitation Create(Guid sharedGoalId, Guid invitedBy, string email) => new()
    {
        SharedGoalId = sharedGoalId,
        InvitedByUserId = invitedBy,
        NormalizedEmail = Normalize(email),
        Status = InvitationStatus.Pending,
    };

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public void Respond(InvitationStatus status, DateTimeOffset now)
    {
        if (Status != InvitationStatus.Pending)
        {
            throw new DomainException("invitation.not_pending", "La invitación ya no está pendiente.");
        }

        Status = status;
        RespondedAt = now;
    }
}

/// <summary>A participant's own contribution. The note is private to its author.</summary>
public sealed class SharedGoalContribution : Entity
{
    public const int NoteMaxLength = 300;

    private SharedGoalContribution()
    {
    }

    public Guid SharedGoalId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Note { get; private set; }

    public static SharedGoalContribution Create(SharedGoal goal, Guid userId, DateOnly date, decimal amount,
        string? note, DateOnly today)
    {
        goal.EnsureActive();
        if (goal.ActiveParticipant(userId) is null)
        {
            throw new DomainException("shared_goal.not_member", "No participas en este objetivo.");
        }

        if (date < goal.StartDate || date > today || (goal.Deadline is { } deadline && date > deadline))
        {
            throw new DomainException("goal.entry_date_out_of_range",
                "La fecha debe estar dentro del periodo del objetivo y no ser futura.");
        }

        if (amount <= 0 || amount > GoalProgressEntry.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("goal.invalid_amount", "La cantidad debe ser mayor que 0, con 2 decimales como máximo.");
        }

        return new SharedGoalContribution
        {
            SharedGoalId = goal.Id,
            UserId = userId,
            LocalDate = date,
            Amount = amount,
            Note = Guard.Optional(note, NoteMaxLength, "nota"),
        };
    }
}
