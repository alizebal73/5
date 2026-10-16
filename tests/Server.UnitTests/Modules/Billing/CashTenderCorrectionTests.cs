using GameNet.Server.Modules.Billing.Domain;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Billing;

public sealed class CashTenderCorrectionTests
{
    [Fact]
    public void Correction_preserves_previous_facts_and_derives_change_from_unchanged_applied_amount()
    {
        var correction = CashTenderCorrection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            version: 1,
            previousAmountReceivedToman: 50_000L,
            previousChangeGivenToman: 20_000L,
            amountAppliedToman: 30_000L,
            correctedAmountReceivedToman: 45_000L,
            correctedByOperatorId: Guid.NewGuid(),
            reason: "Cash received was keyed incorrectly",
            correlationId: "correlation-cash-correction",
            idempotencyKey: Guid.NewGuid().ToString("N"),
            correctedAtUtc: DateTimeOffset.Parse("2026-10-16T10:00:00Z"));

        Assert.Equal(1, correction.Version);
        Assert.Equal(50_000L, correction.PreviousAmountReceivedToman);
        Assert.Equal(20_000L, correction.PreviousChangeGivenToman);
        Assert.Equal(45_000L, correction.CorrectedAmountReceivedToman);
        Assert.Equal(15_000L, correction.CorrectedChangeGivenToman);
        Assert.Equal(30_000L, 45_000L - correction.CorrectedChangeGivenToman);
    }

    [Fact]
    public void Correction_rejects_received_amount_below_original_applied_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CashTenderCorrection.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            50_000L, 20_000L, 30_000L, 29_999L, Guid.NewGuid(),
            "Correct wrong cash", "correlation", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Correction_rejects_previous_tender_facts_that_do_not_reconcile()
    {
        Assert.Throws<ArgumentException>(() => CashTenderCorrection.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            50_000L, 19_999L, 30_000L, 45_000L, Guid.NewGuid(),
            "Correct wrong cash", "correlation", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Correction_rejects_invalid_version_reason_and_control_characters()
    {
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");
        var time = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() => CashTenderCorrection.Create(
            Guid.NewGuid(), invoiceId, paymentId, 0, 50_000L, 20_000L, 30_000L, 45_000L,
            actorId, "Correct wrong cash", "correlation", key, time));
        Assert.Throws<ArgumentException>(() => CashTenderCorrection.Create(
            Guid.NewGuid(), invoiceId, paymentId, 1, 50_000L, 20_000L, 30_000L, 45_000L,
            actorId, "  ", "correlation", key, time));
        Assert.Throws<ArgumentException>(() => CashTenderCorrection.Create(
            Guid.NewGuid(), invoiceId, paymentId, 1, 50_000L, 20_000L, 30_000L, 45_000L,
            actorId, "Bad\nreason", "correlation", key, time));
    }

    [Fact]
    public void Correction_checks_overflow_while_deriving_change()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CashTenderCorrection.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            long.MaxValue, long.MaxValue - 1, 1L, long.MaxValue, Guid.NewGuid(),
            "Correct received amount", "correlation", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow));
    }
}