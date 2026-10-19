using System.ComponentModel;
using System.Windows.Input;
using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.Desktop.Features.Wallet;

public interface IWalletTopUpPort : INotifyPropertyChanged
{
    ICommand TopUpCommand { get; }
    bool CanTopUpWallet { get; }
    bool IsBusy { get; }
    bool CanEditInput { get; }
    string ErrorMessage { get; }
    string StatusMessage { get; }
    string BalanceText { get; }
    CustomerResponse? SelectedCustomer { get; set; }
    string AmountToCreditText { get; set; }
    string CashReceivedText { get; set; }
    string SummaryText { get; }
    void NotifyPermissionsChanged();
    void SetCurrentOperatorId(Guid? operatorId);
    void Reset();
}