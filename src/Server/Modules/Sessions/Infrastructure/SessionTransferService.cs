using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Validates and applies same-type station transfer as a dedicated Session use case.
/// Billing snapshots and monotonic timing evidence remain unchanged; final occupancy and
/// concurrency validation are enforced by the transactional mutation pipeline and DB index.
/// </summary>
public sealed class SessionTransferService(
    GameNet.Server.Persistence.GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    IStationSessionAccess stations,
    IAuditWriter audit,
    GameNet.Shared.Primitives.IGameClock clock)
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<SessionResponse>> TransferAsync(
            TransferSessionCommand command,
            CancellationToken cancellationToken)
        {
            if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
                return SessionService.Fail<SessionResponse>("sessions.forbidden", "Not allowed to transfer sessions.");
    
            var reason = command.Reason?.Trim() ?? string.Empty;
            if (!command.Confirmed ||
                command.TargetStationId == Guid.Empty ||
                reason.Length is < 5 or > 300 ||
                reason.Any(char.IsControl))
                return SessionService.Fail<SessionResponse>(
                    "sessions.transfer_invalid",
                    "Explicit confirmation, a destination station, and a printable reason from 5 through 300 characters are required.");
    
            var normalized = command with { Reason = reason };
            var payload = new
            {
                normalized.SessionId,
                normalized.TargetStationId,
                normalized.ExpectedVersion,
                normalized.Reason,
                normalized.Confirmed
            };
    
            try
            {
                return await mutationPipeline.ExecuteAsync(
                    $"sessions.transfer:{command.Actor.OperatorId:D}",
                    "session.transfer",
                    command.IdempotencyKey,
                    requestFingerprint.Compute(payload),
                    200,
                    Lease,
                    Retention,
                    async token =>
                    {
                        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == normalized.SessionId, token);
                        if (session is null)
                            return SessionService.Fail<SessionResponse>("sessions.not_found", "Session was not found.");
                        if (session.Version != normalized.ExpectedVersion)
                            return SessionService.Fail<SessionResponse>("sessions.version_conflict", "Session changed; refresh before transferring it.");
                        if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
                            return SessionService.Fail<SessionResponse>("sessions.invalid_state", "Only an active or paused Session can be transferred.");
                        if (session.StationId == normalized.TargetStationId)
                            return SessionService.Fail<SessionResponse>("sessions.transfer_invalid", "Source and destination stations must differ.");
    
                        var source = await stations.FindForSessionStartAsync(session.StationId, token);
                        if (source is null)
                            return SessionService.Fail<SessionResponse>("sessions.station_not_found", "The Session's current station was not found.");
    
                        var destination = await stations.FindForSessionStartAsync(normalized.TargetStationId, token);
                        if (destination is null)
                            return SessionService.Fail<SessionResponse>("sessions.station_not_found", "Destination station was not found.");
                        if (source.Type != destination.Type)
                            return SessionService.Fail<SessionResponse>(
                                "sessions.transfer_type_mismatch",
                                "A Session can only transfer to a station of the same type; tariff and billing snapshots are preserved.");
                        if (destination.Status != StationStatusContract.Available)
                            return SessionService.Fail<SessionResponse>("sessions.transfer_station_unavailable", "Destination station is not administratively available.");
                        if (destination.Type == StationTypeContract.Pc && !destination.AgentOnline)
                            return SessionService.Fail<SessionResponse>(
                                "sessions.transfer_agent_unavailable",
                                "Destination PC Agent is unbound, offline, or has no active credential and fresh authoritative heartbeat/lease.");
    
                        var destinationOccupied = await db.Sessions.AnyAsync(
                            x => x.StationId == destination.Id &&
                                 (x.Status == SessionStatus.Active ||
                                  x.Status == SessionStatus.Paused ||
                                  x.Status == SessionStatus.PendingSettlement),
                            token);
                        if (destinationOccupied)
                            return SessionService.Fail<SessionResponse>(
                                "sessions.transfer_destination_occupied",
                                "Destination station has an active, paused, or pending-settlement Session.");
    
                        var before = new
                        {
                            Session = SessionService.ToResponse(session),
                            SourceStationId = source.Id,
                            SourceStationCode = source.Code,
                            DestinationStationId = destination.Id,
                            DestinationStationCode = destination.Code,
                            StationType = source.Type,
                            session.AccumulatedElapsedTicks,
                            session.CurrentSegmentStartedMonotonicTimestamp,
                            session.CurrentSegmentStartedMonotonicFrequency,
                            session.CurrentSegmentStartedSystemUptimeMilliseconds,
                            session.CurrentSegmentStartedBootIdentifier,
                            Reason = normalized.Reason
                        };
    
                        try
                        {
                            session.TransferTo(destination.Id, normalized.Actor.OperatorId);
                        }
                        catch (InvalidOperationException)
                        {
                            return SessionService.Fail<SessionResponse>("sessions.invalid_state", "Session cannot be transferred from its current state.");
                        }
                        catch (ArgumentException)
                        {
                            return SessionService.Fail<SessionResponse>("sessions.transfer_invalid", "The destination or operator identity is invalid.");
                        }
    
                        var response = SessionService.ToResponse(session);
                        var after = new
                        {
                            Session = response,
                            SourceStationId = source.Id,
                            SourceStationCode = source.Code,
                            DestinationStationId = destination.Id,
                            DestinationStationCode = destination.Code,
                            StationType = source.Type,
                            session.AccumulatedElapsedTicks,
                            session.CurrentSegmentStartedMonotonicTimestamp,
                            session.CurrentSegmentStartedMonotonicFrequency,
                            session.CurrentSegmentStartedSystemUptimeMilliseconds,
                            session.CurrentSegmentStartedBootIdentifier,
                            Reason = normalized.Reason
                        };
    
                        audit.Append(new AuditRecord(
                            clock.UtcNow,
                            "Operator",
                            normalized.Actor.OperatorId.ToString("D"),
                            "sessions.transfer",
                            "Session",
                            session.Id.ToString("D"),
                            normalized.Reason,
                            normalized.Actor.CorrelationId,
                            normalized.Actor.Source,
                            "Succeeded",
                            normalized.IdempotencyKey,
                            JsonSerializer.Serialize(before),
                            JsonSerializer.Serialize(after)));
    
                        return Result<SessionResponse>.Success(response);
                    },
                    cancellationToken);
            }
            catch (PersistenceConflictException ex) when (IsStationOccupancyConflict(ex))
            {
                // The partial unique index remains the final arbiter if two transfers race for the same destination.
                return SessionService.Fail<SessionResponse>(
                    "sessions.transfer_destination_occupied",
                    "Destination station was claimed by a concurrent Session operation; refresh and choose another station.");
            }
            catch (PersistenceConflictException)
            {
                return SessionService.Fail<SessionResponse>("sessions.version_conflict", "Session changed concurrently; refresh before transferring it.");
            }
            catch (DbUpdateConcurrencyException)
            {
                return SessionService.Fail<SessionResponse>("sessions.version_conflict", "Session changed concurrently; refresh before transferring it.");
            }
        }

    private static bool IsStationOccupancyConflict(PersistenceConflictException exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is PostgresException postgres &&
                    string.Equals(postgres.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal) &&
                    string.Equals(postgres.ConstraintName, "ux_sessions_one_unsettled_per_station", StringComparison.Ordinal))
                    return true;
            }
    
            return false;
        }
}
