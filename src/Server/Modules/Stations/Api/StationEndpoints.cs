using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Stations.Application;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Stations;
using Microsoft.AspNetCore.Authorization;

namespace GameNet.Server.Modules.Stations.Api;

public static class StationEndpoints
{
    public static void MapStationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/stations")
            .RequireAuthorization();

        group.MapGet("", async (
            StationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(GetActor(context), cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<IReadOnlyList<StationResponse>>(
                    result.Value.Select(ToResponse).ToArray(),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });

        group.MapPost("", async (
            CreateStationRequest request,
            StationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var key = context.Request.Headers[ApiHeaders.IdempotencyKey].ToString();
            var result = await service.CreateAsync(
                new CreateStationCommand(
                    request.Code,
                    request.Name,
                    ToDomainType(request.Type),
                    key,
                    Guid.NewGuid().ToString("N"),
                    GetActor(context)),
                cancellationToken);

            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/stations/{result.Value.Id}",
                    new ApiEnvelope<StationResponse>(
                        ToResponse(result.Value),
                        CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            RenameStationRequest request,
            StationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var key = context.Request.Headers[ApiHeaders.IdempotencyKey].ToString();
            var result = await service.RenameAsync(
                new RenameStationCommand(
                    id,
                    request.Name,
                    key,
                    Guid.NewGuid().ToString("N"),
                    GetActor(context)),
                cancellationToken);

            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<StationResponse>(
                    ToResponse(result.Value),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });
    }

    private static StationActorContext GetActor(HttpContext context)
    {
        var actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue("sub");

        var permissions = context.User.Claims
            .Where(x => string.Equals(x.Type, "permission", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);

        var source = context.Request.Headers.TryGetValue(ApiHeaders.Source, out var supplied) && !string.IsNullOrWhiteSpace(supplied)
            ? supplied.ToString()
            : "Unknown";
        return new StationActorContext("operator", actorId, source, permissions);
    }

    private static StationType ToDomainType(StationTypeContract type) =>
        type switch
        {
            StationTypeContract.Pc => StationType.Pc,
            StationTypeContract.Ps5 => StationType.Ps5,
            StationTypeContract.Foosball => StationType.Foosball,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static StationResponse ToResponse(StationDto dto) =>
        new(
            dto.Id,
            dto.Code,
            dto.Name,
            dto.Type switch
            {
                StationType.Pc => StationTypeContract.Pc,
                StationType.Ps5 => StationTypeContract.Ps5,
                StationType.Foosball => StationTypeContract.Foosball,
                _ => throw new ArgumentOutOfRangeException()
            },
            dto.Status switch
            {
                StationStatus.Disabled => StationStatusContract.Disabled,
                StationStatus.Available => StationStatusContract.Available,
                StationStatus.Maintenance => StationStatusContract.Maintenance,
                StationStatus.RecoveryRequired => StationStatusContract.RecoveryRequired,
                _ => throw new ArgumentOutOfRangeException()
            },
            dto.Version);

    private static IResult Failure(GameNet.Shared.Primitives.Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "stations.forbidden" => StatusCodes.Status403Forbidden,
            "stations.not_found" => StatusCodes.Status404NotFound,
            "stations.code_exists" => StatusCodes.Status409Conflict,
            "stations.concurrency_conflict" => StatusCodes.Status409Conflict,
            "idempotency.in_flight" => StatusCodes.Status409Conflict,
            "idempotency.key_reused" => StatusCodes.Status409Conflict,
            "idempotency.required" => StatusCodes.Status400BadRequest,
            "stations.invalid" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
