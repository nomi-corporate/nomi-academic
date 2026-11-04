using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class AcademicDbContextFactory : IDesignTimeDbContextFactory<AcademicDbContext>
{
    public AcademicDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("NOMI_ACADEMIC_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "Configure NOMI_ACADEMIC_MIGRATION_CONNECTION com a identidade de migrations. " +
                "No ambiente local, use scripts/Invoke-LocalMigrations.ps1.");
        }

        var options = new DbContextOptionsBuilder<AcademicDbContext>()
            .UseNpgsql(connection, provider =>
                provider.MigrationsHistoryTable("__EFMigrationsHistory", "migrations"))
            .Options;

        return new AcademicDbContext(options);
    }
}
