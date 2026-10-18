using GameNet.Server.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id).HasName("pk_roles");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired().HasColumnName("code");
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired().HasColumnName("name");
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ix_roles_code");
    }
}
