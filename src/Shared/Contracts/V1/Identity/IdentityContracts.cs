namespace GameNet.Shared.Contracts.V1.Identity;

public sealed record BootstrapStatusResponse(bool Required);
public sealed record BootstrapAdminRequest(string Username, string DisplayName, string Password);
public sealed record BootstrapAdminResponse(Guid UserId, string Username);
public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc,
    Guid UserId, string Username, string DisplayName, string[] Permissions);
public sealed record CurrentOperatorResponse(Guid UserId, string Username, string DisplayName, string[] Permissions);
public sealed record LogoutResponse(bool Success);
public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ChangeOwnPasswordResponse(bool OtherSessionsRevoked);
public sealed record ResetOperatorPasswordRequest(string NewPassword, string Reason);
public sealed record ResetOperatorPasswordResponse(Guid OperatorId, bool SessionsRevoked);
