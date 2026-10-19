using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Contracts;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Server.Modules.Tariffs.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using static GameNet.Server.Modules.Sessions.Infrastructure.SessionMapping;

namespace GameNet.Server.Modules.Sessions.Infrastructure.UseCases;

/// <summary>
/// Start a PC Session: validates the station, Agent availability, tariff and customer,
/// snapshots the tariff and the authoritative start clock, and persists the Session in
/// one idempotent transaction. The database enforces one unsettled Session per station.
/// </summary>
internal sealed class StartSessionHandler(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    ICustomerSessionAccess customers,
    IStationSessionAccess stations,
    ITariffSessionAccess tariffs,
    IAuditWriter audit,
    IGameClock clock)
{
    public Task<Result<SessionResponse>> HandleAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Task.FromResult(Fail<SessionResponse>("sessions.forbidden", "Not allowed to start sessions."));

        var payload = new { command.StationId, command.TariffId, command.CustomerId };
        var scope = $"sessions.start:{command.Actor.OperatorId:D}";

        return ExecuteAsync(command, scope, payload, cancellationToken);
    }

    private async Task<Result<SessionResponse>> ExecuteAsync(
        StartSessionCommand command,
        string scope,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "session.start", command.IdempotencyKey, requestFingerprint.Compute(payload),
                201, SessionMutationPolicy.IdempotencyLease, SessionMutationPolicy.IdempotencyRetention,
                async token =>
                {
                    var station = await stations.FindForSessionStartAsync(command.StationId, token);
                    if (station is null)
                        return Fail<SessionResponse>("sessions.station_not_found", "Station was not found.");
                    if (station.Type != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.pc_only", "This first session flow supports PC stations only.");
                    if (station.Status != StationStatusContract.Available)
                        return Fail<SessionResponse>("sessions.station_unavailable", "Station is not available.");
                    if (!station.AgentOnline)
                        return Fail<SessionResponse>(
                            "sessions.agent_unavailable",
                            "PC Agent is unbound, offline, lacks an active credential, or has no fresh authoritative heartbeat/lease. Session was not started.");

                    var tariff = await tariffs.FindActiveAsync(command.TariffId, token);
                    if (tariff is null)
                        return Fail<SessionResponse>("sessions.tariff_not_found", "Active tariff was not found.");
                    if (tariff.StationType != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.tariff_station_mismatch", "Tariff is not for PC stations.");

                    var activeExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && x.Status == SessionStatus.Active, token);
                    if (activeExists)
                        return Fail<SessionResponse>("sessions.station_has_active_session", "Station already has an active session.");

                    var pendingSettlementExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && x.Status == SessionStatus.PendingSettlement, token);
                    if (pendingSettlementExists)
                        return Fail<SessionResponse>("sessions.station_pending_settlement", "The previous Session must be paid before this station can be started again.");

                    var customer = command.CustomerId is Guid customerId
                        ? await customers.FindActiveAsync(customerId, token)
                        : await customers.CreateWalkInAsync(clock.UtcNow, token);
                    if (customer is null)
                        return Fail<SessionResponse>("sessions.customer_not_found", "Active customer was not found.");
                    if (customer.IsBlocked)
                        return Fail<SessionResponse>("sessions.customer_blocked", "Blocked customers cannot start a Session.");

                    GameClockSnapshot startSnapshot;
                    try
                    {
                        startSnapshot = clock.Capture();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException
                        or DllNotFoundException or EntryPointNotFoundException)
                    {
                        return Fail<SessionResponse>(
                            "sessions.timing_recovery_required",
                            "Authoritative timing source is unavailable. The Session was not started.");
                    }

                    var session = Session.Start(
                        Guid.NewGuid(),
                        station.Id,
                        customer.Id,
                        tariff.Id,
                        tariff.Code,
                        tariff.Name,
                        tariff.HourlyRateToman,
                        command.Actor.OperatorId,
                        startSnapshot.UtcNow,
                        startSnapshot.Timestamp,
                        startSnapshot.TimestampFrequency,
                        startSnapshot.SystemUptimeMilliseconds,
                        startSnapshot.BootIdentifier);
                    await db.Sessions.AddAsync(session, token);

                    var response = ToResponse(session);
                    audit.Append(SessionAuditRecords.Succeeded(
                        clock.UtcNow,
                        command.Actor,
                        "sessions.start",
                        session,
                        command.IdempotencyKey,
                        null,
                        null,
                        JsonSerializer.Serialize(response)));

                    return Result<SessionResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<SessionResponse>("sessions.station_has_active_session", "Station changed concurrently; refresh and try again.");
        }
    }
}
