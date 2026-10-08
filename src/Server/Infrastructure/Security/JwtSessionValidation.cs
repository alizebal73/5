using System.IdentityModel.Tokens.Jwt;
using GameNet.Server.Modules.Identity.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GameNet.Server.Infrastructure.Security;

public static class JwtSessionValidation
{
    public static void Configure(
        JwtBearerOptions options,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider)
    {
        options.Events ??= new JwtBearerEvents();
        options.Events.OnTokenValidated = async context =>
        {
            var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrWhiteSpace(jti))
            {
                context.Fail("AUTH_SESSION_MISSING");
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIdentityRepository>();
            var session = await repository.FindSessionAsync(
                jti,
                context.HttpContext.RequestAborted);

            if (session is null || !session.IsUsable(timeProvider.GetUtcNow()))
                context.Fail("AUTH_SESSION_REVOKED");
        };
    }
}
