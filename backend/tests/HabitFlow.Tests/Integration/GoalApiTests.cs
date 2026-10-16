using System.Net;
using HabitFlow.Application.Goals;
using HabitFlow.Application.Habits;
using HabitFlow.Domain.Goals;
using HabitFlow.Domain.Habits;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class GoalApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Manual_goal_progress_caps_the_percentage_and_reports_the_surplus()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var goal = await CreateAsync(user, new CreateGoalRequest("Leer 12 libros", null, GoalMetric.Manual, 12, "libros",
            today.AddDays(-10), today.AddDays(300), null));

        var afterOne = await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries",
            new AddGoalEntryRequest(today.AddDays(-1), 3, "Trilogía")).OkAsync<GoalDto>();
        Assert.Equal((3m, 25m, false), (afterOne.Progress.Current, afterOne.Progress.Percent, afterOne.Progress.TargetReached));

        var over = await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries",
            new AddGoalEntryRequest(null, 10, null)).OkAsync<GoalDto>();
        Assert.Equal((13m, 100m, 1m, true), (over.Progress.Current, over.Progress.Percent, over.Progress.ExceededBy,
            over.Progress.TargetReached));
        Assert.Equal(GoalStatus.Active, over.Status); // reaching the target does not complete it implicitly

        var entries = await user.Client.GetAsync($"/api/v1/goals/{goal.Id}/entries").OkAsync<List<GoalEntryDto>>();
        var undone = await user.Client.DeleteAsync($"/api/v1/goals/{goal.Id}/entries/{entries[0].Id}").OkAsync<GoalDto>();
        Assert.Equal(3m, undone.Progress.Current);

        var history = await user.Client.GetAsync($"/api/v1/goals/{goal.Id}/history").OkAsync<List<GoalHistoryPointDto>>();
        Assert.Equal(11, history.Count);
        Assert.Equal(3m, history[^1].Value);
    }

    [Fact]
    public async Task Entries_are_validated_against_the_goal_state_and_dates()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var goal = await CreateAsync(user, new CreateGoalRequest("Ahorrar", null, GoalMetric.Manual, 1000, "€", today, null, null));

        var future = await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries", new AddGoalEntryRequest(today.AddDays(1), 5, null));
        Assert.Equal("goal.entry_date_out_of_range", await future.ProblemCodeAsync());

        await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/status", new ChangeGoalStatusRequest(GoalStatus.Paused))
            .OkAsync<GoalDto>();
        var paused = await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries", new AddGoalEntryRequest(null, 5, null));
        Assert.Equal("goal.not_active", await paused.ProblemCodeAsync());

        var zero = await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries", new AddGoalEntryRequest(null, 0, null));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task Status_transitions_follow_the_rules()
    {
        var user = await factory.RegisterUserAsync();
        var goal = await CreateAsync(user, new CreateGoalRequest("Meta", null, GoalMetric.Manual, 5, "veces", null, null, null));
        async Task<HttpResponseMessage> Move(GoalStatus status) =>
            await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/status", new ChangeGoalStatusRequest(status));

        await (await Move(GoalStatus.Paused)).EnsureStatusAsync(HttpStatusCode.OK);
        await (await Move(GoalStatus.Active)).EnsureStatusAsync(HttpStatusCode.OK);
        var completed = await (await Move(GoalStatus.Completed)).ReadAsync<GoalDto>();
        Assert.NotNull(completed.CompletedAt);
        await (await Move(GoalStatus.Active)).EnsureStatusAsync(HttpStatusCode.OK); // reopen
        await (await Move(GoalStatus.Cancelled)).EnsureStatusAsync(HttpStatusCode.OK);

        var invalid = await Move(GoalStatus.Active);
        Assert.Equal("goal.invalid_transition", await invalid.ProblemCodeAsync());
        var edit = await user.Client.PutJsonAsync($"/api/v1/goals/{goal.Id}",
            new UpdateGoalRequest("x", null, 5, null, null, completed.Version));
        Assert.True(edit.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Habit_completion_goal_counts_only_inside_the_window()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-10)));
        foreach (var offset in new[] { -10, -5, -4, -1 })
        {
            await user.Client.LogAsync(habit.Id, today.AddDays(offset));
        }

        var goal = await CreateAsync(user, new CreateGoalRequest("Entrenar 12 veces", null, GoalMetric.HabitCompletions,
            12, null, today.AddDays(-5), today.AddDays(-2), [habit.Id]));

        Assert.Equal("veces", goal.Unit);
        Assert.Equal(2m, goal.Progress.Current); // -5 and -4; -10 is before the start, -1 after the deadline
        Assert.True(goal.IsOverdue);
    }

    [Fact]
    public async Task Habit_quantity_goal_requires_matching_units_and_sums_logs()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var reading = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today.AddDays(-3)));
        var minutes = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today.AddDays(-3), name: "Estudiar")
            with { Unit = "minutos" });
        var binary = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today));
        await user.Client.LogAsync(reading.Id, today.AddDays(-1), completed: null, quantity: 30.5m);
        await user.Client.LogAsync(reading.Id, today, completed: null, quantity: 12);

        var mixed = await user.Client.PostJsonAsync("/api/v1/goals", new CreateGoalRequest("x", null, GoalMetric.HabitQuantity,
            100, null, today.AddDays(-3), null, [reading.Id, minutes.Id]));
        Assert.Equal("goal.mixed_units", await mixed.ProblemCodeAsync());
        var notQuantity = await user.Client.PostJsonAsync("/api/v1/goals", new CreateGoalRequest("x", null,
            GoalMetric.HabitQuantity, 100, null, null, null, [binary.Id]));
        Assert.Equal("goal.quantity_habits_only", await notQuantity.ProblemCodeAsync());

        var goal = await CreateAsync(user, new CreateGoalRequest("Leer 500 páginas", null, GoalMetric.HabitQuantity, 500,
            null, today.AddDays(-3), null, [reading.Id]));
        Assert.Equal(("páginas", 42.5m), (goal.Unit, goal.Progress.Current));
    }

    [Fact]
    public async Task Streak_goal_uses_the_longest_run_in_the_window()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-8)));
        foreach (var offset in new[] { -8, -7, -6, -4, -3 })
        {
            await user.Client.LogAsync(habit.Id, today.AddDays(offset));
        }

        var goal = await CreateAsync(user, new CreateGoalRequest("Meditar 30 días seguidos", null, GoalMetric.HabitStreak,
            30, null, today.AddDays(-7), null, [habit.Id]));

        Assert.Equal(("días", 2m), (goal.Unit, goal.Progress.Current)); // -7,-6 inside the window (the -8 is outside)
        var single = await user.Client.PostJsonAsync("/api/v1/goals", new CreateGoalRequest("x", null, GoalMetric.HabitStreak,
            5, null, null, null, [habit.Id, Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, single.StatusCode);
    }

    [Fact]
    public async Task Goals_cannot_link_habits_of_other_users_and_are_private()
    {
        var owner = await factory.RegisterUserAsync();
        var other = await factory.RegisterUserAsync();
        var foreignHabit = await owner.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()));
        var goal = await CreateAsync(owner, new CreateGoalRequest("Privado", null, GoalMetric.Manual, 5, "x", null, null, null));

        var link = await other.Client.PostJsonAsync("/api/v1/goals", new CreateGoalRequest("x", null, GoalMetric.HabitCompletions,
            5, null, null, null, [foreignHabit.Id]));
        Assert.Equal("goal.invalid_habit", await link.ProblemCodeAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/goals/{goal.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/goals/{goal.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries", new AddGoalEntryRequest(null, 1, null))).StatusCode);
        Assert.Empty(await other.Client.GetAsync("/api/v1/goals?status=all").OkAsync<List<GoalDto>>());
    }

    [Fact]
    public async Task Updating_the_target_recomputes_the_percentage()
    {
        var user = await factory.RegisterUserAsync();
        var goal = await CreateAsync(user, new CreateGoalRequest("Correr", null, GoalMetric.Manual, 100, "km", null, null, null));
        await user.Client.PostJsonAsync($"/api/v1/goals/{goal.Id}/entries", new AddGoalEntryRequest(null, 25, null)).OkAsync<GoalDto>();
        var current = await user.Client.GetAsync($"/api/v1/goals/{goal.Id}").OkAsync<GoalDto>();

        var updated = await user.Client.PutJsonAsync($"/api/v1/goals/{goal.Id}",
            new UpdateGoalRequest("Correr", null, 50, "km", null, current.Version)).OkAsync<GoalDto>();

        Assert.Equal(50m, updated.Progress.Percent);
    }

    private static async Task<GoalDto> CreateAsync(TestUser user, CreateGoalRequest request)
    {
        var response = await user.Client.PostJsonAsync("/api/v1/goals", request);
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return await response.ReadAsync<GoalDto>();
    }
}
