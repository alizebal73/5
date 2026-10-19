using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Customers;
using GameNet.Shared.Contracts.V1.Protocol;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Customers.Api;

public static class CustomerAuthenticationEndpoints
{
    public static void MapCustomerAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var operatorGroup = endpoints.MapGroup("/api/v1/customers").RequireAuthorization("Operator");
        operatorGroup.MapPost("/{customerId:guid}/credentials", async (
            Guid customerId, SetCustomerCredentialsRequest request, ICustomerAuthenticationService service,
            HttpContext context, CancellationToken ct) =>
        {
            var idText = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (!Guid.TryParse(idText, out var operatorId) || operatorId == Guid.Empty)
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var permissions = context.User.Claims.Where(x => string.Equals(x.Type, "permission", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
            var result = await service.SetCredentialsAsync(new SetCustomerCredentialsCommand(customerId, request.Username, request.Password,
                request.ExpectedVersion, context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(),
                new CustomerPasswordActor(operatorId, CorrelationIdMiddleware.GetCurrent(context), "Desktop", permissions)), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<SetCustomerCredentialsResponse>(new SetCustomerCredentialsResponse(
                    result.Value.CustomerId, result.Value.Username, result.Value.CustomerVersion, result.Value.ChangedAtUtc),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization(Permissions.CustomersCredentialsManage);

        var login = endpoints.MapGroup("/api/v1/customer-auth");
        login.MapPost("/login", async (CustomerLoginRequest request, ICustomerAuthenticationService service,
            HttpContext context, CancellationToken ct) =>
        {
            var result = await service.LoginAsync(new CustomerLoginCommand(request.Username, request.Password,
                CorrelationIdMiddleware.GetCurrent(context), "CustomerClient"), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerLoginResponse>(new CustomerLoginResponse(
                    result.Value.AccessToken, "Bearer", result.Value.ExpiresAtUtc, result.Value.CustomerId,
                    result.Value.Username, result.Value.DisplayName), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).AllowAnonymous().RequireRateLimiting("customer-password-login");

        login.MapGet("/me", async (ICustomerAuthenticationService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetCustomer(context, out var customerId, out _))
                return Failure(new Error("customer_auth.invalid_credentials", "Customer session is invalid."), context);
            var result = await service.GetCurrentProfileAsync(customerId, ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerLoginProfileResponse>(new CustomerLoginProfileResponse(
                    result.Value.CustomerId, result.Value.Username, result.Value.DisplayName, result.Value.CreatedAtUtc),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization("Customer");

        login.MapPost("/logout", async (ICustomerAuthenticationService service, HttpContext context, CancellationToken ct) =>
        {
            if (!TryGetCustomer(context, out var customerId, out var jti))
                return Failure(new Error("customer_auth.invalid_credentials", "Customer session is invalid."), context);
            var result = await service.LogoutAsync(customerId, jti, CorrelationIdMiddleware.GetCurrent(context), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerLogoutResponse>(new CustomerLogoutResponse(result.Value.LoggedOut),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization("Customer");
    }

    private static bool TryGetCustomer(HttpContext context, out Guid customerId, out string jti)
    {
        var id = context.User.FindFirstValue("customer_id") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        jti = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti) ?? string.Empty;
        return Guid.TryParse(id, out customerId) && customerId != Guid.Empty && jti.Length == 32;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "auth.identity_missing" => StatusCodes.Status401Unauthorized,
            "customers.credentials_forbidden" => StatusCodes.Status403Forbidden,
            "customers.not_found" => StatusCodes.Status404NotFound,
            "customer_auth.invalid_credentials" => StatusCodes.Status401Unauthorized,
            "customers.credentials_invalid" or "idempotency.required" => StatusCodes.Status400BadRequest,
            "customers.version_conflict" or "customers.login_username_exists" or "idempotency.in_flight" or "idempotency.key_reused" or "idempotency.unavailable" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)), statusCode: status);
    }
}
