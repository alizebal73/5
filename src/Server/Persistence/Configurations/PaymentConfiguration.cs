using GameNet.Server.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_applied_positive", "amount_applied_toman > 0");
            table.HasCheckConstraint("ck_payments_received_sufficient", "amount_received_toman >= amount_applied_toman");
            table.HasCheckConstraint("ck_payments_change_matches", "change_given_toman = amount_received_toman - amount_applied_toman");
        });
        b.HasKey(x => x.Id).HasName("pk_payments");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.InvoiceId).IsRequired().HasColumnName("invoice_id");
        b.Property(x => x.Method).IsRequired().HasColumnName("method");
        b.Property(x => x.AmountAppliedToman).IsRequired().HasColumnName("amount_applied_toman");
        b.Property(x => x.AmountReceivedToman).IsRequired().HasColumnName("amount_received_toman");
        b.Property(x => x.ChangeGivenToman).IsRequired().HasColumnName("change_given_toman");
        b.Property(x => x.ReceivedByOperatorId).IsRequired().HasColumnName("received_by_operator_id");
        b.Property(x => x.ReceivedAtUtc).IsRequired().HasColumnName("received_at_utc");
        b.HasIndex(x => x.InvoiceId).IsUnique().HasDatabaseName("ux_payments_invoice_id");
        b.HasIndex(x => x.ReceivedAtUtc).HasDatabaseName("ix_payments_received_at_utc");
        b.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payments_invoices_invoice_id");
    }
}
