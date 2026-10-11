using Xunit;
using GameNet.Agent;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { ServerBaseUrl = "https://server.example/api" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { ServerBaseUrl = "http://192.168.0.9:5080" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { HeartbeatIntervalSeconds = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions { InitialRetrySeconds = 121 }.Validate());

        Assert.Throws<InvalidOperationException>(() => new AgentTransportOptions().Validate());
        new AgentTransportOptions { ServerBaseUrl = "http://127.0.0.1:5080" }.Validate();
        new AgentTransportOptions { ServerBaseUrl = "https://192.168.0.9:5081" }.Validate();
    }

    [Fact]
    public void Agent_identity_options_expand_environment_variables_before_validating_the_root_path()
    {
        var variableName = "GAMENET_TEST_PROGRAMDATA";
        var previous = Environment.GetEnvironmentVariable(variableName);
        var testRoot = Path.Combine(Path.GetTempPath(), "gamenet-agent-options-" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(variableName, testRoot);
            var options = new AgentIdentityOptions
            {
                RootPath = $"%{variableName}%\\GameNet Manager\\Agent"
            };

            Assert.Equal(
                Path.GetFullPath(Path.Combine(testRoot, "GameNet Manager", "Agent")),
                options.ResolveRootPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Fact]
    public async Task Agent_identity_is_stable_across_store_recreation()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-identity-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new AgentIdentityOptions { RootPath = root });
            var first = await new AgentIdentityStore(options, new TestHostEnvironment()).GetOrCreateAsync();
            var recreated = await new AgentIdentityStore(options, new TestHostEnvironment()).GetOrCreateAsync();

            Assert.Equal(first.DeviceId, recreated.DeviceId);
            Assert.Equal(32, first.DeviceId.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Agent_identity_store_honors_preassigned_device_id_and_rejects_mismatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "gamenet-agent-preassigned-identity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var firstOptions = Options.Create(new AgentIdentityOptions
            {
                RootPath = root,
                DeviceId = "station-pc-01"
            });
            var first = await new AgentIdentityStore(firstOptions, new TestHostEnvironment()).GetOrCreateAsync();
            Assert.Equal("station-pc-01", first.DeviceId);

            var recreated = await new AgentIdentityStore(firstOptions, new TestHostEnvironment()).GetOrCreateAsync();
            Assert.Equal(first.DeviceId, recreated.DeviceId);

            var mismatch = Options.Create(new AgentIdentityOptions
            {
                RootPath = root,
                DeviceId = "station-pc-02"
            });
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AgentIdentityStore(mismatch, new TestHostEnvironment()).GetOrCreateAsync());
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
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GameNet.Agent.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
