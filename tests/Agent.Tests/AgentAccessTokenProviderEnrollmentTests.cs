using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentAccessTokenProviderEnrollmentTests
{
    [Fact]
    public async Task First_access_token_request_enrolls_once_then_uses_saved_per_device_credential()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-token-flow-" + Guid.NewGuid().ToString("N"));
        const string variable = "GAMENET_TEST_AGENT_TOKEN_FLOW";
        const string enrollmentToken = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_abcde";
        const string credentialSecret = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_ABCDE";
        var previous = Environment.GetEnvironmentVariable(variable);
        var calls = 0;

        try
        {
            Environment.SetEnvironmentVariable(variable, enrollmentToken);
            var transportOptions = Options.Create(new AgentTransportOptions
            {
                ServerBaseUrl = "http://127.0.0.1:5080",
                EnrollmentTokenEnvironmentVariableName = variable
            });
            var identityOptions = Options.Create(new AgentIdentityOptions
            {
                RootPath = root,
                DeviceId = "station-pc-01"
            });
            var credentialStore = new AgentCredentialStore(identityOptions, transportOptions);

            var handler = new StubHandler(async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref calls);
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/v1/agent/enrollment/redeem")
                {
                    var redeem = await request.Content!.ReadFromJsonAsync<AgentEnrollmentRedeemRequest>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                    Assert.Equal("station-pc-01", redeem!.DeviceId);
                    Assert.Equal(enrollmentToken, redeem.Token);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new AgentCredentialSecretResponse(
                            Guid.NewGuid(), "station-pc-01", credentialSecret, DateTimeOffset.UtcNow))
                    };
                }

                if (path == "/api/v1/agent/auth/token")
                {
                    var tokenRequest = await request.Content!.ReadFromJsonAsync<AgentTokenRequest>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                    Assert.Equal("station-pc-01", tokenRequest!.DeviceId);
                    Assert.Equal(credentialSecret, tokenRequest.Secret);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new AgentTokenResponse(
                            "signed-agent-access-token", DateTimeOffset.UtcNow.AddMinutes(5)))
                    };
                }

                throw new InvalidOperationException("Unexpected Agent credential endpoint: " + path);
            });

            using var client = new HttpClient(handler);
            var factory = new SingleClientFactory(client);
            var bootstrapper = new AgentEnrollmentBootstrapper(factory, credentialStore, transportOptions);
            using var provider = new AgentAccessTokenProvider(
                factory,
                credentialStore,
                bootstrapper,
                transportOptions,
                TimeProvider.System);

            var first = await provider.GetAccessTokenAsync("station-pc-01");
            var second = await provider.GetAccessTokenAsync("station-pc-01");

            Assert.Equal("signed-agent-access-token", first);
            Assert.Equal(first, second);
            Assert.Equal(2, calls); // one enrollment redemption plus one normal access-token request
            Assert.Null(Environment.GetEnvironmentVariable(variable));
            Assert.Equal(credentialSecret, await credentialStore.TryLoadAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
