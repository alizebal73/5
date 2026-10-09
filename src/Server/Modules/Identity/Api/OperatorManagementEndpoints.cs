using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Identity.Api;

public static class OperatorManagementEndpoints
{
    public static void MapOperatorManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity").RequireAuthorization("Operator");
        group.MapGet("/users", async (OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.ListOperatorsAsync(actor, ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<OperatorAdminResponse[]>(result.Value.Select(ToResponse).ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityUsersRead);

        group.MapPost("/users", async (CreateOperatorRequest request, OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.CreateOperatorAsync(new CreateOperatorCommand(request.Username, request.DisplayName,
                request.Password, request.RoleId, context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identity/users/{result.Value.Id:D}",
                    new ApiEnvelope<OperatorAdminResponse>(ToResponse(result.Value), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityUsersWrite);

        group.MapPut("/users/{id:guid}/role", async (Guid id, AssignOperatorRoleRequest request, OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.AssignOperatorRoleAsync(new AssignOperatorRoleCommand(id, request.RoleId,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<OperatorAdminResponse>(ToResponse(result.Value), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityUsersWrite);

        group.MapPut("/users/{id:guid}/active", async (Guid id, SetOperatorActiveRequest request, OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.SetOperatorActiveAsync(new SetOperatorActiveCommand(id, request.IsActive,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<OperatorAdminResponse>(ToResponse(result.Value), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityUsersWrite);

        group.MapGet("/roles", async (OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.ListRolesAsync(actor, ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<RoleAdminResponse[]>(result.Value.Select(ToResponse).ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityUsersRead);

        group.MapPost("/roles", async (CreateRoleRequest request, OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.CreateRoleAsync(new CreateRoleCommand(request.Code, request.Name, request.Permissions,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identity/roles/{result.Value.Id:D}",
                    new ApiEnvelope<RoleAdminResponse>(ToResponse(result.Value), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityRolesManage);

        group.MapPut("/roles/{id:guid}/permissions", async (Guid id, UpdateRolePermissionsRequest request, OperatorManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor)) return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.UpdateRolePermissionsAsync(new UpdateRolePermissionsCommand(id, request.Permissions,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<RoleAdminResponse>(ToResponse(result.Value), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.IdentityRolesManage);
    }

    private static bool TryGetActor(HttpContext context, out IdentityActor actor)
    {
        var text = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(text, out var id)) { actor = null!; return false; }
        actor = new IdentityActor(id, CorrelationIdMiddleware.GetCurrent(context), "Desktop");
        return true;
    }

    private static OperatorAdminResponse ToResponse(OperatorManagedDto x) =>
        new(x.Id, x.Username, x.DisplayName, x.IsActive, x.FailedLoginCount,
            x.CreatedAtUtc, x.LastLoginAtUtc, x.RoleIds, x.RoleCodes, x.IsOwner);

    private static RoleAdminResponse ToResponse(RoleManagedDto x) =>
        new(x.Id, x.Code, x.Name, x.Permissions, x.IsSystemRole);

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "identity.invalid" or "identity.unknown_permission" or "idempotency.required" => StatusCodes.Status400BadRequest,
            "identity.forbidden" or "identity.permission_escalation" or "identity.owner_assignment_forbidden" or "identity.owner_protected" => StatusCodes.Status403Forbidden,
            "identity.role_not_found" or "identity.user_not_found" => StatusCodes.Status404NotFound,
            "identity.username_exists" or "identity.role_code_exists" or "identity.system_role_immutable" or "identity.last_owner_required" or
                "identity.self_management_forbidden" or "identity.conflict" or "idempotency.in_flight" or "idempotency.key_reused" or "idempotency.unavailable" => StatusCodes.Status409Conflict,
            "auth.identity_missing" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)), statusCode: status);
    }
}
