using GameNet.Server.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.Permission }).HasName("pk_role_permissions");
        builder.Property(x => x.RoleId).HasColumnName("role_id");
        builder.Property(x => x.Permission).HasMaxLength(128).IsRequired().HasColumnName("permission");
        builder.HasIndex(x => x.Permission).HasDatabaseName("ix_role_permissions_permission");
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_role_permissions_roles_role_id");
    }
}
