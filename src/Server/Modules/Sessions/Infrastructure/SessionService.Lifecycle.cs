using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

public sealed partial class SessionService
{
        public async Task<Result<SessionResponse>> PauseAsync(
            PauseSessionCommand command,
            CancellationToken cancellationToken)
        {
            if (command.Actor.ActorType != "Operator" ||
                !command.Actor.Permissions.Contains(Permissions.SessionsOperate))
                return Fail<SessionResponse>("sessions.forbidden", "Not allowed to pause sessions.");
    
            try
            {
                return await mutationPipeline.ExecuteAsync(
                    $"sessions.pause:{command.Actor.ActorId:D}", "session.pause",
                    command.IdempotencyKey,
                    requestFingerprint.Compute(new { command.SessionId, command.ExpectedVersion }),
                    200, Lease, Retention,
                    async token =>
                    {
                        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                        if (session is null)
                            return Fail<SessionResponse>("sessions.not_found", "Session was not found.");
                        if (session.Version != command.ExpectedVersion)
                            return Fail<SessionResponse>("sessions.version_conflict", "Session changed; refresh before pausing it.");
                        if (session.Status != SessionStatus.Active)
                            return Fail<SessionResponse>("sessions.invalid_state", "Only an active Session can be paused.");
    
                        if (session.CurrentSegmentStartedMonotonicTimestamp is not long segmentTimestamp ||
                            session.CurrentSegmentStartedMonotonicFrequency is not long segmentFrequency ||
                            session.CurrentSegmentStartedSystemUptimeMilliseconds is not long segmentUptime ||
                            session.CurrentSegmentStartedBootIdentifier is not Guid segmentBoot)
                            return Fail<SessionResponse>("sessions.timing_recovery_required", "The active timing baseline is missing; no state changed.");
    
                        GameClockSnapshot snapshot;
                        try { snapshot = clock.Capture(); }
                        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
                        {
                            return Fail<SessionResponse>("sessions.timing_recovery_required", "Authoritative timing source is unavailable; no state changed.");
                        }
    
                        if (!MonotonicElapsedTime.TryCalculateElapsedTicks(
                                segmentTimestamp, segmentFrequency, segmentUptime, segmentBoot, snapshot,
                                out var segmentTicks))
                            return Fail<SessionResponse>("sessions.timing_recovery_required", "Monotonic continuity cannot be proven. Recover timing or leave the Session unchanged.");
    
                        var before = new { Session = ToResponse(session), AccumulatedElapsedTicks = session.AccumulatedElapsedTicks };
                        try { session.Pause(command.Actor.ActorId, segmentTicks); }
                        catch (InvalidOperationException) { return Fail<SessionResponse>("sessions.invalid_state", "Session could not be paused from its current state."); }
                        catch (ArgumentOutOfRangeException) { return Fail<SessionResponse>("sessions.timing_recovery_invalid", "Active segment duration is invalid."); }
                        catch (OverflowException) { return Fail<SessionResponse>("sessions.amount_overflow", "Accumulated billable Session duration exceeded the supported range."); }
    
                        var response = ToResponse(session);
                        audit.Append(new AuditRecord(
                            snapshot.UtcNow, "Operator", command.Actor.ActorId.ToString("D"), "sessions.pause",
                            "Session", session.Id.ToString("D"), null, command.Actor.CorrelationId,
                            command.Actor.Source, "Succeeded", command.IdempotencyKey,
                            JsonSerializer.Serialize(before),
                            JsonSerializer.Serialize(new { Session = response, AccumulatedElapsedTicks = session.AccumulatedElapsedTicks, PausedSegmentElapsedTicks = segmentTicks })));
                        return Result<SessionResponse>.Success(response);
                    },
                    cancellationToken);
            }
            catch (PersistenceConflictException)
            {
                return Fail<SessionResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
            }
        }

        public async Task<Result<SessionResponse>> ResumeAsync(
            ResumeSessionCommand command,
            CancellationToken cancellationToken)
        {
            if (command.Actor.ActorType != "Operator" ||
                !command.Actor.Permissions.Contains(Permissions.SessionsOperate))
                return Fail<SessionResponse>("sessions.forbidden", "Not allowed to resume sessions.");
    
            try
            {
                return await mutationPipeline.ExecuteAsync(
                    $"sessions.resume:{command.Actor.ActorId:D}", "session.resume",
                    command.IdempotencyKey,
                    requestFingerprint.Compute(new { command.SessionId, command.ExpectedVersion }),
                    200, Lease, Retention,
                    async token =>
                    {
                        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                        if (session is null)
                            return Fail<SessionResponse>("sessions.not_found", "Session was not found.");
                        if (session.Version != command.ExpectedVersion)
                            return Fail<SessionResponse>("sessions.version_conflict", "Session changed; refresh before resuming it.");
                        if (session.Status != SessionStatus.Paused)
                            return Fail<SessionResponse>("sessions.invalid_state", "Only a paused Session can be resumed.");
    
                        GameClockSnapshot snapshot;
                        try { snapshot = clock.Capture(); }
                        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
                        {
                            return Fail<SessionResponse>("sessions.timing_recovery_required", "Authoritative timing source is unavailable; the Session remains paused.");
                        }
    
                        var before = new { Session = ToResponse(session), AccumulatedElapsedTicks = session.AccumulatedElapsedTicks };
                        try { session.Resume(command.Actor.ActorId, snapshot); }
                        catch (InvalidOperationException) { return Fail<SessionResponse>("sessions.invalid_state", "Session could not be resumed from its current state."); }
                        catch (ArgumentException) { return Fail<SessionResponse>("sessions.timing_recovery_required", "Clock snapshot is invalid; the Session remains paused."); }
    
                        var response = ToResponse(session);
                        audit.Append(new AuditRecord(
                            snapshot.UtcNow, "Operator", command.Actor.ActorId.ToString("D"), "sessions.resume",
                            "Session", session.Id.ToString("D"), null, command.Actor.CorrelationId,
                            command.Actor.Source, "Succeeded", command.IdempotencyKey,
                            JsonSerializer.Serialize(before),
                            JsonSerializer.Serialize(new { Session = response, AccumulatedElapsedTicks = session.AccumulatedElapsedTicks, NewSegmentBootIdentifier = snapshot.BootIdentifier })));
                        return Result<SessionResponse>.Success(response);
                    },
                    cancellationToken);
            }
            catch (PersistenceConflictException)
            {
                return Fail<SessionResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
            }
        }
}
