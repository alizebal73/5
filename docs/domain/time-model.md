# Authoritative Time Model

## Wall-clock time (UTC)

Persist UTC timestamps for durable facts: Session start/end, invoice issue, payment receipt, Audit entries, and operational records. Business timezone is configured separately for tariff calendars, reports, operator display, and daily settlement boundaries.

UTC event timestamps do not define elapsed Session duration or the order of Session/payment state transitions. System clock corrections can make a later event's UTC value earlier than a prior event's timestamp; state transitions and transactions define the business order.

## Billable duration

Billable Session duration uses the Server's monotonic TimeProvider timestamp, not subtraction of two mutable UTC timestamps.

At Session start, persist:
- the UTC event timestamp for audit/display;
- TimeProvider.GetTimestamp() and its TimestampFrequency;
- Environment.TickCount64 as an independent monotonic continuity signal;
- the immutable Windows boot identifier.

At Session end, the Server verifies the timestamp frequency is unchanged, the monotonic timestamp advanced, the system uptime did not reset, the Windows boot identifier matches the persisted start value, and elapsed time is consistent with the independent uptime counter within a small tolerance. It then persists positive elapsed TimeSpan ticks on the Session. Billing calculates integer-Toman charges only from those persisted elapsed ticks and the immutable hourly-rate snapshot. UTC is never an input to the money calculation.

This supports a Server/service process restart while Windows remains in the same boot session. Forward/backward UTC changes do not affect the amount if monotonic continuity remains verifiable. The Windows boot identifier also makes OS reboot discontinuity detectable.

## Fail-closed recovery

If timestamp frequency/counter, uptime continuity, or boot identity cannot be verified, the Server must not substitute UTC subtraction, create an invoice, or estimate an amount. The end request fails with sessions.timing_recovery_required and the mutation transaction leaves Session/invoice/financial state unchanged. The migration refuses to apply while any active Session exists, so deployment cannot strand a live Session whose old row has no monotonic baseline. Before upgrading, the previous application must end/reconcile all active Sessions. For older completed or pending-settlement Sessions, the monotonic baseline is sentinel data, while the already-persisted invoice duration is backfilled and never recomputed.

The timing model does not guess elapsed time across an OS reboot. Recovery policy for a Session that spans machine restart remains a controlled operator/reconciliation workflow to specify before operational acceptance. Do not infer that automated process tests prove installed Windows Service, a real Windows reboot, or pilot readiness.

## Ownership and tests

The Server owns the start snapshot, final billable duration, recovery decision, and invoice amount. Agent-reported timestamps are evidence only. Tests cover normal duration, forward/backward wall-clock changes, boot identity/counter discontinuity, invalid duration, duplicate command, concurrent end/settlement, and independent Session/Invoice/Payment/Ledger/Audit reconciliation.
