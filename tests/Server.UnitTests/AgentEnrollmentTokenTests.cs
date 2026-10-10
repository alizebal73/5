using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Persistence.Entities;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AgentEnrollmentTokenTests
{
    [Fact]
    public void Enrollment_token_stores_only_the_hash_and_is_usable_before_expiry()
    {
        var now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        var tokenText = AgentCredentialSecretMaterial.Generate();
        var tokenHash = AgentCredentialSecretMaterial.HashEnrollmentToken(tokenText);
        var token = AgentEnrollmentToken.Create(
            Guid.NewGuid(),
            "pc-01",
            tokenHash,
            Guid.NewGuid(),
            now,
            now.AddMinutes(15));

        Assert.Equal(64, token.TokenHash.Length);
        Assert.NotEqual(tokenText, token.TokenHash);
        Assert.DoesNotContain(tokenText, token.TokenHash, StringComparison.Ordinal);
        Assert.True(token.CanRedeem(now));
        Assert.True(token.CanRedeem(now.AddMinutes(14)));
        Assert.False(token.CanRedeem(now.AddMinutes(15)));
    }

    [Fact]
    public void Enrollment_token_can_be_redeemed_only_once()
    {
        var now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        var token = CreateToken(now);

        token.MarkRedeemed(now.AddMinutes(1));

        Assert.False(token.CanRedeem(now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => token.MarkRedeemed(now.AddMinutes(2)));
    }

    [Fact]
    public void Revoked_or_expired_enrollment_token_cannot_be_redeemed()
    {
        var now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        var revoked = CreateToken(now);
        Assert.True(revoked.Revoke(now.AddMinutes(1)));
        Assert.False(revoked.CanRedeem(now.AddMinutes(2)));
        Assert.False(revoked.Revoke(now.AddMinutes(2)));

        var expired = CreateToken(now);
        Assert.False(expired.CanRedeem(now.AddMinutes(15)));
        Assert.Throws<InvalidOperationException>(() => expired.MarkRedeemed(now.AddMinutes(15)));
    }

    [Fact]
    public void Enrollment_token_hashing_accepts_only_fixed_length_base64url_tokens()
    {
        var raw = AgentCredentialSecretMaterial.Generate();
        var first = AgentCredentialSecretMaterial.HashEnrollmentToken(raw);
        var second = AgentCredentialSecretMaterial.HashEnrollmentToken(raw);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Throws<ArgumentException>(() => AgentCredentialSecretMaterial.HashEnrollmentToken("short"));
        Assert.Throws<ArgumentException>(() => AgentCredentialSecretMaterial.HashEnrollmentToken(new string('=', 43)));
    }

    private static AgentEnrollmentToken CreateToken(DateTimeOffset now) =>
        AgentEnrollmentToken.Create(
            Guid.NewGuid(),
            "pc-01",
            AgentCredentialSecretMaterial.HashEnrollmentToken(AgentCredentialSecretMaterial.Generate()),
            Guid.NewGuid(),
            now,
            now.AddMinutes(15));
}
