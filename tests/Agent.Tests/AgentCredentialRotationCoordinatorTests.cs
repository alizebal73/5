using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GameNet.Agent.Identity;
using GameNet.Agent.Transport;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameNet.Agent.Tests;

public sealed class AgentCredentialRotationCoordinatorTests
{
    [Fact]
    public async Task Pending_replacement_is_saved_before_stage_and_promoted_only_after_confirmation()
    {
        var oldSecret = GenerateSecret();
        var store = new FakeCredentialStore(oldSecret);
        var operationId = Guid.NewGuid();
        var calls = 0;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            Assert.NotNull(store.Pending);
            Assert.Equal(oldSecret, store.Current);
            Assert.Equal(operationId, store.Pending!.OperationId);
            Assert.Equal(oldSecret, store.Pending.CurrentSecret);

            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/rotate/stage", StringComparison.Ordinal))
            {
                var stage = await request.Content!.ReadFromJsonAsync<AgentCredentialRotationStageRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                Assert.Equal(operationId, stage!.OperationId);
                Assert.Equal(store.Pending.ReplacementSecret, stage.ReplacementSecret);
                Interlocked.Increment(ref calls);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AgentCredentialRotationStageResponse(
                        operationId, stage.DeviceId, "staged", DateTimeOffset.UtcNow.AddMinutes(10)))
                };
            }

            Assert.EndsWith("/rotate/confirm", path);
            var confirmation = await request.Content!.ReadFromJsonAsync<AgentCredentialRotationConfirmationRequest>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            Assert.Equal(operationId, confirmation!.OperationId);
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AgentCredentialRotationConfirmationResponse(
                    operationId, confirmation.DeviceId, "committed", Guid.NewGuid()))
            };
        });

        using var client = new HttpClient(handler);
        var coordinator = new AgentCredentialRotationCoordinator(
            new SingleClientFactory(client),
            store,
            Options.Create(new AgentTransportOptions { ServerBaseUrl = "https://127.0.0.1:5080" }));

        await coordinator.RotateForOperationAsync("station-rotation-01", operationId);

        Assert.Equal(2, calls);
        Assert.NotEqual(oldSecret, store.Current);
        Assert.Null(store.Pending);
    }

    [Fact]
    public async Task Lost_confirm_response_keeps_both_secrets_and_replays_same_operation_after_restart()
    {
        var oldSecret = GenerateSecret();
        var store = new FakeCredentialStore(oldSecret);
        var operationId = Guid.NewGuid();
        var confirmAttempts = 0;
        var firstHandler = new DelegateHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/rotate/stage", StringComparison.Ordinal))
            {
                var stage = await request.Content!.ReadFromJsonAsync<AgentCredentialRotationStageRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AgentCredentialRotationStageResponse(
                        operationId, stage!.DeviceId, "staged", DateTimeOffset.UtcNow.AddMinutes(10)))
                };
            }

            Interlocked.Increment(ref confirmAttempts);
            throw new HttpRequestException("simulated response loss after the server committed");
        });
        using (var client = new HttpClient(firstHandler))
        {
            var first = NewCoordinator(client, store);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => first.RotateForOperationAsync("station-rotation-02", operationId));
        }

        Assert.Equal(oldSecret, store.Current);
        Assert.NotNull(store.Pending);
        var pendingReplacement = store.Pending!.ReplacementSecret;

        var replayHandler = new DelegateHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/rotate/stage", StringComparison.Ordinal))
            {
                var stage = await request.Content!.ReadFromJsonAsync<AgentCredentialRotationStageRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                Assert.Equal(operationId, stage!.OperationId);
                Assert.Equal(oldSecret, stage.CurrentSecret);
                Assert.Equal(pendingReplacement, stage.ReplacementSecret);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AgentCredentialRotationStageResponse(
                        operationId, stage.DeviceId, "committed", null))
                };
            }

            var confirm = await request.Content!.ReadFromJsonAsync<AgentCredentialRotationConfirmationRequest>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            Assert.Equal(operationId, confirm!.OperationId);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AgentCredentialRotationConfirmationResponse(
                    operationId, confirm.DeviceId, "already_committed", Guid.NewGuid()))
            };
        });
        using var replayClient = new HttpClient(replayHandler);
        var replay = NewCoordinator(replayClient, store);

        Assert.True(await replay.ResumePendingIfAnyAsync("station-rotation-02"));
        Assert.Equal(pendingReplacement, store.Current);
        Assert.Null(store.Pending);
        Assert.Equal(1, confirmAttempts);
    }

    private static AgentCredentialRotationCoordinator NewCoordinator(HttpClient client, IAgentCredentialStore store) =>
        new(new SingleClientFactory(client), store,
            Options.Create(new AgentTransportOptions { ServerBaseUrl = "https://127.0.0.1:5080" }));

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed class FakeCredentialStore(string current) : IAgentCredentialStore
    {
        public string? Current { get; private set; } = current;
        public AgentPendingCredentialRotation? Pending { get; private set; }

        public Task<string?> TryLoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<string> GetOrBootstrapAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current ?? throw new InvalidOperationException("No current credential."));
        public Task SaveAsync(string secret, CancellationToken cancellationToken = default)
        {
            Current = secret;
            return Task.CompletedTask;
        }
        public Task<AgentPendingCredentialRotation?> TryLoadPendingRotationAsync(
            string deviceId, CancellationToken cancellationToken = default)
        {
            if (Pending is not null && !string.Equals(Pending.DeviceId, deviceId, StringComparison.Ordinal))
                throw new InvalidOperationException("Pending rotation belongs to another device.");
            return Task.FromResult(Pending);
        }
        public Task SavePendingRotationAsync(
            Guid operationId, string deviceId, string currentSecret, string replacementSecret,
            CancellationToken cancellationToken = default)
        {
            var next = new AgentPendingCredentialRotation(operationId, deviceId, currentSecret, replacementSecret, DateTimeOffset.UtcNow);
            if (Pending is not null && Pending != next)
                throw new InvalidOperationException("Different rotation already pending.");
            Pending ??= next;
            return Task.CompletedTask;
        }
        public Task PromotePendingRotationAsync(Guid operationId, string deviceId, CancellationToken cancellationToken = default)
        {
            if (Pending is null || Pending.OperationId != operationId || Pending.DeviceId != deviceId)
                throw new InvalidOperationException("Pending rotation mismatch.");
            Current = Pending.ReplacementSecret;
            Pending = null;
            return Task.CompletedTask;
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
