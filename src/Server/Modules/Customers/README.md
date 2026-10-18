# Customers Module Boundary

**Status:** Customer profile core is implemented on candidate branch `feature/customers-core-20261016`; exact-head CI and protected PR review remain pending.

## Ownership

Own registered customer identity/code, profile, optional nickname and phone, activation/block status, and profile writes. Customers does not own Wallet/debt, Session lifecycle or Station state.

## Implemented in this candidate slice

- Create and bounded search/list of registered customers with Server-side authorization.
- Edit name, nickname and optional phone with optimistic concurrency (`ExpectedVersion`).
- Activate, deactivate or block a customer with mandatory reason and Audit.
- Durable API contracts, PostgreSQL migration, request idempotency and audit events.
- Select a registered active customer when starting a PC Session; inactive/blocked customers cannot start a new Session. Walk-in behavior remains available when no customer is selected.
- Domain tests and an opt-in PostgreSQL lifecycle/idempotency/audit regression.

## Incomplete by design

Customer PIN/login, multi-device session policy, Wallet/debt/Gift balances, VIP plan/history, customer statements and full account-activity projections are not part of this slice. Do not duplicate financial/session state into the Customers table.

## Implementation rules

- Keep business rules in Domain; Infrastructure owns persistence and integrations; Api owns HTTP boundaries.
- Desktop/Agent never connect directly to PostgreSQL.
- Add domain, PostgreSQL, permission, idempotency and concurrency tests with each behavior change.
- Sensitive mutations require server-side authorization, transactional idempotency and Audit.
- Do not describe a candidate as accepted until exact-SHA CI and required acceptance evidence are recorded.
