using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

internal interface ISessionTransferHost
{
    IGameNetServerClient Api { get; }
    bool CanOperateSessions { get; }
    bool CanReadStations { get; }
    bool IsBusy { get; }
    ObservableCollection<SessionStationOption> StationChoices { get; }
    ObservableCollection<ActiveSessionListItem> ActiveSessions { get; }
    ActiveSessionListItem? SelectedActiveSession { get; set; }
    Task RefreshAsync();
    string GetKey(string scope, string fingerprint);
    void RemovePendingKey(string scope);
    void BeginBusy();
    void EndBusy();
    string ErrorMessage { set; }
    string StatusMessage { set; }
}

internal sealed class SessionTransferCoordinator : INotifyPropertyChanged
{
    private readonly ISessionTransferHost host;

    public SessionTransferCoordinator(ISessionTransferHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        InitializeSessionTransferCommand();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public void NotifyHostStateChanged() => RaiseSessionTransferCommand();

    private void RaiseSessionTransferCommand() =>
        (TransferSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();

    private bool CanTransferSession()
    {
        var selected = host.SelectedActiveSession?.Session;
        var destination = SelectedTransferDestination?.Station;
        var reason = SessionTransferReasonText?.Trim() ?? string.Empty;
        return !host.IsBusy &&
            host.CanOperateSessions &&
            host.CanReadStations &&
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

    public void OnSelectedActiveSessionChangedForTransfer()
    {
        selectedTransferDestination = null;
        OnPropertyChanged(nameof(SelectedTransferDestination));
        SessionTransferReasonText = string.Empty;
        ConfirmSessionTransfer = false;
        RefreshTransferDestinationChoices();
    }

    public void RefreshTransferDestinationChoices()
    {
        var previousDestinationId = selectedTransferDestination?.Id;
        var selectedSession = host.SelectedActiveSession?.Session;
        var sourceStation = selectedSession is null
            ? null
            : host.StationChoices.FirstOrDefault(x => x.Id == selectedSession.StationId)?.Station;

        TransferDestinationChoices.Clear();
        if (selectedSession is not null &&
            selectedSession.Status is (SessionStatusContract.Active or SessionStatusContract.Paused) &&
            sourceStation is not null)
        {
            var compatible = host.StationChoices
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

    public void ResetSessionTransferState()
    {
        selectedTransferDestination = null;
        SessionTransferReasonText = string.Empty;
        ConfirmSessionTransfer = false;
        TransferDestinationChoices.Clear();
        OnPropertyChanged(nameof(SelectedTransferDestination));
    }

    private async Task TransferSessionAsync()
    {
        if (!CanTransferSession() || host.SelectedActiveSession is null || SelectedTransferDestination is null)
            return;

        var selectedSession = host.SelectedActiveSession.Session;
        var destination = SelectedTransferDestination.Station;
        var reason = SessionTransferReasonText.Trim();
        var fingerprint = $"{selectedSession.Id:D}|{selectedSession.Version}|{destination.Id:D}|{reason}";
        var key = host.GetKey("session.transfer", fingerprint);
        host.BeginBusy();
        host.ErrorMessage = string.Empty;
        try
        {
            var transferred = await host.Api.TransferSessionAsync(
                selectedSession.Id,
                new TransferSessionRequest(destination.Id, selectedSession.Version, reason, true),
                key);
            host.RemovePendingKey("session.transfer");
            await host.RefreshAsync();
            host.SelectedActiveSession = host.ActiveSessions.FirstOrDefault(x => x.Session.Id == transferred.Id);
            SessionTransferReasonText = string.Empty;
            ConfirmSessionTransfer = false;
            host.StatusMessage = TransferSuccessText(destination.Code);
        }
        catch (GameNetApiException ex)
        {
            var errorMessage = SessionWorkflowErrorText.For(ex.Code);
            await TryRefreshAfterTransferFailureAsync();
            host.ErrorMessage = errorMessage;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await TryRefreshAfterTransferFailureAsync();
            var current = host.ActiveSessions.FirstOrDefault(x => x.Session.Id == selectedSession.Id);
            if (current?.Session.StationId == destination.Id)
            {
                host.RemovePendingKey("session.transfer");
                host.SelectedActiveSession = current;
                SessionTransferReasonText = string.Empty;
                ConfirmSessionTransfer = false;
                host.StatusMessage = TransferSuccessText(destination.Code);
                host.ErrorMessage = string.Empty;
            }
            else
            {
                // Keep the idempotency key and form values: the outcome is still ambiguous.
                host.ErrorMessage = Text(
                    "نتیجهٔ تغییر تخصیص نامشخص است؛ وضعیت را تازه‌سازی کنید و همان درخواست را بدون تغییر دوباره بررسی کنید.",
                    "Session reassignment outcome is unknown. Refresh and reconcile before changing or resubmitting the request.");
            }
        }
        catch
        {
            host.ErrorMessage = Text("انتقال Session انجام نشد.", "The Session transfer failed.");
        }
        finally
        {
            host.EndBusy();
        }
    }

    private async Task TryRefreshAfterTransferFailureAsync()
    {
        try
        {
            await host.RefreshAsync();
        }
        catch
        {
            // The original command may have committed; retain its idempotency key and form state.
        }
    }

    private static string TransferSuccessText(string destinationCode) =>
        Text(
            $"تخصیص Session به ایستگاه {destinationCode} تغییر کرد؛ تعرفه و زمان حفظ شدند. کنترل واقعی رایانه در این عملیات انجام نمی‌شود.",
            $"Session assignment moved to station {destinationCode}; tariff and time were preserved. This command does not perform actual PC lock/unlock.");

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string Text(string fa, string en) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? fa : en;
}
