using Microsoft.EntityFrameworkCore;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class AcademicDbContext(DbContextOptions<AcademicDbContext> options) : DbContext(options)
{
    public DbSet<Nomi.Academic.Domain.Subject> Subjects => Set<Nomi.Academic.Domain.Subject>();
    public DbSet<Nomi.Academic.Domain.Professor> Professors => Set<Nomi.Academic.Domain.Professor>();
    public DbSet<Nomi.Academic.Domain.SubjectProfessor> SubjectProfessors => Set<Nomi.Academic.Domain.SubjectProfessor>();
    public DbSet<Nomi.Academic.Domain.IdempotencyRecord> IdempotencyRecords => Set<Nomi.Academic.Domain.IdempotencyRecord>();
    public DbSet<Nomi.Academic.Domain.UserProfile> UserProfiles => Set<Nomi.Academic.Domain.UserProfile>();
    public DbSet<Nomi.Academic.Domain.Commitment> Commitments => Set<Nomi.Academic.Domain.Commitment>();
    public DbSet<Nomi.Academic.Domain.OutboxEvent> OutboxEvents => Set<Nomi.Academic.Domain.OutboxEvent>();
    public DbSet<Nomi.Academic.Domain.StudyGroup> StudyGroups => Set<Nomi.Academic.Domain.StudyGroup>();
    public DbSet<Nomi.Academic.Domain.StudyGroupMembership> StudyGroupMemberships => Set<Nomi.Academic.Domain.StudyGroupMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("academic");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcademicDbContext).Assembly);
    }
}
