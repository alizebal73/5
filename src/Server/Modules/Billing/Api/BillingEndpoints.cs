using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Billing.Application;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Billing.Api;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/billing").RequireAuthorization("Operator");

        group.MapGet("/pending", async (
            IBillingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var result = await service.ListPendingInvoicesAsync(actor, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<InvoicePreviewResponse[]>(result.Value.ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingWrite);

        group.MapGet("/invoices/{invoiceId:guid}/outcome", async (
            Guid invoiceId,
            IBillingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var result = await service.GetCashCheckoutOutcomeAsync(actor, invoiceId, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CashCheckoutOutcomeResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingWrite);

        group.MapGet("/recent-cash-checkouts", async (
            IBillingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var result = await service.ListRecentCashCheckoutsAsync(actor, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CashCheckoutOutcomeResponse[]>(result.Value.ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingWrite);

        group.MapPost("/invoices/{invoiceId:guid}/cash", async (
            Guid invoiceId,
            CashCheckoutRequest request,
            IBillingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var command = new CashCheckoutCommand(
                invoiceId,
                request.AmountReceivedToman,
                request.ExpectedSessionVersion,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(),
                actor);
            var result = await service.CheckoutCashAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CashCheckoutResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingWrite);

        group.MapPost("/invoices/{invoiceId:guid}/cash-reversal", async (
            Guid invoiceId,
            CashSaleReversalRequest request,
            IBillingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var command = new CashSaleReversalCommand(
                invoiceId, request.PaymentId, request.ConfirmedAmountToman, request.Reason,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
            var result = await service.ReverseCashSaleAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CashSaleReversalResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingCashReverse);
    }

    private static bool TryGetActor(HttpContext context, out BillingActorContext actor)
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
        actor = new BillingActorContext(
            operatorId,
            CorrelationIdMiddleware.GetCurrent(context),
            "Desktop",
            permissions);
        return true;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "billing.forbidden" => StatusCodes.Status403Forbidden,
            "billing.invoice_not_found" or "billing.session_not_found" => StatusCodes.Status404NotFound,
            "billing.invoice_not_pending" or "billing.invoice_not_paid" or "billing.sale_already_reversed" or
            "billing.reversal_conflict" or "billing.reversal_confirmation_mismatch" or
            "billing.session_version_conflict" or "billing.session_not_ready" or
            "billing.checkout_conflict" or "idempotency.in_flight" or "idempotency.key_reused" => StatusCodes.Status409Conflict,
            "billing.cash_insufficient" or "billing.reversal_invalid" or "idempotency.required" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(
            new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
