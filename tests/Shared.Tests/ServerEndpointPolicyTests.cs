using GameNet.Shared.Runtime;
using Xunit;

namespace GameNet.Shared.Tests;

public sealed class ServerEndpointPolicyTests
{
    [Theory]
    [InlineData("https://192.168.0.9:5080")]
    [InlineData("https://gamenet.local:5080")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://localhost:5080")]
    [InlineData("http://[::1]:5080")]
    public void Accepts_https_network_endpoints_and_http_loopback_only(string endpoint)
        => Assert.True(ServerEndpointPolicy.IsValidBaseUrl(endpoint));

    [Theory]
    [InlineData("http://192.168.0.9:5080")]
    [InlineData("http://gamenet.local:5080")]
    [InlineData("http://0.0.0.0:5080")]
    [InlineData("https://0.0.0.0:5080")]
    [InlineData("ftp://gamenet.local:5080")]
    [InlineData("not-a-uri")]
    [InlineData("https://user:password@gamenet.local:5080")]
    [InlineData("https://gamenet.local:5080/api")]
    [InlineData("https://gamenet.local:5080?mode=unsafe")]
    public void Rejects_insecure_or_ambiguous_endpoints(string endpoint)
        => Assert.False(ServerEndpointPolicy.IsValidBaseUrl(endpoint));
}
