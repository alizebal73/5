using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class OutboxDeliveryReliability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_inbox_receipts",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_inbox_receipts", x => x.event_id);
            });

        migrationBuilder.AddColumn<int>(
            name: "attempt_count",
            table: "outbox_messages",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_attempt_at_utc",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_failure_code",
            table: "outbox_messages",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "dead_lettered_at_utc",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_delivery_due",
            table: "outbox_messages",
            columns: new[] { "next_attempt_at_utc", "id" },
            filter: "published_at_utc IS NULL AND dead_lettered_at_utc IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_inbox_receipts");

        migrationBuilder.DropIndex(
            name: "ix_outbox_messages_delivery_due",
            table: "outbox_messages");

        migrationBuilder.DropColumn(name: "attempt_count", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "next_attempt_at_utc", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "last_failure_code", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "dead_lettered_at_utc", table: "outbox_messages");
    }
}
