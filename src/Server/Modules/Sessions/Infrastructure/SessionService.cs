using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Infrastructure.UseCases;
using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Server.Modules.Tariffs.Contracts;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Public entry point of the Sessions module. It owns no business rules: every use case
/// lives in its own handler under <c>UseCases</c>, and cross-module access goes through
/// each module's public Contracts. All handlers share the scoped DbContext and the outer
/// mutation transaction supplied by <see cref="CommandMutationPipeline"/>.
/// </summary>
public sealed class SessionService : ISessionService
{
    private readonly StartSessionHandler start;
    private readonly EndSessionHandler end;
    private readonly ReconcileSessionTimingHandler reconcileTiming;
    private readonly ListActiveSessionsHandler listActive;

    public SessionService(
        GameNetDbContext db,
        CommandMutationPipeline mutationPipeline,
        IRequestFingerprint requestFingerprint,
        ICustomerSessionAccess customers,
        IStationSessionAccess stations,
        ITariffSessionAccess tariffs,
        IBillingInvoiceWriter invoices,
        IAuditWriter audit,
        IGameClock clock)
    {
        start = new StartSessionHandler(
            db, mutationPipeline, requestFingerprint, customers, stations, tariffs, audit, clock);
        end = new EndSessionHandler(
            db, mutationPipeline, requestFingerprint, invoices, audit, clock);
        reconcileTiming = new ReconcileSessionTimingHandler(
            db, mutationPipeline, requestFingerprint, invoices, audit, clock);
        listActive = new ListActiveSessionsHandler(db);
    }

    public Task<Result<SessionResponse>> StartAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken) =>
        start.HandleAsync(command, cancellationToken);

    public Task<Result<InvoicePreviewResponse>> EndAsync(
        EndSessionCommand command,
        CancellationToken cancellationToken) =>
        end.HandleAsync(command, cancellationToken);

    public Task<Result<InvoicePreviewResponse>> ReconcileTimingAsync(
        ReconcileSessionTimingCommand command,
        CancellationToken cancellationToken) =>
        reconcileTiming.HandleAsync(command, cancellationToken);

    public Task<Result<IReadOnlyList<SessionResponse>>> ListActiveAsync(
        SessionActorContext actor,
        Guid? stationId,
        CancellationToken cancellationToken) =>
        listActive.HandleAsync(actor, stationId, cancellationToken);
}
