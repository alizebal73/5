# Billing Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The first PC slice creates a fixed-hourly invoice and supports cash checkout with persisted Payment, ledger and Audit records. The module and the overall Session-to-cash journey are not yet accepted; see [Issue #98](../../../../docs/operations/active-execution-control.md).

## Ownership

Own immutable invoices, payment records/allocation, pending settlement, cash checkout and audited correction/reversal workflows. Actual received cash/card payments are distinct from internal Wallet transfers. Wallet owns stored value and debt balances; Billing must not silently treat them as cash received.

## Implemented in the current slice

- Fixed-hourly Session invoice and pending-settlement state.
- Cash-only checkout with checked integer-Toman arithmetic.
- Payment, paid Invoice/Session state, append-only ledger entry and Audit written transactionally.
- Idempotency, rollback and PostgreSQL regression coverage for core persisted invariants.

## Incomplete

Card payments, Wallet/debt, partial allocations, refund/reversal/correction workflows, durable display/recovery of a payment committed before its HTTP response was lost, and manual WPF/database reconciliation remain open. Cash entry must require deliberate operator input; do not treat the current flow as ready for real-money use.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL, idempotency and concurrency/regression tests with the behavior being changed.
- Financial/privileged mutations must use transaction, idempotency and audit boundaries; ledger history is append-only and corrections must be compensating/audited.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
