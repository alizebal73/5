# GameNet 5 — Branch governance

## Current GitHub read-back — 2026-10-10

This section supersedes the 2026-10-09 metadata snapshot below. Values were read back from GitHub; no branch or ruleset was edited during this audit.

| Ref | Observed SHA | Protected | Intended use |
|---|---|---:|---|
| main | b9288471f2047570eaf8d0d6552cf87bc0ddc214 | yes | Keep unchanged until release path is explicitly approved. |
| foundation/runtime-final-v2 | be29687e637b709158a30204bb9213bfa4813950 | yes | Protected candidate/reference only. Last recorded stable-checkpoint SHA differs; re-certify the exact current tip before treating it as currently certified. |
| integration/runtime-operator-v1 | 55a340305f9eba3bc8f9a1ce65fb37bedbae7580 | no | Active Runtime/operator integration branch; do not push unrelated work here. |
| refactor/desktop-view-boundaries-v1 | 9a058043ca8ba69f2b0bd23777263063ee681c92 | no | Isolated PR #24 for the first UI boundary extraction. |
| feature/operator-identity-v1 | d6a98ef1a7b87d9ced078b3b6f522406caa19b36 | no | PR #23 target; keep separate from Foundation candidate. |

Read-back of the active Rulesets confirmed:

- [protect-main](https://github.com/alizebal73/5/rules/24783754) is active and requires a PR, conversation resolution and status check `quick-validation`.
- [protect-foundation-runtime](https://github.com/alizebal73/5/rules/24784035) is active and requires a PR, conversation resolution and status check `foundation`.
- Both currently report `strict_required_status_checks_policy=false`; up-to-date-before-merge hardening remains to be considered. The current GitHub connection exposed read-only Ruleset access, so this audit did not change the rules.
- The active `integration/runtime-operator-v1` ref is not protected. First make sure PR-targeted Quick Validation is green, then add a PR/status-check Ruleset if available. Until then, keep changes isolated and do not treat this ref as protected.

Comparison snapshot (2026-10-10): `integration/runtime-operator-v1` is 122 commits ahead and 1 commit behind `foundation/runtime-final-v2`, with merge base `adb2159fb85564187718df8cbddcd3377e599c1c`. Its tree has product/runtime work and workflow changes; it is not a mirror of the Foundation ref. Do not rebase/merge the whole branch graph just to close this one-commit ancestry gap. Review the effective workflow delta explicitly before a merge. PR #23 itself is four commits ahead of its `feature/operator-identity-v1` base and currently has green exact-head Quick/Full Foundation runs.

### Open PR disposition policy

PR inventory read-back lists #15, #17, #18, #20, #21, #23 and #24 as open Drafts. #23 and #24 are the active implementation path; #15/#17/#18/#20/#21 use older bases or preserve historical evidence/design. No old PR should be merged wholesale. Review each unique diff/evidence, port useful work to the active path, then close with a reason. Do not delete branches until the PR disposition and all unique evidence are resolved.

The complete per-PR decision table and cleanup sequence are in [Engineering Readiness Register](../planning/engineering-readiness-register.md).

## Historical metadata snapshot (2026-10-09 — superseded by current read-back)

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
