using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GameNet.Desktop.Features.Identity;
using GameNet.Desktop.Features.Sessions;
using GameNet.Desktop.Features.Stations;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Tariffs;
using Xunit;

namespace GameNet.Desktop.Tests;

/// <summary>
/// Static checks for WPF binding paths and password-control lifecycle wiring. These checks
/// fail fast on broken view-model contracts, but deliberately do not claim interactive UI
/// acceptance (layout, focus traversal, keyboard behavior or real user input).
/// </summary>
public sealed class DesktopBindingAndPasswordContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Every_main_window_and_station_board_binding_path_resolves_on_its_expected_data_context()
    {
        var root = FindRepositoryRoot();
        var viewPaths = new[]
        {
            Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml"),
            Path.Combine(root, "src", "Desktop", "Features", "Stations", "StationBoardView.xaml"),
            Path.Combine(root, "src", "Desktop", "Features", "Sessions", "SessionWorkflowView.xaml")
        };

        foreach (var viewPath in viewPaths)
        {
            var view = XDocument.Load(viewPath);
            var failures = new List<string>();

            foreach (var attribute in view.Descendants().Attributes())
            {
                foreach (Match match in Regex.Matches(attribute.Value, @"\{Binding(?:\s+([^}]+))?\}"))
                {
                    var bindingBody = match.Groups.Count > 1 && match.Groups[1].Success
                        ? match.Groups[1].Value
                        : string.Empty;
                    var path = bindingBody.Split(',')[0].Trim();
                    if (path.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                        path = path["Path=".Length..].Trim();
                    if (string.IsNullOrWhiteSpace(path) || path == ".")
                        continue;

                    var dataTemplate = attribute.Parent?.AncestorsAndSelf()
                        .FirstOrDefault(element => element.Name.LocalName == "DataTemplate");
                    var rootType = dataTemplate is not null
                        ? ResolveDataTemplateItemType(dataTemplate)
                        : string.Equals(Path.GetFileName(viewPath), "SessionWorkflowView.xaml", StringComparison.Ordinal)
                            ? typeof(ISessionWorkflowPort)
                            : typeof(LoginViewModel);

                    if (!TryResolvePath(rootType, path, out var unresolvedSegment))
                    {
                        failures.Add(
                            $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName}: '{path}' " +
                            $"does not resolve at segment '{unresolvedSegment}' on {rootType.Name}.");
                    }
                }
            }

            Assert.True(failures.Count == 0,
                $"{Path.GetFileName(viewPath)} contains invalid WPF binding paths:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }
    }

    [Fact]
    public void Session_workflow_view_is_plugged_into_a_stable_port_not_a_concrete_view_model()
    {
        var portProperty = typeof(LoginViewModel).GetProperty(nameof(LoginViewModel.SessionWorkflow));
        Assert.NotNull(portProperty);
        Assert.Equal(typeof(ISessionWorkflowPort), portProperty!.PropertyType);
        Assert.True(typeof(ISessionWorkflowPort).IsAssignableFrom(typeof(SessionWorkflowViewModel)));
        Assert.NotNull(typeof(ISessionWorkflowPort).GetProperty(nameof(ISessionWorkflowPort.RefreshCommand)));
        Assert.NotNull(typeof(ISessionWorkflowPort).GetProperty(nameof(ISessionWorkflowPort.StartSessionCommand)));
        Assert.NotNull(typeof(ISessionWorkflowPort).GetProperty(nameof(ISessionWorkflowPort.EndSessionCommand)));
        Assert.NotNull(typeof(ISessionWorkflowPort).GetProperty(nameof(ISessionWorkflowPort.CheckoutCashCommand)));

        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(root, "src", "Desktop", "Features", "Sessions", "SessionWorkflowView.xaml"));
        var bindingValues = view.Descendants().Attributes()
            .Select(attribute => attribute.Value)
            .Where(value => value.Contains("{Binding", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(bindingValues);
        Assert.DoesNotContain(bindingValues, value => value.Contains("SessionWorkflow.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            view.Descendants().Attributes().Select(attribute => attribute.Value),
            value => value.Contains("SessionWorkflowViewModel", StringComparison.Ordinal));
        
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml"));
        var plugin = mainWindow.Descendants().FirstOrDefault(element => element.Name.LocalName == "SessionWorkflowView");
        Assert.NotNull(plugin);
        Assert.Equal("{Binding SessionWorkflow}", (string?)plugin!.Attribute("DataContext"));
    }

    [Fact]
    public void Session_and_invoice_rows_show_station_identity_and_disambiguating_context()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(root, "src", "Desktop", "Features", "Sessions", "SessionWorkflowView.xaml"));
        var listBoxes = view.Descendants().Where(element => element.Name.LocalName == "ListBox").ToArray();
        var activeList = listBoxes.Single(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding ActiveSessions}");
        var pendingList = listBoxes.Single(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding PendingInvoices}");

        var activeBindings = activeList.DescendantsAndSelf().Attributes()
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("{Binding StationLabel}", activeBindings);
        Assert.Contains("{Binding TariffNameSnapshot}", activeBindings);
        Assert.Contains("{Binding StartedAtDisplayText}", activeBindings);

        var pendingBindings = pendingList.DescendantsAndSelf().Attributes()
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("{Binding StationLabel}", pendingBindings);
        Assert.Contains("{Binding TariffNameSnapshot}", pendingBindings);
        Assert.Contains("{Binding DurationDisplayText}", pendingBindings);
        Assert.Contains("{Binding AmountDisplayText}", pendingBindings);

        var activeRowType = typeof(ISessionWorkflowPort).GetProperty(nameof(ISessionWorkflowPort.ActiveSessions))!
            .PropertyType.GetGenericArguments().Single();
        var pendingRowType = typeof(ISessionWorkflowPort).GetProperty(nameof(ISessionWorkflowPort.PendingInvoices))!
            .PropertyType.GetGenericArguments().Single();
        Assert.Equal(typeof(ActiveSessionListItem), activeRowType);
        Assert.Equal(typeof(PendingInvoiceListItem), pendingRowType);
    }

    [Fact]
    public void Every_password_box_event_is_wired_to_a_real_handler()
    {
        var root = FindRepositoryRoot();
        var view = XDocument.Load(Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml.cs"));

        var passwordBoxes = view.Descendants()
            .Where(element => element.Name.LocalName == "PasswordBox")
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ConfirmPasswordChangeInput",
                "CurrentPasswordChangeInput",
                "NewPasswordChangeInput",
                "PasswordInput"
            },
            passwordBoxes
                .Select(element => (string?)element.Attribute(XamlNamespace + "Name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        foreach (var box in passwordBoxes)
        {
            var handler = (string?)box.Attribute("PasswordChanged");
            Assert.False(string.IsNullOrWhiteSpace(handler));
            Assert.Contains($"void {handler}(", codeBehind, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Password_controls_are_cleared_after_login_password_change_and_logout()
    {
        var root = FindRepositoryRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml.cs"));

        var loginSuccess = codeBehind.IndexOf(
            "sender is LoginViewModel { IsAuthenticated: true }",
            StringComparison.Ordinal);
        var loginClear = codeBehind.IndexOf("PasswordInput.Clear();", StringComparison.Ordinal);
        Assert.True(loginSuccess >= 0 && loginClear > loginSuccess,
            "The login PasswordBox must be cleared when authentication succeeds.");

        var passwordChangeSuccess = codeBehind.IndexOf(
            "sender is LoginViewModel { PasswordChangeSucceeded: true }",
            StringComparison.Ordinal);
        Assert.True(passwordChangeSuccess >= 0);
        var passwordClearEnd = codeBehind.IndexOf(
            "if (e.PropertyName == nameof(LoginViewModel.IsAuthenticated)",
            passwordChangeSuccess,
            StringComparison.Ordinal);
        Assert.True(passwordClearEnd > passwordChangeSuccess);
        var changePasswordBlock = codeBehind[passwordChangeSuccess..passwordClearEnd];
        Assert.Contains("CurrentPasswordChangeInput.Clear();", changePasswordBlock, StringComparison.Ordinal);
        Assert.Contains("NewPasswordChangeInput.Clear();", changePasswordBlock, StringComparison.Ordinal);
        Assert.Contains("ConfirmPasswordChangeInput.Clear();", changePasswordBlock, StringComparison.Ordinal);

        var logoutTrigger = codeBehind.IndexOf(
            "sender is LoginViewModel { IsAuthenticated: false }",
            passwordClearEnd,
            StringComparison.Ordinal);
        Assert.True(logoutTrigger > passwordClearEnd);
        var logoutBlockEnd = codeBehind.IndexOf("\n        }", logoutTrigger, StringComparison.Ordinal);
        Assert.True(logoutBlockEnd > logoutTrigger);
        var logoutBlock = codeBehind[logoutTrigger..logoutBlockEnd];
        Assert.Contains("CurrentPasswordChangeInput.Clear();", logoutBlock, StringComparison.Ordinal);
        Assert.Contains("NewPasswordChangeInput.Clear();", logoutBlock, StringComparison.Ordinal);
        Assert.Contains("ConfirmPasswordChangeInput.Clear();", logoutBlock, StringComparison.Ordinal);
    }

    private static Type ResolveDataTemplateItemType(XElement dataTemplate)
    {
        var itemsControl = dataTemplate.Ancestors()
            .FirstOrDefault(element => element.Name.LocalName is "ListBox" or "ComboBox");
        var itemsSource = (string?)itemsControl?.Attribute("ItemsSource") ?? string.Empty;
        var match = Regex.Match(itemsSource, @"\{Binding\s+([^,}]+)");
        var bindingPath = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        var memberName = bindingPath.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        if (string.Equals(memberName, "Tariffs", StringComparison.Ordinal))
            return typeof(TariffResponse);
        if (string.Equals(memberName, "ActiveSessions", StringComparison.Ordinal))
            return typeof(ActiveSessionListItem);
        if (string.Equals(memberName, "PendingInvoices", StringComparison.Ordinal))
            return typeof(PendingInvoiceListItem);
        if (string.Equals(memberName, "RecentCashCheckouts", StringComparison.Ordinal))
            return typeof(CashCheckoutOutcomeListItem);
        if (string.Equals(memberName, "VisibleStations", StringComparison.Ordinal))
            return typeof(StationBoardItem);

        throw new InvalidOperationException(
            $"Cannot determine WPF data-template item type from ItemsSource '{itemsSource}'.");
    }

    private static bool TryResolvePath(Type rootType, string path, out string unresolvedSegment)
    {
        var currentType = rootType;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var property = currentType.GetProperty(
                segment,
                BindingFlags.Instance | BindingFlags.Public);

            if (property is null)
            {
                unresolvedSegment = segment;
                return false;
            }

            currentType = property.PropertyType;
        }

        unresolvedSegment = string.Empty;
        return true;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[]
        {
            new DirectoryInfo(Environment.CurrentDirectory),
            new DirectoryInfo(AppContext.BaseDirectory)
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
            "Could not locate the GameNet repository root for the Desktop binding contract tests.");
    }
}
