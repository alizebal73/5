using System;
using System.Collections.Generic;
using System.ComponentModel;
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


    [Fact]
    public async Task Operator_view_model_completes_start_end_invoice_selection_and_cash_checkout_journey()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-03", "Checkout PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var client = new FakeGameNetServerClient([station], [tariff]);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission is Permissions.StationsRead or Permissions.SessionsOperate or Permissions.BillingWrite);

            await viewModel.RefreshAsync();
            viewModel.SelectedStation = station;
            viewModel.SelectedTariff = tariff;

            var startStatus = WaitForStatusAsync(
                viewModel,
                message => message.Contains("Session started for", StringComparison.Ordinal));
            Assert.True(viewModel.StartSessionCommand.CanExecute(null));
            viewModel.StartSessionCommand.Execute(null);
            await client.StartRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var session = new SessionResponse(
                Guid.NewGuid(), station.Id, Guid.NewGuid(), tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman,
                Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, null,
                SessionStatusContract.Active, 1);
            client.ActiveSessions.Add(session);
            client.StartSessionCompletion.SetResult(session);

            Assert.Contains(station.Code, await startStatus);
            Assert.Single(viewModel.ActiveSessions);
            Assert.Empty(viewModel.PendingInvoices);
            Assert.Equal(session.Id, viewModel.SelectedActiveSession?.Session.Id);
            Assert.Equal(station.Id, client.LastStartRequest?.StationId);
            Assert.False(string.IsNullOrWhiteSpace(client.LastStartIdempotencyKey));

            var endStatus = WaitForStatusAsync(
                viewModel,
                message => message.StartsWith("Invoice issued:", StringComparison.Ordinal));
            Assert.True(viewModel.EndSessionCommand.CanExecute(null));
            viewModel.EndSessionCommand.Execute(null);
            Assert.Contains("30,000 Toman", await endStatus);

            Assert.Empty(viewModel.ActiveSessions);
            var pending = Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(pending.Invoice.InvoiceId, viewModel.SelectedPendingInvoice?.Invoice.InvoiceId);
            Assert.Equal(session.Id, client.LastEndedSessionId);
            Assert.Equal(session.Version, client.LastEndRequest?.ExpectedVersion);
            Assert.False(string.IsNullOrWhiteSpace(client.LastEndIdempotencyKey));

            // The operator enters the cash physically received; the Server response remains
            // the source of truth for due amount, received amount and change.
            viewModel.AmountReceivedText = "50000";
            var cashStatus = WaitForStatusAsync(
                viewModel,
                message => message.StartsWith("Payment recorded.", StringComparison.Ordinal));
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.CheckoutCashCommand.Execute(null);
            var message = await cashStatus;

            Assert.Contains("Due: 30,000", message);
            Assert.Contains("received: 50,000", message);
            Assert.Contains("change: 20,000", message);
            Assert.Empty(viewModel.PendingInvoices);
            Assert.Equal(pending.Invoice.InvoiceId, client.LastCheckoutInvoiceId);
            Assert.Equal(50_000L, client.LastCashCheckoutRequest?.AmountReceivedToman);
            Assert.Equal(pending.Invoice.SessionVersion, client.LastCashCheckoutRequest?.ExpectedSessionVersion);
            Assert.False(string.IsNullOrWhiteSpace(client.LastCheckoutIdempotencyKey));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static async Task<string> WaitForStatusAsync(
        SessionWorkflowViewModel viewModel,
        Func<string, bool> predicate)
    {
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.StatusMessage) &&
                predicate(viewModel.StatusMessage))
            {
                completion.TrySetResult(viewModel.StatusMessage);
            }
        };

        viewModel.PropertyChanged += handler;
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
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
        public List<InvoicePreviewResponse> PendingInvoices { get; } = [];
        public StartSessionRequest? LastStartRequest { get; private set; }
        public string? LastStartIdempotencyKey { get; private set; }
        public Guid? LastEndedSessionId { get; private set; }
        public EndSessionRequest? LastEndRequest { get; private set; }
        public string? LastEndIdempotencyKey { get; private set; }
        public Guid? LastCheckoutInvoiceId { get; private set; }
        public CashCheckoutRequest? LastCashCheckoutRequest { get; private set; }
        public string? LastCheckoutIdempotencyKey { get; private set; }

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
            LastStartIdempotencyKey = idempotencyKey;
            StartRequestReceived.TrySetResult(true);
            return StartSessionCompletion.Task;
        }

        public Task<InvoicePreviewResponse> EndSessionAsync(Guid id, EndSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            LastEndedSessionId = id;
            LastEndRequest = request;
            LastEndIdempotencyKey = idempotencyKey;

            var session = ActiveSessions.Single(x => x.Id == id);
            ActiveSessions.Remove(session);
            var invoice = new InvoicePreviewResponse(
                Guid.NewGuid(), session.Id, checked(session.Version + 1), session.StationId,
                session.CustomerId, session.TariffId, session.TariffCodeSnapshot,
                session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
                TimeSpan.FromMinutes(30).Ticks, 30_000L, InvoiceStatusContract.PendingPayment,
                DateTimeOffset.UtcNow, null);
            PendingInvoices.Add(invoice);
            return Task.FromResult(invoice);
        }

        public Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoicePreviewResponse>>(PendingInvoices.ToArray());

        public Task<CashCheckoutResponse> CheckoutCashAsync(Guid invoiceId, CashCheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            LastCheckoutInvoiceId = invoiceId;
            LastCashCheckoutRequest = request;
            LastCheckoutIdempotencyKey = idempotencyKey;
            var invoice = PendingInvoices.Single(x => x.InvoiceId == invoiceId);
            PendingInvoices.Remove(invoice);
            return Task.FromResult(new CashCheckoutResponse(
                invoice.InvoiceId, Guid.NewGuid(), Guid.NewGuid(), invoice.AmountToman,
                request.AmountReceivedToman, checked(request.AmountReceivedToman - invoice.AmountToman),
                DateTimeOffset.UtcNow));
        }
        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ClearSession() { }
    }
}
