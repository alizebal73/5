using GameNet.Shared.Contracts.V1.Sessions;

namespace GameNet.Server.Modules.Sessions.Application;

public sealed record SessionActorContext(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record StartSessionCommand(Guid StationId, Guid TariffId, Guid? CustomerId, string IdempotencyKey, SessionActorContext Actor);
public sealed record StartPrepaidSessionCommand(Guid StationId, Guid CustomerId, string IdempotencyKey, SessionActorContext Actor);
public sealed record EndSessionCommand(Guid SessionId, int ExpectedVersion, string IdempotencyKey, SessionActorContext Actor);
public sealed record ReconcileSessionTimingCommand(Guid SessionId, int ExpectedSessionVersion, long ReconciledElapsedMinutes, string Reason, bool Confirmed, string IdempotencyKey, SessionActorContext Actor);

public interface ISessionService
{
    Task<GameNet.Shared.Primitives.Result<SessionResponse>> StartAsync(StartSessionCommand command, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<SessionResponse>> StartPrepaidAsync(
        StartPrepaidSessionCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(GameNet.Shared.Primitives.Result<SessionResponse>.Failure(
            new GameNet.Shared.Primitives.Error("sessions.prepaid_not_supported",
                "Prepaid Session start is not configured.")));
    Task<GameNet.Shared.Primitives.Result<InvoicePreviewResponse>> EndAsync(EndSessionCommand command, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<InvoicePreviewResponse>> ReconcileTimingAsync(ReconcileSessionTimingCommand command, CancellationToken cancellationToken);
    Task<GameNet.Shared.Primitives.Result<IReadOnlyList<SessionResponse>>> ListActiveAsync(SessionActorContext actor, Guid? stationId, CancellationToken cancellationToken);
    Task<int> ExpireDuePrepaidSessionsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}

