# Bug-Fix Log

Every defect records symptom, root cause, affected boundary, fix, regression evidence, verification result and rollback reference.

A defect is not closed because a later refactor hides it.

## 2026-10-09 — Agent URL scheme validation aligned with Server transport policy

- Symptom: Agent configuration accepted a remote `http://` Server origin, even though Desktop rejects non-loopback HTTP and the Server's `SecureTransportMiddleware` rejects HTTP for remote authentication/bootstrap/Agent/SignalR routes. This let an invalid deployment configuration pass startup and fail later at enrollment or connection.
- Root cause: `AgentTransportOptions.Validate()` accepted either HTTP or HTTPS without restricting HTTP to loopback development.
- Fix: allow HTTPS for remote Server origins and HTTP only for loopback; retain rejection of embedded credentials, query/fragment, and invalid URLs. Regression tests cover rejected remote HTTP and accepted loopback HTTP / remote HTTPS.
- Initial verification note: pending at commit time; superseded by the exact-SHA evidence below.
- Verified: [Quick Validation #127](https://github.com/alizebal73/5/actions/runs/37959248404) and [Full Foundation Certification #195](https://github.com/alizebal73/5/actions/runs/37959248381) both passed on `b95eab6c68e89b4d8f4500f714f1445ffd9ca4c4`. Foundation passed canonical build/tests, PostgreSQL, isolated backup/restore, Desktop runtime, Agent runtime and evidence upload. This closes the Agent URL validation defect only; the LAN TLS/Kestrel/certificate contract remains open.
- Rollback: revert only the Agent URL validation and its regression tests if they introduce a regression; do not weaken Server HTTPS enforcement.

## 2026-10-09 — Desktop/Agent Server-origin policy centralized

- Symptom: Desktop and Agent each maintained a separate URL validator; their allowed-origin policies could drift, and both had previously allowed an HTTP/LAN or a non-root URL to reach runtime instead of failing during configuration validation.
- Root cause: transport-origin validation was duplicated at two client boundaries rather than owned by a shared, dependency-light contract.
- Fix: add `GameNet.Shared.Primitives.ServerEndpointAddress` as the single origin policy. Desktop and Agent now consume it. Only root HTTPS origins are valid for remote deployment; HTTP is valid only for loopback development; credentials, path prefixes, query and fragment are rejected.
- Regression coverage: shared positive/negative URI tests plus Agent and Desktop consumer tests.
- Verified on exact SHA `553dc80f6f553b0e793b7cbb2b82ba9136b116c0`: [Quick Validation](https://github.com/alizebal73/5/actions/runs/37963001084) and [Full Foundation Certification](https://github.com/alizebal73/5/actions/runs/37963001068) both passed. Canonical build/tests, PostgreSQL, isolated backup/restore, Desktop runtime, Agent runtime and evidence upload all passed. This verifies the common origin policy only; no LAN connection or physical two-PC install is implied.
- Rollback: revert the shared policy and its consumers/tests together; do not relax remote HTTPS enforcement.

**Required process/templates for all new entries:** [bug-fix process](bug-fix-process.md), [bug-fix template](../templates/bug-fix-template.md), [Definition of Ready](../planning/definition-of-ready.md) and [Definition of Done](../planning/definition-of-done.md). This is a historical append-only log. Preserve old entries; correct factual mistakes transparently; append verified outcomes rather than overwriting history.

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
- Initial workflow verification succeeded on exact SHA `aaea71212111aec24f688ae791aabb2543b5633b`: [quick validation #73](https://github.com/alizebal73/5/actions/runs/37921715915) and [full Foundation certification #163](https://github.com/alizebal73/5/actions/runs/37921715979) both completed with `success`, including every PostgreSQL, Backup/Restore, Desktop and Agent gate.
- Stage 3 remains open until a real PR to `foundation/runtime-*` exposes its exact check context and that check is made required in the `protect-foundation-runtime` ruleset. The ruleset currently requires a PR, but not yet a passing CI status.




## 2026-10-09 — Foundation required status check configured from a real PR

- Draft PR [#10](https://github.com/alizebal73/5/pull/10) targeted `foundation/runtime-final-v2` at base SHA `adb2159fb85564187718df8cbddcd3377e599c1c`; it was opened only to observe the actual `pull_request` event and exact status-check context.
- Full Foundation run [#164](https://github.com/alizebal73/5/actions/runs/37922148983) completed with `success` on PR head SHA `fbceb07b9564065b58000ddb53323592627b9c07`.
- GitHub's saved Ruleset `protect-foundation-runtime` (ID `24784035`) was read back and confirmed to require `context=foundation` from GitHub Actions (`integration_id=15368`). Required checks have `strict_required_status_checks_policy=false` (PR is not required to be up-to-date with base).
- Draft PR #10 was closed without merging after the check was added; no feature code was integrated into Foundation or main.
- Remaining Stage 3 item: obtain real PR evidence for the `quick-validation` job on a PR targeting `main`, then require that exact status check in `protect-main`. Do not mark Stage 3 complete until read-back verifies it.



## 2026-10-09 — Stage 3 closed: required status checks verified

- Main protection: after the exact PR check was observed, Ruleset [protect-main](https://github.com/alizebal73/5/rules/24783754) was read back from GitHub and is active for `refs/heads/main`; `required_status_checks` contains `quick-validation`.
- PR validation evidence: [Draft PR #11](https://github.com/alizebal73/5/pull/11), head SHA `685301a6973666b0dca0aee27739f5afccdc9124`, produced the GitHub Actions check `quick-validation`; [run #74](https://github.com/alizebal73/5/actions/runs/37923320241) completed successfully on that SHA.
- Foundation protection: Ruleset [protect-foundation-runtime](https://github.com/alizebal73/5/rules/24784035) is active for `refs/heads/foundation/runtime-*` and requires `foundation`; [run #164](https://github.com/alizebal73/5/actions/runs/37922148983) passed on PR #10's exact head SHA.
- Both temporary validation PRs (#10 and #11) were closed without merging. No code was merged into `main` or Foundation; `main` remains at its starter commit.
- Result: Stage 3 is closed. Next step is Stage 4, building a source-backed product requirements/dependency/ownership/acceptance matrix from the lessons and requirements of repositories 2 and 3 before adding more product features.


## 2026-10-09 — Stage 4 complete: Repo 2/3 requirements consolidated

- Audited the product synthesis, action map, client UX, product-completion/release backlog and physical-validation protocol from Repo 2, then reconciled them with Repo 3's capability map, foundation audit/gap matrix, module ownership, data ownership, transaction rules, time/money rules and Session/Inventory invariants.
- Canonical deliverable: docs/planning/requirements-traceability.md, commit 970e13692f197510f178d3c5030191d5ad5a1e97. It records 56 requirement rows covering ownership, dependencies, acceptance criteria, priority/risk and the actual Repo 5 implementation status; it also maps old failure patterns to required regression tests.
- Summary deliverable: docs/planning/capability-matrix.md, commit 122965218ef0b159396ed02c38107f4deb709a2c.
- Durable decisions: Repo 5 keeps the modular Server + native WPF Desktop + separate Agent + Shared contract + PostgreSQL architecture; Repo 2's older React/SQLite proposal is not adopted. PS5/foosball remain timed stations without a pretend Windows Agent. First release remains single-shop and no code is copied/merged from repos 2 or 3.
- Critical old failure patterns converted to gates: transfer/Agent/Login ownership races; PendingPayment vs Debt and session-payment allocation; wallet/gift/revenue reconciliation; warehouse/showcase stock and reversal; idempotency/concurrency; stale realtime connection/lease; production mocks; migration drift; installer secret/data-root/LAN startup; and physical PC certification.
- Evidence boundary: Identity and Stations exist as code candidates, but latest-head certification remains required. The remaining commercial modules are not described as implemented merely because they appear in requirements documentation. No production code changed in Stage 4.
- Result: Stage 4 is closed. Next: Stage 5, standardize bug records and enforce Ready/Done fields on each implementation slice.


## 2026-10-09 — Stage 5 complete: standardized work-item controls

- Added Definition of Ready and Definition of Done as explicit entry/closure gates, plus a bug-fix process, reusable bug record, and vertical-slice template.
- Updated the Engineering Constitution, master build plan and Bug-Fix Log preamble to make the process mandatory for new work while preserving the historical log as append-only.
- Added scripts/check-work-item-docs.ps1 to validate required policy/template files and headings and require at least 50 unique REQ IDs in the traceability matrix. Wired it into scripts/verify.ps1 so Canonical verification cannot pass if those work-item controls disappear or requirement IDs duplicate.
- The first two gate runs failed on incorrect expected text in the new validator: [run 75](https://github.com/alizebal73/5/actions/runs/37925664994) expected separate English “Definition of Done” text where the source used a combined heading, and [run 76](https://github.com/alizebal73/5/actions/runs/37925776663) expected an English historical-failure label absent from the Persian capability summary. The validation assertions were corrected to match the actual source-of-truth headings rather than changing the product documents to satisfy an inaccurate check.
- Final verification: [quick-validation run 77](https://github.com/alizebal73/5/actions/runs/37925813224) completed with success on exact SHA  bcc18e998962ad724498f69d289ca8f9c084006f. PowerShell parser and the Canonical build/test step passed, including the new work-item documentation gate.
- Scope/evidence boundary: no product runtime code or database schema changed in this stage. This is a documentation/process/verification improvement, not a claim that new product features or release readiness have been certified.
- Result: Stage 5 is closed. Next: Stage 6, operator identity and secure login, with exact-SHA runtime evidence and no completion claim based only on source presence.


## 2026-10-09 — Stage 6A: bounded operator login input verified

- Finding: IdentityService normalized/queried arbitrary-length usernames and passed arbitrary-length password inputs to the password verifier. The password hashing path already accepted only 10–256 characters when creating a hash, but verification had no matching length guard. An oversized password could therefore trigger disproportionate PBKDF2 work for a real account.
- Fix on exact SHA 7909acba2c922f102e18693e3842743f7ad16afc: reject blank/oversized credential inputs before user lookup; cap normalized username at 64 characters and password at 256 characters; enforce the same 256-character ceiling inside Pbkdf2PasswordHasher.Verify as defense in depth.
- Regression evidence: PasswordHasherTests adds a hash for a 257-character password to prove the verifier rejects it even when the derived hash would otherwise match. The clean PostgreSQL Identity runtime harness also verifies overlong username and password return the stable generic auth.invalid_credentials response.
- Verification: [quick-validation run #78](https://github.com/alizebal73/5/actions/runs/37926450614) and [full Foundation Certification run #166](https://github.com/alizebal73/5/actions/runs/37926586429) both completed with success on the exact same SHA. Full certification includes canonical build/tests, clean PostgreSQL migrations/schema/concurrency, Identity + Stations end-to-end, backup/isolated restore, Desktop runtime, Agent runtime and evidence upload.
- Validation PR [#10](https://github.com/alizebal73/5/pull/10) was closed without merging after the full run. main remains unchanged; no feature diff was merged into Foundation.
- Stage 6 remains open. Remaining identity criteria include real operator/user and role-permission management, password change/reset policy, and a runtime test proving a valid operator without a required permission is denied. This focused login-hardening fix does not claim these unfinished controls are complete.


## 2026-10-09 — Stage 6B implementation: operator and role management (runtime pending)

- Added separate Shared request/response contracts, application use cases, EF repository, and API endpoints for listing/creating operators, assigning roles, enabling/disabling accounts, listing/creating roles, and updating role permission sets.
- Server-side checks enforce the permission catalog, role-manager permission ceiling, Owner-only Owner assignment, immutable system Owner role, protection of the last active Owner, prevention of self-role/status changes, and session revocation when roles change or accounts are disabled. Mutations use PostgreSQL transaction coordination, idempotency keys, and append-only audit records.
- `identity.roles.manage` was added to Shared permissions. Owner effective permissions are resolved from the current compiled permission catalog so newly introduced permissions do not depend on old stored role-permission rows.
- Added domain validation for role codes and a unit-test file. Clean PostgreSQL runtime scenarios now cover the limited-role boundary, idempotent user creation, live permission refresh, denial without permission, role permission escalation, Owner protection and system-role immutability.
- Quick validation [run #84](https://github.com/alizebal73/5/actions/runs/37929829011) passed on SHA `2e6013c98501f2fbac106cba866074598e3f7851` before documentation-only follow-ups. A preceding run [#81](https://github.com/alizebal73/5/actions/runs/37929430932) found the missing `OperatorUser.SetActive` domain method; it was added and the next build passed.
- Evidence boundary: full Foundation Runtime has not yet run against the final management harness SHA; do not close these criteria until its real PostgreSQL Identity/runtime and all other Foundation gates pass. Password change/reset remains open.


## 2026-10-09 — Stage 6B runtime certification passed

- Full Foundation Certification [run #168](https://github.com/alizebal73/5/actions/runs/37930045661) completed with `success` on exact code SHA `bf0c355837c15f1cf9dedf324779fa1f09f4b180`. The run passed PowerShell parser, Canonical Release build/tests, PostgreSQL clean migration/schema/concurrency plus Identity/Stations runtime, Backup/isolated restore, Desktop runtime, Agent runtime and certification evidence upload.
- The new PostgreSQL Identity runtime cases passed: owner role listing, limited role/operator creation, same-key idempotent replay, denial when a permission is absent, live permission refresh from persisted role changes, role-manager privilege escalation denial, non-owner Owner-protection denial, and system Owner-role immutability. Existing session revocation/lockout and station/Agent checks also passed.
- Quick validation [run #89](https://github.com/alizebal73/5/actions/runs/37929829011) passed the Canonical build/tests on SHA `2e6013c98501f2fbac106cba866074598e3f7851` before later documentation-only commits. The full certification applies to the code SHA named above.
- Draft validation PR [#10](https://github.com/alizebal73/5/pull/10) was closed without merge. `main` remains at starter SHA `b9288471f2047570eaf8d0d6552cf87bc0ddc214`; no feature code was merged into Foundation or main.
- Scope boundary: Stage 6 remains open for password change/reset and any remaining identity acceptance criteria. This is not an installer, physical pilot or production-release certification.


## 2026-10-09 — Stage 6C: secure operator password lifecycle and keyed idempotency

- Security finding: operator-management idempotency used plain SHA-256 over serialized request payloads. Because operator creation and password reset carry a password in the request payload, a database disclosure could expose an offline-verification oracle for guessed passwords.
- Fix: all operator-management request fingerprints now use HMAC-SHA256 with a purpose-derived key from the configured 32+ character authentication signing secret. The persisted fingerprint contains only the versioned digest; the password and raw hash are never written to the idempotency table. The identity mutations still require idempotency keys and keep transaction/audit behavior.
- Added self-service `PUT /api/v1/identity/me/password`: verify the current password, enforce 10–256-character input bounds, reject an unchanged password, reset lockout state, keep the current authenticated session so a retry can replay safely, and revoke the operator's other active sessions.
- Added `PUT /api/v1/identity/users/{id}/password` behind the distinct `identity.users.password-reset` permission. The target cannot be the actor; a reset reason is mandatory; non-Owners cannot reset an Owner; successful reset revokes all target sessions and clears login lockout state. An append-only Audit record contains actor, target, reason and before/after metadata without credentials.
- Added Persian/English Desktop self-service password-change controls using PasswordBox, confirmation, user-facing errors, clearing of sensitive fields, and same-idempotency-key retry after an uncertain transport result. Administrative reset remains a Server API; its full administrative Desktop UI is tracked under the later Settings/Identity UI work and is not claimed complete here.
- Regression evidence: HMAC unit tests cover deterministic same-payload replay, changed payload, key rotation, no password disclosure in the fingerprint and fail-closed weak-key handling. Operator domain tests prove password change clears login lockout. The isolated PostgreSQL Identity runtime harness verifies wrong/unchanged password rejection, valid self-change, rejection of old credentials, login with the new credential, replay/conflicting payload behavior, separate reset permission, Owner protection, admin reset credential change and session revocation.
- Verification on exact SHA `c0406a4e6d88fc20eb5f7100e7d38c9448e584de`: [quick-validation #94](https://github.com/alizebal73/5/actions/runs/37933516714) passed; [full Foundation Certification #170](https://github.com/alizebal73/5/actions/runs/37933516853) passed PowerShell parser, canonical Release build/tests, clean PostgreSQL migrations/schema/concurrency and Identity runtime, isolated backup/restore, Desktop runtime, Agent runtime and evidence upload. Evidence artifact: [foundation-certification-evidence](https://api.github.com/repos/alizebal73/5/actions/artifacts/11617233775/zip).
- Validation PR [#13](https://github.com/alizebal73/5/pull/13) is a test harness only and must be closed without merge. No feature code is being merged into `main` or `foundation/runtime-final-v2` by this validation.
- Scope boundary: Stage 6's identity/security API and runtime gate is verified on this SHA. The administrative users/roles/reset screens are not complete product UI; do not label the full product ready based on these API and Foundation results alone.

## 2026-10-09 — Stage 7 checkpoint: station board search/filter — certified

- Added presentation-only Station Board search across code, name, type/status, Agent/device details, and combined type/status filters. Added Persian/English labels, empty-filter feedback, counts, and filter-focused unit tests.
- Station board filtering does not mutate Server state or introduce a client-side data source. Visible online count is restricted to PC stations; further UI/runtime review of non-PC station Agent semantics remains part of the next product pass.
- Product code commit: `4f9f1745753e505dcd5daf067fb5b8227f0931ea`; Quick Validation [#95](https://github.com/alizebal73/5/actions/runs/37934341785) passed.
- Full-gate path initially did not trigger for station UI file changes. Fixed the Foundation workflow path filter in workflow-only commit `cba105cd4bea69e76691fbbf8cd8fed30d69d332`, so both checks now trigger for this change set.
- On exact verification SHA `cba105cd4bea69e76691fbbf8cd8fed30d69d332`, Quick Validation [#96](https://github.com/alizebal73/5/actions/runs/37935419095) passed and Full Foundation Certification [#171](https://github.com/alizebal73/5/actions/runs/37935419140) passed.
- Validation-only Draft PR [#14](https://github.com/alizebal73/5/pull/14) was closed without merge after certification. `main` remains at `b9288471f2047570eaf8d0d6552cf87bc0ddc214`; `foundation/runtime-final-v2` remains at `be29687e637b709158a30204bb9213bfa4813950`.
- Scope boundary: this certifies the station-board filter change against the current Foundation gates; it does not mean the whole GameNet product, Agent integration, or all station workflows are release-ready. Next priority is product work on the customer-management vertical slice rather than additional CI-only changes.

## 2026-10-09 — Agent health/presence hardening checkpoint (Stage 8A)

- Tightened stable Agent identity creation so simultaneous first starts cannot overwrite each other's DeviceId; corrupt or invalid stored identities fail closed instead of silently generating a different server identity.
- Validated Agent DeviceId format and transport options at startup; invalid URLs, embedded URL credentials, out-of-bound heartbeat intervals and retry intervals fail configuration validation.
- Cleared the bootstrap secret from the Agent process even when a previously encrypted DPAPI credential is loaded, reducing the time a one-time secret remains in the process environment.
- Hardened Agent lease usage: the client checks local lease expiry, validates the exact device/connection/token/expiry returned by the Server, clears lease state after ambiguous/rejected heartbeat or reconciliation failures, and validates authoritative reconciliation before considering a transport cycle healthy.
- Hardened the Server lease boundary: an Agent connection is not made authoritative in Hub context unless its lease is authoritative and belongs to the current device/connection; competing connections do not receive the active lease token. The Server records heartbeat time using its own clock and validates bounded health metadata.
- Station presence now requires both a fresh-enough Server-side heartbeat and an unexpired lease. Desktop refreshes station/Agent health periodically while signed in, stops the timer on logout/window close, and does not overwrite the operator's action status every polling cycle. PS5 and Foosball display timer-only status rather than a misleading Windows-Agent offline/heartbeat status.
- Expanded the real Agent runtime harness to assert fresh server-observed heartbeat/version/state, advancing heartbeat timestamps, a rejected expired lease followed by Agent reacquisition and fresh health, competing-Agent fencing, owner lease release and reconnect.
- Initial attempts at modifying the PowerShell runtime harness accidentally duplicated its trailing block and failed the parser gate. The malformed tail was discarded by restoring the last parser-green harness and reapplying the health-recovery scenario with line-based insertion. The final tested file is parser-green.
- Exact certified code/test SHA: `bd635748b5189153ba7a9d5873833220923a9ce4`. [Quick Validation #103](https://github.com/alizebal73/5/actions/runs/37938397742) passed and [Full Foundation Certification #178](https://github.com/alizebal73/5/actions/runs/37938397675) passed PowerShell parser, Canonical Build/Test, PostgreSQL, isolated Backup/Restore, Desktop Runtime, Agent Runtime (including expiry/recovery/fencing/reconnect) and evidence upload.
- Scope boundary: this checkpoint certifies Agent identity, lease, heartbeat, reconnect/recovery and Desktop presence display. The repository still has no server-to-Agent command delivery/execution path; stale-command rejection cannot be claimed as runtime-tested until that command path is implemented. Do not mark all of Stage 8 complete and do not start customer features yet. `main` remains `b9288471f2047570eaf8d0d6552cf87bc0ddc214`; `foundation/runtime-final-v2` remains `be29687e637b709158a30204bb9213bfa4813950`.

## 2026-10-09 — Stage 8B checkpoint: guarded Agent Health Probe — certified

- Added a Shared V1 allow-listed command contract. At this checkpoint the only command is `HealthProbe`, a read-only diagnostic; the protocol does not accept arbitrary shell text, executable paths, shutdown, logoff, restart, launch or network-switch requests.
- Server dispatch is in Infrastructure: it selects the current lease only when the lease is live and the server-observed heartbeat is fresh, binds the command to one `CommandId`, `StationId`, `DeviceId`, exact `ConnectionId` and lease token, and sets a short expiry/acknowledgement timeout. The acknowledgement is accepted only from the original current connection/lease with matching identity and a finish time inside the command window.
- Agent fails closed for wrong device, wrong/expired lease, stale/future/overlong command, invalid IDs, unknown command type or missing handler. A bounded per-process de-duplication cache ensures concurrent/repeated delivery of an identical `CommandId` does not call the handler twice; conflicting payload under the same ID is rejected, and capacity pressure rejects new commands rather than evicting a still-live command ID.
- Added a permission-checked Desktop Health Probe action with Persian/English labels. The Server writes an audit record before dispatch (so an Audit failure prevents dispatch) and writes the outcome afterwards; the external call is outside the database transaction. Non-PC timed stations cannot be probed.
- Expanded the real isolated PostgreSQL integration harness: it provisions and binds a test Agent, starts the actual Agent process against the isolated Server, waits for Server-observed heartbeat/online presence, sends the HTTP Health Probe request, asserts the typed success acknowledgement, stops the process, and verifies that the station becomes offline. Unit tests cover stale/wrong-lease rejection, unsupported command types, ID reuse, duplicate delivery, wrong connection/lease acknowledgement and acknowledgement expiry.
- During development, the harness exposed and corrected a nullable identity compile error, a PowerShell assertion block/ordering defect, and an incorrect read path for the provisioning response's one-time secret. An architecture-gate failure was a text-scan false positive caused by the word `SignalR` in Application-layer comments; the actual dispatcher remained behind an Infrastructure interface. These were fixed before the final SHA was certified.
- Exact certified SHA: `89bcbccc4fb244f3f8b8143a5aa7cf568ae043cb`. [Quick Validation #116](https://github.com/alizebal73/5/actions/runs/37945895274) and [Full Foundation Certification #191](https://github.com/alizebal73/5/actions/runs/37945895203) passed. Full certification passed PowerShell parser, architecture/canonical Release build and tests, PostgreSQL/identity runtime (including a real Agent Health Probe), isolated backup/restore, Desktop Runtime, Agent Runtime and evidence upload. Evidence: [foundation-certification-evidence artifact 11623841123](https://api.github.com/repos/alizebal73/5/actions/artifacts/11623841123/zip).
- Scope boundary: this closes the **safe diagnostic command path** only, not all Agent/PC-control requirements. Launch/stop/lock/unlock/message/network profile/restart/shutdown and their dedicated permissions/policy/audit/runtime tests remain unimplemented and must not be shown as available. No feature merge was made: `main` remains `b9288471f2047570eaf8d0d6552cf87bc0ddc214`; `foundation/runtime-final-v2` remains `be29687e637b709158a30204bb9213bfa4813950`.


## 2026-10-09 — تصمیم ترتیب کار / قابلیت نصب زودهنگام

- نوع مورد: تصمیم/شکاف تحویل، نه باگ runtime تأییدشده.
- مشاهده: گواهی فعلی Agent از harness خودکار استفاده می‌کند؛ آن گواهی به‌تنهایی وجود یا صحت یک Setup قابل‌نصب برای Server، Desktop یا Agent را ثابت نمی‌کند. در مخزن یک سند طراحی نصب وجود دارد، اما بسته/نصاب تأییدشده‌ای برای کاربر نهایی شناسایی نشد.
- اقدام ترتیب کار: قبل از قابلیت کنترلی PC بعدی و قبل از ماژول Customers، یک مسیر نصب آزمایشی دوماشینه اضافه می‌شود: Server + PostgreSQL + Desktop در سمت سرور و Agent مستقل در سمت کلاینت، با شواهد نصب پاک و تست انتها‌به‌انتها.
- الزامات ایمنی: secret/connection string در repo یا package ثابت نشود؛ هر Agent اعتبار مستقل داشته باشد؛ HTTPS برای اتصال remote لازم باشد؛ migration صریح و قابل گزارش باشد؛ uninstall دادهٔ تجاری/backup را پاک نکند؛ نصب خراب یا readiness ناموفق به موفقیت ختم نشود.
- وضعیت فعلی: باز؛ در این commit فقط نقشهٔ راه و معیارهای پذیرش به‌روز می‌شوند. هنوز هیچ فایل Setup.exe / MSI به‌عنوان آماده معرفی نمی‌شود.
- معیار بستن: بسته از SHA مشخص ساخته و hash-manifest تولید شود؛ دو installer از سورس یکسان نسخه‌دار باشند؛ روی ماشین پاک نصب شوند؛ Server Ready، Desktop login و Agent heartbeat/Health Probe واقعی تأیید و شواهد نگهداری شود؛ failure/restart/uninstall بررسی شود.


## 2026-10-09 — Deployment payload builder / PowerShell parser regression

- Symptom: the first Quick Validation after adding `scripts/publish-deployment-payloads.ps1` stopped in the PowerShell parser gate before build or packaging; `deployment-payload` was skipped by its dependency gate. No installer or payload artifact was produced by this attempt.
- Root cause from source inspection: the error message interpolated `$LASTEXITCODE` immediately followed by `:`, which PowerShell parses as an invalid scoped-variable reference. The interpolation is now explicitly delimited as `${LASTEXITCODE}`.
- Guard improvement: the CI parser gate now prints parser ErrorId/Message/Extent before failing, so a future script syntax defect carries a line/extent diagnostic instead of only a filename.
- Verification: the syntax fix passed Quick Validation and the deployment-payload job in [run #118](https://github.com/alizebal73/5/actions/runs/37950006602). The subsequent Agent ProgramData expansion was separately tested on SHA `a00f6911ce584114fab70bfb2fba25f9e6deaaa0` in [Quick #119](https://github.com/alizebal73/5/actions/runs/37950593296) and [Full Foundation #192](https://github.com/alizebal73/5/actions/runs/37950593393).
- Rollback: revert this narrow syntax/diagnostics fix if it causes a regression; do not suppress the parser gate.


## 2026-10-09 — Packaging preflight found Agent ProgramData path expansion gap

- Observation: Agent `appsettings.json` configures `RootPath` as `%ProgramData%\\GameNet Manager\\Agent`, but the options validation and storage code previously passed the string directly to `Path.IsPathFullyQualified` / `Path.GetFullPath`. .NET configuration does not automatically expand Windows `%NAME%` tokens; a clean installed service could therefore fail startup or resolve the wrong location.
- Fix: `AgentIdentityOptions.ResolveRootPath()` now explicitly expands environment variables, rejects unresolved/non-absolute values, and returns a normalized absolute path. Startup validation plus both identity and DPAPI credential stores use the same resolver.
- Regression evidence added: unit test sets a temporary environment variable and verifies expansion/normalization. Actual test execution is pending the new Quick Validation run.
- Impact: do not use the payload from SHA `dc664185068fe3473fcb3f76e857cc2854126e92` for installation; the corrected pack must be built from the fix SHA.
- Rollback: revert the narrow option resolver + call sites + test as a unit only if the regression test exposes an incompatibility; never disable absolute-path validation.


## 2026-10-09 — Deployment payload PowerShell parser regression after migration-mode refactor

- Symptom: Quick Validation on SHA 4e50b1ca5903b3fd77008e4516d289b74795d7a4 failed at the parser gate before build/tests; deployment payload was skipped. No artifacts were published.
- Root cause: removing the EF bundle block left an outer try statement with no catch/finally in publish-deployment-payloads.ps1. The nested Push-Location try/finally was valid but the wrapper was not.
- Fix: removed the unnecessary outer wrapper while retaining the inner Push-Location/Pop-Location finally, so working-directory restoration remains guaranteed.
- Verification: pending new Quick Validation, Full Foundation Certification and payload job on the fix SHA.
- Rollback: revert only this script restructuring if it causes a regression; do not bypass parser validation.


## 2026-10-09 — DPAPI platform compatibility guard

- Result after the PowerShell parser fix: the source-size/architecture/documentation gates and PowerShell parser passed; canonical Release build stopped on .NET analyzer CA1416 because ProtectedData.Unprotect is Windows-only while the Server project is not globally Windows-targeted.
- Fix: ProtectedServerSettings.Read now rejects non-Windows execution explicitly before invoking DPAPI. Tests route protection through the same explicit OS guard. The platform guard is deliberate; no analyzer warning was disabled and no fallback encryption scheme was introduced.
- Verification: the final Windows guard/exception choice passed Quick Validation on SHA `c5918d4627955acc46eab1bfeac6501f05be3a00` in [run #123](https://github.com/alizebal73/5/actions/runs/37954272590); the complete migration/settings implementation including that guard passed [Full Foundation #194](https://github.com/alizebal73/5/actions/runs/37955024118) on SHA `c8a20caabad2c963c6a4b1cbcac7e3cbcb3575f7`.


## 2026-10-09 — Placeholder guard and OS-only exception type

- Follow-up verification stopped in the project placeholder guard before build: it treats PlatformNotSupportedException as a forbidden runtime placeholder even when paired with an explicit Windows check.
- The Windows-only fail-closed branch now uses InvalidOperationException with an explicit Windows DPAPI message, preserving the runtime platform guard and keeping the repository's placeholder policy unchanged.
- Verification: the revised guard passed [Quick Validation #123](https://github.com/alizebal73/5/actions/runs/37954272590), and the same source path was covered by [Full Foundation #194](https://github.com/alizebal73/5/actions/runs/37955024118).


## 2026-10-09 — Migration-only bounded runtime proof and package manifest isolation

- PostgreSQL certification now runs the built Server apphost directly with --migrate-only, requires exit within 90 seconds, checks schema-current confirmation, and rejects listener-start output. A regression cannot hang CI indefinitely or mistake a long-running API host for a completed migration.
- Payload builder now includes write-protected-server-settings.ps1 in the Server/Database archive and emits per-package manifests: Server/Desktop/Database ZIP does not list Agent binaries; Agent ZIP does not list Server/Desktop files. A combined build manifest remains an external CI artifact.
- Verification: Full Foundation Certification #194 passed on SHA `c8a20caabad2c963c6a4b1cbcac7e3cbcb3575f7`; Quick Validation and the subsequent payload integrity build passed. PostgreSQL migration, backup/restore, Desktop runtime and Agent runtime gates are green. The latest packaging-only/doc SHA `b7e232d068fb6c085716980249a83abb205961b3` passed [Quick + deployment-payload #125](https://github.com/alizebal73/5/actions/runs/37955819051). Artifact `11628326329` is `gamenet-deployment-payload-b7e232d068fb6c085716980249a83abb205961b3`.
- Package integrity verification now runs before upload: each ZIP is opened, every manifest-listed item has its SHA-256 and byte length recomputed and matched, and the embedded `release-manifest.json` is compared to the specific validated package manifest. `SHA256SUMS.txt` uses relative paths so identical manifest basenames cannot collide.
- The artifact remains a **payload only**, with `installerReady=false`; it is not a Setup.exe/MSI, has not been installed on either server PC or client PC, and does not certify service registration, TLS provisioning, or uninstall/update/rollback.


## 2026-10-09 — Package archive integrity check and unambiguous checksum paths

- The payload builder now opens each produced ZIP and verifies every manifest-listed file exists with the expected size and SHA-256, plus checks that the embedded root release-manifest.json exactly matches the validated package manifest.
- SHA256SUMS.txt records paths relative to the versioned output root instead of basenames only, so the full manifest and the two package manifests cannot collide under the same filename.
- Full Foundation #194 on SHA c8a20caabad2c963c6a4b1cbcac7e3cbcb3575f7 passed. The previous package artifact (11626939566) remains payload-only and is superseded pending these extra integrity checks.
- Verification outcome: [Quick Validation + payload build/upload #125](https://github.com/alizebal73/5/actions/runs/37955819051) passed on SHA `b7e232d068fb6c085716980249a83abb205961b3`; artifact `11628326329` was uploaded. This is still a payload-only artifact, not an installer or two-PC install certificate.


## 2026-10-09 — Production HTTPS listener / certificate provisioning gate

- Changed on `7ab5108b342c74d88faf87ba6303a1c08ac9de9b`: Production payload now configures Kestrel HTTPS on `0.0.0.0:5081`; the TLS certificate path/password are supplied from the Server's DPAPI-protected configuration. The protected-settings allowlist validates absolute PFX path and password length.
- Added reusable `scripts/modules/ServerTlsCertificate.psm1` to create and verify an IP-SAN Server Authentication certificate, export the private PFX separately from the public `.cer`, reopen the PFX to verify the private key, expose expiry and SHA-256 fingerprint, reject overwrite, and clean temporary certificate-store state.
- Added `scripts/verify-server-tls-certificate.ps1` and wired it into canonical verification. PowerShell parser checks include both `.ps1` and `.psm1`; Foundation triggers on these config/script paths.
- Exact-SHA regression evidence: [Quick Validation #128](https://github.com/alizebal73/5/actions/runs/37961799437) and [Full Foundation #196](https://github.com/alizebal73/5/actions/runs/37961799463) succeeded on `7ab5108b342c74d88faf87ba6303a1c08ac9de9b`. Full Foundation passed PowerShell parser, canonical build/tests (including certificate generation/export/reopen/profile/no-overwrite), PostgreSQL, isolated backup/restore, Desktop runtime and Agent runtime.
- The same exact SHA produced and uploaded payload artifact [gamenet-deployment-payload-7ab5108b342c74d88faf87ba6303a1c08ac9de9b](https://api.github.com/repos/alizebal73/5/actions/artifacts/11632326010) (157,201,938 bytes; digest `sha256:dfe2f180df6d41d3a0526fb4e315785f40f8b28bf913de212fa0ccb953def79b`; expires 2026-10-16). It is still ZIP payloads, not Setup.exe/MSI.
- Open risks: client-side fingerprint-verified trust installation; external ProgramData runtime configuration for Desktop/Agent; actual Production HTTPS health/login/Agent handshake over LAN; bootstrap-secret erasure after first owner; service installation and physical two-PC validation. Certificate generation is automated-test-certified, not yet certified in a complete on-machine installation.

## 2026-10-10 — Protected Server settings migration validation regression

- Symptom: Quick Validation failed two Server unit tests on merge-checkout SHA `429d764c1529b5a1151e3aac229c27b1f74a0fbc` (PR head `0eaebb4ea656f9298d7301e50c3f0c2613c0127a`; run [38046791986](https://github.com/alizebal73/5/actions/runs/38046791986)). One test exposed a setup-only protected settings file being rejected because the reader indexed missing deployment-secret keys; the other encoded the previous assumption that the database connection string must be present in `GameNetOptions`.
- Root cause: `ProtectedServerSettings.Read` still unconditionally validated JWT-signing and Agent-provisioning keys after the new secret-store migration made those values optional legacy entries. Separately, the old options test no longer matched the active design, where EF Core receives the PostgreSQL connection string through `IDatabaseConnectionSecret` rather than ordinary configuration.
- Fix: remove the two leftover unconditional signing/provisioning-key checks while retaining validation for those keys when present in legacy files; rename and update the options test to assert that ordinary `GameNetOptions` configuration can omit deployment credentials.
- Regression coverage: `Protected_setup_settings_can_omit_runtime_deployment_secrets` and the revised options-validation test.
- Verification: Quick Validation #196 succeeded on merge checkout `ab46b46` for prior PR head `3b43f269e3bfb42e3b5b52e8df9be887fc68d129` merged with base `55a340305f9eba3bc8f9a1ce65fb37bedbae7580`: [run 38047147586](https://github.com/alizebal73/5/actions/runs/38047147586). Both the PowerShell parser gate and canonical Release build/tests passed. This evidence predates the subsequent bootstrap-settings ACL hardening entry below.
- Rollback: revert only this bounded validation/test correction if the regression tests expose an incompatibility. Do not reintroduce runtime secrets into the protected setup settings file or `GameNetOptions`.

## 2026-10-10 — Protect bootstrap settings ACL at creation time

- Symptom: `scripts/write-protected-server-settings.ps1` wrote the machine-DPAPI-protected setup settings file using the directory's inherited ACL and restricted the file ACL only after writing the ciphertext. Because the containing configuration directory allows ordinary Users read/execute, this created a short window in which a local user could read the encrypted bootstrap-secret blob.
- Root cause: file creation and ACL hardening were separate filesystem operations; DPAPI `LocalMachine` protection does not itself restrict which local accounts can read a ciphertext file.
- Fix: create the file with a protected, explicit DACL from the first filesystem operation: SYSTEM and local Administrators receive FullControl, while the configured Server service identity receives Read. Reject a service-account principal that resolves to SYSTEM or Administrators. Retain the subsequent `icacls` application as defense in depth; no existing file is overwritten.
- Regression coverage: the canonical PowerShell parser gate must pass on the updated script. CI parsing alone does not prove effective Windows ACL behavior; a dedicated Windows setup/service test remains required.
- Verification: code committed as `20884b151f6c8dc78c643a5c1eceb3cc6c101a99`; Quick Validation for this updated script and the resulting exact PR head is pending.
- Rollback: revert only this file-creation ACL change if the Windows/API verification identifies an incompatibility. Never restore create-then-tighten ACL ordering for the DPAPI-protected bootstrap file.


## 2026-10-10 — Enforce protected setup settings at Production startup

- Finding: the Server computed `protectedSettingsEnabled` from configuration but did not reject a Production configuration where the flag was missing or false. It also honored `GAMENET_PROTECTED_SETTINGS_FILE` in Production, allowing a noncanonical DPAPI settings-file path even though the deployment contract specifies the ACL-restricted ProgramData location.
- Risk: Production could start without loading the expected protected setup settings, or use an explicitly supplied alternate path. That weakened the fail-closed boundary for the initial Owner bootstrap secret.
- Fix: add Production startup policy guards requiring `GameNet:ProtectedSettings:Enabled=true` and rejecting the protected-settings path override in Production. Non-Production test hosts retain the explicit path seam.
- Regression coverage: added policy tests for Production rejection, valid Production settings, and Development test-path compatibility.
- Verification: Quick Validation #205 passed for PR head `12e28216968f79b1947ffdc316d4ec1ccdb357f2` on merge checkout `478ccd91fed345880c71c0604993ab9d4897ffdc` (base `55a340305f9eba3bc8f9a1ce65fb37bedbae7580`): [run 38048106104](https://github.com/alizebal73/5/actions/runs/38048106104). PowerShell parsing passed; Release build had 0 warnings/errors; 143 tests passed with 0 failures/skips (Server 64, Agent 22, Desktop 18, Shared 38, Contract 1). Physical service identity, effective ACL, DPAPI restart and TLS private-key checks remain unverified.
- Rollback: revert only the new Production policy enforcement and its tests if a supported Production configuration proves incompatible. Do not restore an unguarded alternate settings path or permit Production startup without protected settings.


## 2026-10-10 — Clear Agent provisioning-key comparison buffers

- Finding: the provisioning-key validator correctly used a fixed-time comparison, but left the UTF-8 byte arrays containing both the protected expected key and the supplied request header to ordinary garbage collection. The credential revoke endpoint also did not consistently set `Cache-Control: no-store` across successful and rejected responses.
- Fix: zero both comparison buffers in a `finally` block and apply `Cache-Control: no-store` before validating provision, rotate and revoke requests, so secret-bearing responses and authorization failures are not cached.
- Scope limit: these changes are hygiene hardening only. The provision/rotate/revoke routes still rely on the shared protected provisioning key; the independently authenticated management issue-token flow with atomic single-use redemption remains a separate required slice.
- Verification: Quick Validation for this code change and the resulting current PR head is pending; prior CI success does not certify this commit.
- Rollback: revert only the buffer-clearing and cache-policy changes if a verified compatibility issue arises; do not weaken the fixed-time key comparison.


## 2026-10-10 — Restore the documented read-only Manager preflight

- Finding: `docs/operations/initial-admin-setup.md` required a read-only preflight from `scripts/inspect-manager-runtime-state.ps1`, but that script did not exist in the active integration target. An operator could not produce the promised evidence before making setup changes.
- Fix: add the read-only preflight. It reports Server service state/logon account, service SID type and SID, public listener settings, certificate metadata, listener presence, and ACL entries for ProgramData settings/secrets paths and the TLS machine private-key file. It intentionally withholds the raw service command line and never opens/decrypts the protected secret payloads.
- Safety boundary: the script is observational only. Its report is evidence for human review, not proof of effective service permissions, DPAPI restart access or an HTTPS handshake; provisioning remains a separate explicit step.
- Verification: the canonical PowerShell parser gate and Release build/tests on the final current PR head are required; prior CI runs do not cover this new script.
- Rollback: remove this script only if a supported Windows environment demonstrates a concrete compatibility issue; otherwise preserve the read-only preflight and correct it without adding machine-changing behavior.

## 2026-10-10 — Apply no-store to every Agent token response

- Finding: `/api/v1/agent/auth/token` only set `Cache-Control: no-store` after successful authentication. Disabled-authentication and rejected-credential responses took an earlier return path without the same explicit cache policy.
- Fix: set the response cache policy at the start of the route, before the disabled-authentication check and credential authentication, matching the provision/rotate/revoke endpoints.
- Verification: Quick Validation must run against the resulting exact PR head. The workflow currently uses a self-hosted Windows runner; queued status is not a pass or failure. No live runtime or installed service was exercised.
- Rollback: revert only this response-header placement if a supported integration test demonstrates a compatibility issue; retain no-store for responses that may carry credentials or tokens.

## 2026-10-10 — Include Manager parent ACL in the read-only preflight

- Finding: the preflight reported ACLs for the Config and Secrets child paths but omitted the `%ProgramData%\GameNet Manager` parent directory. Parent-level delete-child/write rights can affect whether protected child directories can be replaced even when those children have restrictive DACLs.
- Fix: include the Manager root's existence, owner, inheritance protection and access-rule list in the same read-only report. The script remains observational; it does not change ACLs or certify their effective safety.
- Verification: Quick Validation must pass on the resulting exact PR head. The earlier green run does not include this final preflight adjustment.
- Rollback: remove only the additional Manager-root report if a verified supported Windows environment shows a concrete incompatibility; do not turn the preflight into a machine-changing ACL repair tool.
