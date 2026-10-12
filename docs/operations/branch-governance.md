# GameNet 5 — Branch governance

> **Current execution note — 2026-10-13:** Stage 1 Foundation automation remains certified on its recorded SHAs. Stage 2’s 19-module inventory, ownership boundaries and architecture guards have now been merged to Integration in PR #56 at `16e9abe3ac68885af7f9282c3872e47c13ff19aa`; post-merge Quick Validation [#38315716272](https://github.com/alizebal73/5/actions/runs/38315716272) and Full Foundation [#38315716279](https://github.com/alizebal73/5/actions/runs/38315716279) both passed on that exact SHA. The owner authorized that specific merge; this is not being described as an independent review or blanket approval for other PRs. The current implementation focus is Stage 3 platform/security hardening. Refreshed security PRs #59 and #60 have both passed their exact-SHA Quick Validation, Full Foundation/PostgreSQL, and Runtime LAN Smoke Payload checks; both are Ready for Review but remain unmerged pending separate owner decisions/review. Physical Windows Service/LAN, interactive WPF and Setup/update/rollback/pilot acceptance remain final-release gates; CI and regression tests continue throughout.

## Current ref and post-merge certification — 2026-10-13

This timestamped read-back is the current checkpoint at the time this document was prepared. Always re-read GitHub refs before subsequent actions.

| Ref / evidence role | SHA | Meaning |
|---|---|---|
| `main` | `b9288471f2047570eaf8d0d6552cf87bc0ddc214` | Remains unchanged; not a target of PR #56. |
| `integration/runtime-operator-v1` | `16e9abe3ac68885af7f9282c3872e47c13ff19aa` | Live Integration head at this read-back. |
| PR #56 post-merge Quick Validation | [#38315716272](https://github.com/alizebal73/5/actions/runs/38315716272) | Success on the exact Integration merge SHA. |
| PR #56 post-merge Full Foundation | [#38315716279](https://github.com/alizebal73/5/actions/runs/38315716279) | Success on the exact Integration merge SHA. |

PR #53 and PR #56 are merged. On 2026-10-13, refreshed PR #59 at `f8c329fc7e6dbd441a814762ddad9a5e7e4ddf52` and PR #60 at `cf4778b43ed5d367f700d04694a439475f5d8577` both passed their exact-head PR Quick Validation, Full Foundation/PostgreSQL and Runtime LAN Smoke Payload gates. The original PRs #57/#58 are closed as superseded with branches retained. PRs #59/#60 await their own owner merge decisions and remain unmerged. Issue #54 remains open for a durable approval policy.

> **Historical ref snapshot recorded at PR #47 merge (not the latest live read-back):** Integration was advanced by documentation/validator PR #47, squash merge SHA `a2b986865a19b2e2c48e1e414311afda19cdc0b6`. This is an immutable event SHA, not a permanent live pointer; query GitHub for live branch heads before any operation. The post-merge Quick Validation was still running at the snapshot and must be judged only from its exact-run result.

## Last GitHub ref read-back — immediately after PR #42 (2026-10-12; historical snapshot)

This table records a read-back event, not a live branch pointer. Subsequent docs PRs advance Integration; get the live head from GitHub before any operation. Older tables below are historical as well. The [Active Execution Control](active-execution-control.md) is the single PR/gate disposition register. The recorded Integration SHA is a timestamped observation, never the live pointer after subsequent docs merges.

| Ref / role | SHA observed | Protected | Meaning |
|---|---|---:|---|
| `main` | `b9288471f2047570eaf8d0d6552cf87bc0ddc214` | yes | Protected; deliberately unchanged at the read-back. |
| `foundation/runtime-final-v2` | `be29687e637b709158a30204bb9213bfa4813950` | yes | Historical tip before runner-policy PR #32; #32 has since moved Foundation to `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`. Post-merge Full Foundation [#38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) passed on that exact SHA. |
| `integration/runtime-operator-v1` | `d2d8c626c678078c52b840fd0c9c751e7583fa91` | yes | Integration head observed after docs-control PR #42. It is a docs-control commit, not a new runtime-code certification. |
| Runtime source merge | `58717adc6c1553ef21221c3d484f093494ba1ae7` | n/a | PR #40 code merge; exact SHA passed Quick and Full Foundation. |
| PR #40 candidate | `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd` | n/a | Exact candidate passed Quick, Full Foundation and Runtime payload. |

### Active Ruleset read-back

- [protect-main](https://github.com/alizebal73/5/rules/24783754) is active for `refs/heads/main`; requires PR, conversation resolution and `quick-validation`.
- [protect-foundation-runtime](https://github.com/alizebal73/5/rules/24784035) is active for `refs/heads/foundation/runtime-*`; requires PR, conversation resolution and `foundation`.
- [protect-integration-runtime](https://github.com/alizebal73/5/rules/24879997) is active for `refs/heads/integration/runtime-*`; requires PR, conversation resolution and `quick-validation`.
- Live GitHub Ruleset read-back on 2026-10-13 confirms `strict_required_status_checks_policy=true` on all three active Rulesets. `protect-integration-runtime` targets `refs/heads/integration/runtime-*`. All three still require zero approving reviews; that separate human-approval policy remains tracked in Issue #54. No Ruleset settings were changed by this documentation update.
- Post-merge source-code gates on `58717adc...`: [Quick #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) and [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446), both successful.
- PR #41 docs candidate passed [Quick #38181520088](https://github.com/alizebal73/5/actions/runs/38181520088); SHA `362513ad...` passed [Quick #38181599202](https://github.com/alizebal73/5/actions/runs/38181599202). PR #42's exact head `623bb678...` passed [Quick #38182154397](https://github.com/alizebal73/5/actions/runs/38182154397) and merged as `d2d8c626...`. Each run proves only its recorded SHA.
- PR #32 moved Foundation automation to hosted `windows-2025`; Full Foundation #38182502053 passed on exact SHA `71ef653...`. PR #44 moved Integration CI to hosted `windows-2025`; candidate Quick push #38183164928, Quick PR #38183167294, Full Foundation #38183164941 and Runtime payload #38183167300 passed, and merge SHA `161b4c...` passed post-merge Quick #38183458461 and Full Foundation #38183458486. PR #31 is closed as superseded for runner work; source branch retained for separate test-only service helper. #15's endpoint/TLS source remains a separate review item.
- PRs #35–#39 are superseded by #40 and closed; source branches were retained. PR #23 was closed as an obsolete backward PR, and PR #31 was closed as superseded for overlapping runner work; neither source branch was deleted. PRs #32 and #44 are merged.
- CI does not certify installed Windows Service identity/effective ACLs, DPAPI/private-key access after restart, physical two-PC LAN/reconnect or interactive WPF behavior. Before modifying the existing Manager PC, inspect `scripts/inspect-manager-runtime-state.ps1` read-only output and use isolated test machines plus a disposable DB.

## Subsequent verified events — 2026-10-12 (immutable SHA evidence)

- Foundation runner PR #32: merge SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`, [Full Foundation #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) succeeded.
- Integration runner PR #44: candidate SHA `92de1b1fb6002165fc91c7f66b4ecec1c969c0ff` passed [Quick push #38183164928](https://github.com/alizebal73/5/actions/runs/38183164928), [Quick PR #38183167294](https://github.com/alizebal73/5/actions/runs/38183167294), [Full Foundation #38183164941](https://github.com/alizebal73/5/actions/runs/38183164941) and [Runtime Payload #38183167300](https://github.com/alizebal73/5/actions/runs/38183167300). Merge SHA `161b4cbbbe16cd3fd4046f174a1c09380139c67b` passed [Quick #38183458461](https://github.com/alizebal73/5/actions/runs/38183458461) and [Full Foundation #38183458486](https://github.com/alizebal73/5/actions/runs/38183458486). Both workflows used `windows-2025`.
- PR #31 closed without merge after its workflow changes were superseded by #44. Its branch remains available for the test-only service registration helper/runbook, which must be re-cut as a later focused change.

## Historical metadata snapshot (2026-10-09 — superseded by the 2026-10-12 read-back above)

At that earlier read, these refs were reported `protected=false`. Do not treat the table below as the current state:

| Ref | SHA at audit | Reported protected |
|---|---|---|
| `main` | `b9288471f2047570eaf8d0d6552cf87bc0ddc214` | false |
| `foundation/runtime-final-v2` | `adb2159fb85564187718df8cbddcd3377e599c1c` | false |
| `feature/operator-identity-v1` | `853f23427783a93638a0212cb1dc5e97386e0e8f` | false |

The Repository Rulesets endpoint returned an empty list at audit time. The administrative branch-protection endpoint was not writable through the connected GitHub integration, so this document records the observed state and policy; it does **not** claim to have enabled any settings.

## Required policy

### `main`

- Keep the branch at its current empty starter commit until the product's release path is approved; do not populate it merely to make it non-empty.
- Disallow direct pushes for normal work. Merge reviewed pull requests only.
- Disallow force pushes and deletion.
- After stage 3 creates the fast pull-request validation workflow, require that exact workflow status check before merge. Do not configure a required check name before the workflow exists and has succeeded on a pull request.
- Require resolution of review conversations where available. Require an independent approval only once a second trusted reviewer is available, to avoid creating an unmergeable single-owner policy.

### `foundation/runtime-*`

- Treat certified Foundation checkpoints as protected reference bases, not feature branches.
- Disallow force pushes and deletion.
- Updates should be limited to a reviewed maintenance PR that runs the full Foundation certification. Record the certified SHA and evidence; a later documentation commit is not itself a new certification.

### Feature branches

- Develop one focused slice/fix at a time.
- Do not merge legacy branches merely because their names look relevant; compare ancestry and inspect the diff first.
- Do not bypass CI controls to get a green check. A marker is valid for gating only when its SHA is a real ancestor of current HEAD, proven by `scripts/check-business-gate.ps1`.

## Verification result (2026-10-09)

GitHub read-back confirmed both repository Rulesets are active:

- [`protect-main`](https://github.com/alizebal73/5/rules/24783754): targets `refs/heads/main`, active, pull request required, deletion and force push blocked, conversation resolution required, Squash only, no bypass actors.
- [`protect-foundation-runtime`](https://github.com/alizebal73/5/rules/24784035): targets `refs/heads/foundation/runtime-*`, active, pull request required, deletion and force push blocked, conversation resolution required, Squash only, no bypass actors.

GitHub branch metadata reports `protected=true` for `main` and `foundation/runtime-final-v2`. The Stage 2 branch-protection blocker is resolved. Stage 3 is now active: design a low-noise CI split between quick pull-request validation and full Foundation certification, keeping all code gates intact.


## Tracking and proof

- Closed: [Issue #9 — enable actual GitHub branch protection](https://github.com/alizebal73/5/issues/9). Both branch Rulesets are now active and verified below.
- Current branch comparison at audit time showed `feature/operator-identity-v1` at `935ede43843cc51491c8a87def55418f4bcb75bb` is 26 commits ahead and 0 behind `foundation/runtime-final-v2` at `adb2159fb85564187718df8cbddcd3377e599c1c`. This confirms the working branch contains the current Foundation tip but does not remove the separate branch-protection requirement.




## CI status-check follow-up (Stage 3)

Historical note before final Stage 3 verification: both branch rulesets required pull requests but did not yet require named CI status checks. Workflow validation evidence: [quick #73](https://github.com/alizebal73/5/actions/runs/37921715915) and [full #163](https://github.com/alizebal73/5/actions/runs/37921715979) both passed on SHA `aaea71212111aec24f688ae791aabb2543b5633b`. After a real pull request targets `foundation/runtime-*`, read the check contexts from GitHub and require the exact full Foundation certification context in `protect-foundation-runtime`; do not guess or select a check that has not appeared on a real PR. For future PRs to `main`, require the observed `quick-validation` context when it is available.




## Foundation required-check verification (2026-10-09)

GitHub Ruleset read-back confirms `protect-foundation-runtime` now requires the exact status check `foundation` with the GitHub Actions integration ID, based on real PR validation [#10](https://github.com/alizebal73/5/pull/10) and successful run [#164](https://github.com/alizebal73/5/actions/runs/37922148983). The validation PR was closed without merging.

## Remaining CI protection item

Historical blocker before the final read-back: `protect-main` lacked a `required_status_checks` rule. This blocker is resolved by the final verification section below.



## Final status-check read-back — Stage 3 complete (2026-10-09)

- [`protect-main`](https://github.com/alizebal73/5/rules/24783754), ID `24783754`, was re-read from GitHub after saving. It is active, targets `refs/heads/main`, and its required status-check rule contains the exact context `quick-validation`.
- [`protect-foundation-runtime`](https://github.com/alizebal73/5/rules/24784035), ID `24784035`, was re-read and is active for `refs/heads/foundation/runtime-*`; it requires `foundation` from GitHub Actions.
- Evidence for the main PR lane: [Draft PR #11](https://github.com/alizebal73/5/pull/11), head `685301a6973666b0dca0aee27739f5afccdc9124`, had a successful `quick-validation` check in [run #74](https://github.com/alizebal73/5/actions/runs/37923320241). Evidence for Foundation: [run #164](https://github.com/alizebal73/5/actions/runs/37922148983) passed on the temporary Foundation PR's head SHA.
- PRs #10 and #11 were closed without merging after verification. No changes were merged into `main` or Foundation. `main` remains on its starter commit.
- Stage 3 is complete. Stage 4 is next: consolidate product requirements, dependencies, module/data ownership, risks, and acceptance criteria based on repos 2 and 3 before expanding product implementation.


## Security branch validation lane — 2026-10-10

The PR-ready change in `.github/workflows/gamenet.yml` adds `security/**` to the Quick Validation push lane. Once PR #25 is merged, new commits on isolated security branches will receive the fast build/test gate, and pull requests targeting `integration/runtime-operator-v1` receive Quick Validation. Until then, do not interpret the absence of a run on PR #27 as a passing result.


## Security runtime/enrollment integration audit — 2026-10-11

Read-back on the active branches and security PRs confirms the protected references are unchanged: `main` remains `b9288471f2047570eaf8d0d6552cf87bc0ddc214` (protected), and `foundation/runtime-final-v2` remains `be29687e637b709158a30204bb9213bfa4813950` (protected). `integration/runtime-operator-v1` remains `55a340305f9eba3bc8f9a1ce65fb37bedbae7580` and is not protected.

The ancestry/diff audit against that integration ref found:
- PR #29 validation candidate: 168 commits ahead, 0 behind, 67 changed files.
- Source PR #27: 50 commits ahead, 0 behind, 34 changed files.
- Source PR #28: 82 commits ahead, 0 behind, 40 changed files.
- PR #27's 34 changed paths are all present in #29; 39 of #28's 40 paths are present in #29. The source PRs overlap on six paths: workflow files, runtime endpoint docs, the identity runtime verifier, Server service registration and Server Program. #28's one non-carried file is `src/Server/InternalTestAccess.cs`; the combined tree uses `src/Server/Properties/AssemblyInfo.cs` for test visibility and the combined Foundation run passed on its previous exact head.
- Do not merge #27, #28 or #29 wholesale. Preserve all three Drafts for source and validation traceability.

The bounded delivery candidate [PR #30](https://github.com/alizebal73/5/pull/30) was merged by squash to `integration/runtime-operator-v1` after all four checks succeeded on exact head `f4387584d6c14fb55cbfca20521e221ea2462a4f`. Merge commit is `e1626d7d1f2e62601329604b8823787f2c9597b6`. The integration preserves a one-commit/67-file functional diff and does not import the 168-commit history of validation PR #29. Exact-head evidence: [Full Foundation #38096870883](https://github.com/alizebal73/5/actions/runs/38096870883), [Quick Validation push #38096870892](https://github.com/alizebal73/5/actions/runs/38096870892), [Quick Validation PR #38096874251](https://github.com/alizebal73/5/actions/runs/38096874251) and [Runtime payload #38096874276](https://github.com/alizebal73/5/actions/runs/38096874276) all succeeded; package SHA-256 is `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`, outer artifact digest is `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. The source candidate #29 at `f5dd302...` also has all four CI runs green. Post-merge branch checks for `e1626d7...` are now green: [Full Foundation #38097484132](https://github.com/alizebal73/5/actions/runs/38097484132), [Quick push #38097484122](https://github.com/alizebal73/5/actions/runs/38097484122) and [Quick PR #38097487320](https://github.com/alizebal73/5/actions/runs/38097487320). At the time of this 2026-10-11 snapshot, #27/#28/#29 were being retained as Draft traceability records while PR #25 was still pending. Current disposition was re-read on 2026-10-12: #27/#28/#29 are closed without merge; PR #30 delivered the bounded implementation, and PR #25 is merged. Protected `main` and `foundation/runtime-final-v2` remain unchanged.

No merge or deletion was performed. Installed Windows Service/DPAPI identity and physical LAN evidence remain explicitly unverified in this pass.


## Live ruleset read-back — 2026-10-12

This is a fresh read-back from GitHub's live Ruleset endpoints, not a branch-SHA snapshot:

| Ruleset | Target | Required check | Enforcement |
|---|---|---|---|
| [protect-main](https://github.com/alizebal73/5/rules/24783754) | `refs/heads/main` | `quick-validation` | Active |
| [protect-foundation-runtime](https://github.com/alizebal73/5/rules/24784035) | `refs/heads/foundation/runtime-*` | `foundation` | Active |
| [protect-integration-runtime](https://github.com/alizebal73/5/rules/24879997) | `refs/heads/integration/runtime-*` | `quick-validation` | Active |

All three block deletion and non-fast-forward updates, require a pull request and review-thread resolution, permit Squash only, and have no bypass actors. The latest live read-back confirms `strict_required_status_checks_policy=true` on all three Rulesets, so required checks must be based on an up-to-date branch. However, each still has `required_approving_review_count=0`, `require_code_owner_review=false`, `required_reviewers=[]`, and `require_last_push_approval=false`: GitHub does not enforce an approving human review. Treat human review as a mandatory operational gate unless and until an appropriate reviewer requirement is configured.

No Ruleset settings were changed as part of this documentation update. The current integration code branch remains the development target; `main` and the protected Foundation branch were not modified.
