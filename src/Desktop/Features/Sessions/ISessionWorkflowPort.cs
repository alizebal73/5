using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Desktop.Features.Wallet;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>
/// Stable UI port for the Session-to-cash capability. Replaceable WPF views/templates bind
/// to this contract instead of the concrete ViewModel. Commands retain the same API and
/// business-operation path regardless of layout, tab order, or button placement.
/// </summary>
public interface ISessionWorkflowPort : INotifyPropertyChanged
{
    ObservableCollection<StationResponse> PcStations { get; }
    ObservableCollection<CustomerResponse> Customers { get; }
    ObservableCollection<CustomerResponse> AvailableCustomers { get; }
    ObservableCollection<TariffResponse> Tariffs { get; }
    ObservableCollection<ActiveSessionListItem> ActiveSessions { get; }
    ObservableCollection<PendingInvoiceListItem> PendingInvoices { get; }
    ObservableCollection<CashCheckoutOutcomeListItem> RecentCashCheckouts { get; }
    IWalletTopUpPort WalletTopUp { get; }

    ICommand RefreshCommand { get; }
    ICommand SearchCustomersCommand { get; }
    ICommand SearchSessionCustomersCommand { get; }
    ICommand NewCustomerCommand { get; }
    ICommand UseWalkInCustomerCommand { get; }
    ICommand CreateCustomerCommand { get; }
    ICommand UpdateCustomerCommand { get; }
    ICommand ActivateCustomerCommand { get; }
    ICommand DeactivateCustomerCommand { get; }
    ICommand BlockCustomerCommand { get; }
    ICommand CreateTariffCommand { get; }
    ICommand StartSessionCommand { get; }
    ICommand EndSessionCommand { get; }
    ICommand ReconcileSessionTimingCommand { get; }
    ICommand CheckoutCashCommand { get; }
    ICommand ReverseCashSaleCommand { get; }
    ICommand CorrectCashTenderCommand { get; }

    bool CanManageTariffs { get; }
    bool CanReadCustomers { get; }
    bool CanWriteCustomers { get; }
    bool CanOperateSessions { get; }
    bool CanRecoverSessionTiming { get; }
    bool CanCheckout { get; }
    bool CanReverseCashSale { get; }
    bool CanCorrectCashTender { get; }
    bool CanUseWorkspace { get; }
    bool CanReadStations { get; }
    bool IsBusy { get; }

    StationResponse? SelectedStation { get; set; }
    CustomerResponse? SelectedCustomer { get; set; }
    CustomerResponse? SelectedManagedCustomer { get; set; }
    bool IsNewCustomer { get; }
    string CustomerSearchText { get; set; }
    string SessionCustomerSearchText { get; set; }
    string CustomerCodeText { get; set; }
    string CustomerNameText { get; set; }
    string CustomerNicknameText { get; set; }
    string CustomerPhoneText { get; set; }
    string CustomerStatusReasonText { get; set; }
    bool CustomerIncludeInactive { get; set; }
    string CustomerStatusSummaryText { get; }
    TariffResponse? SelectedTariff { get; set; }
    ActiveSessionListItem? SelectedActiveSession { get; set; }
    string ReconciledElapsedMinutesText { get; set; }
    string TimingRecoveryReasonText { get; set; }
    bool ConfirmTimingRecovery { get; set; }
    PendingInvoiceListItem? SelectedPendingInvoice { get; set; }
    CashCheckoutOutcomeListItem? SelectedRecentCashCheckout { get; set; }
    string CashSaleReversalReasonText { get; set; }
    bool ConfirmCashSaleReversal { get; set; }
    string CashSaleReversalSummaryText { get; }
    string CorrectedCashReceivedText { get; set; }
    string CashTenderCorrectionReasonText { get; set; }
    bool ConfirmCashTenderCorrection { get; set; }
    string CashTenderCorrectionSummaryText { get; }

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
