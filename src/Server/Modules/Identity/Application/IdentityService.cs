using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Transactions;
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
    public Task<Result<BootstrapAdminResponseModel>> BootstrapAsync(string username, string displayName, string password,
        string correlationId, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async ct =>
        {
            await repository.AcquireBootstrapLockAsync(ct);
            if (await repository.AnyUsersAsync(ct))
                return Result<BootstrapAdminResponseModel>.Failure(new Error("bootstrap.not_required", "Initial administrator setup is already complete."));
            try
            {
                var now = clock.UtcNow;
                var user = OperatorUser.Create(Guid.NewGuid(), username, displayName, passwordHasher.Hash(password), now);
                var role = Role.Create(Guid.NewGuid(), "owner", "Owner");
                await repository.AddUserAsync(user, ct);
                await repository.AddRoleAsync(role, ct);
                await repository.AddUserRoleAsync(new UserRole(user.Id, role.Id), ct);
                foreach (var permission in AllPermissions)
                    await repository.AddRolePermissionAsync(new RolePermission(role.Id, permission), ct);
                audit.Append(new AuditRecord(
                    OccurredAtUtc: now, ActorType: "System", ActorId: user.Id.ToString("D"),
                    Operation: "identity.bootstrap", ReferenceType: "OperatorUser", ReferenceId: user.Id.ToString("D"),
                    Reason: "Initial owner account created", CorrelationId: correlationId, Source: "Setup",
                    Outcome: "Succeeded", AfterJson: JsonSerializer.Serialize(new { user.Id, user.Username, role.Code })));
                return Result<BootstrapAdminResponseModel>.Success(new BootstrapAdminResponseModel(user.Id, user.Username));
            }
            catch (ArgumentException ex)
            {
                return Result<BootstrapAdminResponseModel>.Failure(new Error("identity.invalid", ex.Message));
            }
        }, cancellationToken);

    public async Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken) =>
        !await repository.AnyUsersAsync(cancellationToken);

    public Task<Result<OperatorLoginResult>> LoginAsync(string username, string password, string correlationId,
        CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async ct =>
        {
            var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0 || string.IsNullOrWhiteSpace(password))
            {
                AppendLoginRejected(null, null, correlationId);
                return Result<OperatorLoginResult>.Failure(new Error("auth.invalid_credentials", "Username or password is incorrect."));
            }
            var user = await repository.FindUserAsync(normalized, ct);
            if (user is null)
            {
                AppendLoginRejected(null, null, correlationId);
                return Result<OperatorLoginResult>.Failure(new Error("auth.invalid_credentials", "Username or password is incorrect."));
            }
            if (!user.IsActive)
            {
                AppendLoginRejected(user, "Account disabled", correlationId);
                return Result<OperatorLoginResult>.Failure(new Error("auth.disabled", "This account is disabled."));
            }
            if (user.IsLocked(clock.UtcNow))
            {
                AppendLoginRejected(user, "Account locked", correlationId);
                return Result<OperatorLoginResult>.Failure(new Error("auth.locked", "This account is temporarily locked."));
            }
            if (!passwordHasher.Verify(password, user.PasswordHash))
            {
                user.RecordFailedLogin(clock.UtcNow);
                AppendLoginRejected(user, "Credentials rejected", correlationId);
                return Result<OperatorLoginResult>.Failure(new Error("auth.invalid_credentials", "Username or password is incorrect."));
            }

            var now = clock.UtcNow;
            user.RecordSuccessfulLogin(now);
            var permissions = await repository.GetPermissionsAsync(user.Id, ct);
            var token = tokenService.Issue(user, permissions);
            await repository.AddSessionAsync(new AuthSession(Guid.NewGuid(), user.Id, token.Jti, now, token.ExpiresAtUtc), ct);
            audit.Append(new AuditRecord(
                OccurredAtUtc: now, ActorType: "Operator", ActorId: user.Id.ToString("D"),
                Operation: "identity.login", ReferenceType: "OperatorUser", ReferenceId: user.Id.ToString("D"),
                Reason: null, CorrelationId: correlationId, Source: "Desktop", Outcome: "Succeeded",
                AfterJson: JsonSerializer.Serialize(new { user.Id, user.Username, PermissionCount = permissions.Count })));
            return Result<OperatorLoginResult>.Success(new OperatorLoginResult(token, user.Id, user.Username,
                user.DisplayName, permissions.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
        }, cancellationToken);

    public async Task<Result<CurrentOperatorResult>> GetCurrentOperatorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<CurrentOperatorResult>.Failure(new Error("auth.identity_not_found", "The operator identity was not found."));
        var permissions = await repository.GetPermissionsAsync(userId, cancellationToken);
        return Result<CurrentOperatorResult>.Success(new CurrentOperatorResult(user.Id, user.Username, user.DisplayName,
            permissions.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
    }

    public Task<Result<bool>> LogoutAsync(Guid userId, string jti, string correlationId, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async ct =>
        {
            var session = await repository.FindSessionAsync(jti, ct);
            if (session is null || session.UserId != userId)
                return Result<bool>.Failure(new Error("auth.session_not_found", "Session was not found."));
            var now = clock.UtcNow;
            session.Revoke(now);
            await repository.UpdateSessionAsync(session, ct);
            audit.Append(new AuditRecord(
                OccurredAtUtc: now, ActorType: "Operator", ActorId: userId.ToString("D"),
                Operation: "identity.logout", ReferenceType: "AuthSession", ReferenceId: session.Id.ToString("D"),
                Reason: null, CorrelationId: correlationId, Source: "Desktop", Outcome: "Succeeded", IdempotencyKey: jti));
            return Result<bool>.Success(true);
        }, cancellationToken);

    private void AppendLoginRejected(OperatorUser? user, string? reason, string correlationId) =>
        audit.Append(new AuditRecord(
            OccurredAtUtc: clock.UtcNow, ActorType: user is null ? "Anonymous" : "Operator",
            ActorId: user?.Id.ToString("D"), Operation: "identity.login_rejected",
            ReferenceType: user is null ? null : "OperatorUser", ReferenceId: user?.Id.ToString("D"),
            Reason: reason == "Account disabled" ? reason : "Invalid credentials or locked account",
            CorrelationId: correlationId, Source: "Desktop", Outcome: "Rejected"));

    private static readonly string[] AllPermissions = typeof(Permissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(field => field.FieldType == typeof(string))
        .Select(field => (string)field.GetValue(null)!)
        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
}

public sealed record BootstrapAdminResponseModel(Guid UserId, string Username);
