using GameNet.Server.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class CashTenderCorrectionConfiguration : IEntityTypeConfiguration<CashTenderCorrection>
{
    public void Configure(EntityTypeBuilder<CashTenderCorrection> b)
    {
        b.ToTable("cash_tender_corrections", table =>
        {
            table.HasCheckConstraint("ck_cash_tender_corrections_version_positive", "version > 0");
            table.HasCheckConstraint("ck_cash_tender_corrections_previous_received_positive", "previous_amount_received_toman > 0");
            table.HasCheckConstraint("ck_cash_tender_corrections_previous_change_nonnegative", "previous_change_given_toman >= 0");
            table.HasCheckConstraint("ck_cash_tender_corrections_corrected_received_positive", "corrected_amount_received_toman > 0");
            table.HasCheckConstraint("ck_cash_tender_corrections_corrected_change_nonnegative", "corrected_change_given_toman >= 0");
            table.HasCheckConstraint(
                "ck_cash_tender_corrections_reason_valid",
                "char_length(btrim(reason)) >= 3 AND btrim(reason) !~ '[[:cntrl:]]'");
        });
        b.HasKey(x => x.Id).HasName("pk_cash_tender_corrections");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.InvoiceId).IsRequired().HasColumnName("invoice_id");
        b.Property(x => x.PaymentId).IsRequired().HasColumnName("payment_id");
        b.Property(x => x.Version).IsRequired().HasColumnName("version");
        b.Property(x => x.PreviousAmountReceivedToman).IsRequired().HasColumnName("previous_amount_received_toman");
        b.Property(x => x.PreviousChangeGivenToman).IsRequired().HasColumnName("previous_change_given_toman");
        b.Property(x => x.CorrectedAmountReceivedToman).IsRequired().HasColumnName("corrected_amount_received_toman");
        b.Property(x => x.CorrectedChangeGivenToman).IsRequired().HasColumnName("corrected_change_given_toman");
        b.Property(x => x.CorrectedByOperatorId).IsRequired().HasColumnName("corrected_by_operator_id");
        b.Property(x => x.Reason).HasMaxLength(256).IsRequired().HasColumnName("reason");
        b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired().HasColumnName("correlation_id");
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired().HasColumnName("idempotency_key");
        b.Property(x => x.CorrectedAtUtc).IsRequired().HasColumnName("corrected_at_utc");
        b.HasIndex(x => new { x.PaymentId, x.Version }).IsUnique().HasDatabaseName("ux_cash_tender_corrections_payment_version");
        b.HasIndex(x => new { x.InvoiceId, x.Version }).IsUnique().HasDatabaseName("ux_cash_tender_corrections_invoice_version");
        b.HasIndex(x => x.CorrectedAtUtc).HasDatabaseName("ix_cash_tender_corrections_corrected_at_utc");
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cash_tender_corrections_invoices_invoice_id");
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_cash_tender_corrections_payments_payment_id");
    }
}