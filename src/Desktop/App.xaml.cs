using System.Globalization;
using System.Windows;
using GameNet.Desktop.Api;
using GameNet.Desktop.Infrastructure;
using GameNet.Desktop.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GameNet.Desktop;

public partial class App : Application
{
    private IHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var culture = DesktopCulture.Resolve(Environment.GetEnvironmentVariable("GAMENET_UI_CULTURE"));
            DesktopCulture.Apply(culture);
            ReplaceResourceDictionary(DesktopCulture.GetResourceDictionaryName(culture));

            host = DesktopHost.Build();
            await host.StartAsync();

            MainWindow = host.Services.GetRequiredService<MainWindow>();

            if (string.Equals(
                    Environment.GetEnvironmentVariable("GAMENET_DESKTOP_SMOKE"),
                    "1",
                    StringComparison.Ordinal))
            {
                var client = host.Services.GetRequiredService<IGameNetServerClient>();
                var health = await client.GetHealthAsync();
                if (!string.Equals(health.Data.Readiness, HealthStatuses.Ready, StringComparison.Ordinal))
                    throw new InvalidOperationException("DESKTOP_SERVER_NOT_READY");

                Shutdown(0);
                return;
            }

            MainWindow.FlowDirection = culture.TextInfo.IsRightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "GameNet 5",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
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
        if (host is not null)
        {
            await host.StopAsync(TimeSpan.FromSeconds(5));
            host.Dispose();
        }

        base.OnExit(e);
    }
}
