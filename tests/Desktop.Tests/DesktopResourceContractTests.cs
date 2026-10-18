using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GameNet.Desktop.Tests;

/// <summary>
/// Validates the static WPF resource contract without requiring a window or a live Server.
/// The full UI still requires interactive Windows acceptance, but missing translation keys
/// and broken XAML must fail the automated build rather than appear at runtime.
/// </summary>
public sealed class DesktopResourceContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Localization_dictionaries_are_well_formed_and_have_identical_key_sets()
    {
        var root = FindRepositoryRoot();
        var faPath = Path.Combine(root, "src", "Desktop", "Resources", "Strings.fa-IR.xaml");
        var enPath = Path.Combine(root, "src", "Desktop", "Resources", "Strings.en-US.xaml");

        var fa = XDocument.Load(faPath);
        var en = XDocument.Load(enPath);
        var faKeys = GetResourceKeys(fa);
        var enKeys = GetResourceKeys(en);

        Assert.NotEmpty(faKeys);
        Assert.NotEmpty(enKeys);
        Assert.Equal(faKeys.OrderBy(key => key, StringComparer.Ordinal),
            enKeys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_dynamic_resource_in_main_window_and_station_board_exists_in_both_localizations()
    {
        var root = FindRepositoryRoot();
        var viewPaths = new[]
        {
            Path.Combine(root, "src", "Desktop", "Shell", "MainWindow.xaml"),
            Path.Combine(root, "src", "Desktop", "Features", "Stations", "StationBoardView.xaml"),
            Path.Combine(root, "src", "Desktop", "Features", "Sessions", "SessionWorkflowView.xaml")
        };
        var faPath = Path.Combine(root, "src", "Desktop", "Resources", "Strings.fa-IR.xaml");
        var enPath = Path.Combine(root, "src", "Desktop", "Resources", "Strings.en-US.xaml");
        var faKeys = GetResourceKeys(XDocument.Load(faPath));
        var enKeys = GetResourceKeys(XDocument.Load(enPath));

        foreach (var viewPath in viewPaths)
        {
            var view = XDocument.Load(viewPath);
            var referencedKeys = GetResourceReferences(view, "DynamicResource");
            Assert.NotEmpty(referencedKeys);

            var missingFa = referencedKeys.Except(faKeys, StringComparer.Ordinal).OrderBy(key => key).ToArray();
            var missingEn = referencedKeys.Except(enKeys, StringComparer.Ordinal).OrderBy(key => key).ToArray();

            Assert.True(missingFa.Length == 0,
                $"{Path.GetFileName(viewPath)} missing fa-IR resource keys: {string.Join(", ", missingFa)}");
            Assert.True(missingEn.Length == 0,
                $"{Path.GetFileName(viewPath)} missing en-US resource keys: {string.Join(", ", missingEn)}");
        }
    }

    [Fact]
    public void Static_resources_used_by_each_view_resolve_in_that_view_resources()
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
            var localKeys = GetResourceKeys(view);
            var referencedKeys = GetResourceReferences(view, "StaticResource");

            var missing = referencedKeys.Except(localKeys, StringComparer.Ordinal).OrderBy(key => key).ToArray();
            Assert.True(missing.Length == 0,
                $"{Path.GetFileName(viewPath)} has unresolved static resources: {string.Join(", ", missing)}");
        }
    }

    private static HashSet<string> GetResourceKeys(XDocument document) =>
        document.Descendants()
            .Select(element => (string?)element.Attribute(XamlNamespace + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> GetResourceReferences(XDocument document, string resourceKind)
    {
        var pattern = new Regex(
            @"\{" + Regex.Escape(resourceKind) + @"\s+([^,\}\s]+)",
            RegexOptions.CultureInvariant);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in document.Descendants().Attributes())
        {
            foreach (Match match in pattern.Matches(attribute.Value))
            {
                if (match.Success && match.Groups.Count > 1)
                    keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
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
            "Could not locate the GameNet repository root for the Desktop resource contract tests.");
    }
}
