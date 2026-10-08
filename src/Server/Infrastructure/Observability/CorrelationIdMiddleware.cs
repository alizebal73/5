using GameNet.Shared.Contracts.V1.Api;

namespace GameNet.Server.Infrastructure.Observability;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private static readonly object Key = new();
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[ApiHeaders.CorrelationId].ToString();
        var id = string.IsNullOrWhiteSpace(supplied) ? Guid.NewGuid().ToString("N") : supplied.Trim();
        context.Items[Key] = id;
        context.Response.Headers[ApiHeaders.CorrelationId] = id;
        await next(context);
    }
    public static string GetCurrent(HttpContext context) => context.Items.TryGetValue(Key, out var value) && value is string id ? id : context.TraceIdentifier;
}
