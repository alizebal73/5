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
            table.HasCheckConstraint("ck_sessions_status_range", "status BETWEEN 1 AND 4");
            table.HasCheckConstraint("ck_sessions_start_actor_shape", "((started_by_customer_id IS NULL AND started_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (started_by_customer_id IS NOT NULL AND started_by_operator_id = '00000000-0000-0000-0000-000000000000'))");
            table.HasCheckConstraint("ck_sessions_end_actor_shape", "((status IN (1, 4) AND ended_by_system = FALSE) OR (status IN (2, 3) AND ((ended_by_system = TRUE AND ended_by_operator_id = '00000000-0000-0000-0000-000000000000') OR (ended_by_system = FALSE AND ended_by_operator_id <> '00000000-0000-0000-0000-000000000000'))))");
            table.HasCheckConstraint("ck_sessions_paid_actor_shape", "((status IN (1, 2, 4) AND paid_by_customer_id IS NULL) OR (status = 3 AND ((paid_by_customer_id IS NULL AND paid_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (paid_by_customer_id IS NOT NULL AND paid_by_operator_id = '00000000-0000-0000-0000-000000000000'))))");
            table.HasCheckConstraint("ck_sessions_time_state", "(status = 1 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NULL AND accumulated_elapsed_ticks >= 0 AND current_segment_started_monotonic_timestamp IS NOT NULL AND current_segment_started_monotonic_frequency IS NOT NULL AND current_segment_started_monotonic_frequency > 0 AND current_segment_started_system_uptime_milliseconds IS NOT NULL AND current_segment_started_system_uptime_milliseconds >= 0 AND current_segment_started_boot_identifier IS NOT NULL) OR (status = 4 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NULL AND accumulated_elapsed_ticks > 0 AND current_segment_started_monotonic_timestamp IS NULL AND current_segment_started_monotonic_frequency IS NULL AND current_segment_started_system_uptime_milliseconds IS NULL AND current_segment_started_boot_identifier IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NOT NULL AND elapsed_ticks > 0 AND accumulated_elapsed_ticks = elapsed_ticks AND current_segment_started_monotonic_timestamp IS NULL AND current_segment_started_monotonic_frequency IS NULL AND current_segment_started_system_uptime_milliseconds IS NULL AND current_segment_started_boot_identifier IS NULL) OR (status = 3 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL AND elapsed_ticks IS NOT NULL AND elapsed_ticks > 0 AND accumulated_elapsed_ticks = elapsed_ticks AND current_segment_started_monotonic_timestamp IS NULL AND current_segment_started_monotonic_frequency IS NULL AND current_segment_started_system_uptime_milliseconds IS NULL AND current_segment_started_boot_identifier IS NULL)");
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
        b.Property(x => x.StartedByCustomerId).HasColumnName("started_by_customer_id");
        b.Property(x => x.StartedAtUtc).IsRequired().HasColumnName("started_at_utc");
        b.Property(x => x.StartedMonotonicTimestamp).IsRequired().HasColumnName("started_monotonic_timestamp");
        b.Property(x => x.StartedMonotonicFrequency).IsRequired().HasColumnName("started_monotonic_frequency");
        b.Property(x => x.StartedSystemUptimeMilliseconds).IsRequired().HasColumnName("started_system_uptime_milliseconds");
        b.Property(x => x.StartedBootIdentifier).IsRequired().HasColumnName("started_boot_identifier");
        b.Property(x => x.AccumulatedElapsedTicks).IsRequired().HasColumnName("accumulated_elapsed_ticks");
        b.Property(x => x.CurrentSegmentStartedMonotonicTimestamp).HasColumnName("current_segment_started_monotonic_timestamp");
        b.Property(x => x.CurrentSegmentStartedMonotonicFrequency).HasColumnName("current_segment_started_monotonic_frequency");
        b.Property(x => x.CurrentSegmentStartedSystemUptimeMilliseconds).HasColumnName("current_segment_started_system_uptime_milliseconds");
        b.Property(x => x.CurrentSegmentStartedBootIdentifier).HasColumnName("current_segment_started_boot_identifier");
        b.Property(x => x.ElapsedTicks).HasColumnName("elapsed_ticks");
        b.Property(x => x.EndedByOperatorId).HasColumnName("ended_by_operator_id");
        b.Property(x => x.EndedBySystem).IsRequired().HasColumnName("ended_by_system");
        b.Property(x => x.EndedAtUtc).HasColumnName("ended_at_utc");
        b.Property(x => x.PaidByOperatorId).HasColumnName("paid_by_operator_id");
        b.Property(x => x.PaidByCustomerId).HasColumnName("paid_by_customer_id");
        b.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
        b.Property(x => x.Status).IsRequired().HasColumnName("status");
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired().HasColumnName("version");

        b.HasIndex(x => x.StationId).IsUnique()
            .HasFilter("status IN (1, 2, 4)")
            .HasDatabaseName("ux_sessions_one_unsettled_per_station");
        b.HasIndex(x => new { x.CustomerId, x.Status }).HasDatabaseName("ix_sessions_customer_status");
        b.HasIndex(x => x.TariffId).HasDatabaseName("ix_sessions_tariff_id");
        b.HasIndex(x => x.StartedAtUtc).HasDatabaseName("ix_sessions_started_at_utc");
        b.HasIndex(x => x.StartedByCustomerId).HasDatabaseName("ix_sessions_started_by_customer_id");
        b.HasIndex(x => x.PaidByCustomerId).HasDatabaseName("ix_sessions_paid_by_customer_id");

        b.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sessions_stations_station_id");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sessions_customers_customer_id");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.StartedByCustomerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sessions_started_by_customers_customer_id");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.PaidByCustomerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sessions_paid_by_customers_customer_id");
        b.HasOne<Tariff>().WithMany().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sessions_tariffs_tariff_id");
    }
}
