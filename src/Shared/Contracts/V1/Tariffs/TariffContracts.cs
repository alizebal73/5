using GameNet.Shared.Contracts.V1.Stations;

namespace GameNet.Shared.Contracts.V1.Tariffs;

public sealed record CreateTariffRequest(
    string Code,
    string Name,
    StationTypeContract StationType,
    long HourlyRateToman,
    bool IsDefault);

public sealed record TariffResponse(
    Guid Id,
    string Code,
    string Name,
    StationTypeContract StationType,
    long HourlyRateToman,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    int Version);
