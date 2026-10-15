using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Stable UI port for the Session-to-cash capability. Replaceable WPF views/templates bind
/// to this contract instead of the concrete ViewModel. Commands retain the same API and
/// business-operation path regardless of layout, tab order, or button placement.
/// </summary>
public interface ISessionWorkflowPort : INotifyPropertyChanged
{
    ObservableCollection<StationResponse> PcStations { get; }
    ObservableCollection<TariffResponse> Tariffs { get; }
    ObservableCollection<ActiveSessionListItem> ActiveSessions { get; }
    ObservableCollection<PendingInvoiceListItem> PendingInvoices { get; }

    ICommand RefreshCommand { get; }
    ICommand CreateTariffCommand { get; }
    ICommand StartSessionCommand { get; }
    ICommand EndSessionCommand { get; }
    ICommand CheckoutCashCommand { get; }

    bool CanManageTariffs { get; }
    bool CanOperateSessions { get; }
    bool CanCheckout { get; }
    bool CanUseWorkspace { get; }
    bool CanReadStations { get; }
    bool IsBusy { get; }

    StationResponse? SelectedStation { get; set; }
    TariffResponse? SelectedTariff { get; set; }
    ActiveSessionListItem? SelectedActiveSession { get; set; }
    PendingInvoiceListItem? SelectedPendingInvoice { get; set; }

    string TariffCode { get; set; }
    string TariffName { get; set; }
    string HourlyRateText { get; set; }
    string AmountReceivedText { get; set; }
    bool ConfirmCashCheckout { get; set; }
    string CashCheckoutSummaryText { get; }
    bool IsDefaultTariff { get; set; }
    string ErrorMessage { get; }
    string StatusMessage { get; }
    string SelectedInvoiceAmountText { get; }
    string SelectedSessionText { get; }

    Task RefreshAsync();
    void NotifyPermissionsChanged();
    void Reset();
}
