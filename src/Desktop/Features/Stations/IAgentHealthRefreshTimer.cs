using System;

namespace GameNet.Desktop.Features.Stations;

/// <summary>
/// A UI-thread timer boundary so Agent health refresh lifecycle can be tested without
/// relying on a running WPF message loop.
/// </summary>
public interface IAgentHealthRefreshTimer : IDisposable
{
    event EventHandler? Tick;

    TimeSpan Interval { get; set; }

    bool IsEnabled { get; }

    void Start();

    void Stop();
}
