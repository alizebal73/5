using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Realtime;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Stations.Application;

/// <summary>
/// Use case for a read-only, short-lived Agent health probe. It never accepts arbitrary OS commands.
/// Audit writes use their own short transactions; the external SignalR dispatch is never inside a DB transaction.
/// </summary>
public sealed class StationAgentHealthService(
    IStationRepository stations,
    IAgentHealthProbeDispatcher dispatcher,
    IAuditWriter audit,
    ITransactionCoordinator transactions,
    IGameClock clock)
{
    public async Task<Result<AgentCommandAcknowledgement>> ProbeAsync(
        Guid stationId,
        StationActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!actor.Permissions.Contains(Permissions.StationsRead))
        {
            await RecordAuditAsync(stationId, actor, "Denied",
                new { errorCode = "stations.forbidden", commandType = nameof(AgentCommandType.HealthProbe) },
                cancellationToken);
            return Failure("stations.forbidden", "Not allowed to inspect station health.");
        }

        var station = await stations.FindAsync(stationId, cancellationToken);
        if (station is null)
        {
            await RecordAuditAsync(stationId, actor, "NotFound",
                new { errorCode = "stations.not_found", commandType = nameof(AgentCommandType.HealthProbe) },
                cancellationToken);
            return Failure("stations.not_found", "Station was not found.");
        }

        if (station.Type != StationType.Pc)
        {
            await RecordAuditAsync(stationId, actor, "Rejected",
                new { errorCode = "stations.agent_not_applicable", commandType = nameof(AgentCommandType.HealthProbe) },
                cancellationToken);
            return Failure("stations.agent_not_applicable", "Only PC stations use the Windows Agent.");
        }

        if (string.IsNullOrWhiteSpace(station.AgentDeviceId))
        {
            await RecordAuditAsync(stationId, actor, "Rejected",
                new { errorCode = "stations.agent_not_bound", commandType = nameof(AgentCommandType.HealthProbe) },
                cancellationToken);
            return Failure("stations.agent_not_bound", "No Agent is bound to this PC.");
        }

        // Persist intent first. If auditing fails, do not dispatch even this remote command.
        await RecordAuditAsync(stationId, actor, "Requested",
            new { commandType = nameof(AgentCommandType.HealthProbe), deviceId = station.AgentDeviceId },
            cancellationToken);

        AgentCommandAcknowledgement? acknowledgement;
        try
        {
            // Never keep a database transaction open across a SignalR/network operation.
            acknowledgement = await dispatcher.ProbeAsync(station.Id, station.AgentDeviceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordAuditAsync(stationId, actor, "Cancelled",
                new { commandType = nameof(AgentCommandType.HealthProbe), deviceId = station.AgentDeviceId },
                CancellationToken.None);
            throw;
        }

        if (acknowledgement is null)
        {
            await RecordAuditAsync(stationId, actor, "Unavailable",
                new { errorCode = "stations.agent_unavailable", commandType = nameof(AgentCommandType.HealthProbe), deviceId = station.AgentDeviceId },
                CancellationToken.None);
            return Failure("stations.agent_unavailable", "Agent is offline or did not acknowledge the health probe in time.");
        }

        var outcome = acknowledgement.Status switch
        {
            AgentCommandStatus.Succeeded => "Succeeded",
            AgentCommandStatus.Rejected => "Rejected",
            _ => "Failed"
        };
        await RecordAuditAsync(stationId, actor, outcome, new
        {
            commandType = nameof(AgentCommandType.HealthProbe),
            commandId = acknowledgement.CommandId,
            deviceId = acknowledgement.DeviceId,
            status = acknowledgement.Status.ToString(),
            errorCode = acknowledgement.ErrorCode,
            agentVersion = acknowledgement.AgentVersion,
            stationState = acknowledgement.StationState
        }, CancellationToken.None);

        return Result<AgentCommandAcknowledgement>.Success(acknowledgement);
    }

    private Task RecordAuditAsync(
        Guid stationId,
        StationActorContext actor,
        string outcome,
        object details,
        CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(token =>
        {
            audit.Append(new AuditRecord(
                OccurredAtUtc: clock.UtcNow,
                ActorType: actor.ActorType,
                ActorId: actor.ActorId,
                Operation: "stations.agent_health_probe",
                ReferenceType: "Station",
                ReferenceId: stationId.ToString("D"),
                Reason: null,
                CorrelationId: actor.CorrelationId,
                Source: actor.Source,
                Outcome: outcome,
                AfterJson: JsonSerializer.Serialize(details)));
            return Task.FromResult(true);
        }, cancellationToken);

    private static Result<AgentCommandAcknowledgement> Failure(string code, string message) =>
        Result<AgentCommandAcknowledgement>.Failure(new Error(code, message));
}
