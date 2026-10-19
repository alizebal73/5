using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Sessions;
using GameNet.Desktop.Features.Wallet;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.System;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Contracts.V1.Wallet;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class SessionWorkflowViewModelTests
{
    [Fact]
    public async Task Wallet_top_up_reuses_persisted_idempotency_key_after_lost_response_and_app_restart()
    {
        var oldUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var storageRoot = Path.Combine(Path.GetTempPath(), "gamenet-wallet-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var customer = new CustomerResponse(
                Guid.NewGuid(), "C-WALLET-01", "Wallet Test", null, null,
                CustomerKindContract.Registered, CustomerStatusContract.Active, DateTimeOffset.UtcNow, 1);
            var operatorId = Guid.NewGuid();
            var store = new FileWalletPendingRequestStore(storageRoot);
            var client = new FakeGameNetServerClient(Array.Empty<StationResponse>(), Array.Empty<TariffResponse>())
            {
                LoseNextWalletTopUpResponseAfterCommit = true
            };
            var viewModel = new WalletTopUpViewModel(client, permission => permission == Permissions.WalletWrite, store);
            viewModel.SetCurrentOperatorId(operatorId);
            viewModel.SelectedCustomer = customer;
            viewModel.AmountToCreditText = "100";
            viewModel.CashReceivedText = "100";

            var unresolved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, args) =>
            {
                if ((args.PropertyName == nameof(viewModel.ErrorMessage) || args.PropertyName == nameof(viewModel.IsBusy)) &&
                    viewModel.ErrorMessage.Contains("A previous top-up", StringComparison.Ordinal) &&
                    !viewModel.IsBusy)
                    unresolved.TrySetResult(true);
            };

            Assert.True(viewModel.TopUpCommand.CanExecute(null));
            viewModel.TopUpCommand.Execute(null);
            await unresolved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(viewModel.CanEditInput);
            Assert.True(viewModel.TopUpCommand.CanExecute(null), "The exact same request must remain replayable.");
            var originalKey = Assert.Single(client.WalletTopUpIdempotencyKeys);
            Assert.Equal(originalKey, store.Load()?.IdempotencyKey);
            viewModel.AmountToCreditText = "200";
            viewModel.CashReceivedText = "250";
            Assert.Equal("100", viewModel.AmountToCreditText);
            Assert.Equal("100", viewModel.CashReceivedText);

            // Another operator must not be able to bypass the unresolved physical-cash request.
            var foreignOperatorViewModel = new WalletTopUpViewModel(
                client, permission => permission == Permissions.WalletWrite, store);
            foreignOperatorViewModel.SetCurrentOperatorId(Guid.NewGuid());
            foreignOperatorViewModel.SelectedCustomer = customer;
            Assert.False(foreignOperatorViewModel.CanEditInput);
            Assert.False(foreignOperatorViewModel.TopUpCommand.CanExecute(null));
            Assert.Contains("belongs to another operator", foreignOperatorViewModel.ErrorMessage, StringComparison.Ordinal);

            // Recreate the UI object to model closing and reopening the Desktop process.
            var resumed = new WalletTopUpViewModel(client, permission => permission == Permissions.WalletWrite, store);
            resumed.SetCurrentOperatorId(operatorId);
            resumed.SelectedCustomer = customer;
            Assert.Equal("100", resumed.AmountToCreditText);
            Assert.Equal("100", resumed.CashReceivedText);
            Assert.False(resumed.CanEditInput);

            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            resumed.PropertyChanged += (_, args) =>
            {
                if ((args.PropertyName == nameof(resumed.StatusMessage) || args.PropertyName == nameof(resumed.IsBusy)) &&
                    resumed.StatusMessage.Contains("Wallet credited by", StringComparison.Ordinal) &&
                    !resumed.IsBusy)
                    completed.TrySetResult(true);
            };

            resumed.TopUpCommand.Execute(null);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, client.WalletTopUpCallCount);
            Assert.Equal(originalKey, client.WalletTopUpIdempotencyKeys[1]);
            Assert.Equal(100, client.WalletBalanceToman);
            Assert.True(resumed.CanEditInput);
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, recursive: true);
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
    }

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
    public async Task Start_session_submits_the_selected_registered_customer_id()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-CUS-01", "Customer Test PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-CUS", "Customer Test Tariff", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var customer = new CustomerResponse(
                Guid.NewGuid(), "C-TEST-01", "Test Player", "Player", "09121234567",
                CustomerKindContract.Registered, CustomerStatusContract.Active, DateTimeOffset.UtcNow, 1);
            var client = new FakeGameNetServerClient([station], [tariff]);
            client.Customers.Add(customer);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission is Permissions.StationsRead or Permissions.SessionsOperate or Permissions.CustomersRead);

            await viewModel.RefreshAsync();
            Assert.Single(viewModel.AvailableCustomers);
            viewModel.SelectedCustomer = Assert.Single(viewModel.AvailableCustomers);
            viewModel.SelectedStation = station;
            viewModel.SelectedTariff = tariff;

            // Manager searches must not remove a valid customer from the independent
            // Session picker or silently alter the identity used by StartSession.
            viewModel.CustomerSearchText = "no such customer";
            viewModel.SearchCustomersCommand.Execute(null);
            Assert.Empty(viewModel.Customers);
            Assert.Contains(viewModel.AvailableCustomers, x => x.Id == customer.Id);
            Assert.Equal(customer.Id, viewModel.SelectedCustomer?.Id);

            // The dedicated Session picker can search older customers independently
            // of the manager filter and resolves a concrete Server-owned customer ID.
            viewModel.SessionCustomerSearchText = "C-TEST-01";
            viewModel.SearchSessionCustomersCommand.Execute(null);
            Assert.Single(viewModel.AvailableCustomers);
            Assert.Equal(customer.Id, Assert.Single(viewModel.AvailableCustomers).Id);
            Assert.Equal(customer.Id, viewModel.SelectedCustomer?.Id);

            var startStatus = WaitForStatusAsync(
                viewModel, message => message.Contains("customer C-TEST-01", StringComparison.Ordinal));
            Assert.True(viewModel.StartSessionCommand.CanExecute(null));
            viewModel.StartSessionCommand.Execute(null);
            await client.StartRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(client.LastStartRequest);
            Assert.Equal(customer.Id, client.LastStartRequest!.CustomerId);
            Assert.False(string.IsNullOrWhiteSpace(client.LastStartIdempotencyKey));

            var session = new SessionResponse(
                Guid.NewGuid(), station.Id, customer.Id, tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman,
                Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, null,
                SessionStatusContract.Active, 1);
            client.ActiveSessions.Add(session);
            client.StartSessionCompletion.SetResult(session);

            Assert.Contains("C-TEST-01", await startStatus);
            Assert.Equal(session.Id, viewModel.SelectedActiveSession?.Session.Id);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
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
            viewModel.ConfirmCashCheckout = true;
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
    public async Task End_session_keeps_server_confirmed_invoice_visible_when_secondary_refresh_fails()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-INVOICE-01", "Invoice PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var tariff = new TariffResponse(
                Guid.NewGuid(), "PC-STD", "PC Standard", StationTypeContract.Pc,
                60_000L, true, true, DateTimeOffset.UtcNow, 1);
            var client = new FakeGameNetServerClient([station], [tariff]);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission is Permissions.StationsRead
                    or Permissions.SessionsOperate or Permissions.BillingWrite);

            await viewModel.RefreshAsync();
            var session = new SessionResponse(
                Guid.NewGuid(), station.Id, Guid.NewGuid(), tariff.Id,
                tariff.Code, tariff.Name, tariff.HourlyRateToman,
                Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, null,
                SessionStatusContract.Active, 1);
            client.ActiveSessions.Add(session);
            await viewModel.RefreshAsync();
            viewModel.SelectedActiveSession = viewModel.ActiveSessions.Single();

            // Ending the Session is confirmed by the Server, but an unrelated tariff refresh fails
            // before the full pending-invoice refresh can run.
            client.NextGetTariffsException = new GameNetApiException("tariffs.lookup_failed", 500);
            var endStatus = WaitForStatusAsync(
                viewModel,
                message => message.StartsWith("Invoice issued:", StringComparison.Ordinal));
            Assert.True(viewModel.EndSessionCommand.CanExecute(null));
            viewModel.EndSessionCommand.Execute(null);

            Assert.Contains("30,000 Toman", await endStatus);
            var pending = Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(InvoiceStatusContract.PendingPayment, pending.Invoice.Status);
            Assert.Equal(pending.Invoice.InvoiceId, viewModel.SelectedPendingInvoice?.Invoice.InvoiceId);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
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
    public async Task Start_session_shows_actionable_error_when_pc_agent_is_unavailable()
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-05", "Offline Agent PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, "agent-device-05", false, null, null, null);
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
            client.NextStartException = new GameNetApiException("sessions.agent_unavailable", 409);

            var error = WaitForErrorAsync(
                viewModel,
                message => message.Contains(
                    "This PC's Agent is not connected or has no active credential and fresh heartbeat/lease.",
                    StringComparison.Ordinal));

            Assert.True(viewModel.StartSessionCommand.CanExecute(null));
            viewModel.StartSessionCommand.Execute(null);
            var message = await error;

            Assert.Contains("Check the Agent connection; the Session was not started.", message);
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
                message => message.Contains("Server confirms this invoice is still pending", StringComparison.Ordinal));
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.CheckoutCashCommand.Execute(null);
            Assert.Contains("Server confirms this invoice is still pending", await error);

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
    public async Task Unknown_checkout_outcome_blocks_retry_until_refresh_confirms_server_state()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-06", "Unknown outcome PC", StationTypeContract.Pc,
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
            client.FailNextCashCheckoutWithNetworkError = true;
            client.FailNextCashCheckoutOutcomeLookupWithNetworkError = true;

            var error = WaitForErrorAsync(
                viewModel,
                message => message.Contains("Checkout outcome is still unverified", StringComparison.Ordinal));
            Assert.True(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.CheckoutCashCommand.Execute(null);
            Assert.Contains("Checkout outcome is still unverified", await error);

            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
            viewModel.AmountReceivedText = "50000";
            viewModel.ConfirmCashCheckout = true;
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));

            // A later explicit refresh must first reconcile the unresolved invoice with the Server.
            await viewModel.RefreshAsync();
            Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(string.Empty, viewModel.AmountReceivedText);
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));

            viewModel.AmountReceivedText = "50000";
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
    public async Task Cash_checkout_timeout_reconciles_server_state_before_retry()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-07", "Timeout PC", StationTypeContract.Pc,
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
            client.FailNextCashCheckoutWithTimeout = true;

            var error = WaitForErrorAsync(
                viewModel,
                message => message.Contains("Server confirms this invoice is still pending", StringComparison.Ordinal));
            viewModel.CheckoutCashCommand.Execute(null);
            Assert.Contains("Server confirms this invoice is still pending", await error);

            Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(invoice.InvoiceId, client.LastCheckoutInvoiceId);
            Assert.Single(client.CheckoutIdempotencyKeys);
            Assert.Equal(string.Empty, viewModel.AmountReceivedText);
            Assert.False(viewModel.ConfirmCashCheckout);
            Assert.False(viewModel.CheckoutCashCommand.CanExecute(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Dedicated_cash_reverse_permission_can_reconcile_a_lost_reversal_response_without_refunding_twice()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-08", "Cash reversal PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var invoiceId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            var ledgerId = Guid.NewGuid();
            var paidAt = DateTimeOffset.UtcNow;
            var outcome = new CashCheckoutOutcomeResponse(
                invoiceId, Guid.NewGuid(), station.Id, "PC-STD", "PC Standard",
                60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L,
                InvoiceStatusContract.Paid, paymentId, ledgerId, 50_000L, 20_000L,
                paidAt.AddMinutes(-30), paidAt);
            var client = new FakeGameNetServerClient([station], []);
            client.CompletedCashOutcomes.Add(outcome);
            client.LoseNextCashSaleReversalResponseAfterCommit = true;
            var viewModel = new SessionWorkflowViewModel(
                client, permission => permission == Permissions.BillingCashReverse);

            await viewModel.RefreshAsync();

            Assert.True(viewModel.CanReverseCashSale);
            Assert.True(viewModel.CanUseWorkspace);
            Assert.False(viewModel.CanCheckout);
            Assert.Single(viewModel.RecentCashCheckouts);
            viewModel.SelectedRecentCashCheckout = viewModel.RecentCashCheckouts.Single();
            Assert.Contains("returns only the invoice amount: 30,000 Toman", viewModel.CashSaleReversalSummaryText);

            viewModel.CashSaleReversalReasonText = "Customer requested full reversal";
            viewModel.ConfirmCashSaleReversal = true;
            Assert.True(viewModel.ReverseCashSaleCommand.CanExecute(null));

            var statusTask = WaitForStatusAsync(
                viewModel,
                message => message.Contains("Server confirms the reversal was already recorded", StringComparison.Ordinal));
            viewModel.ReverseCashSaleCommand.Execute(null);
            Assert.Contains("Server confirms the reversal was already recorded", await statusTask);

            Assert.Equal(invoiceId, client.LastCashSaleReversalInvoiceId);
            Assert.Equal(paymentId, client.LastCashSaleReversalRequest?.PaymentId);
            Assert.Equal(30_000L, client.LastCashSaleReversalRequest?.ConfirmedAmountToman);
            Assert.Equal("Customer requested full reversal", client.LastCashSaleReversalRequest?.Reason);
            Assert.Single(client.CashSaleReversalIdempotencyKeys);
            Assert.Equal(InvoiceStatusContract.Reversed, client.CompletedCashOutcomes.Single().Status);
            Assert.NotNull(client.CompletedCashOutcomes.Single().ReversalLedgerEntryId);
            Assert.Equal(30_000L, client.CompletedCashOutcomes.Single().AmountToman);
            Assert.Equal(50_000L, client.CompletedCashOutcomes.Single().AmountReceivedToman);
            Assert.Equal(20_000L, client.CompletedCashOutcomes.Single().ChangeGivenToman);
            Assert.False(viewModel.ReverseCashSaleCommand.CanExecute(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }


    [Fact]
    public async Task Cash_tender_correction_updates_effective_facts_and_preserves_original_payment_facts()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var invoiceId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            var ledgerEntryId = Guid.NewGuid();
            var stationId = Guid.NewGuid();
            var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
            var paidAt = DateTimeOffset.UtcNow;
            var originalOutcome = new CashCheckoutOutcomeResponse(
                invoiceId, Guid.NewGuid(), stationId, "PC-STD", "PC Standard", 60_000L,
                TimeSpan.FromMinutes(30).Ticks, 30_000L, InvoiceStatusContract.Paid,
                paymentId, ledgerEntryId, 50_000L, 20_000L, issuedAt, paidAt);
            var client = new FakeGameNetServerClient(
                Array.Empty<StationResponse>(), Array.Empty<TariffResponse>());
            client.CompletedCashOutcomes.Add(originalOutcome);
            var viewModel = new SessionWorkflowViewModel(
                client, permission => permission == Permissions.BillingCashTenderCorrect);

            await viewModel.RefreshAsync();
            viewModel.SelectedRecentCashCheckout = Assert.Single(viewModel.RecentCashCheckouts);
            viewModel.CorrectedCashReceivedText = "45000";
            viewModel.CashTenderCorrectionReasonText = "Cash received was keyed incorrectly";

            Assert.False(viewModel.CorrectCashTenderCommand.CanExecute(null));
            viewModel.ConfirmCashTenderCorrection = true;
            Assert.True(viewModel.CorrectCashTenderCommand.CanExecute(null));

            var completed = WaitForStatusAsync(
                viewModel,
                message => message.Contains("Cash tender facts corrected at version 1.", StringComparison.Ordinal));
            viewModel.CorrectCashTenderCommand.Execute(null);
            var status = await completed;

            Assert.Contains("The invoice amount and sale ledger were unchanged.", status);
            Assert.Equal(1, client.CashTenderCorrectionWriteCount);
            Assert.Equal(invoiceId, client.LastCashTenderCorrectionInvoiceId);
            Assert.Equal(paymentId, client.LastCashTenderCorrectionRequest?.PaymentId);
            Assert.Equal(0, client.LastCashTenderCorrectionRequest?.ExpectedVersion);
            Assert.Equal(45_000L, client.LastCashTenderCorrectionRequest?.CorrectedAmountReceivedToman);
            Assert.Equal(true, client.LastCashTenderCorrectionRequest?.Confirmed);
            Assert.False(string.IsNullOrWhiteSpace(client.CashTenderCorrectionIdempotencyKeys.Single()));

            var corrected = Assert.Single(client.CompletedCashOutcomes);
            Assert.Equal(30_000L, corrected.AmountToman);
            Assert.Equal(paymentId, corrected.PaymentId);
            Assert.Equal(ledgerEntryId, corrected.LedgerEntryId);
            Assert.Equal(50_000L, corrected.AmountReceivedToman);
            Assert.Equal(20_000L, corrected.ChangeGivenToman);
            Assert.Equal(45_000L, corrected.EffectiveAmountReceivedToman);
            Assert.Equal(15_000L, corrected.EffectiveChangeGivenToman);
            Assert.Equal(1, corrected.CashTenderCorrectionVersion);
            Assert.Equal("Cash received was keyed incorrectly", corrected.CashTenderCorrectionReason);
            Assert.NotNull(corrected.CashTenderCorrectionId);
            Assert.NotNull(corrected.CashTenderCorrectedAtUtc);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Cash_tender_correction_recovers_committed_result_after_response_loss_without_second_write()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var invoiceId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            var stationId = Guid.NewGuid();
            var originalOutcome = new CashCheckoutOutcomeResponse(
                invoiceId, Guid.NewGuid(), stationId, "PC-STD", "PC Standard", 60_000L,
                TimeSpan.FromMinutes(30).Ticks, 30_000L, InvoiceStatusContract.Paid,
                paymentId, Guid.NewGuid(), 50_000L, 20_000L,
                DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow);
            var client = new FakeGameNetServerClient(
                Array.Empty<StationResponse>(), Array.Empty<TariffResponse>());
            client.CompletedCashOutcomes.Add(originalOutcome);
            client.LoseNextCashTenderCorrectionResponseAfterCommit = true;
            var viewModel = new SessionWorkflowViewModel(
                client, permission => permission == Permissions.BillingCashTenderCorrect);

            await viewModel.RefreshAsync();
            viewModel.SelectedRecentCashCheckout = Assert.Single(viewModel.RecentCashCheckouts);
            viewModel.CorrectedCashReceivedText = "45000";
            viewModel.CashTenderCorrectionReasonText = "Correct recorded cash amount";
            viewModel.ConfirmCashTenderCorrection = true;

            var reconciled = WaitForStatusAsync(
                viewModel,
                message => message.Contains(
                    "Server confirms the tender correction was recorded at version 1.",
                    StringComparison.Ordinal));
            Assert.True(viewModel.CorrectCashTenderCommand.CanExecute(null));
            viewModel.CorrectCashTenderCommand.Execute(null);
            var status = await reconciled;

            Assert.Contains("The invoice amount and sale ledger were unchanged.", status);
            Assert.Equal(1, client.CashTenderCorrectionWriteCount);
            Assert.Single(client.CashTenderCorrectionIdempotencyKeys);
            Assert.False(viewModel.CorrectCashTenderCommand.CanExecute(null));
            var corrected = Assert.Single(client.CompletedCashOutcomes);
            Assert.Equal(50_000L, corrected.AmountReceivedToman);
            Assert.Equal(20_000L, corrected.ChangeGivenToman);
            Assert.Equal(45_000L, corrected.EffectiveAmountReceivedToman);
            Assert.Equal(15_000L, corrected.EffectiveChangeGivenToman);
            Assert.Equal(1, corrected.CashTenderCorrectionVersion);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Timing_recovery_requires_independent_permission_and_records_verified_duration_through_server()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var station = new StationResponse(
                Guid.NewGuid(), "PC-TIME-01", "Timing recovery PC", StationTypeContract.Pc,
                StationStatusContract.Available, 1, null, false, null, null, null);
            var now = DateTimeOffset.UtcNow;
            var session = new SessionResponse(
                Guid.NewGuid(), station.Id, Guid.NewGuid(), Guid.NewGuid(), "PC-STD", "PC Standard",
                60_000L, Guid.NewGuid(), now, null, null, null, null, SessionStatusContract.Active, 1);
            var client = new FakeGameNetServerClient([station], []);
            client.ActiveSessions.Add(session);
            var viewModel = new SessionWorkflowViewModel(
                client,
                permission => permission is Permissions.StationsRead or Permissions.SessionsOperate
                    or Permissions.SessionsTimingRecover or Permissions.BillingWrite);

            await viewModel.RefreshAsync();
            viewModel.SelectedActiveSession = viewModel.ActiveSessions.Single();
            client.NextEndException = new GameNetApiException("sessions.timing_recovery_required", 409);
            var recoveryRequiredTask = WaitForErrorAsync(
                viewModel,
                message => message.Contains("timing continuity", StringComparison.Ordinal));
            viewModel.EndSessionCommand.Execute(null);
            Assert.Contains("timing continuity", await recoveryRequiredTask);
            Assert.True(viewModel.CanRecoverSessionTiming);
            viewModel.ReconciledElapsedMinutesText = "30";
            viewModel.TimingRecoveryReasonText = "Verified play duration after reboot";
            viewModel.ConfirmTimingRecovery = true;

            Assert.True(viewModel.CanRecoverSessionTiming);
            Assert.True(viewModel.ReconcileSessionTimingCommand.CanExecute(null));

            var statusTask = WaitForStatusAsync(
                viewModel,
                message => message.Contains("Session timing was reconciled", StringComparison.Ordinal));
            viewModel.ReconcileSessionTimingCommand.Execute(null);
            Assert.Contains("Session timing was reconciled", await statusTask);

            Assert.Equal(session.Id, client.LastTimingRecoverySessionId);
            Assert.Equal(session.Version, client.LastTimingRecoveryRequest?.ExpectedSessionVersion);
            Assert.Equal(30L, client.LastTimingRecoveryRequest?.ReconciledElapsedMinutes);
            Assert.Equal("Verified play duration after reboot", client.LastTimingRecoveryRequest?.Reason);
            Assert.True(client.LastTimingRecoveryRequest?.Confirmed);
            Assert.Single(client.TimingRecoveryIdempotencyKeys);
            var invoice = Assert.Single(viewModel.PendingInvoices);
            Assert.Equal(session.Id, invoice.Invoice.SessionId);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, invoice.Invoice.ElapsedTicks);
            Assert.Equal(30_000L, invoice.Invoice.AmountToman);
            Assert.Empty(viewModel.ActiveSessions);
            Assert.False(viewModel.ReconcileSessionTimingCommand.CanExecute(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Timing_recovery_command_is_disabled_without_separate_recovery_permission()
    {
        var station = new StationResponse(
            Guid.NewGuid(), "PC-TIME-02", "Restricted timing recovery PC", StationTypeContract.Pc,
            StationStatusContract.Available, 1, null, false, null, null, null);
        var now = DateTimeOffset.UtcNow;
        var session = new SessionResponse(
            Guid.NewGuid(), station.Id, Guid.NewGuid(), Guid.NewGuid(), "PC-STD", "PC Standard",
            60_000L, Guid.NewGuid(), now, null, null, null, null, SessionStatusContract.Active, 1);
        var client = new FakeGameNetServerClient([station], []);
        client.ActiveSessions.Add(session);
        var viewModel = new SessionWorkflowViewModel(
            client,
            permission => permission is Permissions.StationsRead or Permissions.SessionsOperate or Permissions.BillingWrite);

        await viewModel.RefreshAsync();
        viewModel.SelectedActiveSession = viewModel.ActiveSessions.Single();
        viewModel.ReconciledElapsedMinutesText = "30";
        viewModel.TimingRecoveryReasonText = "Verified play duration after reboot";
        viewModel.ConfirmTimingRecovery = true;

        Assert.False(viewModel.CanRecoverSessionTiming);
        Assert.False(viewModel.ReconcileSessionTimingCommand.CanExecute(null));
        Assert.Equal(0, client.TimingRecoveryCallCount);
        Assert.Single(viewModel.ActiveSessions);
        Assert.Empty(viewModel.PendingInvoices);
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
        Assert.False(viewModel.ReverseCashSaleCommand.CanExecute(null));

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
        public List<string> CashSaleReversalIdempotencyKeys { get; } = [];
        public List<string> CashTenderCorrectionIdempotencyKeys { get; } = [];
        public Guid? LastCashTenderCorrectionInvoiceId { get; private set; }
        public CashTenderCorrectionRequest? LastCashTenderCorrectionRequest { get; private set; }
        public int CashTenderCorrectionWriteCount { get; private set; }
        public bool LoseNextCashTenderCorrectionResponseAfterCommit { get; set; }
        private readonly Dictionary<string, CashTenderCorrectionResponse> cashTenderCorrectionResponses = new(StringComparer.Ordinal);
        public Guid? LastCashSaleReversalInvoiceId { get; private set; }
        public CashSaleReversalRequest? LastCashSaleReversalRequest { get; private set; }
        public bool LoseNextCashSaleReversalResponseAfterCommit { get; set; }
        public Exception? NextStartException { get; set; }
        public Exception? NextEndException { get; set; }
        public Exception? NextGetTariffsException { get; set; }
        public bool FailNextCashCheckoutWithNetworkError { get; set; }
        public bool FailNextCashCheckoutWithTimeout { get; set; }
        public bool FailNextCashCheckoutOutcomeLookupWithNetworkError { get; set; }
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

        public List<CustomerResponse> Customers { get; } = [];

        public long WalletBalanceToman { get; private set; }
        public int WalletBalanceVersion { get; private set; }
        public int WalletTopUpCallCount { get; private set; }
        public bool LoseNextWalletTopUpResponseAfterCommit { get; set; }
        public List<string> WalletTopUpIdempotencyKeys { get; } = [];
        private readonly Dictionary<string, WalletCashTopUpResponse> walletTopUpResponses = new(StringComparer.Ordinal);

        public Task<WalletBalanceResponse> GetWalletBalanceAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WalletBalanceResponse(customerId, WalletBalanceToman, WalletBalanceVersion, null));

        public Task<WalletCashTopUpResponse> CashTopUpWalletAsync(
            Guid customerId, WalletCashTopUpRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            WalletTopUpCallCount++;
            WalletTopUpIdempotencyKeys.Add(idempotencyKey);
            if (walletTopUpResponses.TryGetValue(idempotencyKey, out var replay))
                return Task.FromResult(replay);

            WalletBalanceToman = checked(WalletBalanceToman + request.AmountToCreditToman);
            WalletBalanceVersion++;
            var response = new WalletCashTopUpResponse(
                customerId, Guid.NewGuid(), Guid.NewGuid(), request.AmountToCreditToman,
                request.AmountReceivedToman, request.AmountReceivedToman - request.AmountToCreditToman,
                WalletBalanceToman, WalletBalanceVersion, DateTimeOffset.UtcNow);
            walletTopUpResponses[idempotencyKey] = response;

            if (LoseNextWalletTopUpResponseAfterCommit)
            {
                LoseNextWalletTopUpResponseAfterCommit = false;
                throw new HttpRequestException("Simulated lost response after the server committed the top-up.");
            }

            return Task.FromResult(response);
        }

        public Task<IReadOnlyList<CustomerResponse>> GetCustomersAsync(string? query = null, bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            IEnumerable<CustomerResponse> rows = Customers;
            if (!includeInactive)
                rows = rows.Where(x => x.Status == CustomerStatusContract.Active);
            if (!string.IsNullOrWhiteSpace(query))
            {
                var term = query.Trim();
                rows = rows.Where(x =>
                    x.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Nickname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.PhoneNumber?.Contains(term, StringComparison.Ordinal) ?? false));
            }
            return Task.FromResult<IReadOnlyList<CustomerResponse>>(rows.Take(100).ToArray());
        }
        public Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerResponse> UpdateCustomerProfileAsync(Guid id, UpdateCustomerRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerResponse> SetCustomerStatusAsync(Guid id, SetCustomerStatusRequest request, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TariffResponse>> GetTariffsAsync(StationTypeContract? stationType = null, CancellationToken cancellationToken = default)
        {
            if (NextGetTariffsException is { } error)
            {
                NextGetTariffsException = null;
                return Task.FromException<IReadOnlyList<TariffResponse>>(error);
            }

            return Task.FromResult<IReadOnlyList<TariffResponse>>(tariffs
                .Where(tariff => stationType is null || tariff.StationType == stationType)
                .ToArray());
        }

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
            if (NextEndException is { } error)
            {
                NextEndException = null;
                return Task.FromException<InvoicePreviewResponse>(error);
            }

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
        public List<string> TimingRecoveryIdempotencyKeys { get; } = [];
        public Guid? LastTimingRecoverySessionId { get; private set; }
        public ReconcileSessionTimingRequest? LastTimingRecoveryRequest { get; private set; }
        public int TimingRecoveryCallCount { get; private set; }
        private readonly Dictionary<string, InvoicePreviewResponse> timingRecoveryReplays = new(StringComparer.Ordinal);
        public bool LoseNextCashCheckoutResponseAfterCommit { get; set; }

        public Task<InvoicePreviewResponse> ReconcileSessionTimingAsync(
            Guid id,
            ReconcileSessionTimingRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            TimingRecoveryCallCount++;
            LastTimingRecoverySessionId = id;
            LastTimingRecoveryRequest = request;
            TimingRecoveryIdempotencyKeys.Add(idempotencyKey);
            if (timingRecoveryReplays.TryGetValue(idempotencyKey, out var replay))
                return Task.FromResult(replay);

            var session = ActiveSessions.SingleOrDefault(x => x.Id == id);
            if (session is null || session.Version != request.ExpectedSessionVersion ||
                request.ReconciledElapsedMinutes is < 1 or > 43_200 || !request.Confirmed)
                return Task.FromException<InvoicePreviewResponse>(
                    new GameNetApiException("sessions.timing_recovery_invalid", 400));

            var elapsedTicks = checked(request.ReconciledElapsedMinutes * TimeSpan.TicksPerMinute);
            var amount = checked((session.TariffHourlyRateTomanSnapshot * request.ReconciledElapsedMinutes + 59L) / 60L);
            var invoice = new InvoicePreviewResponse(
                Guid.NewGuid(), session.Id, checked(session.Version + 1), session.StationId,
                session.CustomerId, session.TariffId, session.TariffCodeSnapshot,
                session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
                elapsedTicks, amount, InvoiceStatusContract.PendingPayment, DateTimeOffset.UtcNow, null);
            ActiveSessions.Remove(session);
            PendingInvoices.Add(invoice);
            timingRecoveryReplays.Add(idempotencyKey, invoice);
            return Task.FromResult(invoice);
        }

        public Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoicePreviewResponse>>(PendingInvoices.ToArray());

        public Task<CashCheckoutOutcomeResponse> GetCashCheckoutOutcomeAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            if (FailNextCashCheckoutOutcomeLookupWithNetworkError)
            {
                FailNextCashCheckoutOutcomeLookupWithNetworkError = false;
                return Task.FromException<CashCheckoutOutcomeResponse>(new HttpRequestException("Simulated checkout outcome lookup failure."));
            }

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
            if (FailNextCashCheckoutWithTimeout)
            {
                FailNextCashCheckoutWithTimeout = false;
                return Task.FromException<CashCheckoutResponse>(new TaskCanceledException("Simulated checkout timeout before commit."));
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
        public Task<CashSaleReversalResponse> ReverseCashSaleAsync(
            Guid invoiceId, CashSaleReversalRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            LastCashSaleReversalInvoiceId = invoiceId;
            LastCashSaleReversalRequest = request;
            CashSaleReversalIdempotencyKeys.Add(idempotencyKey);

            var index = CompletedCashOutcomes.FindIndex(x => x.InvoiceId == invoiceId);
            if (index < 0)
                return Task.FromException<CashSaleReversalResponse>(new GameNetApiException("billing.invoice_not_found", 404));

            var outcome = CompletedCashOutcomes[index];
            if (outcome.Status != InvoiceStatusContract.Paid ||
                outcome.PaymentId != request.PaymentId ||
                outcome.AmountToman != request.ConfirmedAmountToman)
                return Task.FromException<CashSaleReversalResponse>(new GameNetApiException("billing.reversal_confirmation_mismatch", 409));

            var reversedAt = DateTimeOffset.UtcNow;
            var reversalId = Guid.NewGuid();
            var reversalLedgerId = Guid.NewGuid();
            CompletedCashOutcomes[index] = outcome with
            {
                Status = InvoiceStatusContract.Reversed,
                ReversalId = reversalId,
                ReversedAtUtc = reversedAt,
                ReversalReason = request.Reason,
                ReversalLedgerEntryId = reversalLedgerId
            };

            var response = new CashSaleReversalResponse(
                reversalId, invoiceId, request.PaymentId, reversalLedgerId, request.ConfirmedAmountToman, reversedAt);
            if (LoseNextCashSaleReversalResponseAfterCommit)
            {
                LoseNextCashSaleReversalResponseAfterCommit = false;
                return Task.FromException<CashSaleReversalResponse>(
                    new HttpRequestException("Simulated connection loss after a committed cash-sale reversal."));
            }

            return Task.FromResult(response);
        }


        public Task<CashTenderCorrectionResponse> CorrectCashTenderAsync(
            Guid invoiceId,
            CashTenderCorrectionRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            LastCashTenderCorrectionInvoiceId = invoiceId;
            LastCashTenderCorrectionRequest = request;
            CashTenderCorrectionIdempotencyKeys.Add(idempotencyKey);

            if (cashTenderCorrectionResponses.TryGetValue(idempotencyKey, out var replay))
                return Task.FromResult(replay);

            var index = CompletedCashOutcomes.FindIndex(x => x.InvoiceId == invoiceId);
            if (index < 0)
                return Task.FromException<CashTenderCorrectionResponse>(
                    new GameNetApiException("billing.invoice_not_found", 404));

            var outcome = CompletedCashOutcomes[index];
            if (outcome.Status != InvoiceStatusContract.Paid || outcome.ReversalId is not null ||
                outcome.PaymentId != request.PaymentId ||
                outcome.AmountReceivedToman is not long originalReceived ||
                outcome.ChangeGivenToman is not long originalChange ||
                outcome.PaidAtUtc is null || outcome.LedgerEntryId is null)
                return Task.FromException<CashTenderCorrectionResponse>(
                    new GameNetApiException("billing.checkout_outcome_inconsistent", 409));

            var version = outcome.CashTenderCorrectionVersion ?? 0;
            var currentReceived = outcome.EffectiveAmountReceivedToman ?? originalReceived;
            var currentChange = outcome.EffectiveChangeGivenToman ?? originalChange;
            if (version != request.ExpectedVersion)
                return Task.FromException<CashTenderCorrectionResponse>(
                    new GameNetApiException("billing.tender_correction_version_conflict", 409));
            if (currentReceived < outcome.AmountToman || currentChange != currentReceived - outcome.AmountToman ||
                request.CorrectedAmountReceivedToman < outcome.AmountToman ||
                request.CorrectedAmountReceivedToman == currentReceived)
                return Task.FromException<CashTenderCorrectionResponse>(
                    new GameNetApiException("billing.tender_correction_invalid", 409));

            var correctedAt = DateTimeOffset.UtcNow;
            var correctedChange = checked(request.CorrectedAmountReceivedToman - outcome.AmountToman);
            var response = new CashTenderCorrectionResponse(
                Guid.NewGuid(), invoiceId, request.PaymentId, checked(version + 1),
                currentReceived, currentChange, request.CorrectedAmountReceivedToman,
                correctedChange, request.Reason, correctedAt);
            CompletedCashOutcomes[index] = outcome with
            {
                EffectiveAmountReceivedToman = request.CorrectedAmountReceivedToman,
                EffectiveChangeGivenToman = correctedChange,
                CashTenderCorrectionId = response.CorrectionId,
                CashTenderCorrectionVersion = response.Version,
                CashTenderCorrectionReason = response.Reason,
                CashTenderCorrectedAtUtc = response.CorrectedAtUtc
            };
            cashTenderCorrectionResponses.Add(idempotencyKey, response);
            CashTenderCorrectionWriteCount++;

            if (LoseNextCashTenderCorrectionResponseAfterCommit)
            {
                LoseNextCashTenderCorrectionResponseAfterCommit = false;
                return Task.FromException<CashTenderCorrectionResponse>(
                    new HttpRequestException("Simulated connection loss after a committed cash tender correction."));
            }

            return Task.FromResult(response);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ClearSession() { }
    }
}
