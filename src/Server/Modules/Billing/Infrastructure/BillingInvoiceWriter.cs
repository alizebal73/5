using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Modules.Billing.Contracts;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Billing.Infrastructure;

/// <summary>
/// Appends a pending invoice as part of the active Session-end transaction. It is an
/// internal module contract, not a stand-alone endpoint and does not commit the transaction.
/// </summary>
public sealed class BillingInvoiceWriter(
    GameNetDbContext db,
    IAuditWriter audit,
    IGameClock clock) : IBillingInvoiceWriter
{
    public async Task<Result<InvoicePreviewResponse>> CreatePendingAsync(
        CreatePendingInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var invoice = Invoice.CreateForSession(
                command.InvoiceId,
                command.SessionId,
                command.StationId,
                command.CustomerId,
                command.TariffId,
                command.TariffCodeSnapshot,
                command.TariffNameSnapshot,
                command.TariffHourlyRateTomanSnapshot,
                command.ElapsedTicks,
                command.AmountToman,
                command.IssuedByOperatorId,
                command.IssuedAtUtc);

            await db.Invoices.AddAsync(invoice, cancellationToken);
            var response = new InvoicePreviewResponse(
                invoice.Id,
                invoice.SessionId,
                command.SessionVersion,
                invoice.StationId,
                invoice.CustomerId,
                invoice.TariffId,
                invoice.TariffCodeSnapshot,
                invoice.TariffNameSnapshot,
                invoice.TariffHourlyRateTomanSnapshot,
                invoice.ElapsedTicks,
                invoice.AmountToman,
                (InvoiceStatusContract)invoice.Status,
                invoice.IssuedAtUtc,
                invoice.PaidAtUtc);

            audit.Append(new AuditRecord(
                clock.UtcNow,
                "Operator",
                command.IssuedByOperatorId.ToString("D"),
                "billing.invoice_issued",
                "Invoice",
                invoice.Id.ToString("D"),
                null,
                command.CorrelationId,
                command.Source,
                "Succeeded",
                command.IdempotencyKey,
                null,
                JsonSerializer.Serialize(response)));

            return Result<InvoicePreviewResponse>.Success(response);
        }
        catch (ArgumentException)
        {
            return Result<InvoicePreviewResponse>.Failure(
                new Error("billing.invoice_invalid", "Session data could not produce a valid invoice."));
        }
    }
}
