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
        public GameClockSnapshot Capture() => new(
            UtcNow,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            System.Diagnostics.Stopwatch.Frequency,
            Environment.TickCount64,
            Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0"));
    }

    private sealed record TestSigningKeySecret(string SigningKey) : IJwtSigningKeySecret;
}
