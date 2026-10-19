using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Builds the Billing contract command that turns an ended Session into a pending invoice.
/// Used by both the normal End path and the audited timing-recovery path so the
/// snapshot fields handed to Billing can never drift between them.
/// </summary>
internal static class PendingInvoiceRequests
{
    public static CreatePendingInvoiceCommand ForEndedSession(
        Session session,
        SessionActorContext actor,
        DateTimeOffset endedAtUtc,
        string idempotencyKey) =>
        new(
            Guid.NewGuid(),
            session.Id,
            session.StationId,
            session.CustomerId,
            session.TariffId,
            session.TariffCodeSnapshot,
            session.TariffNameSnapshot,
            session.TariffHourlyRateTomanSnapshot,
            session.ElapsedTicks!.Value,
            session.Version,
            actor.OperatorId,
            endedAtUtc,
            actor.CorrelationId,
            actor.Source,
            idempotencyKey);
}
