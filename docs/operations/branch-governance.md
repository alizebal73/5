# GameNet 5 — Branch Governance

This file defines branch rules only. The live task queue belongs in [Active Execution Control](active-execution-control.md); individual change evidence belongs in the PR and its Actions runs.

## Protected branches and their roles

- `main`: protected release destination; intentionally unchanged until the product/release merge decision.
- `foundation/runtime-*`: protected technical baseline/certification lane. Do not use it as the normal destination for product features.
- `integration/runtime-operator-v1`: protected integration branch and current destination for reviewed product work.

Read the live refs/rulesets in GitHub before merging. A historical SHA or ruleset snapshot in an older report is not a live pointer.

## Change lifecycle

1. Start one focused branch from the latest appropriate base: usually Integration for product work, Foundation only for Foundation maintenance.
2. Put a coherent fix or vertical slice, its relevant tests and any directly related documentation into one PR. Avoid stacks of stale branches and separate PRs for tiny edits that belong to the same feature.
3. Require the configured PR checks on the exact head. Rebase or change the head only when necessary, then wait for the new exact-head checks. For changes affecting persistence, contracts, transactions, security or runtime, run the applicable broader certification gate.
4. Merge only into the intended destination. Read the live post-merge head/checks before declaring source code integrated.
5. Delete a merged feature branch once its PR and checks are settled. Git commits, PR discussion/reviews and Actions runs preserve the engineering evidence; a merged branch is not an archive.
6. For an unmerged stale PR, first identify unique code/evidence. Port it to a current-base PR or record its disposition in the owning Issue, then close the stale PR. Keep a stale branch only while its unique contents are actively needed; never merge a stale stacked base wholesale.

Never force-push/delete protected refs. Never merge a feature directly to `main` without the explicit release decision and release gates.

## CI and trust boundary

The latest verified workflow lane uses GitHub-hosted `windows-2025`; it is not evidence from the personal self-hosted runner. Issue [#67](https://github.com/alizebal73/5/issues/67) tracks the runner-policy decision. Do not run untrusted PR code on a persistent personal runner or describe hosted results as local-runner results.

## Current cleanup rule

Avoid keeping duplicate status reports, obsolete execution snapshots or branches merely because they were once useful. Keep one source of truth per concern: Master Build Plan (delivery stages), Requirements Traceability (capabilities/acceptance), Active Execution Control (current queue), Action Catalog (implemented actions), and domain/ADR files (durable contracts).
