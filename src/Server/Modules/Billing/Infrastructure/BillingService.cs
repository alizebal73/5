using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Application;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Sessions.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Billing.Infrastructure;

public sealed class BillingService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    ISessionSettlement sessionSettlement,
    IAuditWriter audit,
    IGameClock clock) : IBillingService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<Result<IReadOnlyList<InvoicePreviewResponse>>> ListPendingInvoicesAsync(
        BillingActorContext actor,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.BillingWrite))
            return Fail<IReadOnlyList<InvoicePreviewResponse>>("billing.forbidden", "Not allowed to read pending invoices.");

        var rows = await db.Invoices.AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.PendingPayment)
            .Join(db.Sessions.AsNoTracking(),
                invoice => invoice.SessionId,
                session => session.Id,
                (invoice, session) => new { Invoice = invoice, SessionVersion = session.Version })
            .OrderBy(x => x.Invoice.IssuedAtUtc)
            .ToArrayAsync(cancellationToken);

        return Result<IReadOnlyList<InvoicePreviewResponse>>.Success(
            rows.Select(x => ToPreview(x.Invoice, x.SessionVersion)).ToArray());
    }

    public Task<Result<CashCheckoutResponse>> CheckoutCashAsync(
        CashCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.BillingWrite))
            return Task.FromResult(Fail<CashCheckoutResponse>("billing.forbidden", "Not allowed to accept cash payments."));

        var scope = $"billing.cash-checkout:{command.Actor.OperatorId:D}";
        var payload = new
        {
            command.InvoiceId,
            command.AmountReceivedToman,
            command.ExpectedSessionVersion
        };

        return CheckoutCoreAsync(command, scope, payload, cancellationToken);
    }

    private async Task<Result<CashCheckoutResponse>> CheckoutCoreAsync(
        CashCheckoutCommand command,
        string scope,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope,
                "billing.cash-checkout",
                command.IdempotencyKey,
                requestFingerprint.Compute(payload),
                200,
                Lease,
                Retention,
                async token =>
                {
                    var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.Id == command.InvoiceId, token);
                    if (invoice is null)
                        return Fail<CashCheckoutResponse>("billing.invoice_not_found", "Invoice was not found.");
                    if (invoice.Status != InvoiceStatus.PendingPayment)
                        return Fail<CashCheckoutResponse>("billing.invoice_not_pending", "Invoice is not awaiting payment.");

                    var currentSessionVersion = await db.Sessions.AsNoTracking()
                        .Where(x => x.Id == invoice.SessionId)
                        .Select(x => (int?)x.Version)
                        .SingleOrDefaultAsync(token);
                    if (currentSessionVersion is null)
                        return Fail<CashCheckoutResponse>("billing.session_not_found", "Session for this invoice was not found.");
                    if (currentSessionVersion.Value != command.ExpectedSessionVersion)
                        return Fail<CashCheckoutResponse>("billing.session_version_conflict", "Session changed; refresh before checkout.");

                    var paidAt = clock.UtcNow;
                    Payment payment;
                    try
                    {
                        payment = Payment.CreateCash(
                            Guid.NewGuid(),
                            invoice.Id,
                            invoice.AmountToman,
                            command.AmountReceivedToman,
                            command.Actor.OperatorId,
                            paidAt);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return Fail<CashCheckoutResponse>("billing.cash_insufficient", "Cash received must cover the full invoice amount.");
                    }

                    var settledSession = await sessionSettlement.MarkPaidAsync(
                        new SettleSessionCommand(
                            invoice.SessionId,
                            command.ExpectedSessionVersion,
                            command.Actor.OperatorId,
                            paidAt,
                            command.Actor.CorrelationId,
                            command.Actor.Source,
                            command.IdempotencyKey),
                        token);
                    if (!settledSession.IsSuccess)
                    {
                        var code = settledSession.Error.Code switch
                        {
                            "billing.session_not_found" => "billing.session_not_found",
                            "billing.session_version_conflict" => "billing.session_version_conflict",
                            "billing.session_not_ready" => "billing.session_not_ready",
                            _ => "billing.checkout_conflict"
                        };
                        return Fail<CashCheckoutResponse>(code, settledSession.Error.Message);
                    }

                    var before = ToPreview(invoice, command.ExpectedSessionVersion);
                    try
                    {
                        invoice.MarkPaid(command.Actor.OperatorId, paidAt);
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<CashCheckoutResponse>("billing.invoice_not_pending", "Invoice is no longer awaiting payment.");
                    }

                    var ledger = BillingLedgerEntry.RecordCashSale(
                        Guid.NewGuid(),
                        invoice.Id,
                        payment.Id,
                        invoice.AmountToman,
                        command.Actor.OperatorId,
                        paidAt);

                    await db.Payments.AddAsync(payment, token);
                    await db.BillingLedgerEntries.AddAsync(ledger, token);

                    var response = new CashCheckoutResponse(
                        invoice.Id,
                        payment.Id,
                        ledger.Id,
                        invoice.AmountToman,
                        payment.AmountReceivedToman,
                        payment.ChangeGivenToman,
                        paidAt);

                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        "Operator",
                        command.Actor.OperatorId.ToString("D"),
                        "billing.cash_checkout",
                        "Invoice",
                        invoice.Id.ToString("D"),
                        null,
                        command.Actor.CorrelationId,
                        command.Actor.Source,
                        "Succeeded",
                        command.IdempotencyKey,
                        JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(response)));
                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        "Operator",
                        command.Actor.OperatorId.ToString("D"),
                        "billing.cash_ledger_entry",
                        "BillingLedgerEntry",
                        ledger.Id.ToString("D"),
                        null,
                        command.Actor.CorrelationId,
                        command.Actor.Source,
                        "Succeeded",
                        command.IdempotencyKey,
                        null,
                        JsonSerializer.Serialize(ledger)));

                    return Result<CashCheckoutResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<CashCheckoutResponse>("billing.checkout_conflict", "Invoice was settled by another operation; refresh and try again.");
        }
    }

    private static InvoicePreviewResponse ToPreview(Invoice invoice, int sessionVersion) =>
        new(invoice.Id, invoice.SessionId, sessionVersion, invoice.StationId, invoice.CustomerId, invoice.TariffId,
            invoice.TariffCodeSnapshot, invoice.TariffNameSnapshot, invoice.TariffHourlyRateTomanSnapshot,
            invoice.ElapsedTicks, invoice.AmountToman, (InvoiceStatusContract)invoice.Status,
            invoice.IssuedAtUtc, invoice.PaidAtUtc);

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));
}
