# Tariffs Module Boundary

**Status: PARTIALLY IMPLEMENTED.** The first PC slice supports active fixed-hourly PC tariff listing/creation and snapshots the selected rate into a Session. Advanced tariffs and all station-type pricing are not complete.

## Ownership

Own tariff definitions, validity/matching rules and immutable tariff snapshots attached to Sessions/bills. Invalid or overlapping tariffs must be rejected. Tariffs does not collect payments or own Wallet balances.

## Implemented in the current slice

- Active fixed-hourly PC tariff list and creation API.
- Station-type compatibility checks for the first PC Session flow.
- Persisted hourly-rate snapshot consumed by billing, with regression coverage showing later tariff edits do not change an existing Session's rate.

## Incomplete

Time-of-day/day-of-week/holiday pricing, groups/person-count rules, custom promotion/discount/VIP eligibility, comprehensive overlap/validity management, and PS5/foosball tariff workflows remain future work.

## Implementation rules

- Implement through a requirement-tracked slice using [Definition of Ready](../../../../docs/planning/definition-of-ready.md).
- Keep business rules in Domain/Application; use Infrastructure for persistence/integrations and Api for contracts/endpoints.
- Keep EF/DbContext out of Domain/Application/Api; do not reach into another module's implementation namespace.
- Add unit, PostgreSQL and concurrency/regression tests with the behavior being changed.
- Financial/privileged mutations require the appropriate transaction, idempotency and audit boundaries.
- Do not add placeholder endpoints or successful fake responses. Apply [Definition of Done](../../../../docs/planning/definition-of-done.md) before claiming a slice complete.
