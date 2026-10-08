using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class AgentFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agent_connection_leases",
            columns: table => new
            {
                device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                connection_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                lease_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                agent_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                station_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_agent_connection_leases", x => x.device_id));

        migrationBuilder.CreateTable(
            name: "agent_credentials",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                secret_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_authenticated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_agent_credentials", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_agent_connection_leases_Expires_Device",
            table: "agent_connection_leases",
            columns: new[] { "lease_expires_at_utc", "device_id" });

        migrationBuilder.CreateIndex(
            name: "IX_agent_credentials_ActiveDevice",
            table: "agent_credentials",
            column: "device_id",
            unique: true,
            filter: "revoked_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_agent_credentials_Device_Created",
            table: "agent_credentials",
            columns: new[] { "device_id", "created_at_utc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agent_connection_leases");
        migrationBuilder.DropTable(name: "agent_credentials");
    }
}
