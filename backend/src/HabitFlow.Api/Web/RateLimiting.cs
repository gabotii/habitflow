using System.Threading.RateLimiting;
using HabitFlow.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HabitFlow.Api.Web;

public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Login, registration and password attempts per minute and client IP (credential guessing).</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Password-reset e-mails per hour and client IP (mail bombing).</summary>
    public int PasswordResetPerHour { get; set; } = 5;

    /// <summary>Requests per minute and authenticated user for the whole API (abuse and runaway clients).</summary>
    public int ApiPerMinute { get; set; } = 300;
}

internal static class RateLimiting
{
    public const string Auth = "auth";
    public const string PasswordReset = "password-reset";

    public static IServiceCollection AddHabitFlowRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitSettings>(configuration.GetSection(RateLimitSettings.SectionName));

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Demasiadas solicitudes",
                        Detail = "Has realizado demasiadas solicitudes. Espera un momento e inténtalo de nuevo.",
                        Extensions = { ["code"] = "rate_limited" },
                    },
                });
            };

            // Global per-user budget; anonymous requests are only limited by the endpoint policies.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var userId = context.RequestServices.GetRequiredService<ICurrentUser>().UserId;
                return userId == Guid.Empty
                    ? RateLimitPartition.GetNoLimiter("anonymous")
                    : RateLimitPartition.GetFixedWindowLimiter(userId.ToString(), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Settings(context).ApiPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    });
            });

            options.AddPolicy(Auth, context => PerIp(context, Settings(context).AuthPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(PasswordReset,
                context => PerIp(context, Settings(context).PasswordResetPerHour, TimeSpan.FromHours(1)));
        });
    }

    private static RateLimitSettings Settings(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;

    private static RateLimitPartition<string> PerIp(HttpContext context, int limit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window });
}
