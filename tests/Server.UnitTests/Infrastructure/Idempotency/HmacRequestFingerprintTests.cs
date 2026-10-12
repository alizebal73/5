using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests.Infrastructure.Idempotency;

public sealed class HmacRequestFingerprintTests
{
    [Fact]
    public void Fingerprint_is_stable_for_same_payload_and_never_contains_password()
    {
        var fingerprint = new HmacRequestFingerprint(new TestSigningKeySecret(new string('k', 48)));

        var first = fingerprint.Compute(new { Username = "operator", Password = "Sensitive-Password-123" });
        var replay = fingerprint.Compute(new { Username = "operator", Password = "Sensitive-Password-123" });
        var changed = fingerprint.Compute(new { Username = "operator", Password = "Different-Password-123" });

        Assert.Equal(first, replay);
        Assert.NotEqual(first, changed);
        Assert.StartsWith("hmac-sha256-v1:", first);
        Assert.DoesNotContain("Sensitive-Password-123", first);
    }

    [Fact]
    public void Fingerprint_changes_when_server_key_rotates()
    {
        var payload = new { Operation = "users.create", Password = "Sensitive-Password-123" };
        var first = Create('a').Compute(payload);
        var rotated = Create('b').Compute(payload);
        Assert.NotEqual(first, rotated);
    }

    [Fact]
    public void Fingerprinting_fails_closed_without_a_strong_key()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new HmacRequestFingerprint(new TestSigningKeySecret("short")));
    }

    private static HmacRequestFingerprint Create(char key) =>
        new(new TestSigningKeySecret(new string(key, 48)));

    private sealed record TestSigningKeySecret(string SigningKey) : IJwtSigningKeySecret;
}
