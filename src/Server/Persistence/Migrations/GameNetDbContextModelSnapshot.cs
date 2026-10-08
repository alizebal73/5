using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Outbox;
using GameNet.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

[DbContext(typeof(GameNetDbContext))]
partial class GameNetDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OccurredAtUtc, x.Operation }).HasDatabaseName("IX_audit_entries_OccurredAtUtc_Operation");
            b.HasIndex(x => new { x.ReferenceType, x.ReferenceId }).HasDatabaseName("IX_audit_entries_ReferenceType_ReferenceId");
            b.HasIndex(x => x.IdempotencyKey).HasDatabaseName("IX_audit_entries_IdempotencyKey");

            b.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
            b.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
            b.Property(x => x.ActorType).HasMaxLength(64).HasColumnName("actor_type");
            b.Property(x => x.ActorId).HasMaxLength(128).HasColumnName("actor_id");
            b.Property(x => x.Operation).HasMaxLength(200).HasColumnName("operation");
            b.Property(x => x.ReferenceType).HasMaxLength(100).HasColumnName("reference_type");
            b.Property(x => x.ReferenceId).HasMaxLength(128).HasColumnName("reference_id");
            b.Property(x => x.Reason).HasMaxLength(1000).HasColumnName("reason");
            b.Property(x => x.CorrelationId).HasMaxLength(128).HasColumnName("correlation_id");
            b.Property(x => x.Source).HasMaxLength(64).HasColumnName("source");
            b.Property(x => x.Outcome).HasMaxLength(64).HasColumnName("outcome");
            b.Property(x => x.IdempotencyKey).HasMaxLength(200).HasColumnName("idempotency_key");
            b.Property(x => x.BeforeJson).HasColumnType("jsonb").HasColumnName("before_json");
            b.Property(x => x.AfterJson).HasColumnType("jsonb").HasColumnName("after_json");
            b.ToTable("audit_entries");
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.Scope, x.Key }).IsUnique().HasDatabaseName("IX_idempotency_records_Scope_Key");
            b.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_idempotency_records_ExpiresAtUtc");

            b.Property(x => x.Id).ValueGeneratedOnAdd()
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .HasColumnName("id");
            b.Property(x => x.Scope).HasMaxLength(160).HasColumnName("scope");
            b.Property(x => x.Key).HasMaxLength(200).HasColumnName("key");
            b.Property(x => x.Operation).HasMaxLength(200).HasColumnName("operation");
            b.Property(x => x.State).HasMaxLength(32).HasColumnName("state");
            b.Property(x => x.LeaseToken).HasMaxLength(128).HasColumnName("lease_token");
            b.Property(x => x.StatusCode).HasColumnName("status_code");
            b.Property(x => x.ResponseJson).HasColumnType("jsonb").HasColumnName("response_json");
            b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
            b.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            b.ToTable("idempotency_records");
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.EventId).IsUnique().HasDatabaseName("IX_outbox_messages_EventId");
            b.HasIndex(x => new { x.PublishedAtUtc, x.OccurredAtUtc }).HasDatabaseName("IX_outbox_messages_PublishedAtUtc_OccurredAtUtc");
            b.HasIndex(x => new { x.LeaseExpiresAtUtc, x.Id }).HasDatabaseName("IX_outbox_messages_LeaseExpiresAtUtc_Id");

            b.Property(x => x.Id).ValueGeneratedOnAdd()
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .HasColumnName("id");
            b.Property(x => x.EventId).HasColumnName("event_id");
            b.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
            b.Property(x => x.Type).HasMaxLength(200).HasColumnName("type");
            b.Property(x => x.PayloadJson).HasColumnType("jsonb").HasColumnName("payload_json");
            b.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
            b.Property(x => x.LeaseToken).HasMaxLength(128).HasColumnName("lease_token");
            b.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
            b.ToTable("outbox_messages");
        });
    }
}
