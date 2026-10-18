using GameNet.Server.Modules.Stations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace GameNet.Server.Persistence.Configurations;
public sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> b)
    {
        b.ToTable("stations"); b.HasKey(x => x.Id).HasName("pk_stations");
        b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        b.Property(x => x.Code).HasMaxLength(32).IsRequired().HasColumnName("code");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired().HasColumnName("name");
        b.Property(x => x.Type).IsRequired().HasColumnName("type");
        b.Property(x => x.Status).IsRequired().HasColumnName("status");
        b.Property(x => x.Version).IsConcurrencyToken().IsRequired().HasColumnName("version");
        b.Property(x => x.AgentDeviceId).HasMaxLength(128).HasColumnName("agent_device_id");
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ix_stations_code");
        b.HasIndex(x => x.AgentDeviceId).IsUnique().HasFilter("agent_device_id IS NOT NULL").HasDatabaseName("ix_stations_agent_device_id");
    }
}
