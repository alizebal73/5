using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace GameNet.Server.Persistence.Migrations;
public partial class StationBoard : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>(name: "request_hash", table: "idempotency_records", type: "character varying(128)", maxLength: 128, nullable: true);
        m.CreateTable(name: "stations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                type = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                agent_device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            }, constraints: table => table.PrimaryKey("pk_stations", x => x.id));
        m.CreateIndex("ix_stations_code", "stations", "code", unique: true);
        m.CreateIndex(name: "ix_stations_agent_device_id", table: "stations", column: "agent_device_id", unique: true, filter: "agent_device_id IS NOT NULL");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("stations");
        m.DropColumn(name: "request_hash", table: "idempotency_records");
    }
}
