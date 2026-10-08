using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class FoundationOperationalBoundaries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                reference_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                reference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                before_json = table.Column<string>(type: "jsonb", nullable: true),
                after_json = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_audit_entries", x => x.id));

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                lease_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                status_code = table.Column<int>(type: "integer", nullable: false),
                response_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_idempotency_records", x => x.id));

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload_json = table.Column<string>(type: "jsonb", nullable: false),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lease_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_OccurredAtUtc_Operation",
            table: "audit_entries",
            columns: new[] { "occurred_at_utc", "operation" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_ReferenceType_ReferenceId",
            table: "audit_entries",
            columns: new[] { "reference_type", "reference_id" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_IdempotencyKey",
            table: "audit_entries",
            column: "idempotency_key");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Scope_Key",
            table: "idempotency_records",
            columns: new[] { "scope", "key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_ExpiresAtUtc",
            table: "idempotency_records",
            column: "expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_EventId",
            table: "outbox_messages",
            column: "event_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_PublishedAtUtc_OccurredAtUtc",
            table: "outbox_messages",
            columns: new[] { "published_at_utc", "occurred_at_utc" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_LeaseExpiresAtUtc_Id",
            table: "outbox_messages",
            columns: new[] { "lease_expires_at_utc", "id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "audit_entries");
        migrationBuilder.DropTable(name: "idempotency_records");
        migrationBuilder.DropTable(name: "outbox_messages");
    }
}
