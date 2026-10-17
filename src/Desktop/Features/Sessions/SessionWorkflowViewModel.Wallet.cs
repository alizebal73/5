using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    private CustomerResponse? selectedWalletCustomer;
    private WalletBalanceResponse? currentWalletBalance;
    private string walletCustomerSearchText = string.Empty;
    private string walletAmountToCreditText = string.Empty;
    private string walletAmountReceivedText = string.Empty;
    private bool confirmWalletTopUp;
    private string walletTopUpOutcomeText = string.Empty;
    private int walletSearchGeneration;

    public ObservableCollection<CustomerResponse> WalletCustomers { get; } = [];
    public ICommand SearchWalletCustomersCommand { get; private set; } = null!;
    public ICommand LoadWalletBalanceCommand { get; private set; } = null!;
    public ICommand WalletCashTopUpCommand { get; private set; } = null!;

    public CustomerResponse? SelectedWalletCustomer
    {
        get => selectedWalletCustomer;
        set
        {
            if (!SetField(ref selectedWalletCustomer, value))
                return;
            if (currentWalletBalance?.CustomerId != value?.Id)
                CurrentWalletBalance = null;
            OnPropertyChanged(nameof(WalletBalanceSummaryText));
            RaiseWalletCommands();
        }
    }

    public string WalletCustomerSearchText
    {
        get => walletCustomerSearchText;
        set { if (SetField(ref walletCustomerSearchText, value)) RaiseWalletCommands(); }
    }

    public string WalletAmountToCreditText
    {
        get => walletAmountToCreditText;
        set { if (SetField(ref walletAmountToCreditText, value)) RaiseWalletCommands(); }
    }

    public string WalletAmountReceivedText
    {
        get => walletAmountReceivedText;
        set { if (SetField(ref walletAmountReceivedText, value)) RaiseWalletCommands(); }
    }

    public bool ConfirmWalletTopUp
    {
        get => confirmWalletTopUp;
        set { if (SetField(ref confirmWalletTopUp, value)) RaiseWalletCommands(); }
    }

    public WalletBalanceResponse? CurrentWalletBalance
    {
        get => currentWalletBalance;
        set
        {
            if (!SetField(ref currentWalletBalance, value))
                return;
            OnPropertyChanged(nameof(WalletBalanceSummaryText));
            RaiseWalletCommands();
        }
    }

    public string WalletBalanceSummaryText
    {
        get
        {
            if (SelectedWalletCustomer is not { } customer)
                return Text("ابتدا مشتری ثبت‌شده را انتخاب کنید.", "Select a registered customer first.");
            if (CurrentWalletBalance is not { } balance || balance.CustomerId != customer.Id)
                return Text("موجودی این مشتری هنوز از سرور دریافت نشده است.", "This customer's balance has not been loaded from the Server.");
            return Text(
                $"موجودی خریداری‌شده: {FormatToman(balance.PurchasedBalanceToman)} تومان",
                $"Purchased balance: {FormatToman(balance.PurchasedBalanceToman)} Toman");
        }
    }

    public string WalletTopUpOutcomeText
    {
        get => walletTopUpOutcomeText;
        private set => SetField(ref walletTopUpOutcomeText, value);
    }

    private bool CanSearchWalletCustomers => !IsBusy && CanWriteWallet && CanReadCustomers;
    private bool CanLoadWalletBalance => !IsBusy && CanSearchWalletCustomers &&
        SelectedWalletCustomer is { Kind: CustomerKindContract.Registered, Status: CustomerStatusContract.Active };
    private bool CanSubmitWalletTopUp =>
        !IsBusy && CanSearchWalletCustomers &&
        SelectedWalletCustomer is { Kind: CustomerKindContract.Registered, Status: CustomerStatusContract.Active } customer &&
        CurrentWalletBalance is { } balance && balance.CustomerId == customer.Id &&
        TryParseToman(WalletAmountToCreditText, out var credit) && credit > 0 &&
        TryParseToman(WalletAmountReceivedText, out var received) && received >= credit &&
        ConfirmWalletTopUp;

    private void InitializeWalletCommands()
    {
        SearchWalletCustomersCommand = new AsyncUiAction(SearchWalletCustomersAsync, () => CanSearchWalletCustomers);
        LoadWalletBalanceCommand = new AsyncUiAction(LoadWalletBalanceAsync, () => CanLoadWalletBalance);
        WalletCashTopUpCommand = new AsyncUiAction(TopUpWalletCashAsync, () => CanSubmitWalletTopUp);
    }

    private async Task SearchWalletCustomersAsync()
    {
        if (!CanSearchWalletCustomers)
        {
            ErrorMessage = Text(
                "برای انتخاب مشتری کیف پول، مجوز مشاهدهٔ مشتریان و شارژ کیف پول لازم است.",
                "Customer Read and Wallet Write permissions are required to select a Wallet customer.");
            return;
        }

        ErrorMessage = string.Empty;
        var generation = ++walletSearchGeneration;
        BeginBusy();
        try
        {
            var rows = await api.GetCustomersAsync(WalletCustomerSearchText, includeInactive: false);
            if (generation != walletSearchGeneration || !CanReadCustomers || !CanWriteWallet)
                return;
            var registered = rows.Where(x =>
                x.Kind == CustomerKindContract.Registered && x.Status == CustomerStatusContract.Active).ToArray();
            Replace(WalletCustomers, registered, SelectedWalletCustomer?.Id, x => x.Id, out var current);
            SelectedWalletCustomer = current;
            if (registered.Length == 0)
                WalletTopUpOutcomeText = Text("مشتری فعال ثبت‌شده‌ای مطابق جست‌وجو پیدا نشد.", "No active registered customer matched this search.");
        }
        catch (GameNetApiException ex) { ErrorMessage = WalletErrorText(ex.Code); }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("دریافت مشتریان انجام نشد؛ اتصال به سرور را بررسی کنید.", "Could not search customers; check the Server connection.");
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = Text("جست‌وجوی مشتری زمان‌بر شد؛ دوباره وضعیت را بررسی کنید.", "Customer search timed out; refresh and try again.");
        }
        finally { EndBusy(); }
    }

    private async Task LoadWalletBalanceAsync()
    {
        if (SelectedWalletCustomer is not { } customer || !CanWriteWallet || !CanReadCustomers)
            return;

        ErrorMessage = string.Empty;
        BeginBusy();
        try
        {
            var balance = await api.GetWalletBalanceAsync(customer.Id);
            if (SelectedWalletCustomer?.Id != customer.Id)
                return;
            CurrentWalletBalance = balance;
            WalletTopUpOutcomeText = Text(
                $"موجودی سرور برای {balance.CustomerCode} تازه شد.",
                $"Server balance refreshed for {balance.CustomerCode}.");
        }
        catch (GameNetApiException ex) { ErrorMessage = WalletErrorText(ex.Code); }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("موجودی دریافت نشد؛ اتصال به سرور را بررسی کنید.", "Could not load the balance; check the Server connection.");
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = Text("پاسخ موجودی دریافت نشد؛ دوباره تازه‌سازی کنید.", "Balance lookup timed out; refresh and retry.");
        }
        finally { EndBusy(); }
    }

    private async Task TopUpWalletCashAsync()
    {
        if (!CanSubmitWalletTopUp || SelectedWalletCustomer is not { } customer ||
            !TryParseToman(WalletAmountToCreditText, out var credit) ||
            !TryParseToman(WalletAmountReceivedText, out var received))
            return;

        var key = GetKey("wallet.cash-top-up", $"{customer.Id:D}|{credit}|{received}");
        ErrorMessage = string.Empty;
        WalletTopUpOutcomeText = string.Empty;
        BeginBusy();
        try
        {
            var result = await api.TopUpWalletCashAsync(
                customer.Id, new WalletCashTopUpRequest(credit, received), key);
            pendingKeys.Remove("wallet.cash-top-up");
            CurrentWalletBalance = new WalletBalanceResponse(
                result.CustomerId, result.CustomerCode, result.CustomerName,
                result.PurchasedBalanceToman, result.WalletVersion, result.ReceivedAtUtc, true);
            ConfirmWalletTopUp = false;
            WalletAmountToCreditText = string.Empty;
            WalletAmountReceivedText = string.Empty;
            WalletTopUpOutcomeText = Text(
                $"شارژ ثبت شد: اعتبار {FormatToman(result.AmountCreditedToman)} تومان؛ دریافتی {FormatToman(result.AmountReceivedToman)} تومان؛ باقی‌مانده {FormatToman(result.ChangeGivenToman)} تومان. موجودی جدید: {FormatToman(result.PurchasedBalanceToman)} تومان.",
                $"Top-up recorded: {FormatToman(result.AmountCreditedToman)} Toman credited; {FormatToman(result.AmountReceivedToman)} received; {FormatToman(result.ChangeGivenToman)} change. New balance: {FormatToman(result.PurchasedBalanceToman)} Toman.");
            StatusMessage = WalletTopUpOutcomeText;
        }
        catch (GameNetApiException ex)
        {
            ErrorMessage = WalletErrorText(ex.Code);
            if (ex.Code != "idempotency.in_flight")
                pendingKeys.Remove("wallet.cash-top-up");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = Text(
                "نتیجهٔ شارژ نامشخص است؛ کلید همان درخواست حفظ شد. موجودی را تازه کنید و در صورت نیاز همان مشتری و مبلغ‌ها را با همین کلید تکرار کنید.",
                "Top-up outcome is unknown; the original idempotency key is retained. Refresh the balance, then retry the same customer and amounts using that key if needed.");
        }
        catch
        {
            ErrorMessage = Text(
                "نتیجهٔ شارژ تأیید نشد؛ کلید درخواست حفظ شده است. موجودی را تازه کنید و پیش از تلاش دوباره، وضعیت را بررسی کنید.",
                "Top-up outcome was not confirmed; the original idempotency key is retained. Refresh and reconcile before retrying.");
        }
        finally { EndBusy(); }
    }

    private string WalletErrorText(string code) => code switch
    {
        "wallet.forbidden" => Text("مجوز شارژ کیف پول ندارید.", "You do not have permission to modify Wallet balances."),
        "customers.forbidden" => Text("مجوز مشاهدهٔ مشتریان ندارید.", "You do not have permission to read customers."),
        "wallet.customer_not_found" => Text("مشتری پیدا نشد؛ فهرست را تازه کنید.", "Customer was not found; refresh the list."),
        "wallet.registered_customer_required" => Text("کیف پول فقط برای مشتری ثبت‌شده است، نه مشتری گذری.", "Wallet top-ups require a registered customer, not a Walk-in."),
        "wallet.customer_blocked" => Text("مشتری مسدود است و کیف پول او شارژ نشد.", "The customer is blocked; the Wallet was not credited."),
        "wallet.customer_inactive" => Text("مشتری غیرفعال است و کیف پول او شارژ نشد.", "The customer is inactive; the Wallet was not credited."),
        "wallet.cash_insufficient" => Text("وجه دریافتی کمتر از مبلغی است که باید شارژ شود.", "Cash received is less than the amount to credit."),
        "wallet.amount_invalid" => Text("مبلغ شارژ باید عدد صحیح تومان و بزرگ‌تر از صفر باشد.", "Credit amount must be a positive whole-Toman amount."),
        "wallet.amount_overflow" or "wallet.balance_overflow" => Text("مبلغ از سقف مجاز عددی بیشتر است؛ موجودی تغییر نکرد.", "The amount exceeds the supported numeric range; no balance change was committed."),
        "idempotency.in_flight" => Text("همین درخواست هنوز در حال پردازش است؛ وضعیت را تازه کنید و همان درخواست را تکرار کنید.", "The same request is still processing; refresh, then retry the same request."),
        "idempotency.key_reused" => Text("کلید درخواست با اطلاعات متفاوت تداخل دارد؛ مبلغ‌ها را تغییر ندهید و ابتدا وضعیت را بررسی کنید.", "The idempotency key conflicts with a different payload; do not alter the amounts until the outcome is reconciled."),
        _ => Text("عملیات کیف پول تأیید نشد؛ موجودی را تازه و سوابق را بررسی کنید.", "Wallet operation was not confirmed; refresh and reconcile the recorded outcome.")
    };

    private void ResetWalletWorkflow()
    {
        walletSearchGeneration++;
        WalletCustomers.Clear();
        selectedWalletCustomer = null;
        currentWalletBalance = null;
        walletCustomerSearchText = string.Empty;
        walletAmountToCreditText = string.Empty;
        walletAmountReceivedText = string.Empty;
        confirmWalletTopUp = false;
        walletTopUpOutcomeText = string.Empty;
        OnPropertyChanged(nameof(SelectedWalletCustomer));
        OnPropertyChanged(nameof(CurrentWalletBalance));
        OnPropertyChanged(nameof(WalletCustomerSearchText));
        OnPropertyChanged(nameof(WalletAmountToCreditText));
        OnPropertyChanged(nameof(WalletAmountReceivedText));
        OnPropertyChanged(nameof(ConfirmWalletTopUp));
        OnPropertyChanged(nameof(WalletTopUpOutcomeText));
        OnPropertyChanged(nameof(WalletBalanceSummaryText));
        RaiseWalletCommands();
    }

    private void EnforceWalletPermissions()
    {
        OnPropertyChanged(nameof(CanWriteWallet));
        if (!CanWriteWallet || !CanReadCustomers)
            ResetWalletWorkflow();
        RaiseWalletCommands();
    }

    private void RaiseWalletCommands()
    {
        (SearchWalletCustomersCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (LoadWalletBalanceCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
        (WalletCashTopUpCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
    }
}
