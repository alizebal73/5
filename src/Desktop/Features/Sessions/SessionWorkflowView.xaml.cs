using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GameNet.Desktop.Features.Customers;

namespace GameNet.Desktop.Features.Sessions;

public partial class SessionWorkflowView : UserControl
{
    private SessionWorkflowViewModel? subscribedViewModel;

    public SessionWorkflowView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnWorkflowPropertyChanged;
            subscribedViewModel.CustomerCredentials.PropertyChanged -= OnCredentialsPropertyChanged;
        }

        subscribedViewModel = e.NewValue as SessionWorkflowViewModel;
        if (subscribedViewModel is null) return;
        subscribedViewModel.PropertyChanged += OnWorkflowPropertyChanged;
        subscribedViewModel.CustomerCredentials.PropertyChanged += OnCredentialsPropertyChanged;
        CustomerLoginPasswordInput.Clear();
    }

    private void OnWorkflowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionWorkflowViewModel.SelectedManagedCustomer))
            CustomerLoginPasswordInput.Clear();
    }

    private void OnCredentialsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomerCredentialsViewModel.InputResetVersion))
            CustomerLoginPasswordInput.Clear();
    }

    private void CustomerLoginPasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SessionWorkflowViewModel viewModel)
            viewModel.CustomerCredentials.Password = CustomerLoginPasswordInput.Password;
    }
}