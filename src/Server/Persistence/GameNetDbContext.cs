using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Outbox;
using GameNet.Server.Persistence.Configurations;
using GameNet.Server.Persistence.Entities;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Persistence;

public sealed class GameNetDbContext(DbContextOptions<GameNetDbContext> options) : DbContext(options)
{
    public DbSet<AgentCredential> AgentCredentials => Set<AgentCredential>();
    public DbSet<AgentEnrollmentToken> AgentEnrollmentTokens => Set<AgentEnrollmentToken>();
    public DbSet<AgentConnectionLease> AgentConnectionLeases => Set<AgentConnectionLease>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxInboxReceipt> OutboxInboxReceipts => Set<OutboxInboxReceipt>();
    public DbSet<OperatorUser> OperatorUsers => Set<OperatorUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<BillingLedgerEntry> BillingLedgerEntries => Set<BillingLedgerEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectAuditMutation();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override int SaveChanges() => SaveChanges(true);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectAuditMutation();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(true, cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new AgentEnrollmentTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConnectionLeaseConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxInboxReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new OperatorUserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new RolePermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new AuthSessionConfiguration());
        modelBuilder.ApplyConfiguration(new StationConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new TariffConfiguration());
        modelBuilder.ApplyConfiguration(new SessionConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new BillingLedgerEntryConfiguration());
    }

    private void RejectAuditMutation()
    {
        if (ChangeTracker.Entries<AuditEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("AUDIT_IS_APPEND_ONLY");
        }

        if (ChangeTracker.Entries<BillingLedgerEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("BILLING_LEDGER_IS_APPEND_ONLY");
        }
    }
}
