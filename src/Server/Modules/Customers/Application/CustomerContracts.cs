using GameNet.Server.Modules.Customers.Domain;

namespace GameNet.Server.Modules.Customers.Application;

public sealed record CustomerActorContext(
    string ActorType,
    string? ActorId,
    string Source,
    IReadOnlySet<string> Permissions);

public sealed record CreateCustomerCommand(
    string Code,
    string DisplayName,
    string? Phone,
    string? Pin,
    string IdempotencyKey,
    string CommandId,
    CustomerActorContext Actor);

public sealed record UpdateCustomerCommand(
    Guid CustomerId,
    string DisplayName,
    string? Phone,
    string? Pin,
    string IdempotencyKey,
    string CommandId,
    CustomerActorContext Actor);

public sealed record CustomerDto(
    Guid Id,
    string Code,
    string DisplayName,
    string? Phone,
    bool HasPin,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> SearchAsync(string? search, CancellationToken cancellationToken);
    Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Customer customer, CancellationToken cancellationToken);
}
