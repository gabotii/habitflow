using FluentValidation;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Social;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Social;

public sealed record SaveSharedGoalRequest(
    string Name,
    string? Description,
    string Unit,
    decimal CollectiveTarget,
    decimal? IndividualTarget,
    DateOnly? StartDate,
    DateOnly? Deadline,
    uint? Version);

public sealed record ChangeSharedGoalStatusRequest(SharedGoalStatus Status);

public sealed record InviteRequest(string Email);

public sealed record RespondInvitationRequest(bool Accept);

public sealed record TransferOwnershipRequest(Guid ParticipantId);

public sealed record ContributionRequest(DateOnly? Date, decimal Amount, string? Note);

public sealed record ProgressValueDto(decimal Current, decimal Target, decimal Percent, decimal ExceededBy, bool TargetReached);

public sealed record SharedGoalSummaryDto(
    Guid Id,
    string Name,
    string Unit,
    SharedGoalStatus Status,
    DateOnly? Deadline,
    SharedGoalRole MyRole,
    int Participants,
    ProgressValueDto Collective,
    decimal MyTotal);

/// <summary>Participant as seen by the group: name, role and total. Notes and habits are never shared.</summary>
public sealed record SharedParticipantDto(
    Guid ParticipantId,
    string DisplayName,
    SharedGoalRole Role,
    bool IsMe,
    bool Active,
    decimal Total,
    ProgressValueDto? Individual);

public sealed record MyContributionDto(Guid Id, DateOnly Date, decimal Amount, string? Note, DateTimeOffset CreatedAt);

public sealed record PendingInvitationDto(Guid Id, string Email, DateTimeOffset CreatedAt);

public sealed record SharedGoalDto(
    Guid Id,
    string Name,
    string? Description,
    string Unit,
    decimal CollectiveTarget,
    decimal? IndividualTarget,
    DateOnly StartDate,
    DateOnly? Deadline,
    SharedGoalStatus Status,
    SharedGoalRole MyRole,
    uint Version,
    ProgressValueDto Collective,
    IReadOnlyList<SharedParticipantDto> Participants,
    IReadOnlyList<MyContributionDto> MyContributions,
    // Only for the owner.
    IReadOnlyList<PendingInvitationDto> PendingInvitations);

public sealed record MyInvitationDto(Guid Id, Guid SharedGoalId, string GoalName, string InvitedBy, DateTimeOffset CreatedAt);

public sealed class SaveSharedGoalRequestValidator : AbstractValidator<SaveSharedGoalRequest>
{
    public SaveSharedGoalRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Goal.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Goal.DescriptionMaxLength);
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(Goal.UnitMaxLength);
        RuleFor(r => r.CollectiveTarget).Amount(Goal.MaxTarget);
        RuleFor(r => r.IndividualTarget!.Value).Amount(Goal.MaxTarget).OverridePropertyName("individualTarget")
            .When(r => r.IndividualTarget is not null);
    }
}

public sealed class InviteRequestValidator : AbstractValidator<InviteRequest>
{
    public InviteRequestValidator() => RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

public sealed class ContributionRequestValidator : AbstractValidator<ContributionRequest>
{
    public ContributionRequestValidator()
    {
        RuleFor(r => r.Amount).Amount(GoalProgressEntry.MaxAmount);
        RuleFor(r => r.Note).MaximumLength(SharedGoalContribution.NoteMaxLength);
    }
}

/// <summary>
/// Shared goals with explicit permissions. Not covered by ownership filters: every method checks the caller's
/// active participation (404 otherwise) and the owner role where required. A participant can only write their own
/// contributions; notes are returned only to their author.
/// </summary>
public sealed class SharedGoalService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IUserClock clock,
    IUserDirectory users)
{
    public const int MaxPendingInvitations = 50;

    public async Task<IReadOnlyList<SharedGoalSummaryDto>> ListAsync(CancellationToken ct)
    {
        var me = currentUser.UserId;
        var goals = await db.SharedGoals.AsNoTracking().Include(g => g.Participants)
            .Where(g => g.Participants.Any(p => p.UserId == me && p.LeftAt == null))
            .OrderBy(g => g.Status).ThenBy(g => g.Name)
            .ToListAsync(ct);
        var ids = goals.Select(g => g.Id).ToList();
        var totals = await db.SharedGoalContributions.AsNoTracking()
            .Where(c => ids.Contains(c.SharedGoalId))
            .GroupBy(c => new { c.SharedGoalId, c.UserId })
            .Select(g => new { g.Key.SharedGoalId, g.Key.UserId, Sum = g.Sum(c => c.Amount) })
            .ToListAsync(ct);

        return goals.Select(g =>
        {
            var goalTotals = totals.Where(t => t.SharedGoalId == g.Id).ToList();
            return new SharedGoalSummaryDto(g.Id, g.Name, g.Unit, g.Status, g.Deadline,
                g.ActiveParticipant(me)!.Role, g.Participants.Count(p => p.LeftAt is null),
                Progress(goalTotals.Sum(t => t.Sum), g.CollectiveTarget),
                goalTotals.Where(t => t.UserId == me).Sum(t => t.Sum));
        }).ToList();
    }

    public async Task<SharedGoalDto> GetAsync(Guid id, CancellationToken ct)
    {
        var me = currentUser.UserId;
        var goal = await LoadAsync(id, ct, tracked: false);
        var participant = goal.ActiveParticipant(me)!;

        var totals = await db.SharedGoalContributions.AsNoTracking()
            .Where(c => c.SharedGoalId == id)
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, Sum = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(x => x.UserId, x => x.Sum, ct);
        var names = await users.GetDisplayNamesAsync(goal.Participants.Select(p => p.UserId).ToList(), ct);
        var mine = await db.SharedGoalContributions.AsNoTracking()
            .Where(c => c.SharedGoalId == id && c.UserId == me)
            .OrderByDescending(c => c.LocalDate).ThenByDescending(c => c.CreatedAt)
            .Take(200)
            .Select(c => new MyContributionDto(c.Id, c.LocalDate, c.Amount, c.Note, c.CreatedAt))
            .ToListAsync(ct);
        var invitations = participant.Role == SharedGoalRole.Owner
            ? await db.SharedGoalInvitations.AsNoTracking()
                .Where(i => i.SharedGoalId == id && i.Status == InvitationStatus.Pending)
                .OrderBy(i => i.CreatedAt)
                .Select(i => new PendingInvitationDto(i.Id, i.NormalizedEmail.ToLower(), i.CreatedAt))
                .ToListAsync(ct)
            : [];

        var people = goal.Participants
            .Select(p =>
            {
                var total = totals.GetValueOrDefault(p.UserId);
                return new SharedParticipantDto(p.Id, names.GetValueOrDefault(p.UserId, "Participante"), p.Role,
                    p.UserId == me, p.LeftAt is null, total,
                    goal.IndividualTarget is { } target ? Progress(total, target) : null);
            })
            .OrderByDescending(p => p.Active).ThenByDescending(p => p.Total).ThenBy(p => p.DisplayName)
            .ToList();

        return new SharedGoalDto(goal.Id, goal.Name, goal.Description, goal.Unit, goal.CollectiveTarget,
            goal.IndividualTarget, goal.StartDate, goal.Deadline, goal.Status, participant.Role, goal.Version,
            Progress(totals.Values.Sum(), goal.CollectiveTarget), people, mine, invitations);
    }

    public async Task<SharedGoalDto> CreateAsync(SaveSharedGoalRequest request, CancellationToken ct)
    {
        var today = await clock.GetTodayAsync(ct);
        var goal = SharedGoal.Create(currentUser.UserId, request.Name, request.Description, request.Unit,
            request.CollectiveTarget, request.IndividualTarget, request.StartDate ?? today, request.Deadline,
            clock.UtcNow);
        db.SharedGoals.Add(goal);
        await db.SaveChangesAsync(ct);
        return await GetAsync(goal.Id, ct);
    }

    public async Task<SharedGoalDto> UpdateAsync(Guid id, SaveSharedGoalRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        goal.EnsureOwner(currentUser.UserId);
        if (request.Version is { } version && version != goal.Version)
        {
            throw ConflictException.Concurrency();
        }

        goal.Update(request.Name, request.Description, request.Unit, request.CollectiveTarget, request.IndividualTarget,
            request.Deadline);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SharedGoalDto> ChangeStatusAsync(Guid id, ChangeSharedGoalStatusRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        goal.EnsureOwner(currentUser.UserId);
        goal.ChangeStatus(request.Status);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Invites an e-mail. The answer is the same whether or not an account exists (no enumeration); if it exists, the
    /// person also gets an in-app notification.
    /// </summary>
    public async Task InviteAsync(Guid id, InviteRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        goal.EnsureOwner(currentUser.UserId);
        goal.EnsureActive();

        var normalized = SharedGoalInvitation.Normalize(request.Email);
        if (await db.SharedGoalInvitations.AnyAsync(
                i => i.SharedGoalId == id && i.NormalizedEmail == normalized && i.Status == InvitationStatus.Pending, ct))
        {
            return;
        }

        if (await db.SharedGoalInvitations.CountAsync(i => i.SharedGoalId == id && i.Status == InvitationStatus.Pending, ct)
            >= MaxPendingInvitations)
        {
            throw new ConflictException("invitation.limit", "Hay demasiadas invitaciones pendientes.");
        }

        var inviteeId = await users.FindIdByEmailAsync(request.Email, ct);
        if (inviteeId is { } existing && goal.ActiveParticipant(existing) is not null)
        {
            return; // Already a member: nothing to do, and nothing new to reveal.
        }

        db.SharedGoalInvitations.Add(SharedGoalInvitation.Create(id, currentUser.UserId, request.Email));
        if (inviteeId is { } invitee)
        {
            var inviter = (await users.FindAsync(currentUser.UserId, ct))?.DisplayName ?? "Alguien";
            db.Notifications.Add(Notification.Create(invitee, NotificationKind.Invitation,
                $"Invitación a «{goal.Name}»", $"{inviter} te invita a un objetivo compartido.", "/compartidos",
                sendEmail: false));
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeInvitationAsync(Guid id, Guid invitationId, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct, tracked: false);
        goal.EnsureOwner(currentUser.UserId);
        var invitation = await db.SharedGoalInvitations
                             .SingleOrDefaultAsync(i => i.Id == invitationId && i.SharedGoalId == id, ct)
                         ?? throw new NotFoundException("Invitación");
        invitation.Respond(InvitationStatus.Revoked, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Pending invitations addressed to the caller's e-mail.</summary>
    public async Task<IReadOnlyList<MyInvitationDto>> MyInvitationsAsync(CancellationToken ct)
    {
        var email = await MyNormalizedEmailAsync(ct);
        var invitations = await db.SharedGoalInvitations.AsNoTracking()
            .Where(i => i.NormalizedEmail == email && i.Status == InvitationStatus.Pending)
            .Join(db.SharedGoals.Where(g => g.Status == SharedGoalStatus.Active), i => i.SharedGoalId, g => g.Id,
                (i, g) => new { i.Id, GoalId = g.Id, g.Name, i.InvitedByUserId, i.CreatedAt })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(invitations.Select(i => i.InvitedByUserId).Distinct().ToList(), ct);
        return invitations
            .Select(i => new MyInvitationDto(i.Id, i.GoalId, i.Name, names.GetValueOrDefault(i.InvitedByUserId, "Alguien"),
                i.CreatedAt))
            .ToList();
    }

    public async Task<Guid> RespondAsync(Guid invitationId, RespondInvitationRequest request, CancellationToken ct)
    {
        var email = await MyNormalizedEmailAsync(ct);
        var invitation = await db.SharedGoalInvitations
                             .SingleOrDefaultAsync(i => i.Id == invitationId && i.NormalizedEmail == email, ct)
                         ?? throw new NotFoundException("Invitación");
        var now = clock.UtcNow;
        invitation.Respond(request.Accept ? InvitationStatus.Accepted : InvitationStatus.Declined, now);
        if (request.Accept)
        {
            var goal = await db.SharedGoals.Include(g => g.Participants)
                           .SingleOrDefaultAsync(g => g.Id == invitation.SharedGoalId, ct)
                       ?? throw new NotFoundException("Objetivo compartido");
            goal.AddMember(currentUser.UserId, now);
        }

        await db.SaveChangesAsync(ct);
        return invitation.SharedGoalId;
    }

    public async Task LeaveAsync(Guid id, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        goal.RemoveMember(currentUser.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SharedGoalDto> RemoveParticipantAsync(Guid id, Guid participantId, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        goal.EnsureOwner(currentUser.UserId);
        var participant = goal.Participants.SingleOrDefault(p => p.Id == participantId && p.LeftAt == null)
                          ?? throw new NotFoundException("Participante");
        goal.RemoveMember(participant.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SharedGoalDto> TransferOwnershipAsync(Guid id, TransferOwnershipRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct);
        var successor = goal.TransferOwnership(currentUser.UserId, request.ParticipantId);
        var name = (await users.FindAsync(currentUser.UserId, ct))?.DisplayName ?? "Alguien";
        db.Notifications.Add(Notification.Create(successor.UserId, NotificationKind.SharedGoal,
            $"Ahora gestionas «{goal.Name}»", $"{name} te ha cedido la propiedad del objetivo compartido.",
            $"/compartidos/{goal.Id}", sendEmail: false));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Stages the hand-over of every shared goal the user owns before their account is deleted, without saving: the
    /// caller saves it together with the deletion. The first member to join becomes the owner and is notified; a goal
    /// with no other active member is deleted (nobody could open it again). The user's own participations and
    /// contributions go away with the account by cascade.
    /// </summary>
    public async Task PrepareAccountDeletionAsync(Guid userId, CancellationToken ct)
    {
        var owned = await db.SharedGoals.Include(g => g.Participants)
            .Where(g => g.Participants.Any(p => p.UserId == userId && p.LeftAt == null && p.Role == SharedGoalRole.Owner))
            .ToListAsync(ct);
        if (owned.Count == 0)
        {
            return;
        }

        var name = (await users.FindAsync(userId, ct))?.DisplayName ?? "La persona propietaria";
        foreach (var goal in owned)
        {
            if (goal.PromoteSuccessorOf(userId) is { } successor)
            {
                db.Notifications.Add(Notification.Create(successor.UserId, NotificationKind.SharedGoal,
                    $"Ahora gestionas «{goal.Name}»",
                    $"{name} ha eliminado su cuenta y la propiedad del objetivo compartido ha pasado a ti.",
                    $"/compartidos/{goal.Id}", sendEmail: false));
            }
            else
            {
                db.SharedGoals.Remove(goal);
            }
        }
    }

    public async Task<SharedGoalDto> AddContributionAsync(Guid id, ContributionRequest request, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct, tracked: false);
        var today = await clock.GetTodayAsync(ct);
        db.SharedGoalContributions.Add(SharedGoalContribution.Create(goal, currentUser.UserId, request.Date ?? today,
            request.Amount, request.Note, today));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Only the author can delete a contribution (404 for anyone else).</summary>
    public async Task<SharedGoalDto> DeleteContributionAsync(Guid id, Guid contributionId, CancellationToken ct)
    {
        var goal = await LoadAsync(id, ct, tracked: false);
        goal.EnsureActive();
        var contribution = await db.SharedGoalContributions.SingleOrDefaultAsync(
                               c => c.Id == contributionId && c.SharedGoalId == id && c.UserId == currentUser.UserId, ct)
                           ?? throw new NotFoundException("Aportación");
        db.SharedGoalContributions.Remove(contribution);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static ProgressValueDto Progress(decimal current, decimal target)
    {
        var p = GoalProgress.Compute(current, target);
        return new ProgressValueDto(p.Current, p.Target, p.Percent, p.ExceededBy, p.TargetReached);
    }

    private async Task<string> MyNormalizedEmailAsync(CancellationToken ct) =>
        SharedGoalInvitation.Normalize((await users.FindAsync(currentUser.UserId, ct))?.Email
                                       ?? throw new AuthenticationFailedException("Sesión no válida."));

    /// <summary>Loads a goal the caller actively participates in; anything else is 404.</summary>
    private async Task<SharedGoal> LoadAsync(Guid id, CancellationToken ct, bool tracked = true)
    {
        var me = currentUser.UserId;
        var query = db.SharedGoals.Include(g => g.Participants).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
                   g => g.Id == id && g.Participants.Any(p => p.UserId == me && p.LeftAt == null), ct)
               ?? throw new NotFoundException("Objetivo compartido");
    }
}
