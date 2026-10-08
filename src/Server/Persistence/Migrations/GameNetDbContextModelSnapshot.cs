using GameNet.Server.Infrastructure.Persistence;
using GameNet.Server.Modules.Stations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
#nullable disable

namespace GameNet.Server.Persistence.Migrations;

[DbContext(typeof(GameNetDbContext))]
partial class GameNetDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.12")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("id")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            b.Property<string>("ActorId").HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("actor_id");
            b.Property<string>("ActorType").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("actor_type");
            b.Property<string>("AfterJson").HasColumnType("text").HasColumnName("after_json");
            b.Property<string>("BeforeJson").HasColumnType("text").HasColumnName("before_json");
            b.Property<string>("CommandId").HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("command_id");
            b.Property<string>("IdempotencyKey").HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("idempotency_key");
            b.Property<string>("Operation").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("operation");
            b.Property<DateTimeOffset>("OccurredAtUtc").HasColumnType("timestamp with time zone").HasColumnName("occurred_at_utc");
            b.Property<string>("Outcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("outcome");
            b.Property<string>("Reason").HasColumnType("text").HasColumnName("reason");
            b.Property<string>("ReferenceId").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("reference_id");
            b.Property<string>("ReferenceType").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("reference_type");
            b.Property<string>("Source").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("source");

            b.HasKey("Id");
            b.ToTable("audit_entries");
        });

        modelBuilder.Entity<IdempotencyEntry>(b =>
        {
            b.Property<string>("Scope").HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("scope");
            b.Property<string>("Key").HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("key");
            b.Property<string>("Operation").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("operation");
            b.Property<string>("RequestHash").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("request_hash");
            b.Property<string>("LeaseToken").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("lease_token");
            b.Property<DateTimeOffset>("ClaimedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("claimed_at_utc");
            b.Property<bool>("Completed").HasColumnType("boolean").HasColumnName("completed");
            b.Property<int?>("StatusCode").HasColumnType("integer").HasColumnName("status_code");
            b.Property<string>("ResponseJson").HasColumnType("text").HasColumnName("response_json");
            b.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("timestamp with time zone").HasColumnName("completed_at_utc");

            b.HasKey("Scope", "Key");
            b.ToTable("idempotency_entries");
        });

        modelBuilder.Entity<Station>(b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("Code").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("code");
            b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("name");
            b.Property<StationType>("Type").IsRequired().HasConversion<int>().HasColumnType("integer").HasColumnName("type");
            b.Property<StationStatus>("Status").IsRequired().HasConversion<int>().HasColumnType("integer").HasColumnName("status");
            b.Property<int>("Version").IsRequired().IsConcurrencyToken().HasColumnType("integer").HasColumnName("version");

            b.HasKey("Id");
            b.HasIndex("Code").IsUnique();
            b.ToTable("stations");
        });
    }
}
