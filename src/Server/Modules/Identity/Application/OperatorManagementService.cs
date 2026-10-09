using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Identity.Domain;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Identity.Application;

public sealed class OperatorManagementService(
    IOperatorManagementRepository repository,
    IIdentityRepository identityRepository,
    IPasswordHasher passwordHasher,
    ITransactionCoordinator transactions,
    IIdempotencyStore idempotency,
    IAuditWriter audit,
    IGameClock clock)
{
    public async Task<Result<IReadOnlyList<OperatorManagedDto>>> ListOperatorsAsync(IdentityActor actor, CancellationToken ct)
    {
        var permissions = await identityRepository.GetPermissionsAsync(actor.UserId, ct);
        if (!permissions.Contains(Permissions.IdentityUsersRead))
            return Fail<IReadOnlyList<OperatorManagedDto>>("identity.forbidden", "Not allowed to read operator accounts.");
        return Result<IReadOnlyList<OperatorManagedDto>>.Success((await repository.ListUsersAsync(ct)).Select(ToOperatorDto).ToArray());
    }

    public async Task<Result<IReadOnlyList<RoleManagedDto>>> ListRolesAsync(IdentityActor actor, CancellationToken ct)
    {
        var permissions = await identityRepository.GetPermissionsAsync(actor.UserId, ct);
        if (!permissions.Contains(Permissions.IdentityUsersRead))
            return Fail<IReadOnlyList<RoleManagedDto>>("identity.forbidden", "Not allowed to read role definitions.");
        return Result<IReadOnlyList<RoleManagedDto>>.Success((await repository.ListRolesAsync(ct)).Select(ToRoleDto).ToArray());
    }

    public Task<Result<RoleManagedDto>> CreateRoleAsync(CreateRoleCommand command, CancellationToken ct) =>
        MutateAsync("roles.create", command.IdempotencyKey, new { command.Code, command.Name, command.Permissions }, command.Actor, 201,
            async token =>
            {
                var actorPermissions = await identityRepository.GetPermissionsAsync(command.Actor.UserId, token);
                if (!actorPermissions.Contains(Permissions.IdentityRolesManage))
                    return Fail<RoleManagedDto>("identity.forbidden", "Not allowed to manage role definitions.");
                Role role;
                try { role = Role.Create(Guid.NewGuid(), command.Code, command.Name); }
                catch (ArgumentException ex) { return Fail<RoleManagedDto>("identity.invalid", ex.Message); }
                if (role.Code == "owner")
                    return Fail<RoleManagedDto>("identity.system_role_immutable", "The system Owner role cannot be created or replaced.");
                if (await repository.RoleCodeExistsAsync(role.Code, token))
                    return Fail<RoleManagedDto>("identity.role_code_exists", "A role with this code already exists.");
                var owner = await repository.IsOwnerAsync(command.Actor.UserId, token);
                var error = ValidatePermissions(command.Permissions, actorPermissions, owner, out var values);
                if (error is not null) return Result<RoleManagedDto>.Failure(error);
                await repository.AddRoleAsync(role, token);
                foreach (var permission in values)
                    await repository.AddRolePermissionAsync(new RolePermission(role.Id, permission), token);
                var dto = new RoleManagedDto(role.Id, role.Code, role.Name, values, false);
                audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.UserId.ToString("D"),
                    "identity.role_create", "Role", role.Id.ToString("D"), "Role created",
                    command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                    AfterJson: JsonSerializer.Serialize(dto)));
                return Result<RoleManagedDto>.Success(dto);
            }, ct);

    public Task<Result<RoleManagedDto>> UpdateRolePermissionsAsync(UpdateRolePermissionsCommand command, CancellationToken ct) =>
        MutateAsync("roles.permissions", command.IdempotencyKey, new { command.RoleId, command.Permissions }, command.Actor, 200,
            async token =>
            {
                var actorPermissions = await identityRepository.GetPermissionsAsync(command.Actor.UserId, token);
                if (!actorPermissions.Contains(Permissions.IdentityRolesManage))
                    return Fail<RoleManagedDto>("identity.forbidden", "Not allowed to manage role definitions.");
                var role = await repository.FindRoleByIdAsync(command.RoleId, token);
                if (role is null) return Fail<RoleManagedDto>("identity.role_not_found", "Role was not found.");
                if (role.Code == "owner")
                    return Fail<RoleManagedDto>("identity.system_role_immutable", "The system Owner role cannot be edited.");
                var owner = await repository.IsOwnerAsync(command.Actor.UserId, token);
                var error = ValidatePermissions(command.Permissions, actorPermissions, owner, out var values);
                if (error is not null) return Result<RoleManagedDto>.Failure(error);
                var before = new RoleManagedDto(role.Id, role.Code, role.Name,
                    (await repository.GetRolePermissionsAsync(role.Id, token)).OrderBy(x => x, StringComparer.Ordinal).ToArray(), false);
                if (before.Permissions.SequenceEqual(values, StringComparer.Ordinal))
                    return Result<RoleManagedDto>.Success(before);
                await repository.ReplaceRolePermissionsAsync(role.Id, values, token);
                var after = before with { Permissions = values };
                audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.UserId.ToString("D"),
                    "identity.role_permissions_update", "Role", role.Id.ToString("D"), "Role permissions updated",
                    command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                    JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
                return Result<RoleManagedDto>.Success(after);
            }, ct);

    public Task<Result<OperatorManagedDto>> CreateOperatorAsync(CreateOperatorCommand command, CancellationToken ct) =>
        MutateAsync("users.create", command.IdempotencyKey,
            new { command.Username, command.DisplayName, command.Password, command.RoleId }, command.Actor, 201,
            async token =>
            {
                var actorPermissions = await identityRepository.GetPermissionsAsync(command.Actor.UserId, token);
                if (!actorPermissions.Contains(Permissions.IdentityUsersWrite))
                    return Fail<OperatorManagedDto>("identity.forbidden", "Not allowed to create operator accounts.");
                var role = await repository.FindRoleByIdAsync(command.RoleId, token);
                if (role is null) return Fail<OperatorManagedDto>("identity.role_not_found", "Role was not found.");
                var owner = await repository.IsOwnerAsync(command.Actor.UserId, token);
                if (role.Code == "owner" && !owner)
                    return Fail<OperatorManagedDto>("identity.owner_assignment_forbidden", "Only an Owner can create another Owner account.");
                var rolePermissions = await repository.GetRolePermissionsAsync(role.Id, token);
                if (!owner && rolePermissions.Any(x => !actorPermissions.Contains(x)))
                    return Fail<OperatorManagedDto>("identity.permission_escalation", "You cannot assign permissions that you do not hold.");
                if (string.IsNullOrWhiteSpace(command.Username) || command.Username.Length > 64 ||
                    command.Username.Trim().Any(char.IsWhiteSpace) ||
                    string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Trim().Length > 120 ||
                    string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 10 || command.Password.Length > 256)
                    return Fail<OperatorManagedDto>("identity.invalid", "Username, display name or password is invalid.");
                var username = command.Username.Trim().ToLowerInvariant();
                if (await repository.UsernameExistsAsync(username, token))
                    return Fail<OperatorManagedDto>("identity.username_exists", "An operator with this username already exists.");
                string hash;
                try { hash = passwordHasher.Hash(command.Password); }
                catch (ArgumentException ex) { return Fail<OperatorManagedDto>("identity.invalid", ex.Message); }
                OperatorUser user;
                try { user = OperatorUser.Create(Guid.NewGuid(), command.Username, command.DisplayName, hash, clock.UtcNow); }
                catch (ArgumentException ex) { return Fail<OperatorManagedDto>("identity.invalid", ex.Message); }
                await repository.AddUserAsync(user, token);
                await repository.AddUserRoleAsync(new UserRole(user.Id, role.Id), token);
                var dto = new OperatorManagedDto(user.Id, user.Username, user.DisplayName, user.IsActive,
                    user.FailedLoginCount, user.CreatedAtUtc, user.LastLoginAtUtc,
                    new[] { role.Id }, new[] { role.Code }, role.Code == "owner");
                audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.UserId.ToString("D"),
                    "identity.user_create", "OperatorUser", user.Id.ToString("D"), "Operator account created",
                    command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                    AfterJson: JsonSerializer.Serialize(dto)));
                return Result<OperatorManagedDto>.Success(dto);
            }, ct);

    public Task<Result<OperatorManagedDto>> AssignOperatorRoleAsync(AssignOperatorRoleCommand command, CancellationToken ct) =>
        MutateAsync("users.assign-role", command.IdempotencyKey, new { command.OperatorId, command.RoleId }, command.Actor, 200,
            async token =>
            {
                if (command.Actor.UserId == command.OperatorId)
                    return Fail<OperatorManagedDto>("identity.self_management_forbidden", "You cannot change your own role.");
                var actorPermissions = await identityRepository.GetPermissionsAsync(command.Actor.UserId, token);
                if (!actorPermissions.Contains(Permissions.IdentityUsersWrite))
                    return Fail<OperatorManagedDto>("identity.forbidden", "Not allowed to change operator roles.");
                var user = await repository.FindUserByIdAsync(command.OperatorId, token);
                if (user is null) return Fail<OperatorManagedDto>("identity.user_not_found", "Operator was not found.");
                var snapshot = (await repository.ListUsersAsync(token)).Single(x => x.Id == user.Id);
                var role = await repository.FindRoleByIdAsync(command.RoleId, token);
                if (role is null) return Fail<OperatorManagedDto>("identity.role_not_found", "Role was not found.");
                var actorIsOwner = await repository.IsOwnerAsync(command.Actor.UserId, token);
                var targetIsOwner = snapshot.RoleCodes.Contains("owner", StringComparer.Ordinal);
                if (targetIsOwner && role.Code != "owner")
                {
                    if (!actorIsOwner) return Fail<OperatorManagedDto>("identity.owner_protected", "Only an Owner can change an Owner account's role.");
                    var activeOwners = await repository.CountActiveOwnersAsync(token);
                    if (activeOwners - (user.IsActive ? 1 : 0) < 1)
                        return Fail<OperatorManagedDto>("identity.last_owner_required", "At least one active Owner account must remain.");
                }
                if (role.Code == "owner" && !actorIsOwner)
                    return Fail<OperatorManagedDto>("identity.owner_assignment_forbidden", "Only an Owner can assign the Owner role.");
                var rolePermissions = await repository.GetRolePermissionsAsync(role.Id, token);
                if (!actorIsOwner && rolePermissions.Any(x => !actorPermissions.Contains(x)))
                    return Fail<OperatorManagedDto>("identity.permission_escalation", "You cannot assign permissions that you do not hold.");
                var before = ToOperatorDto(snapshot);
                if (snapshot.RoleIds.SequenceEqual(new[] { role.Id })) return Result<OperatorManagedDto>.Success(before);
                await repository.ReplaceUserRoleAsync(user.Id, role.Id, token);
                await repository.RevokeActiveSessionsAsync(user.Id, clock.UtcNow, token);
                var after = before with { RoleIds = new[] { role.Id }, RoleCodes = new[] { role.Code }, IsOwner = role.Code == "owner" };
                audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.UserId.ToString("D"),
                    "identity.user_role_assign", "OperatorUser", user.Id.ToString("D"), "Operator role changed",
                    command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                    JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
                return Result<OperatorManagedDto>.Success(after);
            }, ct);

    public Task<Result<OperatorManagedDto>> SetOperatorActiveAsync(SetOperatorActiveCommand command, CancellationToken ct) =>
        MutateAsync("users.active", command.IdempotencyKey, new { command.OperatorId, command.IsActive }, command.Actor, 200,
            async token =>
            {
                var actorPermissions = await identityRepository.GetPermissionsAsync(command.Actor.UserId, token);
                if (!actorPermissions.Contains(Permissions.IdentityUsersWrite))
                    return Fail<OperatorManagedDto>("identity.forbidden", "Not allowed to change operator status.");
                var user = await repository.FindUserByIdAsync(command.OperatorId, token);
                if (user is null) return Fail<OperatorManagedDto>("identity.user_not_found", "Operator was not found.");
                var snapshot = (await repository.ListUsersAsync(token)).Single(x => x.Id == user.Id);
                if (user.IsActive == command.IsActive) return Result<OperatorManagedDto>.Success(ToOperatorDto(snapshot));
                if (command.Actor.UserId == command.OperatorId)
                    return Fail<OperatorManagedDto>("identity.self_management_forbidden", "You cannot change your own active status.");
                var isOwner = snapshot.RoleCodes.Contains("owner", StringComparer.Ordinal);
                if (isOwner && !await repository.IsOwnerAsync(command.Actor.UserId, token))
                    return Fail<OperatorManagedDto>("identity.owner_protected", "Only an Owner can change an Owner account's status.");
                if (!command.IsActive && isOwner && await repository.CountActiveOwnersAsync(token) <= 1)
                    return Fail<OperatorManagedDto>("identity.last_owner_required", "At least one active Owner account must remain.");
                var before = ToOperatorDto(snapshot);
                user.SetActive(command.IsActive);
                if (!command.IsActive) await repository.RevokeActiveSessionsAsync(user.Id, clock.UtcNow, token);
                var after = before with { IsActive = command.IsActive };
                audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.UserId.ToString("D"),
                    "identity.user_status", "OperatorUser", user.Id.ToString("D"),
                    command.IsActive ? "Operator account enabled" : "Operator account disabled",
                    command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                    JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
                return Result<OperatorManagedDto>.Success(after);
            }, ct);

    private async Task<Result<T>> MutateAsync<T>(string operation, string key, object payload, IdentityActor actor, int status,
        Func<CancellationToken, Task<Result<T>>> action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            return Fail<T>("idempotency.required", "A valid Idempotency-Key is required.");
        var scope = $"identity.{operation}:{actor.UserId:D}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        try
        {
            return await transactions.ExecuteAsync(async token =>
            {
                var claim = await idempotency.TryClaimAsync(scope, key.Trim(), operation, hash,
                    TimeSpan.FromSeconds(30), TimeSpan.FromDays(1), token);
                if (claim.Completed && claim.ResponseJson is not null)
                {
                    var replay = JsonSerializer.Deserialize<T>(claim.ResponseJson);
                    return replay is null ? Fail<T>("idempotency.invalid_replay", "Saved operation response is invalid.") : Result<T>.Success(replay);
                }
                if (claim.InFlight) throw new AbortMutation(new Error("idempotency.in_flight", "Operation is already in progress."));
                if (!claim.Claimed || string.IsNullOrWhiteSpace(claim.LeaseToken))
                    throw new AbortMutation(new Error("idempotency.unavailable", "Operation could not be claimed."));
                var result = await action(token);
                if (!result.IsSuccess) throw new AbortMutation(result.Error);
                await idempotency.CompleteAsync(scope, key.Trim(), claim.LeaseToken, status, JsonSerializer.Serialize(result.Value), token);
                return result;
            }, ct);
        }
        catch (AbortMutation ex) { return Fail<T>(ex.Error.Code, ex.Error.Message); }
        catch (IdempotencyKeyConflictException) { return Fail<T>("idempotency.key_reused", "Idempotency-Key was used for a different request."); }
        catch (PersistenceConflictException) { return Fail<T>("identity.conflict", "The requested identity change conflicts with current data."); }
    }

    private static Error? ValidatePermissions(string[]? requested, IReadOnlySet<string> actorPermissions, bool owner, out string[] values)
    {
        values = Array.Empty<string>();
        if (requested is null || requested.Length > PermissionCatalog.Length || requested.Any(string.IsNullOrWhiteSpace))
            return new Error("identity.invalid", "The permission list is invalid.");
        values = requested.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (values.Any(x => !PermissionSet.Contains(x))) return new Error("identity.unknown_permission", "The role contains an unknown permission.");
        if (!owner && values.Any(x => !actorPermissions.Contains(x)))
            return new Error("identity.permission_escalation", "You cannot grant permissions that you do not hold.");
        return null;
    }

    private static OperatorManagedDto ToOperatorDto(OperatorUserSnapshot user) =>
        new(user.Id, user.Username, user.DisplayName, user.IsActive, user.FailedLoginCount, user.CreatedAtUtc,
            user.LastLoginAtUtc, user.RoleIds, user.RoleCodes, user.RoleCodes.Contains("owner", StringComparer.Ordinal));
    private static RoleManagedDto ToRoleDto(RoleSnapshot role) =>
        role.Code == "owner" ? new RoleManagedDto(role.Id, role.Code, role.Name, PermissionCatalog, true) :
        new RoleManagedDto(role.Id, role.Code, role.Name, role.Permissions, false);
    private static readonly string[] PermissionCatalog = typeof(Permissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(x => x.FieldType == typeof(string)).Select(x => (string)x.GetValue(null)!)
        .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    private static readonly HashSet<string> PermissionSet = new(PermissionCatalog, StringComparer.Ordinal);
    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
    private sealed class AbortMutation(Error error) : Exception(error.Code) { public Error Error { get; } = error; }
}
