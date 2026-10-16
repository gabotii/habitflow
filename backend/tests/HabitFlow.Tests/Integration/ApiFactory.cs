using HabitFlow.Domain.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace HabitFlow.Tests.Integration;

/// <summary>
/// Hosts the real API against a throw-away PostgreSQL database (created per test run, dropped afterwards).
/// The server comes from HABITFLOW_TEST_DB (an admin-capable connection string); by default the local dev server.
/// Time is frozen at 12:00 UTC of the real current date, so local dates in every zone are predictable.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultServer =
        "Host=127.0.0.1;Port=5432;Username=habitflow;Password=habitflow_dev;Database=postgres";

    private readonly string _serverConnectionString =
        Environment.GetEnvironmentVariable("HABITFLOW_TEST_DB") ?? DefaultServer;

    private readonly string _databaseName = $"habitflow_test_{Guid.NewGuid():N}";

    public ApiFactory()
    {
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        Clock = new FakeTimeProvider(new DateTimeOffset(utcToday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
    }

    public FakeTimeProvider Clock { get; }

    public string MailDirectory { get; } = Path.Combine(Path.GetTempPath(), $"habitflow-mail-{Guid.NewGuid():N}");

    public string ConnectionString => new NpgsqlConnectionStringBuilder(_serverConnectionString)
    {
        Database = _databaseName,
        // Tests drop the database at the end; pooled idle connections would block DROP DATABASE.
        Pooling = false,
    }.ConnectionString;

    /// <summary>Today's local date in <paramref name="zoneId"/> for the frozen clock.</summary>
    public DateOnly Today(string zoneId = "Europe/Madrid") =>
        ZonedTime.LocalDate(Clock.GetUtcNow(), TimeZoneIds.Resolve(zoneId));

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
        if (Directory.Exists(MailDirectory))
        {
            Directory.Delete(MailDirectory, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Email:Mode"] = "Pickup",
            ["Email:PickupDirectory"] = MailDirectory,
            // The dispatcher is driven explicitly by the tests (no background timing).
            ["Reminders:Enabled"] = "false",
            ["RateLimiting:AuthPerMinute"] = "10000",
            ["RateLimiting:PasswordResetPerHour"] = "10000",
            ["RateLimiting:ApiPerMinute"] = "100000",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Other sessions must notice a password change immediately in tests.
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
