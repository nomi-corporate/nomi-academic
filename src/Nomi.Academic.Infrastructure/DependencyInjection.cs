using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nomi.Academic.Infrastructure.Persistence;
using Nomi.Academic.Infrastructure.Identity;

namespace Nomi.Academic.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Academic");
        var healthChecks = services.AddHealthChecks();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("identity", client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<IIdentityUserResolver, IdentityUserResolver>();
        if (string.IsNullOrWhiteSpace(connection))
        {
            healthChecks.AddCheck("postgresql",
                () => HealthCheckResult.Unhealthy("Academic database is not configured."),
                tags: ["ready"]);
            return services;
        }

        services.AddDbContext<AcademicDbContext>(options => options.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "migrations")));
        healthChecks.AddCheck<AcademicDatabaseHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
