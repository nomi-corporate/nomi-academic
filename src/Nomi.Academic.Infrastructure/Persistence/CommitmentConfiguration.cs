using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Academic.Domain;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class CommitmentConfiguration : IEntityTypeConfiguration<Commitment>
{
    public void Configure(EntityTypeBuilder<Commitment> builder)
    {
        builder.ToTable("commitments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne(x => x.Subject).WithMany().HasForeignKey(x => new { x.OwnerUserId, x.SubjectId })
            .HasPrincipalKey(x => new { x.OwnerUserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OwnerUserId, x.Deleted, x.DueInstant });
        builder.HasIndex(x => new { x.OwnerUserId, x.Deleted, x.LocalDate });
    }
}
