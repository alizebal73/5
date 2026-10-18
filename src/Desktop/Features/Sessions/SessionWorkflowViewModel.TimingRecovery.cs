using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Explicit, audited fallback for Sessions whose monotonic timing continuity cannot be proven.
/// This UI never estimates duration from UTC; the operator must independently verify it.
/// </summary>
public sealed partial class SessionWorkflowViewModel
{
    private string reconciledElapsedMinutesText = string.Empty;
    private string timingRecoveryReasonText = string.Empty;
    private bool confirmTimingRecovery;
    private Guid? timingRecoveryRequiredSessionId;
    private AsyncUiAction? reconcileSessionTimingCommand;

    public ICommand ReconcileSessionTimingCommand =>
        reconcileSessionTimingCommand ??= new AsyncUiAction(ReconcileSessionTimingAsync, CanReconcileSessionTiming);

    public bool CanRecoverSessionTiming =>
        CanOperateSessions &&
        hasPermission(GameNet.Shared.Contracts.V1.Security.Permissions.SessionsTimingRecover) &&
        SelectedActiveSession?.Session.Id == timingRecoveryRequiredSessionId;

    public string ReconciledElapsedMinutesText
    {
        get => reconciledElapsedMinutesText;
        set
        {
            if (SetField(ref reconciledElapsedMinutesText, value ?? string.Empty))
            {
                ConfirmTimingRecovery = false;
                RaiseCommands();
            }
        }
    }

    public string TimingRecoveryReasonText
    {
        get => timingRecoveryReasonText;
        set
        {
            if (SetField(ref timingRecoveryReasonText, value ?? string.Empty))
            {
                ConfirmTimingRecovery = false;
                RaiseCommands();
            }
        }
    }

    public bool ConfirmTimingRecovery
    {
        get => confirmTimingRecovery;
        set
        {
            if (SetField(ref confirmTimingRecovery, value))
                RaiseCommands();
        }
    }

    private bool CanReconcileSessionTiming()
    {
        return !IsBusy &&
            CanRecoverSessionTiming &&
            SelectedActiveSession?.Session.Status == SessionStatusContract.Active &&
            TryParseToman(ReconciledElapsedMinutesText, out var minutes) &&
            minutes is >= 1 and <= 43_200 &&
            IsValidTimingRecoveryReason(TimingRecoveryReasonText) &&
            ConfirmTimingRecovery;
    }

    private async Task ReconcileSessionTimingAsync()
    {
        var selected = SelectedActiveSession;
        if (selected is null || !CanReconcileSessionTiming())
            return;

        var session = selected.Session;
        if (!TryParseToman(ReconciledElapsedMinutesText, out var minutes) ||
            minutes is < 1 or > 43_200)
            return;

        var reason = TimingRecoveryReasonText.Trim();
        var fingerprint = $"{session.Id:D}|{session.Version}|{minutes}|{reason}|confirmed";
        var key = GetKey("sessions.timing.recovery", fingerprint);
        var expectedElapsedTicks = checked(minutes * TimeSpan.TicksPerMinute);
        var submittedSessionId = session.Id;

        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var invoice = await api.ReconcileSessionTimingAsync(
                session.Id,
                new ReconcileSessionTimingRequest(session.Version, minutes, reason, true),
                key);

            if (!IsExpectedReconciledInvoice(invoice, session, expectedElapsedTicks))
            {
                await RefreshAsync();
                ErrorMessage = Text(
                    "پاسخ بازیابی با Session و مدت تأییدشده تطبیق ندارد؛ پیش از هر تکرار وضعیت را با سرور تطبیق دهید.",
                    "The recovery response does not match the Session and verified duration. Reconcile with the Server before retrying.");
                return;
            }

            pendingKeys.Remove("sessions.timing.recovery");
            ReconciledElapsedMinutesText = string.Empty;
            TimingRecoveryReasonText = string.Empty;
            ConfirmTimingRecovery = false;
            await RefreshAsync();
            SelectedPendingInvoice = PendingInvoices.FirstOrDefault(x => x.Invoice.InvoiceId == invoice.InvoiceId);
            StatusMessage = Text(
                $"زمان Session با ثبت دلیل و سابقه تطبیق داده شد: {minutes} دقیقه؛ صورتحساب {FormatToman(invoice.AmountToman)} تومان. بررسی مبلغ براساس تعرفه ثبت‌شدهٔ زمان شروع انجام شد.",
                $"Session timing was reconciled with an audit reason: {minutes} minutes; invoice {FormatToman(invoice.AmountToman)} Toman. The original start-time tariff snapshot was used.");
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = ErrorText(ex.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A lost response may follow a committed transaction. Refresh first; if the
            // invoice is found, confirm it from Server state. Otherwise keep the same
            // idempotency key so a safe retry cannot create a second invoice.
            try
            {
                await RefreshAsync();
                var recovered = PendingInvoices.FirstOrDefault(x =>
                    x.Invoice.SessionId == submittedSessionId &&
                    x.Invoice.ElapsedTicks == expectedElapsedTicks &&
                    x.Invoice.Status == InvoiceStatusContract.PendingPayment);
                if (recovered is not null)
                {
                    pendingKeys.Remove("sessions.timing.recovery");
                    SelectedPendingInvoice = recovered;
                    SelectedActiveSession = null;
                    ReconciledElapsedMinutesText = string.Empty;
                    TimingRecoveryReasonText = string.Empty;
                    ConfirmTimingRecovery = false;
                    StatusMessage = Text(
                        "پاسخ درخواست دریافت نشد، اما سرور صورتحساب متناظر را تأیید کرد؛ صورتحساب دوباره صادر نشد.",
                        "The request response was lost, but the Server confirms the matching invoice; no duplicate invoice was created.");
                }
                else
                {
                    ErrorMessage = Text(
                        "نتیجهٔ بازیابی هنوز قطعی نیست. ورودی‌ها را پاک نکنید؛ پس از بازگشت اتصال، همان درخواست با همان کلید تطبیق داده می‌شود.",
                        "The recovery outcome is not yet confirmed. Keep the inputs; once connectivity returns, the same request key will be reconciled.");
                }
            }
            catch
            {
                ErrorMessage = Text(
                    "نتیجهٔ بازیابی نامشخص است و تازه‌سازی هم موفق نشد. ورودی‌ها را نگه دارید و تا تطبیق وضعیت، درخواست جدید نسازید.",
                    "The recovery outcome is unknown and refresh also failed. Keep the inputs and do not create a new request until the outcome is reconciled.");
            }
        }
        catch
        {
            ErrorMessage = Text(
                "بازیابی زمان کامل نشد؛ وضعیت Session و صورتحساب را از سرور تازه‌سازی کنید.",
                "Timing recovery did not complete. Refresh Server Session and invoice state.");
        }
        finally
        {
            EndBusy();
        }
    }

    private static bool IsExpectedReconciledInvoice(
        InvoicePreviewResponse invoice,
        SessionResponse session,
        long expectedElapsedTicks) =>
        invoice.InvoiceId != Guid.Empty &&
        invoice.SessionId == session.Id &&
        invoice.StationId == session.StationId &&
        invoice.Status == InvoiceStatusContract.PendingPayment &&
        invoice.SessionVersion == checked(session.Version + 1) &&
        invoice.ElapsedTicks == expectedElapsedTicks &&
        invoice.TariffId == session.TariffId &&
        invoice.TariffHourlyRateTomanSnapshot == session.TariffHourlyRateTomanSnapshot &&
        invoice.AmountToman > 0;

    private static bool IsValidTimingRecoveryReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.Trim().Length is >= 3 and <= 256 &&
        !reason.Any(char.IsControl);
}
