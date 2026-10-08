namespace GameNet.Shared.Contracts.V1.Customers;

public sealed record CreateCustomerRequest(
    string Code,
    string DisplayName,
    string? Phone,
    string? Pin);

public sealed record UpdateCustomerRequest(
    string DisplayName,
    string? Phone,
    string? Pin);

public sealed record CustomerResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string? Phone,
    bool HasPin,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
