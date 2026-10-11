using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using GameNet.Desktop.Features.Stations;
using Xunit;

namespace GameNet.Desktop.Tests;

public sealed class AgentHealthRefreshCoordinatorTests
{
    [Fact]
    public void Timer_starts_only_when_authenticated_and_stops_on_logout()
    {
        var state = new FakeSessionState();
        var timer = new FakeTimer();
        var calls = 0;

        using var coordinator = CreateCoordinator(
            state,
            timer,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        Assert.Equal(TimeSpan.FromSeconds(10), timer.Interval);
        Assert.False(timer.IsEnabled);

        state.SetAuthenticated(true);
        Assert.True(timer.IsEnabled);

        timer.Pulse();
        Assert.Equal(1, calls);

        state.SetAuthenticated(false);
        Assert.False(timer.IsEnabled);
        timer.Pulse();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Timer_skips_ticks_while_login_or_station_board_is_busy()
    {
        var state = new FakeSessionState { IsBusy = true };
        var timer = new FakeTimer();
        var calls = 0;

        using var coordinator = CreateCoordinator(
            state,
            timer,
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        state.SetAuthenticated(true);
        timer.Pulse();
        Assert.Equal(0, calls);

        state.IsBusy = false;
        state.IsStationBoardBusy = true;
        timer.Pulse();
        Assert.Equal(0, calls);

        state.IsStationBoardBusy = false;
        timer.Pulse();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Timer_does_not_start_an_overlapping_refresh()
    {
        var state = new FakeSessionState();
        var timer = new FakeTimer();
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRefreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        using var coordinator = CreateCoordinator(
            state,
            timer,
            async _ =>
            {
                Interlocked.Increment(ref calls);
                firstRefreshStarted.TrySetResult();
                await releaseRefresh.Task;
            });

        state.SetAuthenticated(true);
        timer.Pulse();
        await firstRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        timer.Pulse();
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.True(coordinator.IsRefreshInProgress);

        releaseRefresh.TrySetResult();
        await WaitUntilAsync(() => !coordinator.IsRefreshInProgress);

        timer.Pulse();
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Logout_cancels_an_in_flight_health_request()
    {
        var state = new FakeSessionState();
        var timer = new FakeTimer();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var coordinator = CreateCoordinator(
            state,
            timer,
            async token =>
            {
                started.TrySetResult(token);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }
            });

        state.SetAuthenticated(true);
        timer.Pulse();
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        state.SetAuthenticated(false);

        Assert.True(token.IsCancellationRequested);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !coordinator.IsRefreshInProgress);
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public async Task Dispose_cancels_the_active_session_and_detaches_the_timer()
    {
        var state = new FakeSessionState();
        var timer = new FakeTimer();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var coordinator = CreateCoordinator(
            state,
            timer,
            async token =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult(token);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }
            });

        state.SetAuthenticated(true);
        timer.Pulse();
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.Dispose();

        Assert.True(token.IsCancellationRequested);
        Assert.False(timer.IsEnabled);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !coordinator.IsRefreshInProgress);

        timer.Pulse();
        state.SetAuthenticated(true);
        Assert.Equal(1, Volatile.Read(ref calls));
        timer.Dispose();
    }

    private static AgentHealthRefreshCoordinator CreateCoordinator(
        FakeSessionState state,
        FakeTimer timer,
        Func<CancellationToken, Task> refresh) =>
        new(
            state,
            () => state.IsAuthenticated,
            () => state.IsBusy,
            () => state.IsStationBoardBusy,
            refresh,
            timer);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(5);
        }

        Assert.True(condition(), "The refresh lifecycle did not reach the expected state.");
    }

    private sealed class FakeSessionState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsAuthenticated { get; private set; }
        public bool IsBusy { get; set; }
        public bool IsStationBoardBusy { get; set; }

        public void SetAuthenticated(bool value)
        {
            if (IsAuthenticated == value)
                return;

            IsAuthenticated = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAuthenticated)));
        }
    }

    private sealed class FakeTimer : IAgentHealthRefreshTimer
    {
        private bool disposed;

        public event EventHandler? Tick;

        public TimeSpan Interval { get; set; }

        public bool IsEnabled { get; private set; }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            IsEnabled = true;
        }

        public void Stop() => IsEnabled = false;

        public void Pulse()
        {
            if (!disposed && IsEnabled)
                Tick?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            Stop();
            Tick = null;
        }
    }
}
