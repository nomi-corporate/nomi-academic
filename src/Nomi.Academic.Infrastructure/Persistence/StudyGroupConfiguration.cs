using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Academic.Domain;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class StudyGroupConfiguration : IEntityTypeConfiguration<StudyGroup>
{
    public void Configure(EntityTypeBuilder<StudyGroup> builder)
    {
        builder.ToTable("study_groups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.OwnerUserId, x.Active });
    }
}

public sealed class StudyGroupMembershipConfiguration : IEntityTypeConfiguration<StudyGroupMembership>
{
    public void Configure(EntityTypeBuilder<StudyGroupMembership> builder)
    {
        builder.ToTable("study_group_memberships");
        builder.HasKey(x => new { x.GroupId, x.UserId });
        builder.Property(x => x.Role).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.Active, x.GroupId });
    }
}
