# Active Execution Control — GameNet 5

**Snapshot date:** 2026-10-12  
**Purpose:** Prevent rework loops by separating closed engineering stages from distinct operational/release gates. This is the active execution control document. The master plan remains the product plan; this file governs the current queue and evidence rules.


## Live execution order — read-back 2026-10-14 (supersedes older queue snapshots below)

Older dated sections remain as historical evidence. For current action, this section is authoritative, but read live PR heads/checks again before every merge.

### Verified refs at this read-back
- Integration: integration/runtime-operator-v1 = 6a6cda5bf0c199d7163323d69f4c0fbfef09d58f (PR #90 squash merge).
- Main: main = b9288471f2047570eaf8d0d6552cf87bc0ddc214 (intentionally unchanged starter; this is the exact 40-character SHA).
- Foundation: foundation/runtime-final-v2 = 71ef6534331e6ba5a8a460fd5b8846fe40e8567a.
- Passing CI on a candidate SHA is evidence only for that SHA. It does not certify later rebases, post-merge SHA, installed Service behavior or release readiness.

### Current execution order and timebox
**Immediate bug-fix queue: target no more than 2–3 working days after 2026-10-14, subject to actual CI failures. Do not use this timebox to waive checks.**

1. **PR #90 — DONE.** Merged to Integration at `6a6cda5bf0c199d7163323d69f4c0fbfef09d58f`. It upgrades Microsoft .NET packages to 10.0.13 and SDK to 10.0.402 and keeps the dependency vulnerability scanner fail-closed. The candidate passed Quick/Foundation and Runtime Payload before merge; post-merge checks must be read from the live SHA when available.
2. **PR #94 — current PostgreSQL retry fix replacing old PR #87.** Head at this read-back: `72a6ccbeba8536ae8734e535a7e739862bf132a8`, directly based on post-#90 Integration and limited to `EfTransactionCoordinator.cs`. Runtime Payload run [#38560171404](https://github.com/alizebal73/5/actions/runs/38560171404) succeeded. Full Foundation / PostgreSQL certification [run #38560171403](https://github.com/alizebal73/5/actions/runs/38560171403) was still in progress at the last read-back. Do not merge until all required checks complete successfully on this exact head, especially the concurrent last-active-Owner invariant, audit and rollback behavior. If any code changes, require fresh exact-head runs.
3. **Old PR #87 — superseded by #94.** Once #94 has passed and is merged, close #87 as superseded; do not merge its old-base candidate.
4. **PR #88 — rebuild as redaction-only after #94 merges.** Current PR #88 contains both the retry change and log-redaction change even though its base is Integration. Create a clean branch from the new Integration head and port only `ExceptionHandlingMiddleware` redaction plus its sentinel regression test. Its final diff must not repeat the retry fix. Run exact-head Quick and Foundation; confirm secrets/exception sentinels do not appear in log output before merging.
5. **PR #91 — staged per-device Agent credential rotation is deferred, not cancelled.** It is a substantial security feature and passed its earlier exact-candidate Quick/Foundation gates, but is based on the prior #90/#87 stack. Do not merge the stale stack. Defer implementation integration until the bounded PC-control feasibility task (#92) and the first Session slice are scoped; rebuild it later against the then-current Integration. The old one-step rotation route must not be treated as safe or used operationally before the staged protocol is accepted. Rotation/recovery remains a mandatory pre-pilot/release gate; Issue #74 remains open.
6. **Human review and runner.** At the last read-back, no submitted review was found for the main security PRs and the configured approval count was zero. Delegated technical review is not independent human approval. Current CI uses GitHub-hosted `windows-2025`; resolve local-runner policy in [Issue #67](https://github.com/alizebal73/5/issues/67) separately. Never run untrusted PR code on a persistent personal runner or relabel hosted results as local-runner evidence.

Capture live head SHA, base SHA, all required check conclusions, review/disposition and merge SHA for each merge. Re-read Integration after each merge.

### Stage 3 exit rule — bounded and evidence-based
Close the immediate bug-fix track after #90, #94 and the clean redaction-only PR have merged with exact-SHA evidence; timebox that track to 2–3 working days and record any actual blocker. This does **not** close Issue #74 or claim all secret lifecycle work is complete. Move non-blocking rotation/recovery items to named pre-pilot/release gates with reason, owner, affected capability and evidence required. A deferral never authorizes operational use of the unsafe path.

Remaining Issue #74 items include JWT active/previous signing-key rotation, PostgreSQL runtime credential rotation, provisioning-key rotation, protected-state/host-replacement recovery, and wider redaction. Preserve [Issue #63's idempotency replay contract](https://github.com/alizebal73/5/issues/63), including retained records and fingerprint-key compatibility.

### First product-risk task — Issue #92
Complete a concrete feasibility test on a disposable test PC/VM by **2026-10-18**, not a documentation-only exercise. Establish supported Windows edition and customer privilege boundary; demonstrate the minimum lock/unlock lifecycle and failure behavior for server/network loss, Agent/Windows restart and stale/duplicate command. Record observed results and bypass limits. This early spike is not final Setup acceptance and does not authorize changes to the live Manager/shop PC. If physical hardware is unavailable in the current environment, record that as BLOCKED with a specific test-PC requirement; do not claim PASS.

### First product journey and mandatory pre-Slice prerequisite
1. Freeze V1 scope and persistent money/time decisions with requirement IDs in [requirements traceability](../planning/requirements-traceability.md).
2. Before Session V1, complete [Issue #95](https://github.com/alizebal73/5/issues/95): characterize the current Identity mutation path and extract only the proven common transaction/idempotency/replay mechanics if the same contract is truly reusable. Keep permission checks, domain validation, reason requirements and audit before/after semantics explicit within each use case. Preserve behavior with characterization tests; do not rewrite the 28 KB service merely because of file size or build a generic framework without two real consumers.
3. Build a narrow PC journey in ordered slices: minimal Customer/Walk-in -> fixed PC tariff snapshot -> Session start/end -> invoice -> cash payment -> durable Ledger and Audit, all through WPF, Server and PostgreSQL. The first slice must exercise the verified command pipeline. Only then add wallet, debt, free credit/time, discount and advanced tariff periods.
4. Each slice includes versioned Shared/API command contract, authorization/error mapping, migration, Server use case, database constraints, stable idempotency, unit/PostgreSQL concurrency/regression tests and WPF loading/empty/offline/error/forbidden states. External Agent effects must not run inside a database transaction: commit authoritative state and any required outbox record atomically, then dispatch after commit.
5. After the cash journey passes, execute an early 2–3-station **shadow trial** using a disposable/test database and test build under [Issue #96](https://github.com/alizebal73/5/issues/96). Capture operator feedback early, but do not process real customer sessions/money, replace the legacy source of truth, or modify the live Manager PC. This is not the final production pilot.
6. Continue PS5/foosball as server-managed timed stations, then wallet/debt, shifts/reconciliation/reports, inventory/Buffet, VIP/discounts, Games and only later GameAccounts after its security/legal decision.
7. Build Setup/Updater/rollback after planned product scope is traceable. Final installed-Service/TLS/DPAPI/ACL/two-PC LAN/UI acceptance remains under [Issue #16](https://github.com/alizebal73/5/issues/16) and [Issue #34](https://github.com/alizebal73/5/issues/34). Only after those gates pass may the formal real-shop pilot and release/main promotion proceed.

### Cross-cutting invariants for every product slice
- **Money:** persist amounts as checked integer `long` in Toman. No binary floating point for money. Use one documented integer/fixed-point/rational calculation and rounding policy. Ledger is append-only; corrections are reversal/adjustment entries with reason, not mutation/deletion. Cash received, wallet value, bonus/free credit, free time and debt stay separate.
- **Tariff/session:** use Server UTC time; convert to Jalali only in presentation. Snapshot the effective tariff at session start. Explicit versioned state transitions and a PostgreSQL constraint enforce at most one active session per station. Decide/test tariff boundary crossings, pause/resume, disconnect, restart and recovery.
- **Idempotency/concurrency:** one stable operation key per user intent across retries; same key plus changed payload conflicts. Test double-click, lost response, concurrent operators, duplicate payment/session, simultaneous wallet changes and restart. Database unique/check/FK constraints are the final invariant guard.
- **Agent/control:** Server is authoritative; stale commands are fenced by device/lease/session generation. Persist required state/outbox before dispatch; reconnect reconciles from Server. Explicitly decide behavior after Server/network/Agent/Windows restart.
- **Audit/security:** actor, reason, correlation, and suitable before/after evidence for privileged/financial corrections. Never log credentials, tokens, connection-string secrets or private-key contents. Fail closed on corrupt/unsupported secret state; no silent key regeneration or fallback.
- **Database/contracts:** never edit an applied migration; use expand/compatible transition/contract. Test upgrade and restore with disposable previous-version data. Version Shared/API contracts intentionally and specify old Agent/Desktop compatibility.
- **Module ownership/UI:** Sessions owns time/lifecycle; Tariffs resolves price; Billing owns invoices/payment allocations; Wallet owns stored value/free value/debt; Inventory owns stock movements; Agent only executes authorized OS commands. WPF displays Server results, never computes authoritative price/time/money. Test fa-IR RTL, en-US LTR, Persian/Arabic digits, ی/ي, ک/ك, Toman formatting and loading/empty/offline/error/forbidden states.
- **Testing/process:** do not weaken/disable a failing test for green CI. Review money/session code directly. Split classes by responsibility/change pressure, not arbitrary kilobyte limits. Each slice links requirement IDs and exact-SHA evidence; CI and final installed-service/physical acceptance are distinct gates.
- **Scope discipline:** one primary slice at a time. No generic infrastructure without demonstrated need. Do not merge stale Drafts #15/#17/#21 wholesale.

## Current queue — live audit update (2026-10-13; snapshot before docs-only PR merge)

The Integration branch was read at `610ad37739e4ea32ee4e0d4d24d011b8e413e908` immediately after PR #82 merged and before this status-reconciliation PR. This SHA is immutable evidence, not a pointer to Integration after this PR merges.

1. **Master Plan Stage 1 — Foundation: CLOSED.** Keep its exact-SHA checkpoint in [Stable Checkpoint](../operations/stable-checkpoint.md); final installer/physical acceptance does not reopen this stage.
2. **Master Plan Stage 2 — module inventory and architecture guard: CLOSED.** PR #56 merged and Issue #55 closed; 19 module roots/ownership records and the architecture guard are integrated. This does not claim that all business modules are implemented.
3. **Identity invariant tests — technically complete, closure reconciliation in progress.** PR #82 merged at `610ad37739e4ea32ee4e0d4d24d011b8e413e908`. PR-head exact checks passed in [run #38461912514](https://github.com/alizebal73/5/actions/runs/38461912514); post-merge Quick [#38462799723](https://github.com/alizebal73/5/actions/runs/38462799723) and Full Foundation [#38462799563](https://github.com/alizebal73/5/actions/runs/38462799563) both succeeded on that same SHA. Issue #75 closes after this docs/evidence update merges.
4. **Current platform/security stage: Stage 3 remains ACTIVE.** PR #73 accepted ADR-0002 as architecture; it did not complete rotation/recovery/redaction. Keep [Issue #74](https://github.com/alizebal73/5/issues/74) open and gate operational use on the particular missing guarantees. Do not declare canonical roadmap step 4 (first complete PC business journey) active until Stage 3 exit criteria have exact-SHA evidence or the owner explicitly defers named remaining gates.
5. **Next queue after this documentation change:** [Issue #79](https://github.com/alizebal73/5/issues/79), a focused, behavior-preserving StationBoard view extraction plus a persistent Action Catalog. It is preparation for product UI slices, not the first PC business journey. Continue to keep one primary gate active.
6. **Hosted/local runner trust boundary:** current exact CI evidence uses GitHub-hosted `windows-2025`. [Issue #67](https://github.com/alizebal73/5/issues/67) is still the authority on the conflict with the owner's local-runner preference; do not call these local-runner results or silently change runner labels/trust.
7. **Open Draft PRs:** #15 (TLS/ProgramData source with stale workflow diffs), #17 (historical process-based smoke report) and #21 (old-base Desktop shell). Keep as evidence/compare sources; do not merge them wholesale. PRs #18, #20 and #24 are closed/superseded; their branches remain for history.
8. **Final acceptance is deferred** until the planned product scope and a real Setup.exe/MSI candidate exist. Issues #16 and #34 own installation, update/rollback, actual Service identities/ACL/DPAPI/TLS and physical LAN acceptance. Do not modify the existing Manager PC.

## 1. SHA evidence versus live branch heads

**Important:** the branch values below are a timestamped historical read-back taken immediately after PR #42 merged on 2026-10-12. They are not a permanent live pointer. Documentation PRs themselves advance Integration's commit SHA, so the live Integration head must always be read from GitHub immediately before an operation; never infer it from a SHA copied into this document.

| Ref / evidence role | SHA observed or verified | State and meaning |
|---|---|---|
| `main` | `b9288471f2047570eaf8d0d6552cf87bc0ddc214` | Protected; deliberately unchanged at the read-back. |
| `foundation/runtime-final-v2` | `be29687e637b709158a30204bb9213bfa4813950` | Protected; tip observed at the read-back, but not independently recertified at this exact SHA. Last explicitly recorded Stable Checkpoint certification is `c3a8ba482548e963479fbdf8538be62c4d15acfe`. Verify the live tip before dispatching the gate. |
| `integration/runtime-operator-v1` | `d2d8c626c678078c52b840fd0c9c751e7583fa91` | Protected; this was the head observed immediately after docs-control PR #42 merged. This SHA contains process/documentation updates; it is not a new runtime-code acceptance SHA. |
| Runtime code acceptance commit | `58717adc6c1553ef21221c3d484f093494ba1ae7` | PR #40 integration source merge; its Quick and Full Foundation checks passed. |
| Prior docs-only snapshot | `362513ad812aebef4ad22420f9b905d79490219d` | PR #41 docs-only merge; its tree passed Quick Validation. Historical evidence remains valid for that exact SHA. |

For execution-time live refs, use [the Integration branch page](https://github.com/alizebal73/5/tree/integration/runtime-operator-v1), [the Foundation branch page](https://github.com/alizebal73/5/tree/foundation/runtime-final-v2), or GitHub's branch list. These links/reads—not a SHA embedded in a Markdown file—are the live authority.

## 2. Code-integration checkpoint — CLOSED

This checkpoint means **code integration and its automated verification**, not physical deployment certification or commercial release. The numbered **Stage 1 in the Master Build Plan** means Foundation certification; that plan stage is separately closed with exact-SHA evidence in `docs/planning/master-build-plan.md`. These are related but distinct labels, not a moving acceptance criterion.

Evidence:
- PR [#40](https://github.com/alizebal73/5/pull/40) merged the integrated code. Candidate `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd` passed [Quick Validation #38180651116](https://github.com/alizebal73/5/actions/runs/38180651116), [Full Foundation #38180646187](https://github.com/alizebal73/5/actions/runs/38180646187) and [Runtime payload #38180651104](https://github.com/alizebal73/5/actions/runs/38180651104).
- Post-merge code SHA `58717adc6c1553ef21221c3d484f093494ba1ae7` passed [Quick Validation #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) and [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446).
- PR [#41](https://github.com/alizebal73/5/pull/41) made documentation-only updates and merged as `362513ad812aebef4ad22420f9b905d79490219d`. Its candidate passed [Quick Validation #38181520088](https://github.com/alizebal73/5/actions/runs/38181520088), and the pre-control Integration SHA `362513ad812aebef4ad22420f9b905d79490219d` passed [Quick Validation #38181599202](https://github.com/alizebal73/5/actions/runs/38181599202). This is historical evidence for that SHA, not a claim about a later live branch head.

**Do not reopen Stage 1** to repeat completed tests, certify a different Foundation ref, test installed Windows services, perform a visual WPF smoke, migrate runners, or build an installer. Track those under their own gates below. A later source-code change reopens only the gates affected by that code change.

## 3. Active development queue and deferred final acceptance

**Scheduling decision (2026-10-12):** Physical Runtime/LAN tests, interactive Windows UI acceptance, and end-to-end installer/release acceptance are deferred until the planned product scope is implemented and a real Setup.exe/MSI candidate has been built. Until then, continue automated CI, unit, integration, regression, and security checks for every applicable code slice and SHA; do not postpone those engineering checks. Do not start physical-machine acceptance now. During final acceptance, use isolated Windows PCs and a disposable database, and first review the read-only output of `scripts/inspect-manager-runtime-state.ps1` before touching any existing Manager machine. This decision does not reopen Master Plan Stage 1 or change its evidence.

The active stage is platform/security hardening and closure evidence, with Issue #75's direct Identity Audit gap now green on PR #82's exact merge SHA. Issue #74 remains open for secret rotation/recovery/redaction. After this documentation reconciliation, the next scoped preparation is Issue #79; do not activate the first complete PC business journey until Stage 3 exit criteria are proven or named gates are explicitly deferred. Physical, interactive and installer/release gates remain deferred milestones, not the immediate next action.

### Gate F — Foundation exact-tip certification — CLOSED for the recorded SHA

- The prior read-back target was `be29687e637b709158a30204bb9213bfa4813950`. After PR #32, Foundation tip `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` passed exact-SHA Full Foundation [run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053); this is the latest certified Foundation checkpoint in this record. Read the live branch head before each future dispatch or merge.
- PR [#32](https://github.com/alizebal73/5/pull/32) is now merged to Foundation at `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`. Its push-triggered [Full Foundation run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) **succeeded** on that exact SHA. The new Foundation tip `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` is now the latest certified checkpoint; its uploaded evidence is [artifact 11718566832](https://github.com/alizebal73/5/actions/runs/38182502053). Future protected Foundation pushes should automatically run the same gate. Because the workflow now runs on each `foundation/runtime-*` push, future protected-branch merges should automatically certify their resulting exact tip.
- For future Foundation changes, advance the Stable Checkpoint only after the resulting exact branch tip passes Full Foundation. On failure, fix only the observed root cause, add/retain the regression and rerun the relevant gate; do not repeat unrelated completed work.
- A successful run on a PR candidate or on Integration is not evidence that this exact Foundation SHA was certified.

### Gate R — Hosted Windows CI standardization — CLOSED for Foundation and Integration

Both automation branches now use GitHub-hosted `windows-2025`; this gate is closed for the exact commits and evidence below. It does not certify installed Windows Services, real LAN behavior or visual UI acceptance. Do not merge PR #15 wholesale or reopen superseded runner work.

1. [PR #32](https://github.com/alizebal73/5/pull/32) moved Foundation automation to `windows-2025`, added the runner-policy guard, isolated PostgreSQL to the disposable runner VM and added push-triggered exact-tip certification. Foundation SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` passed [Full Foundation #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053).
2. [PR #44](https://github.com/alizebal73/5/pull/44) moved Integration Quick Validation, Full Foundation and Runtime payload to `windows-2025`. Exact candidate `92de1b1fb6002165fc91c7f66b4ecec1c969c0ff` passed [Quick push #38183164928](https://github.com/alizebal73/5/actions/runs/38183164928), [Quick PR #38183167294](https://github.com/alizebal73/5/actions/runs/38183167294), [Full Foundation #38183164941](https://github.com/alizebal73/5/actions/runs/38183164941) and [Runtime payload #38183167300](https://github.com/alizebal73/5/actions/runs/38183167300). The merged Integration SHA `161b4cbbbe16cd3fd4046f174a1c09380139c67b` then passed [post-merge Quick #38183458461](https://github.com/alizebal73/5/actions/runs/38183458461) and [post-merge Full Foundation #38183458486](https://github.com/alizebal73/5/actions/runs/38183458486).
3. [PR #31](https://github.com/alizebal73/5/pull/31) was closed as superseded for overlapping runner/workflow changes; its branch was retained because it contains separate test-only service-registration tooling/runbook work. Do not merge it; re-cut only the unique helper as a focused PR when final acceptance begins after the feature-complete Setup candidate exists.
4. [PR #15](https://github.com/alizebal73/5/pull/15) remains separate: its ProgramData/TLS runtime source is not runner migration. Split and compare source changes before any future merge.
5. Every runner claim must identify exact SHA, actual runner type, disposable-DB boundary and run result. A status result for another SHA does not certify this SHA.

### Final Gate P — Physical Runtime acceptance (deferred until feature completion and Setup candidate)

Run this only after the planned product scope is complete and the Setup.exe/MSI candidate exists. Then use isolated Windows test machines and a disposable database. First review the read-only output from `scripts/inspect-manager-runtime-state.ps1`; do not provision secrets, clean ACLs, rotate TLS, or otherwise modify the existing Manager PC before preflight review and explicit approval.

During that final pass, record evidence for: installed Service identity; effective directory/file ACLs; DPAPI secret retrieval and TLS private-key access after service restart; enrollment replay/expiry/revocation; lease/heartbeat/fencing; and two-PC LAN reconnect. This gate cannot be marked passed from a ZIP artifact or CI result.

### Final Gate U — Interactive Desktop acceptance (deferred until feature completion and Setup candidate)

After the feature-complete Setup.exe/MSI candidate exists, verify fa-IR RTL and en-US LTR, navigation, focus/keyboard behavior, PasswordBox clearing, loading/offline/error states and permission-denied behavior on Windows. Record the build SHA and observed outcomes. Static resource/binding tests remain part of continuous CI; they do not replace this final interactive pass.

### Final Gate L — Installer and release acceptance (after the Setup candidate is built)

Build the actual Setup.exe/MSI candidate as a deliverable before entering final acceptance. Once the full planned feature scope and requirement traceability are complete, and automated CI is green on the exact candidate SHA, validate clean install, PostgreSQL install/upgrade/repair/uninstall, staged update/rollback, package signing and manifest/SBOM, backup/recovery, and then the staged 2–3-PC followed by ~15-station pilot. The runtime ZIP is not a substitute for a Setup candidate. These final checks never reopen Stage 1.

## 4. Open PR disposition register

| PR | Current purpose | Required disposition |
|---|---|---|
| [#15](https://github.com/alizebal73/5/pull/15) | ProgramData endpoints and trusted LAN TLS; older base also modifies workflow files | Keep Draft. Compare only unique runtime source with live Integration, exclude stale workflow changes, and do not modify the existing Manager PC. |
| [#17](https://github.com/alizebal73/5/pull/17) | Manual Runtime LAN smoke evidence from 2026-10-10 | Keep as historical evidence only. It ran Server/Agent as processes, not installed services; never count it as final installed-Service/two-PC acceptance or merge its stale planning snapshot wholesale. |
| [#21](https://github.com/alizebal73/5/pull/21) | Initial Desktop operator shell on an old base | Keep Draft as a comparison source only. Port only unique, still-required XAML/ViewModel/tests after the current view-extraction task is scoped. Do not merge the old base wholesale. |

Other older-base candidates #18, #20 and #24 are closed/superseded with branches retained. PR #51 was closed without merge as a stale status snapshot. PR #53 and #56 are merged. Current Stage 3 status and exact-SHA evidence are recorded above; this table lists only the three remaining open Draft PRs.

## 5. Anti-loop rules (mandatory)

1. **One active gate at a time.** No second PR touching the same workflow/verification file until the first disposition is recorded.
2. **Exact evidence ledger.** Every pass/fail records run URL, tested SHA, workflow, runner type and conclusion. A status result for another SHA is not relabeled as evidence for this SHA.
3. **No redundant reruns.** Do not rerun passing checks on the same tree unless required by a changed SHA, a stated policy or a credible nondeterminism incident. Retry only failed jobs after recording root cause where supported.
4. **Change-driven revalidation.** Source changes rerun affected code/runtime gates; workflow changes rerun the workflow under change; docs-only changes use Quick Validation unless the docs validator or policy explicitly requires more.
5. **No moving finish line.** Once acceptance criteria are met, close the stage. Record physical/installer work as a separate open gate instead of silently adding it to a closed stage.
6. **No branch archaeology without a decision.** Every open Draft PR must have one of: merge after exact evidence, rebase/split into the active queue, close as superseded with branch retained, or explicitly park for a named later gate.
7. **No unsafe machine changes.** Production/Manager machine modifications require read-only preflight, isolated reproduction where possible, explicit approval and rollback evidence.

## 6. Definition of done for this control problem

- Runtime-code acceptance SHA, timestamped branch read-back and live branch refs read at action time are kept separate. Static documentation must never claim its own commit SHA is the permanently current Integration head.
- Stage 1 remains closed and its evidence links are stable.
- The current Foundation-tip certification is one explicit action, not a recurring broad review.
- Hosted Windows runner migration is complete for both Foundation (#32) and Integration (#44); #31 is closed as superseded with its unique service-helper work retained on the source branch. Runtime endpoint/TLS source work (#15) remains separate.
- At the 2026-10-13 read-back, only #15, #17 and #21 remained open Draft PRs; #18, #20 and #24 were closed/superseded with branches retained. PR #51 was closed without merge; #53 and #56 are merged; #73, #76, #78, #80, #81 and #82 are merged. Issue #75's exact post-merge checks are green; Issue #74 remains open; Issue #79 is next queued UI-boundary preparation.
- Future progress reports state only what changed, which exact SHA was tested, which gate is now closed, and the one next action.
