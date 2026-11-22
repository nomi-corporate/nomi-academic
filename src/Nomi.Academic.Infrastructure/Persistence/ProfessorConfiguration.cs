using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Academic.Domain;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class ProfessorConfiguration : IEntityTypeConfiguration<Professor>
{
    public void Configure(EntityTypeBuilder<Professor> builder)
    {
        builder.ToTable("professors");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OwnerUserId, x.Id });
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Office).HasMaxLength(160);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.OwnerUserId, x.Archived, x.Name, x.Id });
    }
}
