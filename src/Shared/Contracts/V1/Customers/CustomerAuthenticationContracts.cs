namespace GameNet.Shared.Contracts.V1.Customers;

using GameNet.Shared.Contracts.V1.Sessions;

public sealed record SetCustomerCredentialsRequest(string Username, string Password, int ExpectedVersion);
public sealed record SetCustomerCredentialsResponse(Guid CustomerId, string Username, int CustomerVersion, DateTimeOffset ChangedAtUtc);
public sealed record CustomerLoginRequest(string Username, string Password);
public sealed record CustomerLoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, Guid CustomerId, string Username, string? DisplayName);
public sealed record CustomerLoginProfileResponse(Guid CustomerId, string Username, string? DisplayName, DateTimeOffset CreatedAtUtc);
public sealed record CustomerLogoutResponse(bool LoggedOut);

/// <summary>Credentials sent only by the local Agent gateway over HTTPS; no StationId is client-controlled.</summary>
public sealed record CustomerClientEntryRequest(string Username, string Password, string IdempotencyKey);

/// <summary>Successful direct Client login and prepaid Session result, bound by Server to the authenticated Agent device.</summary>
public sealed record CustomerClientEntryResponse(
    bool Succeeded,
    Guid? CustomerId,
    string? DisplayName,
    string? CustomerAccessToken,
    DateTimeOffset? CustomerTokenExpiresAtUtc,
    Guid? SessionId,
    long? PrepaidReservedAmountToman,
    long? PrepaidBudgetTicks,
    string? ErrorCode,
    string? ErrorMessage);
