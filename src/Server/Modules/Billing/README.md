# Billing Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The first PC slice creates a fixed-hourly invoice and supports cash checkout with persisted Payment, ledger and Audit records. The module and the overall Session-to-cash journey are not yet accepted; see [Issue #98](https://github.com/alizebal73/5/issues/98). The current queue is in [Active Execution Control](../../../../docs/operations/active-execution-control.md).

## Ownership

Own immutable invoices, payment records/allocation, pending settlement, cash checkout and audited correction/reversal workflows. Actual received cash/card payments are distinct from internal Wallet transfers. Wallet owns stored value and debt balances; Billing must not silently treat them as cash received.

## Implemented in the current slice

- Fixed-hourly Session invoice and pending-settlement state.
- Cash-only checkout with checked integer-Toman arithmetic.
- Payment, paid Invoice/Session state, append-only ledger entry and Audit written transactionally.
- Idempotency, rollback and PostgreSQL regression coverage for core persisted invariants.
- Durable invoice-based checkout outcome lookup and recent completed checkout history, with automated coverage for reconciliation and simulated lost responses; manual operator acceptance remains outstanding.

## Incomplete

Full cash-sale reversal is being implemented under the bounded V1 contract, with dedicated permission, mandatory reason, idempotency, a forward-only migration and a compensating typed ledger entry. It remains unaccepted for operational use until exact-SHA gates, UI acceptance and independent financial reconciliation are complete. Cash tender correction is specified but not implemented. Card payments, Wallet/debt, partial allocations and manual WPF/database reconciliation remain open. The overall first Session-to-cash journey is not accepted for real-money use.

The requirements are in [Cash Correction and Reversal V1](../../../../docs/domain/cash-correction-and-reversal-v1.md). This implementation is a separate narrow slice; tender correction remains a later task.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL, idempotency and concurrency/regression tests with the behavior being changed.
- Financial/privileged mutations must use transaction, idempotency and audit boundaries; ledger history is append-only and corrections must be compensating/audited.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
