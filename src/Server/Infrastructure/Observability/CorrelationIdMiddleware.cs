using System.Text.Json;
using GameNet.Shared.Contracts.V1.Api;

namespace GameNet.Server.Infrastructure.Observability;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private static readonly object Key = new();
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[ApiHeaders.CorrelationId].ToString().Trim();
        if (supplied.Length > 128)
        {
            var invalidId = Guid.NewGuid().ToString("N");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            context.Response.Headers[ApiHeaders.CorrelationId] = invalidId;
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(
                    new ApiFailure(
                        new ApiError("api.correlation_id_invalid", "The correlation ID must be at most 128 characters."),
                        invalidId)));
            return;
        }

        var id = string.IsNullOrWhiteSpace(supplied) ? Guid.NewGuid().ToString("N") : supplied;
        context.Items[Key] = id;
        context.Response.Headers[ApiHeaders.CorrelationId] = id;
        await next(context);
    }
    public static string GetCurrent(HttpContext context) => context.Items.TryGetValue(Key, out var value) && value is string id ? id : context.TraceIdentifier;
}
