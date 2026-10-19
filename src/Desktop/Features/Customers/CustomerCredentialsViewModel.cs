using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Security;

namespace GameNet.Desktop.Features.Customers;

/// <summary>
/// Owns the operator's customer-credential form and ambiguous-request recovery state.
/// Customer profile, Wallet and Session data remain in their own feature services.
/// </summary>
public sealed class CustomerCredentialsViewModel : ICustomerCredentialsPort
{
    private readonly IGameNetServerClient api;
    private readonly Func<string, bool> hasPermission;
    private readonly Func<Guid, Task> afterSaved;
    private CustomerResponse? selectedCustomer;
    private string username = string.Empty, password = string.Empty, errorMessage = string.Empty, statusMessage = string.Empty;
    private string? pendingFingerprint, pendingKey;
    private int inputResetVersion;
    private bool isBusy;

    public CustomerCredentialsViewModel(
        IGameNetServerClient api, Func<string, bool> hasPermission, Func<Guid, Task> afterSaved)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.hasPermission = hasPermission ?? throw new ArgumentNullException(nameof(hasPermission));
        this.afterSaved = afterSaved ?? throw new ArgumentNullException(nameof(afterSaved));
        SetCredentialsCommand = new AsyncUiAction(SetCredentialsAsync, CanSetCredentials);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SetCredentialsCommand { get; }
    public CustomerResponse? SelectedCustomer
    {
        get => selectedCustomer;
        set
        {
            var identityChanged = selectedCustomer?.Id != value?.Id;
            if (!identityChanged && selectedCustomer?.Version == value?.Version) return;
            selectedCustomer = value;
            if (identityChanged)
                Username = string.Empty;
            Password = string.Empty;
            ErrorMessage = string.Empty;
            if (identityChanged)
                StatusMessage = string.Empty;
            InputResetVersion++;
            OnPropertyChanged();
            RaiseCommand();
        }
    }

    public bool CanManageCredentials => hasPermission(Permissions.CustomersCredentialsManage);
    public string Username { get => username; set { if (SetField(ref username, value)) RaiseCommand(); } }
    public string Password { get => password; set { if (SetField(ref password, value)) RaiseCommand(); } }
    public string ErrorMessage { get => errorMessage; private set => SetField(ref errorMessage, value); }
    public string StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }
    public int InputResetVersion { get => inputResetVersion; private set => SetField(ref inputResetVersion, value); }
    public bool IsBusy { get => isBusy; private set { if (SetField(ref isBusy, value)) RaiseCommand(); } }

    public void NotifyPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanManageCredentials));
        RaiseCommand();
    }

    private bool CanSetCredentials() =>
        !IsBusy && CanManageCredentials &&
        SelectedCustomer is { Kind: CustomerKindContract.Registered } &&
        IsValidUsername(Username) && IsValidPassword(Password);

    private async Task SetCredentialsAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null || !CanSetCredentials()) return;

        var normalizedUsername = Username.Trim().ToUpperInvariant();
        var fingerprint = $"{customer.Id:D}|{customer.Version}|{normalizedUsername}|{Password}";
        if (pendingKey is null || !string.Equals(pendingFingerprint, fingerprint, StringComparison.Ordinal))
        {
            pendingFingerprint = fingerprint;
            pendingKey = Guid.NewGuid().ToString("N");
        }

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await api.SetCustomerCredentialsAsync(customer.Id,
                new SetCustomerCredentialsRequest(normalizedUsername, Password, customer.Version), pendingKey);
            pendingKey = null;
            pendingFingerprint = null;
            Username = result.Username;
            Password = string.Empty;
            InputResetVersion++;
            StatusMessage = Text($"نام کاربری مشتری {result.Username} ثبت شد. رمز در برنامه ذخیره یا نمایش داده نمی‌شود.",
                $"Credentials were saved for {result.Username}. The password is not stored or displayed by the desktop app.");
            await afterSaved(customer.Id);
        }
        catch (GameNetApiException ex)
        {
            pendingKey = null;
            pendingFingerprint = null;
            ErrorMessage = ex.Code switch
            {
                "customers.credentials_forbidden" => Text("مجوز تنظیم مشخصات ورود مشتری را ندارید.", "You do not have permission to manage customer credentials."),
                "customers.credentials_invalid" => Text("نام کاربری یا رمز معتبر نیست؛ نام کاربری ۲ تا ۴۰ نویسه و رمز حداقل ۸ نویسه باشد.", "Username or password is invalid; use a 2-40 character username and a password of at least 8 characters."),
                "customers.not_found" => Text("مشتری ثبت‌شده پیدا نشد.", "Registered customer was not found."),
                "customers.version_conflict" => Text("اطلاعات مشتری تغییر کرده؛ تازه‌سازی کنید و دوباره بررسی کنید.", "Customer data changed. Refresh the customer before retrying."),
                "customers.login_username_exists" => Text("این نام کاربری برای مشتری دیگری ثبت شده است.", "That username is already assigned to another customer."),
                _ => Text("ثبت مشخصات ورود انجام نشد؛ وضعیت مشتری را تازه‌سازی کنید.", "Could not save customer credentials. Refresh the customer state.")
            };
        }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("پاسخ سرور نرسید؛ همان نام کاربری و رمز را نگه دارید و درخواست را دوباره امتحان کنید.",
                "The Server response was lost. Keep the same username/password and retry the same request to reconcile the outcome.");
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = Text("پاسخ سرور در زمان مقرر نرسید؛ اطلاعات را تغییر ندهید و همان درخواست را دوباره امتحان کنید.",
                "The Server timed out. Keep the same credentials and retry the exact request to reconcile its outcome.");
        }
        catch
        {
            ErrorMessage = Text("نتیجهٔ ثبت مشخصات ورود تأیید نشد؛ تا روشن‌شدن وضعیت، اطلاعات را تغییر ندهید.",
                "Could not confirm the credential update. Keep the exact request unchanged until its outcome is reconciled.");
        }
        finally { IsBusy = false; }
    }

    private static bool IsValidUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        return text.Length is >= 2 and <= 40 &&
               text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') &&
               char.IsAsciiLetterOrDigit(text[0]) && char.IsAsciiLetterOrDigit(text[^1]);
    }

    private static bool IsValidPassword(string? value) =>
        value is { Length: >= 8 and <= 256 } && !string.IsNullOrWhiteSpace(value) &&
        !value.Any(char.IsControl);

    private void RaiseCommand() => (SetCredentialsCommand as AsyncUiAction)?.RaiseCanExecuteChanged();
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string Text(string fa, string en) =>
        System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? fa : en;
}
