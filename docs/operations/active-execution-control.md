# Active Execution Control — GameNet 5

**Snapshot date:** 2026-10-12  
**Purpose:** Prevent rework loops by separating closed engineering stages from distinct operational/release gates. This is the active execution control document. The master plan remains the product plan; this file governs the current queue and evidence rules.

## Current queue — live audit update (2026-10-12)

The live Integration head at the start of this audit was `cd1f2ecfd0f0c7eb238669198afa8a382e0e2c43`; this is a historical observed SHA, not a permanent pointer.

1. [PR #51](https://github.com/alizebal73/5/pull/51) corrects stale readiness/governance docs. Quick Validation passed on its exact GitHub merge candidate; PR remains open for review.
2. [PR #53](https://github.com/alizebal73/5/pull/53) wires Full Foundation/PostgreSQL certification into the required Integration `quick-validation` path. Exact tested merge candidate `477912e2a6e269cc1a5e2e3ba081728d200c075d` passed Quick, PostgreSQL/migrations/concurrency, backup/restore, Desktop and Agent checks. PR remains open; merge it after review before persistence-heavy commercial slices.
3. [Issue #55](https://github.com/alizebal73/5/issues/55) records the Stage 2 inventory mismatch. The canonical inventory is `src/Server/Modules/README.md`; `scripts/check-architecture.ps1` now rejects missing module roots, root READMEs, or catalog entries. Stage 2 must close before new business implementation.
4. [Issue #54](https://github.com/alizebal73/5/issues/54) tracks the separate governance configuration gap: protected Rulesets still do not require up-to-date branches or an approving human review.
5. Do not merge older-base feature/secret-store/UI branches wholesale. Carry forward reviewed requirements and rules, not their divergent code tree. Do not change `main`.

No PR is automatically merged here. Every merge requires the repository's intended review decision and exact-candidate CI evidence. Physical service/LAN/UI and installer acceptance remain final-release gates, not Stage 2 blockers.


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

The active work remains requirement-tracked product implementation in Master Build Plan order. The physical, interactive, and installer/release gates below are deferred milestones, not the immediate next action.

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
| [#15](https://github.com/alizebal73/5/pull/15) | ProgramData endpoints, trusted LAN TLS and overlapping workflow files | Keep Draft; separate runtime source from runner/workflow edits; reconcile source against current Integration before review. |
| [#17](https://github.com/alizebal73/5/pull/17) | Manual LAN smoke evidence from 2026-10-10 | Preserve this as historical evidence only; do not count it as final acceptance or merge stale-base planning edits wholesale. Collect fresh controlled evidence against the feature-complete Setup candidate in the final acceptance phase. |
| [#18](https://github.com/alizebal73/5/pull/18) | Proposed secret lifecycle ADR | Keep as design review only until compared with the current secret-store implementation and ADR naming; do not treat a Proposed ADR as approved policy. |
| [#20](https://github.com/alizebal73/5/pull/20) | DPAPI Server secret-store implementation | Its `DpapiServerSecretStore.cs` blob is identical to current Integration. Audit every changed path against Integration; if the entire implementation is already present, close as superseded with a comment and retain the branch. Never merge duplicate code. |
| [#21](https://github.com/alizebal73/5/pull/21) | Initial Desktop operator shell | Keep Draft only if it has unique UI behavior absent from Integration; compare exact XAML/ViewModel/tests before any merge. Do not use an old base as the integration target. |
| [#23](https://github.com/alizebal73/5/pull/23) | Integration branch proposed back into old `feature/operator-identity-v1` base | Closed as obsolete without merging; source branch retained. Do not reopen or merge backwards without a new, explicit requirement. |
| [#24](https://github.com/alizebal73/5/pull/24) | StationBoard view extraction | Keep Draft as a focused UI refactor; compare with the integrated UI and port only unique view work during feature completion if needed. Interactive acceptance remains deferred until the feature-complete Setup candidate exists. |
| [#31](https://github.com/alizebal73/5/pull/31) | Integration runner/CI and readiness consolidation | Closed without merging after #44 superseded overlapping workflow changes; source branch retained for a later focused extraction of the test-only service-registration helper and runbook. |
| [#32](https://github.com/alizebal73/5/pull/32) | Foundation hosted-Windows runner isolation | Merged at `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; exact post-merge Full Foundation #38182502053 passed. |
| [#44](https://github.com/alizebal73/5/pull/44) | Integration hosted-Windows CI standardization and safe test-payload migration-bundle build | Merged at `161b4cbbbe16cd3fd4046f174a1c09380139c67b`; candidate Quick push/PR, Full Foundation and Runtime payload passed; post-merge Quick #38183458461 and Full Foundation #38183458486 passed on the exact merge SHA. |

Closed source PRs #35–#39 remain superseded by #40; their source branches were retained. Do not reopen them or merge their branches separately without a newly demonstrated, unique defect.

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
- The six remaining open Draft PRs (#15, #17, #18, #20, #21 and #24) each have an explicit later-gate disposition; #31 is closed as superseded and #32/#44 are merged.
- Future progress reports state only what changed, which exact SHA was tested, which gate is now closed, and the one next action.
