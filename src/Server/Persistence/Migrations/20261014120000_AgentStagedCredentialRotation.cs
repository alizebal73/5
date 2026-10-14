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
