using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Desktop.Features.Stations;
using GameNet.Desktop.Features.Sessions;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Billing;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Tariffs;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Contracts.V1.System;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class StationBoardPermissionTests
{
    [Fact]
    public async Task Login_uses_authoritative_permissions_and_logout_removes_station_access()
    {
        var api = new PermissionTestServerClient
        {
            CurrentOperator = new CurrentOperatorResponse(
                Guid.Parse("c638fe1b-0de3-4a40-9e8d-873c7afc4c2d"),
                "read-only",
                "Read Only",
                [Permissions.StationsRead])
        };
        var login = new LoginViewModel(api)
        {
            Username = "read-only",
            Password = "test-password"
        };

        login.LoginCommand.Execute(null);
        await WaitUntilAsync(() => !login.IsBusy);

        Assert.True(login.IsAuthenticated);
        Assert.True(login.CanReadStations);
        Assert.False(login.CanOperateStations);
        Assert.True(login.StationBoard.RefreshCommand.CanExecute(null));
        Assert.False(login.StationBoard.CreateCommand.CanExecute(null));
        Assert.Equal(1, api.ListCalls);

        login.LogoutCommand.Execute(null);
        await WaitUntilAsync(() => !login.IsBusy);

        Assert.False(login.IsAuthenticated);
        Assert.False(login.CanReadStations);
        Assert.False(login.CanOperateStations);
        Assert.False(login.StationBoard.RefreshCommand.CanExecute(null));
        Assert.Empty(login.StationBoard.Stations);
    }

    [Fact]
    public async Task Missing_read_permission_blocks_refresh_before_any_server_request()
    {
        var api = new PermissionTestServerClient();
        var board = new StationBoardViewModel(api, _ => false);

        Assert.False(board.RefreshCommand.CanExecute(null));

        await board.RefreshAsync();

        Assert.Equal(0, api.ListCalls);
        Assert.NotEmpty(board.ErrorMessage);

        board.RefreshCommand.Execute(null);
        Assert.Equal(0, api.ListCalls);
    }

    [Fact]
    public async Task Read_only_operator_can_list_and_probe_but_cannot_mutate_stations()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal) { Permissions.StationsRead };
        var api = new PermissionTestServerClient();
        var board = new StationBoardViewModel(api, granted.Contains);

        await board.RefreshAsync();
        Assert.Equal(1, api.ListCalls);
        Assert.Single(board.Stations);

        board.NewCode = "PC-02";
        board.NewName = "Second PC";
        board.SelectedStation = new StationBoardItem(PermissionTestServerClient.TestStation());
        board.RenameName = "Renamed PC";
        board.DeviceId = "agent-other";

        Assert.True(board.RefreshCommand.CanExecute(null));
        Assert.False(board.CreateCommand.CanExecute(null));
        Assert.False(board.RenameCommand.CanExecute(null));
        Assert.False(board.BindAgentCommand.CanExecute(null));
        Assert.False(board.MaintenanceCommand.CanExecute(null));
        Assert.False(board.AvailableCommand.CanExecute(null));
        Assert.True(board.AgentHealthProbeCommand.CanExecute(null));

        // ICommand.Execute does not enforce CanExecute in this implementation; each operation
        // must still fail closed in its handler so code-triggered execution cannot send a request.
        board.CreateCommand.Execute(null);
        board.RenameCommand.Execute(null);
        board.BindAgentCommand.Execute(null);
        board.MaintenanceCommand.Execute(null);
        board.AvailableCommand.Execute(null);

        Assert.Equal(0, api.MutationCalls);
    }

    [Fact]
    public async Task Late_station_response_cannot_repopulate_cache_after_read_permission_is_revoked()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal) { Permissions.StationsRead };
        var response = new TaskCompletionSource<IReadOnlyList<StationResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new PermissionTestServerClient { PendingListResponse = response.Task };
        var board = new StationBoardViewModel(api, granted.Contains);

        var refresh = board.RefreshAsync();
        Assert.Equal(1, api.ListCalls);
        Assert.False(board.RefreshCommand.CanExecute(null));

        granted.Clear();
        board.NotifyPermissionsChanged();
        Assert.Empty(board.Stations);

        response.SetResult([PermissionTestServerClient.TestStation()]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(board.Stations);
        Assert.Empty(board.VisibleStations);
        Assert.Null(board.SelectedStation);
        Assert.False(board.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Operate_permission_enables_mutation_only_when_read_permission_is_also_present()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.StationsRead,
            Permissions.StationsOperate
        };
        var api = new PermissionTestServerClient();
        var board = new StationBoardViewModel(api, granted.Contains);
        var createCommandNotifications = 0;
        board.CreateCommand.CanExecuteChanged += (_, _) => createCommandNotifications++;

        await board.RefreshAsync();
        var notificationsBeforeInput = createCommandNotifications;
        board.NewCode = "PC-02";
        board.NewName = "Second PC";
        Assert.True(createCommandNotifications >= notificationsBeforeInput + 2);
        board.SelectedStation = new StationBoardItem(PermissionTestServerClient.TestStation());
        board.RenameName = "Renamed PC";
        board.DeviceId = "agent-other";

        Assert.True(board.CanReadStations);
        Assert.True(board.CanOperateStations);
        Assert.True(board.CreateCommand.CanExecute(null));
        Assert.True(board.RenameCommand.CanExecute(null));
        Assert.True(board.BindAgentCommand.CanExecute(null));
        Assert.True(board.MaintenanceCommand.CanExecute(null));
        Assert.True(board.AvailableCommand.CanExecute(null));
    }

    [Fact]
    public async Task Revoking_read_permission_clears_cached_station_data_and_disables_commands()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.StationsRead,
            Permissions.StationsOperate
        };
        var api = new PermissionTestServerClient();
        var board = new StationBoardViewModel(api, granted.Contains);

        await board.RefreshAsync();
        Assert.Single(board.Stations);

        var commandNotifications = 0;
        board.RefreshCommand.CanExecuteChanged += (_, _) => commandNotifications++;
        granted.Clear();
        board.NotifyPermissionsChanged();

        Assert.Empty(board.Stations);
        Assert.Empty(board.VisibleStations);
        Assert.Null(board.SelectedStation);
        Assert.False(board.RefreshCommand.CanExecute(null));
        Assert.False(board.CreateCommand.CanExecute(null));
        Assert.True(commandNotifications > 0);

        board.RefreshCommand.Execute(null);
        Assert.Equal(1, api.ListCalls);
    }

    [Fact]
    public async Task Direct_command_execution_cannot_bypass_busy_gate()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.StationsRead,
            Permissions.StationsOperate
        };
        var pendingResponse = new TaskCompletionSource<IReadOnlyList<StationResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new PermissionTestServerClient { PendingListResponse = pendingResponse.Task };
        var board = new StationBoardViewModel(api, granted.Contains)
        {
            NewCode = "PC-02",
            NewName = "Second PC"
        };
        board.SelectedStation = new StationBoardItem(PermissionTestServerClient.TestStation());
        board.RenameName = "Renamed PC";
        board.DeviceId = "agent-other";

        var refresh = board.RefreshAsync();
        Assert.True(board.IsBusy);
        Assert.Equal(1, api.ListCalls);

        // AsyncUiAction.Execute can invoke delegates without enforcing CanExecute.
        // Handler-level busy checks must prevent concurrent HTTP requests.
        board.RefreshCommand.Execute(null);
        board.CreateCommand.Execute(null);
        board.RenameCommand.Execute(null);
        board.BindAgentCommand.Execute(null);
        board.MaintenanceCommand.Execute(null);
        board.AvailableCommand.Execute(null);
        board.AgentHealthProbeCommand.Execute(null);

        Assert.Equal(1, api.ListCalls);
        Assert.Equal(0, api.MutationCalls);
        Assert.Equal(0, api.ProbeCalls);

        pendingResponse.SetResult([PermissionTestServerClient.TestStation()]);
        await refresh.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(board.IsBusy);
    }

    [Fact]
    public async Task Session_and_invoice_lists_identify_the_correct_station_when_tariff_and_amount_match()
    {
        var now = DateTimeOffset.Parse("2026-10-15T10:00:00Z");
        var stationOne = PermissionTestServerClient.TestStation();
        var stationTwo = new StationResponse(
            Guid.Parse("d2d61c84-0a66-466c-b4a0-bf1e0d6c7002"), "PC-02", "Right PC",
            StationTypeContract.Pc, StationStatusContract.Available, 2, "agent-pc-02", true,
            now, "1.2.3", "Ready");
        var stationThree = new StationResponse(
            Guid.Parse("d2d61c84-0a66-466c-b4a0-bf1e0d6c7003"), "PC-03", "Rear PC A",
            StationTypeContract.Pc, StationStatusContract.Available, 1, "agent-pc-03", true,
            now, "1.2.3", "Ready");
        var stationFour = new StationResponse(
            Guid.Parse("d2d61c84-0a66-466c-b4a0-bf1e0d6c7004"), "PC-04", "Rear PC B",
            StationTypeContract.Pc, StationStatusContract.Available, 1, "agent-pc-04", true,
            now, "1.2.3", "Ready");
        var customerId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var sessionOne = new SessionResponse(
            Guid.NewGuid(), stationOne.Id, customerId, tariffId, "PC-STANDARD", "PC Standard", 60_000L,
            operatorId, now.AddMinutes(-30), null, null, null, null, SessionStatusContract.Active, 1);
        var sessionTwo = new SessionResponse(
            Guid.NewGuid(), stationTwo.Id, customerId, tariffId, "PC-STANDARD", "PC Standard", 60_000L,
            operatorId, now.AddMinutes(-25), null, null, null, null, SessionStatusContract.Active, 1);

        var invoiceOne = new InvoicePreviewResponse(
            Guid.NewGuid(), Guid.NewGuid(), 2, stationThree.Id, customerId, tariffId,
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L,
            InvoiceStatusContract.PendingPayment, now, null);
        var invoiceTwo = new InvoicePreviewResponse(
            Guid.NewGuid(), Guid.NewGuid(), 2, stationFour.Id, customerId, tariffId,
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L,
            InvoiceStatusContract.PendingPayment, now, null);

        var api = new PermissionTestServerClient
        {
            StationsResponse = [stationOne, stationTwo, stationThree, stationFour],
            ActiveSessionsResponse = [sessionOne, sessionTwo],
            PendingInvoicesResponse = [invoiceOne, invoiceTwo]
        };
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.StationsRead,
            Permissions.SessionsOperate,
            Permissions.BillingWrite
        };
        var workflow = new SessionWorkflowViewModel(api, granted.Contains);

        await workflow.RefreshAsync();

        Assert.Equal(2, workflow.ActiveSessions.Count);
        Assert.Equal("PC-01", workflow.ActiveSessions[0].StationLabel);
        Assert.Equal("PC-02", workflow.ActiveSessions[1].StationLabel);
        Assert.Equal("PC Standard", workflow.ActiveSessions[0].TariffNameSnapshot);
        Assert.Equal(workflow.ActiveSessions[0].TariffNameSnapshot, workflow.ActiveSessions[1].TariffNameSnapshot);
        Assert.NotEqual(workflow.ActiveSessions[0].StartedAtDisplayText, workflow.ActiveSessions[1].StartedAtDisplayText);

        Assert.Equal(2, workflow.PendingInvoices.Count);
        Assert.Equal("PC-03", workflow.PendingInvoices[0].StationLabel);
        Assert.Equal("PC-04", workflow.PendingInvoices[1].StationLabel);
        Assert.Equal(workflow.PendingInvoices[0].AmountDisplayText, workflow.PendingInvoices[1].AmountDisplayText);
        Assert.Contains("00:30:00", workflow.PendingInvoices[0].DurationDisplayText, StringComparison.Ordinal);
        Assert.Contains("00:30:00", workflow.PendingInvoices[1].DurationDisplayText, StringComparison.Ordinal);

        workflow.SelectedActiveSession = workflow.ActiveSessions[1];
        Assert.True(workflow.EndSessionCommand.CanExecute(null));
        workflow.SelectedPendingInvoice = workflow.PendingInvoices[1];
        Assert.Equal("30000", workflow.AmountReceivedText);
        Assert.True(workflow.CheckoutCashCommand.CanExecute(null));
    }

    [Fact]
    public async Task Billing_operator_with_station_read_sees_pc_code_without_session_operation_permission()
    {
        var now = DateTimeOffset.Parse("2026-10-15T10:00:00Z");
        var station = PermissionTestServerClient.TestStation();
        var customerId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var invoice = new InvoicePreviewResponse(
            Guid.NewGuid(), Guid.NewGuid(), 2, station.Id, customerId, tariffId,
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L,
            InvoiceStatusContract.PendingPayment, now, null);
        var api = new PermissionTestServerClient
        {
            StationsResponse = [station],
            PendingInvoicesResponse = [invoice]
        };
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.StationsRead,
            Permissions.BillingWrite
        };
        var workflow = new SessionWorkflowViewModel(api, granted.Contains);

        await workflow.RefreshAsync();

        Assert.True(workflow.CanReadStations);
        Assert.True(workflow.CanCheckout);
        Assert.False(workflow.CanOperateSessions);
        Assert.Empty(workflow.PcStations);
        Assert.Empty(workflow.ActiveSessions);
        Assert.Single(workflow.PendingInvoices);
        Assert.Equal("PC-01", workflow.PendingInvoices[0].StationLabel);

        workflow.SelectedPendingInvoice = workflow.PendingInvoices[0];
        Assert.True(workflow.CheckoutCashCommand.CanExecute(null));
        Assert.False(workflow.StartSessionCommand.CanExecute(null));
        Assert.False(workflow.EndSessionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Station_label_lookup_failure_does_not_block_cashier_invoice_refresh()
    {
        var now = DateTimeOffset.Parse("2026-10-15T10:00:00Z");
        var station = PermissionTestServerClient.TestStation();
        var invoice = new InvoicePreviewResponse(
            Guid.NewGuid(), Guid.NewGuid(), 2, station.Id, Guid.NewGuid(), Guid.NewGuid(),
            "PC-STANDARD", "PC Standard", 60_000L, TimeSpan.FromMinutes(30).Ticks, 30_000L,
            InvoiceStatusContract.PendingPayment, now, null);
        var api = new PermissionTestServerClient
        {
            StationsException = new HttpRequestException("test-only station lookup failure"),
            PendingInvoicesResponse = [invoice]
        };
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.StationsRead,
            Permissions.BillingWrite
        };
        var workflow = new SessionWorkflowViewModel(api, granted.Contains);

        await workflow.RefreshAsync();

        Assert.Empty(workflow.ErrorMessage);
        Assert.Single(workflow.PendingInvoices);
        Assert.Contains(station.Id.ToString("D"), workflow.PendingInvoices[0].StationLabel, StringComparison.Ordinal);
        Assert.True(
            workflow.StatusMessage.Contains("Station codes were unavailable", StringComparison.Ordinal) ||
            workflow.StatusMessage.Contains("کد ایستگاه در دسترس نبود", StringComparison.Ordinal),
            "A station-label fallback warning should be shown when the optional lookup fails.");
        Assert.True(workflow.CanCheckout);
        Assert.False(workflow.CanOperateSessions);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (predicate())
                return;

            await Task.Delay(5);
        }

        Assert.True(predicate(), "The permission state did not reach its expected value.");
    }

    internal sealed class PermissionTestServerClient : IGameNetServerClient
    {
        public int ListCalls { get; private set; }
        public int MutationCalls { get; private set; }
        public int ProbeCalls { get; private set; }
        public Task<IReadOnlyList<StationResponse>>? PendingListResponse { get; set; }
        public IReadOnlyList<StationResponse>? StationsResponse { get; set; }
        public Exception? StationsException { get; set; }
        public IReadOnlyList<TariffResponse>? TariffsResponse { get; set; }
        public IReadOnlyList<SessionResponse>? ActiveSessionsResponse { get; set; }
        public IReadOnlyList<InvoicePreviewResponse>? PendingInvoicesResponse { get; set; }
        public CurrentOperatorResponse CurrentOperator { get; set; } = new(
            Guid.Parse("c31e4c7e-806f-4d42-9f33-9940ac4ab351"),
            "operator",
            "Operator",
            Array.Empty<string>());

        public Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginResponse(
                "test-token",
                "Bearer",
                DateTimeOffset.UtcNow.AddMinutes(5),
                CurrentOperator.UserId,
                request.Username,
                CurrentOperator.DisplayName,
                CurrentOperator.Permissions));

        public Task<CurrentOperatorResponse> GetCurrentOperatorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentOperator);

        public Task<ChangeOwnPasswordResponse> ChangeOwnPasswordAsync(
            ChangeOwnPasswordRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<StationResponse>> GetStationsAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            if (StationsException is not null)
                return Task.FromException<IReadOnlyList<StationResponse>>(StationsException);
            return PendingListResponse ?? Task.FromResult<IReadOnlyList<StationResponse>>(
                StationsResponse ?? new[] { TestStation() });
        }

        public Task<StationResponse> CreateStationAsync(
            CreateStationRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            throw new InvalidOperationException("An unauthorized mutation reached the server client.");
        }

        public Task<StationResponse> RenameStationAsync(
            Guid id,
            RenameStationRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            throw new InvalidOperationException("An unauthorized mutation reached the server client.");
        }

        public Task<StationResponse> BindStationAgentAsync(
            Guid id,
            BindStationAgentRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            throw new InvalidOperationException("An unauthorized mutation reached the server client.");
        }

        public Task<StationResponse> SetStationStatusAsync(
            Guid id,
            SetStationStatusRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            throw new InvalidOperationException("An unauthorized mutation reached the server client.");
        }

        public Task<AgentCommandAcknowledgement> ProbeAgentHealthAsync(
            Guid stationId,
            CancellationToken cancellationToken = default)
        {
            ProbeCalls++;
            throw new InvalidOperationException("A probe request must not run while another command is busy.");
        }

        public Task<IReadOnlyList<TariffResponse>> GetTariffsAsync(
            StationTypeContract? stationType = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TariffResponse>>(TariffsResponse ?? Array.Empty<TariffResponse>());

        public Task<TariffResponse> CreateTariffAsync(
            CreateTariffRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(
            Guid? stationId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionResponse>>(ActiveSessionsResponse ?? Array.Empty<SessionResponse>());

        public Task<SessionResponse> StartSessionAsync(
            StartSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InvoicePreviewResponse> EndSessionAsync(
            Guid id, EndSessionRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<InvoicePreviewResponse>> GetPendingInvoicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoicePreviewResponse>>(PendingInvoicesResponse ?? Array.Empty<InvoicePreviewResponse>());

        public Task<CashCheckoutOutcomeResponse> GetCashCheckoutOutcomeAsync(
            Guid invoiceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CashCheckoutOutcomeResponse>> GetRecentCashCheckoutsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CashCheckoutResponse> CheckoutCashAsync(
            Guid invoiceId, CashCheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ClearSession() { }

        public static StationResponse TestStation() => new(
            Guid.Parse("c31e4c7e-806f-4d42-9f33-9940ac4ab351"),
            "PC-01",
            "Front PC",
            StationTypeContract.Pc,
            StationStatusContract.Available,
            3,
            "agent-front-01",
            true,
            DateTimeOffset.Parse("2026-10-09T10:00:00Z"),
            "1.2.3",
            "Ready");
    }
}
