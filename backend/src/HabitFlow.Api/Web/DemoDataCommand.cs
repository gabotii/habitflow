using System.Security.Cryptography;
using HabitFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Api.Web;

/// <summary>
/// <c>dotnet run --project src/HabitFlow.Api -- seed-demo</c>: applies migrations and creates the demo account.
/// Refuses to run in Production. The password comes from <c>Demo:Password</c>; if absent a random one is generated
/// and printed once.
/// </summary>
internal static class DemoDataCommand
{
    public static async Task RunAsync(WebApplication app)
    {
        if (app.Environment.IsProduction())
        {
            Console.Error.WriteLine("seed-demo is disabled in Production. Set ASPNETCORE_ENVIRONMENT=Development or Demo.");
            Environment.ExitCode = 1;
            return;
        }

        var email = app.Configuration["Demo:Email"] ?? "demo@habitflow.local";
        var password = app.Configuration["Demo:Password"];
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
        {
            password = "Demo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)) + "a1";
        }

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        var seeder = ActivatorUtilities.CreateInstance<DemoDataSeeder>(scope.ServiceProvider);

        if (!await seeder.SeedAsync(email, password!))
        {
            Console.WriteLine($"The demo account {email} already exists; nothing to do.");
            return;
        }

        Console.WriteLine($"Demo account created: {email}");
        Console.WriteLine(generated ? $"Generated password (shown only once): {password}" : "Password: the value of Demo:Password.");
    }
}
