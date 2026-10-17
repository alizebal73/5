using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Wallet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class WalletAccountConfiguration : IEntityTypeConfiguration<WalletAccount>
{
    public void Configure(EntityTypeBuilder<WalletAccount> b)
    {
        b.ToTable("wallet_accounts", table =>
        {
            table.HasCheckConstraint("ck_wallet_accounts_purchased_balance_nonnegative", "purchased_balance_toman >= 0");
            table.HasCheckConstraint("ck_wallet_accounts_version_nonnegative", "version >= 0");
        });
        b.HasKey(x => x.CustomerId).HasName("pk_wallet_accounts");
        b.Property(x => x.CustomerId).ValueGeneratedNever().HasColumnName("customer_id");
        b.Property(x => x.PurchasedBalanceToman).IsRequired().HasColumnName("purchased_balance_toman");
        b.Property(x => x.Version).IsRequired().HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.UpdatedAtUtc).IsRequired().HasColumnName("updated_at_utc");
        b.HasOne<Customer>().WithOne().HasForeignKey<WalletAccount>(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_accounts_customers_customer_id");
        b.HasIndex(x => x.UpdatedAtUtc).HasDatabaseName("ix_wallet_accounts_updated_at_utc");
    }
}
