using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Api;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers").RequireAuthorization("Operator");
        group.MapGet("", async (ICustomerManagementService service, HttpContext context, CancellationToken ct,
            string? query = null, bool includeInactive = false, int take = 100) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.ListAsync(actor, query, includeInactive, take, ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerResponse[]>(result.Value.ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.CustomersRead);

        group.MapPost("", async (CreateCustomerRequest request, ICustomerManagementService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.CreateAsync(new CreateCustomerCommand(request.Code, request.DisplayName, request.Nickname,
                request.PhoneNumber, context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/customers/{result.Value.Id:D}",
                    new ApiEnvelope<CustomerResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.CustomersWrite);

        group.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerManagementService service,
            HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.UpdateProfileAsync(new UpdateCustomerProfileCommand(id, request.DisplayName, request.Nickname,
                request.PhoneNumber, request.ExpectedVersion, context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.CustomersWrite);

        group.MapPut("/{id:guid}/status", async (Guid id, SetCustomerStatusRequest request, ICustomerManagementService service,
            HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.SetStatusAsync(new SetCustomerStatusCommand(id, request.Status, request.ExpectedVersion,
                request.Reason, context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.CustomersWrite);
        endpoints.MapCustomerAuthenticationEndpoints();
    }

    private static bool TryGetActor(HttpContext context, out CustomerActorContext actor)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(id, out var operatorId) || operatorId == Guid.Empty)
        {
            actor = null!;
            return false;
        }
        var permissions = context.User.Claims
            .Where(x => string.Equals(x.Type, "permission", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        actor = new CustomerActorContext(operatorId, CorrelationIdMiddleware.GetCurrent(context), "Desktop", permissions);
        return true;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "auth.identity_missing" => StatusCodes.Status401Unauthorized,
            "customers.forbidden" => StatusCodes.Status403Forbidden,
            "customers.not_found" => StatusCodes.Status404NotFound,
            "customers.code_exists" or "customers.conflict" or "customers.version_conflict" or "customers.status_invalid" or
                "customers.walkin_profile_forbidden" or "idempotency.in_flight" or "idempotency.key_reused" or "idempotency.unavailable" => StatusCodes.Status409Conflict,
            "customers.invalid" or "idempotency.required" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)), statusCode: status);
    }
}
