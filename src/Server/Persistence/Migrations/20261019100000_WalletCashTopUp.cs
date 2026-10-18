using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class WalletCashTopUp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cash_receipts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                purpose = table.Column<int>(type: "integer", nullable: false),
                amount_applied_toman = table.Column<long>(type: "bigint", nullable: false),
                amount_received_toman = table.Column<long>(type: "bigint", nullable: false),
                change_given_toman = table.Column<long>(type: "bigint", nullable: false),
                received_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cash_receipts", x => x.id);
                table.CheckConstraint("ck_cash_receipts_applied_positive", "amount_applied_toman > 0");
                table.CheckConstraint("ck_cash_receipts_received_sufficient", "amount_received_toman >= amount_applied_toman");
                table.CheckConstraint("ck_cash_receipts_change_matches", "change_given_toman = amount_received_toman - amount_applied_toman");
                table.CheckConstraint("ck_cash_receipts_purpose", "purpose = 1");
                table.ForeignKey("fk_cash_receipts_customers_customer_id", x => x.customer_id, "customers", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "wallet_accounts",
            columns: table => new
            {
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                balance_toman = table.Column<long>(type: "bigint", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_wallet_accounts", x => x.customer_id);
                table.CheckConstraint("ck_wallet_accounts_balance_nonnegative", "balance_toman >= 0");
                table.CheckConstraint("ck_wallet_accounts_version_nonnegative", "version >= 0");
                table.ForeignKey("fk_wallet_accounts_customers_customer_id", x => x.customer_id, "customers", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "wallet_ledger_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                cash_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                entry_type = table.Column<int>(type: "integer", nullable: false),
                amount_toman = table.Column<long>(type: "bigint", nullable: false),
                balance_after_toman = table.Column<long>(type: "bigint", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_wallet_ledger_entries", x => x.id);
                table.CheckConstraint("ck_wallet_ledger_amount_positive", "amount_toman > 0");
                table.CheckConstraint("ck_wallet_ledger_balance_after_covers_credit", "balance_after_toman >= amount_toman");
                table.CheckConstraint("ck_wallet_ledger_entry_type", "entry_type = 1");
                table.ForeignKey("fk_wallet_ledger_customers_customer_id", x => x.customer_id, "customers", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_wallet_ledger_cash_receipts_cash_receipt_id", x => x.cash_receipt_id, "cash_receipts", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "ux_cash_receipts_operator_idempotency", table: "cash_receipts",
            columns: new[] { "received_by_operator_id", "idempotency_key" }, unique: true);
        migrationBuilder.CreateIndex(name: "ix_cash_receipts_customer_received_at", table: "cash_receipts",
            columns: new[] { "customer_id", "received_at_utc" });
        migrationBuilder.CreateIndex(name: "ix_wallet_ledger_customer_occurred_at", table: "wallet_ledger_entries",
            columns: new[] { "customer_id", "occurred_at_utc" });
        migrationBuilder.CreateIndex(name: "ux_wallet_ledger_cash_receipt", table: "wallet_ledger_entries",
            column: "cash_receipt_id", unique: true);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION reject_wallet_financial_record_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION 'WALLET_FINANCIAL_RECORD_APPEND_ONLY: % cannot be updated or deleted', TG_TABLE_NAME;
            END;
            $$;

            CREATE TRIGGER cash_receipts_append_only
            BEFORE UPDATE OR DELETE ON cash_receipts
            FOR EACH ROW EXECUTE FUNCTION reject_wallet_financial_record_mutation();

            CREATE TRIGGER wallet_ledger_entries_append_only
            BEFORE UPDATE OR DELETE ON wallet_ledger_entries
            FOR EACH ROW EXECUTE FUNCTION reject_wallet_financial_record_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM wallet_ledger_entries)
                   OR EXISTS (SELECT 1 FROM cash_receipts)
                   OR EXISTS (SELECT 1 FROM wallet_accounts) THEN
                    RAISE EXCEPTION 'WALLET_CASH_TOP_UP_DOWN_BLOCKED: financial records would be lost';
                END IF;
            END $$;
            DROP TRIGGER IF EXISTS wallet_ledger_entries_append_only ON wallet_ledger_entries;
            DROP TRIGGER IF EXISTS cash_receipts_append_only ON cash_receipts;
            DROP FUNCTION IF EXISTS reject_wallet_financial_record_mutation();
            """);
        migrationBuilder.DropTable(name: "wallet_ledger_entries");
        migrationBuilder.DropTable(name: "wallet_accounts");
        migrationBuilder.DropTable(name: "cash_receipts");
    }
}