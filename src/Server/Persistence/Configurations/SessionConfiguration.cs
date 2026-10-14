using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions", table =>
        {
            table.HasCheckConstraint("ck_sessions_rate_positive", "tariff_hourly_rate_toman_snapshot > 0");
            table.HasCheckConstraint("ck_sessions_time_state", "(status = 1 AND ended_at_utc IS NULL AND paid_at_utc IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND paid_at_utc IS NULL) OR (status = 3 AND ended_at_utc IS NOT NULL AND paid_at_utc IS NOT NULL)");
        });
        b.HasKey(x => x.Id).HasName("pk_sessions");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.StationId).IsRequired().HasColumnName("station_id");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.TariffId).IsRequired().HasColumnName("tariff_id");
        b.Property(x => x.TariffCodeSnapshot).HasMaxLength(32).IsRequired().HasColumnName("tariff_code_snapshot");
        b.Property(x => x.TariffNameSnapshot).HasMaxLength(120).IsRequired().HasColumnName("tariff_name_snapshot");
        b.Property(x => x.TariffHourlyRateTomanSnapshot).IsRequired().HasColumnName("tariff_hourly_rate_toman_snapshot");
        b.Property(x => x.StartedByOperatorId).IsRequired().HasColumnName("started_by_operator_id");
        b.Property(x => x.StartedAtUtc).IsRequired().HasColumnName("started_at_utc");
        b.Property(x => x.EndedByOperatorId).HasColumnName("ended_by_operator_id");
        b.Property(x => x.EndedAtUtc).HasColumnName("ended_at_utc");
        b.Property(x => x.PaidByOperatorId).HasColumnName("paid_by_operator_id");
        b.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
        b.Property(x => x.Status).IsRequired().HasColumnName("status");
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired().HasColumnName("version");

        b.HasIndex(x => x.StationId).IsUnique()
            .HasFilter("status = 1")
            .HasDatabaseName("ux_sessions_one_active_per_station");
        b.HasIndex(x => new { x.CustomerId, x.Status }).HasDatabaseName("ix_sessions_customer_status");
        b.HasIndex(x => x.StartedAtUtc).HasDatabaseName("ix_sessions_started_at_utc");

        b.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tariff>().WithMany().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Restrict);
    }
}
