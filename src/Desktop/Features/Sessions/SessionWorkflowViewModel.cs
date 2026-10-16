using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel : ISessionWorkflowPort
{
    private readonly IGameNetServerClient api;
    private readonly Func<string, bool> hasPermission;
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

    public SessionWorkflowViewModel(IGameNetServerClient api, Func<string, bool> hasPermission)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.hasPermission = hasPermission ?? throw new ArgumentNullException(nameof(hasPermission));

        RefreshCommand = new AsyncUiAction(RefreshAsync, () => !IsBusy && CanUseWorkspace);
        CreateTariffCommand = new AsyncUiAction(CreateTariffAsync, () => !IsBusy && CanManageTariffs &&
            !string.IsNullOrWhiteSpace(TariffCode) && !string.IsNullOrWhiteSpace(TariffName) && TryParseToman(HourlyRateText, out var rate) && rate > 0);
        StartSessionCommand = new AsyncUiAction(StartSessionAsync, () => !IsBusy && CanOperateSessions &&
            SelectedStation is { Type: StationTypeContract.Pc, Status: StationStatusContract.Available } &&
            SelectedTariff is { IsActive: true, StationType: StationTypeContract.Pc } &&
            SelectedStation is not null &&
            !ActiveSessions.Any(x => x.Session.StationId == SelectedStation.Id) &&
            !PendingInvoices.Any(x => x.Invoice.StationId == SelectedStation.Id));
        EndSessionCommand = new AsyncUiAction(EndSessionAsync, () => !IsBusy && CanOperateSessions &&
            SelectedActiveSession?.Session.Status == SessionStatusContract.Active);
        CheckoutCashCommand = new AsyncUiAction(CheckoutCashAsync, () => !IsBusy && CanCheckout &&
            !unresolvedCashCheckoutInvoiceId.HasValue &&
            SelectedPendingInvoice?.Invoice.Status == InvoiceStatusContract.PendingPayment &&
            TryParseToman(AmountReceivedText, out var received) &&
            SelectedPendingInvoice is not null &&
            received >= SelectedPendingInvoice.Invoice.AmountToman &&
            ConfirmCashCheckout);
        ReverseCashSaleCommand = new AsyncUiAction(ReverseCashSaleAsync, () => !IsBusy && CanReverseCashSale &&
            !unresolvedCashSaleReversalInvoiceId.HasValue &&
            SelectedRecentCashCheckout is { Outcome.Status: InvoiceStatusContract.Paid, Outcome.PaymentId: not null } &&
            IsValidCashSaleReversalReason(CashSaleReversalReasonText) &&
            ConfirmCashSaleReversal);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StationResponse> PcStations { get; } = [];
    public ObservableCollection<TariffResponse> Tariffs { get; } = [];
    public ObservableCollection<ActiveSessionListItem> ActiveSessions { get; } = [];
    public ObservableCollection<PendingInvoiceListItem> PendingInvoices { get; } = [];
    public ObservableCollection<CashCheckoutOutcomeListItem> RecentCashCheckouts { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand CreateTariffCommand { get; }
    public ICommand StartSessionCommand { get; }
    public ICommand EndSessionCommand { get; }
    public ICommand CheckoutCashCommand { get; }

    public bool CanManageTariffs => hasPermission(Permissions.SettingsWrite);
    public bool CanOperateSessions => hasPermission(Permissions.SessionsOperate);
    public bool CanCheckout => hasPermission(Permissions.BillingWrite);
    public bool CanReverseCashSale => hasPermission(Permissions.BillingCashReverse);
    public bool CanUseWorkspace => CanManageTariffs || CanOperateSessions || CanCheckout || CanReverseCashSale;
    public bool CanReadStations => hasPermission(Permissions.StationsRead);

    public StationResponse? SelectedStation
    {
        get => selectedStation;
        set { if (SetField(ref selectedStation, value)) RaiseCommands(); }
    }

    public TariffResponse? SelectedTariff
    {
        get => selectedTariff;
        set { if (SetField(ref selectedTariff, value)) RaiseCommands(); }
    }

    public ActiveSessionListItem? SelectedActiveSession
    {
        get => selectedActiveSession;
        set { if (SetField(ref selectedActiveSession, value)) RaiseCommands(); }
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

            ErrorMessage = string.Empty;
            if (CanReadStations)
            {
                StationResponse[] stations;
                try
                {
                    stations = (await api.GetStationsAsync())
                        .Where(x => x.Type == StationTypeContract.Pc)
                        .OrderBy(x => x.Code, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (Exception ex) when (!CanOperateSessions &&
                    (ex is GameNetApiException || ex is HttpRequestException || ex is TaskCanceledException))
                {
                    // A station-code lookup enriches invoice rows; it must not block a cashier from loading invoices.
                    stations = [];
                    stationLabelsUnavailable = true;
                }

                stationLabels.Clear();
                foreach (var station in stations)
                    stationLabels[station.Id] = station.Code;

                if (CanOperateSessions)
                {
                    Replace(PcStations, stations, SelectedStation?.Id, x => x.Id, out var currentStation);
                    SelectedStation = currentStation;
                }
                else
                {
                    // Station labels may be visible to a cashier without granting Session operations.
                    PcStations.Clear();
                    SelectedStation = null;
                }
            }
            else
            {
                stationLabels.Clear();
                PcStations.Clear();
                SelectedStation = null;
            }

            if (CanOperateSessions || CanManageTariffs)
            {
                var tariffs = await api.GetTariffsAsync(StationTypeContract.Pc);
                Replace(Tariffs, tariffs, SelectedTariff?.Id, x => x.Id, out var currentTariff);
                SelectedTariff = currentTariff;
            }
            else
            {
                Tariffs.Clear();
                SelectedTariff = null;
            }

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

            if (CanCheckout || CanReverseCashSale)
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

            OnPropertyChanged(nameof(CanManageTariffs));
            OnPropertyChanged(nameof(CanOperateSessions));
            OnPropertyChanged(nameof(CanCheckout));
            OnPropertyChanged(nameof(CanReverseCashSale));
            OnPropertyChanged(nameof(CanReadStations));
            OnPropertyChanged(nameof(CanUseWorkspace));
            StatusMessage = stationLabelsUnavailable
                ? Text("کد ایستگاه در دسترس نبود؛ در صورت نمایش صورتحساب، شناسهٔ ایستگاه استفاده می‌شود.", "Station codes were unavailable; invoice rows will use station IDs.")
                : Text("وضعیت Sessionها، تعرفه‌ها و صورتحساب‌ها به‌روزرسانی شد.", "Sessions, tariffs and pending invoices were refreshed.");
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = ErrorText(ex.Code);
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
        OnPropertyChanged(nameof(CanCheckout));
        OnPropertyChanged(nameof(CanReverseCashSale));
        OnPropertyChanged(nameof(CanReadStations));
        OnPropertyChanged(nameof(CanUseWorkspace));
        RaiseCommands();
    }

    public void Reset()
    {
        stationLabels.Clear();
        PcStations.Clear();
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
        CashSaleReversalReasonText = string.Empty;
        ConfirmCashSaleReversal = false;
        RaiseCommands();
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanManageTariffs));
        OnPropertyChanged(nameof(CanOperateSessions));
        OnPropertyChanged(nameof(CanCheckout));
        OnPropertyChanged(nameof(CanReverseCashSale));
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

        var fingerprint = $"{TariffCode.Trim()}|{TariffName.Trim()}|{rate}|{IsDefaultTariff}";
        var key = GetKey("tariff.create", fingerprint);
        BeginBusy();
        try
        {
            var created = await api.CreateTariffAsync(
                new CreateTariffRequest(TariffCode, TariffName, StationTypeContract.Pc, rate, IsDefaultTariff),
                key);
            pendingKeys.Remove("tariff.create");
            await RefreshAsync();
            SelectedTariff = Tariffs.FirstOrDefault(x => x.Id == created.Id);
            StatusMessage = Text($"تعرفهٔ {created.Code} با نرخ {FormatToman(created.HourlyRateToman)} ساخته شد.",
                $"Tariff {created.Code} was created at {FormatToman(created.HourlyRateToman)}.");
        }
        catch (GameNetApiException ex) { ErrorMessage = ErrorText(ex.Code); }
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
        var fingerprint = $"{stationId:D}|{tariffId:D}|walk-in";
        var key = GetKey("session.start", fingerprint);
        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var session = await api.StartSessionAsync(
                new StartSessionRequest(stationId, tariffId, null), key);
            pendingKeys.Remove("session.start");
            await RefreshAsync();
            SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == session.Id);
            StatusMessage = Text($"Session برای {stationCode} شروع شد. کنترل قفل PC هنوز جداگانه در حال امکان‌سنجی است.",
                $"Session started for {stationCode}. PC lock/unlock control is not included yet.");
        }
        catch (GameNetApiException ex) { ErrorMessage = ErrorText(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("نتیجهٔ شروع Session نامشخص است؛ ابتدا تازه‌سازی کنید.", "Session start result is unknown; refresh to reconcile before retrying."); }
        catch { ErrorMessage = Text("شروع Session انجام نشد.", "Session start failed."); }
        finally { EndBusy(); }
    }

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
            await RefreshAsync();
            SelectedPendingInvoice = PendingInvoices.FirstOrDefault(x => x.Invoice.InvoiceId == invoice.InvoiceId);
            StatusMessage = Text($"صورتحساب صادر شد: {FormatToman(invoice.AmountToman)} تومان.",
                $"Invoice issued: {FormatToman(invoice.AmountToman)} Toman.");
        }
        catch (GameNetApiException ex) { ErrorMessage = ErrorText(ex.Code); }
        catch (HttpRequestException) { ErrorMessage = Text("نتیجهٔ پایان Session نامشخص است؛ تازه‌سازی کنید تا صورتحساب ثبت‌شده پیدا شود.", "Session end result is unknown; refresh to find any issued invoice.");
        }
        catch { ErrorMessage = Text("پایان Session انجام نشد.", "Ending the Session failed."); }
        finally { EndBusy(); }
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

    private static string FormatDurationDisplayText(long elapsedTicks)
    {
        if (elapsedTicks <= 0)
            return Text("مدت زمان در دسترس نیست", "Duration unavailable");

        var duration = TimeSpan.FromTicks(elapsedTicks);
        var totalHours = checked((long)duration.Days * 24 + duration.Hours);
        var value = $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        return Text($"مدت: {value}", $"Duration: {value}");
    }

    private static string FormatAmountDisplayText(long amountToman) =>
        Text($"{FormatToman(amountToman)} تومان", $"{FormatToman(amountToman)} Toman");

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

        var builder = new System.Text.StringBuilder(text.Length);
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

    private static string FormatToman(long amount) =>
        amount.ToString("N0", CultureInfo.CurrentCulture);

    private string ErrorText(string code) => code switch
    {
        "security.https_required" => Text("برای اتصال به سرور راه دور HTTPS لازم است.", "HTTPS is required for a remote Server."),
        "tariffs.forbidden" or "sessions.forbidden" or "billing.forbidden" => Text("مجوز انجام این عملیات را ندارید.", "You do not have permission for this operation."),
        "tariffs.code_exists" => Text("کد این تعرفه قبلاً ثبت شده است.", "This tariff code already exists."),
        "sessions.station_unavailable" => Text("این ایستگاه آمادهٔ شروع Session نیست.", "This station is not available for a new Session."),
        "sessions.agent_unavailable" => Text("Agent این PC متصل یا دارای اعتبار/heartbeat معتبر نیست؛ اتصال Agent را بررسی کنید. Session شروع نشد.", "This PC's Agent is not connected or has no active credential and fresh heartbeat/lease. Check the Agent connection; the Session was not started."),
        "sessions.timing_recovery_required" or "sessions.invalid_clock" => Text("پیوستگی زمان جلسه قابل تأیید نیست؛ صورتحساب صادر نشد و بازیابی زمان لازم است.", "Session timing continuity could not be verified. No invoice was created; timing recovery is required."),
        "sessions.station_has_active_session" => Text("برای این PC یک Session فعال ثبت شده است.", "This PC already has an active Session."),
        "sessions.station_pending_settlement" => Text("ابتدا صورتحساب Session قبلی را تسویه کنید.", "Pay the previous Session invoice before starting another Session on this PC."),
        "sessions.tariff_station_mismatch" => Text("این تعرفه برای PC نیست.", "This tariff is not for PCs."),
        "sessions.tariff_not_found" => Text("تعرفهٔ فعال پیدا نشد.", "The active tariff was not found."),
        "sessions.version_conflict" or "billing.session_version_conflict" => Text("اطلاعات تغییر کرده است؛ تازه‌سازی کنید.", "The data changed; refresh before trying again."),
        "billing.cash_insufficient" => Text("وجه دریافتی کمتر از صورتحساب است.", "Cash received is less than the invoice amount."),
        "billing.reversal_method_unsupported" => Text("این عملیات فقط برای پرداخت نقدی پشتیبانی می‌شود.", "This operation supports cash payments only."),
        "billing.reversal_invalid" => Text("علت یا اطلاعات برگشت معتبر نیست.", "The reversal reason or request is invalid."),
        "billing.reversal_confirmation_mismatch" => Text("صورتحساب، پرداخت یا مبلغ تأییدشده با اطلاعات سرور تطبیق ندارد.", "The confirmed invoice, payment, or amount does not match Server state."),
        "billing.invoice_not_pending" => Text("این صورتحساب در انتظار پرداخت نیست.", "This invoice is not awaiting payment."),
        "idempotency.in_flight" => Text("این درخواست هنوز در حال پردازش است؛ کمی بعد وضعیت را تازه‌سازی کنید.", "This request is still processing; refresh its status before retrying."),
        "idempotency.key_reused" => Text("کلید تکرار درخواست با اطلاعات متفاوت استفاده شده است.", "The idempotency key was reused with different request data."),
        _ => Text("عملیات انجام نشد؛ پیام خطای پایدار ثبت شد. وضعیت را تازه‌سازی کنید.", "The operation failed. Refresh to reconcile the current state.")
    };

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
        (EndSessionCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (CheckoutCashCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (ReverseCashSaleCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
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
