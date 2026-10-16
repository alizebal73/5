using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class CashTenderCorrection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cash_tender_corrections",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                previous_amount_received_toman = table.Column<long>(type: "bigint", nullable: false),
                previous_change_given_toman = table.Column<long>(type: "bigint", nullable: false),
                corrected_amount_received_toman = table.Column<long>(type: "bigint", nullable: false),
                corrected_change_given_toman = table.Column<long>(type: "bigint", nullable: false),
                corrected_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                corrected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cash_tender_corrections", x => x.id);
                table.CheckConstraint("ck_cash_tender_corrections_version_positive", "version > 0");
                table.CheckConstraint("ck_cash_tender_corrections_previous_received_positive", "previous_amount_received_toman > 0");
                table.CheckConstraint("ck_cash_tender_corrections_previous_change_nonnegative", "previous_change_given_toman >= 0");
                table.CheckConstraint("ck_cash_tender_corrections_corrected_received_positive", "corrected_amount_received_toman > 0");
                table.CheckConstraint("ck_cash_tender_corrections_corrected_change_nonnegative", "corrected_change_given_toman >= 0");
                table.CheckConstraint("ck_cash_tender_corrections_reason_valid",
                    "char_length(btrim(reason)) >= 3 AND btrim(reason) !~ '[[:cntrl:]]'");
                table.ForeignKey(name: "fk_cash_tender_corrections_invoices_invoice_id", column: x => x.invoice_id,
                    principalTable: "invoices", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "fk_cash_tender_corrections_payments_payment_id", column: x => x.payment_id,
                    principalTable: "payments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "ux_cash_tender_corrections_payment_version",
            table: "cash_tender_corrections", columns: new[] { "payment_id", "version" }, unique: true);
        migrationBuilder.CreateIndex(name: "ux_cash_tender_corrections_invoice_version",
            table: "cash_tender_corrections", columns: new[] { "invoice_id", "version" }, unique: true);
        migrationBuilder.CreateIndex(name: "ix_cash_tender_corrections_corrected_at_utc",
            table: "cash_tender_corrections", column: "corrected_at_utc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM cash_tender_corrections) THEN
                    RAISE EXCEPTION 'CASH_TENDER_CORRECTION_DOWN_BLOCKED: correction history prevents downgrade';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(name: "cash_tender_corrections");
    }
}
