namespace GameNet.Server.Modules.Stations.Domain;

public sealed class Station
{
    private Station() { }

    private Station(Guid id, string code, string name, StationType type)
    {
        Id = id;
        Code = Normalize(code);
        Name = Normalize(name);
        Type = type;
        Status = StationStatus.Available;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public StationType Type { get; private set; }
    public StationStatus Status { get; private set; }

    public static Station Create(Guid id, string code, string name, StationType type)
    {
        if (id == Guid.Empty) throw new ArgumentException("Station id is required.", nameof(id));
        return new Station(id, code, name, type);
    }

    public void Rename(string name) => Name = Normalize(name);

    public void Enable()
    {
        if (Status == StationStatus.Maintenance)
            throw new InvalidOperationException("STATION_IN_MAINTENANCE");
        if (Status == StationStatus.RecoveryRequired)
            throw new InvalidOperationException("STATION_RECOVERY_REQUIRED");
        Status = StationStatus.Available;
    }

    public void Disable()
    {
        Status = StationStatus.Disabled;
    }

    public void MarkMaintenance() => Status = StationStatus.Maintenance;

    public void MarkRecoveryRequired() => Status = StationStatus.RecoveryRequired;

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", nameof(value));

        var normalized = value.Trim();
        if (normalized.Length > 100)
            throw new ArgumentException("Value is too long.", nameof(value));

        return normalized;
    }
}
