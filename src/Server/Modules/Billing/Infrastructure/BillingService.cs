using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Application;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Sessions.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Billing.Infrastructure;

public sealed partial class BillingService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    ISessionSettlement sessionSettlement,
    ISessionCheckoutAccess sessionCheckoutAccess,
    IAuditWriter audit,
    IGameClock clock) : IBillingService, ICashReceiptWriter
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public Task<Result<WalletCashReceiptResult>> RecordWalletTopUpAsync(
        RecordWalletTopUpCashReceiptCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var receipt = CashReceipt.RecordWalletTopUp(
                command.CashReceiptId, command.CustomerId, command.AmountAppliedToman, command.AmountReceivedToman,
                command.ReceivedByOperatorId, command.ReceivedAtUtc, command.IdempotencyKey,
                command.CorrelationId, command.Source);
            db.CashReceipts.Add(receipt);
            audit.Append(new AuditRecord(
                command.ReceivedAtUtc, "Operator", command.ReceivedByOperatorId.ToString("D"),
                "billing.wallet_cash_receipt", "CashReceipt", receipt.Id.ToString("D"), null,
                command.CorrelationId, command.Source, "Succeeded", command.IdempotencyKey,
                AfterJson: JsonSerializer.Serialize(new
                {
                    receipt.CustomerId, receipt.Purpose, receipt.AmountAppliedToman,
                    receipt.AmountReceivedToman, receipt.ChangeGivenToman, receipt.ReceivedAtUtc
                })));
            return Task.FromResult(Result<WalletCashReceiptResult>.Success(new WalletCashReceiptResult(
                receipt.Id, receipt.CustomerId, receipt.AmountAppliedToman, receipt.AmountReceivedToman,
                receipt.ChangeGivenToman, receipt.ReceivedAtUtc)));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(Result<WalletCashReceiptResult>.Failure(new Error("wallet.invalid", ex.Message)));
        }
    }

    public async Task<Result<IReadOnlyList<InvoicePreviewResponse>>> ListPendingInvoicesAsync(
        BillingActorContext actor,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.BillingWrite))
            return Fail<IReadOnlyList<InvoicePreviewResponse>>("billing.forbidden", "Not allowed to read pending invoices.");

        var invoices = await db.Invoices.AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.PendingPayment)
            .OrderBy(x => x.IssuedAtUtc)
            .ToArrayAsync(cancellationToken);
        var sessionVersions = await sessionCheckoutAccess.GetSessionVersionsAsync(
            invoices.Select(x => x.SessionId).Distinct().ToArray(),
            cancellationToken);

        var previews = new List<InvoicePreviewResponse>(invoices.Length);
        foreach (var invoice in invoices)
        {
            if (!sessionVersions.TryGetValue(invoice.SessionId, out var sessionVersion))
                return Fail<IReadOnlyList<InvoicePreviewResponse>>(
                    "billing.session_not_found",
                    "Session for a pending invoice was not found.");

            previews.Add(ToPreview(invoice, sessionVersion));
        }

        return Result<IReadOnlyList<InvoicePreviewResponse>>.Success(previews);
    }

    public async Task<Result<CashCheckoutOutcomeResponse>> GetCashCheckoutOutcomeAsync(
        BillingActorContext actor,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.BillingWrite) &&
            !actor.Permissions.Contains(Permissions.BillingCashReverse) &&
            !actor.Permissions.Contains(Permissions.BillingCashTenderCorrect))
            return Fail<CashCheckoutOutcomeResponse>("billing.forbidden", "Not allowed to reconcile cash checkout outcomes.");
        if (invoiceId == Guid.Empty)
            return Fail<CashCheckoutOutcomeResponse>("billing.invoice_not_found", "Invoice was not found.");

        var invoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken);
        if (invoice is null)
            return Fail<CashCheckoutOutcomeResponse>("billing.invoice_not_found", "Invoice was not found.");

        var payment = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        var saleLedger = await db.BillingLedgerEntries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.InvoiceId == invoiceId && x.EntryType == BillingLedgerEntryType.CashSale, cancellationToken);
        var reversalLedger = await db.BillingLedgerEntries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.InvoiceId == invoiceId && x.EntryType == BillingLedgerEntryType.CashSaleReversal, cancellationToken);
        var reversal = await db.CashSaleReversals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        var tenderCorrections = await db.CashTenderCorrections.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId).OrderBy(x => x.Version).ToArrayAsync(cancellationToken);
        return ToCheckoutOutcome(invoice, payment, saleLedger, reversal, reversalLedger, tenderCorrections);
    }

    public async Task<Result<IReadOnlyList<CashCheckoutOutcomeResponse>>> ListRecentCashCheckoutsAsync(
        BillingActorContext actor,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.BillingWrite) &&
            !actor.Permissions.Contains(Permissions.BillingCashReverse) &&
            !actor.Permissions.Contains(Permissions.BillingCashTenderCorrect))
            return Fail<IReadOnlyList<CashCheckoutOutcomeResponse>>(
                "billing.forbidden", "Not allowed to read recent cash checkout outcomes.");

        const int historyLimit = 20;
        var invoices = await db.Invoices.AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.Paid || x.Status == InvoiceStatus.Reversed)
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenBy(x => x.Id)
            .Take(historyLimit)
            .ToArrayAsync(cancellationToken);
        if (invoices.Length == 0)
            return Result<IReadOnlyList<CashCheckoutOutcomeResponse>>.Success(Array.Empty<CashCheckoutOutcomeResponse>());

        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var payments = await db.Payments.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.InvoiceId))
            .ToArrayAsync(cancellationToken);
        var saleEntries = await db.BillingLedgerEntries.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.InvoiceId) && x.EntryType == BillingLedgerEntryType.CashSale)
            .ToArrayAsync(cancellationToken);
        var reversalEntries = await db.BillingLedgerEntries.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.InvoiceId) && x.EntryType == BillingLedgerEntryType.CashSaleReversal)
            .ToArrayAsync(cancellationToken);
        var reversals = await db.CashSaleReversals.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.InvoiceId)).ToArrayAsync(cancellationToken);
        var tenderCorrections = await db.CashTenderCorrections.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.InvoiceId)).OrderBy(x => x.Version).ToArrayAsync(cancellationToken);
        var paymentsByInvoice = payments.ToDictionary(x => x.InvoiceId);
        var salesByInvoice = saleEntries.ToDictionary(x => x.InvoiceId);
        var reversalEntriesByInvoice = reversalEntries.ToDictionary(x => x.InvoiceId);
        var reversalsByInvoice = reversals.ToDictionary(x => x.InvoiceId);
        var tenderCorrectionsByInvoice = tenderCorrections.GroupBy(x => x.InvoiceId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<CashTenderCorrection>)x.OrderBy(y => y.Version).ToArray());

        var outcomes = new List<CashCheckoutOutcomeResponse>(invoices.Length);
        foreach (var invoice in invoices)
        {
            paymentsByInvoice.TryGetValue(invoice.Id, out var payment);
            salesByInvoice.TryGetValue(invoice.Id, out var sale);
            reversalsByInvoice.TryGetValue(invoice.Id, out var reversal);
            reversalEntriesByInvoice.TryGetValue(invoice.Id, out var reversalEntry);
            tenderCorrectionsByInvoice.TryGetValue(invoice.Id, out var invoiceCorrections);
            var outcome = ToCheckoutOutcome(invoice, payment, sale, reversal, reversalEntry,
                invoiceCorrections ?? Array.Empty<CashTenderCorrection>());
            if (!outcome.IsSuccess)
                return Fail<IReadOnlyList<CashCheckoutOutcomeResponse>>(
                    outcome.Error.Code, outcome.Error.Message);
            outcomes.Add(outcome.Value);
        }

        return Result<IReadOnlyList<CashCheckoutOutcomeResponse>>.Success(outcomes);
    }

    private static Result<CashCheckoutOutcomeResponse> ToCheckoutOutcome(
        Invoice invoice, Payment? payment, BillingLedgerEntry? saleLedger,
        CashSaleReversal? reversal, BillingLedgerEntry? reversalLedger,
        IReadOnlyList<CashTenderCorrection> tenderCorrections)
    {
        if (!TryProjectCashTenderHistory(invoice, payment, tenderCorrections,
                out var latestTenderCorrection, out var effectiveReceived, out var effectiveChange))
            return Fail<CashCheckoutOutcomeResponse>("billing.checkout_outcome_inconsistent",
                "Cash tender correction history does not reconcile to the original Payment.");

        if (invoice.Status == InvoiceStatus.PendingPayment)
        {
            if (payment is not null || saleLedger is not null || reversal is not null ||
                reversalLedger is not null || tenderCorrections.Count > 0 || invoice.PaidAtUtc is not null)
                return Fail<CashCheckoutOutcomeResponse>("billing.checkout_outcome_inconsistent",
                    "Pending invoice has persisted payment or reversal data; reconciliation is required.");
        }
        else if (invoice.Status == InvoiceStatus.Paid || invoice.Status == InvoiceStatus.Reversed)
        {
            if (payment is null || saleLedger is null || payment.InvoiceId != invoice.Id ||
                saleLedger.InvoiceId != invoice.Id || saleLedger.PaymentId != payment.Id ||
                saleLedger.EntryType != BillingLedgerEntryType.CashSale ||
                payment.AmountAppliedToman != invoice.AmountToman || saleLedger.AmountToman != invoice.AmountToman ||
                invoice.PaidAtUtc is null || invoice.PaidByOperatorId is null)
                return Fail<CashCheckoutOutcomeResponse>("billing.checkout_outcome_inconsistent",
                    "Paid invoice, payment and original sale ledger do not reconcile.");

            if (invoice.Status == InvoiceStatus.Paid)
            {
                if (reversal is not null || reversalLedger is not null)
                    return Fail<CashCheckoutOutcomeResponse>("billing.checkout_outcome_inconsistent",
                        "Invoice is marked paid but has reversal data; reconciliation is required.");
            }
            else if (reversal is null || reversalLedger is null || reversal.InvoiceId != invoice.Id ||
                reversal.PaymentId != payment.Id || reversal.AmountToman != invoice.AmountToman ||
                reversalLedger.InvoiceId != invoice.Id || reversalLedger.PaymentId != payment.Id ||
                reversalLedger.EntryType != BillingLedgerEntryType.CashSaleReversal ||
                reversalLedger.AmountToman != invoice.AmountToman)
                return Fail<CashCheckoutOutcomeResponse>("billing.checkout_outcome_inconsistent",
                    "Reversed invoice, reversal record and compensating ledger entry do not reconcile.");
        }
        else
            return Fail<CashCheckoutOutcomeResponse>("billing.checkout_outcome_inconsistent",
                "Invoice status is outside the supported checkout lifecycle.");

        return Result<CashCheckoutOutcomeResponse>.Success(new CashCheckoutOutcomeResponse(
            invoice.Id, invoice.SessionId, invoice.StationId, invoice.TariffCodeSnapshot, invoice.TariffNameSnapshot,
            invoice.TariffHourlyRateTomanSnapshot, invoice.ElapsedTicks, invoice.AmountToman,
            (InvoiceStatusContract)invoice.Status, payment?.Id, saleLedger?.Id,
            payment?.AmountReceivedToman, payment?.ChangeGivenToman, invoice.IssuedAtUtc, invoice.PaidAtUtc,
            reversal?.Id, reversal?.ReversedAtUtc, reversal?.Reason, reversalLedger?.Id,
            effectiveReceived, effectiveChange, latestTenderCorrection?.Id, latestTenderCorrection?.Version,
            latestTenderCorrection?.Reason, latestTenderCorrection?.CorrectedAtUtc));
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

                    var sessionVersions = await sessionCheckoutAccess.GetSessionVersionsAsync(
                        new[] { invoice.SessionId },
                        token);
                    if (!sessionVersions.TryGetValue(invoice.SessionId, out var currentSessionVersion))
                        return Fail<CashCheckoutResponse>("billing.session_not_found", "Session for this invoice was not found.");
                    if (currentSessionVersion != command.ExpectedSessionVersion)
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

    public Task<Result<CashSaleReversalResponse>> ReverseCashSaleAsync(
        CashSaleReversalCommand command, CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.BillingCashReverse))
            return Task.FromResult(Fail<CashSaleReversalResponse>("billing.forbidden", "Not allowed to reverse cash sales."));
        var scope = $"billing.cash-reverse:{command.Actor.OperatorId:D}";
        var payload = new { command.InvoiceId, command.PaymentId, command.ConfirmedAmountToman, command.Reason };
        return ReverseCashSaleCoreAsync(command, scope, payload, cancellationToken);
    }

    private async Task<Result<CashSaleReversalResponse>> ReverseCashSaleCoreAsync(
        CashSaleReversalCommand command, string scope, object payload, CancellationToken cancellationToken)
    {
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "billing.cash-sale-reversal", command.IdempotencyKey, requestFingerprint.Compute(payload),
                200, Lease, Retention,
                async token =>
                {
                    var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.Id == command.InvoiceId, token);
                    if (invoice is null)
                        return Fail<CashSaleReversalResponse>("billing.invoice_not_found", "Invoice was not found.");
                    if (invoice.Status == InvoiceStatus.Reversed)
                        return Fail<CashSaleReversalResponse>("billing.sale_already_reversed", "This cash sale has already been reversed.");
                    if (invoice.Status != InvoiceStatus.Paid)
                        return Fail<CashSaleReversalResponse>("billing.invoice_not_paid", "Only a paid invoice can be reversed.");

                    var payment = await db.Payments.SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, token);
                    var saleLedger = await db.BillingLedgerEntries.SingleOrDefaultAsync(
                        x => x.InvoiceId == invoice.Id && x.EntryType == BillingLedgerEntryType.CashSale, token);
                    var existingReversal = await db.CashSaleReversals.SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, token);
                    var existingReversalLedger = await db.BillingLedgerEntries.SingleOrDefaultAsync(
                        x => x.InvoiceId == invoice.Id && x.EntryType == BillingLedgerEntryType.CashSaleReversal, token);
                    if (payment is null || saleLedger is null || existingReversal is not null ||
                        existingReversalLedger is not null || payment.InvoiceId != invoice.Id ||
                        saleLedger.PaymentId != payment.Id || payment.AmountAppliedToman != invoice.AmountToman ||
                        saleLedger.AmountToman != invoice.AmountToman)
                        return Fail<CashSaleReversalResponse>("billing.checkout_outcome_inconsistent",
                            "Invoice, Payment and original sale ledger do not reconcile; no reversal was performed.");

                    if (payment.Method != PaymentMethod.Cash)
                        return Fail<CashSaleReversalResponse>("billing.reversal_method_unsupported",
                            "Only cash payments can be reversed by the V1 cash-sale reversal operation.");

                    if (command.PaymentId != payment.Id || command.ConfirmedAmountToman != invoice.AmountToman)
                        return Fail<CashSaleReversalResponse>("billing.reversal_confirmation_mismatch",
                            "Confirm the original payment and exact invoice amount before reversing.");

                    var reversedAt = clock.UtcNow;
                    CashSaleReversal reversal;
                    try
                    {
                        reversal = CashSaleReversal.Create(Guid.NewGuid(), invoice.Id, payment.Id, invoice.AmountToman,
                            command.Actor.OperatorId, command.Reason, command.Actor.CorrelationId,
                            command.IdempotencyKey, reversedAt);
                        invoice.MarkReversed();
                    }
                    catch (ArgumentException)
                    {
                        return Fail<CashSaleReversalResponse>("billing.reversal_invalid",
                            "A valid reason and reversal record are required.");
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<CashSaleReversalResponse>("billing.sale_already_reversed",
                            "This invoice cannot be reversed from its current state.");
                    }

                    var ledger = BillingLedgerEntry.RecordCashSaleReversal(
                        Guid.NewGuid(), invoice.Id, payment.Id, invoice.AmountToman, command.Actor.OperatorId, reversedAt);
                    await db.CashSaleReversals.AddAsync(reversal, token);
                    await db.BillingLedgerEntries.AddAsync(ledger, token);
                    var response = new CashSaleReversalResponse(reversal.Id, invoice.Id, payment.Id, ledger.Id, invoice.AmountToman, reversedAt);
                    audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "billing.cash_sale_reversal", "CashSaleReversal", reversal.Id.ToString("D"), reversal.Reason,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                        JsonSerializer.Serialize(new { InvoiceId = invoice.Id, Status = InvoiceStatus.Paid,
                            PaymentId = payment.Id, AmountAppliedToman = payment.AmountAppliedToman, SaleLedgerEntryId = saleLedger.Id }),
                        JsonSerializer.Serialize(response)));
                    audit.Append(new AuditRecord(clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "billing.cash_sale_reversal_ledger_entry", "BillingLedgerEntry", ledger.Id.ToString("D"), reversal.Reason,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded", command.IdempotencyKey,
                        null, JsonSerializer.Serialize(ledger)));
                    return Result<CashSaleReversalResponse>.Success(response);
                }, cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<CashSaleReversalResponse>("billing.reversal_conflict",
                "This invoice was reversed by another operation; refresh its current status.");
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
