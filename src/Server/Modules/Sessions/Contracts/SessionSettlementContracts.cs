using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Sessions.Contracts;

public sealed record SettleSessionCommand(
    Guid SessionId,
    int ExpectedVersion,
    Guid PaidByOperatorId,
    DateTimeOffset PaidAtUtc,
    string CorrelationId,
    string Source,
    string IdempotencyKey);

/// <summary>
/// Billing calls this within its cash-checkout transaction. It updates only the Session
/// aggregate and must not open or commit a separate transaction.
/// </summary>
public interface ISessionSettlement
{
    Task<Result<SessionResponse>> MarkPaidAsync(SettleSessionCommand command, CancellationToken cancellationToken);
}
