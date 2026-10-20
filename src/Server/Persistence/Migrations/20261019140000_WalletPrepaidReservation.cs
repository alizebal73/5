using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class WalletPrepaidReservation : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<long>(name: "reserved_balance_toman", table: "wallet_accounts", type: "bigint", nullable: false, defaultValue: 0L);

        m.AddColumn<long>(name: "reserved_balance_after_toman", table: "wallet_ledger_entries", type: "bigint", nullable: false, defaultValue: 0L);
        m.AddColumn<string>(name: "actor_type", table: "wallet_ledger_entries", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Operator");
        m.AddColumn<Guid>(name: "session_id", table: "wallet_ledger_entries", type: "uuid", nullable: true);
        m.AlterColumn<Guid>(
            name: "cash_receipt_id", table: "wallet_ledger_entries", type: "uuid", nullable: true,
            oldClrType: typeof(Guid), oldType: "uuid");

        m.DropCheckConstraint(name: "ck_wallet_ledger_balance_after_covers_credit", table: "wallet_ledger_entries");
        m.DropCheckConstraint(name: "ck_wallet_ledger_entry_type", table: "wallet_ledger_entries");
        m.AddCheckConstraint(name: "ck_wallet_ledger_balance_nonnegative", table: "wallet_ledger_entries", sql: "balance_after_toman >= 0");
        m.AddCheckConstraint(name: "ck_wallet_ledger_reserved_nonnegative", table: "wallet_ledger_entries", sql: "reserved_balance_after_toman >= 0");
        m.AddCheckConstraint(name: "ck_wallet_ledger_entry_type", table: "wallet_ledger_entries", sql: "entry_type BETWEEN 1 AND 4");
        m.AddCheckConstraint(name: "ck_wallet_ledger_entry_shape", table: "wallet_ledger_entries",
            sql: "((entry_type = 1 AND cash_receipt_id IS NOT NULL AND session_id IS NULL AND balance_after_toman >= amount_toman) OR (entry_type BETWEEN 2 AND 4 AND cash_receipt_id IS NULL AND session_id IS NOT NULL))");
        m.AddCheckConstraint(name: "ck_wallet_ledger_actor_type", table: "wallet_ledger_entries",
            sql: "actor_type IN ('Operator', 'Customer', 'System')");
        m.AddCheckConstraint(name: "ck_wallet_accounts_reserved_nonnegative", table: "wallet_accounts", sql: "reserved_balance_toman >= 0");

        m.CreateIndex(name: "ux_wallet_ledger_session_entry_type", table: "wallet_ledger_entries",
            columns: new[] { "session_id", "entry_type" }, unique: true, filter: "session_id IS NOT NULL");
        m.CreateIndex(name: "ix_wallet_ledger_session_id", table: "wallet_ledger_entries", column: "session_id");
        m.AddForeignKey(name: "fk_wallet_ledger_sessions_session_id", table: "wallet_ledger_entries",
            column: "session_id", principalTable: "sessions", principalColumn: "id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM wallet_ledger_entries WHERE entry_type <> 1 OR session_id IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM wallet_accounts WHERE reserved_balance_toman <> 0) THEN
                    RAISE EXCEPTION 'WALLET_PREPAID_RESERVATION_DOWN_BLOCKED: Session funding history or reserved value would be lost';
                END IF;
            END $$;
            """);
        m.DropForeignKey(name: "fk_wallet_ledger_sessions_session_id", table: "wallet_ledger_entries");
        m.DropIndex(name: "ix_wallet_ledger_session_id", table: "wallet_ledger_entries");
        m.DropIndex(name: "ux_wallet_ledger_session_entry_type", table: "wallet_ledger_entries");
        m.DropCheckConstraint(name: "ck_wallet_accounts_reserved_nonnegative", table: "wallet_accounts");
        m.DropCheckConstraint(name: "ck_wallet_ledger_actor_type", table: "wallet_ledger_entries");
        m.DropCheckConstraint(name: "ck_wallet_ledger_entry_shape", table: "wallet_ledger_entries");
        m.DropCheckConstraint(name: "ck_wallet_ledger_reserved_nonnegative", table: "wallet_ledger_entries");
        m.DropCheckConstraint(name: "ck_wallet_ledger_balance_nonnegative", table: "wallet_ledger_entries");
        m.DropCheckConstraint(name: "ck_wallet_ledger_entry_type", table: "wallet_ledger_entries");
        m.AddCheckConstraint(name: "ck_wallet_ledger_entry_type", table: "wallet_ledger_entries", sql: "entry_type = 1");
        m.AddCheckConstraint(name: "ck_wallet_ledger_balance_after_covers_credit", table: "wallet_ledger_entries",
            sql: "balance_after_toman >= amount_toman");
        m.AlterColumn<Guid>(
            name: "cash_receipt_id", table: "wallet_ledger_entries", type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        m.DropColumn(name: "reserved_balance_after_toman", table: "wallet_ledger_entries");
        m.DropColumn(name: "actor_type", table: "wallet_ledger_entries");
        m.DropColumn(name: "session_id", table: "wallet_ledger_entries");
        m.DropColumn(name: "reserved_balance_toman", table: "wallet_accounts");
    }
}
