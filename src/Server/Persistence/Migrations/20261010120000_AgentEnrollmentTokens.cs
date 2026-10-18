using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class AgentEnrollmentTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agent_enrollment_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                issued_by_operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_agent_enrollment_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_agent_enrollment_tokens_issued_by_operator",
                    column: x => x.issued_by_operator_id,
                    principalTable: "operator_users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_agent_enrollment_tokens_token_hash",
            table: "agent_enrollment_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_agent_enrollment_tokens_pending_device",
            table: "agent_enrollment_tokens",
            column: "device_id",
            unique: true,
            filter: "redeemed_at_utc IS NULL AND revoked_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_agent_enrollment_tokens_expires_at_utc",
            table: "agent_enrollment_tokens",
            column: "expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_agent_enrollment_tokens_issued_by_operator_id",
            table: "agent_enrollment_tokens",
            column: "issued_by_operator_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agent_enrollment_tokens");
    }
}
