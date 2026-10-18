using GameNet.Server.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(x => new { x.UserId, x.RoleId }).HasName("pk_user_roles");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.RoleId).HasColumnName("role_id");
        builder.HasIndex(x => x.RoleId).HasDatabaseName("ix_user_roles_role_id");
        builder.HasOne<OperatorUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_roles_operator_users_user_id");
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_roles_roles_role_id");
    }
}
