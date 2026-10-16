# Sessions Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The first PC walk-in slice includes Session start/end, Station and Customer binding, a tariff snapshot, and the transition into pending settlement. The full Sessions module is not complete; Slice 1 acceptance is tracked in [Issue #98](https://github.com/alizebal73/5/issues/98). The current queue is in [Active Execution Control](../../../../docs/operations/active-execution-control.md).

## Ownership

Own authoritative Session state, Station/Customer binding, lifecycle and billable timing. Billing owns invoices and payments; Tariffs owns tariff definitions; the UI never owns timing or billing truth.

## Implemented in the current slice

- Server-authorized PC Session start, pause/resume candidate, and end with persisted state and concurrency protection.
- Walk-in customer binding when the operator does not select an existing customer.
- Immutable hourly-tariff snapshot for the Session and invoice creation at end.
- Transaction, idempotency, audit and PostgreSQL regression coverage for the supported path.
- Candidate Issue #129 adds accumulated monotonic billable ticks, a persisted current-segment baseline, audited pause/resume commands, and an end path that excludes the paused interval, including pause across a Windows boot change. This candidate still requires exact-head CI and manual acceptance.

## Incomplete

Extend/reduce/transfer/release/cancel, full customer multi-device limits, and the complete station-type timing rules remain future work. Candidate pause/resume is tracked in [Issue #129](https://github.com/alizebal73/5/issues/129) and is not merged or accepted until exact-head tests and runtime/manual evidence pass. The broader clock/recovery contract also needs physical installed-Windows acceptance before Session timing can be declared production-ready.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL and concurrency/regression tests with the behavior being changed.
- Sensitive mutations must use the required transaction, idempotency and audit boundaries; durable external effects use Outbox after commit.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
