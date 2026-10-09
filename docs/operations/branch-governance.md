# GameNet 5 — Branch governance

## Audited remote state (2026-10-09)

The GitHub branch metadata endpoint reported `protected=false` for these refs:

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

- Open action item: [Issue #9 — enable actual GitHub branch protection](https://github.com/alizebal73/5/issues/9).
- Current branch comparison at audit time showed `feature/operator-identity-v1` at `935ede43843cc51491c8a87def55418f4bcb75bb` is 26 commits ahead and 0 behind `foundation/runtime-final-v2` at `adb2159fb85564187718df8cbddcd3377e599c1c`. This confirms the working branch contains the current Foundation tip but does not remove the separate branch-protection requirement.




## CI status-check follow-up (Stage 3)

Both branch rulesets currently require pull requests but do not yet require a named CI status check. Keep that distinction explicit. After a real pull request targets `foundation/runtime-*`, read the check contexts from GitHub and require the exact full Foundation certification context in `protect-foundation-runtime`; do not guess or select a check that has not appeared on a real PR. For future PRs to `main`, require the observed `quick-validation` context when it is available.

