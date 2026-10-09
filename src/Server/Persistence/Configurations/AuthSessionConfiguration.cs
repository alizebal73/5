using GameNet.Server.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(x => x.Id).HasName("pk_auth_sessions");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(x => x.UserId).IsRequired().HasColumnName("user_id");
        builder.Property(x => x.Jti).HasMaxLength(128).IsRequired().HasColumnName("jti");
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnName("created_at_utc");
        builder.Property(x => x.ExpiresAtUtc).IsRequired().HasColumnName("expires_at_utc");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.HasIndex(x => x.Jti).IsUnique().HasDatabaseName("ix_auth_sessions_jti");
        builder.HasIndex(x => x.UserId);
        builder.HasOne<OperatorUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_sessions_operator_users_user_id");
    }
}
