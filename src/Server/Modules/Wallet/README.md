# Wallet Module Boundary

**Status:** The purchased-value cash top-up slice is under development on feature/wallet-cash-topup-20261019. Do not describe it as accepted until exact-SHA CI and the PostgreSQL/financial reconciliation tests pass.

## Ownership

Own purchased stored value, free money, free time and debt ledgers plus reconciled balance projections. Direct balance setting is forbidden except an audited, authorized correction. Pending invoice/settlement is not Debt.

## Current bounded slice

- Operator cash top-up for a registered, active, unblocked customer.
- Billing-owned immutable cash receipt records credited value, physically received cash and change.
- Wallet-owned immutable ledger entry and balance projection update atomically with the cash receipt, idempotency record and Audit.
- Whole-Toman signed 64-bit amounts only; excess tender is change, never extra credit.
- Wallet ledger and receipt rows reject update/delete; corrections must be compensating, separately authorized operations.
- Desktop persists the exact unresolved top-up request and idempotency key in the current Windows user's LocalAppData before sending money-mutating HTTP. It stores no credential. An unreadable recovery file or a pending request from another operator fails closed; only the original operator can replay the same request and clear the marker.

## Explicitly not implemented by this slice

Customer PIN/login, wallet spending against Sessions, multi-device policy, automatic countdown, PC locking/control, free credit/time, debt, discounts/VIP, card/terminal, refunds and physical/manual acceptance remain separate capabilities. A green build alone does not accept the feature.

## Implementation rules

- Place business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contract-bound endpoints.
- Keep EF/DbContext out of Domain/Application/Api; never reach into another module's implementation namespace.
- Financial mutations use the shared transaction, idempotency and Audit boundary.
- Balance projection must equal the sum of immutable Wallet ledger movements; PostgreSQL tests reconcile after writes and concurrent top-ups.
