using GameNet.Server.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class OperatorUserConfiguration : IEntityTypeConfiguration<OperatorUser>
{
    public void Configure(EntityTypeBuilder<OperatorUser> builder)
    {
        builder.ToTable("operator_users");
        builder.HasKey(x => x.Id).HasName("pk_operator_users");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(x => x.Username).HasMaxLength(64).IsRequired().HasColumnName("username");
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired().HasColumnName("display_name");
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired().HasColumnName("password_hash");
        builder.Property(x => x.IsActive).IsRequired().HasColumnName("is_active");
        builder.Property(x => x.FailedLoginCount).IsRequired().HasColumnName("failed_login_count");
        builder.Property(x => x.LockoutUntilUtc).HasColumnName("lockout_until_utc");
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnName("created_at_utc");
        builder.Property(x => x.LastLoginAtUtc).HasColumnName("last_login_at_utc");
        builder.HasIndex(x => x.Username).IsUnique().HasDatabaseName("ix_operator_users_username");
    }
}
