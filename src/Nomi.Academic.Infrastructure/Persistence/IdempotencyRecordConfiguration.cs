using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nomi.Academic.Domain;

namespace Nomi.Academic.Infrastructure.Persistence;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(x => new { x.OwnerUserId, x.Operation, x.Key });
        builder.Property(x => x.Operation).HasMaxLength(64);
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ResponseJson).HasColumnType("text");
        builder.Property(x => x.ETag).HasMaxLength(80);
        builder.Property(x => x.Location).HasMaxLength(512);
        var utcTicks = new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(x => x.CreatedAt).HasConversion(utcTicks);
        builder.Property(x => x.LeaseExpiresAt).HasConversion(utcTicks).IsConcurrencyToken();
        builder.HasIndex(x => x.CreatedAt);
    }
}
