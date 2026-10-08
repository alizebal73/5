# Bug-Fix Log

Every defect records symptom, root cause, affected boundary, fix, regression evidence, verification result and rollback reference.

A defect is not closed because a later refactor hides it.

## 2026-10-08 — Repo 2/3 regression controls carried into GameNet 5

### Repo 2 failure pattern
The product became unstable because business ownership, UI/runtime, persistence and release concerns accumulated in broad files and overlapping authorities. The final state still contained production-path mock data and required late security/ownership fixes. The release/install boundary was also validated too late.

Permanent Repo 5 controls:
- module ownership is explicit;
- PostgreSQL is the production authority;
- no production browser/dashboard runtime;
- no production mocks/fakes;
- source-size guard blocks giant files;
- Domain/Application/API cannot access EF directly;
- financial/inventory/session mutations require transaction/idempotency/audit boundaries;
- installer/update/recovery are separate release boundaries;
- real runtime evidence is required before release claims.

### Repo 3 failure pattern
Business work started before Foundation had a truthful runtime certification checkpoint. The first Stations slice then introduced several boundary regressions: direct repository SaveChanges instead of the transaction coordinator, endpoint exception-message matching, missing real authorization/idempotency/audit behavior, and runtime/schema verification gaps.

Permanent Repo 5 controls:
- business implementation is blocked by an executable Foundation gate until the certification marker is recorded;
- all authoritative mutations go through the transaction coordinator;
- API errors use stable typed error codes, never exception-message matching;
- every sensitive mutation declares permission, idempotency and audit requirements;
- database migration/runtime state is validated on the approved Windows/PostgreSQL environment;
- GitHub green status is never treated as a replacement for local certification evidence.

## 2026-10-08 — Foundation API/health boundary hardening

- V1 contract version is enforced at runtime.
- Health status values are centralized in Shared contracts.
- Readiness is measured separately from process health and must reflect PostgreSQL availability.
- The same controls are inherited by every downstream vertical slice.

## Stable rule

When a defect reaches production or certification, fix the root boundary, add regression evidence, and record the exact stable checkpoint. Do not stack unrelated changes onto an uncertified checkpoint.
