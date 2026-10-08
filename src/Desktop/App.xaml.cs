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

        var culture = DesktopCulture.Resolve(Environment.GetEnvironmentVariable("GAMENET_UI_CULTURE"));
        DesktopCulture.Apply(culture);
        ReplaceResourceDictionary(DesktopCulture.GetResourceDictionaryName(culture));

        _host = DesktopHost.Build();
        await _host.StartAsync();

        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.FlowDirection = culture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        MainWindow.Show();
    }

    private void ReplaceResourceDictionary(string resourceName)
    {
        var dictionaries = Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Resources/{resourceName}", UriKind.Relative)
        });
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
