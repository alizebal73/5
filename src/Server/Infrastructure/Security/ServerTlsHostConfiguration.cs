using GameNet.Shared.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace GameNet.Server.Infrastructure.Security;

public static class ServerTlsHostConfiguration
{
    public static void Configure(WebApplicationBuilder builder)
    {
        AddProgramDataConfiguration(builder);

        var urlsSetting =
            Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ??
            Environment.GetEnvironmentVariable("DOTNET_URLS") ??
            builder.Configuration["urls"] ??
            builder.Configuration["ASPNETCORE_URLS"];

        if (string.IsNullOrWhiteSpace(urlsSetting))
        {
            if (builder.Environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Production Server requires explicit listener URLs in ProgramData server.json or ASPNETCORE_URLS.");
            }

            return;
        }

        var configuredUrls = urlsSetting.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasHttpsEndpoint = false;

        foreach (var value in configuredUrls)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Server URL configuration contains an invalid absolute URL.");

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                hasHttpsEndpoint = true;
                continue;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Server listeners must use HTTP or HTTPS.");

            if (builder.Environment.IsProduction() && !uri.IsLoopback)
            {
                throw new InvalidOperationException(
                    "Production Server refuses non-loopback HTTP listeners. Configure HTTPS and a trusted server certificate.");
            }
        }

        // Ensure URLs sourced from ProgramData take effect in the hosting layer as well.
        builder.WebHost.UseUrls(configuredUrls);

        if (!hasHttpsEndpoint)
            return;

        var thumbprint = builder.Configuration["GameNet:ServerTls:CertificateThumbprint"];
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            if (builder.Environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Production HTTPS requires GameNet:ServerTls:CertificateThumbprint in ProgramData server.json or an environment override.");
            }

            return;
        }

        var certificate = ServerTlsCertificateLoader.LoadFromLocalMachine(thumbprint);
        builder.Services.AddSingleton(certificate);
        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureHttpsDefaults(httpsOptions => httpsOptions.ServerCertificate = certificate));
    }

    private static void AddProgramDataConfiguration(WebApplicationBuilder builder)
    {
        var directory = GameNetRuntimePaths.ConfigurationDirectory;
        if (!Directory.Exists(directory))
            return;

        var fileProvider = new PhysicalFileProvider(directory);
        var source = new JsonConfigurationSource
        {
            FileProvider = fileProvider,
            Path = GameNetRuntimePaths.ServerConfigurationFileName,
            Optional = true,
            ReloadOnChange = false
        };

        // ProgramData supplies installed settings; environment variables and command-line
        // values remain higher-priority operational overrides.
        var environmentIndex = -1;
        for (var index = 0; index < builder.Configuration.Sources.Count; index++)
        {
            if (string.Equals(
                    builder.Configuration.Sources[index].GetType().Name,
                    "EnvironmentVariablesConfigurationSource",
                    StringComparison.Ordinal))
            {
                environmentIndex = index;
                break;
            }
        }

        if (environmentIndex < 0)
            environmentIndex = builder.Configuration.Sources.Count;

        builder.Configuration.Sources.Insert(environmentIndex, source);
        builder.Services.AddSingleton<PhysicalFileProvider>(_ => fileProvider);
    }
}
