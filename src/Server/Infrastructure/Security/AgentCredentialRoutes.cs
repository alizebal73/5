using GameNet.Server.Infrastructure.Observability;
using System.Security.Cryptography;
using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Security;
using Microsoft.Extensions.Options;

namespace GameNet.Server.Infrastructure.Security;

public static class AgentCredentialRoutes
{
    private const string ProvisioningHeader = "X-GameNet-Agent-Provisioning-Key";

    public static void MapAgentCredentialRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agent");

        group.MapPost("/auth/token",
            async (
                AgentTokenRequest request,
                IAgentCredentialService credentials,
                IAgentAccessTokenIssuer tokenIssuer,
                IOptions<GameNetOptions> options,
                HttpResponse response,
                CancellationToken cancellationToken) =>
            {
                if (!options.Value.Authentication.Enabled)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                if (!await credentials.AuthenticateAsync(request.DeviceId, request.Secret, cancellationToken))
                    return Results.Unauthorized();

                var token = tokenIssuer.Issue(request.DeviceId);
                response.Headers.CacheControl = "no-store";
                return Results.Ok(token);
            }).AllowAnonymous();

        group.MapPost("/credentials/provision",
            async (
                HttpContext context,
                AgentCredentialProvisionRequest request,
                IAgentCredentialService credentials,
                IAgentProvisioningKeySecret provisioningSecret,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var denied = ValidateProvisioningKey(context, provisioningSecret.ProvisioningKey);
                if (denied is not null) return denied;

                try
                {
                    var issued = await credentials.ProvisionAsync(request, cancellationToken);
                    return Results.Ok(issued);
                }
                catch (AgentCredentialException ex)
                {
                    return WriteCredentialFailure(context, ex);
                }
            }).AllowAnonymous();

        group.MapPost("/credentials/rotate",
            async (
                HttpContext context,
                AgentCredentialRotateRequest request,
                IAgentCredentialService credentials,
                IAgentProvisioningKeySecret provisioningSecret,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var denied = ValidateProvisioningKey(context, provisioningSecret.ProvisioningKey);
                if (denied is not null) return denied;

                try
                {
                    var issued = await credentials.RotateAsync(request, cancellationToken);
                    return Results.Ok(issued);
                }
                catch (AgentCredentialException ex)
                {
                    return WriteCredentialFailure(context, ex);
                }
            }).AllowAnonymous();

        group.MapPost("/credentials/revoke",
            async (
                HttpContext context,
                AgentCredentialRevokeRequest request,
                IAgentCredentialService credentials,
                IAgentProvisioningKeySecret provisioningSecret,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var denied = ValidateProvisioningKey(context, provisioningSecret.ProvisioningKey);
                if (denied is not null) return denied;

                try
                {
                    var revoked = await credentials.RevokeAsync(request, cancellationToken);
                    return Results.Ok(new { revoked });
                }
                catch (AgentCredentialException ex)
                {
                    return WriteCredentialFailure(context, ex);
                }
            }).AllowAnonymous();
    }

    private static IResult WriteCredentialFailure(HttpContext context, AgentCredentialException exception) =>
        Results.Conflict(new ApiFailure(
            new ApiError(exception.Code, "The Agent credential operation was rejected."),
            CorrelationIdMiddleware.GetCurrent(context)));

    private static IResult? ValidateProvisioningKey(HttpContext context, string expectedKey)
    {
        var supplied = context.Request.Headers[ProvisioningHeader].ToString();
        if (string.IsNullOrWhiteSpace(supplied))
            return Results.Unauthorized();

        byte[]? expectedBytes = null;
        byte[]? suppliedBytes = null;
        try
        {
            expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            suppliedBytes = Encoding.UTF8.GetBytes(supplied);

            return expectedBytes.Length == suppliedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes)
                ? null
                : Results.Unauthorized();
        }
        finally
        {
            if (expectedBytes is not null)
                CryptographicOperations.ZeroMemory(expectedBytes);
            if (suppliedBytes is not null)
                CryptographicOperations.ZeroMemory(suppliedBytes);
        }
    }
}
