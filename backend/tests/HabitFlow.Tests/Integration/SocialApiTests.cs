using System.Net;
using HabitFlow.Application.Auth;
using HabitFlow.Application.Common;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Reminders;
using HabitFlow.Application.Social;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Notifications;
using HabitFlow.Domain.Social;
using HabitFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class SocialApiTests(ApiFactory factory)
{
    private async Task<ChallengeDto> CreateChallengeAsync(TestUser user, Guid? habitId, ChallengeRule rule = ChallengeRule.CompletionCount,
        decimal? target = 3, string? unit = null)
    {
        var today = factory.Today();
        var response = await user.Client.PostJsonAsync("/api/v1/challenges", new CreateChallengeRequest(
            "Semana constante", "Tres veces esta semana", today, today.AddDays(6), rule, target, unit, habitId));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return await response.ReadAsync<ChallengeDto>();
    }

    [Fact]
    public async Task Challenge_progress_comes_from_each_participants_own_logs_and_hides_their_habits()
    {
        var today = factory.Today();
        var ana = await factory.RegisterUserAsync();
        var leo = await factory.RegisterUserAsync();
        var anaHabit = await ana.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today, name: "Correr secreto"));
        var leoHabit = await leo.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today, name: "Nadar privado"));
        await ana.Client.LogAsync(anaHabit.Id, today, note: "nota privada");

        var challenge = await CreateChallengeAsync(ana, anaHabit.Id);

        var preview = await leo.Client.GetAsync($"/api/v1/challenges/code/{challenge.JoinCode.ToLowerInvariant()}")
            .OkAsync<ChallengePreviewDto>();
        Assert.Equal((1, false), (preview.Participants, preview.AlreadyJoined));

        var joined = await leo.Client.PostJsonAsync("/api/v1/challenges/join", new JoinChallengeRequest(challenge.JoinCode, leoHabit.Id))
            .OkAsync<ChallengeDto>();
        Assert.Equal(2, joined.Standings.Count);
        Assert.Equal("Nadar privado", joined.MyHabit!.Name);

        var raw = await (await leo.Client.GetAsync($"/api/v1/challenges/{challenge.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Correr secreto", raw);
        Assert.DoesNotContain("nota privada", raw);
        Assert.DoesNotContain(anaHabit.Id.ToString(), raw);

        var view = await leo.Client.GetAsync($"/api/v1/challenges/{challenge.Id}").OkAsync<ChallengeDto>();
        var anaStanding = view.Standings.Single(s => !s.IsMe);
        Assert.Equal((1m, 33.3m, ChallengeResult.InProgress), (anaStanding.Progress, anaStanding.Percent, anaStanding.Result));
        Assert.Equal(0m, view.Standings.Single(s => s.IsMe).Progress);

        // Leaving freezes the result as abandoned.
        var left = await leo.Client.PostAsync($"/api/v1/challenges/{challenge.Id}/leave", null).OkAsync<ChallengeDto>();
        Assert.Equal(ChallengeResult.Abandoned, left.Standings.Single(s => s.IsMe).Result);
        await leo.Client.LogAsync(leoHabit.Id, today);
        var after = await leo.Client.GetAsync($"/api/v1/challenges/{challenge.Id}").OkAsync<ChallengeDto>();
        Assert.Equal(0m, after.Standings.Single(s => s.IsMe).Progress);

        // A participant cannot rejoin, and the creator can no longer delete a challenge others joined.
        var rejoin = await leo.Client.PostJsonAsync("/api/v1/challenges/join", new JoinChallengeRequest(challenge.JoinCode, leoHabit.Id));
        Assert.Equal("challenge.already_joined", await rejoin.ProblemCodeAsync());
        var delete = await ana.Client.DeleteAsync($"/api/v1/challenges/{challenge.Id}");
        Assert.Equal("challenge.has_participants", await delete.ProblemCodeAsync());
    }

    [Fact]
    public async Task Challenge_rules_are_validated_and_outsiders_see_nothing()
    {
        var today = factory.Today();
        var ana = await factory.RegisterUserAsync();
        var outsider = await factory.RegisterUserAsync();
        var reading = await ana.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today));
        var outsiderHabit = await outsider.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today) with { Unit = "minutos" });

        var challenge = await CreateChallengeAsync(ana, reading.Id, ChallengeRule.QuantityTotal, 100, "páginas");
        await ana.Client.LogAsync(reading.Id, today, completed: null, quantity: 40);
        var mine = await ana.Client.GetAsync($"/api/v1/challenges/{challenge.Id}").OkAsync<ChallengeDto>();
        Assert.Equal(40m, mine.Standings.Single().Progress);

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/v1/challenges/{challenge.Id}")).StatusCode);
        Assert.Empty(await outsider.Client.GetAsync("/api/v1/challenges").OkAsync<List<ChallengeSummaryDto>>());

        var wrongUnit = await outsider.Client.PostJsonAsync("/api/v1/challenges/join", new JoinChallengeRequest(challenge.JoinCode, outsiderHabit.Id));
        Assert.Equal("challenge.unit_mismatch", await wrongUnit.ProblemCodeAsync());
        var foreignHabit = await outsider.Client.PostJsonAsync("/api/v1/challenges/join", new JoinChallengeRequest(challenge.JoinCode, reading.Id));
        Assert.Equal("challenge.invalid_habit", await foreignHabit.ProblemCodeAsync());

        var past = await ana.Client.PostJsonAsync("/api/v1/challenges", new CreateChallengeRequest(
            "x", null, today.AddDays(-1), today, ChallengeRule.DailyCompletion, null, null, null));
        Assert.Equal("challenge.start_in_past", await past.ProblemCodeAsync());

        var solo = await CreateChallengeAsync(ana, null, ChallengeRule.DailyCompletion, null);
        await (await ana.Client.DeleteAsync($"/api/v1/challenges/{solo.Id}")).EnsureStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Shared_goal_invitation_contributions_and_privacy()
    {
        var owner = await factory.RegisterUserAsync();
        var member = await factory.RegisterUserAsync();
        var created = await owner.Client.PostJsonAsync("/api/v1/shared-goals", new SaveSharedGoalRequest(
            "Leer 20 libros en familia", null, "libros", 20, 10, null, null, null));
        await created.EnsureStatusAsync(HttpStatusCode.Created);
        var goal = await created.ReadAsync<SharedGoalDto>();

        // Same answer for existing and unknown e-mails.
        var known = await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations", new InviteRequest(member.Profile.Email));
        var unknown = await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations", new InviteRequest("nadie@test.local"));
        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (known.StatusCode, unknown.StatusCode));

        var notifications = await member.Client.GetAsync("/api/v1/notifications").OkAsync<PagedResult<NotificationDto>>();
        Assert.Equal(NotificationKind.Invitation, Assert.Single(notifications.Items).Kind);
        Assert.Equal(HttpStatusCode.NotFound, (await member.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}")).StatusCode);

        var invitation = Assert.Single(await member.Client.GetAsync("/api/v1/shared-goals/invitations").OkAsync<List<MyInvitationDto>>());
        Assert.Equal("Leer 20 libros en familia", invitation.GoalName);
        await member.Client.PostJsonAsync($"/api/v1/shared-goals/invitations/{invitation.Id}/respond", new RespondInvitationRequest(true))
            .OkAsync<object>();

        await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/contributions", new ContributionRequest(null, 3, "nota del dueño"))
            .OkAsync<SharedGoalDto>();
        var view = await member.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/contributions", new ContributionRequest(null, 2, null))
            .OkAsync<SharedGoalDto>();

        Assert.Equal((5m, 25m), (view.Collective.Current, view.Collective.Percent));
        Assert.Equal(SharedGoalRole.Member, view.MyRole);
        Assert.Equal(2m, view.Participants.Single(p => p.IsMe).Total);
        Assert.Equal(30m, view.Participants.Single(p => !p.IsMe).Individual!.Percent);
        Assert.Equal(2m, Assert.Single(view.MyContributions).Amount);
        Assert.Empty(view.PendingInvitations); // owner-only information
        var raw = await (await member.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("nota del dueño", raw);
        Assert.DoesNotContain("nadie@test.local", raw, StringComparison.OrdinalIgnoreCase);

        // Members cannot act as owners or touch other people's contributions.
        var ownerView = await owner.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}").OkAsync<SharedGoalDto>();
        var ownerContribution = Assert.Single(ownerView.MyContributions);
        Assert.Equal("nota del dueño", ownerContribution.Note);
        Assert.Equal("nadie@test.local", Assert.Single(ownerView.PendingInvitations).Email);
        Assert.Equal("shared_goal.owner_only", await (await member.Client.PutJsonAsync($"/api/v1/shared-goals/{goal.Id}",
            new SaveSharedGoalRequest("Hackeado", null, "libros", 1, null, null, null, null))).ProblemCodeAsync());
        Assert.Equal("shared_goal.owner_only", await (await member.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations",
            new InviteRequest("otro@test.local"))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound,
            (await member.Client.DeleteAsync($"/api/v1/shared-goals/{goal.Id}/contributions/{ownerContribution.Id}")).StatusCode);
        var ownerParticipant = ownerView.Participants.Single(p => p.IsMe);
        Assert.Equal("shared_goal.owner_only", await (await member.Client.DeleteAsync(
            $"/api/v1/shared-goals/{goal.Id}/participants/{ownerParticipant.ParticipantId}")).ProblemCodeAsync());

        // Leaving removes access; past contributions still count for the group.
        await (await member.Client.PostAsync($"/api/v1/shared-goals/{goal.Id}/leave", null)).EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await member.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}")).StatusCode);
        var afterLeave = await owner.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}").OkAsync<SharedGoalDto>();
        Assert.Equal(5m, afterLeave.Collective.Current);
        Assert.False(afterLeave.Participants.Single(p => !p.IsMe).Active);
    }

    [Fact]
    public async Task Invitations_can_be_declined_or_revoked_and_closed_goals_reject_contributions()
    {
        var owner = await factory.RegisterUserAsync();
        var guest = await factory.RegisterUserAsync();
        var goal = await (await owner.Client.PostJsonAsync("/api/v1/shared-goals", new SaveSharedGoalRequest(
            "Correr 100 km", null, "km", 100, null, null, null, null))).ReadAsync<SharedGoalDto>();

        await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations", new InviteRequest(guest.Profile.Email));
        var invitation = Assert.Single(await guest.Client.GetAsync("/api/v1/shared-goals/invitations").OkAsync<List<MyInvitationDto>>());
        await guest.Client.PostJsonAsync($"/api/v1/shared-goals/invitations/{invitation.Id}/respond", new RespondInvitationRequest(false))
            .OkAsync<object>();
        Assert.Empty(await guest.Client.GetAsync("/api/v1/shared-goals/invitations").OkAsync<List<MyInvitationDto>>());

        await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations", new InviteRequest(guest.Profile.Email));
        var pending = (await owner.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}").OkAsync<SharedGoalDto>()).PendingInvitations.Single();
        await (await owner.Client.DeleteAsync($"/api/v1/shared-goals/{goal.Id}/invitations/{pending.Id}")).EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Empty(await guest.Client.GetAsync("/api/v1/shared-goals/invitations").OkAsync<List<MyInvitationDto>>());

        // Another user cannot answer someone else's invitation.
        await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations", new InviteRequest(guest.Profile.Email));
        var fresh = Assert.Single(await guest.Client.GetAsync("/api/v1/shared-goals/invitations").OkAsync<List<MyInvitationDto>>());
        var stranger = await factory.RegisterUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostJsonAsync(
            $"/api/v1/shared-goals/invitations/{fresh.Id}/respond", new RespondInvitationRequest(true))).StatusCode);

        await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/status", new ChangeSharedGoalStatusRequest(SharedGoalStatus.Completed))
            .OkAsync<SharedGoalDto>();
        var closed = await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/contributions", new ContributionRequest(null, 5, null));
        Assert.Equal("shared_goal.closed", await closed.ProblemCodeAsync());
    }

    [Fact]
    public async Task Creator_edits_a_challenge_and_rules_lock_once_someone_joins()
    {
        var today = factory.Today();
        var ana = await factory.RegisterUserAsync();
        var leo = await factory.RegisterUserAsync();
        var anaHabit = await ana.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today));
        var leoHabit = await leo.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today));
        var created = await (await ana.Client.PostJsonAsync("/api/v1/challenges", new CreateChallengeRequest(
            "Semana constante", null, today.AddDays(1), today.AddDays(7), ChallengeRule.CompletionCount, 3, null, anaHabit.Id)))
            .ReadAsync<ChallengeDto>();
        Assert.True(created.CanEditRules);

        var edited = await ana.Client.PutJsonAsync($"/api/v1/challenges/{created.Id}", new UpdateChallengeRequest(
            "Cinco de siete", "Más exigente", today.AddDays(2), today.AddDays(8), ChallengeRule.CompletionCount, 5, null))
            .OkAsync<ChallengeDto>();
        Assert.Equal(("Cinco de siete", 5m, today.AddDays(2)), (edited.Name, edited.TargetValue, edited.StartDate));

        await leo.Client.PostJsonAsync("/api/v1/challenges/join", new JoinChallengeRequest(created.JoinCode, leoHabit.Id))
            .OkAsync<ChallengeDto>();
        var forLeo = await leo.Client.GetAsync($"/api/v1/challenges/{created.Id}").OkAsync<ChallengeDto>();
        Assert.False(forLeo.CanEditRules);
        Assert.Equal("challenge.creator_only", await (await leo.Client.PutJsonAsync($"/api/v1/challenges/{created.Id}",
            new UpdateChallengeRequest("Mío", null, today.AddDays(2), today.AddDays(8), ChallengeRule.CompletionCount, 5, null)))
            .ProblemCodeAsync());

        Assert.False((await ana.Client.GetAsync($"/api/v1/challenges/{created.Id}").OkAsync<ChallengeDto>()).CanEditRules);
        Assert.Equal("challenge.rules_locked", await (await ana.Client.PutJsonAsync($"/api/v1/challenges/{created.Id}",
            new UpdateChallengeRequest("Cinco de siete", null, today.AddDays(2), today.AddDays(8), ChallengeRule.CompletionCount, 2, null)))
            .ProblemCodeAsync());
        var renamed = await ana.Client.PutJsonAsync($"/api/v1/challenges/{created.Id}", new UpdateChallengeRequest(
            "Semana de cinco", null, today.AddDays(2), today.AddDays(8), ChallengeRule.CompletionCount, 5, null)).OkAsync<ChallengeDto>();
        Assert.Equal(("Semana de cinco", (string?)null, 5m), (renamed.Name, renamed.Description, renamed.TargetValue));

        var outsider = await factory.RegisterUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.PutJsonAsync($"/api/v1/challenges/{created.Id}",
            new UpdateChallengeRequest("x", null, today.AddDays(2), today.AddDays(8), ChallengeRule.CompletionCount, 5, null))).StatusCode);
    }

    private async Task<SharedGoalDto> CreateSharedGoalWithAsync(TestUser owner, string name, params TestUser[] members)
    {
        var goal = await (await owner.Client.PostJsonAsync("/api/v1/shared-goals", new SaveSharedGoalRequest(
            name, null, "km", 100, null, null, null, null))).ReadAsync<SharedGoalDto>();
        foreach (var member in members)
        {
            await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/invitations", new InviteRequest(member.Profile.Email));
            var invitation = (await member.Client.GetAsync("/api/v1/shared-goals/invitations").OkAsync<List<MyInvitationDto>>())
                .Single(i => i.SharedGoalId == goal.Id);
            await member.Client.PostJsonAsync($"/api/v1/shared-goals/invitations/{invitation.Id}/respond", new RespondInvitationRequest(true))
                .OkAsync<object>();
        }

        return await owner.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}").OkAsync<SharedGoalDto>();
    }

    [Fact]
    public async Task Owner_can_transfer_ownership_and_then_leave()
    {
        var owner = await factory.RegisterUserAsync();
        var member = await factory.RegisterUserAsync();
        var goal = await CreateSharedGoalWithAsync(owner, "Correr 100 km", member);
        var memberParticipant = goal.Participants.Single(p => !p.IsMe);

        Assert.Equal("shared_goal.owner_only", await (await member.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/owner",
            new TransferOwnershipRequest(memberParticipant.ParticipantId))).ProblemCodeAsync());

        var after = await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{goal.Id}/owner",
            new TransferOwnershipRequest(memberParticipant.ParticipantId)).OkAsync<SharedGoalDto>();
        Assert.Equal(SharedGoalRole.Member, after.MyRole);
        Assert.Equal(SharedGoalRole.Owner, after.Participants.Single(p => !p.IsMe).Role);
        Assert.Empty(after.PendingInvitations);

        var notifications = await member.Client.GetAsync("/api/v1/notifications").OkAsync<PagedResult<NotificationDto>>();
        Assert.Contains(notifications.Items, n => n.Kind == NotificationKind.SharedGoal && n.Link == $"/compartidos/{goal.Id}");
        var memberView = await member.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}").OkAsync<SharedGoalDto>();
        Assert.Equal(SharedGoalRole.Owner, memberView.MyRole);

        await (await owner.Client.PostAsync($"/api/v1/shared-goals/{goal.Id}/leave", null)).EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/v1/shared-goals/{goal.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_owners_account_hands_goals_over_or_deletes_empty_ones()
    {
        var owner = await factory.RegisterUserAsync();
        var first = await factory.RegisterUserAsync();
        var second = await factory.RegisterUserAsync();
        var shared = await CreateSharedGoalWithAsync(owner, "Leer 30 libros", first, second);
        var solo = await CreateSharedGoalWithAsync(owner, "Objetivo sin miembros");
        await first.Client.PostJsonAsync($"/api/v1/shared-goals/{shared.Id}/contributions", new ContributionRequest(null, 4, null))
            .OkAsync<SharedGoalDto>();
        await owner.Client.PostJsonAsync($"/api/v1/shared-goals/{shared.Id}/contributions", new ContributionRequest(null, 6, null))
            .OkAsync<SharedGoalDto>();

        await (await owner.Client.DeleteJsonAsync("/api/v1/users/me", new DeleteAccountRequest(owner.Password)))
            .EnsureStatusAsync(HttpStatusCode.NoContent);

        var view = await first.Client.GetAsync($"/api/v1/shared-goals/{shared.Id}").OkAsync<SharedGoalDto>();
        Assert.Equal(SharedGoalRole.Owner, view.MyRole);
        Assert.Equal(2, view.Participants.Count); // the deleted account and its contributions are gone
        Assert.Equal(4m, view.Collective.Current);
        var secondView = await second.Client.GetAsync($"/api/v1/shared-goals/{shared.Id}").OkAsync<SharedGoalDto>();
        Assert.Equal(SharedGoalRole.Member, secondView.MyRole);
        var notifications = await first.Client.GetAsync("/api/v1/notifications").OkAsync<PagedResult<NotificationDto>>();
        Assert.Contains(notifications.Items, n => n.Kind == NotificationKind.SharedGoal);

        // The new owner can manage it.
        await first.Client.PutJsonAsync($"/api/v1/shared-goals/{shared.Id}", new SaveSharedGoalRequest(
            "Leer 25 libros", null, "libros", 25, null, null, null, view.Version)).OkAsync<SharedGoalDto>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.SharedGoals.AnyAsync(g => g.Id == solo.Id));
    }
}
