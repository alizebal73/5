using GameNet.Shared.Contracts.V1.Billing;
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

    Task<Result<CashCheckoutOutcomeResponse>> GetCashCheckoutOutcomeAsync(
        BillingActorContext actor,
        Guid invoiceId,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CashCheckoutOutcomeResponse>>> ListRecentCashCheckoutsAsync(
        BillingActorContext actor,
        CancellationToken cancellationToken);

    Task<Result<CashCheckoutResponse>> CheckoutCashAsync(
        CashCheckoutCommand command,
        CancellationToken cancellationToken);
}
