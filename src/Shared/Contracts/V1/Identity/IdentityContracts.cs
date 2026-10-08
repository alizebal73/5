namespace GameNet.Shared.Contracts.V1.Identity;

public sealed record BootstrapStatusResponse(bool Required);

public sealed record BootstrapAdminRequest(
    string Username,
    string DisplayName,
    string Password);

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    Guid UserId,
    string Username,
    IReadOnlySet<string> Permissions);

public sealed record CurrentOperatorResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlySet<string> Permissions);
