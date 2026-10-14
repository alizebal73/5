using GameNet.Server.Modules.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers", table =>
        {
            table.HasCheckConstraint("ck_customers_code_length", "char_length(code) BETWEEN 2 AND 40");
            table.HasCheckConstraint("ck_customers_kind_range", "kind BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_customers_registered_name", "(kind = 1) OR (display_name IS NOT NULL AND length(trim(display_name)) > 0)");
        });
        b.HasKey(x => x.Id).HasName("pk_customers");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.Code).HasMaxLength(40).IsRequired().HasColumnName("code");
        b.Property(x => x.DisplayName).HasMaxLength(120).HasColumnName("display_name");
        b.Property(x => x.Kind).IsRequired().HasColumnName("kind");
        b.Property(x => x.IsActive).IsRequired().HasColumnName("is_active");
        b.Property(x => x.CreatedAtUtc).IsRequired().HasColumnName("created_at_utc");
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired().HasColumnName("version");
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_customers_code");
        b.HasIndex(x => new { x.Kind, x.IsActive }).HasDatabaseName("ix_customers_kind_active");
    }
}
