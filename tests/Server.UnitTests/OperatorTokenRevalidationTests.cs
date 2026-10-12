using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using GameNetAuthenticationOptions = GameNet.Server.Infrastructure.Configuration.AuthenticationOptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GameNet.Server.UnitTests;

public sealed class OperatorTokenRevalidationTests
{
    [Fact]
    public async Task Operator_token_permissions_are_replaced_with_current_database_permissions()
    {
        var result = await ValidateOperatorTokenAsync(
            tokenPermissions: [Permissions.StationsOperate, Permissions.IdentityUsersWrite],
            currentPermissions: new HashSet<string>([Permissions.StationsRead], StringComparer.Ordinal));

        Assert.Null(result.Failure);
        Assert.Equal(new[] { Permissions.StationsRead }, result.PermissionClaims);
        Assert.DoesNotContain(Permissions.StationsOperate, result.PermissionClaims);
        Assert.DoesNotContain(Permissions.IdentityUsersWrite, result.PermissionClaims);
    }

    [Fact]
    public async Task Operator_token_is_rejected_when_its_server_side_session_is_revoked()
    {
        var result = await ValidateOperatorTokenAsync(revokeSession: true);

        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Operator_token_is_rejected_when_the_user_is_disabled()
    {
        var result = await ValidateOperatorTokenAsync(userActive: false);

        Assert.NotNull(result.Failure);
    }

    private static async Task<OperatorValidationResult> ValidateOperatorTokenAsync(
        string[]? tokenPermissions = null,
        IReadOnlySet<string>? currentPermissions = null,
        bool revokeSession = false,
        bool userActive = true)
    {
        const string signingKey = "unit-test-signing-key-that-is-long-enough-for-hmac-sha256";
        const string jti = "operator-session-unit-test";
        var now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
        var userId = Guid.Parse("e3dd4eec-6d6f-4308-88fb-28c131feeb17");
        var user = OperatorUser.Create(userId, "operator", "Test Operator", "test-password-hash", now.AddDays(-1));
        if (!userActive)
            user.SetActive(false);

        var session = new AuthSession(
            Guid.NewGuid(),
            userId,
            jti,
            now.AddMinutes(-5),
            now.AddHours(1));
        if (revokeSession)
            session.Revoke(now.AddMinutes(-1));

        var permissions = currentPermissions ??
            new HashSet<string>([Permissions.StationsRead], StringComparer.Ordinal);
        var repository = new TestIdentityRepository(user, session, permissions);
        var applicationOptions = new GameNetOptions
        {
            Authentication = new GameNetAuthenticationOptions
            {
                Enabled = true,
                Issuer = "GameNet.Foundation.UnitTests",
                Audience = "GameNet.Operator.UnitTests"
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<GameNetOptions>>(Options.Create(applicationOptions));
        services.AddSingleton<IJwtSigningKeySecret>(new TestSigningKeySecret(signingKey));
        services.AddSingleton<IIdentityRepository>(repository);
        services.AddSingleton<IGameClock>(new TestGameClock(now));
        services.AddGameNetAuthentication();

        using var provider = services.BuildServiceProvider();
        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("actor_type", "Operator")
        };
        if (tokenPermissions is not null)
            claims.AddRange(tokenPermissions.Select(permission => new Claim("permission", permission)));

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        var tokenContext = new TokenValidatedContext(httpContext, scheme, bearerOptions)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme))
        };

        await bearerOptions.Events.OnTokenValidated!(tokenContext);
        var permissionClaims = tokenContext.Principal!
            .FindAll("permission")
            .Select(claim => claim.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new OperatorValidationResult(tokenContext.Result?.Failure, permissionClaims);
    }

    private sealed record TestSigningKeySecret(string SigningKey) : IJwtSigningKeySecret;

    private sealed record OperatorValidationResult(Exception? Failure, string[] PermissionClaims);

    private sealed class TestGameClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestIdentityRepository(
        OperatorUser user,
        AuthSession session,
        IReadOnlySet<string> permissions) : IIdentityRepository
    {
        public Task AcquireBootstrapLockAsync(CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) =>
            Task.FromException<bool>(new NotSupportedException());

        public Task<OperatorUser?> FindUserAsync(string username, CancellationToken cancellationToken) =>
            Task.FromException<OperatorUser?>(new NotSupportedException());

        public Task<OperatorUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<OperatorUser?>(userId == user.Id ? user : null);

        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(permissions);

        public Task AddUserAsync(OperatorUser value, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public Task AddRoleAsync(Role value, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public Task AddUserRoleAsync(UserRole value, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public Task AddRolePermissionAsync(RolePermission value, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public Task AddSessionAsync(AuthSession value, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public Task<AuthSession?> FindSessionAsync(string jti, CancellationToken cancellationToken) =>
            Task.FromResult<AuthSession?>(string.Equals(jti, session.Jti, StringComparison.Ordinal) ? session : null);

        public Task UpdateSessionAsync(AuthSession value, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());
    }
}
