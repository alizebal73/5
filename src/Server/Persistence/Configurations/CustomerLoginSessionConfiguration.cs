using GameNet.Server.Modules.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class CustomerLoginSessionConfiguration : IEntityTypeConfiguration<CustomerLoginSession>
{
    public void Configure(EntityTypeBuilder<CustomerLoginSession> b)
    {
        b.ToTable("customer_login_sessions");
        b.HasKey(x => x.Jti).HasName("pk_customer_login_sessions");
        b.Property(x => x.Jti).HasMaxLength(32).IsRequired().HasColumnName("jti");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.CreatedAtUtc).IsRequired().HasColumnName("created_at_utc");
        b.Property(x => x.ExpiresAtUtc).IsRequired().HasColumnName("expires_at_utc");
        b.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        b.HasIndex(x => x.CustomerId).HasDatabaseName("ix_customer_login_sessions_customer_id");
        b.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("ix_customer_login_sessions_expires_at_utc");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_customer_login_sessions_customers_customer_id");
    }
}
