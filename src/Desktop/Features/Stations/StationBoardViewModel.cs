using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Stations;

public sealed class StationBoardViewModel : INotifyPropertyChanged
{
    private readonly IGameNetServerClient api;
    private readonly Dictionary<string, string> pendingKeys = new(StringComparer.Ordinal);
    private StationBoardItem? selectedStation;
    private string newCode = "", newName = "", newType = "PC", renameName = "", deviceId = "", errorMessage = "", statusMessage = "";
    private bool isBusy;

    public StationBoardViewModel(IGameNetServerClient api)
    {
        this.api = api;
        StationTypes = new[] { "PC", "PS5", "Foosball" };
        RefreshCommand = new AsyncUiAction(RefreshAsync, () => !IsBusy);
        CreateCommand = new AsyncUiAction(CreateAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(NewCode) && !string.IsNullOrWhiteSpace(NewName));
        RenameCommand = new AsyncUiAction(RenameAsync, () => !IsBusy && SelectedStation is not null && !string.IsNullOrWhiteSpace(RenameName));
        BindAgentCommand = new AsyncUiAction(BindAgentAsync, () => !IsBusy && SelectedStation is not null && !string.IsNullOrWhiteSpace(DeviceId));
        MaintenanceCommand = new AsyncUiAction(() => ChangeStatusAsync(StationStatusContract.Maintenance), () => !IsBusy && SelectedStation is not null);
        AvailableCommand = new AsyncUiAction(() => ChangeStatusAsync(StationStatusContract.Available), () => !IsBusy && SelectedStation is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<StationBoardItem> Stations { get; } = new();
    public IReadOnlyList<string> StationTypes { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand BindAgentCommand { get; }
    public ICommand MaintenanceCommand { get; }
    public ICommand AvailableCommand { get; }

    public StationBoardItem? SelectedStation
    {
        get => selectedStation;
        set
        {
            if (!SetField(ref selectedStation, value)) return;
            RenameName = value?.Name ?? "";
            DeviceId = value?.Station.AgentDeviceId ?? "";
        }
    }

    public string NewCode { get => newCode; set => SetField(ref newCode, value); }
    public string NewName { get => newName; set => SetField(ref newName, value); }
    public string NewType { get => newType; set => SetField(ref newType, value); }
    public string RenameName { get => renameName; set => SetField(ref renameName, value); }
    public string DeviceId { get => deviceId; set => SetField(ref deviceId, value); }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }
    public bool IsBusy { get => isBusy; private set => SetField(ref isBusy, value); }

    public async Task RefreshAsync()
    {
        ErrorMessage = "";
        IsBusy = true;
        try
        {
            await LoadCoreAsync();
            StatusMessage = Text("فهرست ایستگاه‌ها به‌روزرسانی شد.", "Station list refreshed.");
        }
        catch (GameNetApiException ex) { ErrorMessage = DescribeError(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("ارتباط با سرور برقرار نشد.", "Could not connect to the Server."); }
        catch (TaskCanceledException) { ErrorMessage = Text("پاسخی از سرور دریافت نشد.", "The Server did not respond in time."); }
        catch { ErrorMessage = Text("دریافت فهرست ایستگاه‌ها ناموفق بود.", "Could not load stations."); }
        finally { IsBusy = false; }
    }

    public void ClearLocalState()
    {
        Stations.Clear();
        SelectedStation = null;
        pendingKeys.Clear();
        ErrorMessage = "";
        StatusMessage = "";
        NewCode = "";
        NewName = "";
        RenameName = "";
        DeviceId = "";
    }

    private async Task CreateAsync()
    {
        if (!TryGetType(NewType, out var type)) { ErrorMessage = Text("نوع ایستگاه معتبر نیست.", "Select a supported station type."); return; }
        ErrorMessage = "";
        const string scope = "create";
        IsBusy = true;
        try
        {
            var station = await api.CreateStationAsync(new CreateStationRequest(NewCode.Trim(), NewName.Trim(), type), Key(scope));
            await LoadCoreAsync(station.Id);
            pendingKeys.Remove(scope);
            NewCode = "";
            NewName = "";
            StatusMessage = Text("ایستگاه ساخته شد.", "Station created.");
        }
        catch (GameNetApiException ex) { ClearKeyForDefinitiveFailure(scope, ex); ErrorMessage = DescribeError(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("ارتباط قطع شد؛ همان درخواست را دوباره امتحان کنید.", "Connection failed; retry the same request."); }
        catch (TaskCanceledException) { ErrorMessage = Text("پاسخی از سرور دریافت نشد؛ درخواست را دوباره امتحان کنید.", "The Server timed out; retry the request."); }
        catch { ErrorMessage = Text("ساخت ایستگاه ناموفق بود.", "Station creation failed."); }
        finally { IsBusy = false; }
    }

    private Task RenameAsync()
    {
        var row = SelectedStation;
        if (row is null) return Task.CompletedTask;
        var requestedName = RenameName.Trim();
        return MutateAsync($"rename:{row.Id:D}", row.Id,
            key => api.RenameStationAsync(row.Id, new RenameStationRequest(requestedName, row.Station.Version), key),
            Text("نام ایستگاه تغییر کرد.", "Station renamed."));
    }

    private Task BindAgentAsync()
    {
        var row = SelectedStation;
        if (row is null) return Task.CompletedTask;
        var requestedDevice = DeviceId.Trim();
        return MutateAsync($"bind:{row.Id:D}", row.Id,
            key => api.BindStationAgentAsync(row.Id, new BindStationAgentRequest(requestedDevice, row.Station.Version), key),
            Text("Agent به ایستگاه متصل شد.", "Agent bound to station."));
    }

    private Task ChangeStatusAsync(StationStatusContract status)
    {
        var row = SelectedStation;
        if (row is null) return Task.CompletedTask;
        var message = status == StationStatusContract.Maintenance
            ? Text("وضعیت اداری روی تعمیرات تنظیم شد.", "Administrative status set to maintenance.")
            : Text("وضعیت اداری روی آماده تنظیم شد.", "Administrative status set to available.");
        return MutateAsync($"status:{row.Id:D}:{(int)status}", row.Id,
            key => api.SetStationStatusAsync(row.Id, new SetStationStatusRequest(status, row.Station.Version), key), message);
    }

    private async Task MutateAsync(string scope, Guid stationId, Func<string, Task<StationResponse>> action, string successMessage)
    {
        ErrorMessage = "";
        IsBusy = true;
        try
        {
            await action(Key(scope));
            await LoadCoreAsync(stationId);
            pendingKeys.Remove(scope);
            StatusMessage = successMessage;
        }
        catch (GameNetApiException ex) { ClearKeyForDefinitiveFailure(scope, ex); ErrorMessage = DescribeError(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("ارتباط قطع شد؛ همان عملیات را دوباره امتحان کنید.", "Connection failed; retry the same operation."); }
        catch (TaskCanceledException) { ErrorMessage = Text("پاسخی از سرور دریافت نشد؛ عملیات را دوباره امتحان کنید.", "The Server timed out; retry the operation."); }
        catch { ErrorMessage = Text("عملیات ایستگاه ناموفق بود.", "Station operation failed."); }
        finally { IsBusy = false; }
    }

    private async Task LoadCoreAsync(Guid? selectId = null)
    {
        var keepId = selectId ?? SelectedStation?.Id;
        var list = await api.GetStationsAsync();
        Stations.Clear();
        foreach (var item in list.OrderBy(x => x.Code, StringComparer.Ordinal))
            Stations.Add(new StationBoardItem(item));
        SelectedStation = Stations.FirstOrDefault(x => x.Id == keepId) ?? Stations.FirstOrDefault();
    }

    private string Key(string scope)
    {
        if (!pendingKeys.TryGetValue(scope, out var key)) pendingKeys[scope] = key = Guid.NewGuid().ToString("N");
        return key;
    }

    private void ClearKeyForDefinitiveFailure(string scope, GameNetApiException ex)
    {
        if (ex.StatusCode is >= 400 and < 500 && ex.StatusCode is not (408 or 429))
            pendingKeys.Remove(scope);
    }

    private static bool TryGetType(string name, out StationTypeContract type)
    {
        type = name switch { "PC" => StationTypeContract.Pc, "PS5" => StationTypeContract.Ps5, "Foosball" => StationTypeContract.Foosball, _ => default };
        return name is "PC" or "PS5" or "Foosball";
    }

    private static string DescribeError(string code) => code switch
    {
        "stations.code_exists" => Text("کد ایستگاه تکراری است.", "That station code already exists."),
        "stations.invalid" => Text("اطلاعات ایستگاه معتبر نیست.", "Station details are invalid."),
        "stations.not_found" => Text("ایستگاه پیدا نشد؛ فهرست را تازه‌سازی کنید.", "Station was not found; refresh the list."),
        "stations.agent_not_provisioned" => Text("برای این Device ID اعتبارنامه فعال Agent وجود ندارد.", "No active Agent credential exists for that device ID."),
        "stations.device_already_bound" => Text("این Agent به ایستگاه دیگری متصل است.", "This Agent is already bound to another station."),
        "stations.version_conflict" => Text("ایستگاه تغییر کرده؛ تازه‌سازی کنید و دوباره تلاش کنید.", "The station changed; refresh and retry."),
        "stations.invalid_transition" => Text("این تغییر وضعیت مجاز نیست.", "This status transition is not allowed."),
        "idempotency.key_reused" => Text("کلید درخواست با داده‌های متفاوت استفاده شده؛ دوباره اقدام کنید.", "The request key was reused with different data; retry the action."),
        "idempotency.in_flight" => Text("این عملیات هنوز در حال اجراست.", "This operation is already in progress."),
        "security.https_required" => Text("برای سرور راه دور، HTTPS لازم است.", "HTTPS is required for remote Server connections."),
        "auth.identity_not_found" or "auth.session_not_found" => Text("نشست اپراتور معتبر نیست؛ دوباره وارد شوید.", "The operator session is no longer valid; sign in again."),
        _ => Text("درخواست به سرور ناموفق بود.", "The Server rejected the request.")
    };

    private static string Text(string fa, string en) =>
        System.Globalization.CultureInfo.CurrentUICulture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? en : fa;

    private void RaiseCommands()
    {
        if (RefreshCommand is AsyncUiAction a) a.RaiseCanExecuteChanged();
        if (CreateCommand is AsyncUiAction b) b.RaiseCanExecuteChanged();
        if (RenameCommand is AsyncUiAction c) c.RaiseCanExecuteChanged();
        if (BindAgentCommand is AsyncUiAction d) d.RaiseCanExecuteChanged();
        if (MaintenanceCommand is AsyncUiAction e) e.RaiseCanExecuteChanged();
        if (AvailableCommand is AsyncUiAction f) f.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        RaiseCommands();
        return true;
    }
}

public sealed class StationBoardItem(StationResponse station)
{
    public StationResponse Station { get; } = station;
    public Guid Id => Station.Id;
    public string Code => Station.Code;
    public string Name => Station.Name;
    public string TypeText => Station.Type switch { StationTypeContract.Pc => "PC", StationTypeContract.Ps5 => "PS5", StationTypeContract.Foosball => "Foosball", _ => "Unknown" };
    public string StatusText => Station.Status switch
    {
        StationStatusContract.Available => Text("آماده", "Available"),
        StationStatusContract.Disabled => Text("غیرفعال", "Disabled"),
        StationStatusContract.Maintenance => Text("تعمیرات", "Maintenance"),
        StationStatusContract.RecoveryRequired => Text("نیازمند بازیابی", "Recovery required"),
        _ => "Unknown"
    };
    public string RuntimeText => Station.AgentOnline ? Text("آنلاین", "Online") : Text("آفلاین", "Offline");
    public string AgentText => string.IsNullOrWhiteSpace(Station.AgentDeviceId) ? Text("Agent متصل نیست", "No Agent bound") : Station.AgentDeviceId;
    public string HeartbeatText => Station.LastHeartbeatAtUtc.HasValue
        ? Station.LastHeartbeatAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture)
        : Text("هنوز heartbeat دریافت نشده", "No heartbeat received yet");
    public string AgentVersionText => Station.AgentVersion ?? "—";
    public string AgentStateText => Station.AgentReportedState ?? "—";
    private static string Text(string fa, string en) =>
        System.Globalization.CultureInfo.CurrentUICulture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? en : fa;
}
