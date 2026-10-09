using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;

namespace GameNet.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer stationHealthRefreshTimer;

    public MainWindow(IGameNetServerClient serverClient)
    {
        InitializeComponent();
        var viewModel = new LoginViewModel(serverClient);
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        DataContext = viewModel;

        stationHealthRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        stationHealthRefreshTimer.Tick += (_, _) =>
        {
            if (DataContext is LoginViewModel { IsAuthenticated: true } current &&
                !current.IsBusy &&
                !current.StationBoard.IsBusy)
            {
                current.StationBoard.RefreshCommand.Execute(null);
            }
        };

        Closed += (_, _) =>
        {
            stationHealthRefreshTimer.Stop();
            viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        };
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
            viewModel.Password = PasswordInput.Password;
    }

    private void CurrentPasswordChangeInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
            viewModel.CurrentPassword = CurrentPasswordChangeInput.Password;
    }

    private void NewPasswordChangeInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
            viewModel.NewPassword = NewPasswordChangeInput.Password;
    }

    private void ConfirmPasswordChangeInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel viewModel)
            viewModel.ConfirmNewPassword = ConfirmPasswordChangeInput.Password;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.IsAuthenticated) &&
            sender is LoginViewModel { IsAuthenticated: true })
        {
            PasswordInput.Clear();
            stationHealthRefreshTimer.Start();
        }

        if (e.PropertyName == nameof(LoginViewModel.IsAuthenticated) &&
            sender is LoginViewModel { IsAuthenticated: false })
            stationHealthRefreshTimer.Stop();

        if (e.PropertyName == nameof(LoginViewModel.PasswordChangeSucceeded) &&
            sender is LoginViewModel { PasswordChangeSucceeded: true })
        {
            CurrentPasswordChangeInput.Clear();
            NewPasswordChangeInput.Clear();
            ConfirmPasswordChangeInput.Clear();
        }

        if (e.PropertyName == nameof(LoginViewModel.IsAuthenticated) &&
            sender is LoginViewModel { IsAuthenticated: false })
        {
            CurrentPasswordChangeInput.Clear();
            NewPasswordChangeInput.Clear();
            ConfirmPasswordChangeInput.Clear();
        }
    }
}
