using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Identity;

namespace GameNet.Desktop.Features.Identity;

public sealed class LoginViewModel : INotifyPropertyChanged
{
    private readonly IGameNetServerClient serverClient;
    private string username = string.Empty, password = string.Empty, errorMessage = string.Empty;
    private string displayName = string.Empty, currentUsername = string.Empty, statusMessage = string.Empty;
    private bool isBusy, isAuthenticated;

    public LoginViewModel(IGameNetServerClient serverClient)
    {
        this.serverClient = serverClient;
        LoginCommand = new AsyncUiAction(LoginAsync, () => !IsBusy && !IsAuthenticated);
        LogoutCommand = new AsyncUiAction(LogoutAsync, () => !IsBusy && IsAuthenticated);
        StatusMessage = Text("نشست ورود از طرف سرور تأیید می‌شود. برد سیستم‌ها در برش بعدی وصل خواهد شد.",
            "Your session is verified by the Server. The station board will be connected in the next slice.");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public string Username { get => username; set => SetField(ref username, value); }
    public string Password { get => password; set => SetField(ref password, value); }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string DisplayName { get => displayName; private set => SetField(ref displayName, value); }
    public string CurrentUsername { get => currentUsername; private set => SetField(ref currentUsername, value); }
    public string StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }
    public bool IsBusy { get => isBusy; private set { if (SetField(ref isBusy, value)) RaiseCommands(); } }
    public bool IsAuthenticated { get => isAuthenticated; private set { if (SetField(ref isAuthenticated, value)) RaiseCommands(); } }

    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = Text("نام کاربری و گذرواژه را وارد کنید.", "Enter your username and password.");
            return;
        }
        IsBusy = true;
        try
        {
            await serverClient.LoginAsync(new LoginRequest(Username, Password));
            var current = await serverClient.GetCurrentOperatorAsync();
            DisplayName = current.DisplayName;
            CurrentUsername = current.Username;
            IsAuthenticated = true;
            Password = string.Empty;
        }
        catch (GameNetApiException exception)
        {
            serverClient.ClearSession();
            ErrorMessage = exception.Code switch
            {
                "auth.invalid_credentials" => Text("نام کاربری یا گذرواژه نادرست است.", "Username or password is incorrect."),
                "auth.locked" => Text("حساب موقتاً قفل شده است. کمی بعد تلاش کنید.", "This account is temporarily locked. Try again later."),
                "auth.disabled" => Text("این حساب غیرفعال است.", "This account is disabled."),
                "security.https_required" => Text("برای اتصال به سرور راه دور، ارتباط امن HTTPS لازم است.", "A secure HTTPS connection is required for remote servers."),
                _ => Text("ورود انجام نشد. وضعیت سرور را بررسی کنید.", "Sign in failed. Check the Server status.")
            };
        }
        catch (HttpRequestException)
        {
            serverClient.ClearSession();
            ErrorMessage = Text("ارتباط با سرور برقرار نشد. اتصال شبکه را بررسی کنید.", "Could not connect to the Server. Check your network.");
        }
        catch (TaskCanceledException)
        {
            serverClient.ClearSession();
            ErrorMessage = Text("پاسخی از سرور دریافت نشد.", "The Server did not respond in time.");
        }
        catch
        {
            serverClient.ClearSession();
            ErrorMessage = Text("ورود انجام نشد. وضعیت سرور را بررسی کنید.", "Sign in failed. Check the Server status.");
        }
        finally { IsBusy = false; }
    }

    private async Task LogoutAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try { await serverClient.LogoutAsync(); }
        catch
        {
            ErrorMessage = Text("ارتباط هنگام خروج قطع شد؛ این دستگاه از نشست خارج شد.",
                "The connection failed during sign-out. This device cleared its session.");
        }
        finally
        {
            serverClient.ClearSession();
            IsAuthenticated = false;
            DisplayName = string.Empty;
            CurrentUsername = string.Empty;
            Password = string.Empty;
            IsBusy = false;
        }
    }

    private void RaiseCommands()
    {
        if (LoginCommand is AsyncUiAction login) login.RaiseCanExecuteChanged();
        if (LogoutCommand is AsyncUiAction logout) logout.RaiseCanExecuteChanged();
    }

    private static string Text(string persian, string english) =>
        System.Globalization.CultureInfo.CurrentUICulture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? english : persian;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
