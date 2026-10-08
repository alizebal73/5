using System.ComponentModel;
using System.Windows;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;

namespace GameNet.Desktop.Shell;

public partial class MainWindow : Window
{
    public MainWindow(IGameNetServerClient serverClient)
    {
        InitializeComponent();
        var viewModel = new LoginViewModel(serverClient);
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        DataContext = viewModel;
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
            viewModel.Password = PasswordInput.Password;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.IsAuthenticated) &&
            sender is LoginViewModel { IsAuthenticated: true })
            PasswordInput.Clear();
    }
}
