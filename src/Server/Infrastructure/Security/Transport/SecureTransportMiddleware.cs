using System.Net;
using System.Text.Json;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Shared.Contracts.V1.Api;

namespace GameNet.Server.Infrastructure.Security.Transport;

public sealed class SecureTransportMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsSensitivePath(context.Request.Path) || IsSecureOrLoopback(context))
        {
            await next(context);
            return;
        }

        var correlationId = CorrelationIdMiddleware.GetCurrent(context);
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(
            new ApiFailure(new ApiError("security.https_required", "HTTPS is required for this endpoint."), correlationId)));
    }

    private static bool IsSecureOrLoopback(HttpContext context)
    {
        if (context.Request.IsHttps) return true;
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }

    private static bool IsSensitivePath(PathString path) =>
        path.StartsWithSegments("/api/v1/auth") ||
        path.StartsWithSegments("/api/v1/bootstrap/admin") ||
        path.StartsWithSegments("/api/v1/agent") ||
        path.StartsWithSegments("/hubs/agent");
}
