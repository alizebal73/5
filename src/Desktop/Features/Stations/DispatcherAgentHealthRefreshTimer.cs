using System;
using System.Windows.Threading;

namespace GameNet.Desktop.Features.Stations;

/// <summary>
/// Adapts WPF's dispatcher timer to the testable health-refresh timer contract.
/// Tick handlers execute on the UI dispatcher, preserving WPF binding thread affinity.
/// </summary>
public sealed class DispatcherAgentHealthRefreshTimer : IAgentHealthRefreshTimer
{
    private readonly DispatcherTimer timer;
    private bool disposed;

    public DispatcherAgentHealthRefreshTimer()
    {
        timer = new DispatcherTimer();
        timer.Tick += OnTimerTick;
    }

    public event EventHandler? Tick;

    public TimeSpan Interval
    {
        get => timer.Interval;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value));
            timer.Interval = value;
        }
    }

    public bool IsEnabled => timer.IsEnabled;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        timer.Start();
    }

    public void Stop()
    {
        if (disposed)
            return;

        timer.Stop();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
        Tick = null;
    }

    private void OnTimerTick(object? sender, EventArgs e) => Tick?.Invoke(this, e);
}
