using System.Windows.Controls;

namespace GameNet.Desktop.Features.Stations;

/// <summary>
/// Presentation-only station workspace. Its DataContext is inherited from the shell
/// so the existing LoginViewModel.StationBoard commands and bindings remain intact.
/// </summary>
public partial class StationBoardView : UserControl
{
    public StationBoardView()
    {
        InitializeComponent();
    }
}
