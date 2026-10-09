using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ServerTlsConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-a-thumbprint")]
    [InlineData("0123456789")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void Certificate_loader_rejects_invalid_thumbprints_before_store_lookup(string thumbprint)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ServerTlsCertificateLoader.LoadFromLocalMachine(thumbprint));
        Assert.Contains("thumbprint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
