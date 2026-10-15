using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Sessions;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Tariffs;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class SessionWorkflowViewModelTests
{
    [Fact]
    public async Task Start_success_message_keeps_the_station_that_was_submitted_when_selection_changes_in_flight()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var stationA = new StationResponse(
                Guid.NewGuid(), "PC-01", "Front PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var stationB = new StationResponse(
                Guid.NewGuid(), "PC-02", "Second PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var client = new FakeGameNetServerClient([stationA, stationB], [tariff]);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission is Permissions.StationsRead or Permissions.SessionsOperate);

            await viewModel.RefreshAsync();
            viewModel.SelectedStation = stationA;
            viewModel.SelectedTariff = tariff;

            var successMessage = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(viewModel.StatusMessage) &&
                    viewModel.StatusMessage.Contains("Session started for", StringComparison.Ordinal))
                {
                    successMessage.TrySetResult(viewModel.StatusMessage);
                }
            };

            Assert.True(viewModel.StartSessionCommand.CanExecute(null));
            viewModel.StartSessionCommand.Execute(null);

            await client.StartRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(client.LastStartRequest);
            Assert.Equal(stationA.Id, client.LastStartRequest!.StationId);
            viewModel.SelectedStation = stationB;

            var startedSession = new SessionResponse(
                Guid.NewGuid(), stationA.Id, Guid.NewGuid(), tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman,
                Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, null,
                SessionStatusContract.Active, 1);
            client.ActiveSessions.Add(startedSession);
            client.StartSessionCompletion.SetResult(startedSession);

            var message = await successMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(stationA.Code, message);
            Assert.DoesNotContain(stationB.Code, message);
            Assert.Equal(startedSession.Id, viewModel.SelectedActiveSession?.Session.Id);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private sealed class FakeGameNetServerClient(
        IReadOnlyList<StationResponse> stations,
        IReadOnlyList<TariffResponse> tariffs) : IGameNetServerClient
    {
        public TaskCompletionSource<bool> StartRequestReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<SessionResponse> StartSessionCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<SessionResponse> ActiveSessions { get; } = [];
        public StartSessionRequest? LastStartRequest { get; private set; }

        public Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurrentOperatorResponse> GetCurrentOperatorAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChangeOwnPasswordResponse> ChangeOwnPasswordAsync(ChangeOwnPasswordRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StationResponse>> GetStationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(stations);

        public Task<StationResponse> CreateStationAsync(CreateStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StationResponse> RenameStationAsync(Guid id, RenameStationRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StationResponse> BindStationAgentAsync(Guid id, BindStationAgentRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StationResponse> SetStationStatusAsync(Guid id, SetStationStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentCommandAcknowledgement> ProbeAgentHealthAsync(Guid stationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TariffResponse>> GetTariffsAsync(StationTypeContract? stationType = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TariffResponse>>(tariffs
                .Where(tariff => stationType is null || tariff.StationType == stationType)
                .ToArray());

        public Task<TariffResponse> CreateTariffAsync(CreateTariffRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(Guid? stationId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionResponse>>(ActiveSessions
                .Where(session => stationId is null || session.StationId == stationId)
                .ToArray());

        public Task<SessionResponse> StartSessionAsync(StartSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            LastStartRequest = request;
            StartRequestReceived.TrySetResult(true);
            return StartSessionCompletion.Task;
        }

        public Task<InvoicePreviewResponse> EndSessionAsync(Guid id, EndSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoicePreviewResponse>>(Array.Empty<InvoicePreviewResponse>());
        public Task<CashCheckoutResponse> CheckoutCashAsync(Guid invoiceId, CashCheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ClearSession() { }
    }
}
