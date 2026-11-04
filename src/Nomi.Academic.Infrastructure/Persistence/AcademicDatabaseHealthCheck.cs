using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nomi.Academic.Infrastructure.Persistence;

internal sealed class AcademicDatabaseHealthCheck(AcademicDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connected = await database.Database.CanConnectAsync(cancellationToken);
        return connected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Academic database is unavailable.");
    }
}
