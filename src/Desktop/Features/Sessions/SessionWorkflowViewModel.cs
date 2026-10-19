using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Desktop.Features.Customers;
using GameNet.Desktop.Features.Wallet;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Contracts.V1.Wallet;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel : ISessionWorkflowPort
{
    private readonly IGameNetServerClient api;
    private readonly Func<string, bool> hasPermission;
    private readonly PrepaidStationEntryCoordinator prepaidStationEntryCoordinator;
    private readonly Dictionary<string, (string Fingerprint, string Key)> pendingKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> stationLabels = [];
    private StationResponse? selectedStation;
    private TariffResponse? selectedTariff;
    private ActiveSessionListItem? selectedActiveSession;
    private PendingInvoiceListItem? selectedPendingInvoice;
    private string tariffCode = "PC-STANDARD";
    private string tariffName = "PC Standard";
    private string hourlyRateText = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;
    private bool isDefaultTariff = true;
    private bool isBusy;
    private int busyDepth;

    public SessionWorkflowViewModel(
        IGameNetServerClient api,
        Func<string, bool> hasPermission,
        ICustomerEntryPrompt? customerEntryPrompt = null)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.hasPermission = hasPermission ?? throw new ArgumentNullException(nameof(hasPermission));
        prepaidStationEntryCoordinator = new PrepaidStationEntryCoordinator(
            api, customerEntryPrompt, BeginBusy, EndBusy,
            message => ErrorMessage = message, SessionWorkflowErrorText.For,
            OnPrepaidSessionStartedAsync);
        CustomerCredentials = new CustomerCredentialsViewModel(api, hasPermission, RefreshManagedCustomerAfterCredentialsAsync);
        InitializeStationTypeOptions();

        RefreshCommand = new AsyncUiAction(RefreshAsync, () => !IsBusy && CanUseWorkspace);
        CreateTariffCommand = new AsyncUiAction(CreateTariffAsync, () => !IsBusy && CanManageTariffs &&
            Enum.IsDefined(TariffStationType) &&
            !string.IsNullOrWhiteSpace(TariffCode) && !string.IsNullOrWhiteSpace(TariffName) && TryParseToman(HourlyRateText, out var rate) && rate > 0);
        StartSessionCommand = new AsyncUiAction(StartSessionAsync, () => !IsBusy && CanOperateSessions &&
            SelectedStation is { Status: StationStatusContract.Available } station &&
            station.Type is (StationTypeContract.Pc or StationTypeContract.Ps5 or StationTypeContract.Foosball) &&
            SelectedTariff is { IsActive: true } tariff &&
            tariff.StationType == station.Type &&
            (SelectedCustomer is null || SelectedCustomer.Status == GameNet.Shared.Contracts.V1.Customers.CustomerStatusContract.Active) &&
            !ActiveSessions.Any(x => x.Session.StationId == station.Id) &&
            !PendingInvoices.Any(x => x.Invoice.StationId == station.Id));
        StartPrepaidSessionFromStationCommand = new AsyncParameterUiAction(
            prepaidStationEntryCoordinator.StartAsync,
            parameter => !IsBusy && CanOperateSessions && CanReadCustomers &&
                prepaidStationEntryCoordinator.CanPrompt &&
                parameter is StationResponse { Type: StationTypeContract.Pc, Status: StationStatusContract.Available });
        PauseSessionCommand = new AsyncUiAction(PauseSessionAsync, () => !IsBusy && CanOperateSessions &&
            SelectedActiveSession?.Session.Status == SessionStatusContract.Active);
        ResumeSessionCommand = new AsyncUiAction(ResumeSessionAsync, () => !IsBusy && CanOperateSessions &&
            SelectedActiveSession?.Session.Status == SessionStatusContract.Paused);
        EndSessionCommand = new AsyncUiAction(EndSessionAsync, () => !IsBusy && CanOperateSessions &&
            SelectedActiveSession?.Session.Status is SessionStatusContract.Active or SessionStatusContract.Paused);
        CheckoutCashCommand = new AsyncUiAction(CheckoutCashAsync, CanCheckoutCashNow);
        ReverseCashSaleCommand = new AsyncUiAction(ReverseCashSaleAsync, () => !IsBusy && CanReverseCashSale &&
            !unresolvedCashSaleReversalInvoiceId.HasValue &&
            SelectedRecentCashCheckout is { Outcome.Status: InvoiceStatusContract.Paid, Outcome.PaymentId: not null } &&
            IsValidCashSaleReversalReason(CashSaleReversalReasonText) &&
            ConfirmCashSaleReversal);
        CorrectCashTenderCommand = new AsyncUiAction(CorrectCashTenderAsync, CanCorrectCashTenderNow);
        WalletTopUp = new WalletTopUpViewModel(api, hasPermission);
        InitializeCustomerCommands();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IWalletTopUpPort WalletTopUp { get; }

    public ObservableCollection<StationResponse> PcStations { get; } = [];
    public ObservableCollection<TariffResponse> Tariffs { get; } = [];
    public ObservableCollection<ActiveSessionListItem> ActiveSessions { get; } = [];
    public ObservableCollection<PendingInvoiceListItem> PendingInvoices { get; } = [];
    public ObservableCollection<CashCheckoutOutcomeListItem> RecentCashCheckouts { get; } = [];

    public ICustomerCredentialsPort CustomerCredentials { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CreateTariffCommand { get; }
    public ICommand StartSessionCommand { get; }
    public ICommand StartPrepaidSessionFromStationCommand { get; }
    public ICommand EndSessionCommand { get; }
    public ICommand PauseSessionCommand { get; }
    public ICommand ResumeSessionCommand { get; }
    public ICommand CheckoutCashCommand { get; }

    public bool CanManageTariffs => hasPermission(Permissions.SettingsWrite);
    public bool CanOperateSessions => hasPermission(Permissions.SessionsOperate);
    public bool CanCheckout => hasPermission(Permissions.BillingWrite);
    public bool CanReverseCashSale => hasPermission(Permissions.BillingCashReverse);
    public bool CanCorrectCashTender => hasPermission(Permissions.BillingCashTenderCorrect);
    public bool CanUseWorkspace => CanManageTariffs || CanOperateSessions || CanCheckout || CanReverseCashSale || CanCorrectCashTender || CanReadCustomers || CanWriteCustomers;
    public bool CanReadStations => hasPermission(Permissions.StationsRead);

    public StationResponse? SelectedStation
    {
        get => selectedStation;
        set { if (SetField(ref selectedStation, value)) OnSelectedStationChanged(); }
    }

    public TariffResponse? SelectedTariff
    {
        get => selectedTariff;
        set { if (SetField(ref selectedTariff, value)) RaiseCommands(); }
    }

    public ActiveSessionListItem? SelectedActiveSession
    {
        get => selectedActiveSession;
        set
        {
            if (SetField(ref selectedActiveSession, value))
            {
                OnPropertyChanged(nameof(CanRecoverSessionTiming));
                RaiseCommands();
            }
        }
    }

    public string TariffCode { get => tariffCode; set { if (SetField(ref tariffCode, value)) RaiseCommands(); } }
    public string TariffName { get => tariffName; set { if (SetField(ref tariffName, value)) RaiseCommands(); } }
    public string HourlyRateText { get => hourlyRateText; set { if (SetField(ref hourlyRateText, value)) RaiseCommands(); } }
    public bool IsDefaultTariff { get => isDefaultTariff; set { if (SetField(ref isDefaultTariff, value)) RaiseCommands(); } }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
                RaiseCommands();
        }
    }

    public string SelectedInvoiceAmountText => SelectedPendingInvoice is null
        ? string.Empty
        : FormatToman(SelectedPendingInvoice.Invoice.AmountToman);

    public string SelectedSessionText => SelectedActiveSession is null
        ? string.Empty
        : $"{SelectedActiveSession.Session.TariffNameSnapshot} — {FormatToman(SelectedActiveSession.Session.TariffHourlyRateTomanSnapshot)}";

    public async Task RefreshAsync()
    {
        if (!CanUseWorkspace)
            return;

        BeginBusy();
        var stationLabelsUnavailable = false;
        try
        {
            // If the previous checkout's HTTP result and its outcome lookup both failed,
            // keep checkout disabled until this explicit refresh confirms Paid or Pending.
            if (unresolvedCashCheckoutInvoiceId is Guid unresolvedInvoiceId)
            {
                CashCheckoutOutcomeResponse outcome;
                try
                {
                    outcome = await api.GetCashCheckoutOutcomeAsync(unresolvedInvoiceId);
                }
                catch
                {
                    ErrorMessage = Text(
                        "نتیجهٔ پرداخت هنوز قابل تأیید نیست. تا روشن‌شدن وضعیت سرور، پرداخت را دوباره ثبت نکنید.",
                        "Checkout outcome is still unverified. Do not record the cash again until the Server outcome can be confirmed.");
                    return;
                }

                if (IsConfirmedPaidCheckoutOutcome(outcome))
                {
                    unresolvedCashCheckoutInvoiceId = null;
                    pendingKeys.Remove("billing.cash");
                    RaiseCommands();
                }
                else if (IsConfirmedPendingCheckoutOutcome(outcome))
                {
                    unresolvedCashCheckoutInvoiceId = null;
                    AmountReceivedText = string.Empty;
                    ConfirmCashCheckout = false;
                    RaiseCommands();
                }
                else
                {
                    ErrorMessage = Text(
                        "نتیجهٔ پرداخت سازگار و قابل تأیید نیست. پرداخت مجدد مسدود است؛ ابتدا نتیجه را با سرور تطبیق دهید.",
                        "The checkout outcome is not consistent enough to confirm. Retry is blocked; reconcile with the Server first.");
                    return;
                }
            }

            if (unresolvedCashSaleReversalInvoiceId is Guid unresolvedReversalInvoiceId)
            {
                CashCheckoutOutcomeResponse reversalOutcome;
                try
                {
                    reversalOutcome = await api.GetCashCheckoutOutcomeAsync(unresolvedReversalInvoiceId);
                }
                catch
                {
                    ErrorMessage = Text(
                        "نتیجهٔ برگشت فروش هنوز قابل تأیید نیست؛ تا تطبیق با سرور، درخواست را تکرار نکنید.",
                        "Cash-sale reversal is still unverified. Do not retry until its durable Server outcome can be reconciled.");
                    return;
                }

                if (IsConfirmedReversedCashSaleOutcome(reversalOutcome))
                {
                    unresolvedCashSaleReversalInvoiceId = null;
                    pendingKeys.Remove("billing.cash-reversal");
                    RaiseCommands();
                }
                else if (IsConfirmedPaidCashSaleWithoutReversal(reversalOutcome))
                {
                    unresolvedCashSaleReversalInvoiceId = null;
                    CashSaleReversalReasonText = string.Empty;
                    ConfirmCashSaleReversal = false;
                    RaiseCommands();
                }
                else
                {
                    ErrorMessage = Text(
                        "وضعیت صورتحساب و برگشت با هم سازگار نیست؛ عملیات دوباره مسدود است.",
                        "Invoice and reversal records do not reconcile. The reversal command remains blocked.");
                    return;
                }
            }

            if (unresolvedCashTenderCorrection is not null &&
                !await ReconcileCashTenderCorrectionBeforeRefreshAsync())
                return;

            ErrorMessage = string.Empty;
            stationLabelsUnavailable = await RefreshStationAndTariffChoicesAsync();

            if (CanOperateSessions)
            {
                var sessions = await api.GetActiveSessionsAsync();
                var sessionItems = sessions.Select(session => new ActiveSessionListItem(
                    session,
                    GetStationLabel(session.StationId),
                    FormatStartedAtDisplayText(session.StartedAtUtc))).ToArray();
                Replace(ActiveSessions, sessionItems, SelectedActiveSession?.Id, x => x.Id, out var currentSession);
                SelectedActiveSession = currentSession;
            }
            else
            {
                ActiveSessions.Clear();
                SelectedActiveSession = null;
            }

            if (CanCheckout)
            {
                var invoices = await api.GetPendingInvoicesAsync();
                var invoiceItems = invoices.Select(invoice => new PendingInvoiceListItem(
                    invoice,
                    GetStationLabel(invoice.StationId),
                    FormatDurationDisplayText(invoice.ElapsedTicks),
                    FormatAmountDisplayText(invoice.AmountToman))).ToArray();
                Replace(PendingInvoices, invoiceItems, SelectedPendingInvoice?.Id, x => x.Id, out var currentInvoice);
                SelectedPendingInvoice = currentInvoice;
            }
            else
            {
                PendingInvoices.Clear();
                SelectedPendingInvoice = null;
            }

            if (CanCheckout || CanReverseCashSale || CanCorrectCashTender)
            {
                var selectedRecentInvoiceId = SelectedRecentCashCheckout?.Id;
                var recentOutcomes = await api.GetRecentCashCheckoutsAsync();
                RecentCashCheckouts.Clear();
                foreach (var outcome in recentOutcomes)
                {
                    RecentCashCheckouts.Add(new CashCheckoutOutcomeListItem(
                        outcome,
                        GetStationLabel(outcome.StationId),
                        FormatInvoiceIdentityDisplayText(outcome.InvoiceId),
                        FormatAmountDisplayText(outcome.AmountToman),
                        FormatCashOutcomeDetailsDisplayText(outcome),
                        FormatPaidAtDisplayText(outcome.PaidAtUtc)));
                }

                SelectedRecentCashCheckout = selectedRecentInvoiceId.HasValue
                    ? RecentCashCheckouts.FirstOrDefault(x => x.Id == selectedRecentInvoiceId.Value)
                    : null;
            }
            else
            {
                RecentCashCheckouts.Clear();
                SelectedRecentCashCheckout = null;
            }

            await RefreshCustomersAsync();
            OnPropertyChanged(nameof(CanManageTariffs));
            OnPropertyChanged(nameof(CanReadCustomers));
            OnPropertyChanged(nameof(CanWriteCustomers));
            OnPropertyChanged(nameof(CanOperateSessions));
            OnPropertyChanged(nameof(CanCheckout));
            OnPropertyChanged(nameof(CanReverseCashSale));
            OnPropertyChanged(nameof(CanCorrectCashTender));
            OnPropertyChanged(nameof(CanReadStations));
            OnPropertyChanged(nameof(CanUseWorkspace));
            StatusMessage = stationLabelsUnavailable
                ? Text("کد ایستگاه در دسترس نبود؛ در صورت نمایش صورتحساب، شناسهٔ ایستگاه استفاده می‌شود.", "Station codes were unavailable; invoice rows will use station IDs.")
                : Text("وضعیت Sessionها، تعرفه‌ها و صورتحساب‌ها به‌روزرسانی شد.", "Sessions, tariffs and pending invoices were refreshed.");
            if (!string.IsNullOrWhiteSpace(cashTenderCorrectionRefreshMessage))
            {
                StatusMessage = cashTenderCorrectionRefreshMessage;
                cashTenderCorrectionRefreshMessage = null;
            }
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = SessionWorkflowErrorText.For(ex.Code);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("ارتباط با سرور برقرار نشد؛ وضعیت مالی تغییر نکرده است.", "Could not reach the Server; no financial result was confirmed.");
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = Text("پاسخ سرور دریافت نشد؛ برای بررسی وضعیت دوباره تازه‌سازی کنید.", "The Server response timed out; refresh to reconcile the current state.");
        }
        catch
        {
            ErrorMessage = Text("به‌روزرسانی انجام نشد. وضعیت سرور را بررسی کنید.", "Refresh failed. Check the Server status.");
        }
        finally
        {
            EndBusy();
        }
    }

    public void NotifyPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanManageTariffs));
        OnPropertyChanged(nameof(CanOperateSessions));
        OnPropertyChanged(nameof(CanRecoverSessionTiming));
        OnPropertyChanged(nameof(CanCheckout));
        OnPropertyChanged(nameof(CanReverseCashSale));
        OnPropertyChanged(nameof(CanCorrectCashTender));
        OnPropertyChanged(nameof(CanReadStations));
        OnPropertyChanged(nameof(CanUseWorkspace));
        RaiseCommands();
        EnforceCustomerPermissions();
        WalletTopUp.NotifyPermissionsChanged();
    }

    public void Reset()
    {
        stationLabels.Clear();
        PcStations.Clear();
        ResetStationAndTariffChoices();
        TariffStationType = StationTypeContract.Pc;
        Tariffs.Clear();
        ActiveSessions.Clear();
        PendingInvoices.Clear();
        RecentCashCheckouts.Clear();
        SelectedRecentCashCheckout = null;
        SelectedStation = null;
        SelectedTariff = null;
        SelectedActiveSession = null;
        SelectedPendingInvoice = null;
        pendingKeys.Clear();
        unresolvedCashCheckoutInvoiceId = null;
        unresolvedCashSaleReversalInvoiceId = null;
        unresolvedCashTenderCorrection = null;
        CorrectedCashReceivedText = string.Empty;
        CashTenderCorrectionReasonText = string.Empty;
        ConfirmCashTenderCorrection = false;
        CashSaleReversalReasonText = string.Empty;
        ConfirmCashSaleReversal = false;
        ReconciledElapsedMinutesText = string.Empty;
        TimingRecoveryReasonText = string.Empty;
        ConfirmTimingRecovery = false;
        timingRecoveryRequiredSessionId = null;
        ResetCustomerState();
        WalletTopUp.Reset();
        OnPropertyChanged(nameof(CanRecoverSessionTiming));
        RaiseCommands();
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanManageTariffs));
        OnPropertyChanged(nameof(CanOperateSessions));
        OnPropertyChanged(nameof(CanCheckout));
        OnPropertyChanged(nameof(CanReverseCashSale));
        OnPropertyChanged(nameof(CanCorrectCashTender));
        OnPropertyChanged(nameof(CanReadStations));
        OnPropertyChanged(nameof(CanUseWorkspace));
    }

    private async Task CreateTariffAsync()
    {
        ErrorMessage = string.Empty;
        if (!TryParseToman(HourlyRateText, out var rate) || rate <= 0)
        {
            ErrorMessage = Text("مبلغ تعرفه باید عدد صحیح مثبت به تومان باشد.", "Hourly rate must be a positive whole number of Toman.");
            return;
        }

        var fingerprint = $"{TariffCode.Trim()}|{TariffName.Trim()}|{(int)TariffStationType}|{rate}|{IsDefaultTariff}";
        var key = GetKey("tariff.create", fingerprint);
        BeginBusy();
        try
        {
            var created = await api.CreateTariffAsync(
                new CreateTariffRequest(TariffCode, TariffName, TariffStationType, rate, IsDefaultTariff),
                key);
            pendingKeys.Remove("tariff.create");
            await RefreshAsync();
            SelectedTariff = Tariffs.FirstOrDefault(x => x.Id == created.Id);
            StatusMessage = Text($"تعرفهٔ {created.Code} با نرخ {FormatToman(created.HourlyRateToman)} ساخته شد.",
                $"Tariff {created.Code} was created at {FormatToman(created.HourlyRateToman)}.");
        }
        catch (GameNetApiException ex) { ErrorMessage = SessionWorkflowErrorText.For(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("نتیجهٔ ساخت تعرفه نامشخص است؛ تازه‌سازی کنید و از ارسال درخواست جدید خودداری کنید.", "Tariff creation result is unknown; refresh before issuing a new request."); }
        catch { ErrorMessage = Text("ساخت تعرفه انجام نشد.", "Tariff creation failed."); }
        finally { EndBusy(); }
    }

    private async Task StartSessionAsync()
    {
        if (SelectedStation is null || SelectedTariff is null)
            return;

        // Preserve the user's original target across the asynchronous request and refresh.
        // The selection control remains editable while the command is in flight.
        var stationId = SelectedStation.Id;
        var stationCode = SelectedStation.Code;
        var tariffId = SelectedTariff.Id;
        var (customerId, customerCode, fingerprint) = BuildSessionStartContext(stationId, tariffId);
        var key = GetKey("session.start", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var session = await api.StartSessionAsync(
                new StartSessionRequest(stationId, tariffId, customerId), key);
            pendingKeys.Remove("session.start");
            await RefreshAsync();
            SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == session.Id);
            StatusMessage = SessionStartedMessage(stationCode, customerCode);
        }
        catch (GameNetApiException ex) { ErrorMessage = SessionWorkflowErrorText.For(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("نتیجهٔ شروع Session نامشخص است؛ ابتدا تازه‌سازی کنید.", "Session start result is unknown; refresh to reconcile before retrying."); }
        catch { ErrorMessage = Text("شروع Session انجام نشد.", "Session start failed."); }
        finally { EndBusy(); }
    }

    private async Task OnPrepaidSessionStartedAsync(
        StationResponse station,
        GameNet.Shared.Contracts.V1.Customers.CustomerResponse customer,
        SessionResponse session)
    {
        await RefreshAsync();
        SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == session.Id);
        StatusMessage = Text(
            $"مشتری {customer.Code} روی {station.Code} وارد شد؛ اعتبار رزروشده {FormatToman(session.PrepaidReservedAmountToman ?? 0)} تومان.",
            $"Customer {customer.Code} started on {station.Code}; {FormatToman(session.PrepaidReservedAmountToman ?? 0)} Toman was reserved.");
    }
    private string GetStationLabel(Guid stationId)
    {
        return stationLabels.TryGetValue(stationId, out var stationCode) &&
               !string.IsNullOrWhiteSpace(stationCode)
            ? stationCode
            : Text($"ایستگاه {stationId:D}", $"Station {stationId:D}");
    }

    private static string FormatStartedAtDisplayText(DateTimeOffset startedAtUtc)
    {
        var localTime = startedAtUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        return Text($"شروع: {localTime}", $"Started: {localTime}");
    }

    private static string FormatToman(long amount) =>
        amount.ToString("N0", CultureInfo.CurrentCulture);

    private static string Text(string fa, string en) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? fa : en;

    private void BeginBusy()
    {
        busyDepth = checked(busyDepth + 1);
        IsBusy = busyDepth > 0;
    }

    private void EndBusy()
    {
        if (busyDepth <= 0)
            throw new InvalidOperationException("SESSION_WORKFLOW_BUSY_DEPTH_UNBALANCED");

        busyDepth--;
        IsBusy = busyDepth > 0;
    }

    private void RaiseCommands()
    {
        (RefreshCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (CreateTariffCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (StartSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (StartPrepaidSessionFromStationCommand as AsyncParameterUiAction)?.RaiseCanExecuteChanged();
        (EndSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        RaiseLifecycleCommands();
        (CheckoutCashCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (ReverseCashSaleCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (CorrectCashTenderCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (ReconcileSessionTimingCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        RaiseCustomerCommands();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
