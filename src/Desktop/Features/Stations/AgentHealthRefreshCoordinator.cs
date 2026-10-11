using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GameNet.Desktop.Features.Stations;

/// <summary>
/// Owns the Agent-health refresh lifecycle independently of the WPF shell. A session token
/// cancels an in-flight refresh at logout or window shutdown. The timer runs on the UI
/// dispatcher, and a re-entry guard prevents overlapping refreshes.
/// </summary>
public sealed class AgentHealthRefreshCoordinator : IDisposable
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly object sync = new();
    private readonly INotifyPropertyChanged sessionState;
    private readonly Func<bool> isAuthenticated;
    private readonly Func<bool> isBusy;
    private readonly Func<bool> isStationBoardBusy;
    private readonly Func<CancellationToken, Task> refreshHealthAsync;
    private readonly IAgentHealthRefreshTimer timer;

    private CancellationTokenSource? activeSessionCancellation;
    private CancellationTokenSource? activeRefreshCancellation;
    private bool refreshInProgress;
    private bool disposed;

    public AgentHealthRefreshCoordinator(
        INotifyPropertyChanged sessionState,
        Func<bool> isAuthenticated,
        Func<bool> isBusy,
        Func<bool> isStationBoardBusy,
        Func<CancellationToken, Task> refreshHealthAsync,
        IAgentHealthRefreshTimer timer)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        ArgumentNullException.ThrowIfNull(isAuthenticated);
        ArgumentNullException.ThrowIfNull(isBusy);
        ArgumentNullException.ThrowIfNull(isStationBoardBusy);
        ArgumentNullException.ThrowIfNull(refreshHealthAsync);
        ArgumentNullException.ThrowIfNull(timer);

        this.sessionState = sessionState;
        this.isAuthenticated = isAuthenticated;
        this.isBusy = isBusy;
        this.isStationBoardBusy = isStationBoardBusy;
        this.refreshHealthAsync = refreshHealthAsync;
        this.timer = timer;

        timer.Interval = DefaultRefreshInterval;
        timer.Tick += OnTimerTick;
        sessionState.PropertyChanged += OnSessionStatePropertyChanged;
        SynchronizeTimerWithSession();
    }

    public bool IsRunning => timer.IsEnabled;

    public bool IsRefreshInProgress
    {
        get
        {
            lock (sync)
                return refreshInProgress;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
        }

        sessionState.PropertyChanged -= OnSessionStatePropertyChanged;
        timer.Tick -= OnTimerTick;
        StopSession();
        timer.Dispose();
    }

    private void OnSessionStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null ||
            string.Equals(e.PropertyName, "IsAuthenticated", StringComparison.Ordinal))
        {
            SynchronizeTimerWithSession();
        }
    }

    private void SynchronizeTimerWithSession()
    {
        bool shouldRun;
        lock (sync)
        {
            if (disposed)
                return;

            shouldRun = isAuthenticated();
            if (shouldRun && activeSessionCancellation is { IsCancellationRequested: false })
            {
                if (!timer.IsEnabled)
                    timer.Start();
                return;
            }
        }

        if (shouldRun)
            StartSession();
        else
            StopSession();
    }

    private void StartSession()
    {
        lock (sync)
        {
            if (disposed || !isAuthenticated())
                return;

            if (activeSessionCancellation is { IsCancellationRequested: false })
            {
                if (!timer.IsEnabled)
                    timer.Start();
                return;
            }

            activeSessionCancellation = new CancellationTokenSource();
            timer.Start();
        }
    }

    private void StopSession()
    {
        CancellationTokenSource? previous;
        lock (sync)
        {
            timer.Stop();
            previous = activeSessionCancellation;
            activeSessionCancellation = null;
            if (previous is null)
                return;

            previous.Cancel();
            if (!ReferenceEquals(activeRefreshCancellation, previous))
                previous.Dispose();
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        CancellationTokenSource? session;
        lock (sync)
        {
            if (disposed ||
                activeSessionCancellation is not { IsCancellationRequested: false } current ||
                !isAuthenticated() ||
                isBusy() ||
                isStationBoardBusy() ||
                refreshInProgress)
            {
                return;
            }

            refreshInProgress = true;
            activeRefreshCancellation = current;
            session = current;
        }

        try
        {
            await refreshHealthAsync(session.Token);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
            // Logout/shutdown deliberately cancels this request; it is not a user-visible error.
        }
        catch (Exception exception)
        {
            // StationBoardViewModel maps expected API failures to UI state. Only unexpected
            // failures reach here; log the type, never request/response content or credentials.
            Trace.TraceError("Agent health refresh failed ({0}).", exception.GetType().Name);
        }
        finally
        {
            lock (sync)
            {
                activeRefreshCancellation = null;
                refreshInProgress = false;
                if (!ReferenceEquals(activeSessionCancellation, session))
                    session.Dispose();
            }
        }
    }
}
