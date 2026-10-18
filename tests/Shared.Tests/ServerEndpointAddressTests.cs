using GameNet.Shared.Primitives;
using Xunit;

namespace GameNet.Shared.Tests;

public sealed class ServerEndpointAddressTests
{
    [Theory]
    [InlineData("https://192.168.0.9:5081")]
    [InlineData("https://gamenet.example:5081")]
    [InlineData("https://gamenet.example/")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://localhost:5080")]
    [InlineData("http://[::1]:5080")]
    public void Accepts_https_origins_and_loopback_http(string value) =>
        Assert.True(ServerEndpointAddress.IsAllowed(value));

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("file:///tmp/gamenet")]
    [InlineData("http://192.168.0.9:5080")]
    [InlineData("http://gamenet.example:5080")]
    [InlineData("https://user:secret@gamenet.example:5081")]
    [InlineData("https://gamenet.example/api")]
    [InlineData("https://gamenet.example/?x=1")]
    [InlineData("https://gamenet.example/#fragment")]
    public void Rejects_invalid_or_insecure_server_origins(string value) =>
        Assert.False(ServerEndpointAddress.IsAllowed(value));
}
