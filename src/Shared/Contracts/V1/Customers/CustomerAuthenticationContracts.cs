namespace GameNet.Shared.Contracts.V1.Customers;

public sealed record SetCustomerPasswordRequest(string Password, int ExpectedVersion);
public sealed record SetCustomerPasswordResponse(Guid CustomerId, string Username, int CustomerVersion, DateTimeOffset ChangedAtUtc);
public sealed record CustomerLoginRequest(string Username, string Password);
public sealed record CustomerLoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, Guid CustomerId, string Username, string? DisplayName);
public sealed record CustomerLoginProfileResponse(Guid CustomerId, string Username, string? DisplayName, DateTimeOffset CreatedAtUtc);
public sealed record CustomerLogoutResponse(bool LoggedOut);
