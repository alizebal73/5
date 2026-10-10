using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.System;

namespace GameNet.Desktop.Shell;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IGameNetServerClient serverClient;
    private readonly Func<string, string> localize;
    private bool isRefreshingServer;
    private string serverStatusText;
    private string serverStatusKind = "checking";
    private string serverVersionText;
    private string lastCheckedText;

    public MainWindowViewModel(
        IGameNetServerClient serverClient,
        Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(serverClient);
        this.serverClient = serverClient;
        this.localize = localize ?? (key => key);

        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
        [
            CreateNavigationItem("overview", "Nav.Overview"),
            CreateNavigationItem("stations", "Nav.Stations"),
            CreateNavigationItem("customers", "Nav.Customers"),
            CreateNavigationItem("sessions", "Nav.Sessions"),
            CreateNavigationItem("billing", "Nav.Billing"),
            CreateNavigationItem("wallet", "Nav.Wallet"),
            CreateNavigationItem("inventory", "Nav.Inventory"),
            CreateNavigationItem("buffet", "Nav.Buffet"),
            CreateNavigationItem("vip", "Nav.Vip"),
            CreateNavigationItem("reports", "Nav.Reports"),
            CreateNavigationItem("agents", "Nav.Agents"),
            CreateNavigationItem("settings", "Nav.Settings"),
            CreateNavigationItem("approvals", "Nav.Approvals"),
            CreateNavigationItem("backup-recovery", "Nav.BackupRecovery"),
            CreateNavigationItem("audit", "Nav.Audit"),
            CreateNavigationItem("diagnostics", "Nav.Diagnostics")
        ]);

        NavigationItems[0].IsSelected = true;
        NavigateCommand = new RelayCommand(parameter => NavigateTo(parameter as string ?? string.Empty));
        RefreshServerCommand = new RelayCommand(
            _ => _ = RefreshServerStatusAsync(),
            _ => !IsRefreshingServer);

        serverStatusText = this.localize("Status.Checking");
        serverVersionText = this.localize("Common.Unknown");
        lastCheckedText = this.localize("Common.NotChecked");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public RelayCommand NavigateCommand { get; }

    public RelayCommand RefreshServerCommand { get; }

    public string ActiveSectionKey =>
        NavigationItems.FirstOrDefault(item => item.IsSelected)?.Key ?? "overview";

    public string ActiveSectionTitle =>
        NavigationItems.FirstOrDefault(item => item.IsSelected)?.Title ?? localize("Nav.Overview");

    public string ActiveSectionDescription =>
        localize(IsOverview ? "Home.Description" : "Section.Description");

    public bool IsOverview => string.Equals(ActiveSectionKey, "overview", StringComparison.Ordinal);

    public bool IsSectionPlaceholder => !IsOverview;

    public bool IsRefreshingServer
    {
        get => isRefreshingServer;
        private set
        {
            if (isRefreshingServer == value)
                return;

            isRefreshingServer = value;
            OnPropertyChanged();
            RefreshServerCommand?.RaiseCanExecuteChanged();
        }
    }

    public string ServerStatusText
    {
        get => serverStatusText;
        private set => SetField(ref serverStatusText, value);
    }

    public string ServerStatusKind
    {
        get => serverStatusKind;
        private set => SetField(ref serverStatusKind, value);
    }

    public string ServerVersionText
    {
        get => serverVersionText;
        private set => SetField(ref serverVersionText, value);
    }

    public string LastCheckedText
    {
        get => lastCheckedText;
        private set => SetField(ref lastCheckedText, value);
    }

    public void NavigateTo(string sectionKey)
    {
        var selected = NavigationItems.FirstOrDefault(
            item => string.Equals(item.Key, sectionKey, StringComparison.Ordinal));

        if (selected is null)
            return;

        foreach (var item in NavigationItems)
            item.IsSelected = ReferenceEquals(item, selected);

        OnPropertyChanged(nameof(ActiveSectionKey));
        OnPropertyChanged(nameof(ActiveSectionTitle));
        OnPropertyChanged(nameof(ActiveSectionDescription));
        OnPropertyChanged(nameof(IsOverview));
        OnPropertyChanged(nameof(IsSectionPlaceholder));
    }

    public async Task RefreshServerStatusAsync()
    {
        if (IsRefreshingServer)
            return;

        IsRefreshingServer = true;
        ServerStatusKind = "checking";
        ServerStatusText = localize("Status.Checking");

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var envelope = await serverClient.GetHealthAsync(timeout.Token);
            var health = envelope.Data;

            ServerVersionText = string.IsNullOrWhiteSpace(health.Version)
                ? localize("Common.Unknown")
                : health.Version;

            if (string.Equals(health.Status, HealthStatuses.Healthy, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(health.Readiness, HealthStatuses.Ready, StringComparison.OrdinalIgnoreCase))
            {
                ServerStatusKind = "ready";
                ServerStatusText = localize("Status.Ready");
            }
            else
            {
                ServerStatusKind = "notready";
                ServerStatusText = localize("Status.NotReady");
            }
        }
        catch (Exception)
        {
            // Keep diagnostics non-sensitive; the UI shows a stable state, not exception details.
            ServerStatusKind = "offline";
            ServerStatusText = localize("Status.Unreachable");
            ServerVersionText = localize("Common.Unknown");
        }
        finally
        {
            LastCheckedText = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
            IsRefreshingServer = false;
        }
    }

    private NavigationItemViewModel CreateNavigationItem(string key, string resourceKey)
        => new(key, localize(resourceKey));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class NavigationItemViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public NavigationItemViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string Title { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
                return;

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Predicate<object?>? canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
