using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Wallet.Contracts;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Shared transactional finalizer for prepaid Session invoices. Operator end and manual
/// timing recovery use the same reservation, ledger, invoice and Session state transition.
/// </summary>
public sealed class SessionPrepaidSettlementService(
    IWalletSessionFunding walletFunding,
    IBillingInvoiceWriter invoices,
    IAuditWriter audit,
    IGameClock clock)
{
public async Task<Result<InvoicePreviewResponse>> CompleteAsync(
        Session session,
        InvoicePreviewResponse invoice,
        SessionActorContext actor,
        DateTimeOffset settledAtUtc,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var reservation = await walletFunding.FindActiveReservationAsync(session.Id, cancellationToken);
        if (reservation is null)
            return Result<InvoicePreviewResponse>.Success(invoice);
        if (reservation.CustomerId != session.CustomerId ||
            invoice.AmountToman > reservation.ReservedAmountToman)
            return SessionService.Fail<InvoicePreviewResponse>("sessions.prepaid_budget_exceeded",
                "The Session charge exceeds its valid prepaid reservation; Session settlement was rolled back.");

        var ledgerActorType = actor.ActorType;
        var ledgerActorId = actor.ActorId;
        var walletResult = await walletFunding.SettleSessionAsync(
            session.CustomerId, session.Id, invoice.AmountToman, ledgerActorId, ledgerActorType,
            actor.CorrelationId, actor.Source, idempotencyKey, cancellationToken);
        if (!walletResult.IsSuccess)
        {
            var errorCode = walletResult.Error.Code == "wallet.prepaid_budget_exceeded"
                ? "sessions.prepaid_budget_exceeded"
                : walletResult.Error.Code;
            return SessionService.Fail<InvoicePreviewResponse>(errorCode, walletResult.Error.Message);
        }

        var invoicePaid = await invoices.MarkPrepaidPaidAsync(
            invoice.InvoiceId, session.Version, session.CustomerId, settledAtUtc,
            actor.CorrelationId, actor.Source, idempotencyKey, cancellationToken);
        if (!invoicePaid.IsSuccess)
            return SessionService.Fail<InvoicePreviewResponse>(invoicePaid.Error.Code, invoicePaid.Error.Message);

        var beforePaid = SessionService.ToResponse(session);
        try
        {
            session.MarkPaid(Guid.Empty, settledAtUtc, session.CustomerId);
        }
        catch (InvalidOperationException)
        {
            return SessionService.Fail<InvoicePreviewResponse>("sessions.invalid_state",
                "Prepaid Session could not enter its paid state; all financial changes were rolled back.");
        }

        audit.Append(new AuditRecord(
            clock.UtcNow,
            ledgerActorType,
            ledgerActorId.ToString("D"),
            "sessions.prepaid_settled",
            "Session",
            session.Id.ToString("D"),
            null,
            actor.CorrelationId,
            actor.Source,
            "Succeeded",
            idempotencyKey,
            JsonSerializer.Serialize(beforePaid),
            JsonSerializer.Serialize(SessionService.ToResponse(session))));

        return Result<InvoicePreviewResponse>.Success(invoicePaid.Value with
        {
            SessionVersion = session.Version,
            Status = InvoiceStatusContract.Paid,
            PaidAtUtc = settledAtUtc
        });
    }
}
