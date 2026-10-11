using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Api;
using GameNet.Desktop.Features.Identity;
using GameNet.Desktop.Features.Stations;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Contracts.V1.Protocol;
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

    private sealed class PermissionTestServerClient : IGameNetServerClient
    {
        public int ListCalls { get; private set; }
        public int MutationCalls { get; private set; }
        public Task<IReadOnlyList<StationResponse>>? PendingListResponse { get; set; }
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
            return PendingListResponse ?? Task.FromResult<IReadOnlyList<StationResponse>>([TestStation()]);
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
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
