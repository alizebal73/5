using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Stations.Application;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
namespace GameNet.Server.Modules.Stations.Api;
public static class StationEndpoints
{
    public static void MapStationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/stations").RequireAuthorization("Operator");
        group.MapGet("", async (StationService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var r = await svc.ListAsync(Actor(ctx), ct);
            return r.IsSuccess ? Results.Ok(new ApiEnvelope<StationResponse[]>(r.Value.Select(ToResponse).ToArray(), CorrelationIdMiddleware.GetCurrent(ctx))) : Failure(r.Error, ctx);
        });
        group.MapPost("", async (CreateStationRequest req, StationService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(req.Type)) return Failure(new Error("stations.invalid", "Unknown station type."), ctx);
            var type = (StationType)req.Type;
            var r = await svc.CreateAsync(new CreateStationCommand(req.Code, req.Name, type, ctx.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), Actor(ctx)), ct);
            return r.IsSuccess ? Results.Created($"/api/v1/stations/{r.Value.Id}", new ApiEnvelope<StationResponse>(ToResponse(r.Value), CorrelationIdMiddleware.GetCurrent(ctx))) : Failure(r.Error, ctx);
        });
        group.MapPut("/{id:guid}", async (Guid id, RenameStationRequest req, StationService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var r = await svc.RenameAsync(new RenameStationCommand(id, req.Name, req.ExpectedVersion, ctx.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), Actor(ctx)), ct);
            return r.IsSuccess ? Results.Ok(new ApiEnvelope<StationResponse>(ToResponse(r.Value), CorrelationIdMiddleware.GetCurrent(ctx))) : Failure(r.Error, ctx);
        });
        group.MapPut("/{id:guid}/agent", async (Guid id, BindStationAgentRequest req, StationService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var r = await svc.BindAgentAsync(new BindStationAgentCommand(id, req.DeviceId, req.ExpectedVersion, ctx.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), Actor(ctx)), ct);
            return r.IsSuccess ? Results.Ok(new ApiEnvelope<StationResponse>(ToResponse(r.Value), CorrelationIdMiddleware.GetCurrent(ctx))) : Failure(r.Error, ctx);
        });
        group.MapPut("/{id:guid}/status", async (Guid id, SetStationStatusRequest req, StationService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(req.Status)) return Failure(new Error("stations.invalid", "Unknown station status."), ctx);
            var r = await svc.SetStatusAsync(new SetStationStatusCommand(id, (StationStatus)req.Status, req.ExpectedVersion, ctx.Request.Headers[ApiHeaders.IdempotencyKey].ToString(), Actor(ctx)), ct);
            return r.IsSuccess ? Results.Ok(new ApiEnvelope<StationResponse>(ToResponse(r.Value), CorrelationIdMiddleware.GetCurrent(ctx))) : Failure(r.Error, ctx);
        });
    }
    private static StationActorContext Actor(HttpContext c)
    {
        var id = c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? c.User.FindFirstValue("sub");
        var permissions = c.User.Claims.Where(x => string.Equals(x.Type, "permission", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        return new StationActorContext("Operator", id, "Desktop", CorrelationIdMiddleware.GetCurrent(c), permissions);
    }
    private static StationResponse ToResponse(StationDto d) => new(d.Id, d.Code, d.Name, (StationTypeContract)d.Type, (StationStatusContract)d.Status,
        d.Version, d.AgentDeviceId, d.AgentOnline, d.LastHeartbeatAtUtc, d.AgentVersion, d.AgentReportedState);
    private static IResult Failure(Error e, HttpContext c)
    {
        var code = e.Code;
        var status = code switch
        {
            "stations.forbidden" => 403, "stations.not_found" => 404,
            "stations.code_exists" or "stations.device_already_bound" or "stations.agent_not_provisioned" or "stations.version_conflict" or "stations.conflict" or "idempotency.in_flight" or "idempotency.key_reused" or "idempotency.unavailable" => 409,
            "idempotency.required" or "stations.invalid" or "stations.invalid_transition" => 400, _ => 500
        };
        return Results.Json(new ApiFailure(new ApiError(e.Code, e.Message), CorrelationIdMiddleware.GetCurrent(c)), statusCode: status);
    }
}
