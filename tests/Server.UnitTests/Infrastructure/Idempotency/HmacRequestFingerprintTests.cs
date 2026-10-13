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
    public void Legacy_secret_migration_preserves_fingerprints_for_retained_idempotency_records()
    {
        var legacySigningKey = Key('a');
        var legacy = ServerSecretMaterial.Create(ConnectionString, legacySigningKey, Key('b'));
        var migrated = ServerSecretMaterial.Create(
            ConnectionString, legacySigningKey, Key('b'), legacySigningKey);
        var payload = new { Operation = "users.create", Password = "Sensitive-Password-123" };

        Assert.False(legacy.HasDedicatedFingerprintKey);
        Assert.True(migrated.HasDedicatedFingerprintKey);
        Assert.Equal(
            new HmacRequestFingerprint(legacy).Compute(payload),
            new HmacRequestFingerprint(migrated).Compute(payload));
    }

    [Fact]
    public void Legacy_protected_payload_migration_round_trips_and_preserves_fingerprints()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var signingKey = Key('a');
        var provisioningKey = Key('b');
        var generationId = Guid.NewGuid().ToString("N");
        var legacyJson = "{\"schemaVersion\":1,\"generationId\":\"" + generationId +
            "\",\"databaseConnectionString\":\"" + ConnectionString +
            "\",\"jwtSigningKeyBase64\":\"" + signingKey +
            "\",\"agentProvisioningKeyBase64\":\"" + provisioningKey + "\"}";

        var legacy = DpapiServerSecretStore.ParsePayload(System.Text.Encoding.UTF8.GetBytes(legacyJson));
        Assert.False(legacy.HasDedicatedFingerprintKey);
        var payload = new { Operation = "users.create", Password = "Sensitive-Password-123" };
        var priorFingerprint = new HmacRequestFingerprint(legacy).Compute(payload);

        // Migration material uses the prior root, not a new random key, so stored rows still replay.
        var migrated = ServerSecretMaterial.Create(
            legacy.ConnectionString, legacy.SigningKey, legacy.ProvisioningKey, legacy.SigningKey);
        var protectedBytes = DpapiServerSecretStore.ProtectPayload(migrated);
        try
        {
            var reloaded = DpapiServerSecretStore.UnprotectPayload(protectedBytes);
            Assert.True(reloaded.HasDedicatedFingerprintKey);
            Assert.Equal(priorFingerprint, new HmacRequestFingerprint(reloaded).Compute(payload));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    [Fact]
    public void Explicit_null_fingerprint_field_in_legacy_payload_fails_closed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var malformedJson = "{\"schemaVersion\":1,\"generationId\":\"" + Guid.NewGuid().ToString("N") +
            "\",\"databaseConnectionString\":\"" + ConnectionString +
            "\",\"jwtSigningKeyBase64\":\"" + Key('a') +
            "\",\"agentProvisioningKeyBase64\":\"" + Key('b') +
            "\",\"idempotencyFingerprintKeyBase64\":null}";

        Assert.Throws<ServerSecretStoreException>(() =>
            DpapiServerSecretStore.ParsePayload(System.Text.Encoding.UTF8.GetBytes(malformedJson)));
    }

    [Fact]
    public void Fingerprinting_fails_closed_without_a_strong_key()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new HmacRequestFingerprint(new TestFingerprintSecret("short")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void Malformed_explicit_fingerprint_key_fails_closed(string fingerprintKey)
    {
        Assert.Throws<ServerSecretStoreException>(() =>
            ServerSecretMaterial.Create(ConnectionString, Key('a'), Key('b'), fingerprintKey));
    }

    private static string Key(char character) => new(character, 64);

    private sealed record TestFingerprintSecret(string FingerprintKey) : IIdempotencyFingerprintSecret;
}
