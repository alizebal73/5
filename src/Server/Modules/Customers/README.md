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

Customer username/password login and the two approved customer entry paths are a separate capability slice; PIN and a third customer-entry mode are not authorized. Dashboard-side PC→customer-ID assignment and client-side username/password login must resolve to one server-authoritative CustomerLogin/Session ownership model. Client and Manager Dashboard are separate product/security surfaces; the UI never owns Session/wallet/time calculations. Owner-configurable client unlock/release permissions are a separate station-access-control use case (Issue #152), not customer login. Multi-device session policy, Wallet/debt/Gift balances, VIP plan/history, customer statements and full account-activity projections remain incomplete. Do not duplicate financial/session state into the Customers table.

## Implementation rules

- Keep business rules in Domain; Infrastructure owns persistence and integrations; Api owns HTTP boundaries.
- Desktop/Agent never connect directly to PostgreSQL.
- Add domain, PostgreSQL, permission, idempotency and concurrency tests with each behavior change.
- Sensitive mutations require server-side authorization, transactional idempotency and Audit.
- Do not describe a candidate as accepted until exact-SHA CI and required acceptance evidence are recorded.
