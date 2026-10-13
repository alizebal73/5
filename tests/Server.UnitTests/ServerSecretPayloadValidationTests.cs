using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ServerSecretPayloadValidationTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Current_payload_keeps_fingerprint_key_separate_from_jwt_signing_key()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fixture = CreatePayload(includeFingerprintKey: true);
        var payloadBytes = Encoding.UTF8.GetBytes(fixture.Json);
        try
        {
            var actual = DpapiServerSecretStore.ParsePayload(payloadBytes);

            Assert.True(actual.HasDedicatedFingerprintKey);
            Assert.Equal(fixture.ConnectionString, actual.ConnectionString);
            Assert.Equal(fixture.SigningKey, actual.SigningKey);
            Assert.Equal(fixture.ProvisioningKey, actual.ProvisioningKey);
            Assert.Equal(fixture.FingerprintKey, actual.FingerprintKey);
            Assert.NotEqual(actual.SigningKey, actual.FingerprintKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Legacy_payload_without_fingerprint_field_preserves_the_effective_legacy_root()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fixture = CreatePayload(includeFingerprintKey: false);
        var payloadBytes = Encoding.UTF8.GetBytes(fixture.Json);
        try
        {
            var actual = DpapiServerSecretStore.ParsePayload(payloadBytes);

            Assert.False(actual.HasDedicatedFingerprintKey);
            Assert.Equal(fixture.SigningKey, actual.FingerprintKey);
            Assert.Equal(fixture.SigningKey, actual.SigningKey);
            Assert.Equal(fixture.ConnectionString, actual.ConnectionString);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("\"\"")]
    [InlineData("\"fingerprint-sentinel-not-valid-base64\"")]
    [SupportedOSPlatform("windows")]
    public void Explicit_invalid_fingerprint_field_fails_closed_without_echoing_payload_values(string rawJsonValue)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fixture = CreatePayload(includeFingerprintKey: true, fingerprintFieldJsonValue: rawJsonValue);
        var payloadBytes = Encoding.UTF8.GetBytes(fixture.Json);
        try
        {
            var exception = Assert.Throws<ServerSecretStoreException>(
                () => DpapiServerSecretStore.ParsePayload(payloadBytes));

            Assert.DoesNotContain(fixture.ConnectionString, exception.Message);
            Assert.DoesNotContain("connection-password-sentinel", exception.Message);
            Assert.DoesNotContain(fixture.SigningKey, exception.Message);
            Assert.DoesNotContain(fixture.ProvisioningKey, exception.Message);
            Assert.DoesNotContain("fingerprint-sentinel-not-valid-base64", exception.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Duplicate_fingerprint_field_fails_closed_without_echoing_payload_values()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fixture = CreatePayload(includeFingerprintKey: true);
        var field = "\"idempotencyFingerprintKeyBase64\":" + JsonSerializer.Serialize(fixture.FingerprintKey);
        var duplicatedJson = fixture.Json.Replace(field, field + "," + field, StringComparison.Ordinal);
        Assert.NotEqual(fixture.Json, duplicatedJson);

        var payloadBytes = Encoding.UTF8.GetBytes(duplicatedJson);
        try
        {
            var exception = Assert.Throws<ServerSecretStoreException>(
                () => DpapiServerSecretStore.ParsePayload(payloadBytes));

            Assert.DoesNotContain(fixture.ConnectionString, exception.Message);
            Assert.DoesNotContain("connection-password-sentinel", exception.Message);
            Assert.DoesNotContain(fixture.SigningKey, exception.Message);
            Assert.DoesNotContain(fixture.ProvisioningKey, exception.Message);
            Assert.DoesNotContain(fixture.FingerprintKey, exception.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Mis_cased_fingerprint_field_is_not_treated_as_a_legacy_payload()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fixture = CreatePayload(includeFingerprintKey: false);
        var invalidJson = fixture.Json.Replace(
            "}",
            ",\"IdempotencyFingerprintKeyBase64\":" + JsonSerializer.Serialize(fixture.FingerprintKey) + "}",
            StringComparison.Ordinal);
        var payloadBytes = Encoding.UTF8.GetBytes(invalidJson);
        try
        {
            var exception = Assert.Throws<ServerSecretStoreException>(
                () => DpapiServerSecretStore.ParsePayload(payloadBytes));

            Assert.DoesNotContain(fixture.ConnectionString, exception.Message);
            Assert.DoesNotContain("connection-password-sentinel", exception.Message);
            Assert.DoesNotContain(fixture.SigningKey, exception.Message);
            Assert.DoesNotContain(fixture.ProvisioningKey, exception.Message);
            Assert.DoesNotContain(fixture.FingerprintKey, exception.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Malformed_json_fails_closed_without_echoing_the_input()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string sentinel = "raw-secret-payload-sentinel";
        var payloadBytes = Encoding.UTF8.GetBytes("{\"databaseConnectionString\":\"" + sentinel);
        try
        {
            var exception = Assert.Throws<ServerSecretStoreException>(
                () => DpapiServerSecretStore.ParsePayload(payloadBytes));

            Assert.DoesNotContain(sentinel, exception.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    private static PayloadFixture CreatePayload(bool includeFingerprintKey, string? fingerprintFieldJsonValue = null)
    {
        var connectionString = "Host=127.0.0.1;Database=secret_payload_test;Username=test;Password=connection-password-sentinel";
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var provisioningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var fingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["generationId"] = Guid.NewGuid().ToString("N"),
            ["databaseConnectionString"] = connectionString,
            ["jwtSigningKeyBase64"] = signingKey,
            ["agentProvisioningKeyBase64"] = provisioningKey
        };

        if (includeFingerprintKey)
        {
            if (fingerprintFieldJsonValue is null)
            {
                payload["idempotencyFingerprintKeyBase64"] = fingerprintKey;
            }
            else
            {
                using var document = JsonDocument.Parse(fingerprintFieldJsonValue);
                payload["idempotencyFingerprintKeyBase64"] = document.RootElement.Clone();
            }
        }

        return new PayloadFixture(
            JsonSerializer.Serialize(payload),
            connectionString,
            signingKey,
            provisioningKey,
            fingerprintKey);
    }

    private sealed record PayloadFixture(
        string Json,
        string ConnectionString,
        string SigningKey,
        string ProvisioningKey,
        string FingerprintKey);
}
