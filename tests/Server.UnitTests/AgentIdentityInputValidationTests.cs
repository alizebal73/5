using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AgentIdentityInputValidationTests
{
    [Theory]
    [InlineData("pc-01")]
    [InlineData("192.168.0.109")]
    [InlineData("station_01")]
    [InlineData("agent.v1")]
    public void Canonical_device_ids_are_accepted(string deviceId)
    {
        Assert.True(AgentDeviceIdentityRules.IsValid(deviceId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad/device")]
    [InlineData("bad device")]
    [InlineData("bad:device")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("line\nbreak")]
    [InlineData("دستگاه")]
    public void Unsupported_device_ids_are_rejected(string deviceId)
    {
        Assert.False(AgentDeviceIdentityRules.IsValid(deviceId));
    }

    [Fact]
    public void Device_id_cannot_exceed_the_protocol_limit()
    {
        Assert.True(AgentDeviceIdentityRules.IsValid(new string('a', 128)));
        Assert.False(AgentDeviceIdentityRules.IsValid(new string('a', 129)));
    }

    [Fact]
    public void Only_canonical_32_byte_base64url_credential_and_enrollment_secrets_are_accepted()
    {
        var validPrefix = new string('A', 42);

        Assert.True(AgentCredentialSecretMaterial.IsCanonicalSecret(validPrefix + 'A'));
        Assert.True(AgentCredentialSecretMaterial.IsCanonicalSecret(validPrefix + '8'));

        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(null));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret("short"));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(new string('A', 44)));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(new string('+', 43)));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(new string('=', 43)));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(new string(' ', 43)));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(validPrefix + 'B'));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(validPrefix + '_'));
        Assert.False(AgentCredentialSecretMaterial.IsCanonicalSecret(new string('_', 43)));
    }
}
