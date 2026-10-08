using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FoundationModelAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_PublishedAtUtc_OccurredAtUtc",
                table: "outbox_messages",
                newName: "IX_outbox_messages_published_at_utc_occurred_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_LeaseExpiresAtUtc_Id",
                table: "outbox_messages",
                newName: "IX_outbox_messages_lease_expires_at_utc_id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_EventId",
                table: "outbox_messages",
                newName: "IX_outbox_messages_event_id");

            migrationBuilder.RenameIndex(
                name: "IX_idempotency_records_Scope_Key",
                table: "idempotency_records",
                newName: "IX_idempotency_records_scope_key");

            migrationBuilder.RenameIndex(
                name: "IX_idempotency_records_ExpiresAtUtc",
                table: "idempotency_records",
                newName: "IX_idempotency_records_expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "occurred_at_utc",
                table: "audit_entries",
                newName: "OccurredAtUtc");

            migrationBuilder.RenameIndex(
                name: "IX_audit_entries_OccurredAtUtc_Operation",
                table: "audit_entries",
                newName: "IX_audit_entries_OccurredAtUtc_operation");

            migrationBuilder.RenameIndex(
                name: "IX_audit_entries_ReferenceType_ReferenceId",
                table: "audit_entries",
                newName: "IX_audit_entries_reference_type_reference_id");

            migrationBuilder.RenameIndex(
                name: "IX_audit_entries_IdempotencyKey",
                table: "audit_entries",
                newName: "IX_audit_entries_idempotency_key");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payload_json",
                table: "outbox_messages",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                table: "idempotency_records",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "scope",
                table: "idempotency_records",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "response_json",
                table: "idempotency_records",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "operation",
                table: "idempotency_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "lease_token",
                table: "idempotency_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "key",
                table: "idempotency_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "audit_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "outcome",
                table: "audit_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "operation",
                table: "audit_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "correlation_id",
                table: "audit_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "actor_type",
                table: "audit_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "secret_hash",
                table: "agent_credentials",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "agent_credentials",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "lease_token",
                table: "agent_connection_leases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "connection_id",
                table: "agent_connection_leases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_published_at_utc_occurred_at_utc",
                table: "outbox_messages",
                newName: "IX_outbox_messages_PublishedAtUtc_OccurredAtUtc");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_lease_expires_at_utc_id",
                table: "outbox_messages",
                newName: "IX_outbox_messages_LeaseExpiresAtUtc_Id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_event_id",
                table: "outbox_messages",
                newName: "IX_outbox_messages_EventId");

            migrationBuilder.RenameIndex(
                name: "IX_idempotency_records_scope_key",
                table: "idempotency_records",
                newName: "IX_idempotency_records_Scope_Key");

            migrationBuilder.RenameIndex(
                name: "IX_idempotency_records_expires_at_utc",
                table: "idempotency_records",
                newName: "IX_idempotency_records_ExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "OccurredAtUtc",
                table: "audit_entries",
                newName: "occurred_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_audit_entries_OccurredAtUtc_operation",
                table: "audit_entries",
                newName: "IX_audit_entries_OccurredAtUtc_Operation");

            migrationBuilder.RenameIndex(
                name: "IX_audit_entries_reference_type_reference_id",
                table: "audit_entries",
                newName: "IX_audit_entries_ReferenceType_ReferenceId");

            migrationBuilder.RenameIndex(
                name: "IX_audit_entries_idempotency_key",
                table: "audit_entries",
                newName: "IX_audit_entries_IdempotencyKey");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "payload_json",
                table: "outbox_messages",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "state",
                table: "idempotency_records",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "scope",
                table: "idempotency_records",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "response_json",
                table: "idempotency_records",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "operation",
                table: "idempotency_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "lease_token",
                table: "idempotency_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "key",
                table: "idempotency_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "audit_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "outcome",
                table: "audit_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "operation",
                table: "audit_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "correlation_id",
                table: "audit_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "actor_type",
                table: "audit_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "secret_hash",
                table: "agent_credentials",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                table: "agent_credentials",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "lease_token",
                table: "agent_connection_leases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "connection_id",
                table: "agent_connection_leases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
