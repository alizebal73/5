using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Sessions.Api;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/sessions").RequireAuthorization("Operator");

        group.MapGet("/active", async (
            Guid? stationId,
            ISessionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var result = await service.ListActiveAsync(actor, stationId, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<SessionResponse[]>(result.Value.ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.SessionsOperate);

        group.MapPost("", async (
            StartSessionRequest request,
            ISessionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var command = new StartSessionCommand(
                request.StationId, request.TariffId, request.CustomerId,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
            var result = await service.StartAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/sessions/{result.Value.Id:D}",
                    new ApiEnvelope<SessionResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.SessionsOperate);

        group.MapPost("/{id:guid}/end", async (
            Guid id,
            EndSessionRequest request,
            ISessionService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var command = new EndSessionCommand(
                id, request.ExpectedVersion,
                context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
            var result = await service.EndAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<InvoicePreviewResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.SessionsOperate);
    }

    internal static bool TryGetActor(HttpContext context, out SessionActorContext actor)
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
        actor = new SessionActorContext(
            operatorId, CorrelationIdMiddleware.GetCurrent(context), "Desktop", permissions);
        return true;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "sessions.forbidden" => StatusCodes.Status403Forbidden,
            "sessions.not_found" or "sessions.station_not_found" or "sessions.tariff_not_found" or "sessions.customer_not_found" => StatusCodes.Status404NotFound,
            "sessions.station_unavailable" or "sessions.agent_unavailable" or "sessions.station_has_active_session" or "sessions.station_pending_settlement" or "sessions.tariff_station_mismatch" or
            "sessions.version_conflict" or "sessions.invalid_state" or "sessions.invalid_clock" or "sessions.amount_overflow" or
            "idempotency.in_flight" or "idempotency.key_reused" => StatusCodes.Status409Conflict,
            "sessions.pc_only" or "idempotency.required" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(
            new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
