using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Server.Modules.Sessions.Domain;

namespace GameNet.Server.Modules.Sessions.Application;

public sealed record SessionActorContext(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record StartSessionCommand(Guid StationId, Guid TariffId, Guid? CustomerId, string IdempotencyKey, SessionActorContext Actor);
public sealed record EndSessionCommand(Guid SessionId, int ExpectedVersion, string IdempotencyKey, SessionActorContext Actor);
public sealed record CashCheckoutCommand(Guid InvoiceId, long AmountReceivedToman, int ExpectedSessionVersion, string IdempotencyKey, SessionActorContext Actor);

public interface ISessionService
{
    Task<GameNet.Shared.Primitives.Result<SessionResponse>> StartAsync(StartSessionCommand command, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<InvoicePreviewResponse>> EndAsync(EndSessionCommand command, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<CashCheckoutResponse>> CheckoutCashAsync(CashCheckoutCommand command, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<IReadOnlyList<SessionResponse>>> ListActiveAsync(SessionActorContext actor, Guid? stationId, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<IReadOnlyList<InvoicePreviewResponse>>> ListPendingInvoicesAsync(SessionActorContext actor, CancellationToken cancellationToken);
}

public sealed record TariffActorContext(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record CreateTariffCommand(string Code, string Name, GameNet.Shared.Contracts.V1.Stations.StationTypeContract StationType, long HourlyRateToman, bool IsDefault, string IdempotencyKey, TariffActorContext Actor);

public interface ITariffService
{
    Task<GameNet.Shared.Primitives.Result<IReadOnlyList<TariffResponse>>> ListActiveAsync(TariffActorContext actor, GameNet.Shared.Contracts.V1.Stations.StationTypeContract? stationType, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<TariffResponse>> CreateAsync(CreateTariffCommand command, CancellationToken cancellationToken);
}
