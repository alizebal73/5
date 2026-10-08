using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Infrastructure.Persistence;

public sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyEntry>
{
    public void Configure(EntityTypeBuilder<IdempotencyEntry> builder)
    {
        builder.ToTable("idempotency_entries");
        builder.HasKey(x => new { x.Scope, x.Key });
        builder.Property(x => x.Scope).HasMaxLength(128);
        builder.Property(x => x.Key).HasMaxLength(256);
        builder.Property(x => x.Operation).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.LeaseToken).HasMaxLength(128).IsRequired();
    }
}
