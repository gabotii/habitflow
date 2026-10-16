using HabitFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HabitFlow.Api.Web;

internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database unreachable");
}
