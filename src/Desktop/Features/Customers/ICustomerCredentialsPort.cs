using System.ComponentModel;
using System.Windows.Input;
using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.Desktop.Features.Customers;

/// <summary>
/// Stable plug/socket contract for operator-managed customer credentials. Views and the
/// Session workspace depend on this contract, not the concrete credentials ViewModel.
/// </summary>
public interface ICustomerCredentialsPort : INotifyPropertyChanged
{
    CustomerResponse? SelectedCustomer { get; set; }
    bool CanManageCredentials { get; }
    string Username { get; set; }
    string Password { get; set; }
    string ErrorMessage { get; }
    string StatusMessage { get; }
    int InputResetVersion { get; }
    ICommand SetCredentialsCommand { get; }
    void NotifyPermissionsChanged();
}
