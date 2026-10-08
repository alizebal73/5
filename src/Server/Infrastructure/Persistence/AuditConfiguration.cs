using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameNet.Server.Infrastructure.Persistence;

public sealed class AuditConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(128);
        builder.Property(x => x.Operation).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReferenceId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CommandId).HasMaxLength(128);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(256);
        builder.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
    }
}
