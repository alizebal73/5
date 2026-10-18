using System.Text.Json;
using GameNet.Shared.Contracts.V1.Api;

namespace GameNet.Server.Infrastructure.Observability;

public sealed class ContractVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1"))
        {
            var version = context.Request.Headers[ApiHeaders.ContractVersion].ToString().Trim();

            if (string.IsNullOrWhiteSpace(version))
            {
                await WriteFailureAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "api.contract_version_required",
                    "The X-GameNet-Contract header is required.");
                return;
            }

            if (!string.Equals(version, ContractVersions.V1, StringComparison.Ordinal))
            {
                await WriteFailureAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "api.contract_version_invalid",
                    "The requested API contract version is not supported.");
                return;
            }
        }

        await next(context);
    }

    private static async Task WriteFailureAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        var correlationId = CorrelationIdMiddleware.GetCurrent(context);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(
                new ApiFailure(
                    new ApiError(code, message),
                    correlationId)));
    }
}
