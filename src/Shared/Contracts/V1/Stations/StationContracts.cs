namespace GameNet.Shared.Contracts.V1.Stations;

public enum StationTypeContract
{
    Pc = 1,
    Ps5 = 2,
    Foosball = 3
}

public enum StationStatusContract
{
    Disabled = 1,
    Available = 2,
    Maintenance = 3,
    RecoveryRequired = 4
}

public sealed record CreateStationRequest(
    string Code,
    string Name,
    StationTypeContract Type);

public sealed record RenameStationRequest(
    string Name);

public sealed record StationResponse(
    Guid Id,
    string Code,
    string Name,
    StationTypeContract Type,
    StationStatusContract Status,
    int Version);
