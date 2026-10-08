using GameNet.Desktop.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameNet.Desktop.Infrastructure;

public static class DesktopHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
