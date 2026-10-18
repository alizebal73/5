# Vertical Slice Record

## Identity
- Slice ID:
- Related REQ IDs:
- Owner module:
- Target release:
- Baseline branch/SHA:
- Status: PROPOSED / BLOCKED / READY / IN_PROGRESS / FIXED_UNVERIFIED / VERIFIED / CLOSED

## User outcome and boundaries
- User/operator problem:
- Expected outcome:
- In scope:
- Out of scope:
- Acceptance criteria:

## Ownership, state and contracts
- Authoritative source:
- Owning module/layer:
- Aggregates and invariants:
- State machine/preconditions:
- Desktop → Server contract:
- Agent ↔ Server contract:
- Shared V1/versioning:
- Cross-module contracts/events:

## Security and reliability
- Actors and permissions:
- Approval/Audit requirements:
- Threats/sensitive data:
- Transaction boundary:
- Idempotency key scope/request-hash:
- Concurrency/database constraints:
- Failure/timeout/retry:
- Restart/reconnect/recovery/reconciliation:
- Observability/correlation:

## Data and release
- Schema/migration:
- Historical data/backfill:
- API/Agent compatibility:
- Install/update/rollback:
- Safe rollback/forward-fix:
- Known risks/open decisions:

## Verification plan
- [ ] Unit/domain tests
- [ ] PostgreSQL integration/migration tests
- [ ] Permission and audit tests
- [ ] Idempotency/concurrency tests
- [ ] Contract tests
- [ ] Desktop/Agent tests, when relevant
- [ ] E2E operator journey
- [ ] Failure/recovery test
- [ ] Performance test, when relevant
- [ ] Quick/canonical/Foundation gate
- [ ] Physical/network test, when relevant

## Results and closure
- Regression tests and result:
- CI/workflow URLs:
- Exact tested SHA:
- Runtime evidence/artifacts:
- Operator acceptance:
- Docs/traceability updates:
- Remaining risks:
- DoD decision/reviewer:
