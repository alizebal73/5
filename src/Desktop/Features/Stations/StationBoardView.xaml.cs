using System.Windows.Controls;

namespace GameNet.Desktop.Features.Stations;

/// <summary>
/// Presentation-only station workspace. It inherits the authenticated shell's LoginViewModel
/// DataContext so existing bindings, commands, filters and server-backed outcomes are preserved.
/// </summary>
public partial class StationBoardView : UserControl
{
    public StationBoardView()
    {
        InitializeComponent();
    }
}
