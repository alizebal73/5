using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Sessions;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Billing;
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


    [Fact]
    public async Task Start_session_shows_clear_error_if_server_reports_station_became_unavailable()
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-04", "Race PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var client = new FakeGameNetServerClient([station], [tariff]);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission is Permissions.StationsRead or Permissions.SessionsOperate);

            await viewModel.RefreshAsync();
            viewModel.SelectedStation = station;
            viewModel.SelectedTariff = tariff;

            // The station was available when loaded, but another actor can change its
            // state before the Server processes the command. The Server remains authoritative.
            client.NextStartException = new GameNetApiException("sessions.station_unavailable", 409);
            var error = WaitForErrorAsync(
                viewModel,
                message => message.Contains("This station is not available for a new Session.", StringComparison.Ordinal));

            Assert.True(viewModel.StartSessionCommand.CanExecute(null));
            viewModel.StartSessionCommand.Execute(null);
            var message = await error;

            Assert.Contains("This station is not available for a new Session.", message);
            Assert.Equal(station.Id, client.LastStartRequest?.StationId);
            Assert.False(string.IsNullOrWhiteSpace(client.LastStartIdempotencyKey));
            Assert.Empty(viewModel.ActiveSessions);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Cash_checkout_does_not_prefill_received_amount_and_requires_fresh_explicit_confirmation()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-04", "Cash PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var invoice = new InvoicePreviewResponse(
                Guid.NewGuid(), Guid.NewGuid(), 2, station.Id, Guid.NewGuid(), tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman, TimeSpan.FromMinutes(30).Ticks,
                30_000L, InvoiceStatusContract.PendingPayment, DateTimeOffset.UtcNow, null);
            var client = new FakeGameNetServerClient([station], [tariff]);
            client.PendingInvoices.Add(invoice);
            var viewModel = new SessionWorkflowViewModel(client,
                permission => permission == Permissions.BillingWrite);

            await viewModel.RefreshAsync();
            Assert.Equal(invoice.InvoiceId, viewModel.SelectedPendingInvoice?.Invoice.InvoiceId);
            Assert.Equal(string.Empty, viewModel.AmountReceivedText);
            Assert.False(viewModel.ConfirmCashCheckout);
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
            Assert.Contains("Amount due: 30,000", viewModel.CashCheckoutSummaryText);

            viewModel.AmountReceivedText = "50000";
            Assert.Contains("cash received: 50,000", viewModel.CashCheckoutSummaryText);
            Assert.Contains("change: 20,000", viewModel.CashCheckoutSummaryText);
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));

            viewModel.ConfirmCashCheckout = true;
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));

            // Changing the received amount invalidates the prior confirmation.
            viewModel.AmountReceivedText = "50001";
            Assert.False(viewModel.ConfirmCashCheckout);
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.ConfirmCashCheckout = true;
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Cash_checkout_response_loss_after_commit_recovers_payment_and_ledger_outcome_without_retry()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-07", "Recovered PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var invoice = new InvoicePreviewResponse(
                Guid.NewGuid(), Guid.NewGuid(), 2, station.Id, Guid.NewGuid(), tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman, TimeSpan.FromMinutes(30).Ticks,
                30_000L, InvoiceStatusContract.PendingPayment, DateTimeOffset.UtcNow, null);
            var client = new FakeGameNetServerClient([station], [tariff]);
            client.PendingInvoices.Add(invoice);
            var viewModel = new SessionWorkflowViewModel(
                client, permission => permission == Permissions.BillingWrite);

            await viewModel.RefreshAsync();
            viewModel.AmountReceivedText = "50000";
            viewModel.ConfirmCashCheckout = true;
            client.LoseNextCashCheckoutResponseAfterCommit = true;

            var reconciled = WaitForStatusAsync(
                viewModel,
                message => message.Contains("payment was already recorded", StringComparison.Ordinal));
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.CheckoutCashCommand.Execute(null);
            var message = await reconciled;

            Assert.Contains("Due: 30,000", message);
            Assert.Contains("received: 50,000", message);
            Assert.Contains("change: 20,000", message);
            Assert.Empty(viewModel.PendingInvoices);
            Assert.Single(viewModel.RecentCashCheckouts);
            Assert.Equal(invoice.InvoiceId, viewModel.RecentCashCheckouts[0].Outcome.InvoiceId);
            Assert.Single(client.CheckoutIdempotencyKeys);

            // Simulate Desktop restart: a fresh ViewModel discovers the paid invoice from Server history.
            var restartedViewModel = new SessionWorkflowViewModel(
                client, permission => permission == Permissions.BillingWrite);
            await restartedViewModel.RefreshAsync();
            Assert.Single(restartedViewModel.RecentCashCheckouts);
            Assert.Equal(invoice.InvoiceId, restartedViewModel.RecentCashCheckouts[0].Outcome.InvoiceId);
            Assert.Empty(restartedViewModel.PendingInvoices);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Cash_checkout_network_failure_confirms_pending_state_and_requires_a_fresh_confirmation_before_retry()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-05", "Retry PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var invoice = new InvoicePreviewResponse(
                Guid.NewGuid(), Guid.NewGuid(), 2, station.Id, Guid.NewGuid(), tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman, TimeSpan.FromMinutes(30).Ticks,
                30_000L, InvoiceStatusContract.PendingPayment, DateTimeOffset.UtcNow, null);
            var client = new FakeGameNetServerClient([station], [tariff]);
            client.PendingInvoices.Add(invoice);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission == Permissions.BillingWrite);

            await viewModel.RefreshAsync();
            Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(invoice.InvoiceId, viewModel.SelectedPendingInvoice?.Invoice.InvoiceId);
            Assert.Equal(string.Empty, viewModel.AmountReceivedText);
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.AmountReceivedText = "50000";
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.ConfirmCashCheckout = true;

            client.FailNextCashCheckoutWithNetworkError = true;
            var error = WaitForErrorAsync(
                viewModel,
                message => message.Contains("Payment result is unknown", StringComparison.Ordinal));
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.CheckoutCashCommand.Execute(null);
            Assert.Contains("Payment result is unknown", await error);

            Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(invoice.InvoiceId, viewModel.SelectedPendingInvoice?.Invoice.InvoiceId);

            // Reconcile with the Server before retrying; keep the same intent, invoice,
            // amount and Session version so the idempotency key remains stable.
            await viewModel.RefreshAsync();
            Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(string.Empty, viewModel.AmountReceivedText);
            viewModel.AmountReceivedText = "50000";
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.ConfirmCashCheckout = true;
            var paid = WaitForStatusAsync(
                viewModel,
                message => message.StartsWith("Payment recorded.", StringComparison.Ordinal));
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.CheckoutCashCommand.Execute(null);
            var message = await paid;

            Assert.Contains("Due: 30,000", message);
            Assert.Contains("received: 50,000", message);
            Assert.Contains("change: 20,000", message);
            Assert.Equal(2, client.CheckoutIdempotencyKeys.Count);
            Assert.Equal(client.CheckoutIdempotencyKeys[0], client.CheckoutIdempotencyKeys[1]);
            Assert.Equal(invoice.InvoiceId, client.LastCheckoutInvoiceId);
            Assert.Empty(viewModel.PendingInvoices);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Operator_without_permissions_cannot_open_or_mutate_session_workspace()
    {
        var station = new StationResponse(
            Guid.NewGuid(), "PC-06", "Restricted PC", StationTypeContract.Pc,
            StationStatusContract.Available, 1, null, false, null, null, null);
        var tariff = new TariffResponse(
            Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
            60_000L, true, true, DateTimeOffset.UtcNow, 1);
        var client = new FakeGameNetServerClient([station], [tariff]);
        var viewModel = new SessionWorkflowViewModel(client, _ => false);

        viewModel.SelectedStation = station;
        viewModel.SelectedTariff = tariff;
        viewModel.AmountReceivedText = "60000";

        Assert.False(viewModel.CanUseWorkspace);
        Assert.False(viewModel.CanManageTariffs);
        Assert.False(viewModel.CanOperateSessions);
        Assert.False(viewModel.CanCheckout);
        Assert.False(viewModel.CanReadStations);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.CreateTariffCommand.CanExecute(null));
        Assert.False(viewModel.StartSessionCommand.CanExecute(null));
        Assert.False(viewModel.EndSessionCommand.CanExecute(null));
        Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));

        await viewModel.RefreshAsync();
        Assert.Empty(viewModel.PcStations);
        Assert.Empty(viewModel.ActiveSessions);
        Assert.Empty(viewModel.PendingInvoices);
    }

    private static async Task<string> WaitForErrorAsync(
        SessionWorkflowViewModel viewModel,
        Func<string, bool> predicate)
    {
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.ErrorMessage) &&
                predicate(viewModel.ErrorMessage))
            {
                completion.TrySetResult(viewModel.ErrorMessage);
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
        public List<string> CheckoutIdempotencyKeys { get; } = [];
        public Exception? NextStartException { get; set; }
        public bool FailNextCashCheckoutWithNetworkError { get; set; }
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
            if (NextStartException is { } error)
            {
                NextStartException = null;
                return Task.FromException<SessionResponse>(error);
            }
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

        public List<CashCheckoutOutcomeResponse> CompletedCashOutcomes { get; } = [];
        public bool LoseNextCashCheckoutResponseAfterCommit { get; set; }

        public Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoicePreviewResponse>>(PendingInvoices.ToArray());

        public Task<CashCheckoutOutcomeResponse> GetCashCheckoutOutcomeAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            var completed = CompletedCashOutcomes.SingleOrDefault(x => x.InvoiceId == invoiceId);
            if (completed is not null)
                return Task.FromResult(completed);

            var invoice = PendingInvoices.SingleOrDefault(x => x.InvoiceId == invoiceId);
            if (invoice is null)
                return Task.FromException<CashCheckoutOutcomeResponse>(
                    new GameNetApiException("billing.invoice_not_found", 404));

            return Task.FromResult(new CashCheckoutOutcomeResponse(
                invoice.InvoiceId, invoice.SessionId, invoice.StationId, invoice.TariffCodeSnapshot,
                invoice.TariffNameSnapshot, invoice.TariffHourlyRateTomanSnapshot, invoice.ElapsedTicks,
                invoice.AmountToman, invoice.Status, null, null, null, null, invoice.IssuedAtUtc, null));
        }

        public Task<IReadOnlyList<CashCheckoutOutcomeResponse>> GetRecentCashCheckoutsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CashCheckoutOutcomeResponse>>(CompletedCashOutcomes.ToArray());

        public Task<CashCheckoutResponse> CheckoutCashAsync(Guid invoiceId, CashCheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            LastCheckoutInvoiceId = invoiceId;
            LastCashCheckoutRequest = request;
            LastCheckoutIdempotencyKey = idempotencyKey;
            CheckoutIdempotencyKeys.Add(idempotencyKey);
            if (FailNextCashCheckoutWithNetworkError)
            {
                FailNextCashCheckoutWithNetworkError = false;
                return Task.FromException<CashCheckoutResponse>(new HttpRequestException("Simulated connection loss before commit."));
            }
            var invoice = PendingInvoices.Single(x => x.InvoiceId == invoiceId);
            PendingInvoices.Remove(invoice);
            var paymentId = Guid.NewGuid();
            var ledgerId = Guid.NewGuid();
            var paidAt = DateTimeOffset.UtcNow;
            var response = new CashCheckoutResponse(
                invoice.InvoiceId, paymentId, ledgerId, invoice.AmountToman,
                request.AmountReceivedToman, checked(request.AmountReceivedToman - invoice.AmountToman),
                paidAt);
            CompletedCashOutcomes.Add(new CashCheckoutOutcomeResponse(
                invoice.InvoiceId, invoice.SessionId, invoice.StationId, invoice.TariffCodeSnapshot,
                invoice.TariffNameSnapshot, invoice.TariffHourlyRateTomanSnapshot, invoice.ElapsedTicks,
                invoice.AmountToman, InvoiceStatusContract.Paid, paymentId, ledgerId,
                request.AmountReceivedToman, response.ChangeGivenToman, invoice.IssuedAtUtc, paidAt));

            if (LoseNextCashCheckoutResponseAfterCommit)
            {
                LoseNextCashCheckoutResponseAfterCommit = false;
                return Task.FromException<CashCheckoutResponse>(new HttpRequestException("Simulated connection loss after commit."));
            }

            return Task.FromResult(response);
        }
        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ClearSession() { }
    }
}
