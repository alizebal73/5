# Customers Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The first PC slice can create/bind a Walk-in customer when no existing Customer is selected. This is not completion of the customer profile, PIN or customer-management workflows.

## Ownership

Own stable customer identity/code, profile, optional customer PIN, activation/deactivation and profile history. PINs must be stored only as salted hashes and never returned. Customers does not own Wallet/debt, Session lifecycle or Station state.

## Implemented in the current slice

- Walk-in customer creation/binding needed by the first PC Session flow.
- Persisted identity binding and regression coverage proving that Session and invoice views resolve the authoritative Customer/Station associations.

## Incomplete

Operator-facing customer search/profile editing, PIN lifecycle, activation/deactivation, profile history, Wallet/debt presentation, login/multi-device policy and the complete customer management UI remain future work.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL and concurrency/regression tests with the behavior being changed.
- Sensitive mutations require appropriate permissions, transaction/idempotency and audit.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
