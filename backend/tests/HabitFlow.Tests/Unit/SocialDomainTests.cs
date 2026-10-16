using HabitFlow.Domain.Common;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Social;

namespace HabitFlow.Tests.Unit;

public sealed class SocialDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private static Habit HabitOf(Guid userId, HabitType type = HabitType.Binary, string? unit = null) =>
        Habit.Create(userId, new HabitDetails("Leer", null, Guid.NewGuid(), HabitColor.Teal, "book-open", unit, null),
            type, Today.AddDays(-30), ScheduleSpec.Create(type, ScheduleKind.Daily), Now);

    private static Challenge Weekly(ChallengeRule rule = ChallengeRule.CompletionCount, decimal? target = 5, string? unit = null) =>
        Challenge.Create(Guid.NewGuid(), "Semana lectora", null, Today, Today.AddDays(6), rule, target, unit, Today);

    [Fact]
    public void Dates_are_validated()
    {
        Assert.Equal("challenge.start_in_past", Assert.Throws<DomainException>(() =>
            Challenge.Create(Guid.NewGuid(), "x", null, Today.AddDays(-1), Today, ChallengeRule.DailyCompletion, null, null, Today)).Code);
        Assert.Equal("challenge.invalid_dates", Assert.Throws<DomainException>(() =>
            Challenge.Create(Guid.NewGuid(), "x", null, Today, Today.AddDays(31), ChallengeRule.DailyCompletion, null, null, Today)).Code);
        Assert.Equal("challenge.unreachable_target", Assert.Throws<DomainException>(() => Weekly(target: 8)).Code);
    }

    [Fact]
    public void Daily_completion_target_is_the_number_of_days()
    {
        var challenge = Weekly(ChallengeRule.DailyCompletion, target: null);
        Assert.Equal(7, challenge.TargetValue);
        Assert.Equal(8, challenge.JoinCode.Length);
    }

    [Fact]
    public void Joining_requires_an_own_active_habit_that_fits_the_rule()
    {
        var user = Guid.NewGuid();
        var quantity = Weekly(ChallengeRule.QuantityTotal, 100, "páginas");

        Assert.Equal("challenge.invalid_habit",
            Assert.Throws<DomainException>(() => quantity.Join(user, HabitOf(Guid.NewGuid(), HabitType.Quantity, "páginas"), Today, Now)).Code);
        Assert.Equal("challenge.unit_mismatch",
            Assert.Throws<DomainException>(() => quantity.Join(user, HabitOf(user, HabitType.Quantity, "minutos"), Today, Now)).Code);

        quantity.Join(user, HabitOf(user, HabitType.Quantity, "Páginas"), Today, Now);
        Assert.Equal("challenge.already_joined",
            Assert.Throws<DomainException>(() => quantity.Join(user, HabitOf(user, HabitType.Quantity, "páginas"), Today, Now)).Code);
    }

    [Fact]
    public void Finished_challenges_reject_participants_and_results_are_final()
    {
        var challenge = Weekly();
        var user = Guid.NewGuid();
        var participant = challenge.Join(user, HabitOf(user), Today, Now);
        var afterEnd = Today.AddDays(7);

        Assert.Equal(ChallengePhase.Finished, challenge.PhaseOn(afterEnd));
        Assert.Throws<DomainException>(() => challenge.Join(Guid.NewGuid(), HabitOf(Guid.NewGuid()), afterEnd, Now));
        Assert.Equal(ChallengeResult.NotCompleted, challenge.ResultFor(participant, 4, afterEnd));
        Assert.Equal(ChallengeResult.Completed, challenge.ResultFor(participant, 5, Today));
        Assert.Equal(ChallengeResult.InProgress, challenge.ResultFor(participant, 4, Today));
        Assert.Throws<DomainException>(() => challenge.Leave(user, afterEnd));
    }

    [Fact]
    public void Leaving_freezes_progress_and_marks_the_participant_as_abandoned()
    {
        var challenge = Weekly();
        var user = Guid.NewGuid();
        var participant = challenge.Join(user, HabitOf(user), Today, Now);

        challenge.Leave(user, Today.AddDays(3));

        Assert.Equal(Today.AddDays(2), challenge.CountsUntil(participant));
        Assert.Equal(ChallengeResult.Abandoned, challenge.ResultFor(participant, 7, Today.AddDays(4)));
    }

    [Fact]
    public void Shared_goal_roles_and_membership()
    {
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var goal = SharedGoal.Create(owner, "Leer 50 libros entre todos", null, "libros", 50, 10, Today, null, Now);

        goal.AddMember(member, Now);
        Assert.Throws<DomainException>(() => goal.EnsureOwner(member));
        Assert.Throws<DomainException>(() => goal.RemoveMember(owner, Now));

        goal.RemoveMember(member, Now);
        Assert.Null(goal.ActiveParticipant(member));
        Assert.Throws<DomainException>(() =>
            SharedGoalContribution.Create(goal, member, Today, 1, null, Today));

        goal.AddMember(member, Now); // rejoining reuses the row
        Assert.Equal(2, goal.Participants.Count);

        goal.ChangeStatus(SharedGoalStatus.Completed);
        Assert.Throws<DomainException>(() => SharedGoalContribution.Create(goal, member, Today, 1, null, Today));
    }

    [Fact]
    public void Owner_can_hand_the_goal_over_to_an_active_member_only()
    {
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var former = Guid.NewGuid();
        var goal = SharedGoal.Create(owner, "Correr 100 km", null, "km", 100, null, Today, null, Now);
        goal.AddMember(member, Now);
        goal.AddMember(former, Now);
        goal.RemoveMember(former, Now);
        Guid IdOf(Guid user) => goal.Participants.Single(p => p.UserId == user).Id;

        Assert.Throws<DomainException>(() => goal.TransferOwnership(member, IdOf(owner)));
        Assert.Throws<DomainException>(() => goal.TransferOwnership(owner, IdOf(former)));
        Assert.Throws<DomainException>(() => goal.TransferOwnership(owner, IdOf(owner)));

        goal.TransferOwnership(owner, IdOf(member));
        goal.EnsureOwner(member);
        Assert.Equal(SharedGoalRole.Member, goal.ActiveParticipant(owner)!.Role);
        goal.RemoveMember(owner, Now); // the former owner can now leave
    }

    [Fact]
    public void Deleting_the_owner_promotes_the_longest_standing_active_member()
    {
        var owner = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var goal = SharedGoal.Create(owner, "Correr 100 km", null, "km", 100, null, Today, null, Now);
        goal.AddMember(first, Now.AddMinutes(1));
        goal.AddMember(second, Now.AddMinutes(2));

        Assert.Equal(first, goal.PromoteSuccessorOf(owner)!.UserId);
        goal.EnsureOwner(first);
        Assert.Equal(SharedGoalRole.Member, goal.ActiveParticipant(second)!.Role);

        // Members who already left are not candidates.
        var alone = SharedGoal.Create(owner, "Solo", null, "km", 1, null, Today, null, Now);
        var gone = Guid.NewGuid();
        alone.AddMember(gone, Now);
        alone.RemoveMember(gone, Now);
        Assert.Null(alone.PromoteSuccessorOf(owner));
    }

    [Fact]
    public void Challenge_rules_change_only_before_the_start_and_while_nobody_else_joined()
    {
        var creator = Guid.NewGuid();
        var (start, end) = (Today.AddDays(1), Today.AddDays(7));
        var challenge = Challenge.Create(creator, "Semana lectora", null, start, end, ChallengeRule.CompletionCount, 5, null, Today);
        var habit = HabitOf(creator);
        challenge.Join(creator, habit, Today, Now);
        Assert.True(challenge.RulesEditableOn(Today));

        // The creator's own linked habit must still fit; a rejected edit changes nothing.
        Assert.Equal("challenge.unit_mismatch", Assert.Throws<DomainException>(() => challenge.Update(
            "Otro", null, start, end, ChallengeRule.QuantityTotal, 100, "páginas", Today, habit)).Code);
        Assert.Equal((ChallengeRule.CompletionCount, "Semana lectora"), (challenge.Rule, challenge.Name));
        Assert.Equal("challenge.start_in_past", Assert.Throws<DomainException>(() => challenge.Update(
            "Otro", null, Today.AddDays(-1), end, ChallengeRule.CompletionCount, 5, null, Today, habit)).Code);

        challenge.Update("Tres días seguidos", "Sin fallar", Today.AddDays(2), Today.AddDays(4),
            ChallengeRule.DailyCompletion, null, null, Today, habit);
        Assert.Equal((3m, "Tres días seguidos", "Sin fallar"), (challenge.TargetValue, challenge.Name, challenge.Description));

        var other = Guid.NewGuid();
        challenge.Join(other, HabitOf(other), Today, Now);
        Assert.False(challenge.RulesEditableOn(Today));
        Assert.Equal("challenge.rules_locked", Assert.Throws<DomainException>(() => challenge.Update(
            "Tres días seguidos", null, Today.AddDays(2), Today.AddDays(5), ChallengeRule.DailyCompletion, null, null, Today, habit)).Code);

        // Same rules (the daily target is derived, so whatever is sent for it is ignored): only the texts change.
        challenge.Update("Nuevo nombre", null, Today.AddDays(2), Today.AddDays(4), ChallengeRule.DailyCompletion, 99, null, Today, habit);
        Assert.Equal(("Nuevo nombre", (string?)null), (challenge.Name, challenge.Description));

        Assert.Equal("challenge.finished", Assert.Throws<DomainException>(() => challenge.Update(
            "Tarde", null, Today.AddDays(2), Today.AddDays(4), ChallengeRule.DailyCompletion, null, null, Today.AddDays(5), habit)).Code);
    }

    [Fact]
    public void Started_challenges_keep_their_rules_even_without_other_participants()
    {
        var challenge = Weekly();
        Assert.False(challenge.RulesEditableOn(Today));
        Assert.Equal("challenge.rules_locked", Assert.Throws<DomainException>(() => challenge.Update(
            "Semana lectora", null, Today, Today.AddDays(6), ChallengeRule.CompletionCount, 6, null, Today, null)).Code);
        challenge.Update("Semana lectora II", null, Today, Today.AddDays(6), ChallengeRule.CompletionCount, 5.00m, null, Today, null);
        Assert.Equal("Semana lectora II", challenge.Name);
    }
}
