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
            table.HasCheckConstraint("ck_wallet_accounts_balance_nonnegative", "balance_toman >= 0");
            table.HasCheckConstraint("ck_wallet_accounts_version_nonnegative", "version >= 0");
            table.HasCheckConstraint("ck_wallet_accounts_reserved_nonnegative", "reserved_balance_toman >= 0");
        });
        b.HasKey(x => x.CustomerId).HasName("pk_wallet_accounts");
        b.Property(x => x.CustomerId).ValueGeneratedNever().HasColumnName("customer_id");
        b.Property(x => x.BalanceToman).IsRequired().HasColumnName("balance_toman");
        b.Property(x => x.ReservedBalanceToman).HasDefaultValue(0L).IsRequired().HasColumnName("reserved_balance_toman");
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired().HasColumnName("version");
        b.Property(x => x.UpdatedAtUtc).IsRequired().HasColumnName("updated_at_utc");
        b.HasOne<GameNet.Server.Modules.Customers.Domain.Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_accounts_customers_customer_id");
    }
}
