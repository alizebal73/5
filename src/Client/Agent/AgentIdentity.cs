namespace GameNet.Agent;

public readonly record struct AgentIdentity
{
    private AgentIdentity(string deviceId) => DeviceId = deviceId;
    public string DeviceId { get; }

    public static AgentIdentity FromDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        return new AgentIdentity(deviceId.Trim());
    }
}
