using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Server.Modules.Customers.Contracts;
using GameNet.Shared.Primitives;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

namespace GameNet.Server.Infrastructure.Security;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddGameNetAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<GameNetOptions>, IJwtSigningKeySecret>((options, appOptions, signingSecret) =>
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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret.SigningKey)),
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
                    },
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        var actorType = principal?.FindFirst("actor_type")?.Value;
                        var services = context.HttpContext.RequestServices;

                        if (string.Equals(actorType, "Agent", StringComparison.Ordinal))
                        {
                            var deviceId = principal?.FindFirst("device_id")?.Value;
                            var credentialIdText = principal?.FindFirst("credential_id")?.Value;
                            if (string.IsNullOrWhiteSpace(deviceId) ||
                                !Guid.TryParse(credentialIdText, out var credentialId) ||
                                credentialId == Guid.Empty)
                            {
                                context.Fail("AUTH_AGENT_CREDENTIAL_MISSING");
                                return;
                            }

                            var credentials = services.GetRequiredService<IAgentCredentialService>();
                            var isCurrentCredential = await credentials.IsCredentialActiveAsync(
                                credentialId, deviceId, context.HttpContext.RequestAborted);
                            if (!isCurrentCredential)
                                context.Fail("AUTH_AGENT_CREDENTIAL_REVOKED");
                            return;
                        }

                        if (string.Equals(actorType, "Customer", StringComparison.Ordinal))
                        {
                            var customerIdText = principal?.FindFirst("customer_id")?.Value
                                                 ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                                 ?? principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                            var customerJti = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                            if (!Guid.TryParse(customerIdText, out var customerId) || customerId == Guid.Empty ||
                                string.IsNullOrWhiteSpace(customerJti))
                            {
                                context.Fail("AUTH_CUSTOMER_SESSION_MISSING");
                                return;
                            }

                            var authority = services.GetRequiredService<ICustomerLoginAuthority>();
                            if (!await authority.IsLoginUsableAsync(customerId, customerJti, context.HttpContext.RequestAborted))
                                context.Fail("AUTH_CUSTOMER_SESSION_REVOKED");
                            return;
                        }

                        if (!string.Equals(actorType, "Operator", StringComparison.Ordinal))
                        {
                            context.Fail("AUTH_ACTOR_INVALID");
                            return;
                        }

                        var jti = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                                  ?? principal?.FindFirst(ClaimTypes.SerialNumber)?.Value;
                        var userIdText = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                         ?? principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                        if (string.IsNullOrWhiteSpace(jti) || !Guid.TryParse(userIdText, out var userId))
                        {
                            context.Fail("AUTH_SESSION_MISSING");
                            return;
                        }

                        var repository = services.GetRequiredService<IIdentityRepository>();
                        var clock = services.GetRequiredService<IGameClock>();
                        var session = await repository.FindSessionAsync(jti, context.HttpContext.RequestAborted);
                        if (session is null || session.UserId != userId || !session.IsUsable(clock.UtcNow))
                        {
                            context.Fail("AUTH_SESSION_REVOKED");
                            return;
                        }

                        var user = await repository.FindUserByIdAsync(userId, context.HttpContext.RequestAborted);
                        if (user is null || !user.IsActive)
                        {
                            context.Fail("AUTH_IDENTITY_DISABLED");
                            return;
                        }

                        var identity = principal?.Identities.FirstOrDefault(x => x.IsAuthenticated);
                        if (identity is null)
                        {
                            context.Fail("AUTH_IDENTITY_MISSING");
                            return;
                        }

                        foreach (var claim in identity.FindAll("permission").ToArray())
                            identity.RemoveClaim(claim);
                        var permissions = await repository.GetPermissionsAsync(userId, context.HttpContext.RequestAborted);
                        foreach (var permission in permissions)
                            identity.AddClaim(new Claim("permission", permission));
                    }
                };
            });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("customer-password-login", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Operator", policy =>
                policy.RequireAuthenticatedUser().RequireClaim("actor_type", "Operator"));

            options.AddPolicy("Customer", policy => policy.RequireAuthenticatedUser().RequireClaim("actor_type", "Customer"));

            options.AddPolicy("AgentTransport", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim("actor_type", "Agent")
                    .RequireClaim("device_id")
                    .RequireClaim("credential_id"));

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
