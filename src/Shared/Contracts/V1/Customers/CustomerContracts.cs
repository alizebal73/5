namespace GameNet.Shared.Contracts.V1.Customers;

public enum CustomerKindContract
{
    WalkIn = 1,
    Registered = 2
}

public enum CustomerStatusContract
{
    Active = 1,
    Inactive = 2,
    Blocked = 3
}

public sealed record CustomerResponse(
    Guid Id,
    string Code,
    string? DisplayName,
    string? Nickname,
    string? PhoneNumber,
    CustomerKindContract Kind,
    CustomerStatusContract Status,
    DateTimeOffset CreatedAtUtc,
    int Version);

public sealed record CreateCustomerRequest(string Code, string DisplayName, string? Nickname, string? PhoneNumber);
public sealed record UpdateCustomerRequest(string DisplayName, string? Nickname, string? PhoneNumber, int ExpectedVersion);
public sealed record SetCustomerStatusRequest(CustomerStatusContract Status, int ExpectedVersion, string Reason);
