using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Billing.Application;

public sealed record BillingActorContext(
    Guid OperatorId,
    string CorrelationId,
    string Source,
    IReadOnlySet<string> Permissions);

public sealed record CashCheckoutCommand(
    Guid InvoiceId,
    long AmountReceivedToman,
    int ExpectedSessionVersion,
    string IdempotencyKey,
    BillingActorContext Actor);

public interface IBillingService
{
    Task<Result<IReadOnlyList<InvoicePreviewResponse>>> ListPendingInvoicesAsync(
        BillingActorContext actor,
        CancellationToken cancellationToken);

    Task<Result<CashCheckoutResponse>> CheckoutCashAsync(
        CashCheckoutCommand command,
        CancellationToken cancellationToken);
}
