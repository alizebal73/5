using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Desktop.Features.Sessions;

public sealed partial class SessionWorkflowViewModel
{
    private async Task StartPrepaidSessionFromStationAsync(object? parameter)
    {
        if (parameter is not StationResponse station ||
            station.Type != StationTypeContract.Pc ||
            station.Status != StationStatusContract.Available ||
            customerEntryPrompt is null)
            return;

        var enteredCode = customerEntryPrompt.PromptForCustomerCode(station);
        if (enteredCode is null)
            return;

        var customerCode = enteredCode.Trim();
        if (customerCode.Length == 0)
        {
            ErrorMessage = Text("شناسه مشتری را وارد کنید.", "Enter a Customer ID.");
            return;
        }

        BeginBusy();
        ErrorMessage = string.Empty;
        try
        {
            var candidates = await api.GetCustomersAsync(customerCode, includeInactive: true);
            var customer = candidates.SingleOrDefault(x =>
                string.Equals(x.Code, customerCode, StringComparison.OrdinalIgnoreCase));
            if (customer is null)
            {
                ErrorMessage = Text("مشتری با این شناسه پیدا نشد.", "No customer was found with that ID.");
                return;
            }
            if (customer.Kind != GameNet.Shared.Contracts.V1.Customers.CustomerKindContract.Registered ||
                customer.Status != GameNet.Shared.Contracts.V1.Customers.CustomerStatusContract.Active)
            {
                ErrorMessage = customer.Status == GameNet.Shared.Contracts.V1.Customers.CustomerStatusContract.Blocked
                    ? Text("این مشتری مسدود است.", "This customer is blocked.")
                    : Text("برای ورود باید یک مشتری ثبت‌شده و فعال انتخاب شود.", "Entry requires an active registered customer.");
                return;
            }

            var fingerprint = $"{station.Id:D}|{customer.Id:D}";
            var key = GetKey("session.prepaid-start", fingerprint);
            var session = await api.StartPrepaidSessionAsync(
                new StartPrepaidSessionRequest(station.Id, customer.Id), key);
            pendingKeys.Remove("session.prepaid-start");

            await RefreshAsync();
            SelectedActiveSession = ActiveSessions.FirstOrDefault(x => x.Session.Id == session.Id);
            StatusMessage = Text(
                $"مشتری {customer.Code} روی {station.Code} وارد شد؛ اعتبار رزروشده {FormatToman(session.PrepaidReservedAmountToman ?? 0)} تومان.",
                $"Customer {customer.Code} started on {station.Code}; {FormatToman(session.PrepaidReservedAmountToman ?? 0)} Toman was reserved.");
        }
        catch (GameNetApiException ex) { ErrorMessage = ErrorText(ex.Code); }
        catch (HttpRequestException)
        {
            ErrorMessage = Text("پاسخ شروع ورود مشتری نامشخص است؛ وضعیت را تازه‌سازی کنید.", "Customer entry result is unknown; refresh to reconcile.");
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = Text("پاسخی از سرور دریافت نشد؛ پیش از تلاش مجدد وضعیت را تازه‌سازی کنید.", "The Server did not respond; refresh before retrying.");
        }
        catch
        {
            ErrorMessage = Text("ورود مشتری انجام نشد؛ وضعیت را تازه‌سازی کنید.", "Customer entry failed; refresh to reconcile.");
        }
        finally { EndBusy(); }
    }
}
