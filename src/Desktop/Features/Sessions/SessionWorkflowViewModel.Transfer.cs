using System.Net.Http;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    private SessionStationOption? selectedTransferDestination;
    private string sessionTransferReasonText = string.Empty;
    private bool confirmSessionTransfer;

    public ObservableCollection<SessionStationOption> TransferDestinationChoices { get; } = [];
    public ICommand TransferSessionCommand { get; private set; } = null!;

    public SessionStationOption? SelectedTransferDestination
    {
        get => selectedTransferDestination;
        set
        {
            if (SetField(ref selectedTransferDestination, value))
                RaiseSessionTransferCommand();
        }
    }

    public string SessionTransferReasonText
    {
        get => sessionTransferReasonText;
        set
        {
            if (SetField(ref sessionTransferReasonText, value))
                RaiseSessionTransferCommand();
        }
    }

    public bool ConfirmSessionTransfer
    {
        get => confirmSessionTransfer;
        set
        {
            if (SetField(ref confirmSessionTransfer, value))
                RaiseSessionTransferCommand();
        }
    }

    private void InitializeSessionTransferCommand() =>
        TransferSessionCommand = new AsyncUiAction(TransferSessionAsync, CanTransferSession);

    private void RaiseSessionTransferCommand() =>
        (TransferSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();

    private bool CanTransferSession()
    {
        var selected = SelectedActiveSession?.Session;
        var destination = SelectedTransferDestination?.Station;
        var reason = SessionTransferReasonText?.Trim() ?? string.Empty;
        return !IsBusy &&
            CanOperateSessions &&
            selected is { Status: SessionStatusContract.Active or SessionStatusContract.Paused } &&
            destination is { Status: StationStatusContract.Available } &&
            destination.Id != selected.StationId &&
            HasTransferDestination(destination.Id, destination.Type) &&
            (selected.Status is SessionStatusContract.Active or SessionStatusContract.Paused) &&
            ConfirmSessionTransfer &&
            reason.Length is >= 5 and <= 300 &&
            !reason.Any(char.IsControl);
    }

    private bool HasTransferDestination(Guid stationId, StationTypeContract type) =>
        TransferDestinationChoices.Any(x => x.Id == stationId && x.Station.Type == type);

    private void OnSelectedActiveSessionChangedForTransfer()
    {
        selectedTransferDestination = null;
        OnPropertyChanged(nameof(SelectedTransferDestination));
        SessionTransferReasonText = string.Empty;
        ConfirmSessionTransfer = false;
        RefreshTransferDestinationChoices();
    }

    private void RefreshTransferDestinationChoices()
    {
        var previousDestinationId = selectedTransferDestination?.Id;
        var selectedSession = SelectedActiveSession?.Session;
        var sourceStation = selectedSession is null
            ? null
            : StationChoices.FirstOrDefault(x => x.Id == selectedSession.StationId)?.Station;

        TransferDestinationChoices.Clear();
        if (selectedSession is not null &&
            selectedSession.Status is SessionStatusContract.Active or SessionStatusContract.Paused &&
            sourceStation is not null)
        {
            var compatible = StationChoices
                .Where(x => x.Id != sourceStation.Id)
                .Where(x => x.Station.Type == sourceStation.Type)
                .Where(x => x.Station.Status == StationStatusContract.Available)
                .Where(x => x.Station.Type != StationTypeContract.Pc || x.Station.AgentOnline)
                .OrderBy(x => x.Code, StringComparer.Ordinal)
                .ToArray();

            foreach (var choice in compatible)
                TransferDestinationChoices.Add(choice);
        }

        selectedTransferDestination = previousDestinationId.HasValue
            ? TransferDestinationChoices.FirstOrDefault(x => x.Id == previousDestinationId.Value)
            : null;
        OnPropertyChanged(nameof(SelectedTransferDestination));
        RaiseSessionTransferCommand();
    }

    private void ResetSessionTransferState()
    {
        selectedTransferDestination = null;
        SessionTransferReasonText = string.Empty;
        ConfirmSessionTransfer = false;
        TransferDestinationChoices.Clear();
        OnPropertyChanged(nameof(SelectedTransferDestination));
    }

    private async Task TransferSessionAsync()
    {
        if (!CanTransferSession() || SelectedActiveSession is null || SelectedTransferDestination is null)
            return;

        var selectedSession = SelectedActiveSession.Session;
        var destination = SelectedTransferDestination.Station;
        var reason = SessionTransferReasonText.Trim();
        var fingerprint = $"{selectedSession.Id:D}|{selectedSession.Version}|{destination.Id:D}|{reason}";
        var key = GetKey("session.transfer", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var transferred = await api.TransferSessionAsync(
                selectedSession.Id,
                new TransferSessionRequest(destination.Id, selectedSession.Version, reason, true),
                key);
            pendingKeys.Remove("session.transfer");
            await RefreshAsync();
            SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == transferred.Id);
            SessionTransferReasonText = string.Empty;
            ConfirmSessionTransfer = false;
            StatusMessage = TransferSuccessText(destination.Code);
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = ErrorText(ex.Code);
            await TryRefreshAfterTransferFailureAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await TryRefreshAfterTransferFailureAsync();
            var current = ActiveSessions.FirstOrDefault(x => x.Session.Id == selectedSession.Id);
            if (current?.Session.StationId == destination.Id)
            {
                pendingKeys.Remove("session.transfer");
                SelectedActiveSession = current;
                SessionTransferReasonText = string.Empty;
                ConfirmSessionTransfer = false;
                StatusMessage = TransferSuccessText(destination.Code);
                ErrorMessage = string.Empty;
            }
            else
            {
                // Keep the idempotency key and form values: the outcome is still ambiguous.
                ErrorMessage = Text(
                    "نتیجهٔ انتقال نامشخص است؛ وضعیت را تازه‌سازی کنید و همان درخواست را بدون تغییر دوباره بررسی کنید.",
                    "Transfer outcome is unknown. Refresh and reconcile before changing or resubmitting the request.");
            }
        }
        catch
        {
            ErrorMessage = Text("انتقال Session انجام نشد.", "The Session transfer failed.");
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task TryRefreshAfterTransferFailureAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // The original command may have committed; retain its idempotency key and form state.
        }
    }

    private static string TransferSuccessText(string destinationCode) =>
        Text(
            $"Session به ایستگاه {destinationCode} منتقل شد؛ تعرفه و زمان‌سنجی حفظ شدند.",
            $"Session transferred to station {destinationCode}; tariff and timing were preserved.");
}
