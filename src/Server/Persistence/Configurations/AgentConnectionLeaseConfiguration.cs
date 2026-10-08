using GameNet.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class AgentConnectionLeaseConfiguration : IEntityTypeConfiguration<AgentConnectionLease>
{
    public void Configure(EntityTypeBuilder<AgentConnectionLease> builder)
    {
        builder.ToTable("agent_connection_leases");
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).HasMaxLength(128).HasColumnName("device_id");
        builder.Property(x => x.ConnectionId).HasMaxLength(128).IsRequired().HasColumnName("connection_id");
        builder.Property(x => x.LeaseToken).HasMaxLength(128).IsRequired().HasColumnName("lease_token");
        builder.Property(x => x.LeaseExpiresAtUtc).IsRequired().HasColumnName("lease_expires_at_utc");
        builder.Property(x => x.UpdatedAtUtc).IsRequired().HasColumnName("updated_at_utc");
        builder.Property(x => x.LastHeartbeatAtUtc).HasColumnName("last_heartbeat_at_utc");
        builder.Property(x => x.AgentVersion).HasMaxLength(64).HasColumnName("agent_version");
        builder.Property(x => x.StationState).HasMaxLength(64).HasColumnName("station_state");
        builder.HasIndex(x => new { x.LeaseExpiresAtUtc, x.DeviceId })
            .HasDatabaseName("IX_agent_connection_leases_Expires_Device");
    }
}
