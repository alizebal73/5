using GameNet.Server.Modules.Tariffs.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> b)
    {
        b.ToTable("tariffs", table =>
        {
            table.HasCheckConstraint("ck_tariffs_hourly_rate_positive", "hourly_rate_toman > 0");
        });
        b.HasKey(x => x.Id).HasName("pk_tariffs");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.Code).HasMaxLength(32).IsRequired().HasColumnName("code");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired().HasColumnName("name");
        b.Property(x => x.StationType).IsRequired().HasColumnName("station_type");
        b.Property(x => x.HourlyRateToman).IsRequired().HasColumnName("hourly_rate_toman");
        b.Property(x => x.IsDefault).IsRequired().HasColumnName("is_default");
        b.Property(x => x.IsActive).IsRequired().HasColumnName("is_active");
        b.Property(x => x.CreatedAtUtc).IsRequired().HasColumnName("created_at_utc");
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired().HasColumnName("version");
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_tariffs_code");
        b.HasIndex(x => x.StationType).IsUnique()
            .HasFilter("is_default = TRUE AND is_active = TRUE")
            .HasDatabaseName("ux_tariffs_one_active_default_per_type");
    }
}
