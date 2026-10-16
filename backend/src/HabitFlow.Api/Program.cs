using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HabitFlow.Api.Web;
using HabitFlow.Application;
using HabitFlow.Application.Abstractions;
using HabitFlow.Infrastructure;
using HabitFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

// Container health probe (the runtime image has no curl): `dotnet HabitFlow.Api.dll --healthcheck`.
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        var response = await probe.GetAsync($"http://127.0.0.1:{port}/health");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}

// No uploads in this API: keep request bodies small.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_048_576);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.Configure<AuthCookieSettings>(configuration.GetSection(AuthCookieSettings.SectionName));

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ADR-03: Identity application cookie (HttpOnly, Secure, SameSite=Strict) + anti-forgery header.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<IOptions<AuthCookieSettings>>((options, settings) =>
    {
        options.Cookie.Name = "habitflow.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = settings.Value.Secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        // An API answers with status codes, never with redirects to a login page.
        options.Events.OnRedirectToLogin = context =>
            WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "unauthorized", "Debes iniciar sesión.");
        options.Events.OnRedirectToAccessDenied = context =>
            WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", "No tienes acceso a este recurso.");
    });

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = Csrf.HeaderName;
    options.Cookie.Name = Csrf.InternalCookieName;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.HttpOnly = true;

    // Antiforgery rejects "Always" on plain-HTTP requests (local dev, nginx on localhost); behind TLS (or forwarded
    // https) the cookie is Secure automatically.
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SuppressXFrameOptionsHeader = true;
});

// Secure by default: every endpoint requires an authenticated user unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Only needed when the SPA is served from another origin; the default deployment shares one origin (no CORS).
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowCredentials()
    .WithHeaders("Content-Type", Csrf.HeaderName)
    .WithMethods("GET", "POST", "PUT", "DELETE")));

builder.Services.AddHabitFlowRateLimiting(configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HabitFlow API",
        Version = "v1",
        Description = "Hábitos, rachas, objetivos y recordatorios. Autenticación por cookie: llama a " +
                      "GET /api/v1/auth/csrf y después a /api/v1/auth/login. Swagger UI añade la cabecera X-XSRF-TOKEN.",
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }
});

if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    // Only enable when the API is reachable exclusively through a trusted reverse proxy (see docker-compose).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (args.Contains("seed-demo"))
{
    await DemoDataCommand.RunAsync(app);
    return Environment.ExitCode;
}

if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Trace-Id"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        // Responses contain personal data: never store them in shared caches.
        headers.CacheControl = "no-store";
    }

    await next();
});

if (app.Environment.IsDevelopment() || configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "HabitFlow API";
        options.UseRequestInterceptor(
            "(req) => { const m = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]+)/); " +
            "if (m) req.headers['X-XSRF-TOKEN'] = decodeURIComponent(m[1]); return req; }");
    });
}

app.UseCors();
app.UseAuthentication();
app.UseCsrfProtection();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

await app.RunAsync();
return 0;

static Task WriteProblemAsync(HttpContext context, int status, string code, string detail)
{
    context.Response.StatusCode = status;
    return context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = GlobalExceptionHandler.Problem(status, code, detail),
    }).AsTask();
}

/// <summary>Entry point; partial so integration tests can reference it from WebApplicationFactory.</summary>
public partial class Program;
