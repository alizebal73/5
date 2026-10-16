using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class CustomerManagementCore : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>(name: "nickname", table: "customers", type: "character varying(60)", maxLength: 60, nullable: true);
        m.AddColumn<string>(name: "phone_number", table: "customers", type: "character varying(32)", maxLength: 32, nullable: true);
        m.AddColumn<bool>(name: "is_blocked", table: "customers", type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM customers WHERE nickname IS NOT NULL OR phone_number IS NOT NULL OR is_blocked = TRUE) THEN
                    RAISE EXCEPTION 'CUSTOMER_MANAGEMENT_DOWN_BLOCKED: customer profile/status data would be lost';
                END IF;
            END $$;
            """);
        m.DropColumn(name: "nickname", table: "customers");
        m.DropColumn(name: "phone_number", table: "customers");
        m.DropColumn(name: "is_blocked", table: "customers");
    }
}
