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

        group.MapGet("/customers/{customerId:guid}/balance", async (
            Guid customerId, IWalletService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.GetBalanceAsync(actor, customerId, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<WalletBalanceResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.WalletWrite);

        group.MapPost("/customers/{customerId:guid}/cash-top-ups", async (
            Guid customerId, WalletCashTopUpRequest request, IWalletService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var command = new WalletCashTopUpCommand(
                customerId, request.AmountToCreditToman, request.AmountReceivedToman,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
            var result = await service.TopUpCashAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/wallet/customers/{customerId:D}/balance",
                    new ApiEnvelope<WalletCashTopUpResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
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
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);
        actor = new WalletActorContext(operatorId, CorrelationIdMiddleware.GetCurrent(context), "Desktop", permissions);
        return true;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "wallet.forbidden" => StatusCodes.Status403Forbidden,
            "wallet.customer_not_found" => StatusCodes.Status404NotFound,
            "wallet.customer_blocked" or "wallet.customer_inactive" or "wallet.registered_customer_required" or
                "wallet.cash_insufficient" => StatusCodes.Status409Conflict,
            "wallet.amount_invalid" or "wallet.amount_overflow" or "wallet.balance_overflow" or "idempotency.required" =>
                StatusCodes.Status400BadRequest,
            "idempotency.in_flight" or "idempotency.key_reused" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiFailure(new ApiError(error.Code, error.Message),
            CorrelationIdMiddleware.GetCurrent(context)), statusCode: status);
    }
}
