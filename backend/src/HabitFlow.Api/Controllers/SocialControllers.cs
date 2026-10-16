using HabitFlow.Application.Social;
using Microsoft.AspNetCore.Mvc;

namespace HabitFlow.Api.Controllers;

/// <summary>
/// Weekly (up to 31-day) challenges joined with a code. Progress comes only from each participant's own habit;
/// other participants see names and numbers, never habits or notes.
/// </summary>
[ApiController]
[Route("api/v1/challenges")]
[Produces("application/json")]
public sealed class ChallengesController(ChallengeService challenges) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ChallengeSummaryDto>> List(CancellationToken ct) => challenges.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<ChallengeDto> Get(Guid id, CancellationToken ct) => challenges.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<ChallengeDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateChallengeRequest request, CancellationToken ct)
    {
        var challenge = await challenges.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = challenge.Id }, challenge);
    }

    /// <summary>What a code leads to, before joining.</summary>
    [HttpGet("code/{code}")]
    public Task<ChallengePreviewDto> Preview(string code, CancellationToken ct) => challenges.PreviewAsync(code, ct);

    [HttpPost("join")]
    public Task<ChallengeDto> Join(JoinChallengeRequest request, CancellationToken ct) => challenges.JoinAsync(request, ct);

    /// <summary>Leaves the challenge; the result becomes "abandoned" and progress stops counting.</summary>
    [HttpPost("{id:guid}/leave")]
    public Task<ChallengeDto> Leave(Guid id, CancellationToken ct) => challenges.LeaveAsync(id, ct);

    /// <summary>
    /// Creator only. Name and description until the end; dates, rule, target and unit only before the start and while
    /// nobody else has joined (<c>canEditRules</c>).
    /// </summary>
    [HttpPut("{id:guid}")]
    public Task<ChallengeDto> Update(Guid id, UpdateChallengeRequest request, CancellationToken ct) =>
        challenges.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await challenges.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Goals shared by invitation, with owner/member permissions and individual vs. collective progress.</summary>
[ApiController]
[Route("api/v1/shared-goals")]
[Produces("application/json")]
public sealed class SharedGoalsController(SharedGoalService sharedGoals) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SharedGoalSummaryDto>> List(CancellationToken ct) => sharedGoals.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<SharedGoalDto> Get(Guid id, CancellationToken ct) => sharedGoals.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<SharedGoalDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveSharedGoalRequest request, CancellationToken ct)
    {
        var goal = await sharedGoals.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = goal.Id }, goal);
    }

    [HttpPut("{id:guid}")]
    public Task<SharedGoalDto> Update(Guid id, SaveSharedGoalRequest request, CancellationToken ct) =>
        sharedGoals.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/status")]
    public Task<SharedGoalDto> ChangeStatus(Guid id, ChangeSharedGoalStatusRequest request, CancellationToken ct) =>
        sharedGoals.ChangeStatusAsync(id, request, ct);

    /// <summary>Always 202: the response never reveals whether the e-mail has an account.</summary>
    [HttpPost("{id:guid}/invitations")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Invite(Guid id, InviteRequest request, CancellationToken ct)
    {
        await sharedGoals.InviteAsync(id, request, ct);
        return Accepted();
    }

    [HttpDelete("{id:guid}/invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, Guid invitationId, CancellationToken ct)
    {
        await sharedGoals.RevokeInvitationAsync(id, invitationId, ct);
        return NoContent();
    }

    /// <summary>Invitations addressed to the signed-in user's e-mail.</summary>
    [HttpGet("invitations")]
    public Task<IReadOnlyList<MyInvitationDto>> MyInvitations(CancellationToken ct) => sharedGoals.MyInvitationsAsync(ct);

    [HttpPost("invitations/{invitationId:guid}/respond")]
    public async Task<object> Respond(Guid invitationId, RespondInvitationRequest request, CancellationToken ct) =>
        new { sharedGoalId = await sharedGoals.RespondAsync(invitationId, request, ct) };

    [HttpPost("{id:guid}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        await sharedGoals.LeaveAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/participants/{participantId:guid}")]
    public Task<SharedGoalDto> RemoveParticipant(Guid id, Guid participantId, CancellationToken ct) =>
        sharedGoals.RemoveParticipantAsync(id, participantId, ct);

    /// <summary>The owner hands the goal over to another active member and stays as a member.</summary>
    [HttpPost("{id:guid}/owner")]
    public Task<SharedGoalDto> TransferOwnership(Guid id, TransferOwnershipRequest request, CancellationToken ct) =>
        sharedGoals.TransferOwnershipAsync(id, request, ct);

    [HttpPost("{id:guid}/contributions")]
    public Task<SharedGoalDto> Contribute(Guid id, ContributionRequest request, CancellationToken ct) =>
        sharedGoals.AddContributionAsync(id, request, ct);

    [HttpDelete("{id:guid}/contributions/{contributionId:guid}")]
    public Task<SharedGoalDto> DeleteContribution(Guid id, Guid contributionId, CancellationToken ct) =>
        sharedGoals.DeleteContributionAsync(id, contributionId, ct);
}
