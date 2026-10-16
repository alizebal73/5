using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Customers.Infrastructure;

/// <summary>Owns durable customer-profile operations; Session and Billing state remain in their modules.</summary>
public sealed class CustomerManagementService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    IAuditWriter audit,
    IGameClock clock) : ICustomerManagementService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<IReadOnlyList<CustomerResponse>>> ListAsync(
        CustomerActorContext actor, string? search, bool includeInactive, int take, CancellationToken ct)
    {
        if (!actor.Permissions.Contains(Permissions.CustomersRead))
            return Fail<IReadOnlyList<CustomerResponse>>("customers.forbidden", "Not allowed to read customers.");
        if (search is { Length: > 80 } || take is < 1 or > 100)
            return Fail<IReadOnlyList<CustomerResponse>>("customers.invalid", "Customer search or page size is invalid.");

        var query = db.Customers.AsNoTracking().Where(x => x.Kind == CustomerKind.Registered);
        if (!includeInactive) query = query.Where(x => x.IsActive && !x.IsBlocked);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = NormalizeSearchTerm(search.Trim());
            query = query.Where(x => x.Code.ToUpper().Contains(term) ||
                (x.DisplayName != null && x.DisplayName.ToUpper().Contains(term)) ||
                (x.Nickname != null && x.Nickname.ToUpper().Contains(term)) ||
                (x.PhoneNumber != null && x.PhoneNumber.Contains(term)));
        }

        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Code).Take(take).ToArrayAsync(ct);
        return Result<IReadOnlyList<CustomerResponse>>.Success(rows.Select(ToResponse).ToArray());
    }

    public Task<Result<CustomerResponse>> CreateAsync(CreateCustomerCommand c, CancellationToken ct)
    {
        if (!c.Actor.Permissions.Contains(Permissions.CustomersWrite))
            return Task.FromResult(Fail<CustomerResponse>("customers.forbidden", "Not allowed to create customers."));
        var payload = new { c.Code, c.DisplayName, c.Nickname, c.PhoneNumber };
        return MutateAsync("customer.create", c.IdempotencyKey, payload, c.Actor, 201, async token =>
        {
            Customer customer;
            try { customer = Customer.CreateRegistered(Guid.NewGuid(), c.Code, c.DisplayName, clock.UtcNow, c.Nickname, c.PhoneNumber); }
            catch (ArgumentException ex) { return Fail<CustomerResponse>("customers.invalid", ex.Message); }
            if (await db.Customers.AnyAsync(x => x.Code == customer.Code, token))
                return Fail<CustomerResponse>("customers.code_exists", "Customer code already exists.");

            await db.Customers.AddAsync(customer, token);
            var response = ToResponse(customer);
            AppendAudit(c.Actor, "customers.create", customer, null, ToAuditSnapshot(customer), null, c.IdempotencyKey);
            return Result<CustomerResponse>.Success(response);
        }, ct);
    }

    public Task<Result<CustomerResponse>> UpdateProfileAsync(UpdateCustomerProfileCommand c, CancellationToken ct)
    {
        if (!c.Actor.Permissions.Contains(Permissions.CustomersWrite))
            return Task.FromResult(Fail<CustomerResponse>("customers.forbidden", "Not allowed to update customers."));
        if (c.ExpectedVersion < 1)
            return Task.FromResult(Fail<CustomerResponse>("customers.invalid", "Expected customer version is invalid."));
        var payload = new { c.CustomerId, c.DisplayName, c.Nickname, c.PhoneNumber, c.ExpectedVersion };
        return MutateAsync("customer.update-profile", c.IdempotencyKey, payload, c.Actor, 200, async token =>
        {
            var customer = await db.Customers.SingleOrDefaultAsync(
                x => x.Id == c.CustomerId && x.Kind == CustomerKind.Registered, token);
            if (customer is null) return Fail<CustomerResponse>("customers.not_found", "Registered customer was not found.");
            if (customer.Version != c.ExpectedVersion)
                return Fail<CustomerResponse>("customers.version_conflict", "Customer data changed; refresh before saving.");
            var before = ToAuditSnapshot(customer);
            try { customer.UpdateProfile(c.DisplayName, c.Nickname, c.PhoneNumber); }
            catch (ArgumentException ex) { return Fail<CustomerResponse>("customers.invalid", ex.Message); }
            var response = ToResponse(customer);
            if (customer.Version != c.ExpectedVersion)
                AppendAudit(c.Actor, "customers.update_profile", customer, before, ToAuditSnapshot(customer), null, c.IdempotencyKey);
            return Result<CustomerResponse>.Success(response);
        }, ct);
    }

    public Task<Result<CustomerResponse>> SetStatusAsync(SetCustomerStatusCommand c, CancellationToken ct)
    {
        if (!c.Actor.Permissions.Contains(Permissions.CustomersWrite))
            return Task.FromResult(Fail<CustomerResponse>("customers.forbidden", "Not allowed to change customer status."));
        if (c.ExpectedVersion < 1 || !Enum.IsDefined(c.Status) || !IsValidReason(c.Reason))
            return Task.FromResult(Fail<CustomerResponse>("customers.invalid", "Customer status, version or reason is invalid."));
        var reason = c.Reason.Trim();
        var payload = new { c.CustomerId, c.Status, c.ExpectedVersion, Reason = reason };
        return MutateAsync("customer.set-status", c.IdempotencyKey, payload, c.Actor, 200, async token =>
        {
            var customer = await db.Customers.SingleOrDefaultAsync(
                x => x.Id == c.CustomerId && x.Kind == CustomerKind.Registered, token);
            if (customer is null) return Fail<CustomerResponse>("customers.not_found", "Registered customer was not found.");
            if (customer.Version != c.ExpectedVersion)
                return Fail<CustomerResponse>("customers.version_conflict", "Customer data changed; refresh before changing status.");
            var before = ToAuditSnapshot(customer);
            try { customer.SetStatus((CustomerStatus)c.Status); }
            catch (ArgumentOutOfRangeException ex) { return Fail<CustomerResponse>("customers.invalid", ex.Message); }
            catch (InvalidOperationException) { return Fail<CustomerResponse>("customers.walkin_profile_forbidden", "Walk-in customers cannot be changed."); }
            var response = ToResponse(customer);
            if (before.Version != customer.Version)
                AppendAudit(c.Actor, "customers.set_status", customer, before, ToAuditSnapshot(customer), reason, c.IdempotencyKey);
            return Result<CustomerResponse>.Success(response);
        }, ct);
    }

    private async Task<Result<T>> MutateAsync<T>(string operation, string key, object payload,
        CustomerActorContext actor, int statusCode, Func<CancellationToken, Task<Result<T>>> action, CancellationToken ct)
    {
        try
        {
            return await mutationPipeline.ExecuteAsync($"customers.{operation}:{actor.OperatorId:D}", operation, key,
                requestFingerprint.Compute(payload), statusCode, Lease, Retention, action, ct);
        }
        catch (PersistenceConflictException)
        {
            return Fail<T>("customers.conflict", "Customer code or concurrent customer data conflicts with another update.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<T>("customers.version_conflict", "Customer data changed; refresh before retrying.");
        }
    }

    private void AppendAudit(CustomerActorContext actor, string operation, Customer customer,
        object? before, object after, string? reason, string idempotencyKey) =>
        audit.Append(new AuditRecord(clock.UtcNow, "Operator", actor.OperatorId.ToString("D"), operation,
            "Customer", customer.Id.ToString("D"), reason, actor.CorrelationId, actor.Source, "Succeeded",
            idempotencyKey, before is null ? null : JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));

    private static string NormalizeSearchTerm(string value) =>
        new(value.Select(ch => ch switch
        {
            >= '۰' and <= '۹' => (char)('0' + ch - '۰'),
            >= '٠' and <= '٩' => (char)('0' + ch - '٠'),
            _ => ch
        }).ToArray()).ToUpperInvariant();

    private static bool IsValidReason(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 3 and <= 256 && !value.Any(char.IsControl);

    private static CustomerResponse ToResponse(Customer x) =>
        new(x.Id, x.Code, x.DisplayName, x.Nickname, x.PhoneNumber,
            (CustomerKindContract)x.Kind, (CustomerStatusContract)x.Status, x.CreatedAtUtc, x.Version);

    private static CustomerAuditSnapshot ToAuditSnapshot(Customer x) =>
        new(x.Id, x.Code, x.DisplayName, (CustomerStatusContract)x.Status, x.Version);

    private sealed record CustomerAuditSnapshot(Guid Id, string Code, string? DisplayName, CustomerStatusContract Status, int Version);
    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
}
