using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Shared.Contracts.V1.Security;
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
            .Configure<IOptions<GameNetOptions>>((options, appOptions) =>
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
                        ? new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                        : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrWhiteSpace(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs/agent"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AgentTransport", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim("actor_type", "Agent")
                    .RequireClaim("device_id"));

            foreach (var permission in typeof(Permissions)
                         .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                         .Where(x => x.FieldType == typeof(string))
                         .Select(x => (string)x.GetValue(null)!)
                         .Distinct(StringComparer.Ordinal))
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser()
                        .RequireClaim("permission", permission));
            }
        });

        return services;
    }
}
