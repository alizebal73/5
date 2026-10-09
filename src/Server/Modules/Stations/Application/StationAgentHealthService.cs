using GameNet.Server.Infrastructure.Realtime;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Stations.Application;

/// <summary>
/// Use case for a read-only, short-lived Agent health probe. It never accepts arbitrary OS commands.
/// </summary>
public sealed class StationAgentHealthService(
    IStationRepository stations,
    IAgentHealthProbeDispatcher dispatcher)
{
    public async Task<Result<AgentCommandAcknowledgement>> ProbeAsync(
        Guid stationId,
        StationActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!actor.Permissions.Contains(Permissions.StationsRead))
            return Result<AgentCommandAcknowledgement>.Failure(
                new Error("stations.forbidden", "Not allowed to inspect station health."));

        var station = await stations.FindAsync(stationId, cancellationToken);
        if (station is null)
            return Result<AgentCommandAcknowledgement>.Failure(
                new Error("stations.not_found", "Station was not found."));

        if (station.Type != StationType.Pc)
            return Result<AgentCommandAcknowledgement>.Failure(
                new Error("stations.agent_not_applicable", "Only PC stations use the Windows Agent."));

        if (string.IsNullOrWhiteSpace(station.AgentDeviceId))
            return Result<AgentCommandAcknowledgement>.Failure(
                new Error("stations.agent_not_bound", "No Agent is bound to this PC."));

        var acknowledgement = await dispatcher.ProbeAsync(station.Id, station.AgentDeviceId, cancellationToken);
        if (acknowledgement is null)
            return Result<AgentCommandAcknowledgement>.Failure(
                new Error("stations.agent_unavailable", "Agent is offline or did not acknowledge the health probe in time."));

        return Result<AgentCommandAcknowledgement>.Success(acknowledgement);
    }
}
