using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class SessionMonotonicTiming : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<long>(name: "started_monotonic_timestamp", table: "sessions", type: "bigint", nullable: true);
        m.AddColumn<long>(name: "started_monotonic_frequency", table: "sessions", type: "bigint", nullable: true);
        m.AddColumn<long>(name: "started_system_uptime_milliseconds", table: "sessions", type: "bigint", nullable: true);
        m.AddColumn<Guid>(name: "started_boot_identifier", table: "sessions", type: "uuid", nullable: true);
        m.AddColumn<long>(name: "elapsed_ticks", table: "sessions", type: "bigint", nullable: true);

        // Existing active Sessions have no trustworthy monotonic baseline; the sentinel makes them fail closed.
        // Completed Sessions retain the duration already persisted by their invoice.
        m.Sql("""
            UPDATE sessions
            SET started_monotonic_timestamp = 0,
                started_monotonic_frequency = 1,
                started_system_uptime_milliseconds = 0,
                started_boot_identifier = '00000000-0000-0000-0000-000000000000';

            UPDATE sessions AS s
            SET elapsed_ticks = i.elapsed_ticks
            FROM invoices AS i
            WHERE i.session_id = s.id AND s.status IN (2, 3);
            """);

        m.AlterColumn<long>(name: "started_monotonic_timestamp", table: "sessions", type: "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        m.AlterColumn<long>(name: "started_monotonic_frequency", table: "sessions", type: "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        m.AlterColumn<long>(name: "started_system_uptime_milliseconds", table: "sessions", type: "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        m.AlterColumn<Guid>(name: "started_boot_identifier", table: "sessions", type: "uuid",
            nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

        m.DropCheckConstraint(name: "ck_sessions_time_state", table: "sessions");
        m.AddCheckConstraint(
            name: "ck_sessions_time_state",
            table: "sessions",
            sql: "(status = 1 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks > 0) OR (status = 3 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL AND elapsed_ticks > 0)");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropCheckConstraint(name: "ck_sessions_time_state", table: "sessions");
        m.AddCheckConstraint(
            name: "ck_sessions_time_state",
            table: "sessions",
            sql: "(status = 1 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND ended_at_utc > started_at_utc) OR (status = 3 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL AND ended_at_utc > started_at_utc AND paid_at_utc >= ended_at_utc)");

        m.DropColumn(name: "elapsed_ticks", table: "sessions");
        m.DropColumn(name: "started_boot_identifier", table: "sessions");
        m.DropColumn(name: "started_system_uptime_milliseconds", table: "sessions");
        m.DropColumn(name: "started_monotonic_frequency", table: "sessions");
        m.DropColumn(name: "started_monotonic_timestamp", table: "sessions");
    }
}
