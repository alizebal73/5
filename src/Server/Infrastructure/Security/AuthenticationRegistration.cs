using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameNet.Server.Infrastructure.Security;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddGameNetAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>()
            .Configure<IOptions<GameNetOptions>, IServiceScopeFactory, TimeProvider>(
                (options, appOptions, scopeFactory, timeProvider) =>
                {
                    var settings = appOptions.Value.Authentication;
                    options.RequireHttpsMetadata = true;
                    options.SaveToken = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = settings.Issuer,
                        ValidateAudience = true,
                        ValidAudience = settings.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = string.IsNullOrWhiteSpace(settings.SigningKey)
                            ? new SymmetricSecurityKey(
                                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                            : new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(settings.SigningKey)),
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };

                    JwtSessionValidation.Configure(
                        options,
                        scopeFactory,
                        timeProvider);
                });

        services.AddAuthorization();
        return services;
    }
}
