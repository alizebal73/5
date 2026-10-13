using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class StationBoardViewBoundaryTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Main_window_hosts_station_board_at_original_row_and_keeps_shell_responsibilities()
    {
        var root = FindRepositoryRoot();
        var mainPath = Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml");
        var main = XDocument.Load(mainPath);

        var host = Assert.Single(main.Descendants(), element => element.Name.LocalName == "StationBoardView");
        Assert.Equal("3", (string?)host.Attribute("Grid.Row"));
        Assert.Equal("clr-namespace:GameNet.Desktop.Features.Stations",
            (string?)main.Root?.Attribute(XNamespace.Xmlns + "stations"));

        var serialized = main.ToString();
        Assert.DoesNotContain("StationBoard.NewCode", serialized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("StationBoard.CreateCommand", serialized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("StationBoard.SelectedStation", serialized, System.StringComparison.Ordinal);

        // Login, PasswordBox handlers, password-change controls, and shell status messages remain in MainWindow.
        Assert.Contains(main.Descendants(), element => element.Name.LocalName == "PasswordBox");
        Assert.Contains(serialized, "PasswordChanged=\"PasswordInput_OnPasswordChanged\"", System.StringComparison.Ordinal);
        Assert.Contains(serialized, "StationBoard.StatusMessage", System.StringComparison.Ordinal);
    }

    [Fact]
    public void Station_board_view_inherits_shell_data_context_and_contains_both_permission_states()
    {
        var root = FindRepositoryRoot();
        var viewPath = Path.Combine(root, "src", "Desktop", "Features", "Stations", "StationBoardView.xaml");
        var view = XDocument.Load(viewPath);
        var rootElement = Assert.IsType<XElement>(view.Root);

        Assert.Equal("UserControl", rootElement.Name.LocalName);
        Assert.Null(rootElement.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "DataContext"));

        var serialized = view.ToString();
        Assert.Contains("StationBoard.CreateCommand", serialized, System.StringComparison.Ordinal);
        Assert.Contains("StationBoard.RefreshCommand", serialized, System.StringComparison.Ordinal);
        Assert.Contains("StationBoard.SelectedStation", serialized, System.StringComparison.Ordinal);
        Assert.Contains("Station.ReadPermissionRequired", serialized, System.StringComparison.Ordinal);
        Assert.Contains("CanReadStations", serialized, System.StringComparison.Ordinal);
        Assert.Contains("BooleanToVisibilityConverter", serialized, System.StringComparison.Ordinal);

        Assert.DoesNotContain(view.Descendants().Attributes(),
            attribute => attribute.Name.LocalName is "Click" or "Loaded" or "PasswordChanged");
        Assert.DoesNotContain(view.Descendants(), element => element.Name.LocalName == "PasswordBox");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[]
        {
            new DirectoryInfo(System.Environment.CurrentDirectory),
            new DirectoryInfo(System.AppContext.BaseDirectory)
        })
        {
            for (DirectoryInfo? directory = start; directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "Desktop")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the GameNet repository root for the StationBoard view boundary tests.");
    }
}
