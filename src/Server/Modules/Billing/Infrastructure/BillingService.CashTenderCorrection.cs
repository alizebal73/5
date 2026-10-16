using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Application;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Billing.Infrastructure;

public sealed partial class BillingService
{
    public Task<Result<CashTenderCorrectionResponse>> CorrectCashTenderAsync(
        CashTenderCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.BillingCashTenderCorrect))
            return Task.FromResult(Fail<CashTenderCorrectionResponse>(
                "billing.forbidden", "Not allowed to correct recorded cash tender facts."));

        if (!command.Confirmed)
            return Task.FromResult(Fail<CashTenderCorrectionResponse>(
                "billing.tender_correction_invalid",
                "Explicit operator confirmation is required before correcting recorded cash tender facts."));

        var scope = $"billing.cash-tender-correct:{command.Actor.OperatorId:D}";
        var payload = new
        {
            command.InvoiceId,
            command.PaymentId,
            command.ExpectedVersion,
            command.CorrectedAmountReceivedToman,
            command.Reason,
            command.Confirmed
        };
        return CorrectCashTenderCoreAsync(command, scope, payload, cancellationToken);
    }

    private async Task<Result<CashTenderCorrectionResponse>> CorrectCashTenderCoreAsync(
        CashTenderCorrectionCommand command,
        string scope,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "billing.cash-tender-correction", command.IdempotencyKey,
                requestFingerprint.Compute(payload), 200, Lease, Retention,
                async token =>
                {
                    if (command.ExpectedVersion < 0)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_invalid", "Expected correction version cannot be negative.");

                    var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.Id == command.InvoiceId, token);
                    if (invoice is null)
                        return Fail<CashTenderCorrectionResponse>("billing.invoice_not_found", "Invoice was not found.");
                    if (invoice.Status == InvoiceStatus.Reversed)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_reversal_forbidden",
                            "Cash tender facts cannot be corrected after a sale has been reversed.");
                    if (invoice.Status != InvoiceStatus.Paid)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.invoice_not_paid", "Only a paid cash invoice can have its tender facts corrected.");

                    var payment = await db.Payments.SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, token);
                    var saleLedger = await db.BillingLedgerEntries.SingleOrDefaultAsync(
                        x => x.InvoiceId == invoice.Id && x.EntryType == BillingLedgerEntryType.CashSale, token);
                    var reversal = await db.CashSaleReversals.SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, token);
                    var reversalLedger = await db.BillingLedgerEntries.SingleOrDefaultAsync(
                        x => x.InvoiceId == invoice.Id && x.EntryType == BillingLedgerEntryType.CashSaleReversal, token);
                    if (reversal is not null || reversalLedger is not null)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_reversal_forbidden",
                            "Cash tender facts cannot be corrected after a reversal record exists.");
                    if (payment is null || saleLedger is null ||
                        payment.InvoiceId != invoice.Id || payment.Method != PaymentMethod.Cash ||
                        payment.AmountAppliedToman != invoice.AmountToman ||
                        payment.AmountReceivedToman < payment.AmountAppliedToman ||
                        payment.ChangeGivenToman != payment.AmountReceivedToman - payment.AmountAppliedToman ||
                        saleLedger.InvoiceId != invoice.Id || saleLedger.PaymentId != payment.Id ||
                        saleLedger.EntryType != BillingLedgerEntryType.CashSale ||
                        saleLedger.AmountToman != invoice.AmountToman)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.checkout_outcome_inconsistent",
                            "Invoice, original Payment and CashSale ledger do not reconcile.");

                    if (command.PaymentId != payment.Id)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_confirmation_mismatch",
                            "The selected payment does not belong to this invoice.");

                    var corrections = await db.CashTenderCorrections.AsNoTracking()
                        .Where(x => x.InvoiceId == invoice.Id)
                        .OrderBy(x => x.Version)
                        .ToArrayAsync(token);
                    if (!TryProjectCashTenderHistory(
                            invoice, payment, corrections,
                            out var currentCorrection, out var currentReceived, out var currentChange) ||
                        currentReceived is not long previousReceived ||
                        currentChange is not long previousChange)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.checkout_outcome_inconsistent",
                            "Existing cash tender correction history does not reconcile.");

                    var currentVersion = currentCorrection?.Version ?? 0;
                    if (command.ExpectedVersion != currentVersion)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_version_conflict",
                            "Cash tender facts changed; refresh and review the current correction version.");

                    if (command.CorrectedAmountReceivedToman < payment.AmountAppliedToman)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_invalid",
                            "Corrected cash received must be at least the unchanged invoice amount.");
                    if (command.CorrectedAmountReceivedToman == previousReceived)
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_no_change",
                            "The corrected cash received amount must differ from the current recorded amount.");

                    var correctedAt = clock.UtcNow;
                    CashTenderCorrection correction;
                    try
                    {
                        correction = CashTenderCorrection.Create(
                            Guid.NewGuid(), invoice.Id, payment.Id, checked(currentVersion + 1),
                            previousReceived, previousChange, payment.AmountAppliedToman,
                            command.CorrectedAmountReceivedToman, command.Actor.OperatorId,
                            command.Reason, command.Actor.CorrelationId, command.IdempotencyKey, correctedAt);
                    }
                    catch (ArgumentException)
                    {
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_invalid",
                            "The reason, version or corrected cash amount is invalid.");
                    }
                    catch (OverflowException)
                    {
                        return Fail<CashTenderCorrectionResponse>(
                            "billing.tender_correction_invalid",
                            "Correction version or amount arithmetic overflowed.");
                    }

                    var response = new CashTenderCorrectionResponse(
                        correction.Id, correction.InvoiceId, correction.PaymentId, correction.Version,
                        correction.PreviousAmountReceivedToman, correction.PreviousChangeGivenToman,
                        correction.CorrectedAmountReceivedToman, correction.CorrectedChangeGivenToman,
                        correction.Reason, correction.CorrectedAtUtc);
                    await db.CashTenderCorrections.AddAsync(correction, token);
                    audit.Append(new AuditRecord(
                        clock.UtcNow,
                        "Operator",
                        command.Actor.OperatorId.ToString("D"),
                        "billing.cash_tender_correction",
                        "CashTenderCorrection",
                        correction.Id.ToString("D"),
                        correction.Reason,
                        command.Actor.CorrelationId,
                        command.Actor.Source,
                        "Succeeded",
                        command.IdempotencyKey,
                        JsonSerializer.Serialize(new
                        {
                            InvoiceId = invoice.Id,
                            PaymentId = payment.Id,
                            InvoiceAmountToman = invoice.AmountToman,
                            Version = currentVersion,
                            AmountReceivedToman = previousReceived,
                            ChangeGivenToman = previousChange
                        }),
                        JsonSerializer.Serialize(response)));
                    return Result<CashTenderCorrectionResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<CashTenderCorrectionResponse>(
                "billing.tender_correction_conflict",
                "Another correction changed this invoice's cash tender facts; refresh before retrying.");
        }
    }

    private static bool TryProjectCashTenderHistory(
        Invoice invoice,
        Payment? payment,
        IReadOnlyList<CashTenderCorrection> corrections,
        out CashTenderCorrection? latest,
        out long? effectiveAmountReceivedToman,
        out long? effectiveChangeGivenToman)
    {
        latest = null;
        effectiveAmountReceivedToman = payment?.AmountReceivedToman;
        effectiveChangeGivenToman = payment?.ChangeGivenToman;
        if (payment is null)
            return corrections.Count == 0;

        if (payment.InvoiceId != invoice.Id || payment.AmountAppliedToman != invoice.AmountToman ||
            payment.AmountReceivedToman < payment.AmountAppliedToman ||
            payment.ChangeGivenToman != payment.AmountReceivedToman - payment.AmountAppliedToman)
            return false;

        var amountReceived = payment.AmountReceivedToman;
        var changeGiven = payment.ChangeGivenToman;
        var version = 0;
        foreach (var correction in corrections.OrderBy(x => x.Version))
        {
            if (correction.InvoiceId != invoice.Id || correction.PaymentId != payment.Id ||
                correction.Version != version + 1 ||
                correction.PreviousAmountReceivedToman != amountReceived ||
                correction.PreviousChangeGivenToman != changeGiven ||
                correction.CorrectedAmountReceivedToman < payment.AmountAppliedToman ||
                correction.CorrectedChangeGivenToman != correction.CorrectedAmountReceivedToman - payment.AmountAppliedToman ||
                correction.CorrectedByOperatorId == Guid.Empty ||
                string.IsNullOrWhiteSpace(correction.Reason) || correction.Reason.Trim().Length is < 3 or > 256 ||
                correction.Reason.Trim().Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(correction.CorrelationId) ||
                string.IsNullOrWhiteSpace(correction.IdempotencyKey))
                return false;

            version = correction.Version;
            amountReceived = correction.CorrectedAmountReceivedToman;
            changeGiven = correction.CorrectedChangeGivenToman;
            latest = correction;
        }

        effectiveAmountReceivedToman = amountReceived;
        effectiveChangeGivenToman = changeGiven;
        return true;
    }
}
