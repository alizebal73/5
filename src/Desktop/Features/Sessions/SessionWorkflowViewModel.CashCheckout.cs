using System;
using System.Net.Http;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Cash checkout, explicit physical-cash confirmation, and durable outcome reconciliation.
/// Kept separate from the station/session workspace to preserve source-size and UI-boundary rules.
/// </summary>
public sealed partial class SessionWorkflowViewModel
{
    private string amountReceivedText = string.Empty;
    private bool confirmCashCheckout;
    private Guid? unresolvedCashCheckoutInvoiceId;

    public PendingInvoiceListItem? SelectedPendingInvoice
    {
        get => selectedPendingInvoice;
        set
        {
            if (SetField(ref selectedPendingInvoice, value))
            {
                // Cash received is a separate real-world observation, never inferred from the amount due.
                AmountReceivedText = string.Empty;
                ConfirmCashCheckout = false;
                RaiseCommands();
                OnPropertyChanged(nameof(SelectedInvoiceAmountText));
                OnPropertyChanged(nameof(CashCheckoutSummaryText));
            }
        }
    }

    public string AmountReceivedText
    {
        get => amountReceivedText;
        set
        {
            if (SetField(ref amountReceivedText, value))
            {
                // Editing the amount invalidates any prior confirmation.
                ConfirmCashCheckout = false;
                OnPropertyChanged(nameof(CashCheckoutSummaryText));
                RaiseCommands();
            }
        }
    }

    public bool ConfirmCashCheckout
    {
        get => confirmCashCheckout;
        set { if (SetField(ref confirmCashCheckout, value)) RaiseCommands(); }
    }

    public string CashCheckoutSummaryText
    {
        get
        {
            if (SelectedPendingInvoice is null)
                return string.Empty;

            var due = SelectedPendingInvoice.Invoice.AmountToman;
            if (!TryParseToman(AmountReceivedText, out var received) || received < due)
            {
                return Text(
                    $"مبلغ صورتحساب: {FormatToman(due)} تومان؛ وجه نقد دریافتی را به‌صورت عدد صحیح و حداقل برابر این مبلغ وارد کنید.",
                    $"Amount due: {FormatToman(due)} Toman; enter a whole cash amount equal to or greater than this amount.");
            }

            return Text(
                $"صورتحساب: {FormatToman(due)}؛ وجه دریافتی: {FormatToman(received)}؛ باقی‌مانده: {FormatToman(received - due)} تومان.",
                $"Due: {FormatToman(due)}; cash received: {FormatToman(received)}; change: {FormatToman(received - due)} Toman.");
        }
    }

    private async Task CheckoutCashAsync()
    {
        if (SelectedPendingInvoice is null)
            return;
        if (unresolvedCashCheckoutInvoiceId.HasValue)
        {
            ErrorMessage = Text(
                "نتیجهٔ پرداخت قبلی هنوز تأیید نشده است؛ ابتدا تازه‌سازی و تطبیق با سرور را انجام دهید.",
                "The previous checkout outcome is still unverified. Refresh and reconcile with the Server before retrying.");
            return;
        }
        if (!ConfirmCashCheckout)
        {
            ErrorMessage = Text("برای ثبت پرداخت، تأیید آگاهانهٔ اپراتور لازم است.", "Explicit operator confirmation is required before recording cash.");
            return;
        }
        if (!TryParseToman(AmountReceivedText, out var received) || received < SelectedPendingInvoice.Invoice.AmountToman)
        {
            ErrorMessage = Text("وجه دریافتی باید عدد صحیح و حداقل برابر مبلغ صورتحساب باشد.", "Cash received must be a whole number at least equal to the invoice amount.");
            return;
        }

        var invoice = SelectedPendingInvoice.Invoice;
        // A confirmation authorizes this one click only; retries require a fresh review.
        ConfirmCashCheckout = false;
        var fingerprint = $"{invoice.InvoiceId:D}|{received}|{invoice.SessionVersion}";
        var key = GetKey("billing.cash", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var result = await api.CheckoutCashAsync(
                invoice.InvoiceId,
                new CashCheckoutRequest(received, invoice.SessionVersion),
                key);
            pendingKeys.Remove("billing.cash");
            await RefreshAsync();
            StatusMessage = Text(
                $"پرداخت ثبت شد. مبلغ: {FormatToman(result.InvoiceAmountToman)}؛ دریافتی: {FormatToman(result.AmountReceivedToman)}؛ باقی‌مانده: {FormatToman(result.ChangeGivenToman)} تومان.",
                $"Payment recorded. Due: {FormatToman(result.InvoiceAmountToman)}; received: {FormatToman(result.AmountReceivedToman)}; change: {FormatToman(result.ChangeGivenToman)} Toman.");
        }
        catch (GameNetApiException ex) when (ex.Code is "billing.invoice_not_pending" or "billing.checkout_conflict")
        {
            await ReconcileCashCheckoutOutcomeAsync(invoice.InvoiceId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await ReconcileCashCheckoutOutcomeAsync(invoice.InvoiceId);
        }
        catch (GameNetApiException ex) { ErrorMessage = ErrorText(ex.Code); }
        catch
        {
            // If the outcome of a financial command is unexpected, resolve durable Server state
            // before allowing another attempt; never assume an exception means no commit.
            await ReconcileCashCheckoutOutcomeAsync(invoice.InvoiceId);
        }
        finally { EndBusy(); }
    }

    private async Task ReconcileCashCheckoutOutcomeAsync(Guid invoiceId)
    {
        // An ambiguous checkout outcome blocks any retry until durable Server state is confirmed.
        unresolvedCashCheckoutInvoiceId = invoiceId;
        RaiseCommands();
        AmountReceivedText = string.Empty;
        ConfirmCashCheckout = false;
        try
        {
            var outcome = await api.GetCashCheckoutOutcomeAsync(invoiceId);
            if (IsConfirmedPaidCheckoutOutcome(outcome) &&
                outcome.AmountReceivedToman is long received &&
                outcome.ChangeGivenToman is long change)
            {
                // Invoice, Payment and Ledger identities are read back from durable Server state.
                // Do not retry a checkout that has already committed, even if its HTTP response was lost.
                unresolvedCashCheckoutInvoiceId = null;
                RaiseCommands();
                pendingKeys.Remove("billing.cash");
                await RefreshAsync();
                ErrorMessage = string.Empty;
                StatusMessage = Text(
                    $"سرور تأیید کرد پرداخت قبلاً ثبت شده است. مبلغ: {FormatToman(outcome.AmountToman)}؛ دریافتی: {FormatToman(received)}؛ باقی‌مانده: {FormatToman(change)} تومان.",
                    $"Server confirms payment was already recorded. Due: {FormatToman(outcome.AmountToman)}; received: {FormatToman(received)}; change: {FormatToman(change)} Toman. No second payment was sent.");
                return;
            }

            if (IsConfirmedPendingCheckoutOutcome(outcome))
            {
                // The Server explicitly confirms that the invoice is still pending and has no payment/ledger.
                unresolvedCashCheckoutInvoiceId = null;
                RaiseCommands();
                await RefreshAsync();
                ErrorMessage = Text(
                    "سرور تأیید کرد این صورتحساب هنوز باز است و پرداختی ثبت نشده؛ مبلغ دریافتی را دوباره بررسی و ثبت را مجدداً تأیید کنید.",
                    "Server confirms this invoice is still pending and no payment was recorded. Review the cash amount and explicitly confirm before retrying.");
                return;
            }

            ErrorMessage = Text(
                "نتیجهٔ پرداخت سازگار و قابل تأیید نیست. پرداخت مجدد مسدود است؛ ابتدا نتیجه را با سرور تطبیق دهید.",
                "The checkout outcome is not consistent enough to confirm. Retry is blocked; reconcile with the Server first.");
        }
        catch (Exception ex) when (ex is GameNetApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = Text(
                "نتیجهٔ پرداخت هنوز قابل تأیید نیست. تا روشن‌شدن وضعیت سرور، پرداخت را دوباره ثبت نکنید.",
                "Checkout outcome is still unverified. Do not record the cash again until the Server outcome can be confirmed.");
        }
        catch
        {
            ErrorMessage = Text(
                "نتیجهٔ پرداخت هنوز قابل تأیید نیست. تا روشن‌شدن وضعیت سرور، پرداخت را دوباره ثبت نکنید.",
                "Checkout outcome is still unverified. Do not record the cash again until the Server outcome can be confirmed.");
        }
    }

    private static bool IsConfirmedPaidCheckoutOutcome(CashCheckoutOutcomeResponse outcome)
    {
        return outcome.Status == InvoiceStatusContract.Paid &&
               outcome.PaymentId is not null &&
               outcome.LedgerEntryId is not null &&
               outcome.PaidAtUtc is not null &&
               outcome.AmountReceivedToman is long received &&
               outcome.ChangeGivenToman is long change &&
               received >= outcome.AmountToman &&
               change == received - outcome.AmountToman;
    }

    private static bool IsConfirmedPendingCheckoutOutcome(CashCheckoutOutcomeResponse outcome) =>
        outcome.Status == InvoiceStatusContract.PendingPayment &&
        outcome.PaymentId is null &&
        outcome.LedgerEntryId is null &&
        outcome.AmountReceivedToman is null &&
        outcome.ChangeGivenToman is null &&
        outcome.PaidAtUtc is null;

    private static string FormatInvoiceIdentityDisplayText(Guid invoiceId) =>
        Text($"شناسهٔ صورتحساب: {invoiceId:D}", $"Invoice: {invoiceId:D}");

    private static string FormatPaidAtDisplayText(DateTimeOffset? paidAtUtc) =>
        paidAtUtc is { } value
            ? Text($"زمان ثبت: {value.ToLocalTime():g}", $"Paid: {value.ToLocalTime():g}")
            : Text("زمان پرداخت در دسترس نیست", "Payment time unavailable");

    private static string FormatCashOutcomeDetailsDisplayText(CashCheckoutOutcomeResponse outcome)
    {
        if (outcome.Status == InvoiceStatusContract.Reversed)
        {
            if (outcome.ReversalId is null || outcome.ReversedAtUtc is null ||
                outcome.AmountReceivedToman is not long reversedReceived ||
                outcome.ChangeGivenToman is not long reversedChange ||
                outcome.PaymentId is null || outcome.LedgerEntryId is null)
                return Text("سوابق برگشت نیازمند تطبیق است", "Reversal outcome requires reconciliation");

            return Text(
                $"برگشت کامل: {FormatToman(outcome.AmountToman)} تومان؛ دریافتی اولیه: {FormatToman(reversedReceived)}؛ باقی‌ماندهٔ اولیه: {FormatToman(reversedChange)} تومان",
                $"Reversed in full: {FormatToman(outcome.AmountToman)} Toman; original received: {FormatToman(reversedReceived)}; original change: {FormatToman(reversedChange)} Toman");
        }

        if (outcome.Status != InvoiceStatusContract.Paid ||
            outcome.AmountReceivedToman is not long received ||
            outcome.ChangeGivenToman is not long change ||
            outcome.PaymentId is null ||
            outcome.LedgerEntryId is null ||
            outcome.ReversalId is not null)
            return Text("نیازمند بررسی نتیجهٔ پرداخت", "Checkout outcome requires reconciliation");

        return Text(
            $"دریافتی: {FormatToman(received)}؛ باقی‌مانده: {FormatToman(change)} تومان",
            $"Received: {FormatToman(received)}; change: {FormatToman(change)} Toman");
    }

}
