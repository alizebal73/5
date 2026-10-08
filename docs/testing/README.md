# Testing Strategy

Unit -> domain rules.
Integration -> PostgreSQL constraints, queries, migrations and transactions.
Concurrency -> real PostgreSQL idempotency, ownership and fencing.
Contract -> Shared wire compatibility.
Desktop -> native launch/localization/navigation/API boundary.
Agent -> service lifecycle/reconnect/execution/reconciliation.
E2E -> critical operator journeys.
Certification -> migration, upgrade, backup/restore, recovery, update/rollback.

Mocks isolate units only. They never replace real-environment evidence.
