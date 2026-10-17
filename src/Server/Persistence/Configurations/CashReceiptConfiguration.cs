using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Customers.Domain;
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
            table.HasCheckConstraint("ck_cash_receipts_wallet_top_up_purpose", "purpose = 1");
            table.HasCheckConstraint("ck_cash_receipts_cash_method", "method = 1");
        });
        b.HasKey(x => x.Id).HasName("pk_cash_receipts");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.Purpose).IsRequired().HasColumnName("purpose");
        b.Property(x => x.ReferenceId).IsRequired().HasColumnName("reference_id");
        b.Property(x => x.AmountAppliedToman).IsRequired().HasColumnName("amount_applied_toman");
        b.Property(x => x.AmountReceivedToman).IsRequired().HasColumnName("amount_received_toman");
        b.Property(x => x.ChangeGivenToman).IsRequired().HasColumnName("change_given_toman");
        b.Property(x => x.Method).IsRequired().HasColumnName("method");
        b.Property(x => x.ReceivedByOperatorId).IsRequired().HasColumnName("received_by_operator_id");
        b.Property(x => x.ReceivedAtUtc).IsRequired().HasColumnName("received_at_utc");
        b.HasIndex(x => new { x.Purpose, x.ReferenceId }).IsUnique().HasDatabaseName("ux_cash_receipts_purpose_reference_id");
        b.HasIndex(x => x.CustomerId).HasDatabaseName("ix_cash_receipts_customer_id");
        b.HasIndex(x => x.ReceivedAtUtc).HasDatabaseName("ix_cash_receipts_received_at_utc");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cash_receipts_customers_customer_id");
    }
}
