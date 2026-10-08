using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GameNet.Server.Infrastructure.Configuration;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;
using GameNet.Shared.Primitives;
using Microsoft.Extensions.Options;

namespace GameNet.Server.Modules.Identity.Api;

public static class IdentityEndpoints
{
    private const string BootstrapSecretHeader = "X-GameNet-Bootstrap-Secret";

    public static void MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");
        group.MapGet("/bootstrap/status", async (IdentityService service, HttpContext context, CancellationToken ct) =>
        {
            var required = await service.IsBootstrapRequiredAsync(ct);
            return Results.Ok(new ApiEnvelope<BootstrapStatusResponse>(
                new BootstrapStatusResponse(required), CorrelationIdMiddleware.GetCurrent(context)));
        }).AllowAnonymous();

        group.MapPost("/bootstrap/admin", async (BootstrapAdminRequest request, IOptions<GameNetOptions> options,
            IdentityService service, HttpContext context, CancellationToken ct) =>
        {
            var denied = ValidateBootstrapSecret(context, options.Value.Setup.BootstrapSecret);
            if (denied is not null) return denied;
            var result = await service.BootstrapAsync(request.Username, request.DisplayName, request.Password,
                CorrelationIdMiddleware.GetCurrent(context), ct);
            return result.IsSuccess
                ? Results.Created("/api/v1/bootstrap/status", new ApiEnvelope<BootstrapAdminResponse>(
                    new BootstrapAdminResponse(result.Value.UserId, result.Value.Username), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).AllowAnonymous();

        group.MapPost("/auth/login", async (LoginRequest request, IdentityService service, HttpContext context, CancellationToken ct) =>
        {
            var result = await service.LoginAsync(request.Username, request.Password, CorrelationIdMiddleware.GetCurrent(context), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<LoginResponse>(new LoginResponse(result.Value.Token.AccessToken, "Bearer",
                    result.Value.Token.ExpiresAtUtc, result.Value.UserId, result.Value.Username, result.Value.DisplayName,
                    result.Value.Permissions), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).AllowAnonymous();

        group.MapGet("/auth/me", async (IdentityService service, HttpContext context, CancellationToken ct) =>
        {
            var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (!Guid.TryParse(userIdText, out var userId))
                return Failure(new Error("auth.identity_missing", "The authenticated operator identity is missing."), context);
            var result = await service.GetCurrentOperatorAsync(userId, ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CurrentOperatorResponse>(new CurrentOperatorResponse(
                    result.Value.UserId, result.Value.Username, result.Value.DisplayName, result.Value.Permissions),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization("Operator");

        group.MapPost("/auth/logout", async (IdentityService service, HttpContext context, CancellationToken ct) =>
        {
            var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var jti = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti) ?? context.User.FindFirstValue(ClaimTypes.SerialNumber);
            if (!Guid.TryParse(userIdText, out var userId) || string.IsNullOrWhiteSpace(jti))
                return Failure(new Error("auth.identity_missing", "The authenticated session identity is missing."), context);
            var result = await service.LogoutAsync(userId, jti, CorrelationIdMiddleware.GetCurrent(context), ct);
            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<LogoutResponse>(new LogoutResponse(result.Value), CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization("Operator");
    }

    private static IResult? ValidateBootstrapSecret(HttpContext context, string? expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret) || Encoding.UTF8.GetByteCount(expectedSecret) < 32)
            return Failure(new Error("bootstrap.secret_not_configured", "Initial setup is not configured on this Server."), context);
        var supplied = context.Request.Headers[BootstrapSecretHeader].ToString();
        if (string.IsNullOrWhiteSpace(supplied))
            return Failure(new Error("bootstrap.secret_invalid", "The setup secret was not accepted."), context);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        if (expectedBytes.Length != suppliedBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
            return Failure(new Error("bootstrap.secret_invalid", "The setup secret was not accepted."), context);
        return null;
    }

    private static IResult Failure(Error error, HttpContext context)
    {
        var status = error.Code switch
        {
            "bootstrap.not_required" => StatusCodes.Status409Conflict,
            "bootstrap.secret_not_configured" => StatusCodes.Status503ServiceUnavailable,
            "bootstrap.secret_invalid" => StatusCodes.Status401Unauthorized,
            "identity.invalid" => StatusCodes.Status400BadRequest,
            "auth.invalid_credentials" => StatusCodes.Status401Unauthorized,
            "auth.disabled" => StatusCodes.Status403Forbidden,
            "auth.locked" => StatusCodes.Status423Locked,
            "auth.session_not_found" => StatusCodes.Status401Unauthorized,
            "auth.identity_missing" => StatusCodes.Status401Unauthorized,
            "auth.identity_not_found" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new ApiFailure(new ApiError(error.Code, error.Message), CorrelationIdMiddleware.GetCurrent(context)), statusCode: status);
    }
}
