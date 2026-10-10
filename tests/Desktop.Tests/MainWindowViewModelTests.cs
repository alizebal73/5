using GameNet.Desktop.Api;
using GameNet.Desktop.Shell;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.System;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Navigation_starts_on_overview_and_exposes_planned_sections()
    {
        var viewModel = new MainWindowViewModel(new FakeServerClient(ReadyHealth()));

        Assert.Equal("overview", viewModel.ActiveSectionKey);
        Assert.True(viewModel.IsOverview);
        Assert.False(viewModel.IsSectionPlaceholder);
        Assert.Equal("Nav.Overview", viewModel.ActiveSectionTitle);
        Assert.Contains(viewModel.NavigationItems, item => item.Key == "stations");
        Assert.Contains(viewModel.NavigationItems, item => item.Key == "customers");
        Assert.Contains(viewModel.NavigationItems, item => item.Key == "sessions");
    }

    [Fact]
    public void Navigation_changes_the_selected_section_without_claiming_it_is_live()
    {
        var viewModel = new MainWindowViewModel(new FakeServerClient(ReadyHealth()));

        viewModel.NavigateTo("stations");

        Assert.Equal("stations", viewModel.ActiveSectionKey);
        Assert.Equal("Nav.Stations", viewModel.ActiveSectionTitle);
        Assert.False(viewModel.IsOverview);
        Assert.True(viewModel.IsSectionPlaceholder);
        Assert.Single(viewModel.NavigationItems, item => item.IsSelected);
    }

    [Fact]
    public async Task Refresh_shows_ready_only_when_server_is_healthy_and_ready()
    {
        var viewModel = new MainWindowViewModel(new FakeServerClient(ReadyHealth()));

        await viewModel.RefreshServerStatusAsync();

        Assert.Equal("ready", viewModel.ServerStatusKind);
        Assert.Equal("Status.Ready", viewModel.ServerStatusText);
        Assert.Equal("5.0.0-test", viewModel.ServerVersionText);
        Assert.NotEqual("Common.NotChecked", viewModel.LastCheckedText);
    }

    [Fact]
    public async Task Refresh_uses_a_stable_unreachable_state_without_exposing_exception_details()
    {
        var viewModel = new MainWindowViewModel(new FakeServerClient(
            new InvalidOperationException("example-secret-or-connection-detail")));

        await viewModel.RefreshServerStatusAsync();

        Assert.Equal("offline", viewModel.ServerStatusKind);
        Assert.Equal("Status.Unreachable", viewModel.ServerStatusText);
        Assert.Equal("Common.Unknown", viewModel.ServerVersionText);
        Assert.DoesNotContain("example-secret-or-connection-detail", viewModel.ServerStatusText);
    }

    private static ApiEnvelope<HealthResponse> ReadyHealth()
        => new(
            new HealthResponse(
                "GameNet 5 Server",
                "5.0.0-test",
                HealthStatuses.Healthy,
                HealthStatuses.Ready,
                "desktop-test-correlation"),
            "desktop-test-correlation");

    private sealed class FakeServerClient : IGameNetServerClient
    {
        private readonly ApiEnvelope<HealthResponse>? response;
        private readonly Exception? exception;

        public FakeServerClient(ApiEnvelope<HealthResponse> response)
            => this.response = response;

        public FakeServerClient(Exception exception)
            => this.exception = exception;

        public Task<ApiEnvelope<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            if (exception is not null)
                return Task.FromException<ApiEnvelope<HealthResponse>>(exception);

            return Task.FromResult(response!);
        }
    }
}
