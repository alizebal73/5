using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Wallet.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Infrastructure.Transactions;

/// <summary>
/// Finds funded Sessions whose reserved budget is exhausted and closes them through the
/// same idempotent Session end use case used by the operator. Server time is authoritative.
/// </summary>
public sealed class PrepaidSessionExpiryService(
    GameNetDbContext db,
    IWalletSessionFunding walletFunding,
    ISessionService sessionService,
    IGameClock clock) : IPrepaidSessionExpiryService
{
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions.AsNoTracking()
            .Where(x => x.Status == SessionStatus.Active)
            .OrderBy(x => x.StartedAtUtc)
            .ToArrayAsync(cancellationToken);
        if (sessions.Length == 0)
            return 0;

        GameClockSnapshot snapshot;
        try { snapshot = clock.Capture(); }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }

        var expiredCount = 0;
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reservation = await walletFunding.FindActiveReservationAsync(session.Id, cancellationToken);
            if (reservation is null)
                continue;

            var maximumElapsedTicks = PrepaidSessionBudget.CalculateMaximumElapsedTicks(
                reservation.ReservedAmountToman, session.TariffHourlyRateTomanSnapshot);
            if (!MonotonicElapsedTime.TryCalculateElapsedTicks(
                    session.StartedMonotonicTimestamp,
                    session.StartedMonotonicFrequency,
                    session.StartedSystemUptimeMilliseconds,
                    session.StartedBootIdentifier,
                    snapshot,
                    out var elapsedTicks) ||
                elapsedTicks < maximumElapsedTicks)
                continue;

            // A stable per-Session/version key makes periodic retries safe after timeouts or restarts.
            var key = $"prepaid-expiry-{session.Id:N}-{session.Version}";
            var actor = new SessionActorContext(
                session.StartedByOperatorId,
                $"prepaid-expiry:{session.Id:D}",
                "PrepaidExpiryWorker",
                new HashSet<string>(StringComparer.Ordinal) { Permissions.SessionsOperate });
            var result = await sessionService.EndAsync(
                new EndSessionCommand(session.Id, session.Version, key, actor),
                cancellationToken);
            if (result.IsSuccess && result.Value.Status == InvoiceStatusContract.Paid)
                expiredCount++;
        }

        return expiredCount;
    }
}
