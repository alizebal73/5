using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class CustomerPasswordLogin : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>(name: "password_hash", table: "customers", type: "character varying(256)", maxLength: 256, nullable: true);
        m.AddColumn<int>(name: "password_failed_attempts", table: "customers", type: "integer", nullable: false, defaultValue: 0);
        m.AddColumn<DateTimeOffset>(name: "password_locked_until_utc", table: "customers", type: "timestamp with time zone", nullable: true);
        m.AddCheckConstraint(name: "ck_customers_password_failed_attempts_range", table: "customers",
            sql: "password_failed_attempts BETWEEN 0 AND 4");
        m.CreateTable(
            name: "customer_login_sessions",
            columns: table => new
            {
                jti = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customer_login_sessions", x => x.jti);
                table.ForeignKey(name: "fk_customer_login_sessions_customers_customer_id", column: x => x.customer_id,
                    principalTable: "customers", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });
        m.CreateIndex(name: "ix_customer_login_sessions_customer_id", table: "customer_login_sessions", column: "customer_id");
        m.CreateIndex(name: "ix_customer_login_sessions_expires_at_utc", table: "customer_login_sessions", column: "expires_at_utc");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM customers WHERE password_hash IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM customer_login_sessions) THEN
                    RAISE EXCEPTION 'CUSTOMER_PASSWORD_LOGIN_DOWN_BLOCKED: customer credentials or login history would be lost';
                END IF;
            END $$;
            """);
        m.DropTable(name: "customer_login_sessions");
        m.DropCheckConstraint(name: "ck_customers_password_failed_attempts_range", table: "customers");
        m.DropColumn(name: "password_hash", table: "customers");
        m.DropColumn(name: "password_failed_attempts", table: "customers");
        m.DropColumn(name: "password_locked_until_utc", table: "customers");
    }
}