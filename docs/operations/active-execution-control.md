# Active Execution Control — GameNet 5

**Snapshot date:** 2026-10-12  
**Purpose:** Prevent rework loops by separating closed engineering stages from distinct operational/release gates. This is the active execution control document. The master plan remains the product plan; this file governs the current queue and evidence rules.

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

## 2. Stage 1 decision — CLOSED

Stage 1 means **code integration and its automated verification**, not physical deployment certification or commercial release.

Evidence:
- PR [#40](https://github.com/alizebal73/5/pull/40) merged the integrated code. Candidate `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd` passed [Quick Validation #38180651116](https://github.com/alizebal73/5/actions/runs/38180651116), [Full Foundation #38180646187](https://github.com/alizebal73/5/actions/runs/38180646187) and [Runtime payload #38180651104](https://github.com/alizebal73/5/actions/runs/38180651104).
- Post-merge code SHA `58717adc6c1553ef21221c3d484f093494ba1ae7` passed [Quick Validation #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) and [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446).
- PR [#41](https://github.com/alizebal73/5/pull/41) made documentation-only updates and merged as `362513ad812aebef4ad22420f9b905d79490219d`. Its candidate passed [Quick Validation #38181520088](https://github.com/alizebal73/5/actions/runs/38181520088), and the pre-control Integration SHA `362513ad812aebef4ad22420f9b905d79490219d` passed [Quick Validation #38181599202](https://github.com/alizebal73/5/actions/runs/38181599202). This is historical evidence for that SHA, not a claim about a later live branch head.

**Do not reopen Stage 1** to repeat completed tests, certify a different Foundation ref, test installed Windows services, perform a visual WPF smoke, migrate runners, or build an installer. Track those under their own gates below. A later source-code change reopens only the gates affected by that code change.

## 3. Single active queue

### Gate F — Recertify the current Foundation tip

- Target recorded at the last read-back: `be29687e637b709158a30204bb9213bfa4813950` on `foundation/runtime-final-v2`. Re-read the live branch head immediately before dispatch; if it changed, use the actual current tip and record it.
- PR [#32](https://github.com/alizebal73/5/pull/32) is now merged to Foundation at `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`. Its new push-triggered [Full Foundation run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) is running on that exact SHA. Record its final result and tested checkout SHA in [Stable Checkpoint](stable-checkpoint.md) and the readiness register; do not mark Foundation certified until the run succeeds. Because the workflow now runs on each `foundation/runtime-*` push, future protected-branch merges should automatically certify their resulting exact tip.
- If it passes, update the active certification marker to that exact SHA. If it fails, fix only the observed root cause, add/retain the regression, and rerun the relevant gate; do not repeat unrelated completed work.
- A successful run on a PR candidate or on Integration is not evidence that this exact Foundation SHA was certified.

### Gate R — Choose and stabilize one CI runner policy

Foundation runner migration is complete in PR #32; the exact post-merge Full Foundation run is Gate F and remains in progress. Do not merge PR #15 or #31 wholesale, and do not change overlapping workflow files concurrently.

1. [PR #32](https://github.com/alizebal73/5/pull/32) has been merged to the protected Foundation branch after exact-head Full Foundation passed on its PR candidate. It now uses `windows-2025`, enforces a runner-policy guard, and triggers Full Foundation on pushes. The post-merge certification is [run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) on SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; that run must succeed before the new tip is called certified.
2. After the Foundation run finishes, reconcile [PR #31](https://github.com/alizebal73/5/pull/31) against the already-merged runner policy. Its base is stale and workflow diffs overlap; port only unique, needed Integration workflow changes onto the then-current Integration tip and run exact-head Quick + Full Foundation, or close #31 as superseded with source branch retained.
3. Review [PR #15](https://github.com/alizebal73/5/pull/15) separately: its ProgramData/TLS runtime code is not the same work item as runner migration. Separate runtime-source changes from overlapping workflow changes before proposing a merge.
4. Evidence for a runner change must identify the exact SHA, actual runner image/type, the PostgreSQL isolation boundary and Full Foundation outcome. Success on a self-hosted runner must never be reported as hosted-runner certification.

### Gate P — Physical Runtime acceptance (not executable from GitHub CI alone)

Use isolated Windows test machines and a disposable database. First inspect the read-only output from `scripts/inspect-manager-runtime-state.ps1`. Do not provision secrets, clean ACLs, rotate TLS, or otherwise modify the existing Manager PC before preflight review and explicit approval.

Record evidence for: installed Service identity; effective directory/file ACLs; DPAPI secret retrieval and TLS private-key access after service restart; enrollment replay/expiry/revocation; lease/heartbeat/fencing; and two-PC LAN reconnect. This gate cannot be marked passed from a ZIP artifact or CI result.

### Gate U — Interactive Desktop acceptance

On Windows, verify fa-IR RTL and en-US LTR, navigation, focus/keyboard behavior, PasswordBox clearing, loading/offline/error states and permission-denied behavior. Record the build SHA and observed outcomes. Static resource/binding tests are evidence for contracts, not visual interaction.

### Gate L — Installer and release

Installer/setup, PostgreSQL install/upgrade/repair/uninstall, staged update/rollback, signed release artifacts, SBOM, backup/recovery release checks and the staged 2–3-PC then ~15-station pilot remain release gates. None is required to keep Stage 1 closed; none can be claimed complete from the current portable runtime ZIP.

## 4. Open PR disposition register

| PR | Current purpose | Required disposition |
|---|---|---|
| [#15](https://github.com/alizebal73/5/pull/15) | ProgramData endpoints, trusted LAN TLS and overlapping workflow files | Keep Draft; separate runtime source from runner/workflow edits; reconcile source against current Integration before review. |
| [#17](https://github.com/alizebal73/5/pull/17) | Manual LAN smoke evidence from 2026-10-10 | Preserve the historical evidence; do not merge its stale-base planning edits wholesale. Promote/reconcile the evidence on the active branch as a focused docs change when checked. |
| [#18](https://github.com/alizebal73/5/pull/18) | Proposed secret lifecycle ADR | Keep as design review only until compared with the current secret-store implementation and ADR naming; do not treat a Proposed ADR as approved policy. |
| [#20](https://github.com/alizebal73/5/pull/20) | DPAPI Server secret-store implementation | Its `DpapiServerSecretStore.cs` blob is identical to current Integration. Audit every changed path against Integration; if the entire implementation is already present, close as superseded with a comment and retain the branch. Never merge duplicate code. |
| [#21](https://github.com/alizebal73/5/pull/21) | Initial Desktop operator shell | Keep Draft only if it has unique UI behavior absent from Integration; compare exact XAML/ViewModel/tests before any merge. Do not use an old base as the integration target. |
| [#23](https://github.com/alizebal73/5/pull/23) | Integration branch proposed back into old `feature/operator-identity-v1` base | Closed as obsolete without merging; source branch retained. Do not reopen or merge backwards without a new, explicit requirement. |
| [#24](https://github.com/alizebal73/5/pull/24) | StationBoard view extraction | Keep Draft as a later focused UI refactor; rebase/retarget to the current Integration tip only after the runtime acceptance priority is cleared and exact-head gates pass. |
| [#31](https://github.com/alizebal73/5/pull/31) | Integration runner/CI and readiness consolidation | Base is stale; #32 is merged. After Gate F, compare exact diff, port only unique needed Integration runner changes to a fresh current-base branch, or close as superseded with evidence and retain source branch. |
| [#32](https://github.com/alizebal73/5/pull/32) | Foundation hosted-Windows runner isolation | Merged at `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; PR candidate passed Full Foundation on exact head `4fb20dc...`. Post-merge Full Foundation [#38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) is running on the merged SHA; keep certification pending until it succeeds. |

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
- Runner migration has a specified order (#32 Foundation first, then #31 Integration reconciliation); runtime endpoint/TLS source work (#15) is separate.
- The nine open drafts have explicit dispositions instead of silently blocking stage progress.
- Future progress reports state only what changed, which exact SHA was tested, which gate is now closed, and the one next action.
