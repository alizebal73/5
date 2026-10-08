using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

[DbContext(typeof(GameNetDbContext))]
[Migration("20261009010000_OperatorIdentity")]
public sealed class OperatorIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "operator_users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                failed_login_count = table.Column<int>(type: "integer", nullable: false),
                lockout_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_operator_users", x => x.id));

        migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_roles", x => x.id));

        migrationBuilder.CreateTable(
            name: "auth_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                jti = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_sessions", x => x.id);
                table.ForeignKey("fk_auth_sessions_operator_users_user_id", x => x.user_id,
                    "operator_users", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_roles",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                table.ForeignKey("fk_user_roles_operator_users_user_id", x => x.user_id,
                    "operator_users", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("fk_user_roles_roles_role_id", x => x.role_id,
                    "roles", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "role_permissions",
            columns: table => new
            {
                role_id = table.Column<Guid>(type: "uuid", nullable: false),
                permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission });
                table.ForeignKey("fk_role_permissions_roles_role_id", x => x.role_id,
                    "roles", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("ix_operator_users_username", "operator_users", "username", unique: true);
        migrationBuilder.CreateIndex("ix_roles_code", "roles", "code", unique: true);
        migrationBuilder.CreateIndex("ix_auth_sessions_jti", "auth_sessions", "jti", unique: true);
        migrationBuilder.CreateIndex("ix_user_roles_role_id", "user_roles", "role_id");
        migrationBuilder.CreateIndex("ix_role_permissions_permission", "role_permissions", "permission");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("auth_sessions");
        migrationBuilder.DropTable("role_permissions");
        migrationBuilder.DropTable("user_roles");
        migrationBuilder.DropTable("roles");
        migrationBuilder.DropTable("operator_users");
    }
}
