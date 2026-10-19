using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.CustomerClient.Features;

/// <summary>
/// Presentation state for the isolated customer UI. All entry eligibility, tariff, Wallet,
/// and Session timing authority remains on the Server.
/// </summary>
public sealed class CustomerClientViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CustomerEntryPipeClient gateway;
    private readonly DispatcherTimer countdownTimer;
    private readonly AsyncLoginAction loginCommand;
    private string username = string.Empty;
    private string password = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = "برای ورود، نام کاربری و رمز عبور مشتری را وارد کنید.";
    private string displayName = string.Empty;
    private string remainingTimeText = "--:--:--";
    private bool isBusy;
    private bool isSessionStarted;
    private long remainingSeconds;
    private string? pendingIdempotencyKey;

    public CustomerClientViewModel(CustomerEntryPipeClient gateway)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        loginCommand = new AsyncLoginAction(LoginAsync, () => CanSubmit);
        LoginCommand = loginCommand;
        countdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        countdownTimer.Tick += CountdownTimerOnTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand LoginCommand { get; }
    public string Username
    {
        get => username;
        set { if (SetField(ref username, value)) RaiseCommands(); }
    }
    public string Password
    {
        get => password;
        set { if (SetField(ref password, value)) RaiseCommands(); }
    }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }
    public string DisplayName { get => displayName; private set => SetField(ref displayName, value); }
    public string RemainingTimeText { get => remainingTimeText; private set => SetField(ref remainingTimeText, value); }
    public bool IsBusy { get => isBusy; private set { if (SetField(ref isBusy, value)) RaiseCommands(); } }
    public bool IsSessionStarted { get => isSessionStarted; private set { if (SetField(ref isSessionStarted, value)) RaiseCommands(); } }
    public bool CanSubmit => !IsBusy && !IsSessionStarted &&
                             !string.IsNullOrWhiteSpace(Username) &&
                             !string.IsNullOrWhiteSpace(Password);

    private async Task LoginAsync()
    {
        if (!CanSubmit)
            return;

        pendingIdempotencyKey ??= Guid.NewGuid().ToString("N");
        var request = new CustomerClientEntryRequest(
            Username.Trim(), Password, pendingIdempotencyKey);

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "در حال بررسی حساب و اعتبار از طریق سرور...";
        try
        {
            var response = await gateway.StartAsync(request);
            if (!response.Succeeded || response.CustomerId is null ||
                response.SessionId is null || response.PrepaidReservedAmountToman is not > 0 ||
                response.PrepaidBudgetTicks is not > 0)
            {
                ErrorMessage = LocalizeError(response.ErrorCode, response.ErrorMessage);
                StatusMessage = "ورود انجام نشد؛ بررسی کیف پول و اتصال رایانه به سرور لازم است.";
                return;
            }

            pendingIdempotencyKey = null;
            DisplayName = string.IsNullOrWhiteSpace(response.DisplayName) ? Username : response.DisplayName;
            remainingSeconds = Math.Max(1, (long)Math.Ceiling(
                response.PrepaidBudgetTicks.Value / (double)TimeSpan.TicksPerSecond));
            RemainingTimeText = FormatRemaining(remainingSeconds);
            StatusMessage = $"اعتبار رزروشده: {response.PrepaidReservedAmountToman.Value.ToString("N0", CultureInfo.CurrentCulture)} تومان. پایان Session را سرور کنترل می‌کند.";
            Password = string.Empty;
            IsSessionStarted = true;
            countdownTimer.Start();
        }
        catch (Exception)
        {
            // Keep the same idempotency key: a lost pipe response may follow a committed
            // Session. The Server can safely reconcile this exact request on retry.
            ErrorMessage = "ارتباط با Agent برقرار نشد؛ نتیجه نامشخص است. پیش از تلاش دوباره اتصال را بررسی کنید.";
            StatusMessage = "برای جلوگیری از شروع تکراری، درخواست بعدی از همان کلید استفاده می‌کند.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CountdownTimerOnTick(object? sender, EventArgs e)
    {
        if (remainingSeconds > 0)
            remainingSeconds--;
        RemainingTimeText = FormatRemaining(remainingSeconds);
        if (remainingSeconds > 0)
            return;

        countdownTimer.Stop();
        StatusMessage = "زمان اعتبار تمام شد. پایان و تسویهٔ Session را سرور مدیریت می‌کند؛ قفل واقعی ویندوز جداگانه است.";
    }

    private static string FormatRemaining(long totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    private static string LocalizeError(string? code, string? message) => code switch
    {
        "customer_auth.invalid_credentials" => "نام کاربری یا رمز عبور نادرست است، یا حساب اجازهٔ ورود ندارد.",
        "wallet.insufficient_balance" => "اعتبار کیف پول کافی نیست؛ با اپراتور تماس بگیرید.",
        "wallet.prepaid_reservation_active" or "sessions.customer_has_active_session" =>
            "این حساب از قبل روی یک رایانه Session فعال دارد.",
        "sessions.station_unavailable" or "sessions.agent_unavailable" or "agent.customer_station_unbound" =>
            "این رایانه آمادهٔ ورود نیست؛ با اپراتور تماس بگیرید.",
        "client.agent_unavailable" => "سرویس GameNet روی این رایانه در دسترس نیست.",
        "client.result_unknown" => "نتیجهٔ ورود نامشخص است؛ با همان درخواست دوباره تلاش کنید.",
        _ => string.IsNullOrWhiteSpace(message) ? "ورود انجام نشد؛ وضعیت را بررسی کنید." : message
    };

    private void RaiseCommands()
    {
        OnPropertyChanged(nameof(CanSubmit));
        loginCommand.RaiseCanExecuteChanged();
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

    public void Dispose()
    {
        countdownTimer.Stop();
        countdownTimer.Tick -= CountdownTimerOnTick;
    }

    private sealed class AsyncLoginAction(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public async void Execute(object? parameter) => await execute();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
