using System.Globalization;
using System.Net.Http;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>Owns the Customer ID lookup sequence outside the Dashboard ViewModel.</summary>
public sealed class PrepaidStationEntryCoordinator(
    IGameNetServerClient api, ICustomerEntryPrompt? customerEntryPrompt,
    Action beginBusy, Action endBusy, Action<string> setErrorMessage,
    Func<string, string> serverErrorText,
    Func<StationResponse, CustomerResponse, SessionResponse, Task> onSuccess)
{
    private readonly Dictionary<string, (string Fingerprint, string Key)> pendingKeys = new(StringComparer.Ordinal);
    public bool CanPrompt => customerEntryPrompt is not null;

    public async Task StartAsync(object? parameter)
    {
        if (parameter is not StationResponse station || station.Type != StationTypeContract.Pc ||
            station.Status != StationStatusContract.Available || customerEntryPrompt is null) return;
        var enteredCode = customerEntryPrompt.PromptForCustomerCode(station);
        if (enteredCode is null) return;
        var code = enteredCode.Trim();
        if (code.Length == 0) { setErrorMessage(Text("شناسه مشتری را وارد کنید.", "Enter a Customer ID.")); return; }
        beginBusy();
        setErrorMessage(string.Empty);
        try
        {
            var candidates = await api.GetCustomersAsync(code, includeInactive: true);
            var customer = candidates.SingleOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (customer is null) { setErrorMessage(Text("مشتری با این شناسه پیدا نشد.", "No customer was found with that ID.")); return; }
            if (customer.Kind != CustomerKindContract.Registered || customer.Status != CustomerStatusContract.Active)
            {
                setErrorMessage(customer.Status == CustomerStatusContract.Blocked
                    ? Text("این مشتری مسدود است.", "This customer is blocked.")
                    : Text("برای ورود باید یک مشتری ثبت‌شده و فعال انتخاب شود.", "Entry requires an active registered customer."));
                return;
            }
            var fingerprint = station.Id.ToString("D") + "|" + customer.Id.ToString("D");
            var key = GetKey("session.prepaid-start", fingerprint);
            var session = await api.StartPrepaidSessionAsync(new StartPrepaidSessionRequest(station.Id, customer.Id), key);
            pendingKeys.Remove("session.prepaid-start");
            await onSuccess(station, customer, session);
        }
        catch (GameNetApiException ex) { setErrorMessage(serverErrorText(ex.Code)); }
        catch (HttpRequestException) { setErrorMessage(Text("پاسخ شروع ورود مشتری نامشخص است؛ وضعیت را تازه‌سازی کنید.", "Customer entry result is unknown; refresh to reconcile.")); }
        catch (TaskCanceledException) { setErrorMessage(Text("پاسخی از سرور دریافت نشد؛ پیش از تلاش مجدد وضعیت را تازه‌سازی کنید.", "The Server did not respond; refresh before retrying.")); }
        catch { setErrorMessage(Text("ورود مشتری انجام نشد؛ وضعیت را تازه‌سازی کنید.", "Customer entry failed; refresh to reconcile.")); }
        finally { endBusy(); }
    }

    private string GetKey(string operation, string fingerprint)
    {
        if (pendingKeys.TryGetValue(operation, out var existing) && existing.Fingerprint == fingerprint) return existing.Key;
        var key = Guid.NewGuid().ToString("N");
        pendingKeys[operation] = (fingerprint, key);
        return key;
    }
    private static string Text(string fa, string en) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? fa : en;
}
