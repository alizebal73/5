using System.Net.Http;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    public ICommand PauseSessionCommand { get; }
    public ICommand ResumeSessionCommand { get; }

    private void RaiseLifecycleCommands()
    {
        (PauseSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (ResumeSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
    }

    private async Task PauseSessionAsync()
    {
        if (SelectedActiveSession is null)
            return;

        var session = SelectedActiveSession.Session;
        var fingerprint = $"{session.Id:D}|{session.Version}";
        var key = GetKey("session.pause", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var paused = await api.PauseSessionAsync(
                session.Id, new SessionLifecycleRequest(session.Version), key);
            pendingKeys.Remove("session.pause");
            await RefreshAsync();
            SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == paused.Id);
            StatusMessage = Text(
                $"زمان صورتحساب PC {GetStationLabel(session.StationId)} متوقف شد. مدت توقف محاسبه نمی‌شود.",
                $"Billing time paused for PC {GetStationLabel(session.StationId)}. Paused time is not billed.");
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = ErrorText(ex.Code);
        }
        catch (HttpRequestException)
        {
            // Keep the fingerprint/key so a refresh and retry can safely reconcile the
            // ambiguous outcome through the server's idempotency record.
            var message = Text(
                "نتیجهٔ توقف نامشخص است؛ وضعیت را تازه‌سازی کنید و پیش از ارسال درخواست تازه، Session را بررسی کنید.",
                "Pause outcome is unknown; refresh the server state before issuing a new request.");
            await TryRefreshAfterLifecycleTransportFailureAsync();
            ErrorMessage = message;
        }
        catch
        {
            ErrorMessage = Text("توقف Session انجام نشد.", "Pausing the Session failed.");
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task ResumeSessionAsync()
    {
        if (SelectedActiveSession is null)
            return;

        var session = SelectedActiveSession.Session;
        var fingerprint = $"{session.Id:D}|{session.Version}";
        var key = GetKey("session.resume", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var resumed = await api.ResumeSessionAsync(
                session.Id, new SessionLifecycleRequest(session.Version), key);
            pendingKeys.Remove("session.resume");
            await RefreshAsync();
            SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == resumed.Id);
            StatusMessage = Text(
                $"بازی PC {GetStationLabel(session.StationId)} ادامه یافت؛ زمان‌سنجی از نقطهٔ جدید شروع شد.",
                $"Play resumed on PC {GetStationLabel(session.StationId)}; a fresh monotonic timing segment has started.");
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = ErrorText(ex.Code);
        }
        catch (HttpRequestException)
        {
            var message = Text(
                "نتیجهٔ ادامه نامشخص است؛ وضعیت را تازه‌سازی کنید و پیش از ارسال درخواست تازه، Session را بررسی کنید.",
                "Resume outcome is unknown; refresh the server state before issuing a new request.");
            await TryRefreshAfterLifecycleTransportFailureAsync();
            ErrorMessage = message;
        }
        catch
        {
            ErrorMessage = Text("ادامهٔ Session انجام نشد.", "Resuming the Session failed.");
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task TryRefreshAfterLifecycleTransportFailureAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // The original mutation outcome remains unknown and its idempotency key is
            // retained. A later operator refresh will reconcile with the Server.
        }
    }
}
