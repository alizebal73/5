namespace GameNet.Server.Modules.Stations.Domain;

public sealed class Station
{
    private Station() { }
    private Station(Guid id, string code, string name, StationType type)
    { Id = id; Code = NormalizeCode(code); Name = NormalizeName(name); Type = type; Status = StationStatus.Available; Version = 1; }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public StationType Type { get; private set; }
    public StationStatus Status { get; private set; }
    public int Version { get; private set; }
    public string? AgentDeviceId { get; private set; }

    public static Station Create(Guid id, string code, string name, StationType type)
    {
        if (id == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(id));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        return new Station(id, code, name, type);
    }

    public void Rename(string name)
    {
        var value = NormalizeName(name);
        if (Name == value) return;
        Name = value; Version = checked(Version + 1);
    }
    public void BindAgent(string deviceId)
    {
        var value = NormalizeDeviceId(deviceId);
        if (AgentDeviceId == value) return;
        AgentDeviceId = value; Version = checked(Version + 1);
    }
    public void SetAdministrativeStatus(StationStatus status)
    {
        if (status == StationStatus.RecoveryRequired) throw new InvalidOperationException("STATION_RECOVERY_REQUIRES_RECOVERY_FLOW");
        if (status is not (StationStatus.Available or StationStatus.Disabled or StationStatus.Maintenance))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (Status == StationStatus.RecoveryRequired && status == StationStatus.Available)
            throw new InvalidOperationException("STATION_RECOVERY_REQUIRED");
        if (Status == status) return;
        Status = status; Version = checked(Version + 1);
    }
    private static string NormalizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Station code is required.", nameof(value));
        var s = value.Trim().ToUpperInvariant();
        if (s.Length > 32 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
            !char.IsAsciiLetterOrDigit(s[0]) || !char.IsAsciiLetterOrDigit(s[^1]))
            throw new ArgumentException("Station code must be 1-32 ASCII letters, digits, dashes or underscores.", nameof(value));
        return s;
    }
    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Station name is required.", nameof(value));
        var s = value.Trim();
        if (s.Length > 100 || s.Any(char.IsControl)) throw new ArgumentException("Station name must be at most 100 characters.", nameof(value));
        return s;
    }
    private static string NormalizeDeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Agent device id is required.", nameof(value));
        var s = value.Trim();
        if (s.Length > 128 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException("Agent device id contains unsupported characters.", nameof(value));
        return s;
    }
}
