using System.Net;
using HabitFlow.Application.Common;
using HabitFlow.Application.Habits;
using HabitFlow.Application.Tracking;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Habits.Tracking;
using HabitFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class LogApiTests(ApiFactory factory)
{
    private async Task<int> CountLogsAsync(Guid habitId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.HabitLogs.IgnoreQueryFilters().CountAsync(l => l.HabitId == habitId);
    }

    [Fact]
    public async Task Repeating_the_same_put_is_idempotent()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today));

        var first = await user.Client.LogAsync(habit.Id, today, quantity: 12);
        var second = await user.Client.LogAsync(habit.Id, today, quantity: 12);

        Assert.Equal(first.Day, second.Day);
        Assert.Equal(1, await CountLogsAsync(habit.Id));
        var changes = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/logs/changes")
            .OkAsync<PagedResult<LogChangeDto>>();
        Assert.Equal(1, changes.TotalCount);
    }

    [Fact]
    public async Task Concurrent_first_writes_never_create_duplicates()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/{today:yyyy-MM-dd}", new UpsertLogRequest(true, null, null))));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await CountLogsAsync(habit.Id));
    }

    [Fact]
    public async Task Quantity_is_a_daily_total_and_completion_follows_the_target()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.QuantityHabit(today, target: 20));

        var partial = await user.Client.LogAsync(habit.Id, today, quantity: 15, note: "Capítulo 3");
        Assert.Equal(HabitDayStatus.Partial, partial.Day.Status);
        Assert.Equal("Capítulo 3", partial.Log!.Note);

        var done = await user.Client.LogAsync(habit.Id, today, quantity: 22);
        Assert.Equal(HabitDayStatus.Completed, done.Day.Status);
        Assert.Equal(22, done.Log!.Quantity);
        Assert.Equal(1, done.Streak.Current);

        var changes = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/logs/changes")
            .OkAsync<PagedResult<LogChangeDto>>();
        var update = changes.Items[0];
        Assert.Equal((LogChangeAction.Updated, 15m, 22m, false, true),
            (update.Action, update.PreviousQuantity!.Value, update.NewQuantity!.Value, update.PreviousCompleted!.Value,
                update.NewCompleted!.Value));
    }

    [Fact]
    public async Task A_null_note_keeps_the_stored_note_and_an_empty_one_clears_it()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today));
        await user.Client.LogAsync(habit.Id, today, note: "Con calma");

        var unchecked_ = await user.Client.LogAsync(habit.Id, today, completed: false);
        Assert.Equal("Con calma", unchecked_.Log!.Note);
        Assert.False(unchecked_.Log.Completed);

        var cleared = await user.Client.LogAsync(habit.Id, today, completed: false, note: "");
        Assert.Null(cleared.Log!.Note);
    }

    [Fact]
    public async Task Dates_outside_the_allowed_window_are_rejected()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-2)));

        var future = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/{today.AddDays(1):yyyy-MM-dd}",
            new UpsertLogRequest(true, null, null));
        var beforeStart = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/{today.AddDays(-3):yyyy-MM-dd}",
            new UpsertLogRequest(true, null, null));
        var quantityOnBinary = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/{today:yyyy-MM-dd}",
            new UpsertLogRequest(null, 5, null));
        var empty = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/{today:yyyy-MM-dd}",
            new UpsertLogRequest(null, null, null));
        var badDate = await user.Client.PutJsonAsync($"/api/v1/habits/{habit.Id}/logs/2026-13-45",
            new UpsertLogRequest(true, null, null));

        Assert.Equal("log.future_date", await future.ProblemCodeAsync());
        Assert.Equal("log.before_start", await beforeStart.ProblemCodeAsync());
        Assert.Equal("log.quantity_not_allowed", await quantityOnBinary.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.True(badDate.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Past_days_can_be_corrected_and_the_streak_is_recomputed()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-4)));
        foreach (var offset in new[] { -4, -3, -1 })
        {
            await user.Client.LogAsync(habit.Id, today.AddDays(offset));
        }

        var before = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/streak").OkAsync<StreakDto>();
        Assert.Equal(1, before.Current);

        var corrected = await user.Client.LogAsync(habit.Id, today.AddDays(-2));
        Assert.Equal(4, corrected.Streak.Current);

        var removed = await user.Client.DeleteAsync($"/api/v1/habits/{habit.Id}/logs/{today.AddDays(-2):yyyy-MM-dd}")
            .OkAsync<LogResultDto>();
        Assert.Equal(HabitDayStatus.Missed, removed.Day.Status);
        Assert.Equal(1, removed.Streak.Current);
        Assert.Null(removed.Log);

        // Deleting again is not an error.
        await (await user.Client.DeleteAsync($"/api/v1/habits/{habit.Id}/logs/{today.AddDays(-2):yyyy-MM-dd}"))
            .EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Today_depends_on_the_users_time_zone()
    {
        // The test clock is 12:00 UTC: already tomorrow in Kiritimati (UTC+14), still today in Pago Pago (UTC−11).
        var east = await factory.RegisterUserAsync("Pacific/Kiritimati");
        var west = await factory.RegisterUserAsync("Pacific/Pago_Pago");
        var eastToday = factory.Today("Pacific/Kiritimati");
        var westToday = factory.Today("Pacific/Pago_Pago");
        Assert.Equal(westToday.AddDays(1), eastToday);

        var eastHabit = await east.Client.CreateHabitAsync(TestHelpers.BinaryHabit(westToday));
        var westHabit = await west.Client.CreateHabitAsync(TestHelpers.BinaryHabit(westToday));

        await east.Client.LogAsync(eastHabit.Id, eastToday);
        var westFuture = await west.Client.PutJsonAsync($"/api/v1/habits/{westHabit.Id}/logs/{eastToday:yyyy-MM-dd}",
            new UpsertLogRequest(true, null, null));
        Assert.Equal("log.future_date", await westFuture.ProblemCodeAsync());

        // A habit can override the zone: the same west user tracking a habit in Kiritimati time.
        var travel = await west.Client.CreateHabitAsync(TestHelpers.BinaryHabit(westToday) with { TimeZoneId = "Pacific/Kiritimati" });
        Assert.Equal(eastToday, travel.Today.Date);
        await west.Client.LogAsync(travel.Id, eastToday);
    }

    [Fact]
    public async Task Paginated_change_history_is_newest_first()
    {
        var user = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-5)));
        for (var i = -5; i <= 0; i++)
        {
            await user.Client.LogAsync(habit.Id, today.AddDays(i));
            factory.Clock.Advance(TimeSpan.FromMilliseconds(5));
        }

        var page = await user.Client.GetAsync($"/api/v1/habits/{habit.Id}/logs/changes?page=2&pageSize=4")
            .OkAsync<PagedResult<LogChangeDto>>();

        Assert.Equal(6, page.TotalCount);
        Assert.Equal([today.AddDays(-4), today.AddDays(-5)], page.Items.Select(i => i.Date));
    }
}
