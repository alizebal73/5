using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

internal interface ISessionLifecycleHost
{
    IGameNetServerClient Api { get; }
    bool CanOperateSessions { get; }
    bool IsBusy { get; }
    ActiveSessionListItem? SelectedActiveSession { get; set; }
    Guid? TimingRecoveryRequiredSessionId { get; }
    IReadOnlyCollection<ActiveSessionListItem> ActiveSessions { get; }
    IReadOnlyCollection<PendingInvoiceListItem> PendingInvoices { get; }
    PendingInvoiceListItem? SelectedPendingInvoice { get; set; }
    bool HasPermission(string permission);
    void BeginBusy();
    void EndBusy();
    Task RefreshAsync();
    string GetKey(string scope, string fingerprint);
    void RemovePendingKey(string scope);
    string GetStationLabel(Guid stationId);
    bool TryParseToman(string? value, out long amount);
    string FormatToman(long amount);
    string ErrorMessage { set; }
    string StatusMessage { set; }
}

internal sealed class SessionLifecycleCoordinator : INotifyPropertyChanged
{
    private readonly ISessionLifecycleHost host;
    private string reconciledElapsedMinutesText = string.Empty;
    private string timingRecoveryReasonText = string.Empty;
    private bool confirmTimingRecovery;

    public SessionLifecycleCoordinator(ISessionLifecycleHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        PauseSessionCommand = new AsyncUiAction(PauseSessionAsync, CanPauseSession);
        ResumeSessionCommand = new AsyncUiAction(ResumeSessionAsync, CanResumeSession);
        ReconcileSessionTimingCommand = new AsyncUiAction(ReconcileSessionTimingAsync, CanReconcileSessionTiming);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand PauseSessionCommand { get; }
    public ICommand ResumeSessionCommand { get; }
    public ICommand ReconcileSessionTimingCommand { get; }

    private bool CanPauseSession() =>
        !host.IsBusy && host.CanOperateSessions &&
        host.SelectedActiveSession?.Session.Status == SessionStatusContract.Active;

    private bool CanResumeSession() =>
        !host.IsBusy && host.CanOperateSessions &&
        host.SelectedActiveSession?.Session.Status == SessionStatusContract.Paused;

    public bool CanRecoverSessionTiming =>
        host.CanOperateSessions &&
        host.HasPermission(Permissions.SessionsTimingRecover) &&
        host.SelectedActiveSession?.Session.Id == host.TimingRecoveryRequiredSessionId;

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

    public void NotifyHostStateChanged()
    {
        OnPropertyChanged(nameof(CanRecoverSessionTiming));
        RaiseCommands();
    }

    private void RaiseLifecycleCommands()
    {
        (PauseSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (ResumeSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
    }

    private void RaiseCommands()
    {
        RaiseLifecycleCommands();
        (ReconcileSessionTimingCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRecoverSessionTiming));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string Text(string fa, string en) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? fa : en;

private async Task PauseSessionAsync()
    {
        if (host.SelectedActiveSession is null)
            return;

        var session = host.SelectedActiveSession.Session;
        var fingerprint = $"{session.Id:D}|{session.Version}";
        var key = host.GetKey("session.pause", fingerprint);
        host.BeginBusy();
        host.ErrorMessage = string.Empty;
        try
        {
            var paused = await host.Api.PauseSessionAsync(
                session.Id, new SessionLifecycleRequest(session.Version), key);
            host.RemovePendingKey("session.pause");
            await host.RefreshAsync();
            host.SelectedActiveSession = host.ActiveSessions.FirstOrDefault(x => x.Session.Id == paused.Id);
            host.StatusMessage = Text(
                $"زمان صورتحساب PC {host.GetStationLabel(session.StationId)} متوقف شد. مدت توقف محاسبه نمی‌شود.",
                $"Billing time paused for PC {host.GetStationLabel(session.StationId)}. Paused time is not billed.");
        }
        catch (GameNetApiException ex)
        {
            host.ErrorMessage = SessionWorkflowErrorText.For(ex.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Keep the fingerprint/key so a refresh and retry can safely reconcile the
            // ambiguous outcome through the server's idempotency record.
            var message = Text(
                "نتیجهٔ توقف نامشخص است؛ وضعیت را تازه‌سازی کنید و پیش از ارسال درخواست تازه، Session را بررسی کنید.",
                "Pause outcome is unknown; refresh the server state before issuing a new request.");
            await TryRefreshAfterLifecycleTransportFailureAsync();
            host.ErrorMessage = message;
        }
        catch
        {
            host.ErrorMessage = Text("توقف Session انجام نشد.", "Pausing the Session failed.");
        }
        finally
        {
            host.EndBusy();
        }
    }

    private async Task ResumeSessionAsync()
    {
        if (host.SelectedActiveSession is null)
            return;

        var session = host.SelectedActiveSession.Session;
        var fingerprint = $"{session.Id:D}|{session.Version}";
        var key = host.GetKey("session.resume", fingerprint);
        host.BeginBusy();
        host.ErrorMessage = string.Empty;
        try
        {
            var resumed = await host.Api.ResumeSessionAsync(
                session.Id, new SessionLifecycleRequest(session.Version), key);
            host.RemovePendingKey("session.resume");
            await host.RefreshAsync();
            host.SelectedActiveSession = host.ActiveSessions.FirstOrDefault(x => x.Session.Id == resumed.Id);
            host.StatusMessage = Text(
                $"بازی PC {host.GetStationLabel(session.StationId)} ادامه یافت؛ زمان‌سنجی از نقطهٔ جدید شروع شد.",
                $"Play resumed on PC {host.GetStationLabel(session.StationId)}; a fresh monotonic timing segment has started.");
        }
        catch (GameNetApiException ex)
        {
            host.ErrorMessage = SessionWorkflowErrorText.For(ex.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var message = Text(
                "نتیجهٔ ادامه نامشخص است؛ وضعیت را تازه‌سازی کنید و پیش از ارسال درخواست تازه، Session را بررسی کنید.",
                "Resume outcome is unknown; refresh the server state before issuing a new request.");
            await TryRefreshAfterLifecycleTransportFailureAsync();
            host.ErrorMessage = message;
        }
        catch
        {
            host.ErrorMessage = Text("ادامهٔ Session انجام نشد.", "Resuming the Session failed.");
        }
        finally
        {
            host.EndBusy();
        }
    }

    private async Task TryRefreshAfterLifecycleTransportFailureAsync()
    {
        try
        {
            await host.RefreshAsync();
        }
        catch
        {
            // The original mutation outcome remains unknown and its idempotency key is
            // retained. A later operator refresh will reconcile with the Server.
        }
    }

private string reconciledElapsedMinutesText = string.Empty;
    private string timingRecoveryReasonText = string.Empty;
    private bool confirmTimingRecovery;
    private Guid? host.TimingRecoveryRequiredSessionId;
    public bool CanRecoverSessionTiming =>
        host.CanOperateSessions &&
        host.HasPermission(GameNet.Shared.Contracts.V1.Security.Permissions.SessionsTimingRecover) &&
        host.SelectedActiveSession?.Session.Id == host.TimingRecoveryRequiredSessionId;

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
        return !host.IsBusy &&
            CanRecoverSessionTiming &&
            host.SelectedActiveSession?.Session.Status == SessionStatusContract.Active &&
            host.TryParseToman(ReconciledElapsedMinutesText, out var minutes) &&
            minutes is >= 1 and <= 43_200 &&
            IsValidTimingRecoveryReason(TimingRecoveryReasonText) &&
            ConfirmTimingRecovery;
    }

    private async Task ReconcileSessionTimingAsync()
    {
        var selected = host.SelectedActiveSession;
        if (selected is null || !CanReconcileSessionTiming())
            return;

        var session = selected.Session;
        if (!host.TryParseToman(ReconciledElapsedMinutesText, out var minutes) ||
            minutes is < 1 or > 43_200)
            return;

        var reason = TimingRecoveryReasonText.Trim();
        var fingerprint = $"{session.Id:D}|{session.Version}|{minutes}|{reason}|confirmed";
        var key = host.GetKey("sessions.timing.recovery", fingerprint);
        var expectedElapsedTicks = checked(minutes * TimeSpan.TicksPerMinute);
        var submittedSessionId = session.Id;

        host.BeginBusy();
        host.ErrorMessage = string.Empty;
        try
        {
            var invoice = await host.Api.ReconcileSessionTimingAsync(
                session.Id,
                new ReconcileSessionTimingRequest(session.Version, minutes, reason, true),
                key);

            if (!IsExpectedReconciledInvoice(invoice, session, expectedElapsedTicks))
            {
                await host.RefreshAsync();
                host.ErrorMessage = Text(
                    "پاسخ بازیابی با Session و مدت تأییدشده تطبیق ندارد؛ پیش از هر تکرار وضعیت را با سرور تطبیق دهید.",
                    "The recovery response does not match the Session and verified duration. Reconcile with the Server before retrying.");
                return;
            }

            host.RemovePendingKey("sessions.timing.recovery");
            ReconciledElapsedMinutesText = string.Empty;
            TimingRecoveryReasonText = string.Empty;
            ConfirmTimingRecovery = false;
            await host.RefreshAsync();
            host.SelectedPendingInvoice = host.PendingInvoices.FirstOrDefault(x => x.Invoice.InvoiceId == invoice.InvoiceId);
            host.StatusMessage = Text(
                $"زمان Session با ثبت دلیل و سابقه تطبیق داده شد: {minutes} دقیقه؛ صورتحساب {host.FormatToman(invoice.AmountToman)} تومان. بررسی مبلغ براساس تعرفه ثبت‌شدهٔ زمان شروع انجام شد.",
                $"Session timing was reconciled with an audit reason: {minutes} minutes; invoice {host.FormatToman(invoice.AmountToman)} Toman. The original start-time tariff snapshot was used.");
        }
        catch (GameNetApiException ex)
        {
            host.ErrorMessage = SessionWorkflowErrorText.For(ex.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A lost response may follow a committed transaction. Refresh first; if the
            // invoice is found, confirm it from Server state. Otherwise keep the same
            // idempotency key so a safe retry cannot create a second invoice.
            try
            {
                await host.RefreshAsync();
                var recovered = host.PendingInvoices.FirstOrDefault(x =>
                    x.Invoice.SessionId == submittedSessionId &&
                    x.Invoice.ElapsedTicks == expectedElapsedTicks &&
                    x.Invoice.Status == InvoiceStatusContract.PendingPayment);
                if (recovered is not null)
                {
                    host.RemovePendingKey("sessions.timing.recovery");
                    host.SelectedPendingInvoice = recovered;
                    host.SelectedActiveSession = null;
                    ReconciledElapsedMinutesText = string.Empty;
                    TimingRecoveryReasonText = string.Empty;
                    ConfirmTimingRecovery = false;
                    host.StatusMessage = Text(
                        "پاسخ درخواست دریافت نشد، اما سرور صورتحساب متناظر را تأیید کرد؛ صورتحساب دوباره صادر نشد.",
                        "The request response was lost, but the Server confirms the matching invoice; no duplicate invoice was created.");
                }
                else
                {
                    host.ErrorMessage = Text(
                        "نتیجهٔ بازیابی هنوز قطعی نیست. ورودی‌ها را پاک نکنید؛ پس از بازگشت اتصال، همان درخواست با همان کلید تطبیق داده می‌شود.",
                        "The recovery outcome is not yet confirmed. Keep the inputs; once connectivity returns, the same request key will be reconciled.");
                }
            }
            catch
            {
                host.ErrorMessage = Text(
                    "نتیجهٔ بازیابی نامشخص است و تازه‌سازی هم موفق نشد. ورودی‌ها را نگه دارید و تا تطبیق وضعیت، درخواست جدید نسازید.",
                    "The recovery outcome is unknown and refresh also failed. Keep the inputs and do not create a new request until the outcome is reconciled.");
            }
        }
        catch
        {
            host.ErrorMessage = Text(
                "بازیابی زمان کامل نشد؛ وضعیت Session و صورتحساب را از سرور تازه‌سازی کنید.",
                "Timing recovery did not complete. Refresh Server Session and invoice state.");
        }
        finally
        {
            host.EndBusy();
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
