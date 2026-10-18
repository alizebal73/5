using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class CashSaleReversal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_invoices_status_range", table: "invoices");
        migrationBuilder.AddCheckConstraint(name: "ck_invoices_status_range", table: "invoices", sql: "status BETWEEN 1 AND 3");
        migrationBuilder.DropCheckConstraint(name: "ck_invoices_payment_state", table: "invoices");
        migrationBuilder.AddCheckConstraint(
            name: "ck_invoices_payment_state",
            table: "invoices",
            sql: "(status = 1 AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL) OR (status IN (2, 3) AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL)");

        migrationBuilder.DropCheckConstraint(name: "ck_billing_ledger_entry_type", table: "billing_ledger_entries");
        migrationBuilder.AddCheckConstraint(
            name: "ck_billing_ledger_entry_type",
            table: "billing_ledger_entries",
            sql: "entry_type IN (1, 2)");

        migrationBuilder.DropIndex(name: "ux_billing_ledger_invoice_id", table: "billing_ledger_entries");
        migrationBuilder.DropIndex(name: "ux_billing_ledger_payment_id", table: "billing_ledger_entries");
        migrationBuilder.CreateIndex(name: "ux_billing_ledger_invoice_cash_sale", table: "billing_ledger_entries",
            column: "invoice_id", unique: true, filter: "entry_type = 1");
        migrationBuilder.CreateIndex(name: "ux_billing_ledger_invoice_cash_reversal", table: "billing_ledger_entries",
            column: "invoice_id", unique: true, filter: "entry_type = 2");
        migrationBuilder.CreateIndex(name: "ux_billing_ledger_payment_cash_sale", table: "billing_ledger_entries",
            column: "payment_id", unique: true, filter: "entry_type = 1");
        migrationBuilder.CreateIndex(name: "ux_billing_ledger_payment_cash_reversal", table: "billing_ledger_entries",
            column: "payment_id", unique: true, filter: "entry_type = 2");

        migrationBuilder.CreateTable(
            name: "cash_sale_reversals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount_toman = table.Column<long>(type: "bigint", nullable: false),
                reversed_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                reversed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cash_sale_reversals", x => x.id);
                table.CheckConstraint("ck_cash_sale_reversals_amount_positive", "amount_toman > 0");
                table.CheckConstraint(
                    "ck_cash_sale_reversals_reason_valid",
                    "char_length(btrim(reason)) >= 3 AND btrim(reason) !~ '[[:cntrl:]]'");
                table.ForeignKey(name: "fk_cash_sale_reversals_invoices_invoice_id", column: x => x.invoice_id,
                    principalTable: "invoices", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "fk_cash_sale_reversals_payments_payment_id", column: x => x.payment_id,
                    principalTable: "payments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "ux_cash_sale_reversals_invoice_id", table: "cash_sale_reversals",
            column: "invoice_id", unique: true);
        migrationBuilder.CreateIndex(name: "ux_cash_sale_reversals_payment_id", table: "cash_sale_reversals",
            column: "payment_id", unique: true);
        migrationBuilder.CreateIndex(name: "ix_cash_sale_reversals_reversed_at_utc", table: "cash_sale_reversals",
            column: "reversed_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM cash_sale_reversals) THEN
                    RAISE EXCEPTION 'CASH_SALE_REVERSAL_DOWN_BLOCKED: financial reversal history prevents downgrade';
                END IF;
                IF EXISTS (SELECT 1 FROM invoices WHERE status = 3) THEN
                    RAISE EXCEPTION 'CASH_SALE_REVERSAL_DOWN_BLOCKED: reversed invoice lifecycle prevents downgrade';
                END IF;
                IF EXISTS (SELECT 1 FROM billing_ledger_entries WHERE entry_type = 2) THEN
                    RAISE EXCEPTION 'CASH_SALE_REVERSAL_DOWN_BLOCKED: compensating ledger history prevents downgrade';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(name: "cash_sale_reversals");

        migrationBuilder.DropIndex(name: "ux_billing_ledger_invoice_cash_sale", table: "billing_ledger_entries");
        migrationBuilder.DropIndex(name: "ux_billing_ledger_invoice_cash_reversal", table: "billing_ledger_entries");
        migrationBuilder.DropIndex(name: "ux_billing_ledger_payment_cash_sale", table: "billing_ledger_entries");
        migrationBuilder.DropIndex(name: "ux_billing_ledger_payment_cash_reversal", table: "billing_ledger_entries");
        migrationBuilder.CreateIndex(name: "ux_billing_ledger_invoice_id", table: "billing_ledger_entries",
            column: "invoice_id", unique: true);
        migrationBuilder.CreateIndex(name: "ux_billing_ledger_payment_id", table: "billing_ledger_entries",
            column: "payment_id", unique: true);

        migrationBuilder.DropCheckConstraint(name: "ck_billing_ledger_entry_type", table: "billing_ledger_entries");
        migrationBuilder.AddCheckConstraint(name: "ck_billing_ledger_entry_type", table: "billing_ledger_entries", sql: "entry_type = 1");
        migrationBuilder.DropCheckConstraint(name: "ck_invoices_payment_state", table: "invoices");
        migrationBuilder.AddCheckConstraint(name: "ck_invoices_payment_state", table: "invoices",
            sql: "(status = 1 AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL) OR (status = 2 AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL)");
        migrationBuilder.DropCheckConstraint(name: "ck_invoices_status_range", table: "invoices");
        migrationBuilder.AddCheckConstraint(name: "ck_invoices_status_range", table: "invoices", sql: "status BETWEEN 1 AND 2");
    }
}
