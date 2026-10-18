using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Security;

namespace GameNet.Server.Infrastructure.Security;

public static class AgentEnrollmentRoutes
{
    public static void MapAgentEnrollmentRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/agent/enrollment-tokens",
            async (
                AgentEnrollmentIssueRequest request,
                IAgentEnrollmentService enrollment,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!TryGetOperatorId(context, out var operatorId))
                    return Results.Unauthorized();

                try
                {
                    var issued = await enrollment.IssueAsync(new AgentEnrollmentIssueCommand(
                        request.DeviceId,
                        operatorId,
                        CorrelationIdMiddleware.GetCurrent(context),
                        "Desktop"), cancellationToken);
                    return Results.Ok(issued);
                }
                catch (AgentCredentialException exception)
                {
                    return Failure(context, exception);
                }
            })
            .RequireAuthorization("Operator")
            .RequireAuthorization(Permissions.AgentEnrollmentManage);

        app.MapPost("/api/v1/agent/enrollment/recover",
            async (
                AgentEnrollmentRecoverRequest request,
                IAgentEnrollmentService enrollment,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!TryGetOperatorId(context, out var operatorId))
                    return Results.Unauthorized();

                try
                {
                    var issued = await enrollment.RecoverAsync(new AgentEnrollmentRecoverCommand(
                        request.DeviceId,
                        request.Reason,
                        operatorId,
                        CorrelationIdMiddleware.GetCurrent(context),
                        "Desktop"), cancellationToken);
                    return Results.Ok(issued);
                }
                catch (AgentCredentialException exception)
                {
                    return Failure(context, exception);
                }
            })
            .RequireAuthorization("Operator")
            .RequireAuthorization(Permissions.AgentEnrollmentManage);

        app.MapPost("/api/v1/agent/enrollment-tokens/{tokenId:guid}/revoke",
            async (
                Guid tokenId,
                AgentEnrollmentRevokeRequest request,
                IAgentEnrollmentService enrollment,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!TryGetOperatorId(context, out var operatorId))
                    return Results.Unauthorized();

                try
                {
                    var revoked = await enrollment.RevokeAsync(
                        tokenId,
                        request.Reason,
                        operatorId,
                        CorrelationIdMiddleware.GetCurrent(context),
                        "Desktop",
                        cancellationToken);
                    return revoked ? Results.Ok(new { revoked = true }) : Results.NotFound();
                }
                catch (AgentCredentialException exception)
                {
                    return Failure(context, exception);
                }
            })
            .RequireAuthorization("Operator")
            .RequireAuthorization(Permissions.AgentEnrollmentManage);

        app.MapPost("/api/v1/agent/enrollment/redeem",
            async (
                AgentEnrollmentRedeemRequest request,
                IAgentEnrollmentService enrollment,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                try
                {
                    var credential = await enrollment.RedeemAsync(
                        request,
                        CorrelationIdMiddleware.GetCurrent(context),
                        cancellationToken);
                    return credential is null ? Results.Unauthorized() : Results.Ok(credential);
                }
                catch (AgentCredentialException exception)
                {
                    return Failure(context, exception);
                }
            })
            .AllowAnonymous();
    }

    private static bool TryGetOperatorId(HttpContext context, out Guid operatorId)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out operatorId);
    }

    private static IResult Failure(HttpContext context, AgentCredentialException exception)
    {
        var (status, message) = exception.Code switch
        {
            "auth.identity_missing" => (StatusCodes.Status401Unauthorized, "The authenticated operator identity is missing."),
            "agent.device_id_invalid" => (StatusCodes.Status400BadRequest, "DeviceId is invalid."),
            "agent.enrollment_reason_invalid" => (StatusCodes.Status400BadRequest, "A revocation reason of at most 1000 characters is required."),
            "agent.enrollment_request_invalid" => (StatusCodes.Status400BadRequest, "The enrollment request is invalid."),
            "agent.credential_exists" => (StatusCodes.Status409Conflict, "An active credential already exists for this device."),
            "agent.enrollment_token_pending" => (StatusCodes.Status409Conflict, "An unexpired enrollment token already exists for this device."),
            "agent.enrollment_conflict" => (StatusCodes.Status409Conflict, "The enrollment operation conflicted with another request."),
            "agent.enrollment_recovery_not_safe" => (StatusCodes.Status409Conflict, "Enrollment recovery is not safe for the current Agent credential state."),
            _ => (StatusCodes.Status400BadRequest, "The enrollment operation was rejected.")
        };

        return Results.Json(
            new ApiFailure(new ApiError(exception.Code, message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
