using GameNet.Server.Infrastructure.Realtime;
using GameNet.Shared.Contracts.V1.Protocol;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class AgentHealthProbeDispatchFenceTests
{
    [Fact]
    public async Task Rejected_lease_fence_does_not_send_and_cleans_pending_command()
    {
        var leases = new RejectingLeaseStore();
        var results = new AgentCommandResultCoordinator();
        var hub = new RecordingHubContext();
        var dispatcher = new AgentHealthProbeDispatcher(
            leases, results, hub, TimeProvider.System);

        var result = await dispatcher.ProbeAsync(Guid.NewGuid(), "pc-01");

        Assert.Null(result);
        Assert.Equal(1, leases.FenceCalls);
        Assert.Equal(0, hub.Proxy.SendCount);
        Assert.Equal(0, results.PendingCount);
    }

    private sealed class RejectingLeaseStore : IAgentConnectionLeaseStore
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public int FenceCalls { get; private set; }

        public Task<AgentConnectionLeaseState?> TryAcquireAsync(
            AgentConnectionLeaseRequest request,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentConnectionLeaseState?>(null);

        public Task<bool> RenewAsync(
            string deviceId,
            string connectionId,
            string leaseToken,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RecordHeartbeatAsync(
            AgentHeartbeat heartbeat,
            string connectionId,
            string leaseToken,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<AgentCommandLeaseTarget?> GetFreshLeaseForCommandAsync(
            string deviceId,
            TimeSpan heartbeatFreshness,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentCommandLeaseTarget?>(new AgentCommandLeaseTarget(
                deviceId,
                "connection-1",
                "lease-token",
                now.AddMinutes(1),
                now,
                "test-agent",
                "Ready"));

        public Task<bool> IsCurrentLeaseAsync(
            string deviceId,
            string connectionId,
            string leaseToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> SendIfCurrentLeaseAsync(
            string deviceId,
            string connectionId,
            string leaseToken,
            Func<CancellationToken, Task> send,
            CancellationToken cancellationToken = default)
        {
            FenceCalls++;
            // Simulate revocation winning the database lock race. The send delegate
            // must not be called, and the dispatcher must remove its pending result.
            return Task.FromResult(false);
        }

        public Task ReleaseIfOwnerAsync(
            string deviceId,
            string connectionId,
            string leaseToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingHubContext : IHubContext<AgentHub>
    {
        public RecordingClientProxy Proxy { get; } = new();
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new NoopGroupManager();

        public RecordingHubContext() => Clients = new RecordingHubClients(Proxy);
    }

    private sealed class RecordingHubClients(RecordingClientProxy proxy) : IHubClients
    {
        public IClientProxy All => proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Client(string connectionId) => proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
        public IClientProxy Group(string groupName) => proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;
        public IClientProxy User(string userId) => proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public int SendCount { get; private set; }

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
