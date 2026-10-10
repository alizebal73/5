using System.Windows;

namespace GameNet.Desktop.Shell;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.RefreshServerStatusAsync();
    }
}
