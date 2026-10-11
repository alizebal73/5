# GameNet 5 — Branch governance

## Current GitHub read-back — 2026-10-11 (latest authoritative snapshot)

Ruleset read-back confirms Integration is protected. Runner-group restrictions are not confirmed; the personal Runner is retained until GameNet completion by owner decision.

| Ref | Observed SHA | Protected | Intended use |
|---|---|---:|---|
| main | b9288471f2047570eaf8d0d6552cf87bc0ddc214 | yes | Keep unchanged. |
| foundation/runtime-final-v2 | be29687e637b709158a30204bb9213bfa4813950 | yes | Protected Foundation reference; Stable Checkpoint remains at historical SHA c3a8ba482548e963479fbdf8538be62c4d15acfe. |
| integration/runtime-operator-v1 | 82d90178c0011c65eef76bd32cc33318d5bbd91c | yes | Protected by active Ruleset protect-integration-runtime. |
| security/runner-and-stage1-stabilization-v1 | e5caceb11968512fb91b5f808cbd4afd24be22ad (last code-bearing baseline; see PR #31 for the latest docs-only branch HEAD) | no | Draft review candidate only. |
| security/foundation-hosted-runner-isolation-v1 | 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9 | no | PR #32 Draft Foundation maintenance candidate only. |
| fix/programdata-endpoints-v1 | 15402b2758808327874c18e21753120ef9cfccce | no | Older TLS/ProgramData PR #15; not selected as runtime acceptance baseline. |

Active Rulesets read-back:
- protect-main ID 24783754 targets refs/heads/main; requires PR, conversation resolution and quick-validation.
- protect-foundation-runtime ID 24784035 targets refs/heads/foundation/runtime-*; requires PR, conversation resolution and foundation.
- protect-integration-runtime ID 24879997 targets exactly refs/heads/integration/runtime-operator-v1; requires PR, conversation resolution and quick-validation, blocks deletion/force-push, permits Squash only and has no bypass actor.

## CI Runner policy — 2026-10-11

The last code-bearing PR #31 baseline e5caceb11968512fb91b5f808cbd4afd24be22ad passed

The personal Runner remains registered by owner decision; remove it after GameNet completion and verify disappearance from repository/organization Runner settings. Runner-group restrictions are not certified and older branches may still contain self-hosted definitions. Eventual cleanup is tracked in [Issue #33](https://github.com/alizebal73/5/issues/33).

The hosted payload includes a test-only Server/Agent service-registration helper. It verifies manifest hashes, refuses existing GameNet services/state, checks role-specific service identity/SID and registers only Manual/Demand Start while leaving it stopped. CI invokes it only with -ValidateOnly; it does not touch services on the hosted Runner. PR #31 stays Draft/unmerged until the complete diff and cross-PR order are reviewed.

Older records below remain a historical audit trail and do not override the current snapshot above.

## Historical metadata snapshot (2026-10-09 — superseded by 2026-10-11 read-back)

At that earlier read, these refs were reported `protected=false`. Do not treat the table below as the current state:

| Ref | SHA at audit | Reported protected |
|---|---|---|
| `main` | `b9288471f2047570eaf8d0d6552cf87bc0ddc214` | false |
| `foundation/runtime-final-v2` | `adb2159fb85564187718df8cbddcd3377e599c1c` | false |
| `feature/operator-identity-v1` | `853f23427783a93638a0212cb1dc5e97386e0e8f` | false |

The Repository Rulesets endpoint returned an empty list at audit time. The administrative branch-protection endpoint was not writable through the connected GitHub integration, so this document records the observed state and policy; it does **not** claim to have enabled any settings.

## Required policy

The exact current hosted-CI candidate and blockers before physical acceptance are tracked in [Pre-Windows and Pre-Commercial Readiness Gate](../planning/pre-commercial-readiness-gate.md). Integration Ruleset administration is complete and read back. Runner-group access is still unverified and intentionally deferred until GameNet completion by owner decision; no source-level guard is a substitute.

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


## Historical Security runtime/enrollment integration audit — 2026-10-11 (captured at Integration SHA 55a3403)

**Historical snapshot only — superseded by the authoritative current GitHub read-back above.** At the time of this audit, `main` was `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, `foundation/runtime-final-v2` was `be29687e637b709158a30204bb9213bfa4813950`, and Integration was at `55a340305f9eba3bc8f9a1ce65fb37bedbae7580`. Do not use this older state as current branch-protection evidence.

The ancestry/diff audit against that integration ref found:
- PR #29 validation candidate: 168 commits ahead, 0 behind, 67 changed files.
- Source PR #27: 50 commits ahead, 0 behind, 34 changed files.
- Source PR #28: 82 commits ahead, 0 behind, 40 changed files.
- PR #27's 34 changed paths are all present in #29; 39 of #28's 40 paths are present in #29. The source PRs overlap on six paths: workflow files, runtime endpoint docs, the identity runtime verifier, Server service registration and Server Program. #28's one non-carried file is `src/Server/InternalTestAccess.cs`; the combined tree uses `src/Server/Properties/AssemblyInfo.cs` for test visibility and the combined Foundation run passed on its previous exact head.
- Do not merge #27, #28 or #29 wholesale. Preserve all three Drafts for source and validation traceability.

The bounded delivery candidate [PR #30](https://github.com/alizebal73/5/pull/30) was merged by squash to `integration/runtime-operator-v1` after all four checks succeeded on exact head `f4387584d6c14fb55cbfca20521e221ea2462a4f`. Merge commit is `e1626d7d1f2e62601329604b8823787f2c9597b6`. The integration preserves a one-commit/67-file functional diff and does not import the 168-commit history of validation PR #29. Exact-head evidence: [Full Foundation #38096870883](https://github.com/alizebal73/5/actions/runs/38096870883), [Quick Validation push #38096870892](https://github.com/alizebal73/5/actions/runs/38096870892), [Quick Validation PR #38096874251](https://github.com/alizebal73/5/actions/runs/38096874251) and [Runtime payload #38096874276](https://github.com/alizebal73/5/actions/runs/38096874276) all succeeded; package SHA-256 is `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`, outer artifact digest is `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. The source candidate #29 at `f5dd302...` also has all four CI runs green. Post-merge branch checks for `e1626d7...` are now green: [Full Foundation #38097484132](https://github.com/alizebal73/5/actions/runs/38097484132), [Quick push #38097484122](https://github.com/alizebal73/5/actions/runs/38097484122) and [Quick PR #38097487320](https://github.com/alizebal73/5/actions/runs/38097487320). Keep #27/#28/#29 as Draft traceability records until the final docs PR #25 validates and merges. Protected `main` and `foundation/runtime-final-v2` remain unchanged.

No merge or deletion was performed. Installed Windows Service/DPAPI identity and physical LAN evidence remain explicitly unverified in this pass.
