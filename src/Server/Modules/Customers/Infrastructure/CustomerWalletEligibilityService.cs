using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Customers.Infrastructure;

/// <summary>
/// Owns Customer registration/status/version rules and obtains the row lock needed by
/// a calling financial use case. The caller's transaction spans this operation and the money writes.
/// </summary>
public sealed class CustomerWalletEligibilityService(GameNetDbContext db) : ICustomerWalletEligibility
{
    public async Task<Result<CustomerWalletSnapshot>> GetRegisteredCustomerAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
            return Fail("customers.not_found", "Registered customer was not found.");

        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == customerId && x.Kind == CustomerKind.Registered, cancellationToken);
        return customer is null
            ? Fail("customers.not_found", "Registered customer was not found.")
            : Result<CustomerWalletSnapshot>.Success(new CustomerWalletSnapshot(customer.Id, customer.Code, customer.Version));
    }

    public async Task<Result<CustomerWalletSnapshot>> LockActiveRegisteredCustomerForWalletTopUpAsync(
        Guid customerId, int expectedVersion, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty || expectedVersion < 1)
            return Fail("customers.invalid", "Customer identity or expected version is invalid.");

        var customer = await db.Customers
            .FromSqlInterpolated($"SELECT * FROM customers WHERE id = {customerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null || customer.Kind != CustomerKind.Registered)
            return Fail("customers.not_found", "Registered customer was not found.");
        if (!customer.IsActive || customer.IsBlocked)
            return Fail("customers.inactive", "Only active, unblocked customers can receive purchased credit.");
        if (customer.Version != expectedVersion)
            return Fail("customers.version_conflict", "Customer data changed; refresh and review before retrying.");

        return Result<CustomerWalletSnapshot>.Success(new CustomerWalletSnapshot(customer.Id, customer.Code, customer.Version));
    }

    public async Task<Result<CustomerWalletSnapshot>> LockActiveRegisteredCustomerForSessionAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
            return Fail("customers.not_found", "Registered customer was not found.");

        // Session start holds this row lock until the Wallet reservation and Session commit.
        // This serializes competing starts and Wallet top-ups for the same registered customer.
        var customer = await db.Customers
            .FromSqlInterpolated($"SELECT * FROM customers WHERE id = {customerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null || customer.Kind != CustomerKind.Registered)
            return Fail("customers.not_found", "Registered customer was not found.");
        if (!customer.IsActive || customer.IsBlocked)
            return Fail("customers.inactive", "Only active, unblocked registered customers can start a prepaid Session.");

        return Result<CustomerWalletSnapshot>.Success(
            new CustomerWalletSnapshot(customer.Id, customer.Code, customer.Version));
    }

    private static Result<CustomerWalletSnapshot> Fail(string code, string message) =>
        Result<CustomerWalletSnapshot>.Failure(new Error(code, message));
}