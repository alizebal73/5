using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Operator adapter for correcting recorded cash-tender facts without changing the invoice charge.
/// </summary>
public sealed partial class SessionWorkflowViewModel
{
    private string correctedCashReceivedText = string.Empty;
    private string cashTenderCorrectionReasonText = string.Empty;
    private bool confirmCashTenderCorrection;
    private PendingCashTenderCorrection? unresolvedCashTenderCorrection;
    private bool cashTenderCorrectionRequiresReview;
    private string? cashTenderCorrectionRefreshMessage;

    public ICommand CorrectCashTenderCommand { get; }

    public string CorrectedCashReceivedText
    {
        get => correctedCashReceivedText;
        set
        {
            if (SetField(ref correctedCashReceivedText, value ?? string.Empty))
            {
                ConfirmCashTenderCorrection = false;
                OnPropertyChanged(nameof(CashTenderCorrectionSummaryText));
                RaiseCommands();
            }
        }
    }

    public string CashTenderCorrectionReasonText
    {
        get => cashTenderCorrectionReasonText;
        set
        {
            if (SetField(ref cashTenderCorrectionReasonText, value ?? string.Empty))
            {
                ConfirmCashTenderCorrection = false;
                RaiseCommands();
            }
        }
    }

    public bool ConfirmCashTenderCorrection
    {
        get => confirmCashTenderCorrection;
        set
        {
            if (SetField(ref confirmCashTenderCorrection, value))
                RaiseCommands();
        }
    }

    public string CashTenderCorrectionSummaryText
    {
        get
        {
            if (SelectedRecentCashCheckout is not { } selected)
                return Text(
                    "برای اصلاح، یک پرداخت نقدی ثبت‌شده را انتخاب کنید.",
                    "Select a recorded cash payment to review a tender correction.");

            var outcome = selected.Outcome;
            if (outcome.Status == InvoiceStatusContract.Reversed || outcome.ReversalId is not null)
                return Text(
                    "پس از برگشت فروش، اصلاح سوابق وجه دریافتی مجاز نیست.",
                    "Cash tender facts cannot be corrected after the sale has been reversed.");

            if (!TryReadTenderFacts(outcome, out var received, out var change, out var version))
                return Text(
                    "سوابق پرداخت یا اصلاح قبلی با هم تطبیق ندارند؛ عملیات مسدود است.",
                    "Payment and correction history do not reconcile; correction is blocked.");

            var correctionText = version == 0
                ? Text("اصلاح قبلی ثبت نشده است.", "No previous tender correction is recorded.")
                : Text(
                    $"آخرین اصلاح ثبت‌شده نسخهٔ {version} است.",
                    $"Latest recorded correction version is {version}.");

            var reviewText = cashTenderCorrectionRequiresReview
                ? Text(" نتیجهٔ درخواست قبلی ثبت نشدن اصلاح را نشان داد؛ مبلغ و علت را بازبینی و دوباره تأیید کنید.",
                    " The previous request was not committed; review the amount and reason and explicitly confirm again.")
                : string.Empty;

            return Text(
                $"مبلغ صورتحساب: {FormatToman(outcome.AmountToman)} تومان. سوابق اولیهٔ دریافتی/باقی‌مانده: {FormatToman(outcome.AmountReceivedToman ?? 0)} / {FormatToman(outcome.ChangeGivenToman ?? 0)} تومان. مقادیر مؤثر فعلی: {FormatToman(received)} / {FormatToman(change)} تومان. این عملیات فقط ثبت واقعیت وجه دریافتی را اصلاح می‌کند؛ مبلغ صورتحساب، ردیف فروش و انتقال فیزیکی وجه را تغییر نمی‌دهد. {correctionText}{reviewText}",
                $"Invoice amount: {FormatToman(outcome.AmountToman)} Toman. Original received/change facts: {FormatToman(outcome.AmountReceivedToman ?? 0)} / {FormatToman(outcome.ChangeGivenToman ?? 0)} Toman. Current effective facts: {FormatToman(received)} / {FormatToman(change)} Toman. This only corrects recorded tender facts; it does not change the invoice amount, sale ledger, or physically move cash. {correctionText}{reviewText}");
        }
    }

    private async Task CorrectCashTenderAsync()
    {
        if (SelectedRecentCashCheckout is not { } selected)
            return;

        if (!CanCorrectCashTender)
        {
            ErrorMessage = Text("مجوز مستقل اصلاح سوابق وجه دریافتی را ندارید.", "You do not have the dedicated cash tender correction permission.");
            return;
        }
        if (unresolvedCashTenderCorrection is not null)
        {
            ErrorMessage = Text(
                "نتیجهٔ اصلاح قبلی هنوز قابل تأیید نیست؛ ابتدا وضعیت آن را با سرور تطبیق دهید.",
                "The previous correction outcome is unverified. Reconcile with the Server before retrying.");
            return;
        }

        var outcome = selected.Outcome;
        if (!TryReadTenderFacts(outcome, out var previousReceived, out var previousChange, out var currentVersion) ||
            outcome.Status != InvoiceStatusContract.Paid || outcome.ReversalId is not null ||
            outcome.PaymentId is not Guid paymentId)
        {
            ErrorMessage = Text("فقط پرداخت نقدی پرداخت‌شده و تطبیق‌یافته، قبل از برگشت فروش، قابل اصلاح است.",
                "Only a reconciled paid cash payment before sale reversal can be corrected.");
            return;
        }
        if (!TryParseToman(CorrectedCashReceivedText, out var correctedReceived) ||
            correctedReceived < outcome.AmountToman || correctedReceived == previousReceived)
        {
            ErrorMessage = Text(
                "وجه دریافتی اصلاح‌شده باید عدد صحیح، متفاوت از مقدار فعلی و حداقل برابر مبلغ صورتحساب باشد.",
                "Corrected cash received must be a whole number, differ from the current effective value, and be at least the invoice amount.");
            return;
        }
        if (!IsValidCashTenderCorrectionReason(CashTenderCorrectionReasonText))
        {
            ErrorMessage = Text("علت اصلاح باید بین ۳ تا ۲۵۶ نویسهٔ قابل‌چاپ باشد.",
                "The correction reason must contain 3–256 printable characters.");
            return;
        }
        if (!ConfirmCashTenderCorrection)
        {
            ErrorMessage = Text("برای اصلاح سوابق وجه دریافتی، تأیید آگاهانهٔ اپراتور لازم است.",
                "Explicit operator confirmation is required before correcting recorded cash tender.");
            return;
        }

        var reason = CashTenderCorrectionReasonText.Trim();
        var fingerprint = $"{outcome.InvoiceId:D}|{paymentId:D}|{currentVersion}|{correctedReceived}|{reason}";
        var key = GetKey("billing.cash-tender-correction", fingerprint);
        unresolvedCashTenderCorrection = new PendingCashTenderCorrection(
            outcome.InvoiceId, paymentId, currentVersion, previousReceived, previousChange, correctedReceived, reason, key);
        ConfirmCashTenderCorrection = false;
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var response = await api.CorrectCashTenderAsync(
                outcome.InvoiceId,
                new CashTenderCorrectionRequest(paymentId, currentVersion, correctedReceived, reason, Confirmed: true),
                key);

            if (response.InvoiceId != outcome.InvoiceId ||
                response.PaymentId != paymentId ||
                response.CorrectionId == Guid.Empty ||
                response.Version != currentVersion + 1 ||
                response.PreviousAmountReceivedToman != previousReceived ||
                response.PreviousChangeGivenToman != previousChange ||
                response.CorrectedAmountReceivedToman != correctedReceived ||
                response.CorrectedChangeGivenToman != correctedReceived - outcome.AmountToman ||
                !string.Equals(response.Reason, reason, StringComparison.Ordinal))
            {
                await RefreshAsync();
                return;
            }

            unresolvedCashTenderCorrection = null;
            pendingKeys.Remove("billing.cash-tender-correction");
            cashTenderCorrectionRequiresReview = false;
            CorrectedCashReceivedText = string.Empty;
            CashTenderCorrectionReasonText = string.Empty;
            ConfirmCashTenderCorrection = false;
            await RefreshAsync();
            SelectedRecentCashCheckout = RecentCashCheckouts.FirstOrDefault(x => x.Id == outcome.InvoiceId);
            StatusMessage = Text(
                $"سوابق وجه دریافتی اصلاح شد، نسخهٔ {response.Version}. مبلغ صورتحساب و دفتر فروش تغییر نکردند.",
                $"Cash tender facts corrected at version {response.Version}. The invoice amount and sale ledger were unchanged.");
        }
        catch (GameNetApiException ex) when (
            ex.Code is "billing.tender_correction_version_conflict" or "billing.tender_correction_conflict" or
                "billing.checkout_outcome_inconsistent" or "idempotency.in_flight")
        {
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RefreshAsync();
        }
        catch (GameNetApiException ex)
        {
            unresolvedCashTenderCorrection = null;
            ErrorMessage = SessionWorkflowErrorText.For(ex.Code);
        }
        catch
        {
            await RefreshAsync();
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task<bool> ReconcileCashTenderCorrectionBeforeRefreshAsync()
    {
        var pending = unresolvedCashTenderCorrection;
        if (pending is null)
            return true;

        try
        {
            var outcome = await api.GetCashCheckoutOutcomeAsync(pending.InvoiceId);
            if (!TryReadTenderFacts(outcome, out var received, out var change, out var version) ||
                outcome.Status != InvoiceStatusContract.Paid || outcome.PaymentId != pending.PaymentId ||
                outcome.ReversalId is not null)
            {
                ErrorMessage = Text(
                    "نتیجهٔ اصلاح با وضعیت صورتحساب/پرداخت تطبیق ندارد؛ اصلاح دوباره مسدود است.",
                    "Correction outcome does not reconcile with the invoice/payment; another correction is blocked.");
                return false;
            }

            var hasCommitted = version == pending.ExpectedVersion + 1 &&
                received == pending.CorrectedAmountReceivedToman &&
                change == pending.CorrectedAmountReceivedToman - outcome.AmountToman &&
                string.Equals(outcome.CashTenderCorrectionReason, pending.Reason, StringComparison.Ordinal) &&
                outcome.CashTenderCorrectionId is Guid correctionId && correctionId != Guid.Empty &&
                outcome.CashTenderCorrectedAtUtc is not null;

            if (hasCommitted)
            {
                unresolvedCashTenderCorrection = null;
                pendingKeys.Remove("billing.cash-tender-correction");
                cashTenderCorrectionRequiresReview = false;
                cashTenderCorrectionRefreshMessage = Text(
                    $"سرور ثبت اصلاح سوابق وجه دریافتی را تأیید کرد؛ نسخهٔ {version}. صورتحساب و دفتر فروش تغییر نکردند.",
                    $"Server confirms the tender correction was recorded at version {version}. The invoice amount and sale ledger were unchanged.");
                return true;
            }

            var unchanged = version == pending.ExpectedVersion &&
                received == pending.PreviousAmountReceivedToman &&
                change == pending.PreviousChangeGivenToman;
            if (unchanged)
            {
                unresolvedCashTenderCorrection = null;
                cashTenderCorrectionRequiresReview = true;
                ConfirmCashTenderCorrection = false;
                cashTenderCorrectionRefreshMessage = Text(
                    "سرور تأیید کرد اصلاح قبلی ثبت نشده است؛ مقدار و علت را بازبینی و دوباره تأیید کنید.",
                    "Server confirms the previous correction was not recorded. Review the amount and reason and explicitly confirm again.");
                return true;
            }

            ErrorMessage = Text(
                "نسخهٔ اصلاح تغییر کرده و نتیجهٔ درخواست جاری قابل اثبات نیست؛ اصلاح دوباره مسدود است.",
                "The correction version changed and the current request cannot be proven. Another correction is blocked.");
            return false;
        }
        catch (Exception ex) when (ex is GameNetApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = Text(
                "نتیجهٔ اصلاح هنوز قابل تأیید نیست؛ تا تطبیق با سرور درخواست را تکرار نکنید.",
                "Correction outcome is still unverified. Do not retry until the Server state can be reconciled.");
            return false;
        }
    }

    private static bool TryReadTenderFacts(
        CashCheckoutOutcomeResponse outcome,
        out long effectiveReceived,
        out long effectiveChange,
        out int version)
    {
        effectiveReceived = 0;
        effectiveChange = 0;
        version = outcome.CashTenderCorrectionVersion ?? 0;
        if (outcome.PaymentId is null || outcome.LedgerEntryId is null || outcome.PaidAtUtc is null ||
            outcome.AmountReceivedToman is not long originalReceived ||
            outcome.ChangeGivenToman is not long originalChange ||
            originalReceived < outcome.AmountToman ||
            originalChange != originalReceived - outcome.AmountToman ||
            version < 0)
            return false;

        effectiveReceived = outcome.EffectiveAmountReceivedToman ?? originalReceived;
        effectiveChange = outcome.EffectiveChangeGivenToman ?? originalChange;
        if (effectiveReceived < outcome.AmountToman ||
            effectiveChange != effectiveReceived - outcome.AmountToman)
            return false;

        if (version == 0)
        {
            if (outcome.CashTenderCorrectionId is not null ||
                outcome.CashTenderCorrectionReason is not null ||
                outcome.CashTenderCorrectedAtUtc is not null ||
                effectiveReceived != originalReceived || effectiveChange != originalChange)
                return false;
        }
        else if (outcome.CashTenderCorrectionId is not Guid correctionId || correctionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(outcome.CashTenderCorrectionReason) ||
            outcome.CashTenderCorrectedAtUtc is null)
            return false;

        return true;
    }

    private static bool IsValidCashTenderCorrectionReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.Trim().Length is >= 3 and <= 256 &&
        !reason.Trim().Any(char.IsControl);

    private sealed record PendingCashTenderCorrection(
        Guid InvoiceId,
        Guid PaymentId,
        int ExpectedVersion,
        long PreviousAmountReceivedToman,
        long PreviousChangeGivenToman,
        long CorrectedAmountReceivedToman,
        string Reason,
        string IdempotencyKey);
}
