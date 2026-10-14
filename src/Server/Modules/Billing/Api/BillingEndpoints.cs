using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Shared.Contracts.V1.Api;
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
            ISessionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!SessionEndpoints.TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var result = await service.ListPendingInvoicesAsync(actor, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<InvoicePreviewResponse[]>(result.Value.ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingWrite);

        group.MapPost("/invoices/{invoiceId:guid}/cash", async (
            Guid invoiceId,
            CashCheckoutRequest request,
            ISessionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!SessionEndpoints.TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var command = new CashCheckoutCommand(
                invoiceId, request.AmountReceivedToman, request.ExpectedSessionVersion,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
            var result = await service.CheckoutCashAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CashCheckoutResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.BillingWrite);
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "billing.forbidden" => StatusCodes.Status403Forbidden,
            "billing.invoice_not_found" or "billing.session_not_found" => StatusCodes.Status404NotFound,
            "billing.invoice_not_pending" or "billing.session_version_conflict" or "billing.session_not_ready" or
            "billing.checkout_conflict" or "idempotency.in_flight" or "idempotency.key_reused" => StatusCodes.Status409Conflict,
            "billing.cash_insufficient" or "idempotency.required" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(
            new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
