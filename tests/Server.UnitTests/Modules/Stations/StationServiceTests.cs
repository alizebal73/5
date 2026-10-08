using GameNet.Server.Application;
using GameNet.Server.Modules.Stations.Application;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Shared.Primitives;

namespace GameNet.Server.UnitTests.Modules.Stations;

public sealed class StationServiceTests
{
    private static readonly StationActorContext Operator = new(
        "operator",
        "operator-1",
        new HashSet<string>(StringComparer.Ordinal)
        {
            "stations.read",
            "stations.operate"
        });

    [Fact]
    public async Task Create_is_idempotent_for_the_same_key()
    {
        var repo = new FakeStationRepository();
        var idempotency = new FakeIdempotencyStore();
        var audit = new FakeAuditWriter();
        var service = CreateService(repo, idempotency, audit);

        var command = new CreateStationCommand("pc01", "PC 01", StationType.Pc, "cmd-1", Operator);

        var first = await service.CreateAsync(command, CancellationToken.None);
        var second = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(repo.Items);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task Reusing_a_key_for_different_data_is_rejected()
    {
        var repo = new FakeStationRepository();
        var idempotency = new FakeIdempotencyStore();
        var audit = new FakeAuditWriter();
        var service = CreateService(repo, idempotency, audit);

        var first = await service.CreateAsync(
            new CreateStationCommand("pc01", "PC 01", StationType.Pc, "cmd-1", Operator),
            CancellationToken.None);

        var second = await service.CreateAsync(
            new CreateStationCommand("pc02", "PC 02", StationType.Pc, "cmd-1", Operator),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("idempotency.key_reused", second.Error.Code);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Operator_without_station_permission_is_rejected()
    {
        var actor = new StationActorContext(
            "operator",
            "operator-2",
            new HashSet<string>(StringComparer.Ordinal));

        var service = CreateService(
            new FakeStationRepository(),
            new FakeIdempotencyStore(),
            new FakeAuditWriter());

        var result = await service.CreateAsync(
            new CreateStationCommand("PC01", "PC 01", StationType.Pc, "cmd-2", actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("stations.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Rename_advances_station_version()
    {
        var repo = new FakeStationRepository();
        var station = Station.Create(Guid.NewGuid(), "PC01", "PC 01", StationType.Pc);
        await repo.AddAsync(station, CancellationToken.None);

        var service = CreateService(repo, new FakeIdempotencyStore(), new FakeAuditWriter());

        var result = await service.RenameAsync(
            new RenameStationCommand(station.Id, "PC One", "rename-1", Operator),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal("PC One", result.Value.Name);
    }

    private static StationService CreateService(
        FakeStationRepository repository,
        FakeIdempotencyStore idempotency,
        FakeAuditWriter audit) =>
        new(
            repository,
            new PassthroughTransactionCoordinator(),
            idempotency,
            audit,
            new FixedClock());

    private sealed class FakeStationRepository : IStationRepository
    {
        public List<Station> Items { get; } = [];

        public Task<IReadOnlyList<Station>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Station>>(Items.OrderBy(x => x.Code).ToArray());

        public Task AddAsync(Station station, CancellationToken cancellationToken)
        {
            Items.Add(station);
            return Task.CompletedTask;
        }

        public Task<Station?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<(string Scope, string Key), (string Operation, string Hash, string Lease, string? Response)> _entries = [];

        public Task<IdempotencyClaim> TryClaimAsync(
            string scope,
            string key,
            string operation,
            string requestHash,
            CancellationToken cancellationToken = default)
        {
            if (!_entries.TryGetValue((scope, key), out var existing))
            {
                var lease = Guid.NewGuid().ToString("N");
                _entries[(scope, key)] = (operation, requestHash, lease, null);
                return Task.FromResult(new IdempotencyClaim(IdempotencyClaimState.Claimed, lease, null, null));
            }

            if (existing.Operation != operation || existing.Hash != requestHash)
                return Task.FromResult(new IdempotencyClaim(IdempotencyClaimState.Conflict, existing.Lease, 409, existing.Response));

            if (existing.Response is not null)
                return Task.FromResult(new IdempotencyClaim(IdempotencyClaimState.Completed, existing.Lease, 200, existing.Response));

            return Task.FromResult(new IdempotencyClaim(IdempotencyClaimState.InFlight, existing.Lease, null, null));
        }

        public Task CompleteAsync(
            string scope,
            string key,
            string leaseToken,
            int statusCode,
            string responseJson,
            CancellationToken cancellationToken = default)
        {
            var existing = _entries[(scope, key)];
            Assert.Equal(existing.Lease, leaseToken);
            _entries[(scope, key)] = (existing.Operation, existing.Hash, existing.Lease, responseJson);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditRecord> Records { get; } = [];

        public void Append(AuditRecord record) => Records.Add(record);
    }

    private sealed class PassthroughTransactionCoordinator : ITransactionCoordinator
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }

    private sealed class FixedClock : IGameClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
