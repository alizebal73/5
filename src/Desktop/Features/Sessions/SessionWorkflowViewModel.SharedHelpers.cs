using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    private bool CanCheckoutCashNow()
    {
        if (IsBusy || !CanCheckout || unresolvedCashCheckoutInvoiceId.HasValue || !ConfirmCashCheckout)
            return false;

        var pending = SelectedPendingInvoice;
        return pending is not null &&
               pending.Invoice.Status == InvoiceStatusContract.PendingPayment &&
               TryParseToman(AmountReceivedText, out var received) &&
               received >= pending.Invoice.AmountToman;
    }

    private bool CanCorrectCashTenderNow()
    {
        if (IsBusy || !CanCorrectCashTender || unresolvedCashTenderCorrection is not null ||
            !IsValidCashTenderCorrectionReason(CashTenderCorrectionReasonText) ||
            !ConfirmCashTenderCorrection)
            return false;

        var selected = SelectedRecentCashCheckout;
        if (selected is null || selected.Outcome.Status != InvoiceStatusContract.Paid ||
            selected.Outcome.PaymentId is null ||
            !TryParseToman(CorrectedCashReceivedText, out var correctedReceived))
            return false;

        var receivedBefore = selected.Outcome.EffectiveAmountReceivedToman ?? selected.Outcome.AmountReceivedToman;
        return correctedReceived >= selected.Outcome.AmountToman && correctedReceived != receivedBefore;
    }

    private string GetKey(string scope, string fingerprint)
    {
        if (pendingKeys.TryGetValue(scope, out var existing) &&
            string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            return existing.Key;

        var key = Guid.NewGuid().ToString("N");
        pendingKeys[scope] = (fingerprint, key);
        return key;
    }

    private static void Replace<T, TKey>(
        ObservableCollection<T> collection,
        IReadOnlyCollection<T> values,
        TKey? previousKey,
        Func<T, TKey> key,
        out T? selected)
        where TKey : struct
        where T : class
    {
        collection.Clear();
        foreach (var value in values)
            collection.Add(value);

        selected = previousKey.HasValue
            ? values.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals(key(x), previousKey.Value))
            : default;
        selected ??= values.FirstOrDefault();
    }

    private static bool TryParseToman(string? text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (c is '۰' or '٠') builder.Append('0');
            else if (c is '۱' or '١') builder.Append('1');
            else if (c is '۲' or '٢') builder.Append('2');
            else if (c is '۳' or '٣') builder.Append('3');
            else if (c is '۴' or '٤') builder.Append('4');
            else if (c is '۵' or '٥') builder.Append('5');
            else if (c is '۶' or '٦') builder.Append('6');
            else if (c is '۷' or '٧') builder.Append('7');
            else if (c is '۸' or '٨') builder.Append('8');
            else if (c is '۹' or '٩') builder.Append('9');
            else if (c is ',' or '٬' or ' ' or '\u00A0') continue;
            else builder.Append(c);
        }

        return long.TryParse(builder.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDurationDisplayText(long elapsedTicks) =>
        SessionWorkflowFormatting.FormatDurationDisplayText(elapsedTicks);

    private static string FormatAmountDisplayText(long amountToman) =>
        Text($"‏{FormatToman(amountToman)} تومان", $"{FormatToman(amountToman)} Toman");
}
