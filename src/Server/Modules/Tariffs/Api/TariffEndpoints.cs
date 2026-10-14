using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Tariffs.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Tariffs.Api;

public static class TariffEndpoints
{
    public static void MapTariffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tariffs").RequireAuthorization("Operator");

        group.MapGet("", async (
            StationTypeContract? stationType,
            ITariffService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var result = await service.ListActiveAsync(actor, stationType, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<TariffResponse[]>(result.Value.ToArray(), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });

        group.MapPost("", async (
            CreateTariffRequest request,
            ITariffService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetActor(context, out var actor))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);

            var command = new CreateTariffCommand(
                request.Code, request.Name, request.StationType, request.HourlyRateToman,
                request.IsDefault, context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), actor);
            var result = await service.CreateAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/tariffs/{result.Value.Id:D}",
                    new ApiEnvelope<TariffResponse>(result.Value, CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.SettingsWrite);
    }

    private static bool TryGetActor(HttpContext context, out TariffActorContext actor)
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
        actor = new TariffActorContext(
            operatorId, CorrelationIdMiddleware.GetCurrent(context), "Desktop", permissions);
        return true;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "tariffs.forbidden" => StatusCodes.Status403Forbidden,
            "tariffs.code_exists" or "tariffs.conflict" => StatusCodes.Status409Conflict,
            "tariffs.invalid" or "idempotency.required" => StatusCodes.Status400BadRequest,
            "idempotency.in_flight" or "idempotency.key_reused" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(
            new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
