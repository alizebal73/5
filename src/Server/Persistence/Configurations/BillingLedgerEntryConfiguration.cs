using GameNet.Server.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class BillingLedgerEntryConfiguration : IEntityTypeConfiguration<BillingLedgerEntry>
{
    public void Configure(EntityTypeBuilder<BillingLedgerEntry> b)
    {
        b.ToTable("billing_ledger_entries", table =>
        {
            table.HasCheckConstraint("ck_billing_ledger_amount_positive", "amount_toman > 0");
            table.HasCheckConstraint("ck_billing_ledger_entry_type", "entry_type = 1");
        });
        b.HasKey(x => x.Id).HasName("pk_billing_ledger_entries");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.InvoiceId).IsRequired().HasColumnName("invoice_id");
        b.Property(x => x.PaymentId).IsRequired().HasColumnName("payment_id");
        b.Property(x => x.EntryType).IsRequired().HasColumnName("entry_type");
        b.Property(x => x.AmountToman).IsRequired().HasColumnName("amount_toman");
        b.Property(x => x.ActorId).IsRequired().HasColumnName("actor_id");
        b.Property(x => x.OccurredAtUtc).IsRequired().HasColumnName("occurred_at_utc");
        b.HasIndex(x => x.InvoiceId).IsUnique().HasDatabaseName("ux_billing_ledger_invoice_id");
        b.HasIndex(x => x.PaymentId).IsUnique().HasDatabaseName("ux_billing_ledger_payment_id");
        b.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("ix_billing_ledger_occurred_at_utc");
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_billing_ledger_entries_invoices_invoice_id");
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_billing_ledger_entries_payments_payment_id");
    }
}
