using System.Net;
using HabitFlow.Application.Common;
using HabitFlow.Application.Habits;

namespace HabitFlow.Tests.Integration;

/// <summary>Every private resource must be invisible (404) and immutable for other users, reads included.</summary>
[Collection(ApiCollection.Name)]
public sealed class IsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Another_user_cannot_read_or_modify_a_habit_or_its_logs()
    {
        var owner = await factory.RegisterUserAsync();
        var intruder = await factory.RegisterUserAsync();
        var today = factory.Today();
        var habit = await owner.Client.CreateHabitAsync(TestHelpers.BinaryHabit(today.AddDays(-1)));
        await owner.Client.LogAsync(habit.Id, today, note: "privado");

        var id = habit.Id;
        var date = today.ToString("yyyy-MM-dd");
        var attempts = new[]
        {
            await intruder.Client.GetAsync($"/api/v1/habits/{id}"),
            await intruder.Client.GetAsync($"/api/v1/habits/{id}/logs"),
            await intruder.Client.GetAsync($"/api/v1/habits/{id}/logs/changes"),
            await intruder.Client.GetAsync($"/api/v1/habits/{id}/streak"),
            await intruder.Client.PutJsonAsync($"/api/v1/habits/{id}", habit.ToUpdate(name: "Hackeado")),
            await intruder.Client.PutJsonAsync($"/api/v1/habits/{id}/logs/{date}", new UpsertLogRequest(false, null, null)),
            await intruder.Client.DeleteAsync($"/api/v1/habits/{id}/logs/{date}"),
            await intruder.Client.PostAsync($"/api/v1/habits/{id}/archive", null),
            await intruder.Client.DeleteAsync($"/api/v1/habits/{id}"),
        };

        Assert.All(attempts, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        var list = await intruder.Client.GetAsync("/api/v1/habits?status=all").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Empty(list.Items);

        // The owner's data is untouched.
        var logs = await owner.Client.GetAsync($"/api/v1/habits/{id}/logs").OkAsync<List<HabitLogDto>>();
        Assert.Equal("privado", Assert.Single(logs).Note);
        var current = await owner.Client.GetAsync($"/api/v1/habits/{id}").OkAsync<HabitDto>();
        Assert.Equal(habit.Name, current.Name);
    }

    [Fact]
    public async Task Owner_fields_in_the_body_are_ignored()
    {
        var owner = await factory.RegisterUserAsync();
        var other = await factory.RegisterUserAsync();

        // An extra "userId" property is not part of the contract and cannot assign ownership.
        var response = await other.Client.PostAsync("/api/v1/habits", new StringContent(
            $$"""
              {"name":"Intento","categoryId":"{{TestHelpers.LearningCategory}}","color":"teal","icon":"brain",
               "type":"binary","schedule":{"kind":"daily"},"userId":"{{owner.Profile.Id}}"}
              """, System.Text.Encoding.UTF8, "application/json"));
        await response.EnsureStatusAsync(HttpStatusCode.Created);

        var ownerList = await owner.Client.GetAsync("/api/v1/habits").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Empty(ownerList.Items);
        var otherList = await other.Client.GetAsync("/api/v1/habits").OkAsync<PagedResult<HabitListItemDto>>();
        Assert.Single(otherList.Items);
    }
}
