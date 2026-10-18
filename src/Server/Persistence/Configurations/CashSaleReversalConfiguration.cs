using GameNet.Server.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class CashSaleReversalConfiguration : IEntityTypeConfiguration<CashSaleReversal>
{
    public void Configure(EntityTypeBuilder<CashSaleReversal> b)
    {
        b.ToTable("cash_sale_reversals", table =>
        {
            table.HasCheckConstraint("ck_cash_sale_reversals_amount_positive", "amount_toman > 0");
            table.HasCheckConstraint(
                "ck_cash_sale_reversals_reason_valid",
                "char_length(btrim(reason)) >= 3 AND btrim(reason) !~ '[[:cntrl:]]'");
        });
        b.HasKey(x => x.Id).HasName("pk_cash_sale_reversals");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.InvoiceId).IsRequired().HasColumnName("invoice_id");
        b.Property(x => x.PaymentId).IsRequired().HasColumnName("payment_id");
        b.Property(x => x.AmountToman).IsRequired().HasColumnName("amount_toman");
        b.Property(x => x.ReversedByOperatorId).IsRequired().HasColumnName("reversed_by_operator_id");
        b.Property(x => x.Reason).HasMaxLength(256).IsRequired().HasColumnName("reason");
        b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired().HasColumnName("correlation_id");
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired().HasColumnName("idempotency_key");
        b.Property(x => x.ReversedAtUtc).IsRequired().HasColumnName("reversed_at_utc");
        b.HasIndex(x => x.InvoiceId).IsUnique().HasDatabaseName("ux_cash_sale_reversals_invoice_id");
        b.HasIndex(x => x.PaymentId).IsUnique().HasDatabaseName("ux_cash_sale_reversals_payment_id");
        b.HasIndex(x => x.ReversedAtUtc).HasDatabaseName("ix_cash_sale_reversals_reversed_at_utc");
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cash_sale_reversals_invoices_invoice_id");
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cash_sale_reversals_payments_payment_id");
    }
}
