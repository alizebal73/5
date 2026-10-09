using Xunit;
using GameNet.Agent;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;
namespace GameNet.Agent.Tests;
public sealed class AgentFoundationTests
{
    [Fact]
    public void Agent_worker_type_exists() => Assert.NotNull(typeof(AgentWorker));

    [Theory]
    [InlineData("")]
    [InlineData("bad/device")]
    [InlineData("bad device")]
    public void Agent_identity_rejects_invalid_device_ids(string deviceId) =>
        Assert.Throws<ArgumentException>(() => AgentIdentity.FromDeviceId(deviceId));

    [Fact]
    public void Agent_identity_rejects_device_ids_over_protocol_limit() =>
        Assert.Throws<ArgumentException>(() => AgentIdentity.FromDeviceId(new string('a', 129)));

    [Fact]
    public void Agent_transport_options_reject_invalid_urls_and_retry_intervals()
    {
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { ServerBaseUrl = "file:///tmp/gamenet" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { ServerBaseUrl = "https://user:secret@server.example" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { HeartbeatIntervalSeconds = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { InitialRetrySeconds = 121 }.Validate());
        new AgentTransportOptions().Validate();
    }

    [Fact]
    public async Task Agent_identity_is_stable_across_store_recreation()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-identity-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new AgentIdentityOptions { RootPath = root });
            var first = await new AgentIdentityStore(options).GetOrCreateAsync();
            var recreated = await new AgentIdentityStore(options).GetOrCreateAsync();

            Assert.Equal(first.DeviceId, recreated.DeviceId);
            Assert.Equal(32, first.DeviceId.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Agent_credential_round_trip_survives_store_recreation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "gamenet-agent-credential-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            var identity = Options.Create(new AgentIdentityOptions { RootPath = root });
            var transport = Options.Create(new AgentTransportOptions());
            const string secret = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

            var firstStore = new AgentCredentialStore(identity, transport);
            await firstStore.SaveAsync(secret);

            var variableName = "GAMENET_AGENT_TEST_BOOTSTRAP_SECRET";
            var previousBootstrap = Environment.GetEnvironmentVariable(variableName);
            try
            {
                Environment.SetEnvironmentVariable(variableName, "must-not-replace-the-persisted-credential");
                var configuredTransport = Options.Create(new AgentTransportOptions
                {
                    BootstrapCredentialEnvironmentVariableName = variableName
                });
                var recreatedStore = new AgentCredentialStore(identity, configuredTransport);
                var loaded = await recreatedStore.GetOrBootstrapAsync();

                Assert.Equal(secret, loaded);
                Assert.Null(Environment.GetEnvironmentVariable(variableName));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variableName, previousBootstrap);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
