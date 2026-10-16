using System.Security.Claims;
using HabitFlow.Application.Abstractions;
using HabitFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace HabitFlow.Api.Web;

/// <summary>Reads the user id from the validated Identity session cookie (never from the request body).</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return Guid.Empty;
            }

            return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        }
    }
}

public sealed class AuthCookieSettings
{
    public const string SectionName = "AuthCookie";

    /// <summary>Marks cookies Secure. Browsers accept Secure cookies on http://localhost, so keep it on.</summary>
    public bool Secure { get; set; } = true;
}

/// <summary>
/// CSRF protection for the cookie-authenticated API (double-submit pattern, ADR-03): the client reads the
/// <c>XSRF-TOKEN</c> cookie and echoes it in the <c>X-XSRF-TOKEN</c> header on every unsafe request. The token is bound
/// to the signed-in identity, so it is re-issued whenever the identity changes (login, logout, password change).
/// </summary>
internal static class Csrf
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string RequestCookieName = "XSRF-TOKEN";
    public const string InternalCookieName = "habitflow.af";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public static void Issue(HttpContext context, ClaimsPrincipal user)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var secure = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthCookieSettings>>()
            .Value.Secure;
        var previous = context.User;
        context.User = user;
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.User = previous;

        context.Response.Cookies.Append(RequestCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // Must be readable by the SPA to echo it in the header.
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    /// <summary>Validates every unsafe request under /api before it reaches authentication-sensitive code.</summary>
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!SafeMethods.Contains(context.Request.Method) && context.Request.Path.StartsWithSegments("/api"))
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }

        await next();
    });
}

internal static class SessionCookies
{
    public static async Task SignInAsync(HttpContext context, AppUser user, bool persistent)
    {
        var signIn = context.RequestServices.GetRequiredService<SignInManager<AppUser>>();
        await signIn.SignInAsync(user, persistent);
        Csrf.Issue(context, await signIn.CreateUserPrincipalAsync(user));
    }

    public static async Task SignOutAsync(HttpContext context)
    {
        var signIn = context.RequestServices.GetRequiredService<SignInManager<AppUser>>();
        await signIn.SignOutAsync();
        Csrf.Issue(context, new ClaimsPrincipal(new ClaimsIdentity()));
    }
}
