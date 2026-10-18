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
    public void Main_window_keeps_session_workflow_visible_and_station_administration_collapsed()
    {
        var root = FindRepositoryRoot();
        var mainPath = Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml");
        var main = XDocument.Load(mainPath);

        var sessionView = Assert.Single(main.Descendants(),
            element => element.Name.LocalName == "SessionWorkflowView");

        // The session/invoice/cash workflow must remain visible without expanding a secondary section.
        Assert.DoesNotContain(sessionView.Ancestors(),
            element => element.Name.LocalName == "Expander");
        Assert.Contains(sessionView.Ancestors(),
            element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("{Binding SessionWorkflow}",
            (string?)sessionView.Attribute("DataContext"));

        var stationView = Assert.Single(main.Descendants(),
            element => element.Name.LocalName == "StationBoardView");
        var stationAdminHost = Assert.Single(main.Descendants(),
            element => element.Name.LocalName == "Expander" &&
                       element.Descendants().Any(child => child.Name.LocalName == "StationBoardView"));

        // Infrequent station administration is collapsed by default, not placed ahead of checkout.
        Assert.Equal("{DynamicResource Station.Title}",
            (string?)stationAdminHost.Attribute("Header"));
        Assert.Equal("False", (string?)stationAdminHost.Attribute("IsExpanded"));
        Assert.Contains(stationView.Ancestors(),
            element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("clr-namespace:GameNet.Desktop.Features.Stations",
            (string?)main.Root?.Attribute(XNamespace.Xmlns + "stations"));

        var serialized = main.ToString();
        Assert.DoesNotContain("StationBoard.NewCode", serialized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("StationBoard.CreateCommand", serialized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("StationBoard.SelectedStation", serialized, System.StringComparison.Ordinal);

        // Login, PasswordBox handlers, password-change controls, and shell status messages remain in MainWindow.
        Assert.Contains(main.Descendants(), element => element.Name.LocalName == "PasswordBox");
        Assert.Contains("PasswordChanged=\"PasswordInput_OnPasswordChanged\"", serialized, System.StringComparison.Ordinal);
        Assert.Contains("StationBoard.StatusMessage", serialized, System.StringComparison.Ordinal);
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
        Assert.Contains("StationBoard.RenameCommand", serialized, System.StringComparison.Ordinal);
        Assert.Contains("StationBoard.SelectedStation", serialized, System.StringComparison.Ordinal);
        Assert.Contains("Station.ReadPermissionRequired", serialized, System.StringComparison.Ordinal);
        Assert.Contains("CanReadStations", serialized, System.StringComparison.Ordinal);
        Assert.Contains("BooleanToVisibilityConverter", serialized, System.StringComparison.Ordinal);

        Assert.DoesNotContain(view.Descendants().Attributes(),
            attribute => attribute.Name.LocalName is "Click" or "Loaded" or "PasswordChanged");
        Assert.DoesNotContain(view.Descendants(), element => element.Name.LocalName == "PasswordBox");
    }


    [Fact]
    public void Session_workflow_keeps_pending_invoices_visible_outside_secondary_expanders()
    {
        var root = FindRepositoryRoot();
        var sessionPath = Path.Combine(root, "src", "Desktop", "Features", "Sessions", "SessionWorkflowView.xaml");
        var session = XDocument.Load(sessionPath);

        var pendingList = Assert.Single(session.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding PendingInvoices}");

        Assert.Equal("{Binding SelectedPendingInvoice, Mode=TwoWay}",
            (string?)pendingList.Attribute("SelectedItem"));
        Assert.DoesNotContain(pendingList.Ancestors(),
            element => element.Name.LocalName == "Expander");
        Assert.Contains(pendingList.Ancestors(),
            element => element.Name.LocalName == "Border" &&
                       (string?)element.Attribute("Grid.Column") == "2");
        Assert.Contains(session.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding PendingInvoices.Count}");
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
