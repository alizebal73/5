# Sessions Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The Session → invoice → cash path is being extended from PC to PS5/foosball timed stations under [Issue #133](https://github.com/alizebal73/5/issues/133), reusing the same Server-authoritative lifecycle and financial pipeline. The Sessions module is not complete; Issue #98 acceptance and physical/manual evidence are reserved for the final installed release candidate. The current queue is in [Active Execution Control](../../../../docs/operations/active-execution-control.md).

## Ownership

Own authoritative Session state, Station/Customer binding, lifecycle and billable timing. Billing owns invoices and payments; Tariffs owns tariff definitions; the UI never owns timing or billing truth.

## Implemented in the current slice

- Server-authorized timed Session start/end for PC (with authoritative Agent readiness required) and the Issue #133 candidate for PS5/foosball (without inventing an Agent). Pause/resume is implemented on the parent candidate under Issue #129 / PR #130.
- Walk-in customer binding when the operator does not select an existing customer.
- Immutable hourly-tariff snapshot for the Session and invoice creation at end.
- Transaction, idempotency, audit and PostgreSQL regression coverage for the supported path.
- Timing remains Server-side monotonic elapsed time with an immutable fixed-hourly tariff snapshot; PS5/foosball must use a tariff whose type exactly matches the station, then use the existing invoice/cash path. No Agent is fabricated for non-PC stations.
- Candidate Issue #129 / PR #130 adds accumulated monotonic billable ticks, persisted segment baselines, audited pause/resume and ending a paused Session without charging the paused interval, including across a Windows boot change. It remains subject to exact-head tests/review and final installed acceptance.
- Issue #135 is implementing audited same-type reassignment of Active/Paused Sessions. The Server preserves the session/customer/tariff snapshots, accumulated billable ticks and active segment baseline; it does not reprice or create a bill/payment. Destination occupancy and PC Agent readiness are validated in the authoritative transaction. This operation changes the Server-side station assignment only; it does not lock/unlock a PC, move a Windows user/game process or issue Agent handoff commands. Those operating-system/control semantics remain outside this slice and cannot be implied complete by an assignment update. Tests and the WPF reassignment form are on a feature branch pending exact-head CI.

## Incomplete

Extend/reduce, operator release/forced termination, full customer multi-device limits, richer PS5/foosball pricing (controller/person/add-ons), tariff time bands/free-time/VIP packages and full recovery remain future work. Issue #133 non-PC Sessions, Issue #129 pause/resume and Issue #135 transfer are candidates until exact-head automation passes and the dependent code is merged. Installed-Windows/WPF/LAN evidence is intentionally postponed to the final real Setup acceptance; Session timing is not declared production-ready before that gate.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL and concurrency/regression tests with the behavior being changed.
- Sensitive mutations must use the required transaction, idempotency and audit boundaries; durable external effects use Outbox after commit.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
