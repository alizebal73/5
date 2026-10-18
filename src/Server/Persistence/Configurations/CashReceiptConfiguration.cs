using GameNet.Server.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class CashReceiptConfiguration : IEntityTypeConfiguration<CashReceipt>
{
    public void Configure(EntityTypeBuilder<CashReceipt> b)
    {
        b.ToTable("cash_receipts", table =>
        {
            table.HasCheckConstraint("ck_cash_receipts_applied_positive", "amount_applied_toman > 0");
            table.HasCheckConstraint("ck_cash_receipts_received_sufficient", "amount_received_toman >= amount_applied_toman");
            table.HasCheckConstraint("ck_cash_receipts_change_matches", "change_given_toman = amount_received_toman - amount_applied_toman");
            table.HasCheckConstraint("ck_cash_receipts_purpose", "purpose = 1");
        });
        b.HasKey(x => x.Id).HasName("pk_cash_receipts");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.Purpose).IsRequired().HasColumnName("purpose");
        b.Property(x => x.AmountAppliedToman).IsRequired().HasColumnName("amount_applied_toman");
        b.Property(x => x.AmountReceivedToman).IsRequired().HasColumnName("amount_received_toman");
        b.Property(x => x.ChangeGivenToman).IsRequired().HasColumnName("change_given_toman");
        b.Property(x => x.ReceivedByOperatorId).IsRequired().HasColumnName("received_by_operator_id");
        b.Property(x => x.ReceivedAtUtc).IsRequired().HasColumnName("received_at_utc");
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired().HasColumnName("idempotency_key");
        b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired().HasColumnName("correlation_id");
        b.Property(x => x.Source).HasMaxLength(64).IsRequired().HasColumnName("source");
        b.HasIndex(x => new { x.ReceivedByOperatorId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_cash_receipts_operator_idempotency");
        b.HasIndex(x => new { x.CustomerId, x.ReceivedAtUtc }).HasDatabaseName("ix_cash_receipts_customer_received_at");
        b.HasOne<GameNet.Server.Modules.Customers.Domain.Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cash_receipts_customers_customer_id");
    }
}