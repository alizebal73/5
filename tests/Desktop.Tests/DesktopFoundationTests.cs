using Xunit;
using GameNet.Desktop.Infrastructure;
using GameNet.Desktop.Shell;

namespace GameNet.Desktop.Tests;

public sealed class DesktopFoundationTests
{
    [Fact]
    public void Shell_type_exists() => Assert.NotNull(typeof(MainWindow));

    [Theory]
    [InlineData("fa-IR", "fa-IR")]
    [InlineData("en-US", "en-US")]
    [InlineData("unknown", "fa-IR")]
    public void Culture_resolution_is_stable(string requested, string expected)
        => Assert.Equal(expected, DesktopCulture.Resolve(requested).Name);

    [Fact]
    public void Resource_dictionary_is_deterministic_for_supported_cultures()
    {
        Assert.Equal("Strings.fa-IR.xaml", DesktopCulture.GetResourceDictionaryName(DesktopCulture.Resolve("fa-IR")));
        Assert.Equal("Strings.en-US.xaml", DesktopCulture.GetResourceDictionaryName(DesktopCulture.Resolve("en-US")));
    }
}
