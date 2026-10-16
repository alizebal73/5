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
/// Operator adapter for the dedicated full cash-sale reversal operation.
/// Business rules and transaction authority remain on the Server.
/// </summary>
public sealed partial class SessionWorkflowViewModel
{
    private string cashSaleReversalReasonText = string.Empty;
    private bool confirmCashSaleReversal;
    private CashCheckoutOutcomeListItem? selectedRecentCashCheckout;
    private Guid? unresolvedCashSaleReversalInvoiceId;

    public ICommand ReverseCashSaleCommand { get; }

    public CashCheckoutOutcomeListItem? SelectedRecentCashCheckout
    {
        get => selectedRecentCashCheckout;
        set
        {
            if (SetField(ref selectedRecentCashCheckout, value))
            {
                CashSaleReversalReasonText = string.Empty;
                ConfirmCashSaleReversal = false;
                OnPropertyChanged(nameof(CashSaleReversalSummaryText));
                RaiseCommands();
            }
        }
    }

    public string CashSaleReversalReasonText
    {
        get => cashSaleReversalReasonText;
        set
        {
            if (SetField(ref cashSaleReversalReasonText, value ?? string.Empty))
            {
                // Any edit invalidates the explicit approval of this exact request.
                ConfirmCashSaleReversal = false;
                RaiseCommands();
            }
        }
    }

    public bool ConfirmCashSaleReversal
    {
        get => confirmCashSaleReversal;
        set
        {
            if (SetField(ref confirmCashSaleReversal, value))
                RaiseCommands();
        }
    }

    public string CashSaleReversalSummaryText
    {
        get
        {
            if (SelectedRecentCashCheckout is not { } selected)
                return Text(
                    "برای برگشت، یک فروش نقدی ثبت‌شده را انتخاب کنید.",
                    "Select a recorded cash sale to review a possible reversal.");

            var outcome = selected.Outcome;
            if (outcome.Status == InvoiceStatusContract.Reversed)
                return Text(
                    $"این فروش قبلاً در وضعیت برگشت‌شده است. مبلغ ثبت‌شدهٔ برگشت: {FormatToman(outcome.AmountToman)} تومان.",
                    $"This sale is already reversed. Recorded reversal amount: {FormatToman(outcome.AmountToman)} Toman.");

            if (!IsConfirmedPaidCashSaleWithoutReversal(outcome))
                return Text(
                    "وضعیت پرداخت برای برگشت کامل قابل تأیید نیست؛ عملیات مسدود است.",
                    "The payment state cannot be verified for full reversal; the operation is blocked.");

            var received = outcome.AmountReceivedToman!.Value;
            var change = outcome.ChangeGivenToman!.Value;
            return Text(
                $"برگشت فقط مبلغ صورتحساب، یعنی {FormatToman(outcome.AmountToman)} تومان را برمی‌گرداند. وجه دریافتی {FormatToman(received)} و باقی‌مانده {FormatToman(change)} تومان سوابق پرداخت هستند و مبلغ برگشت را افزایش نمی‌دهند.",
                $"This returns only the invoice amount: {FormatToman(outcome.AmountToman)} Toman. Cash received ({FormatToman(received)}) and change ({FormatToman(change)} Toman) are historical tender facts and do not increase the reversal amount.");
        }
    }

    private async Task ReverseCashSaleAsync()
    {
        var selected = SelectedRecentCashCheckout;
        if (selected is null)
            return;

        if (!CanReverseCashSale)
        {
            ErrorMessage = Text("مجوز مستقل برگشت فروش نقدی را ندارید.", "You do not have the dedicated cash-sale reversal permission.");
            return;
        }
        if (unresolvedCashSaleReversalInvoiceId.HasValue)
        {
            ErrorMessage = Text(
                "نتیجهٔ برگشت قبلی هنوز قابل تأیید نیست؛ ابتدا وضعیت آن را با سرور تطبیق دهید.",
                "The previous reversal outcome is unverified. Reconcile it with the Server before retrying.");
            return;
        }

        var outcome = selected.Outcome;
        if (!IsConfirmedPaidCashSaleWithoutReversal(outcome) || outcome.PaymentId is not Guid paymentId)
        {
            ErrorMessage = Text(
                "فقط فروش نقدی پرداخت‌شده و تطبیق‌یافته قابل برگشت است؛ وضعیت فعلی مجاز نیست.",
                "Only a reconciled paid cash sale can be reversed; the current outcome is not eligible.");
            return;
        }
        if (!IsValidCashSaleReversalReason(CashSaleReversalReasonText))
        {
            ErrorMessage = Text(
                "علت برگشت باید بین ۳ تا ۲۵۶ نویسهٔ قابل‌چاپ باشد.",
                "The reversal reason must contain 3–256 printable characters.");
            return;
        }
        if (!ConfirmCashSaleReversal)
        {
            ErrorMessage = Text(
                "برای برگشت وجه، تأیید آگاهانهٔ اپراتور برای همین صورتحساب و مبلغ لازم است.",
                "Explicit operator confirmation is required for this exact invoice and amount.");
            return;
        }

        var invoiceId = outcome.InvoiceId;
        var amountToman = outcome.AmountToman;
        var reason = CashSaleReversalReasonText.Trim();
        var fingerprint = $"{invoiceId:D}|{paymentId:D}|{amountToman}|{reason}";
        var key = GetKey("billing.cash-reversal", fingerprint);

        // Confirmation authorizes one attempt only. An ambiguous response must be reconciled
        // with the durable Server outcome before another mutation can be issued.
        ConfirmCashSaleReversal = false;
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var response = await api.ReverseCashSaleAsync(
                invoiceId,
                new CashSaleReversalRequest(paymentId, amountToman, reason),
                key);

            if (response.InvoiceId != invoiceId ||
                response.PaymentId != paymentId ||
                response.AmountToman != amountToman ||
                response.ReversalId == Guid.Empty ||
                response.LedgerEntryId == Guid.Empty)
            {
                await ReconcileCashSaleReversalOutcomeAsync(invoiceId);
                return;
            }

            pendingKeys.Remove("billing.cash-reversal");
            unresolvedCashSaleReversalInvoiceId = null;
            await RefreshAsync();
            SelectedRecentCashCheckout = RecentCashCheckouts.FirstOrDefault(x => x.Id == invoiceId);
            StatusMessage = Text(
                $"برگشت کامل فروش ثبت شد. مبلغ جبرانی: {FormatToman(response.AmountToman)} تومان؛ صورتحساب و سوابق اولیه حفظ شدند.",
                $"Full cash-sale reversal recorded. Reversal amount: {FormatToman(response.AmountToman)} Toman; original invoice and payment history were preserved.");
        }
        catch (GameNetApiException ex) when (
            ex.Code is "billing.sale_already_reversed" or "billing.reversal_conflict" or
                "billing.checkout_outcome_inconsistent" or "billing.reversal_response_inconsistent")
        {
            await ReconcileCashSaleReversalOutcomeAsync(invoiceId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await ReconcileCashSaleReversalOutcomeAsync(invoiceId);
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = ErrorText(ex.Code);
        }
        catch
        {
            await ReconcileCashSaleReversalOutcomeAsync(invoiceId);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task ReconcileCashSaleReversalOutcomeAsync(Guid invoiceId)
    {
        unresolvedCashSaleReversalInvoiceId = invoiceId;
        RaiseCommands();
        ConfirmCashSaleReversal = false;

        try
        {
            var outcome = await api.GetCashCheckoutOutcomeAsync(invoiceId);
            if (IsConfirmedReversedCashSaleOutcome(outcome))
            {
                unresolvedCashSaleReversalInvoiceId = null;
                pendingKeys.Remove("billing.cash-reversal");
                RaiseCommands();
                await RefreshAsync();
                SelectedRecentCashCheckout = RecentCashCheckouts.FirstOrDefault(x => x.Id == invoiceId);
                ErrorMessage = string.Empty;
                StatusMessage = Text(
                    $"سرور تأیید کرد برگشت قبلاً ثبت شده است: {FormatToman(outcome.AmountToman)} تومان. درخواست برگشت دیگری ارسال نشد.",
                    $"Server confirms the reversal was already recorded for {FormatToman(outcome.AmountToman)} Toman. No second reversal was sent.");
                return;
            }

            if (IsConfirmedPaidCashSaleWithoutReversal(outcome))
            {
                // Invoice remains paid and no reversal exists, so the prior transaction did not commit.
                // Retain the same idempotency key for the same request but require fresh confirmation.
                unresolvedCashSaleReversalInvoiceId = null;
                CashSaleReversalReasonText = string.Empty;
                ConfirmCashSaleReversal = false;
                RaiseCommands();
                await RefreshAsync();
                ErrorMessage = Text(
                    "سرور تأیید کرد برگشتی ثبت نشده و فروش همچنان پرداخت‌شده است؛ علت را دوباره بررسی و تأیید کنید.",
                    "Server confirms no reversal was recorded and the sale remains paid. Review the reason and explicitly confirm before retrying.");
                return;
            }

            ErrorMessage = Text(
                "سوابق برگشت و پرداخت با هم تطبیق ندارند؛ درخواست دوباره مسدود است.",
                "Payment and reversal records do not reconcile. The reversal command remains blocked.");
        }
        catch (Exception ex) when (ex is GameNetApiException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = Text(
                "نتیجهٔ برگشت هنوز قابل تأیید نیست؛ تا روشن‌شدن وضعیت سرور آن را تکرار نکنید.",
                "The reversal outcome is still unverified. Do not retry until the Server state can be confirmed.");
        }
    }

    private static bool IsValidCashSaleReversalReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.Trim().Length is >= 3 and <= 256 &&
        !reason.Trim().Any(char.IsControl);

    private static bool IsConfirmedPaidCashSaleWithoutReversal(CashCheckoutOutcomeResponse outcome) =>
        outcome.Status == InvoiceStatusContract.Paid &&
        outcome.ReversalId is null &&
        outcome.ReversedAtUtc is null &&
        outcome.ReversalReason is null &&
        outcome.PaymentId is not null &&
        outcome.LedgerEntryId is not null &&
        outcome.PaidAtUtc is not null &&
        outcome.AmountReceivedToman is long received &&
        outcome.ChangeGivenToman is long change &&
        received >= outcome.AmountToman &&
        change == received - outcome.AmountToman;

    private static bool IsConfirmedReversedCashSaleOutcome(CashCheckoutOutcomeResponse outcome) =>
        outcome.Status == InvoiceStatusContract.Reversed &&
        outcome.ReversalId is not null &&
        outcome.ReversedAtUtc is not null &&
        !string.IsNullOrWhiteSpace(outcome.ReversalReason) &&
        outcome.PaymentId is not null &&
        outcome.LedgerEntryId is not null &&
        outcome.PaidAtUtc is not null &&
        outcome.AmountReceivedToman is long received &&
        outcome.ChangeGivenToman is long change &&
        received >= outcome.AmountToman &&
        change == received - outcome.AmountToman;
}
