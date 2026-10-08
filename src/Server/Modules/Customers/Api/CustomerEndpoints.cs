using System.Security.Claims;
using GameNet.Server.Infrastructure.Observability;
using GameNet.Server.Modules.Customers.Application;
using GameNet.Shared.Contracts.V1.Api;
using GameNet.Shared.Contracts.V1.Customers;

namespace GameNet.Server.Modules.Customers.Api;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/customers")
            .RequireAuthorization();

        group.MapGet("", async (
            string? search,
            CustomerService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(
                search,
                GetActor(context),
                cancellationToken);

            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<IReadOnlyList<CustomerResponse>>(
                    result.Value.Select(ToResponse).ToArray(),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });

        group.MapPost("", async (
            CreateCustomerRequest request,
            CustomerService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(
                new CreateCustomerCommand(
                    request.Code,
                    request.DisplayName,
                    request.Phone,
                    request.Pin,
                    context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(),
                    Guid.NewGuid().ToString("N"),
                    GetActor(context)),
                cancellationToken);

            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/customers/{result.Value.Id}",
                    new ApiEnvelope<CustomerResponse>(
                        ToResponse(result.Value),
                        CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateCustomerRequest request,
            CustomerService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(
                new UpdateCustomerCommand(
                    id,
                    request.DisplayName,
                    request.Phone,
                    request.Pin,
                    context.Request.Headers[ApiHeaders.IdempotencyKey].ToString(),
                    Guid.NewGuid().ToString("N"),
                    GetActor(context)),
                cancellationToken);

            return result.IsSuccess
                ? Results.Ok(new ApiEnvelope<CustomerResponse>(
                    ToResponse(result.Value),
                    CorrelationIdMiddleware.GetCurrent(context)))
                : Failure(result.Error, context);
        });
    }

    private static CustomerActorContext GetActor(HttpContext context)
    {
        var actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue("sub");

        var permissions = context.User.Claims
            .Where(x => string.Equals(x.Type, "permission", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);

        var source = context.Request.Headers.TryGetValue(ApiHeaders.Source, out var supplied) &&
                     !string.IsNullOrWhiteSpace(supplied)
            ? supplied.ToString()
            : "Unknown";

        return new CustomerActorContext(
            "operator",
            actorId,
            source,
            permissions);
    }

    private static CustomerResponse ToResponse(CustomerDto dto) =>
        new(
            dto.Id,
            dto.Code,
            dto.DisplayName,
            dto.Phone,
            dto.HasPin,
            dto.IsActive,
            dto.CreatedAtUtc,
            dto.UpdatedAtUtc,
            dto.Version);

    private static IResult Failure(
        GameNet.Shared.Primitives.Error error,
        HttpContext context)
    {
        var status = error.Code switch
        {
            "customers.forbidden" => StatusCodes.Status403Forbidden,
            "customers.not_found" => StatusCodes.Status404NotFound,
            "customers.code_exists" => StatusCodes.Status409Conflict,
            "customers.concurrency_conflict" => StatusCodes.Status409Conflict,
            "idempotency.in_flight" => StatusCodes.Status409Conflict,
            "idempotency.key_reused" => StatusCodes.Status409Conflict,
            "idempotency.required" => StatusCodes.Status400BadRequest,
            "customers.invalid" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new ApiFailure(
                new ApiError(error.Code, error.Message),
                CorrelationIdMiddleware.GetCurrent(context)),
            statusCode: status);
    }
}
