using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Academic.Domain;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");
        builder.HasKey(subject => subject.Id);
        builder.HasAlternateKey(subject => new { subject.OwnerUserId, subject.Id });
        builder.Property(subject => subject.Name).HasMaxLength(120).IsRequired();
        builder.Property(subject => subject.Code).HasMaxLength(40);
        builder.Property(subject => subject.ColorToken).HasMaxLength(80);
        builder.Property(subject => subject.IconKey).HasMaxLength(80);
        builder.Property(subject => subject.Version).IsConcurrencyToken();
        builder.HasIndex(subject => new { subject.OwnerUserId, subject.Archived, subject.Name, subject.Id });
    }
}
