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
            table.HasCheckConstraint("ck_wallet_ledger_balance_nonnegative", "balance_after_toman >= 0");
            table.HasCheckConstraint("ck_wallet_ledger_reserved_nonnegative", "reserved_balance_after_toman >= 0");
            table.HasCheckConstraint("ck_wallet_ledger_entry_type", "entry_type BETWEEN 1 AND 4");
            table.HasCheckConstraint("ck_wallet_ledger_entry_shape",
                "((entry_type = 1 AND cash_receipt_id IS NOT NULL AND session_id IS NULL AND balance_after_toman >= amount_toman) OR (entry_type BETWEEN 2 AND 4 AND cash_receipt_id IS NULL AND session_id IS NOT NULL))");
            table.HasCheckConstraint("ck_wallet_ledger_actor_type", "actor_type IN ('Operator', 'Customer', 'System')");
            table.HasCheckConstraint("ck_wallet_ledger_wallet_version_positive", "wallet_version_after > 0");
        });
        b.HasKey(x => x.Id).HasName("pk_wallet_ledger_entries");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.CashReceiptId).HasColumnName("cash_receipt_id");
        b.Property(x => x.SessionId).HasColumnName("session_id");
        b.Property(x => x.EntryType).IsRequired().HasColumnName("entry_type");
        b.Property(x => x.AmountToman).IsRequired().HasColumnName("amount_toman");
        b.Property(x => x.BalanceAfterToman).IsRequired().HasColumnName("balance_after_toman");
        b.Property(x => x.ReservedBalanceAfterToman).IsRequired().HasDefaultValue(0).HasColumnName("reserved_balance_after_toman");
        b.Property(x => x.ActorId).IsRequired().HasColumnName("actor_id");
        b.Property(x => x.ActorType).HasMaxLength(32).IsRequired().HasDefaultValue("Operator").HasColumnName("actor_type");
        b.Property(x => x.OccurredAtUtc).IsRequired().HasColumnName("occurred_at_utc");
        b.Property(x => x.WalletVersionAfter).IsRequired().HasColumnName("wallet_version_after");
        b.HasIndex(x => x.CashReceiptId).IsUnique().HasDatabaseName("ux_wallet_ledger_cash_receipt");
        // Keep the migration-defined lookup index for all funding movements tied to one Session.
        b.HasIndex(x => x.SessionId).HasDatabaseName("ix_wallet_ledger_session_id");
        b.HasIndex(x => new { x.SessionId, x.EntryType }).IsUnique()
            .HasFilter("session_id IS NOT NULL").HasDatabaseName("ux_wallet_ledger_session_entry_type");
        b.HasIndex(x => new { x.CustomerId, x.OccurredAtUtc }).HasDatabaseName("ix_wallet_ledger_customer_occurred_at");
        b.HasOne<GameNet.Server.Modules.Customers.Domain.Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_ledger_customers_customer_id");
        b.HasOne<GameNet.Server.Modules.Billing.Domain.CashReceipt>().WithMany().HasForeignKey(x => x.CashReceiptId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_ledger_cash_receipts_cash_receipt_id");
        b.HasOne<GameNet.Server.Modules.Sessions.Domain.Session>().WithMany().HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_ledger_sessions_session_id");
    }
}
