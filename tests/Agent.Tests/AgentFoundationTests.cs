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

            var recreatedStore = new AgentCredentialStore(identity, transport);
            var loaded = await recreatedStore.GetOrBootstrapAsync();

            Assert.Equal(secret, loaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
