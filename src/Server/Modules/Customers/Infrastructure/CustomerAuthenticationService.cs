using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameNet.Server.Modules.Customers.Infrastructure;

public sealed class CustomerAuthenticationService(
    GameNetDbContext db, CommandMutationPipeline mutationPipeline, IRequestFingerprint requestFingerprint,
    ICustomerPasswordHasher passwordHasher, IAuditWriter audit, IGameClock clock, ITransactionCoordinator transactions, IOptions<GameNetOptions> options,
    IJwtSigningKeySecret signingSecret) : ICustomerAuthenticationService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(4);

    public async Task<Result<CustomerCredentialsSetResult>> SetCredentialsAsync(SetCustomerCredentialsCommand command, CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.CustomersCredentialsManage))
            return Fail<CustomerCredentialsSetResult>("customers.credentials_forbidden", "Not allowed to manage customer credentials.");
        if (command.CustomerId == Guid.Empty || command.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(command.Username) || !passwordHasher.IsValidPassword(command.Password))
            return Fail<CustomerCredentialsSetResult>("customers.credentials_invalid", "Customer, username, version or password is invalid.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Fail<CustomerCredentialsSetResult>("idempotency.required", "An idempotency key is required.");

        var encodedHash = passwordHasher.HashPassword(command.Password);
        var key = command.IdempotencyKey.Trim();
        var fingerprint = requestFingerprint.Compute(new { command.CustomerId, Username = command.Username, command.ExpectedVersion, Password = command.Password });
        try
        {
            return await mutationPipeline.ExecuteAsync(
                $"customers.credentials.set:{command.Actor.OperatorId:D}", "customer.credentials.set", key, fingerprint, 200,
                Lease, Retention,
                async ct =>
                {
                    var customer = await db.Customers.FromSqlInterpolated(
                        $"SELECT * FROM customers WHERE id = {command.CustomerId} FOR UPDATE").SingleOrDefaultAsync(ct);
                    if (customer is null || customer.Kind != CustomerKind.Registered)
                        return Fail<CustomerCredentialsSetResult>("customers.not_found", "Registered customer was not found.");
                    if (customer.Version != command.ExpectedVersion)
                        return Fail<CustomerCredentialsSetResult>("customers.version_conflict", "Customer data changed; refresh before setting the password.");

                    var oldVersion = customer.Version;
                    var oldUsername = customer.LoginUsername;
                    var credentialsWereConfigured = customer.PasswordHash is not null;
                    try { customer.SetCredentials(command.Username, encodedHash); }
                    catch (ArgumentException ex) { return Fail<CustomerCredentialsSetResult>("customers.credentials_invalid", ex.Message); }
                    var now = clock.UtcNow;
                    await db.CustomerLoginSessions.Where(x => x.CustomerId == customer.Id && x.RevokedAtUtc == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
                    var response = new CustomerCredentialsSetResult(customer.Id, customer.LoginUsername!, customer.Version, now);
                    audit.Append(new AuditRecord(now, "Operator", command.Actor.OperatorId.ToString("D"),
                        "customer.credentials.set", "Customer", customer.Id.ToString("D"), null, command.Actor.CorrelationId,
                        command.Actor.Source, "Succeeded", key,
                        JsonSerializer.Serialize(new { customer.Id, Username = oldUsername, CustomerVersion = oldVersion, CredentialsConfigured = credentialsWereConfigured }),
                        JsonSerializer.Serialize(new { customer.Id, Username = customer.LoginUsername, CustomerVersion = customer.Version, CredentialsConfigured = true, ExistingCustomerLoginsRevoked = true })));
                    return Result<CustomerCredentialsSetResult>.Success(response);
                }, cancellationToken, IsolationLevel.ReadCommitted);
        }
        catch (PersistenceConflictException)
        {
            return Fail<CustomerCredentialsSetResult>("customers.version_conflict", "Customer data changed concurrently; refresh before retrying.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<CustomerCredentialsSetResult>("customers.version_conflict", "Customer data changed concurrently; refresh before retrying.");
        }
    }

    public Task<Result<CustomerLoginResult>> LoginAsync(CustomerLoginCommand command, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async ct =>
        {
            var username = NormalizeUsername(command.Username);
            var password = command.Password ?? string.Empty;
            if (username is null)
            {
                _ = passwordHasher.VerifyPassword(password, CustomerPasswordHasher.DummyHash);
                audit.Append(new AuditRecord(clock.UtcNow, "Anonymous", "anonymous", "customers.login",
                    "Customer", null, "Credentials rejected", command.CorrelationId, command.Source, "Rejected"));
                return Fail<CustomerLoginResult>("customer_auth.invalid_credentials", "Username or password is incorrect.");
            }

            var customer = await db.Customers.FromSqlInterpolated(
                $"SELECT * FROM customers WHERE login_username = {username} FOR UPDATE").SingleOrDefaultAsync(ct);
            var verified = passwordHasher.VerifyPassword(password, customer?.PasswordHash ?? CustomerPasswordHasher.DummyHash) &&
                           passwordHasher.IsValidPassword(password);
            var now = clock.UtcNow;
            var eligible = customer is not null && customer.Kind == GameNet.Server.Modules.Customers.Domain.CustomerKind.Registered &&
                           customer.IsActive && !customer.IsBlocked && customer.LoginUsername is not null && customer.PasswordHash is not null;
            var locked = customer?.IsPasswordLoginLocked(now) ?? false;

            if (!eligible || !verified || locked)
            {
                if (customer is not null && customer.Kind == GameNet.Server.Modules.Customers.Domain.CustomerKind.Registered &&
                    !verified && !locked)
                    customer.RecordFailedPasswordAttempt(now);
                audit.Append(new AuditRecord(now, "Anonymous", "anonymous", "customers.login", "Customer",
                    customer?.Id.ToString("D"), "Credentials rejected", command.CorrelationId, command.Source, "Rejected"));
                return Fail<CustomerLoginResult>("customer_auth.invalid_credentials", "Username or password is incorrect.");
            }

            customer!.RecordSuccessfulPasswordLogin();
            var jti = Guid.NewGuid().ToString("N");
            var expires = now.Add(TokenLifetime);
            await db.CustomerLoginSessions.AddAsync(CustomerLoginSession.Create(jti, customer.Id, now, expires), ct);
            var auth = options.Value.Authentication;
            if (!auth.Enabled || string.IsNullOrWhiteSpace(auth.Issuer) || string.IsNullOrWhiteSpace(auth.Audience) ||
                string.IsNullOrWhiteSpace(signingSecret.SigningKey))
                throw new InvalidOperationException("Customer authentication is not fully configured.");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, customer.Id.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString("D")),
                new Claim(ClaimTypes.Name, customer.LoginUsername!),
                new Claim("actor_type", "Customer"),
                new Claim("customer_id", customer.Id.ToString("D")),
                new Claim("customer_code", customer.Code),
                new Claim("display_name", customer.DisplayName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, jti)
            };
            var token = new JwtSecurityToken(auth.Issuer, auth.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret.SigningKey)), SecurityAlgorithms.HmacSha256));
            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
            audit.Append(new AuditRecord(now, "Customer", customer.Id.ToString("D"), "customers.login", "Customer",
                customer.Id.ToString("D"), null, command.CorrelationId, command.Source, "Succeeded",
                AfterJson: JsonSerializer.Serialize(new { customer.Id, Username = customer.LoginUsername, ExpiresAtUtc = expires })));
            return Result<CustomerLoginResult>.Success(
                new CustomerLoginResult(accessToken, expires, customer.Id, customer.LoginUsername!, customer.DisplayName));
        }, cancellationToken, IsolationLevel.ReadCommitted);

    public async Task<Result<CustomerLoginProfile>> GetCurrentProfileAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = customerId == Guid.Empty ? null : await db.Customers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == customerId && x.Kind == GameNet.Server.Modules.Customers.Domain.CustomerKind.Registered &&
                 x.IsActive && !x.IsBlocked, cancellationToken);
        return customer is null
            ? Fail<CustomerLoginProfile>("customer_auth.invalid_credentials", "Customer session is invalid.")
            : Result<CustomerLoginProfile>.Success(new CustomerLoginProfile(customer.Id, customer.LoginUsername!, customer.DisplayName, customer.CreatedAtUtc));
    }

    public Task<Result<CustomerLogoutResult>> LogoutAsync(
        Guid customerId, string jti, string correlationId, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async ct =>
        {
            if (customerId == Guid.Empty || string.IsNullOrWhiteSpace(jti) || jti.Length != 32)
                return Fail<CustomerLogoutResult>("customer_auth.invalid_credentials", "Customer session is invalid.");

            var session = await db.CustomerLoginSessions.SingleOrDefaultAsync(
                x => x.Jti == jti && x.CustomerId == customerId, ct);
            if (session is not null && session.RevokedAtUtc is null)
            {
                var now = clock.UtcNow;
                session.Revoke(now);
                audit.Append(new AuditRecord(now, "Customer", customerId.ToString("D"), "customers.logout",
                    "Customer", customerId.ToString("D"), null, correlationId, "CustomerClient", "Succeeded"));
            }
            return Result<CustomerLogoutResult>.Success(new CustomerLogoutResult(true));
        }, cancellationToken, IsolationLevel.ReadCommitted);

    private static string? NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 40) return null;
        var value = username.Trim().ToUpperInvariant();
        return value.Length is >= 2 and <= 40 &&
               value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') &&
               char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]) ? value : null;
    }

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
}
