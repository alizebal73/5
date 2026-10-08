using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Application;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Application;

public sealed class CustomerService(
    ICustomerRepository repository,
    IPinHasher pinHasher,
    ITransactionCoordinator transactions,
    IIdempotencyStore idempotency,
    IAuditWriter audit,
    IGameClock clock)
{
    public async Task<Result<IReadOnlyList<CustomerDto>>> SearchAsync(
        string? search,
        CustomerActorContext actor,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.CustomersRead))
            return Result<IReadOnlyList<CustomerDto>>.Failure(
                new Error("customers.forbidden", "The operator is not allowed to read customers."));

        var customers = await repository.SearchAsync(search, cancellationToken);
        return Result<IReadOnlyList<CustomerDto>>.Success(
            customers.Select(ToDto).ToArray());
    }

    public Task<Result<CustomerDto>> CreateAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(
            "customers.create",
            command.IdempotencyKey,
            "customers.create",
            new { command.Code, command.DisplayName, command.Phone, command.Pin },
            command.CommandId,
            command.Actor,
            async ct =>
            {
                try
                {
                    var pinHash = string.IsNullOrWhiteSpace(command.Pin)
                        ? null
                        : pinHasher.Hash(command.Pin);

                    var customer = Customer.Create(
                        Guid.NewGuid(),
                        command.Code,
                        command.DisplayName,
                        command.Phone,
                        pinHash,
                        clock.UtcNow);

                    await repository.AddAsync(customer, ct);
                    var dto = ToDto(customer);

                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        command.Actor.ActorType,
                        command.Actor.ActorId,
                        "customers.create",
                        "Customer",
                        customer.Id.ToString("D"),
                        null,
                        null,
                        JsonSerializer.Serialize(dto),
                        command.Actor.Source,
                        command.CommandId,
                        command.IdempotencyKey,
                        "Succeeded"));

                    return Result<CustomerDto>.Success(dto);
                }
                catch (ArgumentException ex)
                {
                    return Failure("customers.invalid", ex.Message);
                }
            },
            201,
            cancellationToken);

    public Task<Result<CustomerDto>> UpdateAsync(
        UpdateCustomerCommand command,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(
            "customers.update",
            command.IdempotencyKey,
            "customers.update",
            new { command.CustomerId, command.DisplayName, command.Phone, command.Pin },
            command.CommandId,
            command.Actor,
            async ct =>
            {
                var customer = await repository.FindAsync(command.CustomerId, ct);
                if (customer is null)
                    return Failure("customers.not_found", "Customer was not found.");

                var before = ToDto(customer);

                try
                {
                    customer.UpdateProfile(
                        command.DisplayName,
                        command.Phone,
                        clock.UtcNow);

                    if (command.Pin is not null)
                        customer.ChangePin(
                            pinHasher.Hash(command.Pin),
                            clock.UtcNow);

                    var after = ToDto(customer);

                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        command.Actor.ActorType,
                        command.Actor.ActorId,
                        "customers.update",
                        "Customer",
                        customer.Id.ToString("D"),
                        null,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(after),
                        command.Actor.Source,
                        command.CommandId,
                        command.IdempotencyKey,
                        "Succeeded"));

                    return Result<CustomerDto>.Success(after);
                }
                catch (ArgumentException ex)
                {
                    return Failure("customers.invalid", ex.Message);
                }
            },
            200,
            cancellationToken);

    private async Task<Result<CustomerDto>> ExecuteIdempotentAsync(
        string scope,
        string key,
        string operation,
        object request,
        string commandId,
        CustomerActorContext actor,
        Func<CancellationToken, Task<Result<CustomerDto>>> action,
        int statusCode,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.CustomersWrite))
            return Failure("customers.forbidden", "The operator is not allowed to manage customers.");

        if (string.IsNullOrWhiteSpace(key))
            return Failure("idempotency.required", "An idempotency key is required.");

        var requestHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(request))));

        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                var claim = await idempotency.TryClaimAsync(
                    scope,
                    key.Trim(),
                    operation,
                    requestHash,
                    ct);

                if (claim.State == IdempotencyClaimState.Completed &&
                    claim.ResponseJson is not null)
                {
                    var replay = JsonSerializer.Deserialize<CustomerDto>(claim.ResponseJson);
                    return replay is null
                        ? Failure("idempotency.invalid_replay", "The stored idempotent response is invalid.")
                        : Result<CustomerDto>.Success(replay);
                }

                if (claim.State == IdempotencyClaimState.InFlight)
                    throw new MutationAbortedException(
                        new Error("idempotency.in_flight", "The same operation is already in progress."));

                if (claim.State == IdempotencyClaimState.Conflict)
                    throw new MutationAbortedException(
                        new Error("idempotency.key_reused", "The idempotency key was already used for different request data."));

                var result = await action(ct);
                if (!result.IsSuccess)
                    throw new MutationAbortedException(result.Error);

                await idempotency.CompleteAsync(
                    scope,
                    key.Trim(),
                    claim.LeaseToken,
                    statusCode,
                    JsonSerializer.Serialize(result.Value),
                    ct);

                return result;
            }, cancellationToken);
        }
        catch (MutationAbortedException ex)
        {
            return Failure(ex.Error.Code, ex.Error.Message);
        }
        catch (PersistenceConflictException ex) when (ex.Code == "persistence.unique")
        {
            return Failure("customers.code_exists", "A customer with this code already exists.");
        }
        catch (PersistenceConflictException ex) when (ex.Code == "persistence.concurrency")
        {
            return Failure("customers.concurrency_conflict", "The customer changed before this operation could be saved.");
        }
    }

    private static Result<CustomerDto> Failure(string code, string message) =>
        Result<CustomerDto>.Failure(new Error(code, message));

    private static CustomerDto ToDto(Customer customer) =>
        new(
            customer.Id,
            customer.Code,
            customer.DisplayName,
            customer.Phone,
            customer.PinHash is not null,
            customer.IsActive,
            customer.CreatedAtUtc,
            customer.UpdatedAtUtc,
            customer.Version);
}
