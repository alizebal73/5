using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Security;
using GameNet.Shared.Primitives;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AgentAccessTokenIssuerTests
{
    [Fact]
    public void Issuer_rejects_noncanonical_device_ids_and_empty_credential_ids()
    {
        var issuer = new AgentAccessTokenIssuer(
            Options.Create(new GameNetOptions()),
            new FixedClock(),
            new TestSigningKeySecret(new string('k', 48)));

        Assert.Throws<ArgumentException>(() => issuer.Issue("bad/device", Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => issuer.Issue("pc-01", Guid.Empty));
    }

    private sealed class FixedClock : IGameClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed record TestSigningKeySecret(string SigningKey) : IJwtSigningKeySecret;
}
