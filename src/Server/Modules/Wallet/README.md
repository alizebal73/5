# Wallet Module Boundary

**Status: CASH TOP-UP CANDIDATE IMPLEMENTED; exact-head validation and release acceptance remain open.** Tracked by [Issue #137](https://github.com/alizebal73/5/issues/137) and [PR #138](https://github.com/alizebal73/5/pull/138) once the PR is created. Do not treat this candidate as merged or ready for real-money operation.

## Ownership

Wallet owns purchased stored value, free money, free time, debt ledgers and reconciled balance projections. This first slice handles purchased-value cash top-up only. It does not implement free/gift balances, free time, debt, Session debit, customer PIN/login or multi-device limits. Pending invoice/settlement is not Debt.

## Candidate cash top-up flow

- Only an active, registered Customer may be topped up; Walk-in, inactive and blocked records are rejected by the Server.
- The operator supplies two independent integer-Toman values: amount to credit and actual cash received. Cash received must be at least the credit amount; change is calculated by Server arithmetic.
- Billing owns the append-only actual-cash receipt; Wallet owns the immutable CashTopUp ledger movement and the purchased-balance projection.
- The receipt, ledger movement, balance projection, idempotency record and Audit participate in the same Server-authoritative transaction. Same-key retries reuse the original outcome; a changed payload with that key conflicts.
- The projection must reconcile to the signed immutable Wallet ledger. Editing/deleting Wallet movements or cash-receipt facts through EF is explicitly guarded.

## Implementation rules

- Implement only through a requirement-tracked slice that passes [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Place business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contract-bound endpoints.
- Keep EF/DbContext out of Domain/Application/Api; use module-owned contracts at cross-module boundaries.
- Keep all amounts as checked integer Toman; never treat additional cash as additional Wallet credit or record Wallet transfers as cash receipts.
- Add domain, PostgreSQL, authorization, idempotency, concurrency, restore/reconciliation, and Desktop contract tests alongside behavior changes.
- Physical/manual acceptance and real Setup remain deferred to the owner's final release gate. Do not test this candidate on the live shop database.
- Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming the slice complete.
