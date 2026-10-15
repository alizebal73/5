using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices", table =>
        {
            table.HasCheckConstraint("ck_invoices_amount_positive", "amount_toman > 0");
            table.HasCheckConstraint("ck_invoices_elapsed_positive", "elapsed_ticks IS NOT NULL AND elapsed_ticks > 0");
            table.HasCheckConstraint("ck_invoices_rate_positive", "tariff_hourly_rate_toman_snapshot > 0");
            table.HasCheckConstraint("ck_invoices_status_range", "status BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_invoices_payment_state", "(status = 1 AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL) OR (status = 2 AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL)");
        });
        b.HasKey(x => x.Id).HasName("pk_invoices");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.SessionId).IsRequired().HasColumnName("session_id");
        b.Property(x => x.StationId).IsRequired().HasColumnName("station_id");
        b.Property(x => x.CustomerId).IsRequired().HasColumnName("customer_id");
        b.Property(x => x.TariffId).IsRequired().HasColumnName("tariff_id");
        b.Property(x => x.TariffCodeSnapshot).HasMaxLength(32).IsRequired().HasColumnName("tariff_code_snapshot");
        b.Property(x => x.TariffNameSnapshot).HasMaxLength(120).IsRequired().HasColumnName("tariff_name_snapshot");
        b.Property(x => x.TariffHourlyRateTomanSnapshot).IsRequired().HasColumnName("tariff_hourly_rate_toman_snapshot");
        b.Property(x => x.ElapsedTicks).IsRequired().HasColumnName("elapsed_ticks");
        b.Property(x => x.AmountToman).IsRequired().HasColumnName("amount_toman");
        b.Property(x => x.IssuedByOperatorId).IsRequired().HasColumnName("issued_by_operator_id");
        b.Property(x => x.IssuedAtUtc).IsRequired().HasColumnName("issued_at_utc");
        b.Property(x => x.PaidByOperatorId).HasColumnName("paid_by_operator_id");
        b.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
        b.Property(x => x.Status).IsRequired().HasColumnName("status");
        b.HasIndex(x => x.SessionId).IsUnique().HasDatabaseName("ux_invoices_session_id");
        b.HasIndex(x => x.StationId).HasDatabaseName("ix_invoices_station_id");
        b.HasIndex(x => x.CustomerId).HasDatabaseName("ix_invoices_customer_id");
        b.HasIndex(x => x.TariffId).HasDatabaseName("ix_invoices_tariff_id");
        b.HasIndex(x => x.PaidAtUtc).HasDatabaseName("ix_invoices_paid_at_utc");

        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_invoices_sessions_session_id");
        b.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_invoices_stations_station_id");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_invoices_customers_customer_id");
        b.HasOne<Tariff>().WithMany().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_invoices_tariffs_tariff_id");
    }
}
