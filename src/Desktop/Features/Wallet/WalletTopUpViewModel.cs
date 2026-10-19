using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Wallet;

namespace GameNet.Desktop.Features.Wallet;

/// <summary>Owns the Wallet cash top-up screen independently of Session workflow UI.</summary>
public sealed class WalletTopUpViewModel : IWalletTopUpPort
{
    private readonly IGameNetServerClient api;
    private readonly Func<string, bool> hasPermission;
    private readonly IWalletPendingRequestStore pendingRequestStore;
    private readonly TopUpAction topUpCommand;
    private Guid? currentOperatorId;
    private Guid? pendingCustomerId;
    private int? pendingCustomerVersion;
    private string? pendingCustomerCode;
    private bool pendingRecoveryStoreUnavailable;
    private CustomerResponse? selectedCustomer;
    private string amountToCreditText = string.Empty;
    private string cashReceivedText = string.Empty;
    private string balanceText = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;
    private string? pendingFingerprint;
    private string? pendingIdempotencyKey;
    private int balanceRefreshGeneration;
    private bool isBusy;

    public WalletTopUpViewModel(
        IGameNetServerClient api,
        Func<string, bool> hasPermission,
        IWalletPendingRequestStore? pendingRequestStore = null)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.hasPermission = hasPermission ?? throw new ArgumentNullException(nameof(hasPermission));
        this.pendingRequestStore = pendingRequestStore ?? new FileWalletPendingRequestStore();
        topUpCommand = new TopUpAction(this);
        BalanceText = Text("برای مشاهدهٔ موجودی، مشتری ثبت‌شده را انتخاب کنید.", "Select a registered customer to view its Wallet balance.");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand TopUpCommand => topUpCommand;
    public bool CanTopUpWallet => hasPermission(Permissions.WalletWrite);
    public bool CanEditInput => !IsBusy && pendingIdempotencyKey is null && !pendingRecoveryStoreUnavailable;
    public bool IsBusy { get => isBusy; private set { if (SetField(ref isBusy, value)) RaiseCommand(); } }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }
    public string BalanceText { get => balanceText; private set => SetField(ref balanceText, value); }

    public CustomerResponse? SelectedCustomer
    {
        get => selectedCustomer;
        set
        {
            if (!SetField(ref selectedCustomer, value)) return;
            if (pendingRecoveryStoreUnavailable)
            {
                // Keep the fail-closed explanation visible even while the parent view
                // changes or refreshes the selected customer.
            }
            else if (pendingIdempotencyKey is null)
            {
                ErrorMessage = string.Empty;
                StatusMessage = string.Empty;
            }
            else
            {
                ErrorMessage = PendingReconciliationText;
            }
            RaiseCommand();
            RefreshBalance();
        }
    }

    public string AmountToCreditText
    {
        get => amountToCreditText;
        set
        {
            if (pendingIdempotencyKey is not null) return;
            if (SetField(ref amountToCreditText, value)) { OnPropertyChanged(nameof(SummaryText)); RaiseCommand(); }
        }
    }

    public string CashReceivedText
    {
        get => cashReceivedText;
        set
        {
            if (pendingIdempotencyKey is not null) return;
            if (SetField(ref cashReceivedText, value)) { OnPropertyChanged(nameof(SummaryText)); RaiseCommand(); }
        }
    }

    public string SummaryText
    {
        get
        {
            if (!TryParseToman(AmountToCreditText, out var credit) || credit <= 0 ||
                !TryParseToman(CashReceivedText, out var received) || received < credit)
                return string.Empty;
            return Text(
                $"شارژ {FormatToman(credit)}، وجه دریافتی {FormatToman(received)}، باقی‌پول {FormatToman(received - credit)}",
                $"Credit {FormatToman(credit)}, cash received {FormatToman(received)}, change {FormatToman(received - credit)}");
        }
    }

    public void NotifyPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanTopUpWallet));
        RaiseCommand();
        RefreshBalance();
    }

    public void SetCurrentOperatorId(Guid? operatorId)
    {
        if (currentOperatorId == operatorId && !pendingRecoveryStoreUnavailable) return;

        currentOperatorId = operatorId;
        selectedCustomer = null;
        OnPropertyChanged(nameof(SelectedCustomer));
        pendingFingerprint = null;
        pendingIdempotencyKey = null;
        pendingCustomerId = null;
        pendingCustomerVersion = null;
        pendingCustomerCode = null;
        pendingRecoveryStoreUnavailable = false;
        AmountToCreditText = string.Empty;
        CashReceivedText = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (operatorId is Guid authenticatedOperatorId)
        {
            try
            {
                var pending = pendingRequestStore.Load();
                if (pending is not null)
                {
                    if (pending.OperatorId != authenticatedOperatorId)
                    {
                        // Fail closed across operator changes: an uncertain physical-cash
                        // operation must be reconciled by its original operator first.
                        pendingRecoveryStoreUnavailable = true;
                        ErrorMessage = ForeignOperatorPendingText;
                    }
                    else
                    {
                        AmountToCreditText = pending.AmountToCreditToman.ToString(CultureInfo.InvariantCulture);
                        CashReceivedText = pending.AmountReceivedToman.ToString(CultureInfo.InvariantCulture);
                        pendingCustomerId = pending.CustomerId;
                        pendingCustomerVersion = pending.CustomerVersion;
                        pendingCustomerCode = pending.CustomerCode;
                        pendingFingerprint = Fingerprint(
                            pending.CustomerId, pending.CustomerVersion,
                            pending.AmountToCreditToman, pending.AmountReceivedToman);
                        pendingIdempotencyKey = pending.IdempotencyKey;
                        ErrorMessage = PendingReconciliationText;
                    }
                }
            }
            catch
            {
                // A corrupt/inaccessible recovery record blocks new financial operations.
                // Never discard the file automatically: the Server may already have committed it.
                pendingRecoveryStoreUnavailable = true;
                ErrorMessage = RecoveryStoreUnavailableText;
            }
        }

        OnPropertyChanged(nameof(CanEditInput));
        RaiseCommand();
        RefreshBalance();
    }

    public void Reset()
    {
        balanceRefreshGeneration++;
        if (pendingRecoveryStoreUnavailable)
        {
            OnPropertyChanged(nameof(CanEditInput));
            RaiseCommand();
            return;
        }
        // A request may already have committed even if its response was lost or the user logs out.
        // Preserve the exact target and tender until the same idempotency key is reconciled.
        if (pendingIdempotencyKey is not null)
        {
            ErrorMessage = PendingReconciliationText;
            StatusMessage = string.Empty;
            OnPropertyChanged(nameof(CanEditInput));
            RaiseCommand();
            return;
        }

        selectedCustomer = null;
        OnPropertyChanged(nameof(SelectedCustomer));
        AmountToCreditText = string.Empty;
        CashReceivedText = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        BalanceText = Text("برای مشاهدهٔ موجودی، مشتری ثبت‌شده را انتخاب کنید.", "Select a registered customer to view its Wallet balance.");
    }

    private void RefreshBalance()
    {
        var generation = ++balanceRefreshGeneration;
        var customer = SelectedCustomer;
        if (!CanTopUpWallet || customer is not { Kind: CustomerKindContract.Registered })
        {
            BalanceText = Text("برای مشاهدهٔ موجودی، مشتری ثبت‌شده را انتخاب کنید.", "Select a registered customer to view its Wallet balance.");
            return;
        }

        BalanceText = Text("در حال دریافت موجودی کیف پول…", "Loading Wallet balance…");
        _ = LoadBalanceAsync(customer.Id, generation);
    }

    private async Task LoadBalanceAsync(Guid customerId, int generation)
    {
        try
        {
            var balance = await api.GetWalletBalanceAsync(customerId);
            if (generation != balanceRefreshGeneration || SelectedCustomer?.Id != customerId) return;
            BalanceText = Text($"موجودی کیف پول: {FormatToman(balance.BalanceToman)}", $"Wallet balance: {FormatToman(balance.BalanceToman)}");
        }
        catch (GameNetApiException ex)
        {
            if (generation == balanceRefreshGeneration) BalanceText = ErrorText(ex.Code);
        }
        catch (HttpRequestException)
        {
            if (generation == balanceRefreshGeneration)
                BalanceText = Text("موجودی دریافت نشد؛ اتصال به سرور را بررسی کنید.", "Could not load Wallet balance. Check the Server connection.");
        }
        catch (TaskCanceledException)
        {
            if (generation == balanceRefreshGeneration)
                BalanceText = Text("دریافت موجودی زمان‌بر شد؛ دوباره تازه‌سازی کنید.", "Wallet balance request timed out. Refresh before retrying.");
        }
        catch
        {
            if (generation == balanceRefreshGeneration)
                BalanceText = Text("موجودی کیف پول در دسترس نیست.", "Wallet balance is unavailable.");
        }
    }

    private bool CanSubmit
    {
        get
        {
            if (IsBusy || pendingRecoveryStoreUnavailable || currentOperatorId is null || !CanTopUpWallet ||
                SelectedCustomer is not { Kind: CustomerKindContract.Registered } customer ||
                (pendingIdempotencyKey is null && customer.Status != CustomerStatusContract.Active) ||
                !TryParseToman(AmountToCreditText, out var credit) || credit <= 0 ||
                !TryParseToman(CashReceivedText, out var received) || received < credit)
                return false;

            var requestVersion = pendingIdempotencyKey is null ? customer.Version : pendingCustomerVersion;
            if (!requestVersion.HasValue) return false;

            var currentFingerprint = Fingerprint(customer.Id, requestVersion.Value, credit, received);
            return pendingIdempotencyKey is null ||
                (customer.Id == pendingCustomerId &&
                 string.Equals(pendingFingerprint, currentFingerprint, StringComparison.Ordinal));
        }
    }

    private async Task TopUpAsync()
    {
        if (!CanSubmit || SelectedCustomer is not { } target ||
            !TryParseToman(AmountToCreditText, out var credit) ||
            !TryParseToman(CashReceivedText, out var received))
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        var requestVersion = pendingIdempotencyKey is null ? target.Version : pendingCustomerVersion!.Value;
        var fingerprint = Fingerprint(target.Id, requestVersion, credit, received);
        if (pendingIdempotencyKey is null)
        {
            pendingFingerprint = fingerprint;
            pendingCustomerId = target.Id;
            pendingCustomerVersion = target.Version;
            pendingCustomerCode = target.Code;
            pendingIdempotencyKey = Guid.NewGuid().ToString("N");
            OnPropertyChanged(nameof(CanEditInput));
        }
        else if (target.Id != pendingCustomerId ||
                 !string.Equals(pendingFingerprint, fingerprint, StringComparison.Ordinal))
        {
            // Never mint a fresh key while the prior cash outcome is unresolved.
            ErrorMessage = PendingReconciliationText;
            return;
        }

        var key = pendingIdempotencyKey!;
        try
        {
            // Persist the exact request before sending it. If the app exits after a lost
            // response, the next launch can safely replay this same idempotency key.
            pendingRequestStore.Save(new PendingWalletTopUp(
                currentOperatorId!.Value, pendingCustomerId!.Value, pendingCustomerCode ?? target.Code,
                pendingCustomerVersion!.Value, credit, received, key));
        }
        catch
        {
            ErrorMessage = RecoveryCheckpointSaveFailedText;
            return; // Do not send a financial request without a durable recovery marker.
        }

        IsBusy = true;
        try
        {
            var result = await api.CashTopUpWalletAsync(
                target.Id, new WalletCashTopUpRequest(credit, received, requestVersion), key);
            if (!ClearPendingRequest())
            {
                ErrorMessage = RecoveryCheckpointClearFailedText;
                return;
            }
            AmountToCreditText = string.Empty;
            CashReceivedText = string.Empty;
            StatusMessage = Text(
                $"کیف پول مشتری {target.Code} به مبلغ {FormatToman(result.AmountCreditedToman)} شارژ شد؛ باقی‌پول {FormatToman(result.ChangeGivenToman)}.",
                $"Customer {target.Code} Wallet credited by {FormatToman(result.AmountCreditedToman)}; change {FormatToman(result.ChangeGivenToman)}.");
            if (SelectedCustomer?.Id == target.Id)
                await LoadBalanceAsync(target.Id, ++balanceRefreshGeneration);
        }
        catch (GameNetApiException ex)
        {
            // A server-side 5xx or malformed success body can happen after the transaction
            // committed. Keep the key in those ambiguous cases; business 4xx rejections
            // are known non-committing outcomes and may be corrected safely.
            if (ex.StatusCode >= 500 || ex.Code == "server.response_invalid" ||
                ex.Code is "idempotency.in_flight" or "idempotency.key_reused" or "idempotency.unavailable")
            {
                ErrorMessage = PendingReconciliationText;
            }
            else
            {
                if (!ClearPendingRequest())
                {
                    ErrorMessage = RecoveryCheckpointClearFailedText;
                    return;
                }
                ErrorMessage = ErrorText(ex.Code);
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = PendingReconciliationText;
            await LoadBalanceAsync(target.Id, ++balanceRefreshGeneration);
            ErrorMessage = PendingReconciliationText;
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = PendingReconciliationText;
            await LoadBalanceAsync(target.Id, ++balanceRefreshGeneration);
            ErrorMessage = PendingReconciliationText;
        }
        catch
        {
            // Treat unexpected transport/response failures as ambiguous until the same
            // idempotent request is replayed; do not permit a new key for another amount.
            ErrorMessage = PendingReconciliationText;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanEditInput));
            RaiseCommand();
        }
    }

    private static string Fingerprint(Guid customerId, int customerVersion, long credit, long received) =>
        $"{customerId:D}|{customerVersion}|{credit}|{received}";

    private string PendingReconciliationText
    {
        get
        {
            var customer = string.IsNullOrWhiteSpace(pendingCustomerCode) ? "" : $" ({pendingCustomerCode})";
            return Text(
                $"شارژ قبلی{customer} هنوز تعیین‌تکلیف نشده است. همان مشتری و مبلغ‌ها را انتخاب کنید و همان درخواست را دوباره بزنید؛ تا آن زمان شارژ جدید مجاز نیست.",
                $"A previous top-up{customer} is unresolved. Select the original customer and amounts, then retry the same request. New top-ups remain blocked until reconciliation.");
        }
    }

    private string ForeignOperatorPendingText => Text(
        "یک درخواست شارژ نامشخص از اپراتور دیگری وجود دارد. برای جلوگیری از ثبت دوباره وجه، باید اپراتور اصلی وارد شود و همان درخواست را تعیین‌تکلیف کند؛ شارژ جدید فعلاً مسدود است.",
        "An unresolved cash top-up belongs to another operator. Sign in as that original operator and reconcile the same request before any new Wallet top-up; new top-ups are blocked.");

    private string RecoveryStoreUnavailableText => Text(
        "وضعیت بازیابی شارژ کیف پول خوانده نشد یا معتبر نیست. برای جلوگیری از شارژ تکراری، عملیات مالی مسدود شد؛ فایل بازیابی را حذف نکنید.",
        "The Wallet recovery record is unreadable or invalid. Financial actions are blocked to prevent duplicate credit; do not delete the recovery file.");

    private string RecoveryCheckpointSaveFailedText => Text(
        "نشانگر بازیابی شارژ ذخیره نشد؛ درخواست به سرور ارسال نشده است. مجوز نوشتن پوشه کاربر را بررسی و دوباره تلاش کنید.",
        "The Wallet recovery checkpoint could not be saved; the request was not sent to the Server. Check write access to the current user's local data and retry.");

    private string RecoveryCheckpointClearFailedText => Text(
        "نتیجهٔ شارژ با سرور دریافت شد، اما نشانگر بازیابی محلی پاک نشد. همان درخواست را دوباره بزنید تا با همان کلید تطبیق و سپس پاک شود؛ شارژ دوباره ثبت نمی‌شود.",
        "The Server response was received, but the local recovery checkpoint could not be cleared. Retry the same request to reconcile and clear it; the idempotency key prevents duplicate credit.");

    private bool ClearPendingRequest()
    {
        try
        {
            pendingRequestStore.Clear();
        }
        catch
        {
            return false;
        }

        pendingFingerprint = null;
        pendingIdempotencyKey = null;
        pendingCustomerId = null;
        pendingCustomerVersion = null;
        pendingCustomerCode = null;
        OnPropertyChanged(nameof(CanEditInput));
        RaiseCommand();
        return true;
    }

    private string ErrorText(string code) => code switch
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

    private void RaiseCommand() => topUpCommand.RaiseCanExecuteChanged();
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(propertyName); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static bool TryParseToman(string? text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var digits = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            if (c is >= '0' and <= '9') digits.Append(c);
            else if (c is '۰' or '٠') digits.Append('0');
            else if (c is '۱' or '١') digits.Append('1');
            else if (c is '۲' or '٢') digits.Append('2');
            else if (c is '۳' or '٣') digits.Append('3');
            else if (c is '۴' or '٤') digits.Append('4');
            else if (c is '۵' or '٥') digits.Append('5');
            else if (c is '۶' or '٦') digits.Append('6');
            else if (c is '۷' or '٧') digits.Append('7');
            else if (c is '۸' or '٨') digits.Append('8');
            else if (c is '۹' or '٩') digits.Append('9');
            else if (char.IsWhiteSpace(c) || c is ',' or '٬') continue;
            else return false;
        }
        return long.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatToman(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private static string Text(string persian, string english) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? persian : english;

    private sealed class TopUpAction(WalletTopUpViewModel owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => owner.CanSubmit;
        public async void Execute(object? parameter) => await owner.TopUpAsync();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}