using HabitFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class DemoSeederTests(ApiFactory factory)
{
    [Fact]
    public async Task Seeds_one_demo_account_once_and_touches_nothing_else()
    {
        var other = await factory.RegisterUserAsync();
        var email = $"demo-{Guid.NewGuid():N}@habitflow.local";

        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = ActivatorUtilities.CreateInstance<DemoDataSeeder>(scope.ServiceProvider);
        Assert.True(await seeder.SeedAsync(email, "Demo12345"));
        Assert.False(await seeder.SeedAsync(email, "Demo12345"));

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var demoId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        Assert.Equal(6, await db.Habits.IgnoreQueryFilters().CountAsync(h => h.UserId == demoId));
        Assert.True(await db.HabitLogs.IgnoreQueryFilters().CountAsync(l => l.UserId == demoId) > 50);
        Assert.Equal(2, await db.Goals.IgnoreQueryFilters().CountAsync(g => g.UserId == demoId));
        Assert.Equal(0, await db.Habits.IgnoreQueryFilters().CountAsync(h => h.UserId == other.Profile.Id));
    }
}
