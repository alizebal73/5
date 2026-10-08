using Xunit;
using GameNet.Desktop.Shell;
namespace GameNet.Desktop.Tests;
public sealed class DesktopFoundationTests
{
    [Fact] public void Shell_type_exists() => Assert.NotNull(typeof(MainWindow));
}
