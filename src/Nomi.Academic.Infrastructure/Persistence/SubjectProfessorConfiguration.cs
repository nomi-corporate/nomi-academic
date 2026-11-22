using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Academic.Domain;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class SubjectProfessorConfiguration : IEntityTypeConfiguration<SubjectProfessor>
{
    public void Configure(EntityTypeBuilder<SubjectProfessor> builder)
    {
        builder.ToTable("subject_professors");
        builder.HasKey(x => new { x.OwnerUserId, x.SubjectId, x.ProfessorId });
        builder.HasOne(x => x.Subject).WithMany(x => x.ProfessorLinks)
            .HasForeignKey(x => new { x.OwnerUserId, x.SubjectId })
            .HasPrincipalKey(x => new { x.OwnerUserId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Professor).WithMany()
            .HasForeignKey(x => new { x.OwnerUserId, x.ProfessorId })
            .HasPrincipalKey(x => new { x.OwnerUserId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OwnerUserId, x.ProfessorId });
    }
}
