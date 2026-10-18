using GameNet.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredential>
{
    public void Configure(EntityTypeBuilder<AgentCredential> builder)
    {
        builder.ToTable("agent_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(x => x.DeviceId).HasMaxLength(128).IsRequired().HasColumnName("device_id");
        builder.Property(x => x.SecretHash).HasMaxLength(128).IsRequired().HasColumnName("secret_hash");
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnName("created_at_utc");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(x => x.LastAuthenticatedAtUtc).HasColumnName("last_authenticated_at_utc");
        builder.HasIndex(x => x.DeviceId)
            .HasDatabaseName("IX_agent_credentials_ActiveDevice")
            .IsUnique()
            .HasFilter("revoked_at_utc IS NULL");
        builder.HasIndex(x => new { x.DeviceId, x.CreatedAtUtc })
            .HasDatabaseName("IX_agent_credentials_Device_Created");
    }
}
