using GameNet.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Scope).HasMaxLength(160).IsRequired().HasColumnName("scope");
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired().HasColumnName("key");
        builder.Property(x => x.Operation).HasMaxLength(200).IsRequired().HasColumnName("operation");
        builder.Property(x => x.State).HasMaxLength(32).IsRequired().HasColumnName("state");
        builder.Property(x => x.LeaseToken).HasMaxLength(128).IsRequired().HasColumnName("lease_token");
        builder.Property(x => x.StatusCode).HasColumnName("status_code");
        builder.Property(x => x.ResponseJson).HasColumnType("jsonb").IsRequired().HasColumnName("response_json");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
