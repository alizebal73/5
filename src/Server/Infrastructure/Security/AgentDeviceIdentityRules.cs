namespace GameNet.Server.Infrastructure.Security;

internal static class AgentDeviceIdentityRules
{
    internal static bool IsValid(string? deviceId) =>
        deviceId is { Length: > 0 and <= 128 } &&
        string.Equals(deviceId, deviceId.Trim(), StringComparison.Ordinal) &&
        deviceId.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
