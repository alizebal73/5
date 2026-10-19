using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

public partial class SessionPauseResume : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<long>(
            name: "accumulated_elapsed_ticks",
            table: "sessions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
        m.AddColumn<long>(
            name: "current_segment_started_monotonic_timestamp",
            table: "sessions",
            type: "bigint",
            nullable: true);
        m.AddColumn<long>(
            name: "current_segment_started_monotonic_frequency",
            table: "sessions",
            type: "bigint",
            nullable: true);
        m.AddColumn<long>(
            name: "current_segment_started_system_uptime_milliseconds",
            table: "sessions",
            type: "bigint",
            nullable: true);
        m.AddColumn<Guid>(
            name: "current_segment_started_boot_identifier",
            table: "sessions",
            type: "uuid",
            nullable: true);

        // Carry settled timing forward and initialize the current active segment without
        // changing the already-authoritative timing baseline of existing Active Sessions.
        m.Sql("""
            UPDATE sessions
            SET accumulated_elapsed_ticks = COALESCE(elapsed_ticks, 0),
                current_segment_started_monotonic_timestamp =
                    CASE WHEN status = 1 THEN started_monotonic_timestamp ELSE NULL END,
                current_segment_started_monotonic_frequency =
                    CASE WHEN status = 1 THEN started_monotonic_frequency ELSE NULL END,
                current_segment_started_system_uptime_milliseconds =
                    CASE WHEN status = 1 THEN started_system_uptime_milliseconds ELSE NULL END,
                current_segment_started_boot_identifier =
                    CASE WHEN status = 1 THEN started_boot_identifier ELSE NULL END;
            """);

        m.DropCheckConstraint(name: "ck_sessions_time_state", table: "sessions");
        m.DropCheckConstraint(name: "ck_sessions_status_range", table: "sessions");
        m.DropCheckConstraint(name: "ck_sessions_end_actor_shape", table: "sessions");
        m.DropCheckConstraint(name: "ck_sessions_paid_actor_shape", table: "sessions");

        m.DropIndex(name: "ux_sessions_one_unsettled_per_station", table: "sessions");
        m.CreateIndex(
            name: "ux_sessions_one_unsettled_per_station",
            table: "sessions",
            column: "station_id",
            unique: true,
            filter: "status IN (1, 2, 4)");

        m.AddCheckConstraint(
            name: "ck_sessions_status_range",
            table: "sessions",
            sql: "status BETWEEN 1 AND 4");
        m.AddCheckConstraint(
            name: "ck_sessions_end_actor_shape",
            table: "sessions",
            sql: "((status IN (1, 4) AND ended_by_system = FALSE) OR (status IN (2, 3) AND ((ended_by_system = TRUE AND ended_by_operator_id = '00000000-0000-0000-0000-000000000000') OR (ended_by_system = FALSE AND ended_by_operator_id <> '00000000-0000-0000-0000-000000000000'))))");
        m.AddCheckConstraint(
            name: "ck_sessions_paid_actor_shape",
            table: "sessions",
            sql: "((status IN (1, 2, 4) AND paid_by_customer_id IS NULL) OR (status = 3 AND ((paid_by_customer_id IS NULL AND paid_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (paid_by_customer_id IS NOT NULL AND paid_by_operator_id = '00000000-0000-0000-0000-000000000000'))))");
        m.AddCheckConstraint(
            name: "ck_sessions_time_state",
            table: "sessions",
            sql: "(status = 1 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NULL AND accumulated_elapsed_ticks >= 0 AND current_segment_started_monotonic_timestamp IS NOT NULL AND current_segment_started_monotonic_frequency IS NOT NULL AND current_segment_started_monotonic_frequency > 0 AND current_segment_started_system_uptime_milliseconds IS NOT NULL AND current_segment_started_system_uptime_milliseconds >= 0 AND current_segment_started_boot_identifier IS NOT NULL) OR (status = 4 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NULL AND accumulated_elapsed_ticks > 0 AND current_segment_started_monotonic_timestamp IS NULL AND current_segment_started_monotonic_frequency IS NULL AND current_segment_started_system_uptime_milliseconds IS NULL AND current_segment_started_boot_identifier IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NOT NULL AND elapsed_ticks > 0 AND accumulated_elapsed_ticks = elapsed_ticks AND current_segment_started_monotonic_timestamp IS NULL AND current_segment_started_monotonic_frequency IS NULL AND current_segment_started_system_uptime_milliseconds IS NULL AND current_segment_started_boot_identifier IS NULL) OR (status = 3 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL AND elapsed_ticks IS NOT NULL AND elapsed_ticks > 0 AND accumulated_elapsed_ticks = elapsed_ticks AND current_segment_started_monotonic_timestamp IS NULL AND current_segment_started_monotonic_frequency IS NULL AND current_segment_started_system_uptime_milliseconds IS NULL AND current_segment_started_boot_identifier IS NULL)");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM sessions
                    WHERE status = 4
                       OR (status = 1 AND (
                           accumulated_elapsed_ticks > 0
                           OR current_segment_started_monotonic_timestamp IS DISTINCT FROM started_monotonic_timestamp
                           OR current_segment_started_monotonic_frequency IS DISTINCT FROM started_monotonic_frequency
                           OR current_segment_started_system_uptime_milliseconds IS DISTINCT FROM started_system_uptime_milliseconds
                           OR current_segment_started_boot_identifier IS DISTINCT FROM started_boot_identifier
                       ))
                THEN
                    RAISE EXCEPTION 'SESSION_PAUSE_RESUME_DOWN_BLOCKED: rollback would lose paused state or misbill a resumed Active Session';
                END IF;
            END $$;
            """);

        m.DropCheckConstraint(name: "ck_sessions_time_state", table: "sessions");
        m.DropCheckConstraint(name: "ck_sessions_status_range", table: "sessions");
        m.DropCheckConstraint(name: "ck_sessions_end_actor_shape", table: "sessions");
        m.DropCheckConstraint(name: "ck_sessions_paid_actor_shape", table: "sessions");
        m.DropIndex(name: "ux_sessions_one_unsettled_per_station", table: "sessions");

        m.DropColumn(name: "current_segment_started_monotonic_timestamp", table: "sessions");
        m.DropColumn(name: "current_segment_started_monotonic_frequency", table: "sessions");
        m.DropColumn(name: "current_segment_started_system_uptime_milliseconds", table: "sessions");
        m.DropColumn(name: "current_segment_started_boot_identifier", table: "sessions");
        m.DropColumn(name: "accumulated_elapsed_ticks", table: "sessions");

        m.CreateIndex(
            name: "ux_sessions_one_unsettled_per_station",
            table: "sessions",
            column: "station_id",
            unique: true,
            filter: "status IN (1, 2)");
        m.AddCheckConstraint(
            name: "ck_sessions_status_range",
            table: "sessions",
            sql: "status BETWEEN 1 AND 3");
        m.AddCheckConstraint(
            name: "ck_sessions_end_actor_shape",
            table: "sessions",
            sql: "((status = 1 AND ended_by_system = FALSE) OR (status IN (2, 3) AND ((ended_by_system = TRUE AND ended_by_operator_id = '00000000-0000-0000-0000-000000000000') OR (ended_by_system = FALSE AND ended_by_operator_id <> '00000000-0000-0000-0000-000000000000'))))");
        m.AddCheckConstraint(
            name: "ck_sessions_paid_actor_shape",
            table: "sessions",
            sql: "((status IN (1, 2) AND paid_by_customer_id IS NULL) OR (status = 3 AND ((paid_by_customer_id IS NULL AND paid_by_operator_id <> '00000000-0000-0000-0000-000000000000') OR (paid_by_customer_id IS NOT NULL AND paid_by_operator_id = '00000000-0000-0000-0000-000000000000'))))");
        m.AddCheckConstraint(
            name: "ck_sessions_time_state",
            table: "sessions",
            sql: "(status = 1 AND ended_at_utc IS NULL AND ended_by_operator_id IS NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NULL) OR (status = 2 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NULL AND paid_by_operator_id IS NULL AND elapsed_ticks IS NOT NULL AND elapsed_ticks > 0) OR (status = 3 AND ended_at_utc IS NOT NULL AND ended_by_operator_id IS NOT NULL AND paid_at_utc IS NOT NULL AND paid_by_operator_id IS NOT NULL AND elapsed_ticks IS NOT NULL AND elapsed_ticks > 0)");
    }
}
