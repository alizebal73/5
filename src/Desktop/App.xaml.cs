using System.Globalization;
using System.Windows;
using GameNet.Desktop.Infrastructure;
using GameNet.Desktop.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameNet.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = DesktopHost.Build();
        await _host.StartAsync();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fa-IR");
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
