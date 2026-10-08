using System.Text.Json;
using GameNet.Server.Application;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Identity.Application;

public sealed class IdentityService(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ITransactionCoordinator transactions,
    IAuditWriter audit,
    IGameClock clock)
{
    private const string OwnerRoleCode = "owner";

    public async Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken) =>
        !await repository.AnyUsersAsync(cancellationToken);

    public async Task<Result<object>> BootstrapAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                await repository.AcquireBootstrapLockAsync(ct);

                if (await repository.AnyUsersAsync(ct))
                    throw new MutationAbortedException(
                        new Error(
                            "bootstrap.not_required",
                            "Initial administrator setup is already complete."));

                var user = OperatorUser.Create(
                    Guid.NewGuid(),
                    username,
                    displayName,
                    passwordHasher.Hash(password),
                    clock.UtcNow);

                var role = Role.Create(Guid.NewGuid(), OwnerRoleCode, "Owner");

                await repository.AddUserAsync(user, ct);
                await repository.AddRoleAsync(role, ct);
                await repository.AddUserRoleAsync(
                    new UserRole(user.Id, role.Id),
                    ct);

                foreach (var permission in AllPermissions)
                    await repository.AddRolePermissionAsync(
                        new RolePermission(role.Id, permission),
                        ct);

                audit.Append(new AuditRecord(
                    clock.UtcNow,
                    "system",
                    null,
                    "identity.bootstrap",
                    "OperatorUser",
                    user.Id.ToString("D"),
                    "Initial administrator created",
                    null,
                    JsonSerializer.Serialize(
                        new { user.Id, user.Username, role.Code }),
                    "Setup",
                    user.Id.ToString("D"),
                    null,
                    "Succeeded"));

                return Result<object>.Success(
                    new { user.Id, user.Username });
            }, cancellationToken);
        }
        catch (MutationAbortedException ex)
        {
            return Result<object>.Failure(ex.Error);
        }
        catch (ArgumentException ex)
        {
            return Result<object>.Failure(
                new Error("identity.invalid", ex.Message));
        }
    }

    public async Task<Result<CurrentOperatorResult>> GetCurrentOperatorAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<CurrentOperatorResult>.Failure(
                new Error("auth.identity_not_found", "The operator identity was not found."));

        var permissions = await repository.GetPermissionsAsync(userId, cancellationToken);
        return Result<CurrentOperatorResult>.Success(
            new CurrentOperatorResult(
                user.Id,
                user.Username,
                user.DisplayName,
                permissions));
    }

    public async Task<Result<LoginResult>> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        return await transactions.ExecuteAsync(async ct =>
        {
            var user = await repository.FindUserAsync(
                username.Trim().ToLowerInvariant(),
                ct);

            if (user is null)
                return Result<LoginResult>.Failure(
                    new Error(
                        "auth.invalid_credentials",
                        "Username or password is incorrect."));

            if (!user.IsActive)
                return Result<LoginResult>.Failure(
                    new Error(
                        "auth.disabled",
                        "This account is disabled."));

            if (user.IsLocked(clock.UtcNow))
                return Result<LoginResult>.Failure(
                    new Error(
                        "auth.locked",
                        "This account is temporarily locked."));

            if (!passwordHasher.Verify(password, user.PasswordHash))
            {
                user.RecordFailedLogin(clock.UtcNow);
                return Result<LoginResult>.Failure(
                    new Error(
                        "auth.invalid_credentials",
                        "Username or password is incorrect."));
            }

            user.RecordSuccessfulLogin(clock.UtcNow);

            var permissions = await repository.GetPermissionsAsync(user.Id, ct);
            var issued = tokenService.Issue(user, permissions);
            var session = new AuthSession(
                Guid.NewGuid(),
                user.Id,
                issued.Jti,
                clock.UtcNow,
                issued.ExpiresAtUtc);

            await repository.AddSessionAsync(session, ct);

            audit.Append(new AuditRecord(
                clock.UtcNow,
                "operator",
                user.Id.ToString("D"),
                "identity.login",
                "OperatorUser",
                user.Id.ToString("D"),
                null,
                null,
                null,
                "Desktop",
                issued.Jti,
                null,
                "Succeeded"));

            return Result<LoginResult>.Success(
                new LoginResult(issued, user, permissions));
        }, cancellationToken);
    }

    public async Task<Result<object>> LogoutAsync(
        Guid userId,
        string jti,
        CancellationToken cancellationToken)
    {
        return await transactions.ExecuteAsync(async ct =>
        {
            var session = await repository.FindSessionAsync(jti, ct);
            if (session is null || session.UserId != userId)
                return Result<object>.Failure(
                    new Error(
                        "auth.session_not_found",
                        "Session was not found."));

            session.Revoke(clock.UtcNow);
            await repository.UpdateSessionAsync(session, ct);

            audit.Append(new AuditRecord(
                clock.UtcNow,
                "operator",
                userId.ToString("D"),
                "identity.logout",
                "AuthSession",
                session.Id.ToString("D"),
                null,
                null,
                null,
                "Desktop",
                jti,
                null,
                "Succeeded"));

            return Result<object>.Success(new { success = true });
        }, cancellationToken);
    }

    private static readonly string[] AllPermissions =
    [
        Permissions.StationsRead,
        Permissions.StationsOperate,
        Permissions.CustomersRead,
        Permissions.CustomersWrite,
        Permissions.SessionsOperate,
        Permissions.BillingWrite,
        Permissions.WalletWrite,
        Permissions.InventoryWrite,
        Permissions.ReportsRead,
        Permissions.SettingsWrite,
        Permissions.BackupOperate
    ];
}

public sealed record LoginResult(
    IssuedAccessToken Token,
    OperatorUser User,
    IReadOnlySet<string> Permissions);
