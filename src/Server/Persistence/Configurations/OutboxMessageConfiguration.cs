using GameNet.Server.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired().HasColumnName("type");
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired().HasColumnName("payload_json");
        builder.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(x => x.LastFailureCode).HasMaxLength(128).HasColumnName("last_failure_code");
        builder.Property(x => x.DeadLetteredAtUtc).HasColumnName("dead_lettered_at_utc");
        builder.Property(x => x.LeaseToken).HasMaxLength(128).HasColumnName("lease_token");
        builder.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => new { x.PublishedAtUtc, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.LeaseExpiresAtUtc, x.Id });
        builder.HasIndex(x => new { x.NextAttemptAtUtc, x.Id })
            .HasDatabaseName("ix_outbox_messages_delivery_due")
            .HasFilter("published_at_utc IS NULL AND dead_lettered_at_utc IS NULL");
    }
}
