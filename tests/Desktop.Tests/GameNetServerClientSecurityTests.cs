using System.Net;
using GameNet.Desktop.Api;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class GameNetServerClientSecurityTests
{
    [Fact]
    public async Task Login_does_not_send_password_to_remote_http_server()
    {
        var handler = new NeverSendHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://192.0.2.20:5080/") };
        var client = new GameNetServerClient(httpClient);
        var exception = await Assert.ThrowsAsync<GameNetApiException>(
            () => client.LoginAsync(new LoginRequest("operator", "strong-example-password")));
        Assert.Equal("security.https_required", exception.Code);
        Assert.Equal(426, exception.StatusCode);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class NeverSendHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Sensitive login request must never be sent over remote HTTP.");
        }
    }
    [Fact]
    public async Task Api_client_does_not_duplicate_the_contract_header_configured_by_desktop_host()
    {
        var handler = new HeaderCaptureHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5096/") };
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(ApiHeaders.ContractVersion, ContractVersions.V1);
        var client = new GameNetServerClient(httpClient);

        await Assert.ThrowsAsync<GameNetApiException>(() => client.GetHealthAsync());

        Assert.Equal(new[] { ContractVersions.V1 }, handler.ContractVersions);
    }

    private sealed class HeaderCaptureHandler : HttpMessageHandler
    {
        public string[] ContractVersions { get; private set; } = Array.Empty<string>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ContractVersions = request.Headers.TryGetValues(ApiHeaders.ContractVersion, out var values)
                ? values.ToArray()
                : Array.Empty<string>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}")
            });
        }
    }

}
