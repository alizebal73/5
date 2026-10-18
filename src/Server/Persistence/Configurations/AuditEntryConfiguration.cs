using GameNet.Server.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ActorType).HasMaxLength(64).IsRequired().HasColumnName("actor_type");
        builder.Property(x => x.ActorId).HasMaxLength(128).HasColumnName("actor_id");
        builder.Property(x => x.Operation).HasMaxLength(200).IsRequired().HasColumnName("operation");
        builder.Property(x => x.ReferenceType).HasMaxLength(100).HasColumnName("reference_type");
        builder.Property(x => x.ReferenceId).HasMaxLength(128).HasColumnName("reference_id");
        builder.Property(x => x.Reason).HasMaxLength(1000).HasColumnName("reason");
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired().HasColumnName("correlation_id");
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired().HasColumnName("source");
        builder.Property(x => x.Outcome).HasMaxLength(64).IsRequired().HasColumnName("outcome");
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).HasColumnName("idempotency_key");
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb").HasColumnName("before_json");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb").HasColumnName("after_json");
        builder.Property(x => x.OccurredAtUtc).IsRequired().HasColumnName("occurred_at_utc");
        builder.HasIndex(x => new { x.OccurredAtUtc, x.Operation })
            .HasDatabaseName("IX_audit_entries_OccurredAtUtc_Operation");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("IX_audit_entries_ReferenceType_ReferenceId");
        builder.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("IX_audit_entries_IdempotencyKey");
    }
}
