using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests.Infrastructure.Idempotency;

public sealed class HmacRequestFingerprintTests
{
    private const string ConnectionString = "Host=localhost;Database=gamenet;Username=gamenet";

    [Fact]
    public void Fingerprint_is_stable_for_same_payload_and_never_contains_password()
    {
        var fingerprint = new HmacRequestFingerprint(new TestFingerprintSecret(Key('k')));

        var first = fingerprint.Compute(new { Username = "operator", Password = "Sensitive-Password-123" });
        var replay = fingerprint.Compute(new { Username = "operator", Password = "Sensitive-Password-123" });
        var changed = fingerprint.Compute(new { Username = "operator", Password = "Different-Password-123" });

        Assert.Equal(first, replay);
        Assert.NotEqual(first, changed);
        Assert.StartsWith("hmac-sha256-v1:", first);
        Assert.DoesNotContain("Sensitive-Password-123", first);
    }

    [Fact]
    public void Fingerprint_is_stable_across_jwt_signing_key_rotation_when_dedicated_key_is_retained()
    {
        var payload = new { Operation = "users.create", Password = "Sensitive-Password-123" };
        var dedicatedKey = Key('f');
        var beforeRotation = ServerSecretMaterial.Create(
            ConnectionString, Key('a'), Key('b'), dedicatedKey);
        var afterRotation = ServerSecretMaterial.Create(
            ConnectionString, Key('c'), Key('d'), dedicatedKey);

        var before = new HmacRequestFingerprint(beforeRotation).Compute(payload);
        var after = new HmacRequestFingerprint(afterRotation).Compute(payload);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Legacy_secret_material_keeps_the_previous_fingerprint_root_for_replay_compatibility()
    {
        var legacySigningKey = Key('a');
        var legacy = ServerSecretMaterial.Create(ConnectionString, legacySigningKey, Key('b'));
        var payload = new { Operation = "stations.create", Code = "PC-01" };

        var previous = new HmacRequestFingerprint(new TestFingerprintSecret(legacySigningKey)).Compute(payload);
        var compatible = new HmacRequestFingerprint(legacy).Compute(payload);

        Assert.Equal(previous, compatible);
    }

    [Fact]
    public void Fingerprinting_fails_closed_without_a_strong_key()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new HmacRequestFingerprint(new TestFingerprintSecret("short")));
    }

    private static string Key(char character) => new(character, 64);

    private sealed record TestFingerprintSecret(string FingerprintKey) : IIdempotencyFingerprintSecret;
}
