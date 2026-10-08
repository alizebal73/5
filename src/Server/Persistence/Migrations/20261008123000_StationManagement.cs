using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

[DbContext(typeof(GameNetDbContext))]
[Migration("20261008123000_StationManagement")]
public partial class StationManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_entries",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                reference_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                reference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                reason = table.Column<string>(type: "text", nullable: true),
                before_json = table.Column<string>(type: "text", nullable: true),
                after_json = table.Column<string>(type: "text", nullable: true),
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                command_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_audit_entries", x => x.id));

        migrationBuilder.CreateTable(
            name: "idempotency_entries",
            columns: table => new
            {
                scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                lease_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                claimed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed = table.Column<bool>(type: "boolean", nullable: false),
                status_code = table.Column<int>(type: "integer", nullable: true),
                response_json = table.Column<string>(type: "text", nullable: true),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_idempotency_entries", x => new { x.scope, x.key }));

        migrationBuilder.CreateTable(
            name: "stations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                type = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_stations", x => x.id));

        migrationBuilder.CreateIndex(
            name: "ix_stations_code",
            table: "stations",
            column: "code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "audit_entries");
        migrationBuilder.DropTable(name: "idempotency_entries");
        migrationBuilder.DropTable(name: "stations");
    }
}
