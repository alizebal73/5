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


## 2026-10-09 — Foundation certification / Desktop runtime harness

- Symptom: Desktop runtime certification reached its readiness timeout even though canonical build/tests, PostgreSQL migration/schema certification and isolated backup/restore had passed.
- Root-cause finding from code: the Desktop harness started the Server with the default Production environment but did not provide the temporary authentication settings required by the explicit Production startup guard in \`src/Server/Program.cs\`. The guard rejects Production startup when authentication is disabled; the harness therefore could wait for readiness from a process that had already exited. The prior run's server log was deleted by unconditional cleanup, so the historical exception itself could not be confirmed from logs.
- Contributing diagnostic defects: server stdout/stderr were written to the OS temp directory and deleted even on failure; the workflow's diagnostic upload step appeared before Desktop runtime and therefore could not upload diagnostics produced by that later step.
- Fix applied on an isolated fix branch: runtime certification now forces an explicit Production environment, supplies ephemeral signing/provisioning keys and the required authentication configuration, resolves the database connection for the child process, fails quickly if the server exits, captures Desktop stdout/stderr, and preserves redacted diagnostics on failure. The upload step now runs after the runtime certification steps.
- Regression evidence: pending execution of the updated foundation-certification workflow against this branch.
- Verification result: pending; the Foundation must not be marked certified until the complete workflow succeeds on one SHA.
- Rollback: revert the single runtime-certification fix commit as a unit; do not remove or weaken the Production authentication guard.


## 2026-10-09 — Foundation certification / Agent runtime diagnostics

- Symptom: the Agent runtime certification did not observe an authoritative lease after starting the Agent; Desktop, build/tests, PostgreSQL, and isolated backup/restore passed on the same commit.
- Diagnostic gap: the Agent certification script unconditionally deleted its temporary root containing server and Agent stdout/stderr, and the workflow artifact did not include Agent-runtime files. The failed attempt therefore identified the boundary (lease acquisition) but did not preserve the underlying transport/authentication error.
- Fix applied: preserve server/Agent logs and a commit-stamped diagnostic summary on Agent certification failure, redact likely credential/JWT material before writing the artifact, and include Agent-runtime diagnostics in the existing failure artifact. Successful runs continue to delete temporary diagnostics.
- Root cause and regression evidence: pending the next single-SHA Foundation certification; do not mark Foundation certified until Agent lease/fencing/reconnect checks pass.
- Rollback: revert this diagnostic-only commit if preservation causes a runner problem; do not weaken Agent authentication, lease fencing, or the certification assertions.


## 2026-10-09 — Foundation Agent runtime / JWT bearer scheme configuration

- Symptom: Agent credentials were provisioned successfully and the token endpoint returned HTTP 200, but SignalR negotiation repeatedly returned HTTP 401 and no authoritative Agent lease was created.
- Root cause confirmed from preserved server logs: JWT validation raised 'IDX10500: Signature validation failed. No security keys were provided to validate the signature.' The registration used 'AddOptions<JwtBearerOptions>()', which configures the unnamed options instance, while the registered JWT bearer handler reads the named 'Bearer' scheme options. Consequently, the signing key, issuer/audience validation settings, and SignalR query-token event were not applied to the handler actually validating the Agent token.
- Fix: register the options under 'JwtBearerDefaults.AuthenticationScheme' and add a regression test asserting the named Bearer options contain the configured issuer, audience, and signing key. No authentication guard or lease authorization is weakened.
- Related diagnostic hardening: redact token query parameters and fix the failure-summary path join expression; Agent logs remain failure-only artifacts.
- Verification: pending the next full Foundation certification. Certification marker remains pending until Agent heartbeat, fencing and reconnect tests pass on one SHA.
- Rollback: revert this commit as a unit if the regression test or runtime certification fails; do not restore the unnamed options registration.

- First verification after the authentication fix exposed a compile-only defect in the new regression test: the base SecurityKey type exposes no Key byte-array property. The test now casts the configured key to SymmetricSecurityKey before comparing its material; the server fix itself compiled successfully in that attempt.
- A failed diagnostic capture also revealed that Get-Content -Raw can yield null for empty stderr files, prematurely stopping the log-copy loop. Failure diagnostics now read empty files as empty strings so later Agent logs are preserved too.


## 2026-10-09 — Foundation certification / JWT bearer and Agent lease end-to-end

- Regression fix verified: JWT bearer configuration is now attached to the named Bearer scheme. The new server unit test verifies the issuer, audience, signing key, issuer-signing-key validation and SignalR query-token callback on the exact scheme options used at runtime.
- Runtime result: Foundation Certification completed successfully on commit c3a8ba482548e963479fbdf8538be62c4d15acfe in [workflow run 37860650826](https://github.com/alizebal73/5/actions/runs/37860650826).
- Gates passed on that SHA: PowerShell parser; canonical guards, Release build and tests; clean PostgreSQL migration/schema/concurrency checks; backup and isolated restore; Desktop -> Server smoke in fa-IR and en-US; Agent auth, credential bootstrap, authoritative lease, heartbeat, connection fencing, lease release and reconnect.
- Evidence artifact: [foundation-certification-evidence](https://github.com/alizebal73/5/actions/runs/37860650826/artifacts/11585812517). The in-run evidence file records the certified SHA, branch and UTC timestamp.
- Status: this exact Foundation checkpoint is certified. The installer/updater/rollback release gates remain required before production release. Business feature branches must be rebuilt/rebased from this checkpoint; no legacy feature branch is implicitly approved for merge.
- Rollback: if a future regression appears, set the certification marker back to pending and remove the checkpoint's active status; never bypass a failing Foundation gate.


## 2026-10-09 — First product slice / Operator Identity

- Work starts from the fully certified Foundation checkpoint on the new isolated branch `feature/operator-identity-v1`. Legacy Identity/Stations/Customers branches remain untouched and are not merged.
- Scope: protected first-owner bootstrap using `GAMENET_BOOTSTRAP_SECRET` and a PostgreSQL advisory transaction lock; PBKDF2-SHA256 password hashing; five-attempt lockout; JWT access token bound to durable, revocable AuthSession; operator claims are refreshed from PostgreSQL on each authenticated request; audit records share the transaction; WPF login/logout holds bearer tokens only in memory and has fa-IR/en-US UI strings.
- Only Identity-owned tables are added by a new migration after FoundationCore. Existing Foundation schema and audit/idempotency/outbox models are preserved.
- Verification: pending full Foundation certification on the feature branch. Do not merge into the Foundation branch unless clean PostgreSQL migration, Desktop smoke, Agent runtime and canonical gates pass on the same commit.
- Production HTTPS provisioning remains a release gate; bootstrap secrets and passwords must not be used over unencrypted LAN connections.


## 2026-10-09 — Operator Identity / secure transport and migration review

- Added a server-side transport guard for operator authentication, first-owner bootstrap, Agent credential APIs and SignalR Agent connections. Non-loopback HTTP is rejected with a stable 426 contract error; loopback HTTP remains available for local smoke/development.
- Desktop connection options now accept HTTPS for remote hosts and HTTP only for loopback. The HTTP client independently refuses to submit operator credentials to remote non-HTTPS BaseUrls.
- Added regression tests for the server transport boundary and Desktop's no-send-on-insecure-URL behavior.
- Added an interactive bootstrap PowerShell script that prompts for the setup secret and owner password as SecureString inputs, refuses remote HTTP, requires setup to still be pending, and does not write credentials to disk. The Server operator must configure GAMENET_BOOTSTRAP_SECRET separately before running it.
- Migration snapshot review: explicitly recorded ValueGeneratedNever for Identity GUID keys to match the entity configurations and keep the migration snapshot deterministic.
- Verification: full Certification on the feature branch remains pending. Do not merge if any canonical, clean PostgreSQL migration, Desktop or Agent gate fails.


## 2026-10-09 — Desktop API contract header regression

- Static review found the Desktop host set `X-GameNet-Contract: v1` as a default HttpClient header while `GameNetServerClient` also added the same header to each request. That could serialize two values and cause the Server's exact contract-version guard to reject otherwise valid requests.
- Fix: the request client now adds the contract header only when the host has not already configured it. A regression test simulates the Desktop host's default header and asserts exactly one contract version reaches the handler.
- Verification: pending the full feature-branch workflow.


## 2026-10-09 — CI blocker / module persistence boundary and migration artifacts

- Verified the latest feature-branch CI failure from the workflow logs. PowerShell parsing and source-size checks passed; the architecture gate stopped on `src/Server/Modules/Identity/Infrastructure/Persistence/EfIdentityRepository.cs` because all files under a business module are prohibited from directly referencing EF/Persistence.
- Correct boundary: keep `IIdentityRepository` in Identity.Application and move the EF implementation to `src/Server/Infrastructure/Persistence/Identity`, with registration owned by Server composition. Do not weaken `check-architecture.ps1`.
- The same review found the new handwritten migration lacked the required EF `.Designer.cs` artifact. Added a migration designer target model derived from the current model snapshot and made the migration class partial so the existing migration-integrity gate can verify it.
- Verification: the new commit must pass architecture, migration-integrity, clean PostgreSQL migration, pending-model-change, full unit/contract tests, Desktop smoke, and Agent runtime before this slice can be considered for merge.


## 2026-10-09 — CI blocker / command naming and EF migration partial pattern

- The next canonical run reached the architecture guard and correctly rejected the WPF command helper because its type name ended in `Command`, matching the guard's reserved transport contract suffix rule. Renamed the UI-only type to `AsyncUiAction`; the guard remains unchanged and transport DTOs stay exclusively in Shared.Contracts.
- Standardized the new EF migration into the normal partial-class pattern: migration source declares the `Migration` base, designer owns the `DbContext`/`Migration` attributes and target model.
- Verification: rerun canonical and full runtime certification on this exact branch head. Migration snapshot drift and compile/runtime failures remain unknown until those gates execute.


## 2026-10-09 — EF model snapshot drift diagnostic

- The architecture gate and Release build/test suite pass, but `dotnet ef migrations has-pending-model-changes` fails on the current Identity slice. A hand-authored snapshot is not acceptable just because it compiles.
- Added failure-only diagnostics to the PostgreSQL certification script: when the pending-model gate fails, CI asks EF itself to scaffold the exact model delta in its disposable checkout, writes the generated migration source/designer into `ef-model-drift-diagnostic.txt`, and fails closed before any database certification can be marked successful. The diagnostic migration is not committed to the branch.
- The resulting EF-generated operations and target model must be reviewed and applied to the real migration/snapshot; do not suppress the pending-model gate or treat this failed run as certified.


## 2026-10-09 — EF snapshot drift root cause confirmed and fixed

- Read the EF-generated drift artifact from CI run 37908275648. The only model delta is `CreateIndex("IX_auth_sessions_user_id", "auth_sessions", "user_id")`.
- Cause: EF convention creates an index for the `AuthSession.UserId` foreign key, but the hand-authored Identity model snapshot/designer omitted that index. The entity configuration also relied on the convention without documenting it.
- Fix: explicitly configure the UserId index in `AuthSessionConfiguration`, record it in both the migration's target model and the context snapshot, and include the index creation in `20261009010000_OperatorIdentity`. The reverse migration drops the whole `auth_sessions` table, so its index is removed with the table. The pending-model gate remains enabled.
- Verification required: `dotnet ef migrations has-pending-model-changes` must pass; then clean PostgreSQL migration/concurrency, backup/restore, Desktop runtime and Agent runtime must all pass on the same feature commit.


## 2026-10-09 — Identity end-to-end verification gap

- The full Foundation workflow passed, but the existing Desktop smoke intentionally exits after `GET /health`; it does not exercise the new login screen or operator API. The Agent smoke covers agent leases, not operator Identity.
- Added a real Identity runtime check against the isolated, clean PostgreSQL cluster as part of PostgreSQL certification. It exercises bootstrap-secret validation, first-owner create-once semantics, invalid and valid login, database-derived owner permissions, authenticated `/auth/me`, logout/rejected reuse of a revoked JWT, and five-attempt account lockout.
- The helper uses a random throwaway owner/secrets and restores all process environment variables. On failure it emits a redacted diagnostic file matched by the existing workflow artifact rule. It does not touch the configured machine database.
- Verification: the next full Certification must pass with the new Identity runtime path; the preceding successful run did not include these end-to-end assertions.


## 2026-10-09 — Stations slice / live Agent status and idempotency integrity

- Added Stations domain/API/repository on the certified Identity branch. Legacy migration is not imported because it recreates Foundation-owned audit/idempotency tables.
- Online state is derived from the Server-owned `agent_connection_leases` expiry and heartbeat; administrative Station status is a separate field.
- Agent binding requires a non-revoked provisioned credential and is unique per Station. Mutations enforce expected Version, request-hash idempotency and transactional audit.
- Existing idempotency storage now records request hashes, so reusing an active/completed key for a different operation or payload returns a stable conflict instead of replaying an unrelated response.
- Migration `20261009020000_StationBoard` is additive: one nullable idempotency request-hash column plus the Stations table and indexes. It does not recreate Foundation schema.
- Verification required before merge: full certification, idempotency conflict tests and end-to-end station/Agent binding and status tests on one commit.


- Follow-up migration diagnostic found the manually inserted `RequestHash` snapshot metadata was attached to `AuditEntry` because both entities have an `Operation` property. Moved the metadata specifically inside the `IdempotencyRecord` snapshot/designer block. The generated diff now must be rechecked by EF; the gate remains on.


## 2026-10-09 — Stations end-to-end certification

- Extended the isolated Identity runtime harness to exercise the new Stations API with a bootstrapped Owner token: create, replay of the same idempotency key, rejection of the same key with a different payload, provisioning and binding an active Agent credential, authoritative offline state, unique device binding, stale Version rejection, and administrative status separation.
- These assertions use the isolated clean PostgreSQL database and random throwaway station/device IDs. They do not contact or mutate the production machine database.
- Verification requirement: the full Foundation + Identity + Stations workflow must pass on the same commit before the Stations slice is treated as certified.


## 2026-10-09 — Runtime diagnostics must not mask the original failure

- The first extended Stations runtime run reached the new scenario but its failure cleanup then failed reading the Server log because the child process still held the file open on Windows. That cleanup exception masked the original assertion and prevented actionable diagnostics.
- Added bounded retry-based log reads with an explicit `DIAGNOSTIC_READ_FAILED` fallback so the original failure is preserved and the diagnostic artifact is still written.
- The prior run is not considered passed; the same end-to-end Station scenario must be rerun after this diagnostic fix.


## 2026-10-09 — Upload Identity runtime diagnostics on certification failure

- The Stations end-to-end request correctly failed closed with HTTP 500, but the workflow upload glob did not match `identity-runtime-diagnostic-<token>.txt`; consequently the Server exception log was not preserved as an artifact.
- Added the exact Identity diagnostic pattern to the existing failure-only artifact paths. The next run will expose the redacted inner Server exception without changing runtime behavior or reducing any assertion.


## 2026-10-09 — Idempotency claim SQL composition defect

- The Stations runtime test reached the API and got HTTP 500 on station creation. The captured Server exception identified EF Core composition over a non-composable `INSERT ... RETURNING` passed through `SqlQueryRaw<string>(...).SingleOrDefaultAsync()`.
- Replaced that scalar query with parameterized `ExecuteSqlInterpolatedAsync` and an `INSERT ... ON CONFLICT DO NOTHING`; the affected-row count determines whether this request acquired the idempotency claim. This preserves parameter binding and does not suppress idempotency conflicts.
- Verification is still required: the next full Certification must pass the same-key replay/different-payload conflict assertions and every PostgreSQL, restore, Desktop and Agent runtime gate on the exact same SHA.

## 2026-10-09 — WPF localization XAML closing-tag defect (Foundation run #159)

- Symptom: `Canonical verification` failed during the Release build with WPF/XAML error MC3000 at line 29 in both `Strings.en-US.xaml` and `Strings.fa-IR.xaml`.
- Root cause: the `Station.Maintenance` resource opened as `<system:String ...>` but closed as `</system>`; the XML element names did not match.
- Fix: corrected only the malformed closing tag to `</system:String>` in both locale dictionaries. The displayed translations and runtime behavior are unchanged.
- Regression evidence: the canonical Release build compiles both XAML dictionaries; the full Foundation workflow must pass on the exact resulting commit before the branch is considered verified.
- Status at entry: correction committed; verification pending. Do not treat the failed run #159 as a passing checkpoint.
- Roadmap/checkpoint: see `docs/development/roadmap-and-current-checkpoint.md`.

## 2026-10-09 — Full Foundation Certification passed after WPF XAML correction

- Exact commit: `853f23427783a93638a0212cb1dc5e97386e0e8f`.
- GitHub Actions run [#160](https://github.com/alizebal73/5/actions/runs/37917339204) completed with conclusion `success` on that exact SHA.
- Passed gates: PowerShell parser, canonical Release build/test, isolated PostgreSQL migration/concurrency and runtime assertions, backup/restore, Desktop runtime, Agent runtime, and evidence upload.
- This closes the XAML correction stage. Later commits are not automatically certified; each requires its own relevant verification.

## 2026-10-09 — Business gate must verify certified Foundation ancestry

- Root cause: `scripts/check-business-gate.ps1` trusted only `status=certified`; it did not validate the 40-character `certifiedCommit`, confirm the commit object existed, or prove it was an ancestor of current `HEAD`. A stale or unrelated marker could therefore pass.
- Fix in this commit: resolve the marker SHA and current HEAD, use `git merge-base --is-ancestor`, and fail closed for missing, malformed, unrelated or uncheckable certificate commits. The workflow fetches full history (`fetch-depth: 0`) so the ancestry proof is meaningful.
- Regression test: `scripts/test-business-gate.ps1` builds a temporary Git repository, proves an actual ancestor is accepted, and proves a real commit from unrelated history is rejected. It does not modify the project repository or production database.
- Verification: GitHub Actions run [#161](https://github.com/alizebal73/5/actions/runs/37918110954) completed with `success` on exact SHA `2b731276ca89015cb32be4a2eb90dc5f449c4d49`. The regression test ran inside Canonical verification; PostgreSQL, isolated backup/restore, Desktop runtime, Agent runtime, and evidence upload also passed.

## 2026-10-09 — GitHub branch governance still needs repository-level protection

- API audit reported `protected=false` for `main`, `foundation/runtime-final-v2`, and `feature/operator-identity-v1`; the repository rulesets endpoint returned an empty list.
- Recommended protection policy is recorded in `docs/operations/branch-governance.md`. No remote repository settings were changed by this commit.
- The available GitHub connection could read branch metadata but could not access/write the administrative branch-protection endpoint. Do not report remote protection as enabled until settings are applied and re-read/verified.




## 2026-10-09 — Documentation-only changes excluded from full certification trigger

- Workflow change commit: `935ede43843cc51491c8a87def55418f4bcb75bb`.
- Full certification run [#162](https://github.com/alizebal73/5/actions/runs/37918565934) completed with conclusion `success` on that exact SHA.
- The workflow now ignores changes under `docs/**` and Markdown files for push-triggered full certification. Changes to source, scripts, or workflow files still trigger certification; `workflow_dispatch` remains available.
- This reduces avoidable runs for docs-only commits; it does not yet implement the separate, lightweight pull-request validation workflow planned for stage 3.



## 2026-10-09 — GitHub branch protection verified; stage 2 closed

- Read-back from GitHub confirmed Ruleset `protect-main` (ID `24783754`) is `active` for `refs/heads/main`, and `protect-foundation-runtime` (ID `24784035`) is `active` for `refs/heads/foundation/runtime-*`.
- GitHub branch metadata now reports `protected=true` for both `main` (SHA `b9288471f2047570eaf8d0d6552cf87bc0ddc214`) and `foundation/runtime-final-v2` (SHA `adb2159fb85564187718df8cbddcd3377e599c1c`).
- Both rules enforce pull requests, prevent deletion and non-fast-forward updates (force pushes), require conversation resolution, and allow Squash only. Required approvals are zero; bypass list is empty; extra approval for unattributed Copilot PRs is disabled in both.
- This closes the branch-protection subtask and Stage 2. Next is Stage 3: separate a lightweight PR validation workflow from the full Foundation certification without weakening code checks.

## 2026-10-09 — Stage 3: CI lanes separated, triggers narrowed safely

- Root cause: the full Foundation suite ran for normal commits on `feature/operator-identity-v1` and Foundation pushes, while `gamenet.yml` independently ran Canonical build/test on PRs. Its prior job condition skipped PRs whose **source/head** branch began with `foundation/runtime-`, potentially leaving those PRs without the intended quick lane. The manually dispatched `foundation-runtime.yml` had only PostgreSQL checks but its name could be mistaken for full certification.
- Change: `gamenet.yml` is now `gamenet-quick-validation`, triggered on non-Markdown pushes to the feature branch and PRs targeting `main`; it runs PowerShell parsing plus `scripts/verify.ps1` (static architecture/size/placeholders, build and tests), but not the PostgreSQL/backup/Desktop/Agent runtime lanes.
- Change: `foundation-certification.yml` retains every prior full gate and now runs full certification manually, for PRs **targeting** `foundation/runtime-*`, and when the certification workflow definition itself changes on the active feature branch. Normal feature-branch code pushes no longer rerun the full runtime suite; a PR into protected Foundation runs it before merge.
- Change: the PostgreSQL-only manual workflow is named `foundation-postgresql-diagnostic` and documents that this is a diagnostic lane only.
- Path-filter safety: the old broad `docs/**` ignore would have skipped changes to `docs/operations/foundation-certification.json`, which the business gate reads. Filters now ignore Markdown only on push. PR workflows do not use path-ignore, so a docs-only PR still produces its required check.
- No test gate was removed from full Foundation certification. Required status checks have not yet been configured in the GitHub rulesets; after a real Foundation PR exposes the exact check context, make the full certification check required before merging.
- Verification: pending on the commit that includes these workflow changes; do not close Stage 3 until the exact commit has a successful quick lane and full workflow-definition self-check.

