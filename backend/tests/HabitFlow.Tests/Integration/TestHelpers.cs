using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HabitFlow.Application.Auth;
using HabitFlow.Application.Habits;
using HabitFlow.Domain.Habits;

namespace HabitFlow.Tests.Integration;

/// <summary>
/// Behaves like the SPA: keeps cookies (including Secure ones, since the base address is https) and echoes the
/// XSRF-TOKEN cookie in the X-XSRF-TOKEN header on unsafe requests.
/// </summary>
public sealed class BrowserSessionHandler : DelegatingHandler
{
    public CookieContainer Cookies { get; } = new();

    public bool SendCsrfHeader { get; set; } = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var cookieHeader = Cookies.GetCookieHeader(uri);
        if (cookieHeader.Length > 0)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        if (SendCsrfHeader && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head &&
            Cookies.GetCookies(uri)["XSRF-TOKEN"] is { } token)
        {
            request.Headers.Add("X-XSRF-TOKEN", token.Value);
        }

        var response = await base.SendAsync(request, ct);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                Cookies.SetCookies(uri, value);
            }
        }

        return response;
    }
}

public sealed record TestUser(HttpClient Client, BrowserSessionHandler Session, UserDto Profile, string Password);

public static class TestHelpers
{
    public const string Password = "Sup3rSecret";
    public static readonly Guid LearningCategory = Guid.Parse("01990000-0000-7000-8000-000000000003");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static (HttpClient Client, BrowserSessionHandler Session) CreateBrowser(this ApiFactory factory)
    {
        var session = new BrowserSessionHandler();
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), session);
        return (client, session);
    }

    public static async Task<TestUser> RegisterUserAsync(this ApiFactory factory, string timeZone = "Europe/Madrid")
    {
        var (client, session) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, Password, "Test User", timeZone));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return new TestUser(client, session, await response.ReadAsync<UserDto>(), Password);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    /// <summary>Asserts the status code and includes the body in the failure message.</summary>
    public static async Task EnsureStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode}: {body}");
        }
    }

    public static async Task<T> OkAsync<T>(this Task<HttpResponseMessage> request)
    {
        var response = await request;
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadAsync<T>();
    }

    public static async Task<string> ProblemCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync()).GetProperty("code").GetString()!;

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> DeleteJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body, options: Json) });

    public static CreateHabitRequest BinaryHabit(DateOnly start, ScheduleInput? schedule = null, string name = "Meditar") =>
        new(name, null, LearningCategory, HabitColor.Teal, "brain", HabitType.Binary, null, start, null,
            schedule ?? new ScheduleInput(ScheduleKind.Daily));

    public static CreateHabitRequest QuantityHabit(DateOnly start, decimal? target = 20, string name = "Leer") =>
        new(name, null, LearningCategory, HabitColor.Indigo, "book-open", HabitType.Quantity, "páginas", start, null,
            new ScheduleInput(ScheduleKind.Daily, TargetValue: target));

    public static async Task<HabitDto> CreateHabitAsync(this HttpClient client, CreateHabitRequest request)
    {
        var response = await client.PostJsonAsync("/api/v1/habits", request);
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return await response.ReadAsync<HabitDto>();
    }

    public static async Task<LogResultDto> LogAsync(this HttpClient client, Guid habitId, DateOnly date,
        bool? completed = true, decimal? quantity = null, string? note = null)
    {
        var response = await client.PutJsonAsync($"/api/v1/habits/{habitId}/logs/{date:yyyy-MM-dd}",
            new UpsertLogRequest(completed, quantity, note));
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadAsync<LogResultDto>();
    }

    public static UpdateHabitRequest ToUpdate(this HabitDto habit, ScheduleInput? schedule = null, string? name = null,
        DateOnly? startDate = null) =>
        new(name ?? habit.Name, habit.Description, habit.Category.Id, habit.Color, habit.Icon, habit.Unit,
            startDate ?? habit.StartDate, habit.TimeZoneId,
            schedule ?? new ScheduleInput(habit.Schedule.Kind, habit.Schedule.Weekdays, habit.Schedule.TimesPerWeek,
                habit.Schedule.IntervalDays, habit.Schedule.TargetValue),
            habit.Version);
}
