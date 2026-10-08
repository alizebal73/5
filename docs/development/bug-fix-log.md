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

## 2026-10-08 — Foundation CI / .NET host command

- Symptom: self-hosted Windows Runner reached canonical verification, but both `dotnet --version` and `dotnet --info` exited with `-2147450751` after printing CLI usage text.
- Root cause: not yet certified. The runner routing is proven correct; the remaining defect is in the .NET executable/environment resolved by the Runner service.
- First hypothesis rejected: changing `--version` to `--info` did not resolve the failure.
- Current diagnostic: verification now records the resolved `dotnet.exe` path, DOTNET_ROOT, PATH and file/product version before invoking `dotnet --info`.
- Regression evidence: PowerShell parser, business gate, source-size guard and architecture guard passed on the same self-hosted Runner before the .NET host failure.
- Verification result: pending rerun with executable/environment diagnostics.
- Rollback: revert the diagnostic verification-script change after the root cause is fixed.

## 2026-10-08 — Foundation CI / dotnet executable resolution

- Current finding: Microsoft documents both `dotnet --info` and `dotnet --version` as valid .NET CLI options for supported SDK installations.
- The self-hosted Runner reproduced failure for both commands while the job itself and repository checkout succeeded.
- Working root-cause hypothesis: the Windows service is resolving a non-standard or incompatible `dotnet.exe` through its service environment/PATH.
- Control applied: verification now prefers the system-wide `C:\Program Files\dotnet\dotnet.exe` (or `ProgramW6432`) and uses that exact executable for environment inspection, tool restore, restore, build and test.
- Verification result: pending rerun.


## 2026-10-08 — Foundation verification root-cause record

- Runner routing was correct throughout: verification executed on self-hosted Windows Runner `Server` under `C:\actions-runner-5\_work\5\5`.
- The initial `dotnet` failure was caused by `verify.ps1` naming a function parameter `Args`; PowerShell treated `$args` as the automatic argument variable, so `dotnet` was invoked without the intended CLI arguments and returned `0x80008081`.
- The machine-wide .NET installation was then standardized at `C:\Program Files\dotnet` with SDK `10.0.401`; the final successful run confirmed that exact host and SDK.
- Foundation build exposed three code/test defects: missing `using Xunit;` in test files, and missing `using Microsoft.EntityFrameworkCore;` for the EF relational transaction extension.
- Controls added: `Invoke-Checked` now uses `CommandArgs`; test files explicitly import Xunit; transaction coordinator imports the EF extension namespace; verifier pins the standard system-wide dotnet host.
- Regression evidence: workflow run #54 completed with conclusion `success` on self-hosted Windows x64.

## Stable rule

When a defect reaches production or certification, fix the root boundary, add regression evidence, and record the exact stable checkpoint. Do not stack unrelated changes onto an uncertified checkpoint.

## 2026-10-09 — Foundation audit / migration and credential persistence

- Symptom: the PostgreSQL Foundation lane failed with `42703: column "OccurredAtUtc" does not exist` during the first migration.
- Root cause: the generated EF migration contained a stale physical index column name while the model/snapshot mapped the property to `occurred_at_utc`.
- Fix: the migration artifact now uses the actual PostgreSQL column name; Foundation certification also creates an isolated clean PostgreSQL cluster and applies the full migration chain from zero.
- Regression evidence: clean migration and schema verification are mandatory Foundation gates.
- Symptom: Agent credential persistence protected data with `LocalMachine` while restore used `CurrentUser`.
- Root cause: the protection scope was changed on only one side of the persistence boundary.
- Fix: both Protect and Unprotect use `CurrentUser`, matching the Windows service identity.
- Regression evidence: the Agent test recreates the credential store and decrypts the persisted secret; the architecture guard rejects `LocalMachine` in this credential store.
- Symptom: backup certification could overwrite the caller's saved `PGPASSWORD` state.
- Root cause: the parser helper mutated the saved password through a ref parameter and was called more than once.
- Fix: environment preservation is owned by the caller and the parser no longer mutates the caller's saved-state reference.

## 2026-10-09 — Foundation audit / second hardening pass

- Agent credential rotate/revoke routes could previously leak exception-message-based response behavior or fall through to generic 500 for typed credential failures.
- Fix: all provisioning-key credential mutations now return the shared stable credential error envelope.
- Readiness previously proved only database connectivity.
- Fix: readiness now also requires zero pending EF migrations so the Server cannot report Ready against an incompatible schema.
- Correlation IDs previously had no upper bound even though Audit storage allows 128 characters.
- Fix: oversized correlation IDs are rejected with a stable API error before dispatch.
- Placeholder guard now rejects both explicit TODO/FIXME markers and unfinished runtime exceptions (NotImplementedException / NotSupportedException).
- Business gate now covers approved business roots beyond the primary Modules/Features paths so future business code cannot silently bypass the pre-certification lock.
