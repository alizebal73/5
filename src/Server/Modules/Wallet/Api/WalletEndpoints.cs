using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Wallet.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Wallet.Api;

public static class WalletEndpoints
{
    public static void MapWalletEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/wallet").RequireAuthorization("Operator");

        group.MapGet("/customers/{customerId:guid}/balance",
            async (Guid customerId, IWalletService service, HttpContext context, CancellationToken ct) =>
            {
                if (!TryGetActor(context, out var actor))
                    return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
                var result = await service.GetBalanceAsync(customerId, actor, ct);
                return result.IsSuccess
                    ? Results.Ok(new ApiEnvelope<WalletBalanceResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                    : Failure(result.Error, context);
            }).RequireAuthorization(Permissions.WalletWrite);

        group.MapPost("/customers/{customerId:guid}/cash-top-up",
            async (Guid customerId, WalletCashTopUpRequest request, IWalletService service,
                HttpContext context, CancellationToken ct) =>
            {
                if (!TryGetActor(context, out var actor))
                    return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
                var command = new CashTopUpWalletCommand(customerId, request.AmountToCreditToman,
                    request.AmountReceivedToman, request.ExpectedCustomerVersion,
                    context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
                var result = await service.CashTopUpAsync(command, ct);
                return result.IsSuccess
                    ? Results.Ok(new ApiEnvelope<WalletCashTopUpResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                    : Failure(result.Error, context);
            }).RequireAuthorization(Permissions.WalletWrite);
    }

    private static bool TryGetActor(HttpContext context, out WalletActorContext actor)
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
        actor = new WalletActorContext(operatorId, CorrelationIdMiddleware.GetCurrent(context), "Desktop", permissions);
        return true;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "auth.identity_missing" => StatusCodes.Status401Unauthorized,
            "wallet.forbidden" => StatusCodes.Status403Forbidden,
            "wallet.customer_not_found" => StatusCodes.Status404NotFound,
            "wallet.invalid" => StatusCodes.Status400BadRequest,
            "wallet.customer_inactive" or "wallet.customer_version_conflict" or "wallet.balance_overflow" or
            "idempotency.in_flight" or "idempotency.key_reused" or "idempotency.unavailable" => StatusCodes.Status409Conflict,
            "idempotency.required" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)), statusCode: status);
    }
}