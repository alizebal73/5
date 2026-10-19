using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Stations.Contracts;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Infrastructure.Security;

/// <summary>
/// Direct Client entry is submitted through the authenticated PC Agent. The Agent device
/// identity—not a StationId supplied by the Client—selects the Station.
/// </summary>
public static class AgentCustomerSessionEndpoints
{
    public static void MapAgentCustomerSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agent/customer-session")
            .RequireAuthorization("AgentTransport");

        group.MapPost("/start", async (
            CustomerClientEntryRequest request,
            IStationSessionAccess stationAccess,
            ICustomerAuthenticationService customerAuthentication,
            ISessionService sessions,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var deviceId = context.User.FindFirstValue("device_id");
            if (!string.Equals(context.User.FindFirstValue("actor_type"), "Agent", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(deviceId))
                return Failure("auth.agent_identity_missing", "Authenticated Agent identity is missing.", StatusCodes.Status401Unauthorized, context);

            var keyFromHeader = context.Request.Headers[ApiHeaders.IdempotencyKey].ToString().Trim();
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200 ||
                string.IsNullOrWhiteSpace(keyFromHeader) ||
                !string.Equals(request.IdempotencyKey.Trim(), keyFromHeader, StringComparison.Ordinal))
                return Failure("idempotency.required", "A matching Idempotency-Key is required.", StatusCodes.Status400BadRequest, context);

            var stationId = await stationAccess.FindAssignedPcStationIdForAgentAsync(deviceId, cancellationToken);
            if (stationId is null)
                return Failure("agent.customer_station_unbound",
                    "This Agent is not bound to exactly one PC Station.", StatusCodes.Status409Conflict, context);

            var correlationId = CorrelationIdMiddleware.GetCurrent(context);
            var login = await customerAuthentication.LoginAsync(
                new CustomerLoginCommand(request.Username, request.Password, correlationId, "CustomerClient"),
                cancellationToken);
            if (!login.IsSuccess)
                return Failure("customer_auth.invalid_credentials",
                    "Username or password is incorrect.", StatusCodes.Status401Unauthorized, context);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Value.AccessToken);
            var jti = jwt.Id;
            if (string.IsNullOrWhiteSpace(jti))
                return Failure("customer_auth.invalid_credentials",
                    "Customer login session could not be established.", StatusCodes.Status401Unauthorized, context);

            var actor = new SessionActorContext(
                Guid.Empty,
                correlationId,
                "CustomerClient",
                new HashSet<string>(StringComparer.Ordinal) { Permissions.SessionsOperate },
                "Customer",
                login.Value.CustomerId);

            var started = await sessions.StartPrepaidAsync(
                new StartPrepaidSessionCommand(stationId.Value, login.Value.CustomerId, keyFromHeader, actor),
                cancellationToken);
            if (!started.IsSuccess)
            {
                // Do not leave a usable Customer token behind when paid entry is denied.
                try
                {
                    await customerAuthentication.LogoutAsync(
                        login.Value.CustomerId, jti, correlationId, CancellationToken.None);
                }
                catch
                {
                    // The Session result remains denied; no customer token is returned to the Client.
                }

                var (status, message) = MapSessionFailure(started.Error);
                return Failure(started.Error.Code, message, status, context);
            }

            // This Agent-mediated Client flow needs only the authenticated Session result.
            // Revoke the transient credential token so it is not left usable after entry.
            try
            {
                await customerAuthentication.LogoutAsync(
                    login.Value.CustomerId, jti, correlationId, CancellationToken.None);
            }
            catch
            {
                // The Session is valid even if transient-token cleanup needs later reconciliation.
            }

            var response = new CustomerClientEntryResponse(
                true,
                login.Value.CustomerId,
                login.Value.DisplayName,
                started.Value.Id,
                started.Value.PrepaidReservedAmountToman,
                started.Value.PrepaidBudgetTicks,
                null,
                null);
            return Results.Ok(new ApiEnvelope<CustomerClientEntryResponse>(
                response, CorrelationIdMiddleware.GetCurrent(context)));
        });

    }

    private static (int Status, string Message) MapSessionFailure(Error error) => error.Code switch
    {
        "sessions.station_not_found" or "sessions.default_pc_tariff_not_found" =>
            (StatusCodes.Status404NotFound, error.Message),
        "sessions.station_unavailable" or "sessions.agent_unavailable" or
        "sessions.station_has_active_session" or "sessions.station_pending_settlement" or
        "sessions.customer_has_active_session" or "sessions.customer_blocked" or
        "wallet.insufficient_balance" or "wallet.prepaid_reservation_active" or
        "wallet.customer_inactive" or "wallet.customer_not_found" or
        "wallet.prepaid_budget_exceeded" or "sessions.prepaid_budget_exceeded" =>
            (StatusCodes.Status409Conflict, error.Message),
        "sessions.prepaid_customer_required" or "idempotency.required" =>
            (StatusCodes.Status400BadRequest, error.Message),
        _ => (StatusCodes.Status500InternalServerError, "Customer entry could not be completed.")
    };

    private static IResult Failure(string code, string message, int status, HttpContext context) =>
        Results.Json(
            new ApiFailure(new ApiError(code, message), CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
}
