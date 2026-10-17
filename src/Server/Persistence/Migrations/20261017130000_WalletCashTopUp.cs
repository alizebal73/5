using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class WalletCashTopUp : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(
            name: "wallet_accounts",
            columns: table => new
            {
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchased_balance_toman = table.Column<long>(type: "bigint", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_wallet_accounts", x => x.customer_id);
                table.CheckConstraint("ck_wallet_accounts_purchased_balance_nonnegative", "purchased_balance_toman >= 0");
                table.CheckConstraint("ck_wallet_accounts_version_nonnegative", "version >= 0");
                table.ForeignKey("fk_wallet_accounts_customers_customer_id", x => x.customer_id,
                    principalTable: "customers", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateTable(
            name: "cash_receipts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                purpose = table.Column<int>(type: "integer", nullable: false),
                reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount_applied_toman = table.Column<long>(type: "bigint", nullable: false),
                amount_received_toman = table.Column<long>(type: "bigint", nullable: false),
                change_given_toman = table.Column<long>(type: "bigint", nullable: false),
                method = table.Column<int>(type: "integer", nullable: false),
                received_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cash_receipts", x => x.id);
                table.CheckConstraint("ck_cash_receipts_applied_positive", "amount_applied_toman > 0");
                table.CheckConstraint("ck_cash_receipts_received_sufficient", "amount_received_toman >= amount_applied_toman");
                table.CheckConstraint("ck_cash_receipts_change_matches", "change_given_toman = amount_received_toman - amount_applied_toman");
                table.CheckConstraint("ck_cash_receipts_wallet_top_up_purpose", "purpose = 1");
                table.CheckConstraint("ck_cash_receipts_cash_method", "method = 1");
                table.ForeignKey("fk_cash_receipts_customers_customer_id", x => x.customer_id,
                    principalTable: "customers", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateTable(
            name: "wallet_ledger_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                cash_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                entry_type = table.Column<int>(type: "integer", nullable: false),
                amount_toman = table.Column<long>(type: "bigint", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_wallet_ledger_entries", x => x.id);
                table.CheckConstraint("ck_wallet_ledger_amount_positive", "amount_toman > 0");
                table.CheckConstraint("ck_wallet_ledger_entry_type", "entry_type = 1");
                table.ForeignKey("fk_wallet_ledger_wallet_accounts_customer_id", x => x.customer_id,
                    principalTable: "wallet_accounts", principalColumn: "customer_id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_wallet_ledger_cash_receipts_cash_receipt_id", x => x.cash_receipt_id,
                    principalTable: "cash_receipts", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });

        m.CreateIndex(name: "ix_wallet_accounts_updated_at_utc", table: "wallet_accounts", column: "updated_at_utc");
        m.CreateIndex(name: "ux_cash_receipts_purpose_reference_id", table: "cash_receipts",
            columns: new[] { "purpose", "reference_id" }, unique: true);
        m.CreateIndex(name: "ix_cash_receipts_customer_id", table: "cash_receipts", column: "customer_id");
        m.CreateIndex(name: "ix_cash_receipts_received_at_utc", table: "cash_receipts", column: "received_at_utc");
        m.CreateIndex(name: "ux_wallet_ledger_cash_receipt_id", table: "wallet_ledger_entries", column: "cash_receipt_id", unique: true);
        m.CreateIndex(name: "ix_wallet_ledger_customer_occurred_at_utc", table: "wallet_ledger_entries",
            columns: new[] { "customer_id", "occurred_at_utc" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM wallet_ledger_entries)
                   OR EXISTS (SELECT 1 FROM cash_receipts)
                   OR EXISTS (SELECT 1 FROM wallet_accounts) THEN
                    RAISE EXCEPTION 'WALLET_CASH_TOP_UP_DOWN_BLOCKED: wallet balances or cash receipt evidence would be lost';
                END IF;
            END $$;
            """);
        m.DropTable(name: "wallet_ledger_entries");
        m.DropTable(name: "cash_receipts");
        m.DropTable(name: "wallet_accounts");
    }
}
