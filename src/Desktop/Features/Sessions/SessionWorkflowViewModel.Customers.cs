using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    private CustomerResponse? selectedCustomer;
    private CustomerResponse? selectedManagedCustomer;
    private string customerSearchText = string.Empty;
    private string sessionCustomerSearchText = string.Empty;
    private string customerCodeText = string.Empty;
    private string customerNameText = string.Empty;
    private string customerNicknameText = string.Empty;
    private string customerPhoneText = string.Empty;
    private string customerStatusReasonText = string.Empty;
    private bool customerIncludeInactive = true;
    private int customerRefreshGeneration;
    private int walletBalanceRefreshGeneration;
    private string walletTopUpAmountText = string.Empty;
    private string walletCashReceivedText = string.Empty;
    private string walletBalanceText = string.Empty;

    public ObservableCollection<CustomerResponse> Customers { get; } = [];
    public ObservableCollection<CustomerResponse> AvailableCustomers { get; } = [];
    public ICommand SearchCustomersCommand { get; private set; } = null!;
    public ICommand SearchSessionCustomersCommand { get; private set; } = null!;
    public ICommand NewCustomerCommand { get; private set; } = null!;
    public ICommand UseWalkInCustomerCommand { get; private set; } = null!;
    public ICommand CreateCustomerCommand { get; private set; } = null!;
    public ICommand UpdateCustomerCommand { get; private set; } = null!;
    public ICommand ActivateCustomerCommand { get; private set; } = null!;
    public ICommand DeactivateCustomerCommand { get; private set; } = null!;
    public ICommand BlockCustomerCommand { get; private set; } = null!;
    public ICommand WalletTopUpCommand { get; private set; } = null!;

    public bool CanReadCustomers => hasPermission(Permissions.CustomersRead);
    public bool IsNewCustomer => SelectedManagedCustomer is null;
    public bool CanWriteCustomers => hasPermission(Permissions.CustomersWrite);
    public bool CanTopUpWallet => hasPermission(Permissions.WalletWrite);

    public CustomerResponse? SelectedCustomer
    {
        get => selectedCustomer;
        set { if (SetField(ref selectedCustomer, value)) RaiseCommands(); }
    }

    public CustomerResponse? SelectedManagedCustomer
    {
        get => selectedManagedCustomer;
        set
        {
            var previousCustomerId = selectedManagedCustomer?.Id;
            if (!SetField(ref selectedManagedCustomer, value)) return;
            if (previousCustomerId != value?.Id)
                CustomerStatusReasonText = string.Empty;
            if (value is not null)
            {
                CustomerCodeText = value.Code;
                CustomerNameText = value.DisplayName ?? string.Empty;
                CustomerNicknameText = value.Nickname ?? string.Empty;
                CustomerPhoneText = value.PhoneNumber ?? string.Empty;
            }
            else
            {
                CustomerCodeText = string.Empty;
                CustomerNameText = string.Empty;
                CustomerNicknameText = string.Empty;
                CustomerPhoneText = string.Empty;
            }
            OnPropertyChanged(nameof(CustomerStatusSummaryText));
            OnPropertyChanged(nameof(IsNewCustomer));
            StartWalletBalanceRefresh(value);
            RaiseCustomerCommands();
        }
    }

    public string CustomerSearchText { get => customerSearchText; set { if (SetField(ref customerSearchText, value)) RaiseCustomerCommands(); } }
    public string SessionCustomerSearchText { get => sessionCustomerSearchText; set { if (SetField(ref sessionCustomerSearchText, value)) RaiseCustomerCommands(); } }
    public string CustomerCodeText { get => customerCodeText; set { if (SetField(ref customerCodeText, value)) RaiseCustomerCommands(); } }
    public string CustomerNameText { get => customerNameText; set { if (SetField(ref customerNameText, value)) RaiseCustomerCommands(); } }
    public string CustomerNicknameText { get => customerNicknameText; set { if (SetField(ref customerNicknameText, value)) RaiseCustomerCommands(); } }
    public string CustomerPhoneText { get => customerPhoneText; set { if (SetField(ref customerPhoneText, value)) RaiseCustomerCommands(); } }
    public string CustomerStatusReasonText { get => customerStatusReasonText; set { if (SetField(ref customerStatusReasonText, value)) RaiseCustomerCommands(); } }
    public bool CustomerIncludeInactive { get => customerIncludeInactive; set { if (SetField(ref customerIncludeInactive, value)) RaiseCustomerCommands(); } }
    public string WalletTopUpAmountText { get => walletTopUpAmountText; set { if (SetField(ref walletTopUpAmountText, value)) { OnPropertyChanged(nameof(WalletTopUpSummaryText)); RaiseCustomerCommands(); } } }
    public string WalletCashReceivedText { get => walletCashReceivedText; set { if (SetField(ref walletCashReceivedText, value)) { OnPropertyChanged(nameof(WalletTopUpSummaryText)); RaiseCustomerCommands(); } } }
    public string WalletBalanceText { get => walletBalanceText; private set => SetField(ref walletBalanceText, value); }
    public string WalletTopUpSummaryText
    {
        get
        {
            if (!TryParseToman(WalletTopUpAmountText, out var credit) || credit <= 0 ||
                !TryParseToman(WalletCashReceivedText, out var received) || received < credit)
                return string.Empty;
            var change = received - credit;
            return Text($"شارژ {FormatToman(credit)}، وجه دریافتی {FormatToman(received)}، باقی‌پول {FormatToman(change)}",
                $"Credit {FormatToman(credit)}, cash received {FormatToman(received)}, change {FormatToman(change)}");
        }
    }

    public string CustomerStatusSummaryText => SelectedManagedCustomer?.Status switch
    {
        CustomerStatusContract.Active => Text("وضعیت مشتری: فعال", "Customer status: Active"),
        CustomerStatusContract.Inactive => Text("وضعیت مشتری: غیرفعال", "Customer status: Inactive"),
        CustomerStatusContract.Blocked => Text("وضعیت مشتری: مسدود", "Customer status: Blocked"),
        _ => Text("مشتری انتخاب نشده است.", "No customer selected.")
    };

    private void InitializeCustomerCommands()
    {
        SearchCustomersCommand = new AsyncUiAction(SearchCustomersAsync, () => !IsBusy && CanReadCustomers);
        SearchSessionCustomersCommand = new AsyncUiAction(SearchSessionCustomersAsync, () => !IsBusy && CanReadCustomers);
        NewCustomerCommand = new AsyncUiAction(NewCustomerAsync, () => !IsBusy && CanWriteCustomers);
        UseWalkInCustomerCommand = new AsyncUiAction(UseWalkInCustomer, () => !IsBusy && SelectedCustomer is not null);
        CreateCustomerCommand = new AsyncUiAction(CreateCustomerAsync, () => !IsBusy && CanWriteCustomers &&
            SelectedManagedCustomer is null && !string.IsNullOrWhiteSpace(CustomerCodeText) && !string.IsNullOrWhiteSpace(CustomerNameText));
        UpdateCustomerCommand = new AsyncUiAction(UpdateCustomerProfileAsync, () => !IsBusy && CanWriteCustomers &&
            SelectedManagedCustomer is { Kind: CustomerKindContract.Registered } && !string.IsNullOrWhiteSpace(CustomerNameText));
        ActivateCustomerCommand = new AsyncUiAction(() => SetCustomerStatusAsync(CustomerStatusContract.Active),
            () => !IsBusy && CanWriteCustomers && SelectedManagedCustomer is { Status: not CustomerStatusContract.Active } && IsValidCustomerReason(CustomerStatusReasonText));
        DeactivateCustomerCommand = new AsyncUiAction(() => SetCustomerStatusAsync(CustomerStatusContract.Inactive),
            () => !IsBusy && CanWriteCustomers && SelectedManagedCustomer is { Status: not CustomerStatusContract.Inactive } && IsValidCustomerReason(CustomerStatusReasonText));
        BlockCustomerCommand = new AsyncUiAction(() => SetCustomerStatusAsync(CustomerStatusContract.Blocked),
            () => !IsBusy && CanWriteCustomers && SelectedManagedCustomer is { Status: not CustomerStatusContract.Blocked } && IsValidCustomerReason(CustomerStatusReasonText));
        WalletTopUpCommand = new AsyncUiAction(TopUpWalletAsync, () => !IsBusy && CanTopUpWallet &&
            SelectedManagedCustomer is { Kind: CustomerKindContract.Registered, Status: CustomerStatusContract.Active } &&
            TryParseToman(WalletTopUpAmountText, out var credit) && credit > 0 &&
            TryParseToman(WalletCashReceivedText, out var received) && received >= credit);
    }

    private Task UseWalkInCustomer()
    {
        SelectedCustomer = null;
        return Task.CompletedTask;
    }

    private async Task NewCustomerAsync()
    {
        SelectedManagedCustomer = null;
        CustomerSearchText = string.Empty;
        SessionCustomerSearchText = string.Empty;
        await RefreshCustomersAsync();
    }

    private async Task SearchCustomersAsync()
    {
        ErrorMessage = string.Empty;
        await RefreshCustomersAsync();
    }

    private async Task SearchSessionCustomersAsync()
    {
        ErrorMessage = string.Empty;
        await RefreshCustomersAsync();
    }

    private async Task RefreshCustomersAsync()
    {
        if (!CanReadCustomers)
        {
            customerRefreshGeneration++;
            Customers.Clear();
            AvailableCustomers.Clear();
            SelectedManagedCustomer = null;
            SelectedCustomer = null;
            return;
        }

        var generation = ++customerRefreshGeneration;
        try
        {
            var selectedManagedId = SelectedManagedCustomer?.Id;
            var selectedSessionId = SelectedCustomer?.Id;
            var rows = await api.GetCustomersAsync(CustomerSearchText, CustomerIncludeInactive);
            if (generation != customerRefreshGeneration || !CanReadCustomers) return;

            // Keep the session picker independent from the customer-manager search and
            // include-inactive filter. Searching another profile must not silently clear
            // the currently selected customer for the next Session.
            var selectableCustomers = await api.GetCustomersAsync(SessionCustomerSearchText, includeInactive: false);
            if (generation != customerRefreshGeneration || !CanReadCustomers) return;

            Replace(Customers, rows, selectedManagedId, x => x.Id, out var currentManaged);
            SelectedManagedCustomer = currentManaged;
            AvailableCustomers.Clear();
            foreach (var row in selectableCustomers.Where(x => x.Status == CustomerStatusContract.Active))
                AvailableCustomers.Add(row);
            SelectedCustomer = selectedSessionId.HasValue
                ? AvailableCustomers.FirstOrDefault(x => x.Id == selectedSessionId.Value)
                : null;
        }
        catch (GameNetApiException ex)
        {
            if (generation == customerRefreshGeneration) ErrorMessage = CustomerErrorText(ex.Code);
        }
        catch (HttpRequestException)
        {
            if (generation == customerRefreshGeneration)
                ErrorMessage = Text("دریافت فهرست مشتریان ممکن نشد؛ اتصال به سرور را بررسی کنید.", "Could not load customers. Check the Server connection.");
        }
        catch (TaskCanceledException)
        {
            if (generation == customerRefreshGeneration)
                ErrorMessage = Text("پاسخ جست‌وجوی مشتری دریافت نشد؛ وضعیت را تازه‌سازی کنید.", "Customer search timed out. Refresh before retrying.");
        }
    }

    private void StartWalletBalanceRefresh(CustomerResponse? customer)
    {
        var generation = ++walletBalanceRefreshGeneration;
        if (!CanTopUpWallet || customer is not { Kind: CustomerKindContract.Registered })
        {
            WalletBalanceText = Text("برای دیدن موجودی، مشتری ثبت‌شده را انتخاب کنید.", "Select a registered customer to view Wallet balance.");
            return;
        }
        WalletBalanceText = Text("در حال دریافت موجودی کیف پول…", "Loading Wallet balance…");
        _ = LoadWalletBalanceAsync(customer.Id, generation);
    }

    private async Task LoadWalletBalanceAsync(Guid customerId, int generation)
    {
        try
        {
            var balance = await api.GetWalletBalanceAsync(customerId);
            if (generation != walletBalanceRefreshGeneration || SelectedManagedCustomer?.Id != customerId) return;
            WalletBalanceText = Text($"موجودی کیف پول: {FormatToman(balance.BalanceToman)}", $"Wallet balance: {FormatToman(balance.BalanceToman)}");
        }
        catch (GameNetApiException ex)
        {
            if (generation == walletBalanceRefreshGeneration)
                WalletBalanceText = WalletErrorText(ex.Code);
        }
        catch (HttpRequestException)
        {
            if (generation == walletBalanceRefreshGeneration)
                WalletBalanceText = Text("موجودی دریافت نشد؛ اتصال به سرور را بررسی کنید.", "Could not load Wallet balance. Check the Server connection.");
        }
        catch (TaskCanceledException)
        {
            if (generation == walletBalanceRefreshGeneration)
                WalletBalanceText = Text("دریافت موجودی زمان‌بر شد؛ دوباره تازه‌سازی کنید.", "Wallet balance request timed out. Refresh before retrying.");
        }
        catch
        {
            if (generation == walletBalanceRefreshGeneration)
                WalletBalanceText = Text("موجودی کیف پول در دسترس نیست.", "Wallet balance is unavailable.");
        }
    }

    private async Task TopUpWalletAsync()
    {
        if (SelectedManagedCustomer is not { Kind: CustomerKindContract.Registered, Status: CustomerStatusContract.Active } target ||
            !TryParseToman(WalletTopUpAmountText, out var credit) || credit <= 0 ||
            !TryParseToman(WalletCashReceivedText, out var received) || received < credit)
            return;

        ErrorMessage = string.Empty;
        var fingerprint = $"{target.Id:D}|{target.Version}|{credit}|{received}";
        var key = GetKey("wallet.cash-top-up", fingerprint);
        BeginBusy();
        try
        {
            var result = await api.CashTopUpWalletAsync(
                target.Id, new WalletCashTopUpRequest(credit, received, target.Version), key);
            pendingKeys.Remove("wallet.cash-top-up");
            WalletTopUpAmountText = string.Empty;
            WalletCashReceivedText = string.Empty;
            StatusMessage = Text(
                $"کیف پول مشتری {target.Code} به مبلغ {FormatToman(result.AmountCreditedToman)} شارژ شد؛ باقی‌پول {FormatToman(result.ChangeGivenToman)}.",
                $"Customer {target.Code} Wallet credited by {FormatToman(result.AmountCreditedToman)}; change {FormatToman(result.ChangeGivenToman)}.");
            await LoadWalletBalanceAsync(target.Id, ++walletBalanceRefreshGeneration);
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = WalletErrorText(ex.Code);
        }
        catch (HttpRequestException)
        {
            await LoadWalletBalanceAsync(target.Id, ++walletBalanceRefreshGeneration);
            ErrorMessage = Text(
                "پاسخ شارژ دریافت نشد. مبلغ‌ها را تغییر نده؛ ابتدا موجودی را بررسی کن. تلاش دوباره با همان مبلغ، همان درخواست را امن تطبیق می‌دهد.",
                "The top-up response was lost. Do not change the amounts; check the balance first. Retrying with the same amounts safely reconciles the same request.");
        }
        catch (TaskCanceledException)
        {
            await LoadWalletBalanceAsync(target.Id, ++walletBalanceRefreshGeneration);
            ErrorMessage = Text(
                "پاسخ شارژ زمان‌بر شد. مبلغ‌ها را تغییر نده؛ موجودی را بررسی کن و فقط همان درخواست را تطبیق بده.",
                "The top-up response timed out. Do not change the amounts; check balance and reconcile the same request.");
        }
        catch
        {
            ErrorMessage = Text("شارژ کیف پول انجام نشد؛ وضعیت سرور را تازه‌سازی کنید.", "Wallet top-up failed. Refresh and reconcile Server state.");
        }
        finally { EndBusy(); }
    }

    private string WalletErrorText(string code) => code switch
    {
        "wallet.forbidden" => Text("مجوز شارژ کیف پول را ندارید.", "You do not have permission to top up Wallets."),
        "wallet.customer_not_found" => Text("مشتری ثبت‌شده پیدا نشد.", "Registered customer was not found."),
        "wallet.customer_inactive" => Text("فقط مشتری فعال و غیرمسدود قابل شارژ است.", "Only active, unblocked customers can be topped up."),
        "wallet.customer_version_conflict" => Text("اطلاعات مشتری تغییر کرده؛ فهرست را تازه‌سازی و دوباره بررسی کن.", "Customer data changed. Refresh and review before retrying."),
        "wallet.invalid" => Text("مبلغ شارژ یا وجه دریافتی معتبر نیست.", "Credit or cash-received amount is invalid."),
        "wallet.balance_overflow" => Text("موجودی از محدودهٔ مجاز سیستم بیشتر می‌شود.", "Wallet balance would exceed the supported range."),
        "idempotency.in_flight" => Text("درخواست شارژ هنوز در حال پردازش است؛ وضعیت را تازه‌سازی کنید.", "Top-up request is still processing; refresh before retrying."),
        "idempotency.key_reused" => Text("درخواست با کلید قبلی و مبلغ متفاوت ارسال شده است؛ وضعیت را بررسی کنید.", "The idempotency key was reused with different amounts; reconcile the result."),
        _ => Text("شارژ کیف پول انجام نشد؛ وضعیت سرور را بررسی کنید.", "Wallet top-up failed. Check Server state.")
    };

    private async Task CreateCustomerAsync()
    {
        ErrorMessage = string.Empty;
        var code = CustomerCodeText.Trim();
        var name = CustomerNameText.Trim();
        var nickname = EmptyToNull(CustomerNicknameText);
        var phone = EmptyToNull(CustomerPhoneText);
        var fingerprint = $"{code.ToUpperInvariant()}|{name}|{nickname}|{phone}";
        var key = GetKey("customer.create", fingerprint);
        BeginBusy();
        try
        {
            var created = await api.CreateCustomerAsync(new CreateCustomerRequest(code, name, nickname, phone), key);
            pendingKeys.Remove("customer.create");
            CustomerSearchText = created.Code;
            await RefreshCustomersAsync();
            SelectedManagedCustomer = Customers.FirstOrDefault(x => x.Id == created.Id);
            StatusMessage = Text($"مشتری {created.Code} ثبت شد.", $"Customer {created.Code} was created.");
        }
        catch (GameNetApiException ex) { ErrorMessage = CustomerErrorText(ex.Code); }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("نتیجهٔ ثبت مشتری نامشخص است؛ ابتدا جست‌وجو و وضعیت سرور را بررسی کنید.", "Customer creation outcome is unknown; search and reconcile before retrying.");
        }
        catch { ErrorMessage = Text("ثبت مشتری انجام نشد.", "Customer creation failed."); }
        finally { EndBusy(); }
    }

    private async Task UpdateCustomerProfileAsync()
    {
        if (SelectedManagedCustomer is null) return;
        var target = SelectedManagedCustomer;
        var name = CustomerNameText.Trim();
        var nickname = EmptyToNull(CustomerNicknameText);
        var phone = EmptyToNull(CustomerPhoneText);
        var fingerprint = $"{target.Id:D}|{target.Version}|{name}|{nickname}|{phone}";
        var key = GetKey("customer.update", fingerprint);
        BeginBusy();
        try
        {
            var updated = await api.UpdateCustomerProfileAsync(target.Id,
                new UpdateCustomerRequest(name, nickname, phone, target.Version), key);
            pendingKeys.Remove("customer.update");
            await RefreshCustomersAsync();
            SelectedManagedCustomer = Customers.FirstOrDefault(x => x.Id == updated.Id);
            StatusMessage = Text($"اطلاعات مشتری {updated.Code} ذخیره شد.", $"Customer {updated.Code} was updated.");
        }
        catch (GameNetApiException ex) { ErrorMessage = CustomerErrorText(ex.Code); }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("نتیجهٔ ویرایش نامشخص است؛ ابتدا مشتری را تازه‌سازی و وضعیت را تطبیق دهید.", "Customer update outcome is unknown; refresh and reconcile before retrying.");
        }
        catch { ErrorMessage = Text("ویرایش اطلاعات مشتری انجام نشد.", "Customer update failed."); }
        finally { EndBusy(); }
    }

    private async Task SetCustomerStatusAsync(CustomerStatusContract status)
    {
        if (SelectedManagedCustomer is null || !IsValidCustomerReason(CustomerStatusReasonText)) return;
        var target = SelectedManagedCustomer;
        var reason = CustomerStatusReasonText.Trim();
        var fingerprint = $"{target.Id:D}|{target.Version}|{status}|{reason}";
        var key = GetKey("customer.status", fingerprint);
        BeginBusy();
        try
        {
            var updated = await api.SetCustomerStatusAsync(target.Id,
                new SetCustomerStatusRequest(status, target.Version, reason), key);
            pendingKeys.Remove("customer.status");
        pendingKeys.Remove("wallet.cash-top-up");
        WalletTopUpAmountText = string.Empty;
        WalletCashReceivedText = string.Empty;
            await RefreshCustomersAsync();
            SelectedManagedCustomer = Customers.FirstOrDefault(x => x.Id == updated.Id);
            CustomerStatusReasonText = string.Empty;
            StatusMessage = Text($"وضعیت مشتری {updated.Code} به {StatusName(status, true)} تغییر کرد.",
                $"Customer {updated.Code} is now {StatusName(status, false)}.");
        }
        catch (GameNetApiException ex) { ErrorMessage = CustomerErrorText(ex.Code); }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("نتیجهٔ تغییر وضعیت نامشخص است؛ پیش از تلاش دوباره، وضعیت ذخیره‌شده را بررسی کنید.", "Customer status outcome is unknown; reconcile the persisted state before retrying.");
        }
        catch { ErrorMessage = Text("تغییر وضعیت مشتری انجام نشد.", "Customer status update failed."); }
        finally { EndBusy(); }
    }

    private void ResetCustomerState()
    {
        customerRefreshGeneration++;
        Customers.Clear();
        AvailableCustomers.Clear();
        SelectedCustomer = null;
        SelectedManagedCustomer = null;
        CustomerSearchText = string.Empty;
        CustomerCodeText = string.Empty;
        CustomerNameText = string.Empty;
        CustomerNicknameText = string.Empty;
        CustomerPhoneText = string.Empty;
        CustomerStatusReasonText = string.Empty;
        CustomerIncludeInactive = true;
        pendingKeys.Remove("customer.create");
        pendingKeys.Remove("customer.update");
        pendingKeys.Remove("customer.status");
        OnPropertyChanged(nameof(CustomerStatusSummaryText));
        OnPropertyChanged(nameof(CanReadCustomers));
        OnPropertyChanged(nameof(CanWriteCustomers));
        OnPropertyChanged(nameof(CanTopUpWallet));
        RaiseCustomerCommands();
    }

    private (Guid? CustomerId, string? CustomerCode, string Fingerprint) BuildSessionStartContext(Guid stationId, Guid tariffId)
    {
        var customerId = SelectedCustomer?.Id;
        var customerCode = SelectedCustomer?.Code;
        var fingerprint = $"{stationId:D}|{tariffId:D}|{(customerId?.ToString("D") ?? "walk-in")}";
        return (customerId, customerCode, fingerprint);
    }

    private string SessionStartedMessage(string stationCode, string? customerCode) => customerCode is null
        ? Text($"Session برای {stationCode} با مشتری گذری شروع شد.", $"Session started for {stationCode} with a Walk-in customer.")
        : Text($"Session برای {stationCode} و مشتری {customerCode} شروع شد.", $"Session started for {stationCode} for customer {customerCode}.");

    private void EnforceCustomerPermissions()
    {
        OnPropertyChanged(nameof(CanReadCustomers));
        OnPropertyChanged(nameof(CanWriteCustomers));
        OnPropertyChanged(nameof(CanTopUpWallet));
        if (!CanReadCustomers)
        {
            customerRefreshGeneration++;
            Customers.Clear();
            AvailableCustomers.Clear();
            SelectedManagedCustomer = null;
            SelectedCustomer = null;
        }
        RaiseCustomerCommands();
    }

    private void RaiseCustomerCommands()
    {
        (SearchCustomersCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (SearchSessionCustomersCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (NewCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (UseWalkInCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (CreateCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (UpdateCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (ActivateCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (DeactivateCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (BlockCustomerCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (WalletTopUpCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsValidCustomerReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 3 and <= 256 && !reason.Any(char.IsControl);
    private static string StatusName(CustomerStatusContract status, bool persian) => status switch
    {
        CustomerStatusContract.Active => persian ? "فعال" : "Active",
        CustomerStatusContract.Inactive => persian ? "غیرفعال" : "Inactive",
        _ => persian ? "مسدود" : "Blocked"
    };
    private string CustomerErrorText(string code) => code switch
    {
        "customers.forbidden" => Text("مجوز مدیریت یا مشاهدهٔ مشتریان را ندارید.", "You do not have permission to read or manage customers."),
        "customers.invalid" => Text("کد، نام، لقب، شماره یا اطلاعات درخواست مشتری معتبر نیست.", "Customer code, name, nickname, phone or request data is invalid."),
        "customers.code_exists" => Text("این کد مشتری قبلاً ثبت شده است.", "That customer code already exists."),
        "customers.not_found" => Text("مشتری پیدا نشد؛ فهرست را تازه‌سازی کنید.", "Customer was not found. Refresh the list."),
        "customers.version_conflict" or "customers.conflict" => Text("اطلاعات مشتری تغییر کرده است؛ تازه‌سازی و بررسی دوباره لازم است.", "Customer data changed. Refresh and review before retrying."),
        "idempotency.in_flight" => Text("درخواست مشتری در حال پردازش است؛ وضعیت را تازه‌سازی کنید.", "Customer request is in progress; refresh its status."),
        "idempotency.key_reused" => Text("کلید درخواست با اطلاعات متفاوت استفاده شده است.", "The idempotency key was reused with different data."),
        _ => Text("عملیات مشتری انجام نشد؛ وضعیت سرور را تازه‌سازی کنید.", "Customer operation failed. Refresh to reconcile Server state.")
    };
}
