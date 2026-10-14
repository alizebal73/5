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
            // Never log the exception object: its Message and inner exceptions can contain
            // passwords, tokens, or connection strings. Keep only safe correlation,
            // exception-type and stack information for incident triage.
            logger.LogError(
                "Unhandled request failure. CorrelationId={CorrelationId}; ExceptionType={ExceptionType}; StackTrace={StackTrace}",
                id,
                ex.GetType().FullName,
                ex.StackTrace);
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiFailure(new ApiError("server.unhandled", "An unexpected server error occurred."), id)));
        }
    }
}
