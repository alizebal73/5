using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Server.Persistence;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace GameNet.Server.Modules.Sessions.Application;

public sealed class SessionService(
    GameNetDbContext db,
    CommandMutationPipeline mutationPipeline,
    IRequestFingerprint requestFingerprint,
    IAuditWriter audit,
    IGameClock clock) : ISessionService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public Task<Result<SessionResponse>> StartAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Task.FromResult(Fail<SessionResponse>("sessions.forbidden", "Not allowed to start sessions."));

        var payload = new { command.StationId, command.TariffId, command.CustomerId };
        return StartCoreAsync(command, payload, cancellationToken);
    }

    private async Task<Result<SessionResponse>> StartCoreAsync(
        StartSessionCommand command,
        object payload,
        CancellationToken cancellationToken)
    {
        var scope = $"sessions.start:{command.Actor.OperatorId:D}";
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "session.start", command.IdempotencyKey, requestFingerprint.Compute(payload),
                201, Lease, Retention,
                async token =>
                {
                    var station = await db.Stations.SingleOrDefaultAsync(x => x.Id == command.StationId, token);
                    if (station is null)
                        return Fail<SessionResponse>("sessions.station_not_found", "Station was not found.");
                    if (station.Type != StationType.Pc)
                        return Fail<SessionResponse>("sessions.pc_only", "This first session flow supports PC stations only.");
                    if (station.Status != StationStatus.Available)
                        return Fail<SessionResponse>("sessions.station_unavailable", "Station is not available.");

                    var tariff = await db.Tariffs.SingleOrDefaultAsync(
                        x => x.Id == command.TariffId && x.IsActive, token);
                    if (tariff is null)
                        return Fail<SessionResponse>("sessions.tariff_not_found", "Active tariff was not found.");
                    if (tariff.StationType != StationTypeContract.Pc)
                        return Fail<SessionResponse>("sessions.tariff_station_mismatch", "Tariff is not for PC stations.");

                    var activeExists = await db.Sessions.AnyAsync(
                        x => x.StationId == station.Id && x.Status == SessionStatus.Active, token);
                    if (activeExists)
                        return Fail<SessionResponse>("sessions.station_has_active_session", "Station already has an active session.");

                    Customer customer;
                    if (command.CustomerId is Guid customerId)
                    {
                        var existing = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId, token);
                        if (existing is null || !existing.IsActive)
                            return Fail<SessionResponse>("sessions.customer_not_found", "Active customer was not found.");
                        customer = existing;
                    }
                    else
                    {
                        customer = Customer.CreateWalkIn(Guid.NewGuid(), clock.UtcNow);
                        await db.Customers.AddAsync(customer, token);
                    }

                    var session = Session.Start(
                        Guid.NewGuid(), station.Id, customer.Id, tariff.Id,
                        tariff.Code, tariff.Name, tariff.HourlyRateToman,
                        command.Actor.OperatorId, clock.UtcNow);
                    await db.Sessions.AddAsync(session, token);

                    var response = ToResponse(session);
                    audit.Append(new AuditRecord(
                        clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "sessions.start", "Session", session.Id.ToString("D"), null,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                        command.IdempotencyKey, null, JsonSerializer.Serialize(response)));

                    return Result<SessionResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<SessionResponse>("sessions.station_has_active_session", "Station changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<InvoicePreviewResponse>> EndAsync(
        EndSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<InvoicePreviewResponse>("sessions.forbidden", "Not allowed to end sessions.");

        var scope = $"sessions.end:{command.Actor.OperatorId:D}";
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "session.end", command.IdempotencyKey,
                requestFingerprint.Compute(new { command.SessionId, command.ExpectedVersion }),
                200, Lease, Retention,
                async token =>
                {
                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == command.SessionId, token);
                    if (session is null)
                        return Fail<InvoicePreviewResponse>("sessions.not_found", "Session was not found.");
                    if (session.Version != command.ExpectedVersion)
                        return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed; refresh before ending it.");
                    if (session.Status != SessionStatus.Active)
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Only an active session can be ended.");

                    var endedAt = clock.UtcNow;
                    SessionChargeBreakdown charge;
                    try
                    {
                        charge = HourlySessionChargeCalculator.Calculate(
                            session.TariffHourlyRateTomanSnapshot, session.StartedAtUtc, endedAt);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.invalid_clock", "Server time did not produce a valid session duration.");
                    }
                    catch (OverflowException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.amount_overflow", "Session charge exceeds the supported Toman range.");
                    }

                    var before = ToResponse(session);
                    try
                    {
                        session.End(command.Actor.OperatorId, endedAt);
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<InvoicePreviewResponse>("sessions.invalid_state", "Session could not be ended from its current state.");
                    }

                    var invoice = Invoice.CreateForSession(
                        Guid.NewGuid(), session.Id, session.StationId, session.CustomerId, session.TariffId,
                        session.TariffCodeSnapshot, session.TariffNameSnapshot,
                        session.TariffHourlyRateTomanSnapshot, charge.ElapsedTicks, charge.AmountToman,
                        command.Actor.OperatorId, endedAt);
                    await db.Invoices.AddAsync(invoice, token);

                    var response = ToPreview(invoice);
                    audit.Append(new AuditRecord(
                        clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "sessions.end", "Session", session.Id.ToString("D"), null,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                        command.IdempotencyKey, JsonSerializer.Serialize(before),
                        JsonSerializer.Serialize(ToResponse(session))));
                    audit.Append(new AuditRecord(
                        clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "billing.invoice_issued", "Invoice", invoice.Id.ToString("D"), null,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                        command.IdempotencyKey, null, JsonSerializer.Serialize(response)));

                    return Result<InvoicePreviewResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<InvoicePreviewResponse>("sessions.version_conflict", "Session changed concurrently; refresh and try again.");
        }
    }

    public async Task<Result<CashCheckoutResponse>> CheckoutCashAsync(
        CashCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.Actor.Permissions.Contains(Permissions.BillingWrite))
            return Fail<CashCheckoutResponse>("billing.forbidden", "Not allowed to accept cash payments.");

        var scope = $"billing.cash-checkout:{command.Actor.OperatorId:D}";
        try
        {
            return await mutationPipeline.ExecuteAsync(
                scope, "billing.cash-checkout", command.IdempotencyKey,
                requestFingerprint.Compute(new { command.InvoiceId, command.AmountReceivedToman, command.ExpectedSessionVersion }),
                200, Lease, Retention,
                async token =>
                {
                    var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.Id == command.InvoiceId, token);
                    if (invoice is null)
                        return Fail<CashCheckoutResponse>("billing.invoice_not_found", "Invoice was not found.");
                    if (invoice.Status != InvoiceStatus.PendingPayment)
                        return Fail<CashCheckoutResponse>("billing.invoice_not_pending", "Invoice is not awaiting payment.");

                    var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == invoice.SessionId, token);
                    if (session is null)
                        return Fail<CashCheckoutResponse>("billing.session_not_found", "Session for this invoice was not found.");
                    if (session.Version != command.ExpectedSessionVersion)
                        return Fail<CashCheckoutResponse>("billing.session_version_conflict", "Session changed; refresh before checkout.");
                    if (session.Status != SessionStatus.PendingSettlement)
                        return Fail<CashCheckoutResponse>("billing.session_not_ready", "Session is not ready for settlement.");

                    var paidAt = clock.UtcNow;
                    Payment payment;
                    try
                    {
                        payment = Payment.CreateCash(
                            Guid.NewGuid(), invoice.Id, invoice.AmountToman,
                            command.AmountReceivedToman, command.Actor.OperatorId, paidAt);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return Fail<CashCheckoutResponse>("billing.cash_insufficient", "Cash received must cover the full invoice amount.");
                    }

                    var ledger = BillingLedgerEntry.RecordCashSale(
                        Guid.NewGuid(), invoice.Id, payment.Id, invoice.AmountToman,
                        command.Actor.OperatorId, paidAt);
                    var before = ToPreview(invoice);

                    try
                    {
                        invoice.MarkPaid(command.Actor.OperatorId, paidAt);
                        session.MarkPaid(command.Actor.OperatorId, paidAt);
                    }
                    catch (InvalidOperationException)
                    {
                        return Fail<CashCheckoutResponse>("billing.session_not_ready", "Invoice or session is no longer ready for payment.");
                    }

                    await db.Payments.AddAsync(payment, token);
                    await db.BillingLedgerEntries.AddAsync(ledger, token);
                    var response = new CashCheckoutResponse(
                        invoice.Id, payment.Id, ledger.Id, invoice.AmountToman,
                        payment.AmountReceivedToman, payment.ChangeGivenToman, paidAt);

                    audit.Append(new AuditRecord(
                        clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "billing.cash_checkout", "Invoice", invoice.Id.ToString("D"), null,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                        command.IdempotencyKey, JsonSerializer.Serialize(before), JsonSerializer.Serialize(response)));
                    audit.Append(new AuditRecord(
                        clock.UtcNow, "Operator", command.Actor.OperatorId.ToString("D"),
                        "billing.cash_ledger_entry", "BillingLedgerEntry", ledger.Id.ToString("D"), null,
                        command.Actor.CorrelationId, command.Actor.Source, "Succeeded",
                        command.IdempotencyKey, null, JsonSerializer.Serialize(ledger)));

                    return Result<CashCheckoutResponse>.Success(response);
                },
                cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            return Fail<CashCheckoutResponse>("billing.checkout_conflict", "Invoice was settled by another operation; refresh before retrying.");
        }
    }

    public async Task<Result<IReadOnlyList<SessionResponse>>> ListActiveAsync(
        SessionActorContext actor,
        Guid? stationId,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.SessionsOperate))
            return Fail<IReadOnlyList<SessionResponse>>("sessions.forbidden", "Not allowed to read sessions.");

        var query = db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.Active);
        if (stationId.HasValue) query = query.Where(x => x.StationId == stationId.Value);
        var rows = await query.OrderBy(x => x.StartedAtUtc).ToArrayAsync(cancellationToken);
        return Result<IReadOnlyList<SessionResponse>>.Success(rows.Select(ToResponse).ToArray());
    }

    public async Task<Result<IReadOnlyList<InvoicePreviewResponse>>> ListPendingInvoicesAsync(
        SessionActorContext actor,
        CancellationToken cancellationToken)
    {
        if (!actor.Permissions.Contains(Permissions.BillingWrite))
            return Fail<IReadOnlyList<InvoicePreviewResponse>>("billing.forbidden", "Not allowed to read pending invoices.");

        var rows = await db.Invoices.AsNoTracking()
            .Where(x => x.Status == InvoiceStatus.PendingPayment)
            .OrderBy(x => x.IssuedAtUtc).ToArrayAsync(cancellationToken);
        return Result<IReadOnlyList<InvoicePreviewResponse>>.Success(rows.Select(ToPreview).ToArray());
    }

    private static SessionResponse ToResponse(Session session) =>
        new(session.Id, session.StationId, session.CustomerId, session.TariffId,
            session.TariffCodeSnapshot, session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
            session.StartedByOperatorId, session.StartedAtUtc, session.EndedByOperatorId, session.EndedAtUtc,
            session.PaidByOperatorId, session.PaidAtUtc, (SessionStatusContract)session.Status, session.Version);

    private static InvoicePreviewResponse ToPreview(Invoice invoice) =>
        new(invoice.Id, invoice.SessionId, invoice.StationId, invoice.CustomerId, invoice.TariffId,
            invoice.TariffCodeSnapshot, invoice.TariffNameSnapshot, invoice.TariffHourlyRateTomanSnapshot,
            invoice.ElapsedTicks, invoice.AmountToman, (InvoiceStatusContract)invoice.Status,
            invoice.IssuedAtUtc, invoice.PaidAtUtc);

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));
}
