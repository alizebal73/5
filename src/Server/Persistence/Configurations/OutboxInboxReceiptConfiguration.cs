using GameNet.Server.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class OutboxInboxReceiptConfiguration : IEntityTypeConfiguration<OutboxInboxReceipt>
{
    public void Configure(EntityTypeBuilder<OutboxInboxReceipt> builder)
    {
        builder.ToTable("outbox_inbox_receipts");
        builder.HasKey(x => x.EventId).HasName("pk_outbox_inbox_receipts");
        builder.Property(x => x.EventId)
            .ValueGeneratedNever()
            .HasColumnName("event_id");
        builder.Property(x => x.Type)
            .HasMaxLength(200)
            .IsRequired()
            .HasColumnName("type");
        builder.Property(x => x.ProcessedAtUtc)
            .IsRequired()
            .HasColumnName("processed_at_utc");
    }
}
