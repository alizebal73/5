using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    private async Task EndSessionAsync()
    {
        if (SelectedActiveSession is null)
            return;

        var session = SelectedActiveSession.Session;
        var fingerprint = $"{session.Id:D}|{session.Version}";
        var key = GetKey("session.end", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var invoice = await api.EndSessionAsync(session.Id, new EndSessionRequest(session.Version), key);
            pendingKeys.Remove("session.end");

            // EndSessionAsync returned a server-confirmed pending invoice. Show that authoritative
            // result immediately instead of making visibility depend on unrelated refresh calls
            // for stations, tariffs, or active sessions succeeding first.
            var issuedInvoiceItem = new PendingInvoiceListItem(
                invoice,
                GetStationLabel(invoice.StationId),
                FormatDurationDisplayText(invoice.ElapsedTicks),
                FormatAmountDisplayText(invoice.AmountToman));
            var existingInvoice = PendingInvoices.FirstOrDefault(x => x.Invoice.InvoiceId == invoice.InvoiceId);
            if (existingInvoice is null)
            {
                PendingInvoices.Insert(0, issuedInvoiceItem);
            }
            else
            {
                PendingInvoices[PendingInvoices.IndexOf(existingInvoice)] = issuedInvoiceItem;
            }
            SelectedPendingInvoice = issuedInvoiceItem;

            await RefreshAsync();
            var refreshedInvoice = PendingInvoices.FirstOrDefault(x => x.Invoice.InvoiceId == invoice.InvoiceId);
            if (refreshedInvoice is not null)
            {
                SelectedPendingInvoice = refreshedInvoice;
            }
            else if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                // A failed refresh must not hide the invoice the Server just confirmed.
                PendingInvoices.Insert(0, issuedInvoiceItem);
                SelectedPendingInvoice = issuedInvoiceItem;
            }
            else
            {
                // A successful server refresh is authoritative if the invoice has already moved
                // out of PendingPayment (for example, another operator completed checkout).
                SelectedPendingInvoice = null;
            }

            StatusMessage = Text($"صورتحساب صادر شد: {FormatToman(invoice.AmountToman)} تومان.",
                $"Invoice issued: {FormatToman(invoice.AmountToman)} Toman.");
        }
        catch (GameNetApiException ex)
        {
            if (string.Equals(ex.Code, "sessions.timing_recovery_required", StringComparison.Ordinal))
            {
                timingRecoveryRequiredSessionId = session.Id;
                OnPropertyChanged(nameof(CanRecoverSessionTiming));
                RaiseCommands();
            }
            ErrorMessage = ErrorText(ex.Code);
        }
        catch (HttpRequestException) { ErrorMessage = Text("نتیجهٔ پایان Session نامشخص است؛ تازه‌سازی کنید تا صورتحساب ثبت‌شده پیدا شود.", "Session end result is unknown; refresh to find any issued invoice.");
        }
        catch { ErrorMessage = Text("پایان Session انجام نشد.", "Ending the Session failed."); }
        finally { EndBusy(); }
    }
}
