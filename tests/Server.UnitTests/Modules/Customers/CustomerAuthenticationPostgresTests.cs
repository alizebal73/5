using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Customers.Infrastructure;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Customers;

[Collection("Customer authentication PostgreSQL serial")]
public sealed class CustomerAuthenticationPostgresTests
{
    private const string Password = "a valid customer password 123";
    private const string SigningKey = "customer-auth-postgres-test-signing-key-that-is-long-enough-for-hs256";
    private const string Issuer = "GameNet.CustomerAuth.PostgreSQL.Tests";
    private const string Audience = "GameNet.CustomerClient.PostgreSQL.Tests";

    [Fact]
    public async Task Successful_customer_login_issues_only_customer_identity_and_logout_revokes_it()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var customerId = Guid.NewGuid();
        var code = "AUTH-" + token[..16].ToUpperInvariant();
        var username = "player-" + token[..16];
        var correlation = "customer-auth-ok-" + token;
        var clock = new TestClock(DateTimeOffset.UtcNow);

        try
        {
            var hasher = new CustomerPasswordHasher();
            await using (var seed = CreateContext(connectionString))
            {
                var customer = Customer.CreateRegistered(customerId, code, "Customer Auth Test", clock.UtcNow);
                customer.SetCredentials(username, hasher.HashPassword(Password));
                seed.Customers.Add(customer);
                await seed.SaveChangesAsync();
            }

            await using var db = CreateContext(connectionString);
            var service = CreateService(db, clock);
            var login = await service.LoginAsync(
                new CustomerLoginCommand(username, Password, correlation, "CustomerClient"),
                CancellationToken.None);

            Assert.True(login.IsSuccess, login.IsSuccess ? "" : login.Error.Code);
            Assert.Equal(customerId, login.Value.CustomerId);
            Assert.Equal(username.ToUpperInvariant(), login.Value.Username);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Value.AccessToken);
            var actorType = Assert.Single(jwt.Claims, claim => claim.Type == "actor_type");
            var subject = Assert.Single(jwt.Claims, claim => claim.Type == "customer_id");
            var jti = Assert.Single(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Jti);
            Assert.Equal("Customer", actorType.Value);
            Assert.Equal(customerId.ToString("D"), subject.Value);
            Assert.Equal(32, jti.Value.Length);
            Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "permission");

            var authority = new CustomerLoginAuthority(db, clock);
            Assert.True(await authority.IsLoginUsableAsync(customerId, jti.Value, CancellationToken.None));

            var logout = await service.LogoutAsync(customerId, jti.Value, correlation, CancellationToken.None);
            Assert.True(logout.IsSuccess, logout.IsSuccess ? "" : logout.Error.Code);
            Assert.True(logout.Value.LoggedOut);
            Assert.False(await authority.IsLoginUsableAsync(customerId, jti.Value, CancellationToken.None));

            var auditOperations = await db.AuditEntries.AsNoTracking()
                .Where(x => x.CorrelationId == correlation)
                .Select(x => x.Operation)
                .ToArrayAsync();
            Assert.Contains("customers.login", auditOperations);
            Assert.Contains("customers.logout", auditOperations);
        }
        finally
        {
            await CleanupAsync(connectionString, customerId, correlation);
        }
    }

    [Fact]
    public async Task Wrong_password_lockout_and_blocked_or_inactive_accounts_fail_closed()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(now);
        var hasher = new CustomerPasswordHasher();
        var activeId = Guid.NewGuid();
        var blockedId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var activeCode = "AU-" + token[..16].ToUpperInvariant();
        var blockedCode = "BL-" + token[..16].ToUpperInvariant();
        var inactiveCode = "IN-" + token[..16].ToUpperInvariant();
        var activeUsername = "active-" + token[..14];
        var blockedUsername = "blocked-" + token[..14];
        var inactiveUsername = "inactive-" + token[..14];
        var correlation = "customer-auth-negative-" + token;

        try
        {
            await using (var seed = CreateContext(connectionString))
            {
                var activeCustomerSeed = Customer.CreateRegistered(activeId, activeCode, "Active Test Customer", now);
                activeCustomerSeed.SetCredentials(activeUsername, hasher.HashPassword(Password));
                var blocked = Customer.CreateRegistered(blockedId, blockedCode, "Blocked Test Customer", now);
                blocked.SetCredentials(blockedUsername, hasher.HashPassword(Password));
                blocked.SetStatus(CustomerStatus.Blocked);
                var inactive = Customer.CreateRegistered(inactiveId, inactiveCode, "Inactive Test Customer", now);
                inactive.SetCredentials(inactiveUsername, hasher.HashPassword(Password));
                inactive.SetStatus(CustomerStatus.Inactive);
                seed.Customers.AddRange(activeCustomerSeed, blocked, inactive);
                await seed.SaveChangesAsync();
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var wrong = await AttemptLoginAsync(connectionString, clock, activeUsername, "incorrect password", correlation);
                Assert.False(wrong.IsSuccess);
                Assert.Equal("customer_auth.invalid_credentials", wrong.Error.Code);
            }

            var correctButLocked = await AttemptLoginAsync(connectionString, clock, activeUsername, Password, correlation);
            Assert.False(correctButLocked.IsSuccess);
            Assert.Equal("customer_auth.invalid_credentials", correctButLocked.Error.Code);

            var blockedLogin = await AttemptLoginAsync(connectionString, clock, blockedUsername, Password, correlation);
            Assert.False(blockedLogin.IsSuccess);
            Assert.Equal("customer_auth.invalid_credentials", blockedLogin.Error.Code);

            var inactiveLogin = await AttemptLoginAsync(connectionString, clock, inactiveUsername, Password, correlation);
            Assert.False(inactiveLogin.IsSuccess);
            Assert.Equal("customer_auth.invalid_credentials", inactiveLogin.Error.Code);

            await using var verify = CreateContext(connectionString);
            var active = await verify.Customers.AsNoTracking().SingleAsync(x => x.Id == activeId);
            Assert.Equal(0, active.PasswordFailedAttempts); // Counter resets when the 10-minute lock is applied.
            Assert.True(active.IsPasswordLoginLocked(now));
            Assert.Equal(0, await verify.CustomerLoginSessions.CountAsync(
                x => x.CustomerId == blockedId || x.CustomerId == inactiveId));
        }
        finally
        {
            await CleanupAsync(connectionString, activeId, correlation);
            await CleanupAsync(connectionString, blockedId, correlation);
            await CleanupAsync(connectionString, inactiveId, correlation);
        }
    }

    private static async Task<Result<CustomerLoginResult>> AttemptLoginAsync(
        string connectionString, TestClock clock, string username, string password, string correlation)
    {
        await using var db = CreateContext(connectionString);
        return await CreateService(db, clock).LoginAsync(
            new CustomerLoginCommand(username, password, correlation, "CustomerClient"),
            CancellationToken.None);
    }

    private static CustomerAuthenticationService CreateService(GameNetDbContext db, TestClock clock)
    {
        var transactions = new EfTransactionCoordinator(db);
        return new CustomerAuthenticationService(
            db,
            new CommandMutationPipeline(transactions, new EfIdempotencyStore(db, clock)),
            new TestFingerprint(),
            new CustomerPasswordHasher(),
            new EfAuditWriter(db),
            clock,
            transactions,
            Options.Create(new GameNetOptions
            {
                Authentication = new AuthenticationOptions
                {
                    Enabled = true,
                    Issuer = Issuer,
                    Audience = Audience
                }
            }),
            new TestSigningKeySecret(SigningKey));
    }

    private static GameNetDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GameNetDbContext>().UseNpgsql(connectionString).Options);

    private static async Task CleanupAsync(string connectionString, Guid customerId, string correlation)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM customer_login_sessions WHERE customer_id = {customerId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM customers WHERE id = {customerId}");
    }

    private static bool IsExplicitlyEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("GAMENET_CUSTOMER_AUTH_POSTGRES_TESTS"), "true",
            StringComparison.OrdinalIgnoreCase);

    private static string GetConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private sealed class TestClock(DateTimeOffset now) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public GameClockSnapshot Capture() =>
            new(UtcNow, 1_000, TimeSpan.TicksPerSecond, 5_000,
                Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0"));
    }

    private sealed class TestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }

    private sealed record TestSigningKeySecret(string SigningKey) : IJwtSigningKeySecret;
}
