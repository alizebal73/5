using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class SessionActorIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "started_by_customer_id",
            table: "sessions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ended_by_system",
            table: "sessions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "paid_by_customer_id",
            table: "sessions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "issued_by_system",
            table: "invoices",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "paid_by_customer_id",
            table: "invoices",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_sessions_started_by_customer_id",
            table: "sessions",
            column: "started_by_customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_sessions_paid_by_customer_id",
            table: "sessions",
            column: "paid_by_customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_invoices_paid_by_customer_id",
            table: "invoices",
            column: "paid_by_customer_id");

        migrationBuilder.AddForeignKey(
            name: "fk_sessions_started_by_customers_customer_id",
            table: "sessions",
            column: "started_by_customer_id",
            principalTable: "customers",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_sessions_paid_by_customers_customer_id",
            table: "sessions",
            column: "paid_by_customer_id",
            principalTable: "customers",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_invoices_paid_by_customers_customer_id",
            table: "invoices",
            column: "paid_by_customer_id",
            principalTable: "customers",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddCheckConstraint(
            name: "ck_sessions_start_actor_shape",
            table: "sessions",
            sql: "((started_by_customer_id IS NULL AND started_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (started_by_customer_id IS NOT NULL AND started_by_operator_id = '00000000-0000-0000-0000-000000000000'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sessions_end_actor_shape",
            table: "sessions",
            sql: "((status = 1 AND ended_by_system = FALSE) OR (status IN (2, 3) AND ((ended_by_system = TRUE AND ended_by_operator_id = '00000000-0000-0000-0000-000000000000') OR (ended_by_system = FALSE AND ended_by_operator_id <> '00000000-0000-0000-0000-000000000000'))))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_sessions_paid_actor_shape",
            table: "sessions",
            sql: "((status IN (1, 2) AND paid_by_customer_id IS NULL) OR (status = 3 AND ((paid_by_customer_id IS NULL AND paid_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (paid_by_customer_id IS NOT NULL AND paid_by_operator_id = '00000000-0000-0000-0000-000000000000'))))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_invoices_issuer_actor_shape",
            table: "invoices",
            sql: "((issued_by_system = TRUE AND issued_by_operator_id = '00000000-0000-0000-0000-000000000000') OR (issued_by_system = FALSE AND issued_by_operator_id <> '00000000-0000-0000-0000-000000000000'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_invoices_paid_actor_shape",
            table: "invoices",
            sql: "((status = 1 AND paid_by_customer_id IS NULL) OR (status IN (2, 3) AND ((paid_by_customer_id IS NULL AND paid_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (paid_by_customer_id IS NOT NULL AND paid_by_operator_id = '00000000-0000-0000-0000-000000000000'))))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM sessions
                    WHERE started_by_customer_id IS NOT NULL
                       OR ended_by_system = TRUE
                       OR paid_by_customer_id IS NOT NULL
                ) OR EXISTS (
                    SELECT 1 FROM invoices
                    WHERE issued_by_system = TRUE
                       OR paid_by_customer_id IS NOT NULL
                ) THEN
                    RAISE EXCEPTION 'SESSION_ACTOR_IDENTITY_DOWN_BLOCKED: customer/system actor history would be lost';
                END IF;
            END $$;
            """);

        migrationBuilder.DropCheckConstraint(name: "ck_sessions_start_actor_shape", table: "sessions");
        migrationBuilder.DropCheckConstraint(name: "ck_sessions_end_actor_shape", table: "sessions");
        migrationBuilder.DropCheckConstraint(name: "ck_sessions_paid_actor_shape", table: "sessions");
        migrationBuilder.DropCheckConstraint(name: "ck_invoices_issuer_actor_shape", table: "invoices");
        migrationBuilder.DropCheckConstraint(name: "ck_invoices_paid_actor_shape", table: "invoices");

        migrationBuilder.DropForeignKey(name: "fk_sessions_started_by_customers_customer_id", table: "sessions");
        migrationBuilder.DropForeignKey(name: "fk_sessions_paid_by_customers_customer_id", table: "sessions");
        migrationBuilder.DropForeignKey(name: "fk_invoices_paid_by_customers_customer_id", table: "invoices");

        migrationBuilder.DropIndex(name: "ix_sessions_started_by_customer_id", table: "sessions");
        migrationBuilder.DropIndex(name: "ix_sessions_paid_by_customer_id", table: "sessions");
        migrationBuilder.DropIndex(name: "ix_invoices_paid_by_customer_id", table: "invoices");

        migrationBuilder.DropColumn(name: "started_by_customer_id", table: "sessions");
        migrationBuilder.DropColumn(name: "ended_by_system", table: "sessions");
        migrationBuilder.DropColumn(name: "paid_by_customer_id", table: "sessions");
        migrationBuilder.DropColumn(name: "issued_by_system", table: "invoices");
        migrationBuilder.DropColumn(name: "paid_by_customer_id", table: "invoices");
    }
}
