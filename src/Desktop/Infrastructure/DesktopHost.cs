using System.Net.Http.Headers;
using GameNet.Desktop.Api;
using GameNet.Desktop.Shell;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GameNet.Desktop.Infrastructure;

public static class DesktopHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddOptions<ServerConnectionOptions>()
            .BindConfiguration(ServerConnectionOptions.SectionName)
            .Validate(options => ServerEndpointAddress.IsAllowed(options.BaseUrl), "Server BaseUrl must be a root HTTPS origin, except for loopback development.")
            .ValidateOnStart();

        builder.Services.AddHttpClient<IGameNetServerClient, GameNetServerClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ServerConnectionOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation(ApiHeaders.ContractVersion, ContractVersions.V1);
        });

        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
