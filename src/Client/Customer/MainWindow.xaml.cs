using System.Windows;
using GameNet.CustomerClient.Features;

namespace GameNet.CustomerClient;

public partial class MainWindow : Window
{
    private readonly CustomerClientViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new CustomerClientViewModel(new CustomerEntryPipeClient());
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        viewModel.Password = PasswordInput.Password;
}
