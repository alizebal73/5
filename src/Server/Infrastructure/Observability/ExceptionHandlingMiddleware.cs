using System.Text.Json;
using GameNet.Shared.Contracts.V1.Api;

namespace GameNet.Server.Infrastructure.Observability;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var id = CorrelationIdMiddleware.GetCurrent(context);
            logger.LogError(ex, "Unhandled request failure. CorrelationId={CorrelationId}", id);
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiFailure(new ApiError("server.unhandled", "An unexpected server error occurred."), id)));
        }
    }
}
