using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Stations;
using GameNet.Shared.Contracts.V1.Identity;

namespace GameNet.Desktop.Features.Identity;

public sealed class LoginViewModel : INotifyPropertyChanged
{
    private readonly IGameNetServerClient serverClient;
    private string username = string.Empty, password = string.Empty, errorMessage = string.Empty;
    private string displayName = string.Empty, currentUsername = string.Empty;
    private string currentPassword = string.Empty, newPassword = string.Empty, confirmNewPassword = string.Empty;
    private string passwordChangeMessage = string.Empty;
    private string? pendingPasswordChangeKey, pendingPasswordCurrent, pendingPasswordNew;
    private bool passwordChangeSucceeded;
    private bool isBusy, isAuthenticated;

    public LoginViewModel(IGameNetServerClient serverClient)
    {
        this.serverClient = serverClient;
        LoginCommand = new AsyncUiAction(LoginAsync, () => !IsBusy && !IsAuthenticated);
        LogoutCommand = new AsyncUiAction(LogoutAsync, () => !IsBusy && IsAuthenticated);
        ChangePasswordCommand = new AsyncUiAction(ChangePasswordAsync, () => !IsBusy && IsAuthenticated);
        StationBoard = new StationBoardViewModel(serverClient);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public StationBoardViewModel StationBoard { get; }
    public string Username { get => username; set => SetField(ref username, value); }
    public string Password { get => password; set => SetField(ref password, value); }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string DisplayName { get => displayName; private set => SetField(ref displayName, value); }
    public string CurrentUsername { get => currentUsername; private set => SetField(ref currentUsername, value); }
    public string CurrentPassword { get => currentPassword; set => SetField(ref currentPassword, value); }
    public string NewPassword { get => newPassword; set => SetField(ref newPassword, value); }
    public string ConfirmNewPassword { get => confirmNewPassword; set => SetField(ref confirmNewPassword, value); }
    public string PasswordChangeMessage { get => passwordChangeMessage; private set => SetField(ref passwordChangeMessage, value); }
    public bool PasswordChangeSucceeded { get => passwordChangeSucceeded; private set => SetField(ref passwordChangeSucceeded, value); }
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
            await StationBoard.RefreshAsync();
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

    private async Task ChangePasswordAsync()
    {
        PasswordChangeSucceeded = false;
        PasswordChangeMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            PasswordChangeMessage = Text("گذرواژه فعلی و گذرواژه جدید را وارد کنید.", "Enter your current and new passwords.");
            return;
        }
        if (NewPassword.Length < 10 || NewPassword.Length > 256)
        {
            PasswordChangeMessage = Text("گذرواژه جدید باید حداقل ۱۰ و حداکثر ۲۵۶ نویسه داشته باشد.", "The new password must contain 10 to 256 characters.");
            return;
        }
        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            PasswordChangeMessage = Text("تکرار گذرواژه جدید یکسان نیست.", "The new password confirmation does not match.");
            return;
        }

        if (pendingPasswordChangeKey is null ||
            !string.Equals(pendingPasswordCurrent, CurrentPassword, StringComparison.Ordinal) ||
            !string.Equals(pendingPasswordNew, NewPassword, StringComparison.Ordinal))
        {
            pendingPasswordChangeKey = Guid.NewGuid().ToString("N");
            pendingPasswordCurrent = CurrentPassword;
            pendingPasswordNew = NewPassword;
        }

        IsBusy = true;
        try
        {
            var result = await serverClient.ChangeOwnPasswordAsync(
                new ChangeOwnPasswordRequest(CurrentPassword, NewPassword), pendingPasswordChangeKey);
            PasswordChangeMessage = result.OtherSessionsRevoked
                ? Text("گذرواژه تغییر کرد؛ نشست‌های دیگر از سامانه خارج شدند.", "Password changed; other sessions were signed out.")
                : Text("گذرواژه تغییر کرد.", "Password changed.");
            ClearPendingPasswordChange();
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
            PasswordChangeSucceeded = true;
        }
        catch (GameNetApiException exception)
        {
            if (exception.StatusCode >= 400 && exception.StatusCode < 500) ClearPendingPasswordChange();
            PasswordChangeMessage = exception.Code switch
            {
                "identity.current_password_invalid" => Text("گذرواژه فعلی صحیح نیست.", "The current password is incorrect."),
                "identity.password_unchanged" => Text("گذرواژه جدید باید با گذرواژه فعلی متفاوت باشد.", "The new password must differ from the current password."),
                "identity.invalid" => Text("گذرواژه جدید معتبر نیست.", "The new password is invalid."),
                "idempotency.key_reused" => Text("شناسه درخواست با محتوای متفاوت استفاده شده؛ دوباره تلاش کنید.", "The request key was reused with different content. Try again."),
                "security.https_required" => Text("برای اتصال به سرور راه دور، ارتباط امن HTTPS لازم است.", "A secure HTTPS connection is required for remote servers."),
                _ => Text("تغییر گذرواژه انجام نشد. اتصال و وضعیت سرور را بررسی کنید.", "Password change failed. Check the connection and Server status.")
            };
        }
        catch (HttpRequestException)
        {
            PasswordChangeMessage = Text("نتیجه از سرور دریافت نشد. با همان مقادیر دوباره تلاش کنید.", "The result was not received. Retry with the same values.");
        }
        catch (TaskCanceledException)
        {
            PasswordChangeMessage = Text("پاسخی از سرور دریافت نشد. با همان مقادیر دوباره تلاش کنید.", "The Server did not respond. Retry with the same values.");
        }
        catch
        {
            PasswordChangeMessage = Text("تغییر گذرواژه انجام نشد. وضعیت سرور را بررسی کنید.", "Password change failed. Check the Server status.");
        }
        finally { IsBusy = false; }
    }

    private void ClearPendingPasswordChange()
    {
        pendingPasswordChangeKey = null;
        pendingPasswordCurrent = null;
        pendingPasswordNew = null;
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
            StationBoard.ClearLocalState();
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
            PasswordChangeMessage = string.Empty;
            PasswordChangeSucceeded = false;
            ClearPendingPasswordChange();
            IsBusy = false;
        }
    }

    private void RaiseCommands()
    {
        if (LoginCommand is AsyncUiAction login) login.RaiseCanExecuteChanged();
        if (LogoutCommand is AsyncUiAction logout) logout.RaiseCanExecuteChanged();
        if (ChangePasswordCommand is AsyncUiAction changePassword) changePassword.RaiseCanExecuteChanged();
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
