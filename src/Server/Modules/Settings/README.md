# Settings Module Boundary

**Status:** Structure/ownership only; no business behavior is claimed complete.

Own validated, versioned Server-backed operational settings and policy revisions. The owning domain module still validates its own rules; network actions execute through ClientControl/Agent only when supported.

## Implementation rules

- Implement only through a requirement-tracked slice that passes Definition of Ready.
- Place business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contract-bound endpoints.
- Keep EF/DbContext out of Domain/Application/Api; never reach into another module's implementation namespace.
- Add tests under root `tests/` projects in a module-specific path. Add PostgreSQL/concurrency tests for persisted invariants.
- Financial, inventory, Session and privileged mutations must use the required transaction, idempotency and audit boundaries; durable external effects use Outbox after commit.
- Do not add placeholder endpoints, successful fake responses, dummy projects or unneeded interfaces just to populate folders.
