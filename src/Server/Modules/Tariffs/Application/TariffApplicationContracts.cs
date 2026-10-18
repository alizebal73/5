using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Tariffs.Application;

public sealed record TariffActorContext(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record CreateTariffCommand(string Code, string Name, StationTypeContract StationType, long HourlyRateToman, bool IsDefault, string IdempotencyKey, TariffActorContext Actor);

public interface ITariffService
{
    Task<Result<IReadOnlyList<TariffResponse>>> ListActiveAsync(TariffActorContext actor, StationTypeContract? stationType, CancellationToken cancellationToken);
    Task<Result<TariffResponse>> CreateAsync(CreateTariffCommand command, CancellationToken cancellationToken);
}
