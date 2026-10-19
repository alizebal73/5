using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Application;

public sealed record CustomerPasswordActor(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record SetCustomerCredentialsCommand(Guid CustomerId, string Username, string Password, int ExpectedVersion, string IdempotencyKey, CustomerPasswordActor Actor);
public sealed record CustomerCredentialsSetResult(Guid CustomerId, string Username, int CustomerVersion, DateTimeOffset ChangedAtUtc);
public sealed record CustomerLoginCommand(string? Username, string? Password, string CorrelationId, string Source);
public sealed record CustomerLoginResult(string AccessToken, DateTimeOffset ExpiresAtUtc, Guid CustomerId, string CustomerCode, string? DisplayName);
public sealed record CustomerLoginProfile(Guid CustomerId, string CustomerCode, string? DisplayName, DateTimeOffset CreatedAtUtc);
public sealed record CustomerLogoutResult(bool LoggedOut);

public interface ICustomerPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string? password, string encodedHash);
    bool IsValidPassword(string? password);
}

public interface ICustomerAuthenticationService
{
    Task<Result<CustomerCredentialsSetResult>> SetCredentialsAsync(SetCustomerCredentialsCommand command, CancellationToken cancellationToken);
    Task<Result<CustomerLoginResult>> LoginAsync(CustomerLoginCommand command, CancellationToken cancellationToken);
    Task<Result<CustomerLoginProfile>> GetCurrentProfileAsync(Guid customerId, CancellationToken cancellationToken);
    Task<Result<CustomerLogoutResult>> LogoutAsync(Guid customerId, string jti, string correlationId, CancellationToken cancellationToken);
}
