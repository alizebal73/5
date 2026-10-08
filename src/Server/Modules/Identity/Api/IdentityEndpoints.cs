using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Identity.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Identity;

namespace GameNet.Server.Modules.Identity.Api;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");

        group.MapGet("/bootstrap/status", async (
            IdentityService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var required = await service.IsBootstrapRequiredAsync(cancellationToken);
            return Results.Ok(new ApiEnvelope<BootstrapStatusResponse>(
                new BootstrapStatusResponse(required),
                CorrelationIdMiddleware.GetCurrent(context)));
        }).AllowAnonymous();

        group.MapPost("/bootstrap/admin", async (
            BootstrapAdminRequest request,
            IdentityService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BootstrapAsync(
                request.Username,
                request.DisplayName,
                request.Password,
                cancellationToken);

            return result.IsSuccess
                ? Results.Created(
                    "/api/v1/bootstrap/status",
                    new ApiEnvelope<object>(
                        result.Value,
                        CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).AllowAnonymous();

        group.MapPost("/auth/login", async (
            LoginRequest request,
            IdentityService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.LoginAsync(
                request.Username,
                request.Password,
                cancellationToken);

            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<LoginResponse>(
                    new LoginResponse(
                        result.Value.Token.AccessToken,
                        "Bearer",
                        result.Value.Token.ExpiresAtUtc,
                        result.Value.User.Id,
                        result.Value.User.Username,
                        result.Value.Permissions),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).AllowAnonymous();

        group.MapGet("/auth/me", async (
            IdentityService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? context.User.FindFirstValue("sub");
            if (!Guid.TryParse(userIdText, out var userId))
                return Failure(
                    new GameNet.Shared.Primitives.Error(
                        "auth.identity_missing",
                        "The authenticated operator identity is missing."),
                    context);

            var user = await service.GetCurrentOperatorAsync(
                userId,
                cancellationToken);

            return user.IsSuccess
                ? Results.Ok(new ApiEnvelope<CurrentOperatorResponse>(
                    new CurrentOperatorResponse(
                        user.Value.UserId,
                        user.Value.Username,
                        user.Value.DisplayName,
                        user.Value.Permissions),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(user.Error, context);
        }).RequireAuthorization();

        group.MapPost("/auth/logout", async (
            IdentityService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? context.User.FindFirstValue("sub");
            var jti = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti);

            if (!Guid.TryParse(userIdText, out var userId) || string.IsNullOrWhiteSpace(jti))
                return Failure(
                    new GameNet.Shared.Primitives.Error(
                        "auth.identity_missing",
                        "The authenticated session identity is missing."),
                    context);

            var result = await service.LogoutAsync(
                userId,
                jti,
                cancellationToken);

            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<object>(
                    result.Value,
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        }).RequireAuthorization();
    }

    private static IResult Failure(
        GameNet.Shared.Primitives.Error error,
        HttpContext context)
    {
        var status = error.Code switch
        {
            "bootstrap.not_required" => StatusCodes.Status409Conflict,
            "identity.invalid" => StatusCodes.Status400BadRequest,
            "auth.invalid_credentials" => StatusCodes.Status401Unauthorized,
            "auth.disabled" => StatusCodes.Status403Forbidden,
            "auth.locked" => StatusCodes.Status423Locked,
            "auth.session_not_found" => StatusCodes.Status404NotFound,
            "auth.identity_missing" => StatusCodes.Status401Unauthorized,
            "auth.identity_not_found" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new ApiFailure(
                new ApiError(error.Code, error.Message),
                CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
