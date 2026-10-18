namespace GameNet.Agent;

public readonly record struct AgentIdentity
{
    private AgentIdentity(string deviceId) => DeviceId = deviceId;
    public string DeviceId { get; }

    public static AgentIdentity FromDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));

        var normalized = deviceId.Trim();
        if (normalized.Length > 128 ||
            normalized.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "DeviceId must be 1-128 ASCII letters, digits, dashes, underscores or dots.",
                nameof(deviceId));
        }

        return new AgentIdentity(normalized);
    }
}
