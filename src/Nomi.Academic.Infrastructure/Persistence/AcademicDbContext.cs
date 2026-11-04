using Microsoft.EntityFrameworkCore;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class AcademicDbContext(DbContextOptions<AcademicDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("academic");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcademicDbContext).Assembly);
    }
}
