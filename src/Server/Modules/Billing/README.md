# Billing Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The first PC slice creates a fixed-hourly invoice and supports cash checkout with persisted Payment, ledger and Audit records. The module and the overall Session-to-cash journey are not yet accepted; see [Issue #98](https://github.com/alizebal73/5/issues/98). The current queue is in [Active Execution Control](../../../../docs/operations/active-execution-control.md).

## Ownership

Own immutable invoices, payment records/allocation, pending settlement, cash checkout, invoice-related correction/reversal, and actual cash receipts for non-invoice sales. Wallet top-up uses the Billing-owned CashReceipt record; it is deliberately not a Session invoice Payment or a Wallet balance. This candidate only accepts cash; card/terminal, split payment and broader cash-receipt purposes remain unimplemented.

## Implemented in the current slice

- Fixed-hourly Session invoice and pending-settlement state.
- Cash-only checkout with checked integer-Toman arithmetic.
- Payment, paid Invoice/Session state, append-only ledger entry and Audit written transactionally.
- Idempotency, rollback and PostgreSQL regression coverage for core persisted invariants.
- Durable invoice-based checkout outcome lookup and recent completed checkout history, with automated coverage for reconciliation and simulated lost responses; manual operator acceptance remains outstanding.

## Incomplete

The current integrated implementation provides full cash-sale reversal through the Server and operator WPF workflow under the bounded V1 contract. The Wallet cash top-up candidate tracked by [Issue #137](https://github.com/alizebal73/5/issues/137) adds a distinct Billing-owned actual-cash receipt that links to one Wallet ledger movement; it does not create a Session invoice or imply that Wallet features are fully accepted. It has dedicated permission, mandatory reason and confirmation, idempotency, a forward-only migration, an append-only reversal record and a compensating typed ledger entry. PR #120 was squash-merged to Integration at `0660e632f0e38ec1654a2830f633e2f3cd60ae5f`; its exact PR-head automated gates passed, and post-merge Quick Validation passed. The feature remains unaccepted for operational use until manual WPF/recovery acceptance and independent financial reconciliation are complete. Cash tender correction is specified but not implemented. Card payments, Wallet/debt, partial allocations and the overall first Session-to-cash acceptance remain open.

The requirements are in [Cash Correction and Reversal V1](../../../../docs/domain/cash-correction-and-reversal-v1.md). This implementation is a separate narrow slice; tender correction remains a later task.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL, idempotency and concurrency/regression tests with the behavior being changed.
- Financial/privileged mutations must use transaction, idempotency and audit boundaries; ledger history is append-only and corrections must be compensating/audited.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
