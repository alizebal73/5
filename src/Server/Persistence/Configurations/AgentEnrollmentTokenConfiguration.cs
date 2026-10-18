using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class AgentEnrollmentTokenConfiguration : IEntityTypeConfiguration<AgentEnrollmentToken>
{
    public void Configure(EntityTypeBuilder<AgentEnrollmentToken> builder)
    {
        builder.ToTable("agent_enrollment_tokens");
        builder.HasKey(x => x.Id).HasName("pk_agent_enrollment_tokens");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(x => x.DeviceId).HasMaxLength(128).IsRequired().HasColumnName("device_id");
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired().HasColumnName("token_hash");
        builder.Property(x => x.IssuedByOperatorId).IsRequired().HasColumnName("issued_by_operator_id");
        builder.Property(x => x.IssuedAtUtc).IsRequired().HasColumnName("issued_at_utc");
        builder.Property(x => x.ExpiresAtUtc).IsRequired().HasColumnName("expires_at_utc");
        builder.Property(x => x.RedeemedAtUtc).HasColumnName("redeemed_at_utc");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_agent_enrollment_tokens_token_hash");
        builder.HasIndex(x => x.DeviceId)
            .IsUnique()
            .HasDatabaseName("ix_agent_enrollment_tokens_pending_device")
            .HasFilter("redeemed_at_utc IS NULL AND revoked_at_utc IS NULL");
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_agent_enrollment_tokens_expires_at_utc");

        builder.HasOne<OperatorUser>()
            .WithMany()
            .HasForeignKey(x => x.IssuedByOperatorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_agent_enrollment_tokens_issued_by_operator");
    }
}
