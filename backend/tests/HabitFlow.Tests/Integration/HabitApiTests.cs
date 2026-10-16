using System.Net;
using HabitFlow.Application.Categories;
using HabitFlow.Application.Common;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class HabitApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Create_get_and_list_a_habit()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();

        var created = await user.Client.CreateHabitAsync(new CreateHabitRequest(
            "Entrenar", "Fuerza", TestHelpers.LearningCategory, HabitColor.Orange, "dumbbell", HabitType.Binary, null,
            today, null, new ScheduleInput(ScheduleKind.Weekdays, [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday])));

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], created.Schedule.Weekdays);
        Assert.Equal("Aprendizaje", created.Category.Name);
        Assert.Equal("Europe/Madrid", created.EffectiveTimeZoneId);
        Assert.Single(created.ScheduleHistory);

        var list = await user.Client.GetAsync("/api/v1/habits").OkAsync<PagedResult<HabitListItemDto>>();
        var item = Assert.Single(list.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(today, item.Today.Date);
    }

    [Fact]
    public async Task Invalid_habits_get_field_errors()
    {
        var user = await factory.RegisterUserAsync();
        var request = TestHelpers.BinaryHabit(factory.Today(), new ScheduleInput(ScheduleKind.TimesPerWeek, TimesPerWeek: 9))
            with { Name = "", Icon = "rocket" };

        var response = await user.Client.PostJsonAsync("/api/v1/habits", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("icon", out _));
        Assert.True(errors.TryGetProperty("schedule.timesPerWeek", out _));
    }

    [Fact]
    public async Task Quantity_habits_require_a_unit_and_binary_habits_reject_targets()
    {
        var user = await factory.RegisterUserAsync();
        var noUnit = TestHelpers.QuantityHabit(factory.Today()) with { Unit = null };
        var binaryTarget = TestHelpers.BinaryHabit(factory.Today(), new ScheduleInput(ScheduleKind.Daily, TargetValue: 5));

        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostJsonAsync("/api/v1/habits", noUnit)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostJsonAsync("/api/v1/habits", binaryTarget)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_target_keeps_past_results_and_reevaluates_today()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today.AddDays(-3), target: 20));
        await user.Client.LogAsync(habit.Id, today.AddDays(-1), quantity: 25);
        await user.Client.LogAsync(habit.Id, today, quantity: 25);

        var updated = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}",
            habit.ToUpdate(new ScheduleInput(ScheduleKind.Daily, TargetValue: 30))).OkAsync<HabitDto>();

        Assert.Equal(2, updated.ScheduleHistory.Count);
        Assert.Equal(today, updated.ScheduleHistory[1].EffectiveFrom);
        Assert.Equal(HabitDayStatus.Partial, updated.Today.Status);

        var logs = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/logs").OkAsync<List<HabitLogDto>>();
        Assert.True(logs.Single(l => l.Date == today.AddDays(-1)).Completed);
        Assert.False(logs.Single(l => l.Date == today).Completed);
    }

    [Fact]
    public async Task Stale_version_returns_409()
    {
        var user = await factory.RegisterUserAsync();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()));
        await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}", habit.ToUpdate(name: "Primero")).OkAsync<HabitDto>();

        var stale = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}", habit.ToUpdate(name: "Segundo"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency_conflict", await stale.ProblemCodeAsync());
    }

    [Fact]
    public async Task Start_date_is_locked_once_logs_exist()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-2)));
        var moved = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}",
            habit.ToUpdate(startDate: today.AddDays(-5))).OkAsync<HabitDto>();
        Assert.Equal(today.AddDays(-5), moved.StartDate);

        await user.Client.LogAsync(habit.Id, today);
        var locked = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}",
            moved.ToUpdate(startDate: today.AddDays(-1)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, locked.StatusCode);
        Assert.Equal("habit.start_date_locked", await locked.ProblemCodeAsync());
    }

    [Fact]
    public async Task Archive_keeps_history_blocks_logging_and_restore_reopens()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-3)));
        await user.Client.LogAsync(habit.Id, today.AddDays(-1));

        var archived = await user.Client.PostAsync($"/api/v1/habits/{habit.Id}/archive", null).OkAsync<HabitDto>();
        Assert.True(archived.IsArchived);

        var log = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/{today:yyyy-MM-dd}",
            new UpsertLogRequest(true, null, null));
        Assert.Equal("habit.archived", await log.ProblemCodeAsync());

        var active = await user.Client.GetAsync("/api/v1/habits").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Empty(active.Items);
        var archivedList = await user.Client.GetAsync("/api/v1/habits?status=archived").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Single(archivedList.Items);

        var logs = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/logs").OkAsync<List<HabitLogDto>>();
        Assert.Single(logs);

        var restored = await user.Client.PostAsync($"/api/v1/habits/{habit.Id}/restore", null).OkAsync<HabitDto>();
        Assert.False(restored.IsArchived);
        Assert.Empty(restored.Pauses); // archived and restored on the same day
    }

    [Fact]
    public async Task Only_archived_habits_can_be_deleted_permanently()
    {
        var user = await factory.RegisterUserAsync();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()));

        var refused = await user.Client.DeleteAsync($"/api/v1/habits/{habit.Id}");
        Assert.Equal("habit.delete_requires_archive", await refused.ProblemCodeAsync());

        await user.Client.PostAsync($"/api/v1/habits/{habit.Id}/archive", null);
        await (await user.Client.DeleteAsync($"/api/v1/habits/{habit.Id}")).EnsureStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/v1/habits/{habit.Id}")).StatusCode);
    }

    [Fact]
    public async Task Search_and_category_filters()
    {
        var user = await factory.RegisterUserAsync();
        await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today(), name: "Meditar 100%"));
        await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(factory.Today(), name: "Leer"));

        var search = await user.Client.GetAsync("/api/v1/habits?search=100%25").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Equal("Meditar 100%", Assert.Single(search.Items).Name);

        var none = await user.Client.GetAsync($"/api/v1/habits?categoryId={Guid.NewGuid()}").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Custom_categories_are_private_and_cannot_be_deleted_while_used()
    {
        var owner = await factory.RegisterUserAsync();
        var other = await factory.RegisterUserAsync();
        var created = await owner.Client.PostJsonAsync("/api/v1/categories",
            new SaveCategoryRequest("Música", HabitColor.Pink, "music"));
        await created.EnsureStatusAsync(HttpStatusCode.Created);
        var category = await created.ReadAsync<CategoryDto>();

        var duplicate = await owner.Client.PostJsonAsync("/api/v1/categories", new SaveCategoryRequest("música", HabitColor.Pink, "music"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var builtInName = await owner.Client.PostJsonAsync("/api/v1/categories", new SaveCategoryRequest("Salud", HabitColor.Pink, "music"));
        Assert.Equal(HttpStatusCode.Conflict, builtInName.StatusCode);

        var otherList = await other.Client.GetAsync("/api/v1/categories").OkAsync<List<CategoryDto>>();
        Assert.DoesNotContain(otherList, c => c.Id == category.Id);
        Assert.Equal(10, otherList.Count(c => c.IsBuiltIn));

        var stolen = await other.Client.PostJsonAsync("/api/v1/habits",
            TestHelpers.BinaryHabit(factory.Today()) with { CategoryId = category.Id });
        Assert.Equal("habit.invalid_category", await stolen.ProblemCodeAsync());

        await owner.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()) with { CategoryId = category.Id });
        var inUse = await owner.Client.DeleteAsync($"/api/v1/categories/{category.Id}");
        Assert.Equal("category.in_use", await inUse.ProblemCodeAsync());

        var builtIn = await owner.Client.DeleteAsync($"/api/v1/categories/{TestHelpers.LearningCategory}");
        Assert.Equal("category.built_in", await builtIn.ProblemCodeAsync());
    }

    [Fact]
    public async Task Weekly_habit_exposes_the_current_week_progress()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-14),
            new ScheduleInput(ScheduleKind.TimesPerWeek, TimesPerWeek: 3)));

        var result = await user.Client.LogAsync(habit.Id, today);

        Assert.NotNull(result.Week);
        Assert.Equal(1, result.Week!.Completed);
        Assert.Equal(3, result.Week.Required);
        Assert.Equal(PeriodUnit.Week, result.Streak.Unit);
    }
}
