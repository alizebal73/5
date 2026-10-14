using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class AgentStagedCredentialRotation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("is_pending_rotation", "agent_credentials", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<Guid>("rotation_operation_id", "agent_credentials", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>("replaces_credential_id", "agent_credentials", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("rotation_expires_at_utc", "agent_credentials", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>("rotation_requested_operation_id", "agent_credentials", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("rotation_requested_at_utc", "agent_credentials", type: "timestamp with time zone", nullable: true);

        migrationBuilder.DropIndex(name: "IX_agent_credentials_ActiveDevice", table: "agent_credentials");
        migrationBuilder.CreateIndex(name: "IX_agent_credentials_ActiveDevice", table: "agent_credentials",
            column: "device_id", unique: true, filter: "revoked_at_utc IS NULL AND is_pending_rotation = FALSE");
        migrationBuilder.CreateIndex(name: "IX_agent_credentials_PendingRotationDevice", table: "agent_credentials",
            column: "device_id", unique: true, filter: "revoked_at_utc IS NULL AND is_pending_rotation = TRUE");
        migrationBuilder.CreateIndex(name: "IX_agent_credentials_RotationOperation", table: "agent_credentials",
            column: "rotation_operation_id", unique: true, filter: "rotation_operation_id IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_agent_credentials_RotationRequestedOperation", table: "agent_credentials",
            column: "rotation_requested_operation_id", unique: true, filter: "rotation_requested_operation_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_AgentCredentialRotationRequestOperation",
            table: "audit_entries",
            columns: new[] { "reference_type", "reference_id" },
            unique: true,
            filter: "reference_type = 'AgentCredentialRotationRequest' AND reference_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A schema rollback must not reactivate or strand a staged replacement.
        // The legacy active-device index has no pending-rotation predicate, so an
        // unrevoked staged row would collide with the prior active credential when
        // the new columns are dropped. Cancel and audit in-flight staged rotations
        // before restoring that legacy invariant.
        migrationBuilder.Sql("""
            INSERT INTO audit_entries (
                id, occurred_at_utc, actor_type, actor_id, operation,
                reference_type, reference_id, reason, correlation_id, source, outcome)
            SELECT
                gen_random_uuid(), clock_timestamp(), 'System', device_id,
                'AgentCredential.RotationCancelledByRollback',
                'AgentCredentialRotationRollback', id::text,
                'Pending credential rotation was cancelled by schema rollback; the prior credential was retained.',
                gen_random_uuid()::text, 'ServerMigration', 'Cancelled'
            FROM agent_credentials
            WHERE revoked_at_utc IS NULL AND is_pending_rotation = TRUE;

            INSERT INTO audit_entries (
                id, occurred_at_utc, actor_type, actor_id, operation,
                reference_type, reference_id, reason, correlation_id, source, outcome)
            SELECT
                gen_random_uuid(), clock_timestamp(), 'System', device_id,
                'AgentCredential.RotationRequestCancelledByRollback',
                'AgentCredentialRotationRequestRollback', rotation_requested_operation_id::text,
                'Credential rotation request was cancelled by schema rollback; the current credential was retained.',
                gen_random_uuid()::text, 'ServerMigration', 'Cancelled'
            FROM agent_credentials
            WHERE revoked_at_utc IS NULL
              AND is_pending_rotation = FALSE
              AND rotation_requested_operation_id IS NOT NULL;

            UPDATE agent_credentials
            SET revoked_at_utc = clock_timestamp(),
                is_pending_rotation = FALSE,
                rotation_expires_at_utc = NULL
            WHERE revoked_at_utc IS NULL AND is_pending_rotation = TRUE;

            UPDATE agent_credentials
            SET rotation_requested_operation_id = NULL,
                rotation_requested_at_utc = NULL
            WHERE revoked_at_utc IS NULL
              AND is_pending_rotation = FALSE
              AND rotation_requested_operation_id IS NOT NULL;
            """);

        migrationBuilder.DropIndex(
            name: "IX_audit_entries_AgentCredentialRotationRequestOperation",
            table: "audit_entries");
        migrationBuilder.DropIndex(name: "IX_agent_credentials_RotationRequestedOperation", table: "agent_credentials");
        migrationBuilder.DropIndex(name: "IX_agent_credentials_RotationOperation", table: "agent_credentials");
        migrationBuilder.DropIndex(name: "IX_agent_credentials_PendingRotationDevice", table: "agent_credentials");
        migrationBuilder.DropIndex(name: "IX_agent_credentials_ActiveDevice", table: "agent_credentials");

        migrationBuilder.DropColumn("rotation_requested_at_utc", "agent_credentials");
        migrationBuilder.DropColumn("rotation_requested_operation_id", "agent_credentials");
        migrationBuilder.DropColumn("rotation_expires_at_utc", "agent_credentials");
        migrationBuilder.DropColumn("replaces_credential_id", "agent_credentials");
        migrationBuilder.DropColumn("rotation_operation_id", "agent_credentials");
        migrationBuilder.DropColumn("is_pending_rotation", "agent_credentials");

        migrationBuilder.CreateIndex(name: "IX_agent_credentials_ActiveDevice", table: "agent_credentials",
            column: "device_id", unique: true, filter: "revoked_at_utc IS NULL");
    }
}
