using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Billing.Contracts;

public sealed record CreatePendingInvoiceCommand(
    Guid InvoiceId,
    Guid SessionId,
    Guid StationId,
    Guid CustomerId,
    Guid TariffId,
    string TariffCodeSnapshot,
    string TariffNameSnapshot,
    long TariffHourlyRateTomanSnapshot,
    long ElapsedTicks,
    int SessionVersion,
    Guid IssuedByOperatorId,
    DateTimeOffset IssuedAtUtc,
    string CorrelationId,
    string Source,
    string IdempotencyKey);

/// <summary>
/// Called while the Sessions use case's mutation transaction is active. The Billing
/// implementation appends the invoice and its audit record using the same scoped context.
/// </summary>
public interface IBillingInvoiceWriter
{
    Task<Result<InvoicePreviewResponse>> CreatePendingAsync(
        CreatePendingInvoiceCommand command,
        CancellationToken cancellationToken);
}
