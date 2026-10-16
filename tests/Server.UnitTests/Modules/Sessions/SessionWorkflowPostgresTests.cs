using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameNet.Server.Infrastructure.Audit;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Server.Modules.Billing.Application;
using GameNet.Server.Modules.Billing.Domain;
using GameNet.Server.Modules.Billing.Infrastructure;
using GameNet.Server.Modules.Customers.Domain;
using GameNet.Server.Modules.Customers.Infrastructure;
using GameNet.Server.Modules.Stations.Infrastructure;
using GameNet.Server.Modules.Tariffs.Infrastructure;
using GameNet.Server.Modules.Sessions.Infrastructure;
using GameNet.Server.Modules.Sessions.Application;
using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Server.Modules.Stations.Domain;
using GameNet.Server.Modules.Tariffs.Domain;
using GameNet.Server.Persistence;
using GameNet.Server.Persistence.Entities;
using GameNet.Shared.Contracts.V1.Security;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Sessions;

/// <summary>
/// PostgreSQL-backed end-to-end service tests. The Foundation script opts in explicitly
/// and points these tests only at its disposable PostgreSQL database.
/// </summary>
[Collection("Session workflow PostgreSQL serial")]
public sealed class SessionWorkflowPostgresTests
{
    [Fact]
    public async Task Pc_session_replay_end_invoice_and_cash_checkout_are_atomic()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "slice1-pg-" + token;
        var idempotencyPrefix = "slice1-pg-" + token + "-";
        var startKey = idempotencyPrefix + "start";
        var endKey = idempotencyPrefix + "end";
        var checkoutKey = idempotencyPrefix + "cash";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));
        Guid? customerId = null;

        try
        {
            await using (var db = CreateContext(connectionString))
            {
                var station = Station.Create(stationId, "PG-" + token[..12], "Slice 1 PostgreSQL PC", StationType.Pc);
                var tariff = Tariff.CreateFixedHourly(
                    tariffId, "PG-" + token[..12], "Slice 1 PostgreSQL tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow);
                db.Stations.Add(station);
                db.Tariffs.Add(tariff);
                await db.SaveChangesAsync();

                var service = CreateService(db, clock);
                var actor = Actor(operatorId, correlation);
                var billingActor = BillingActor(operatorId, correlation);
                var billing = CreateBillingService(db, clock);

                var customerCountBeforeRejectedStarts = await db.Customers.CountAsync();
                var unboundStart = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, startKey + "-unbound", actor),
                    CancellationToken.None);
                Assert.False(unboundStart.IsSuccess);
                Assert.Equal("sessions.agent_unavailable", unboundStart.Error.Code);
                Assert.Equal(0, await db.Sessions.CountAsync(x => x.StationId == stationId));
                Assert.Equal(customerCountBeforeRejectedStarts, await db.Customers.CountAsync());

                var deviceId = "pg-agent-" + token[..12];
                station.BindAgent(deviceId);
                var credential = new AgentCredential
                {
                    DeviceId = deviceId,
                    SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test-agent-secret-" + token))),
                    CreatedAtUtc = clock.UtcNow
                };
                var lease = new AgentConnectionLease
                {
                    DeviceId = deviceId,
                    ConnectionId = "pg-connection-" + token[..12],
                    LeaseToken = Guid.NewGuid().ToString("N"),
                    LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(-1),
                    UpdatedAtUtc = clock.UtcNow,
                    LastHeartbeatAtUtc = clock.UtcNow
                };
                db.AgentCredentials.Add(credential);
                db.AgentConnectionLeases.Add(lease);
                await db.SaveChangesAsync();

                var expiredLeaseStart = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, startKey + "-expired-lease", actor),
                    CancellationToken.None);
                Assert.False(expiredLeaseStart.IsSuccess);
                Assert.Equal("sessions.agent_unavailable", expiredLeaseStart.Error.Code);

                lease.LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5);
                lease.LastHeartbeatAtUtc = clock.UtcNow.AddMinutes(-3);
                await db.SaveChangesAsync();
                var staleHeartbeatStart = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, startKey + "-stale-heartbeat", actor),
                    CancellationToken.None);
                Assert.False(staleHeartbeatStart.IsSuccess);
                Assert.Equal("sessions.agent_unavailable", staleHeartbeatStart.Error.Code);

                lease.LastHeartbeatAtUtc = clock.UtcNow;
                credential.Revoke(clock.UtcNow);
                await db.SaveChangesAsync();
                var revokedCredentialStart = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, startKey + "-revoked-credential", actor),
                    CancellationToken.None);
                Assert.False(revokedCredentialStart.IsSuccess);
                Assert.Equal("sessions.agent_unavailable", revokedCredentialStart.Error.Code);
                Assert.Equal(customerCountBeforeRejectedStarts, await db.Customers.CountAsync());
                Assert.Equal(0, await db.Sessions.CountAsync(x => x.StationId == stationId));

                db.AgentCredentials.Add(new AgentCredential
                {
                    DeviceId = deviceId,
                    SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test-agent-secret-rotation-" + token))),
                    CreatedAtUtc = clock.UtcNow
                });
                lease.LastHeartbeatAtUtc = clock.UtcNow;
                lease.LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5);
                await db.SaveChangesAsync();

                // Blocked registered customers must be rejected by the Server boundary,
                // even when a live Agent lease and a valid tariff are available.
                var blockedCustomer = Customer.CreateRegistered(
                    Guid.NewGuid(), "CB-" + token[..12], "Blocked PostgreSQL customer", clock.UtcNow);
                blockedCustomer.SetStatus(CustomerStatus.Blocked);
                db.Customers.Add(blockedCustomer);
                await db.SaveChangesAsync();

                var blockedCustomerStart = await service.StartAsync(
                    new StartSessionCommand(
                        stationId, tariffId, blockedCustomer.Id, startKey + "-blocked-customer", actor),
                    CancellationToken.None);
                Assert.False(blockedCustomerStart.IsSuccess);
                Assert.Equal("sessions.customer_blocked", blockedCustomerStart.Error.Code);
                Assert.Equal(0, await db.Sessions.CountAsync(x => x.StationId == stationId));
                db.Customers.Remove(blockedCustomer);
                await db.SaveChangesAsync();

                var startCommand = new StartSessionCommand(stationId, tariffId, null, startKey, actor);
                var started = await service.StartAsync(startCommand, CancellationToken.None);
                Assert.True(started.IsSuccess, started.IsSuccess ? "" : started.Error.Code);
                Assert.Equal(SessionStatusContract.Active, started.Value.Status);
                Assert.Equal(("PG-" + token[..12]).ToUpperInvariant(), started.Value.TariffCodeSnapshot);
                Assert.Equal(60_000L, started.Value.TariffHourlyRateTomanSnapshot);
                customerId = started.Value.CustomerId;

                // Null CustomerId must create a persisted minimal Walk-in customer under
                // the authoritative Server clock, and a replay must not duplicate it.
                var walkIn = await db.Customers.AsNoTracking()
                    .SingleAsync(x => x.Id == customerId.Value);
                Assert.Equal(CustomerKind.WalkIn, walkIn.Kind);
                Assert.StartsWith("W-", walkIn.Code, StringComparison.Ordinal);
                Assert.Null(walkIn.DisplayName);
                Assert.True(walkIn.IsActive);
                // PostgreSQL timestamptz persists microsecond precision; compare at that
                // boundary instead of requiring .NET's finer 100-nanosecond tick precision.
                Assert.Equal(
                    clock.UtcNow.UtcDateTime.Ticks / 10,
                    walkIn.CreatedAtUtc.UtcDateTime.Ticks / 10);

                var replay = await service.StartAsync(startCommand, CancellationToken.None);
                Assert.True(replay.IsSuccess);
                Assert.Equal(started.Value.Id, replay.Value.Id);

                var changedIntent = startCommand with { TariffId = Guid.NewGuid() };
                var conflict = await service.StartAsync(changedIntent, CancellationToken.None);
                Assert.False(conflict.IsSuccess);
                Assert.Equal("idempotency.key_reused", conflict.Error.Code);

                Assert.Equal(1, await db.Sessions.CountAsync(x => x.StationId == stationId));
                Assert.Equal(1, await db.Customers.CountAsync(x => x.Id == customerId.Value));
                Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.Key == startKey));

                // Editing today's tariff after Session start must not reprice this Session.
                tariff.ChangeHourlyRate(120_000L);
                await db.SaveChangesAsync();
                Assert.Equal(120_000L, await db.Tariffs.AsNoTracking()
                    .Where(x => x.Id == tariffId)
                    .Select(x => x.HourlyRateToman)
                    .SingleAsync());
                Assert.Equal(60_000L, started.Value.TariffHourlyRateTomanSnapshot);

                clock.Advance(TimeSpan.FromMinutes(30));
                var endCommand = new EndSessionCommand(
                    started.Value.Id, started.Value.Version, endKey, actor);
                var ended = await service.EndAsync(endCommand, CancellationToken.None);
                Assert.True(ended.IsSuccess, ended.IsSuccess ? "" : ended.Error.Code);
                Assert.Equal(InvoiceStatusContract.PendingPayment, ended.Value.Status);
                Assert.Equal(60_000L, ended.Value.TariffHourlyRateTomanSnapshot);
                Assert.Equal(30_000L, ended.Value.AmountToman);
                Assert.Equal(started.Value.Version + 1, ended.Value.SessionVersion);

                var invoiceReplay = await service.EndAsync(endCommand, CancellationToken.None);
                Assert.True(invoiceReplay.IsSuccess);
                Assert.Equal(ended.Value.InvoiceId, invoiceReplay.Value.InvoiceId);
                Assert.Equal(ended.Value.AmountToman, invoiceReplay.Value.AmountToman);
                Assert.Equal(1, await db.Invoices.CountAsync(x => x.SessionId == started.Value.Id));

                var insufficientCheckout = await billing.CheckoutCashAsync(
                    new CashCheckoutCommand(
                        ended.Value.InvoiceId, 29_999L, ended.Value.SessionVersion,
                        idempotencyPrefix + "cash-low", billingActor),
                    CancellationToken.None);
                Assert.False(insufficientCheckout.IsSuccess);
                Assert.Equal("billing.cash_insufficient", insufficientCheckout.Error.Code);
                Assert.Equal(InvoiceStatus.PendingPayment,
                    (await db.Invoices.SingleAsync(x => x.Id == ended.Value.InvoiceId)).Status);
                Assert.Equal(0, await db.Payments.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));
                Assert.Equal(0, await db.BillingLedgerEntries.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));

                // The 30-minute fake-time jump represents billing duration, not an Agent outage.
                // Refresh authoritative Agent liveness before isolating the pending-settlement rule.
                lease.LastHeartbeatAtUtc = clock.UtcNow;
                lease.LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5);
                lease.UpdatedAtUtc = clock.UtcNow;
                await db.SaveChangesAsync();

                var blockedStart = await service.StartAsync(
                    new StartSessionCommand(
                        stationId, tariffId, null, idempotencyPrefix + "start-before-settlement", actor),
                    CancellationToken.None);
                Assert.False(blockedStart.IsSuccess);
                Assert.Equal("sessions.station_pending_settlement", blockedStart.Error.Code);

                var checkoutCommand = new CashCheckoutCommand(
                    ended.Value.InvoiceId, 50_000L, ended.Value.SessionVersion, checkoutKey, billingActor);
                var pendingList = await billing.ListPendingInvoicesAsync(billingActor, CancellationToken.None);
                Assert.True(pendingList.IsSuccess);
                Assert.Contains(pendingList.Value, x => x.InvoiceId == ended.Value.InvoiceId && x.SessionVersion == ended.Value.SessionVersion);

                var pendingOutcome = await billing.GetCashCheckoutOutcomeAsync(
                    billingActor, ended.Value.InvoiceId, CancellationToken.None);
                Assert.True(pendingOutcome.IsSuccess, pendingOutcome.IsSuccess ? "" : pendingOutcome.Error.Code);
                Assert.Equal(InvoiceStatusContract.PendingPayment, pendingOutcome.Value.Status);
                Assert.Null(pendingOutcome.Value.PaymentId);
                Assert.Null(pendingOutcome.Value.LedgerEntryId);
                Assert.Null(pendingOutcome.Value.AmountReceivedToman);
                Assert.Null(pendingOutcome.Value.ChangeGivenToman);

                var staleCheckout = await billing.CheckoutCashAsync(
                    new CashCheckoutCommand(
                        ended.Value.InvoiceId, 50_000L, ended.Value.SessionVersion - 1,
                        idempotencyPrefix + "cash-stale", billingActor),
                    CancellationToken.None);
                Assert.False(staleCheckout.IsSuccess);
                Assert.Equal("billing.session_version_conflict", staleCheckout.Error.Code);
                Assert.Equal(InvoiceStatus.PendingPayment,
                    (await db.Invoices.SingleAsync(x => x.Id == ended.Value.InvoiceId)).Status);
                Assert.Equal(0, await db.Payments.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));
                Assert.Equal(0, await db.BillingLedgerEntries.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));

                var checkout = await billing.CheckoutCashAsync(checkoutCommand, CancellationToken.None);
                Assert.True(checkout.IsSuccess, checkout.IsSuccess ? "" : checkout.Error.Code);
                Assert.Equal(30_000L, checkout.Value.InvoiceAmountToman);
                Assert.Equal(50_000L, checkout.Value.AmountReceivedToman);
                Assert.Equal(20_000L, checkout.Value.ChangeGivenToman);

                var paidOutcome = await billing.GetCashCheckoutOutcomeAsync(
                    billingActor, ended.Value.InvoiceId, CancellationToken.None);
                Assert.True(paidOutcome.IsSuccess, paidOutcome.IsSuccess ? "" : paidOutcome.Error.Code);
                Assert.Equal(InvoiceStatusContract.Paid, paidOutcome.Value.Status);
                Assert.Equal(checkout.Value.PaymentId, paidOutcome.Value.PaymentId);
                Assert.Equal(checkout.Value.LedgerEntryId, paidOutcome.Value.LedgerEntryId);
                Assert.Equal(50_000L, paidOutcome.Value.AmountReceivedToman);
                Assert.Equal(20_000L, paidOutcome.Value.ChangeGivenToman);

                var recentOutcomes = await billing.ListRecentCashCheckoutsAsync(billingActor, CancellationToken.None);
                Assert.True(recentOutcomes.IsSuccess, recentOutcomes.IsSuccess ? "" : recentOutcomes.Error.Code);
                Assert.Contains(recentOutcomes.Value, x =>
                    x.InvoiceId == ended.Value.InvoiceId &&
                    x.Status == InvoiceStatusContract.Paid &&
                    x.PaymentId == checkout.Value.PaymentId);

                var checkoutReplay = await billing.CheckoutCashAsync(checkoutCommand, CancellationToken.None);
                Assert.True(checkoutReplay.IsSuccess);
                Assert.Equal(checkout.Value.PaymentId, checkoutReplay.Value.PaymentId);
                Assert.Equal(checkout.Value.LedgerEntryId, checkoutReplay.Value.LedgerEntryId);

                var correctionOnlyActor = billingActor with
                {
                    Permissions = new HashSet<string>([Permissions.BillingCashTenderCorrect], StringComparer.Ordinal)
                };
                var correctionCommand = new CashTenderCorrectionCommand(
                    ended.Value.InvoiceId,
                    checkout.Value.PaymentId,
                    ExpectedVersion: 0,
                    CorrectedAmountReceivedToman: 45_000L,
                    Reason: "Recorded cash received amount was entered incorrectly",
                    Confirmed: true,
                    IdempotencyKey: idempotencyPrefix + "tender-correction-v1",
                    Actor: correctionOnlyActor);
                var unconfirmedCorrection = await billing.CorrectCashTenderAsync(
                    correctionCommand with
                    {
                        Confirmed = false,
                        IdempotencyKey = idempotencyPrefix + "tender-correction-unconfirmed"
                    },
                    CancellationToken.None);
                Assert.False(unconfirmedCorrection.IsSuccess);
                Assert.Equal("billing.tender_correction_invalid", unconfirmedCorrection.Error.Code);
                Assert.Equal(0, await db.CashTenderCorrections.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));

                var correction = await billing.CorrectCashTenderAsync(correctionCommand, CancellationToken.None);
                Assert.True(correction.IsSuccess, correction.IsSuccess ? "" : correction.Error.Code);
                Assert.Equal(1, correction.Value.Version);
                Assert.Equal(50_000L, correction.Value.PreviousAmountReceivedToman);
                Assert.Equal(20_000L, correction.Value.PreviousChangeGivenToman);
                Assert.Equal(45_000L, correction.Value.CorrectedAmountReceivedToman);
                Assert.Equal(15_000L, correction.Value.CorrectedChangeGivenToman);

                var correctionReplay = await billing.CorrectCashTenderAsync(correctionCommand, CancellationToken.None);
                Assert.True(correctionReplay.IsSuccess, correctionReplay.IsSuccess ? "" : correctionReplay.Error.Code);
                Assert.Equal(correction.Value.CorrectionId, correctionReplay.Value.CorrectionId);
                Assert.Equal(1, await db.CashTenderCorrections.CountAsync(x => x.InvoiceId == ended.Value.InvoiceId));

                var staleCorrection = await billing.CorrectCashTenderAsync(
                    correctionCommand with
                    {
                        CorrectedAmountReceivedToman = 44_000L,
                        Reason = "Correct a stale cash entry",
                        IdempotencyKey = idempotencyPrefix + "tender-correction-stale"
                    },
                    CancellationToken.None);
                Assert.False(staleCorrection.IsSuccess);
                Assert.Equal("billing.tender_correction_version_conflict", staleCorrection.Error.Code);

                var correctedOutcome = await billing.GetCashCheckoutOutcomeAsync(
                    correctionOnlyActor, ended.Value.InvoiceId, CancellationToken.None);
                Assert.True(correctedOutcome.IsSuccess, correctedOutcome.IsSuccess ? "" : correctedOutcome.Error.Code);
                Assert.Equal(50_000L, correctedOutcome.Value.AmountReceivedToman);
                Assert.Equal(20_000L, correctedOutcome.Value.ChangeGivenToman);
                Assert.Equal(45_000L, correctedOutcome.Value.EffectiveAmountReceivedToman);
                Assert.Equal(15_000L, correctedOutcome.Value.EffectiveChangeGivenToman);
                Assert.Equal(correction.Value.CorrectionId, correctedOutcome.Value.CashTenderCorrectionId);
                Assert.Equal(1, correctedOutcome.Value.CashTenderCorrectionVersion);

                var correctedHistory = await billing.ListRecentCashCheckoutsAsync(correctionOnlyActor, CancellationToken.None);
                Assert.True(correctedHistory.IsSuccess, correctedHistory.IsSuccess ? "" : correctedHistory.Error.Code);
                Assert.Contains(correctedHistory.Value, x =>
                    x.InvoiceId == ended.Value.InvoiceId &&
                    x.EffectiveAmountReceivedToman == 45_000L &&
                    x.EffectiveChangeGivenToman == 15_000L);

                var reversalA = new CashSaleReversalCommand(
                    ended.Value.InvoiceId, checkout.Value.PaymentId, 30_000L,
                    "Test full cash-sale reversal", idempotencyPrefix + "reversal-a", billingActor);
                var reversalB = reversalA with { IdempotencyKey = idempotencyPrefix + "reversal-b" };
                await using var reverseDbA = CreateContext(connectionString);
                await using var reverseDbB = CreateContext(connectionString);
                var reverseBillingA = CreateBillingService(reverseDbA, clock);
                var reverseBillingB = CreateBillingService(reverseDbB, clock);
                var reversalAttempts = await Task.WhenAll(
                    reverseBillingA.ReverseCashSaleAsync(reversalA, CancellationToken.None),
                    reverseBillingB.ReverseCashSaleAsync(reversalB, CancellationToken.None))
                    .WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Single(reversalAttempts, result => result.IsSuccess);
                var winnerIndex = reversalAttempts[0].IsSuccess ? 0 : 1;
                var winnerService = winnerIndex == 0 ? reverseBillingA : reverseBillingB;
                var winnerCommand = winnerIndex == 0 ? reversalA : reversalB;
                var reversal = reversalAttempts[winnerIndex].Value;
                var failedAttempt = reversalAttempts[1 - winnerIndex];
                Assert.False(failedAttempt.IsSuccess);
                Assert.Contains(failedAttempt.Error.Code, new[] { "billing.sale_already_reversed", "billing.reversal_conflict" });
                var replayedReversal = await winnerService.ReverseCashSaleAsync(winnerCommand, CancellationToken.None);
                Assert.True(replayedReversal.IsSuccess);
                Assert.Equal(reversal.ReversalId, replayedReversal.Value.ReversalId);

                var reversalOnlyActor = billingActor with
                {
                    Permissions = new HashSet<string>([Permissions.BillingCashReverse], StringComparer.Ordinal)
                };
                var reversedOutcome = await billing.GetCashCheckoutOutcomeAsync(reversalOnlyActor, ended.Value.InvoiceId, CancellationToken.None);
                Assert.True(reversedOutcome.IsSuccess, reversedOutcome.IsSuccess ? "" : reversedOutcome.Error.Code);
                Assert.Equal(InvoiceStatusContract.Reversed, reversedOutcome.Value.Status);
                Assert.Equal(reversal.ReversalId, reversedOutcome.Value.ReversalId);
                Assert.Equal(reversal.LedgerEntryId, reversedOutcome.Value.ReversalLedgerEntryId);
                Assert.Equal(30_000L, reversedOutcome.Value.AmountToman);
                Assert.Equal(50_000L, reversedOutcome.Value.AmountReceivedToman);
                Assert.Equal(20_000L, reversedOutcome.Value.ChangeGivenToman);
                Assert.Equal(45_000L, reversedOutcome.Value.EffectiveAmountReceivedToman);
                Assert.Equal(15_000L, reversedOutcome.Value.EffectiveChangeGivenToman);
                Assert.Equal(correction.Value.CorrectionId, reversedOutcome.Value.CashTenderCorrectionId);

                var correctionAfterReversal = await billing.CorrectCashTenderAsync(
                    correctionCommand with
                    {
                        ExpectedVersion = 1,
                        CorrectedAmountReceivedToman = 44_000L,
                        Reason = "Attempt after sale reversal",
                        IdempotencyKey = idempotencyPrefix + "tender-correction-after-reversal"
                    },
                    CancellationToken.None);
                Assert.False(correctionAfterReversal.IsSuccess);
                Assert.Equal("billing.tender_correction_reversal_forbidden", correctionAfterReversal.Error.Code);

                var recentOutcome = await billing.ListRecentCashCheckoutsAsync(reversalOnlyActor, CancellationToken.None);
                Assert.True(recentOutcome.IsSuccess);
                Assert.Contains(recentOutcome.Value, x => x.InvoiceId == ended.Value.InvoiceId &&
                    x.Status == InvoiceStatusContract.Reversed && x.ReversalId == reversal.ReversalId);

                // Original Payment and sale row remain immutable; the compensating ledger amount is positive.
                db.ChangeTracker.Clear();
                var session = await db.Sessions.SingleAsync(x => x.Id == started.Value.Id);
                var invoice = await db.Invoices.SingleAsync(x => x.Id == ended.Value.InvoiceId);
                var payment = await db.Payments.SingleAsync(x => x.InvoiceId == invoice.Id);
                var ledgerEntries = await db.BillingLedgerEntries.Where(x => x.InvoiceId == invoice.Id).ToArrayAsync();
                var saleLedger = ledgerEntries.Single(x => x.EntryType == BillingLedgerEntryType.CashSale);
                var reversalLedger = ledgerEntries.Single(x => x.EntryType == BillingLedgerEntryType.CashSaleReversal);
                var reversalRow = await db.CashSaleReversals.SingleAsync(x => x.InvoiceId == invoice.Id);
                var correctionRow = await db.CashTenderCorrections.SingleAsync(x => x.InvoiceId == invoice.Id);
                Assert.Equal(SessionStatus.Paid, session.Status);
                Assert.Equal(InvoiceStatus.Reversed, invoice.Status);
                Assert.Equal(PaymentMethod.Cash, payment.Method);
                Assert.Equal(30_000L, payment.AmountAppliedToman);
                Assert.Equal(50_000L, payment.AmountReceivedToman);
                Assert.Equal(20_000L, payment.ChangeGivenToman);
                Assert.Equal(BillingLedgerEntryType.CashSale, saleLedger.EntryType);
                Assert.Equal(invoice.AmountToman, saleLedger.AmountToman);
                Assert.Equal(BillingLedgerEntryType.CashSaleReversal, reversalLedger.EntryType);
                Assert.Equal(invoice.AmountToman, reversalLedger.AmountToman);
                Assert.Equal(reversal.ReversalId, reversalRow.Id);
                Assert.Equal(correction.Value.CorrectionId, correctionRow.Id);
                Assert.Equal(1, correctionRow.Version);
                Assert.Equal(45_000L, correctionRow.CorrectedAmountReceivedToman);
                Assert.Equal(15_000L, correctionRow.CorrectedChangeGivenToman);
                Assert.Equal(1, await db.CashTenderCorrections.CountAsync(x => x.InvoiceId == invoice.Id));
                Assert.Equal(1, await db.Payments.CountAsync(x => x.InvoiceId == invoice.Id));
                Assert.Equal(2, await db.BillingLedgerEntries.CountAsync(x => x.InvoiceId == invoice.Id));
                Assert.Equal(1, await db.CashSaleReversals.CountAsync(x => x.InvoiceId == invoice.Id));

                var audits = await db.AuditEntries.AsNoTracking()
                    .Where(x => x.CorrelationId == correlation)
                    .Select(x => x.Operation)
                    .ToArrayAsync();
                Assert.Contains("sessions.start", audits);
                Assert.Contains("sessions.end", audits);
                Assert.Contains("billing.invoice_issued", audits);
                Assert.Contains("billing.cash_checkout", audits);
                Assert.Contains("billing.cash_ledger_entry", audits);
                Assert.Contains("billing.cash_sale_reversal", audits);
                Assert.Contains("billing.cash_sale_reversal_ledger_entry", audits);
                Assert.Contains("billing.cash_tender_correction", audits);
            }
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Concurrent_starts_cannot_create_two_active_sessions_for_one_pc()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();
        var correlationA = "slice1-concurrent-a-" + token;
        var correlationB = "slice1-concurrent-b-" + token;
        var idempotencyPrefix = "slice1-concurrent-" + token + "-";
        Guid? customerA = null;
        Guid? customerB = null;
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        try
        {
            await using (var setup = CreateContext(connectionString))
            {
                var station = Station.Create(
                    stationId, "PC-" + token[..12], "Concurrent start PC", StationType.Pc);
                setup.Stations.Add(station);
                setup.Tariffs.Add(Tariff.CreateFixedHourly(
                    tariffId, "TR-" + token[..12], "Concurrent start tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow));
                await setup.SaveChangesAsync();
                await BindActiveAgentAsync(setup, station, clock, token);
            }

            Result<SessionResponse> first;
            Result<SessionResponse> second;
            await using (var dbA = CreateContext(connectionString))
            await using (var dbB = CreateContext(connectionString))
            {
                var serviceA = CreateService(dbA, clock);
                var serviceB = CreateService(dbB, clock);
                var requestA = new StartSessionCommand(
                    stationId, tariffId, null, idempotencyPrefix + "a", Actor(operatorA, correlationA));
                var requestB = new StartSessionCommand(
                    stationId, tariffId, null, idempotencyPrefix + "b", Actor(operatorB, correlationB));

                var results = await Task.WhenAll(
                    serviceA.StartAsync(requestA, CancellationToken.None),
                    serviceB.StartAsync(requestB, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(30));
                first = results[0];
                second = results[1];
                if (first.IsSuccess) customerA = first.Value.CustomerId;
                if (second.IsSuccess) customerB = second.Value.CustomerId;
            }

            Assert.Single(new[] { first, second }, result => result.IsSuccess);
            var failure = first.IsSuccess ? second : first;
            Assert.False(failure.IsSuccess);
            Assert.Equal("sessions.station_has_active_session", failure.Error.Code);

            await using var verify = CreateContext(connectionString);
            Assert.Equal(1, await verify.Sessions.CountAsync(x => x.StationId == stationId && x.Status == SessionStatus.Active));
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerA, correlationA, idempotencyPrefix);
            await CleanupAsync(connectionString, stationId, tariffId, customerB, correlationB, idempotencyPrefix);
        }
    }


    [Fact]
    public async Task Session_end_after_server_process_restart_and_utc_jump_uses_monotonic_elapsed_time()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "timing-utc-" + token;
        var idempotencyPrefix = "timing-utc-" + token + "-";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));
        Guid? customerId = null;

        try
        {
            await using (var setup = CreateContext(connectionString))
            {
                var station = Station.Create(stationId, "TU-" + token[..12], "Timing UTC PC", StationType.Pc);
                setup.Stations.Add(station);
                setup.Tariffs.Add(Tariff.CreateFixedHourly(
                    tariffId, "TU-" + token[..12], "Timing UTC tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow));
                await setup.SaveChangesAsync();
                await BindActiveAgentAsync(setup, station, clock, token);
            }

            SessionResponse started;
            await using (var startDb = CreateContext(connectionString))
            {
                var start = await CreateService(startDb, clock).StartAsync(
                    new StartSessionCommand(
                        stationId, tariffId, null, idempotencyPrefix + "start", Actor(operatorId, correlation)),
                    CancellationToken.None);
                Assert.True(start.IsSuccess, start.IsSuccess ? "" : start.Error.Code);
                started = start.Value;
                customerId = started.CustomerId;
            }

            // A new scoped context/service represents a Server process restart. UTC jumps
            // backwards, while monotonic timestamp, OS uptime and boot identity remain valid.
            clock.Advance(TimeSpan.FromMinutes(30));
            clock.JumpWallClock(TimeSpan.FromHours(-4));

            await using (var endDb = CreateContext(connectionString))
            {
                var service = CreateService(endDb, clock);
                var recoveryActor = new SessionActorContext(
                    operatorId, correlation, "PostgreSQLTest",
                    new HashSet<string>(
                        [Permissions.SessionsOperate, Permissions.SessionsTimingRecover, Permissions.BillingWrite],
                        StringComparer.Ordinal));
                var unnecessaryRecovery = await service.ReconcileTimingAsync(
                    new ReconcileSessionTimingCommand(
                        started.Id, started.Version, 31,
                        "Attempted manual timing although monotonic continuity is valid", true,
                        idempotencyPrefix + "unnecessary-recovery", recoveryActor),
                    CancellationToken.None);
                Assert.False(unnecessaryRecovery.IsSuccess);
                Assert.Equal("sessions.timing_recovery_not_required", unnecessaryRecovery.Error.Code);
                Assert.Equal(0, await endDb.Invoices.CountAsync(x => x.SessionId == started.Id));

                var ended = await service.EndAsync(
                    new EndSessionCommand(
                        started.Id, started.Version, idempotencyPrefix + "end", Actor(operatorId, correlation)),
                    CancellationToken.None);
                Assert.True(ended.IsSuccess, ended.IsSuccess ? "" : ended.Error.Code);
                Assert.Equal(TimeSpan.FromMinutes(30).Ticks, ended.Value.ElapsedTicks);
                Assert.Equal(30_000L, ended.Value.AmountToman);
                Assert.True(ended.Value.IssuedAtUtc < started.StartedAtUtc);
            }

            await using var verify = CreateContext(connectionString);
            var savedSession = await verify.Sessions.AsNoTracking().SingleAsync(x => x.Id == started.Id);
            Assert.Equal(SessionStatus.PendingSettlement, savedSession.Status);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, savedSession.ElapsedTicks);
            Assert.Equal(1, await verify.Invoices.CountAsync(x => x.SessionId == started.Id));
            var auditOperations = await verify.AuditEntries.AsNoTracking()
                .Where(x => x.CorrelationId == correlation)
                .Select(x => x.Operation)
                .ToArrayAsync();
            Assert.Contains("sessions.start", auditOperations);
            Assert.Contains("sessions.end", auditOperations);
            Assert.Contains("billing.invoice_issued", auditOperations);
            Assert.DoesNotContain("sessions.timing.reconciled", auditOperations);
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Windows_reboot_fails_closed_until_dual_permission_audited_timing_reconciliation()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "timing-recovery-" + token;
        var idempotencyPrefix = "timing-recovery-" + token + "-";
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));
        Guid? customerId = null;

        try
        {
            await using (var setup = CreateContext(connectionString))
            {
                var station = Station.Create(stationId, "TR-" + token[..12], "Timing recovery PC", StationType.Pc);
                setup.Stations.Add(station);
                setup.Tariffs.Add(Tariff.CreateFixedHourly(
                    tariffId, "TR-" + token[..12], "Timing recovery tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow));
                await setup.SaveChangesAsync();
                await BindActiveAgentAsync(setup, station, clock, token);
            }

            SessionResponse started;
            await using (var startDb = CreateContext(connectionString))
            {
                var start = await CreateService(startDb, clock).StartAsync(
                    new StartSessionCommand(
                        stationId, tariffId, null, idempotencyPrefix + "start", Actor(operatorId, correlation)),
                    CancellationToken.None);
                Assert.True(start.IsSuccess, start.IsSuccess ? "" : start.Error.Code);
                started = start.Value;
                customerId = started.CustomerId;
            }

            clock.Advance(TimeSpan.FromMinutes(30));
            clock.SimulateWindowsReboot();

            await using (var db = CreateContext(connectionString))
            {
                var service = CreateService(db, clock);
                var normalEnd = await service.EndAsync(
                    new EndSessionCommand(
                        started.Id, started.Version, idempotencyPrefix + "normal-end", Actor(operatorId, correlation)),
                    CancellationToken.None);
                Assert.False(normalEnd.IsSuccess);
                Assert.Equal("sessions.timing_recovery_required", normalEnd.Error.Code);
                Assert.Equal(0, await db.Invoices.CountAsync(x => x.SessionId == started.Id));
                Assert.Equal(SessionStatus.Active,
                    (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == started.Id)).Status);

                var denied = await service.ReconcileTimingAsync(
                    new ReconcileSessionTimingCommand(
                        started.Id, started.Version, 30,
                        "Verified play duration after Windows reboot", true,
                        idempotencyPrefix + "denied-recovery", Actor(operatorId, correlation)),
                    CancellationToken.None);
                Assert.False(denied.IsSuccess);
                Assert.Equal("sessions.forbidden", denied.Error.Code);
                Assert.Equal(0, await db.Invoices.CountAsync(x => x.SessionId == started.Id));
            }

            var recoveryActor = new SessionActorContext(
                operatorId, correlation, "PostgreSQLTest",
                new HashSet<string>(
                    [Permissions.SessionsOperate, Permissions.SessionsTimingRecover, Permissions.BillingWrite],
                    StringComparer.Ordinal));
            var command = new ReconcileSessionTimingCommand(
                started.Id, started.Version, 30,
                "Verified play duration after Windows reboot", true,
                idempotencyPrefix + "authorized-recovery", recoveryActor);

            await using (var recoveryDb = CreateContext(connectionString))
            {
                var service = CreateService(recoveryDb, clock);
                var reconciled = await service.ReconcileTimingAsync(command, CancellationToken.None);
                Assert.True(reconciled.IsSuccess, reconciled.IsSuccess ? "" : reconciled.Error.Code);
                Assert.Equal(started.Id, reconciled.Value.SessionId);
                Assert.Equal(InvoiceStatusContract.PendingPayment, reconciled.Value.Status);
                Assert.Equal(TimeSpan.FromMinutes(30).Ticks, reconciled.Value.ElapsedTicks);
                Assert.Equal(30_000L, reconciled.Value.AmountToman);

                var replay = await service.ReconcileTimingAsync(command, CancellationToken.None);
                Assert.True(replay.IsSuccess, replay.IsSuccess ? "" : replay.Error.Code);
                Assert.Equal(reconciled.Value.InvoiceId, replay.Value.InvoiceId);
                Assert.Equal(reconciled.Value.AmountToman, replay.Value.AmountToman);
            }

            await using var verify = CreateContext(connectionString);
            var savedSession = await verify.Sessions.AsNoTracking().SingleAsync(x => x.Id == started.Id);
            Assert.Equal(SessionStatus.PendingSettlement, savedSession.Status);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, savedSession.ElapsedTicks);
            Assert.Equal(1, await verify.Invoices.CountAsync(x => x.SessionId == started.Id));
            var recoveryAudit = await verify.AuditEntries.AsNoTracking()
                .Where(x => x.CorrelationId == correlation && x.Operation == "sessions.timing.reconciled")
                .SingleAsync();
            Assert.Equal("Verified play duration after Windows reboot", recoveryAudit.Reason);
            Assert.Contains("\"operator_reconciliation\"", recoveryAudit.AfterJson ?? string.Empty);
            var operations = await verify.AuditEntries.AsNoTracking()
                .Where(x => x.CorrelationId == correlation)
                .Select(x => x.Operation)
                .ToArrayAsync();
            Assert.Contains("sessions.timing.reconciled", operations);
            Assert.Contains("billing.invoice_issued", operations);
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerId, correlation, idempotencyPrefix);
        }
    }

    private static async Task BindActiveAgentAsync(
        GameNetDbContext db,
        Station station,
        IGameClock clock,
        string token)
    {
        var deviceId = "pg-agent-" + token[..12];
        station.BindAgent(deviceId);
        db.AgentCredentials.Add(new AgentCredential
        {
            DeviceId = deviceId,
            SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test-agent-secret-" + token))),
            CreatedAtUtc = clock.UtcNow
        });
        db.AgentConnectionLeases.Add(new AgentConnectionLease
        {
            DeviceId = deviceId,
            ConnectionId = "pg-connection-" + token[..12],
            LeaseToken = Guid.NewGuid().ToString("N"),
            LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5),
            UpdatedAtUtc = clock.UtcNow,
            LastHeartbeatAtUtc = clock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static BillingService CreateBillingService(GameNetDbContext db, IGameClock clock)
    {
        var audit = new EfAuditWriter(db);
        return new BillingService(
            db,
            new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock)),
            new TestRequestFingerprint(),
            new SessionSettlement(db, audit, clock),
            new SessionCheckoutAccess(db),
            audit,
            clock);
    }

    [Fact]
    public async Task Pc_session_pause_resume_excludes_paused_time_even_across_windows_reboot()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "slice2-pause-resume-" + token;
        var idempotencyPrefix = "slice2-pause-resume-" + token + "-";
        Guid? customerId = null;
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));

        try
        {
            Guid sessionId;
            await using (var db = CreateContext(connectionString))
            {
                var station = Station.Create(stationId, "PR-" + token[..12], "Pause/resume PostgreSQL PC", StationType.Pc);
                var tariff = Tariff.CreateFixedHourly(
                    tariffId, "PR-" + token[..12], "Pause/resume tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow);
                var deviceId = "pause-agent-" + token[..12];
                station.BindAgent(deviceId);
                db.Stations.Add(station);
                db.Tariffs.Add(tariff);
                db.AgentCredentials.Add(new AgentCredential
                {
                    DeviceId = deviceId,
                    SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("pause-agent-secret-" + token))),
                    CreatedAtUtc = clock.UtcNow
                });
                db.AgentConnectionLeases.Add(new AgentConnectionLease
                {
                    DeviceId = deviceId,
                    ConnectionId = "pause-connection-" + token[..12],
                    LeaseToken = Guid.NewGuid().ToString("N"),
                    LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5),
                    UpdatedAtUtc = clock.UtcNow,
                    LastHeartbeatAtUtc = clock.UtcNow
                });
                await db.SaveChangesAsync();

                var service = CreateService(db, clock);
                var actor = Actor(operatorId, correlation);
                var started = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, idempotencyPrefix + "start", actor),
                    CancellationToken.None);
                Assert.True(started.IsSuccess, started.IsSuccess ? "" : started.Error.Code);
                sessionId = started.Value.Id;
                customerId = started.Value.CustomerId;

                clock.Advance(TimeSpan.FromMinutes(20));
                var pause = new PauseSessionCommand(sessionId, started.Value.Version, idempotencyPrefix + "pause", actor);
                var paused = await service.PauseAsync(pause, CancellationToken.None);
                Assert.True(paused.IsSuccess, paused.IsSuccess ? "" : paused.Error.Code);
                Assert.Equal(SessionStatusContract.Paused, paused.Value.Status);
                Assert.Equal(started.Value.Version + 1, paused.Value.Version);

                // Replaying the same mutation must not add the segment twice.
                var pauseReplay = await service.PauseAsync(pause, CancellationToken.None);
                Assert.True(pauseReplay.IsSuccess);
                Assert.Equal(paused.Value.Version, pauseReplay.Value.Version);
                var changedPauseIntent = await service.PauseAsync(
                    pause with { ExpectedVersion = started.Value.Version + 1 }, CancellationToken.None);
                Assert.False(changedPauseIntent.IsSuccess);
                Assert.Equal("idempotency.key_reused", changedPauseIntent.Error.Code);
                Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.Key == idempotencyPrefix + "pause"));

                var pausedRow = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
                Assert.Equal(TimeSpan.FromMinutes(20).Ticks, pausedRow.AccumulatedElapsedTicks);
                Assert.Null(pausedRow.CurrentSegmentStartedMonotonicTimestamp);

                // A paused Session continues to occupy the station and blocks a second start.
                var leaseDuringPause = await db.AgentConnectionLeases.SingleAsync(x => x.DeviceId == deviceId);
                leaseDuringPause.LastHeartbeatAtUtc = clock.UtcNow;
                leaseDuringPause.LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5);
                leaseDuringPause.UpdatedAtUtc = clock.UtcNow;
                await db.SaveChangesAsync();
                var secondStart = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, idempotencyPrefix + "start-during-pause", actor),
                    CancellationToken.None);
                Assert.False(secondStart.IsSuccess);
                Assert.Equal("sessions.station_has_active_session", secondStart.Error.Code);
                Assert.Equal(1, await db.Sessions.CountAsync(x => x.StationId == stationId));

                // A long paused interval and Windows boot change are deliberately excluded.
                clock.Advance(TimeSpan.FromMinutes(50));
                clock.SimulateWindowsReboot();
                var resume = await service.ResumeAsync(
                    new ResumeSessionCommand(sessionId, paused.Value.Version, idempotencyPrefix + "resume", actor),
                    CancellationToken.None);
                Assert.True(resume.IsSuccess, resume.IsSuccess ? "" : resume.Error.Code);
                Assert.Equal(SessionStatusContract.Active, resume.Value.Status);
                Assert.Equal(paused.Value.Version + 1, resume.Value.Version);

                var resumedRow = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
                Assert.Equal(TimeSpan.FromMinutes(20).Ticks, resumedRow.AccumulatedElapsedTicks);
                Assert.NotEqual(resumedRow.StartedBootIdentifier, resumedRow.CurrentSegmentStartedBootIdentifier!.Value);
                Assert.NotNull(resumedRow.CurrentSegmentStartedMonotonicTimestamp);

                clock.Advance(TimeSpan.FromMinutes(10));
                var ended = await service.EndAsync(
                    new EndSessionCommand(sessionId, resume.Value.Version, idempotencyPrefix + "end", actor),
                    CancellationToken.None);
                Assert.True(ended.IsSuccess, ended.IsSuccess ? "" : ended.Error.Code);
                Assert.Equal(InvoiceStatusContract.PendingPayment, ended.Value.Status);
                Assert.Equal(TimeSpan.FromMinutes(30).Ticks, ended.Value.ElapsedTicks);
                Assert.Equal(30_000L, ended.Value.AmountToman);

                var finalRow = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
                Assert.Equal(SessionStatus.PendingSettlement, finalRow.Status);
                Assert.Equal(TimeSpan.FromMinutes(30).Ticks, finalRow.AccumulatedElapsedTicks);
                Assert.Equal(finalRow.ElapsedTicks, finalRow.AccumulatedElapsedTicks);
                Assert.Null(finalRow.CurrentSegmentStartedMonotonicTimestamp);
                Assert.Equal(3, await db.AuditEntries.CountAsync(x =>
                    x.CorrelationId == correlation &&
                    (x.Operation == "sessions.pause" || x.Operation == "sessions.resume" || x.Operation == "sessions.end")));
            }
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerId, correlation, idempotencyPrefix);
        }
    }

    [Fact]
    public async Task Pc_session_end_while_paused_invoices_only_accumulated_active_time_after_reboot()
    {
        if (!IsExplicitlyEnabled()) return;

        var connectionString = GetConnectionString();
        var token = Guid.NewGuid().ToString("N");
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var tariffId = Guid.NewGuid();
        var correlation = "slice2-paused-end-" + token;
        var idempotencyPrefix = "slice2-paused-end-" + token + "-";
        Guid? customerId = null;
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddHours(-1));

        try
        {
            Guid sessionId;
            await using (var db = CreateContext(connectionString))
            {
                var station = Station.Create(stationId, "PE-" + token[..12], "Paused-end PostgreSQL PC", StationType.Pc);
                var tariff = Tariff.CreateFixedHourly(
                    tariffId, "PE-" + token[..12], "Paused-end tariff",
                    StationTypeContract.Pc, 60_000L, false, clock.UtcNow);
                var deviceId = "paused-end-agent-" + token[..12];
                station.BindAgent(deviceId);
                db.Stations.Add(station);
                db.Tariffs.Add(tariff);
                db.AgentCredentials.Add(new AgentCredential
                {
                    DeviceId = deviceId,
                    SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("paused-end-secret-" + token))),
                    CreatedAtUtc = clock.UtcNow
                });
                db.AgentConnectionLeases.Add(new AgentConnectionLease
                {
                    DeviceId = deviceId,
                    ConnectionId = "paused-end-connection-" + token[..12],
                    LeaseToken = Guid.NewGuid().ToString("N"),
                    LeaseExpiresAtUtc = clock.UtcNow.AddMinutes(5),
                    UpdatedAtUtc = clock.UtcNow,
                    LastHeartbeatAtUtc = clock.UtcNow
                });
                await db.SaveChangesAsync();

                var service = CreateService(db, clock);
                var actor = Actor(operatorId, correlation);
                var started = await service.StartAsync(
                    new StartSessionCommand(stationId, tariffId, null, idempotencyPrefix + "start", actor),
                    CancellationToken.None);
                Assert.True(started.IsSuccess, started.IsSuccess ? "" : started.Error.Code);
                sessionId = started.Value.Id;
                customerId = started.Value.CustomerId;

                clock.Advance(TimeSpan.FromMinutes(17));
                var paused = await service.PauseAsync(
                    new PauseSessionCommand(sessionId, started.Value.Version, idempotencyPrefix + "pause", actor),
                    CancellationToken.None);
                Assert.True(paused.IsSuccess, paused.IsSuccess ? "" : paused.Error.Code);
                Assert.Equal(SessionStatusContract.Paused, paused.Value.Status);

                // End while paused must not try to reconstruct paused time from clock or boot evidence.
                clock.Advance(TimeSpan.FromHours(4));
                clock.SimulateWindowsReboot();
                var ended = await service.EndAsync(
                    new EndSessionCommand(sessionId, paused.Value.Version, idempotencyPrefix + "end", actor),
                    CancellationToken.None);

                Assert.True(ended.IsSuccess, ended.IsSuccess ? "" : ended.Error.Code);
                Assert.Equal(paused.Value.Version + 1, ended.Value.SessionVersion);
                Assert.Equal(TimeSpan.FromMinutes(17).Ticks, ended.Value.ElapsedTicks);
                Assert.Equal(17_000L, ended.Value.AmountToman);
                Assert.Equal(InvoiceStatusContract.PendingPayment, ended.Value.Status);

                var row = await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
                Assert.Equal(SessionStatus.PendingSettlement, row.Status);
                Assert.Equal(TimeSpan.FromMinutes(17).Ticks, row.AccumulatedElapsedTicks);
                Assert.Equal(row.ElapsedTicks, row.AccumulatedElapsedTicks);
                Assert.Null(row.CurrentSegmentStartedMonotonicTimestamp);
                Assert.Single(await db.Invoices.Where(x => x.SessionId == sessionId).ToArrayAsync());
            }
        }
        finally
        {
            await CleanupAsync(connectionString, stationId, tariffId, customerId, correlation, idempotencyPrefix);
        }
    }

    private static BillingActorContext BillingActor(Guid operatorId, string correlation) =>
        new(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.BillingWrite, Permissions.BillingCashReverse], StringComparer.Ordinal));

    private static SessionService CreateService(GameNetDbContext db, IGameClock clock)
    {
        var audit = new EfAuditWriter(db);
        return new SessionService(
            db,
            new CommandMutationPipeline(new EfTransactionCoordinator(db), new EfIdempotencyStore(db, clock)),
            new TestRequestFingerprint(),
            new CustomerSessionAccess(db),
            new StationSessionAccess(db, clock),
            new TariffSessionAccess(db),
            new BillingInvoiceWriter(db, audit, clock),
            audit,
            clock);
    }

    private static SessionActorContext Actor(Guid operatorId, string correlation) =>
        new(operatorId, correlation, "PostgreSQLTest",
            new HashSet<string>([Permissions.SessionsOperate, Permissions.BillingWrite], StringComparer.Ordinal));

    private static GameNetDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new GameNetDbContext(options);
    }

    private static bool IsExplicitlyEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("GAMENET_SESSION_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    private static string GetConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static async Task CleanupAsync(
        string connectionString,
        Guid stationId,
        Guid tariffId,
        Guid? customerId,
        string correlation,
        string idempotencyPrefix)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit_entries WHERE correlation_id = {correlation}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing_ledger_entries WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_sale_reversals WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_tender_corrections WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments WHERE invoice_id IN (SELECT id FROM invoices WHERE station_id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM invoices WHERE station_id = {stationId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM sessions WHERE station_id = {stationId}");
        if (customerId.HasValue)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM customers WHERE id = {customerId.Value}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_connection_leases WHERE device_id IN (SELECT agent_device_id FROM stations WHERE id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agent_credentials WHERE device_id IN (SELECT agent_device_id FROM stations WHERE id = {stationId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tariffs WHERE id = {tariffId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM stations WHERE id = {stationId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM idempotency_records WHERE key LIKE {idempotencyPrefix + "%"}");
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IGameClock
    {
        private long timestamp;
        private long uptimeMilliseconds = 5_000;
        private Guid bootId = Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0");

        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public GameClockSnapshot Capture() => new(
            UtcNow, timestamp, TimeSpan.TicksPerSecond, uptimeMilliseconds, bootId);

        public void Advance(TimeSpan amount)
        {
            UtcNow = UtcNow.Add(amount);
            timestamp = checked(timestamp + amount.Ticks);
            uptimeMilliseconds = checked(uptimeMilliseconds + (long)amount.TotalMilliseconds);
        }

        public void JumpWallClock(TimeSpan amount) => UtcNow = UtcNow.Add(amount);

        public void SimulateWindowsReboot()
        {
            timestamp = 1_000;
            uptimeMilliseconds = 5_000;
            bootId = Guid.NewGuid();
        }
    }

    private sealed class TestRequestFingerprint : IRequestFingerprint
    {
        public string Compute(object payload) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }
}
