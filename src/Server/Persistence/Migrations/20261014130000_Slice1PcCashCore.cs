using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class Slice1PcCashCore : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(
            name: "customers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                kind = table.Column<int>(type: "integer", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customers", x => x.id);
                table.CheckConstraint("ck_customers_code_length", "char_length(code) BETWEEN 2 AND 40");
                table.CheckConstraint("ck_customers_registered_name", "(kind = 1) OR (display_name IS NOT NULL AND length(trim(display_name)) > 0)");
            });

        m.CreateTable(
            name: "tariffs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                station_type = table.Column<int>(type: "integer", nullable: false),
                hourly_rate_toman = table.Column<long>(type: "bigint", nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tariffs", x => x.id);
                table.CheckConstraint("ck_tariffs_hourly_rate_positive", "hourly_rate_toman > 0");
                table.CheckConstraint("ck_tariffs_station_type", "station_type BETWEEN 1 AND 3");
            });

        m.CreateTable(
            name: "sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                station_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                tariff_id = table.Column<Guid>(type: "uuid", nullable: false),
                tariff_code_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tariff_name_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                tariff_hourly_rate_toman_snapshot = table.Column<long>(type: "bigint", nullable: false),
                started_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ended_by_operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                paid_by_operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                paid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sessions", x => x.id);
                table.CheckConstraint("ck_sessions_rate_positive", "tariff_hourly_rate_toman_snapshot > 0");
                table.CheckConstraint("ck_sessions_status_range", "status BETWEEN 1 AND 3");
                table.CheckConstraint("ck_sessions_time_state", "(status = 1 AND ended_at_utc IS NULL AND paid_at_utc IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND paid_at_utc IS NULL) OR (status = 3 AND ended_at_utc IS NOT NULL AND paid_at_utc IS NOT NULL)");
                table.ForeignKey("fk_sessions_stations_station_id", x => x.station_id, "stations", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_sessions_customers_customer_id", x => x.customer_id, "customers", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_sessions_tariffs_tariff_id", x => x.tariff_id, "tariffs", "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateTable(
            name: "invoices",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                station_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                tariff_id = table.Column<Guid>(type: "uuid", nullable: false),
                tariff_code_snapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tariff_name_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                tariff_hourly_rate_toman_snapshot = table.Column<long>(type: "bigint", nullable: false),
                elapsed_ticks = table.Column<long>(type: "bigint", nullable: false),
                amount_toman = table.Column<long>(type: "bigint", nullable: false),
                issued_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                paid_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                paid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_invoices", x => x.id);
                table.CheckConstraint("ck_invoices_amount_positive", "amount_toman > 0");
                table.CheckConstraint("ck_invoices_elapsed_positive", "elapsed_ticks > 0");
                table.CheckConstraint("ck_invoices_rate_positive", "tariff_hourly_rate_toman_snapshot > 0");
                table.CheckConstraint("ck_invoices_status_paid", "status = 1");
                table.ForeignKey("fk_invoices_sessions_session_id", x => x.session_id, "sessions", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_invoices_stations_station_id", x => x.station_id, "stations", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_invoices_customers_customer_id", x => x.customer_id, "customers", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_invoices_tariffs_tariff_id", x => x.tariff_id, "tariffs", "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateTable(
            name: "payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                method = table.Column<int>(type: "integer", nullable: false),
                amount_applied_toman = table.Column<long>(type: "bigint", nullable: false),
                amount_received_toman = table.Column<long>(type: "bigint", nullable: false),
                change_given_toman = table.Column<long>(type: "bigint", nullable: false),
                received_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payments", x => x.id);
                table.CheckConstraint("ck_payments_applied_positive", "amount_applied_toman > 0");
                table.CheckConstraint("ck_payments_received_sufficient", "amount_received_toman >= amount_applied_toman");
                table.CheckConstraint("ck_payments_change_matches", "change_given_toman = amount_received_toman - amount_applied_toman");
                table.CheckConstraint("ck_payments_method_cash", "method = 1");
                table.ForeignKey("fk_payments_invoices_invoice_id", x => x.invoice_id, "invoices", "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateTable(
            name: "billing_ledger_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                entry_type = table.Column<int>(type: "integer", nullable: false),
                amount_toman = table.Column<long>(type: "bigint", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_billing_ledger_entries", x => x.id);
                table.CheckConstraint("ck_billing_ledger_amount_positive", "amount_toman > 0");
                table.CheckConstraint("ck_billing_ledger_entry_type", "entry_type = 1");
                table.ForeignKey("fk_billing_ledger_entries_invoices_invoice_id", x => x.invoice_id, "invoices", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_billing_ledger_entries_payments_payment_id", x => x.payment_id, "payments", "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateIndex("ux_customers_code", "customers", "code", unique: true);
        m.CreateIndex("ix_customers_kind_active", "customers", new[] { "kind", "is_active" });

        m.CreateIndex("ux_tariffs_code", "tariffs", "code", unique: true);
        m.CreateIndex(name: "ux_tariffs_one_active_default_per_type", table: "tariffs", column: "station_type", unique: true, filter: "is_default = TRUE AND is_active = TRUE");

        m.CreateIndex(name: "ux_sessions_one_active_per_station", table: "sessions", column: "station_id", unique: true, filter: "status = 1");
        m.CreateIndex("ix_sessions_customer_status", "sessions", new[] { "customer_id", "status" });
        m.CreateIndex("ix_sessions_started_at_utc", "sessions", "started_at_utc");
        m.CreateIndex("ux_invoices_session_id", "invoices", "session_id", unique: true);
        m.CreateIndex("ix_invoices_paid_at_utc", "invoices", "paid_at_utc");
        m.CreateIndex("ux_payments_invoice_id", "payments", "invoice_id", unique: true);
        m.CreateIndex("ix_payments_received_at_utc", "payments", "received_at_utc");
        m.CreateIndex("ux_billing_ledger_invoice_id", "billing_ledger_entries", "invoice_id", unique: true);
        m.CreateIndex("ux_billing_ledger_payment_id", "billing_ledger_entries", "payment_id", unique: true);
        m.CreateIndex("ix_billing_ledger_occurred_at_utc", "billing_ledger_entries", "occurred_at_utc");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("billing_ledger_entries");
        m.DropTable("payments");
        m.DropTable("invoices");
        m.DropTable("sessions");
        m.DropTable("tariffs");
        m.DropTable("customers");
    }
}
