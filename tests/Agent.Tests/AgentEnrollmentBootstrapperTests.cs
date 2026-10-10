using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentEnrollmentBootstrapperTests
{
    [Fact]
    public async Task Redeems_one_time_token_persists_credential_and_clears_token_only_after_success()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamenet-enrollment-test-" + Guid.NewGuid().ToString("N"));
        const string variable = "GAMENET_TEST_AGENT_ENROLLMENT_TOKEN";
        const string enrollmentToken = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_abcde";
        const string secret = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_ABCDE";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            Environment.SetEnvironmentVariable(variable, enrollmentToken);
            var store = new AgentCredentialStore(
                Options.Create(new AgentIdentityOptions { RootPath = root }),
                Options.Create(new AgentTransportOptions { EnrollmentTokenEnvironmentVariableName = variable }));

            AgentEnrollmentRedeemRequest? sent = null;
            var handler = new StubHandler(async (request, cancellationToken) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/api/v1/agent/enrollment/redeem", request.RequestUri!.AbsolutePath);
                Assert.Equal("v1", request.Headers.GetValues("X-GameNet-Contract").Single());
                sent = await request.Content!.ReadFromJsonAsync<AgentEnrollmentRedeemRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                var response = new AgentCredentialSecretResponse(
                    Guid.NewGuid(), "pc-01", secret, DateTimeOffset.Parse("2026-10-10T12:00:00Z"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(response)
                };
            });
            using var client = new HttpClient(handler);
            var bootstrapper = new AgentEnrollmentBootstrapper(
                new SingleClientFactory(client),
                store,
                new AgentEnrollmentTokenStore(
                    Options.Create(new AgentIdentityOptions { RootPath = root }), TimeProvider.System),
                new TestHostEnvironment(),
                Options.Create(new AgentTransportOptions
                {
                    ServerBaseUrl = "http://127.0.0.1:5080",
                    EnrollmentTokenEnvironmentVariableName = variable
                }));

            var result = await bootstrapper.EnrollIfConfiguredAsync("pc-01");

            Assert.Equal(secret, result);
            Assert.NotNull(sent);
            Assert.Equal("pc-01", sent!.DeviceId);
            Assert.Equal(enrollmentToken, sent.Token);
            Assert.Null(Environment.GetEnvironmentVariable(variable));
            Assert.Equal(secret, await store.TryLoadAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public async Task Does_not_consume_token_or_persist_credential_if_server_returns_another_device()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamenet-enrollment-test-" + Guid.NewGuid().ToString("N"));
        const string variable = "GAMENET_TEST_AGENT_ENROLLMENT_TOKEN_MISMATCH";
        const string enrollmentToken = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_abcde";
        const string secret = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_ABCDE";
        var previous = Environment.GetEnvironmentVariable(variable);

        try
        {
            Environment.SetEnvironmentVariable(variable, enrollmentToken);
            var store = new AgentCredentialStore(
                Options.Create(new AgentIdentityOptions { RootPath = root }),
                Options.Create(new AgentTransportOptions { EnrollmentTokenEnvironmentVariableName = variable }));
            var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AgentCredentialSecretResponse(
                    Guid.NewGuid(), "pc-02", secret, DateTimeOffset.UtcNow))
            }));
            using var client = new HttpClient(handler);
            var bootstrapper = new AgentEnrollmentBootstrapper(
                new SingleClientFactory(client),
                store,
                new AgentEnrollmentTokenStore(
                    Options.Create(new AgentIdentityOptions { RootPath = root }), TimeProvider.System),
                new TestHostEnvironment(),
                Options.Create(new AgentTransportOptions
                {
                    ServerBaseUrl = "http://127.0.0.1:5080",
                    EnrollmentTokenEnvironmentVariableName = variable
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => bootstrapper.EnrollIfConfiguredAsync("pc-01"));

            Assert.Equal(enrollmentToken, Environment.GetEnvironmentVariable(variable));
            Assert.Null(await store.TryLoadAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public async Task Leaves_legacy_provisioning_path_available_when_no_enrollment_token_is_configured()
    {
        const string variable = "GAMENET_TEST_AGENT_ENROLLMENT_TOKEN_ABSENT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            var handler = new StubHandler((_, _) =>
                throw new InvalidOperationException("An HTTP request must not be sent without an enrollment token."));
            using var client = new HttpClient(handler);
            var store = new AgentCredentialStore(
                Options.Create(new AgentIdentityOptions
                {
                    RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
                }),
                Options.Create(new AgentTransportOptions()));
            var bootstrapper = new AgentEnrollmentBootstrapper(
                new SingleClientFactory(client),
                store,
                Options.Create(new AgentTransportOptions
                {
                    ServerBaseUrl = "http://127.0.0.1:5080",
                    EnrollmentTokenEnvironmentVariableName = variable
                }));

            Assert.Null(await bootstrapper.EnrollIfConfiguredAsync("pc-01"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GameNet.Agent.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
