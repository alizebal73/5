using GameNet.Server.Modules.Wallet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class WalletLedgerEntryConfiguration : IEntityTypeConfiguration<WalletLedgerEntry>
{
    public void Configure(EntityTypeBuilder<WalletLedgerEntry> b)
    {
        b.ToTable("wallet_ledger_entries", table =>
        {
            table.HasCheckConstraint("ck_wallet_ledger_amount_positive", "amount_toman > 0");
            table.HasCheckConstraint("ck_wallet_ledger_balance_after_covers_credit", "balance_after_toman >= amount_toman");
            table.HasCheckConstraint("ck_wallet_ledger_entry_type", "entry_type = 1");
        });
        b.HasKey(x => x.Id).HasName("pk_wallet_ledger_entries");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.CashReceiptId).IsRequired().HasColumnName("cash_receipt_id");
        b.Property(x => x.EntryType).IsRequired().HasColumnName("entry_type");
        b.Property(x => x.AmountToman).IsRequired().HasColumnName("amount_toman");
        b.Property(x => x.BalanceAfterToman).IsRequired().HasColumnName("balance_after_toman");
        b.Property(x => x.ActorId).IsRequired().HasColumnName("actor_id");
        b.Property(x => x.OccurredAtUtc).IsRequired().HasColumnName("occurred_at_utc");
        b.HasIndex(x => x.CashReceiptId).IsUnique().HasDatabaseName("ux_wallet_ledger_cash_receipt");
        b.HasIndex(x => new { x.CustomerId, x.OccurredAtUtc }).HasDatabaseName("ix_wallet_ledger_customer_occurred_at");
        b.HasOne<GameNet.Server.Modules.Customers.Domain.Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_ledger_customers_customer_id");
        b.HasOne<GameNet.Server.Modules.Billing.Domain.CashReceipt>().WithMany().HasForeignKey(x => x.CashReceiptId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_ledger_cash_receipts_cash_receipt_id");
    }
}