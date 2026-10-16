using System.Net;
using System.Text.RegularExpressions;
using HabitFlow.Application.Auth;

namespace HabitFlow.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed partial class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_signs_in_with_a_secure_http_only_cookie()
    {
        var (client, session) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");
        var response = await client.PostJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"new-{Guid.NewGuid():N}@test.local", "Sup3rSecret", "Nueva", "America/Bogota"));

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("habitflow.auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetAsync("/api/v1/users/me").OkAsync<UserDto>();
        Assert.Equal("America/Bogota", me.TimeZoneId);
        Assert.NotNull(session.Cookies.GetCookies(new Uri("https://localhost"))["XSRF-TOKEN"]);
    }

    [Fact]
    public async Task Unsafe_requests_without_the_csrf_header_are_rejected()
    {
        var user = await factory.RegisterUserAsync();
        user.Session.SendCsrfHeader = false;

        var response = await user.Client.PostJsonAsync("/api/v1/habits",
            TestHelpers.BinaryHabit(factory.Today()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_invalid", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Protected_endpoints_return_401_problem_without_a_session()
    {
        var (client, _) = factory.CreateBrowser();

        var response = await client.GetAsync("/api/v1/habits");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Login_errors_do_not_reveal_whether_the_account_exists()
    {
        var user = await factory.RegisterUserAsync();
        var (client, _) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");

        var wrongPassword = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(user.Profile.Email, "Wr0ngPassword"));
        var unknown = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "Wr0ngPassword"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrongPassword.ReadJsonAsync()).GetProperty("detail").GetString(),
            (await unknown.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_locks_after_five_failed_attempts()
    {
        var user = await factory.RegisterUserAsync();
        var (client, _) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");
        for (var i = 0; i < 5; i++)
        {
            await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(user.Profile.Email, "Wr0ngPassword"));
        }

        var response = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(user.Profile.Email, user.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("bloqueada", (await response.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var user = await factory.RegisterUserAsync();

        await (await user.Client.PostAsync("/api/v1/auth/logout", null)).EnsureStatusAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_and_weak_password_are_rejected()
    {
        var user = await factory.RegisterUserAsync();
        var (client, _) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");

        var duplicate = await client.PostJsonAsync("/api/v1/auth/register",
            new RegisterRequest(user.Profile.Email, "Sup3rSecret", "Otra", null));
        var weak = await client.PostJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"x-{Guid.NewGuid():N}@test.local", "short", "Otra", null));
        var badZone = await client.PostJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"x-{Guid.NewGuid():N}@test.local", "Sup3rSecret", "Otra", "Mars/Olympus"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_sessions_but_keeps_this_one()
    {
        var user = await factory.RegisterUserAsync();
        var (other, _) = factory.CreateBrowser();
        await other.GetAsync("/api/v1/auth/csrf");
        await (await other.PostJsonAsync("/api/v1/auth/login", new LoginRequest(user.Profile.Email, user.Password)))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var change = await user.Client.PostJsonAsync("/api/v1/users/me/password",
            new ChangePasswordRequest(user.Password, "N3wPassword"));

        await change.EnsureStatusAsync(HttpStatusCode.NoContent);
        await (await user.Client.GetAsync("/api/v1/users/me")).EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Profile_update_changes_name_zone_and_email_consent()
    {
        var user = await factory.RegisterUserAsync();

        var updated = await user.Client.PutJsonAsync("/api/v1/users/me",
            new UpdateProfileRequest("Nombre Nuevo", "Asia/Tokyo", true)).OkAsync<UserDto>();

        Assert.Equal(("Nombre Nuevo", "Asia/Tokyo", true), (updated.DisplayName, updated.TimeZoneId, updated.EmailRemindersEnabled));
    }

    [Fact]
    public async Task Password_reset_by_email_works_end_to_end()
    {
        var user = await factory.RegisterUserAsync();
        var (client, _) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");

        var capabilities = await client.GetAsync("/api/v1/auth/capabilities").OkAsync<AuthCapabilitiesDto>();
        Assert.True(capabilities.PasswordResetAvailable);

        var request = await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(user.Profile.Email));
        await request.EnsureStatusAsync(HttpStatusCode.Accepted);
        var unknown = await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("ghost@test.local"));
        await unknown.EnsureStatusAsync(HttpStatusCode.Accepted);

        var mail = Directory.GetFiles(factory.MailDirectory, "*.eml")
            .Select(File.ReadAllText)
            .Single(m => m.Contains(user.Profile.Email, StringComparison.OrdinalIgnoreCase));
        var unfolded = mail.Replace("=\r\n", string.Empty).Replace("=3D", "=");
        var token = TokenRegex().Match(unfolded).Groups[1].Value;

        var bad = await client.PostJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(user.Profile.Email, "bm90LWEtdG9rZW4", "N3wPassword"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var reset = await client.PostJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(user.Profile.Email, token, "N3wPassword"));
        await reset.EnsureStatusAsync(HttpStatusCode.NoContent);

        var login = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(user.Profile.Email, "N3wPassword"));
        await login.EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_the_account_requires_the_password_and_removes_the_data()
    {
        var user = await factory.RegisterUserAsync();
        await user.Client.CreateHabitAsync(TestHelpers.BinaryHabit(factory.Today()));

        var wrong = await user.Client.DeleteJsonAsync("/api/v1/users/me", new DeleteAccountRequest("Wr0ngPassword"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var deleted = await user.Client.DeleteJsonAsync("/api/v1/users/me", new DeleteAccountRequest(user.Password));
        await deleted.EnsureStatusAsync(HttpStatusCode.NoContent);

        var (client, _) = factory.CreateBrowser();
        await client.GetAsync("/api/v1/auth/csrf");
        var login = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(user.Profile.Email, user.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [GeneratedRegex(@"token=([A-Za-z0-9_\-]+)")]
    private static partial Regex TokenRegex();
}
