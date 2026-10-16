# Active Execution Control — GameNet 5

**Snapshot:** 2026-10-16. This is the single current execution queue, not an immutable branch pointer. Before any merge, read the live branch SHA, PR head/base and exact required checks from GitHub. Keep event-level details in the relevant Issue, PR and Actions run; do not copy them into additional status documents.

## Canonical refs

- main: b9288471f2047570eaf8d0d6552cf87bc0ddc214; protected and intentionally unchanged.
- foundation/runtime-final-v2: 71ef6534331e6ba5a8a460fd5b8846fe40e8567a; certified Foundation checkpoint. It is not a routine product-feature target.
- integration/runtime-operator-v1: active product-development branch. Query its live SHA; it changes with each merge.

Foundation certification proves only the exact SHA tested. It does not prove physical Windows Service, ACL/DPAPI/TLS, installed UI, two-PC LAN, Setup/update/rollback or pilot readiness.

## Current product objective — Issue #98

**Deliver and accept the first complete PC walk-in Session-to-cash journey. Status: PARTIAL / NOT ACCEPTED.** This milestone is tracked in [Issue #98](https://github.com/alizebal73/5/issues/98).

The Integration branch has a partial WPF/API/Server/PostgreSQL path for fixed-hourly PC Sessions, invoice creation and cash checkout, plus automated Server/DB/UI-contract coverage. A green automated run is not a substitute for the remaining risk fixes and manual acceptance.

### Required order

1. **Time contract and recovery — merged to Integration by PR #117; acceptance remains open.** The implementation persists a monotonic timestamp/frequency, independent system-uptime baseline and Windows boot identifier; billing uses persisted elapsed ticks, not UTC subtraction. UTC jumps and a Server process restart are supported only while same-boot continuity is verified. A Windows reboot or unprovable continuity fails closed without invoicing. The migration refuses to run while legacy active Sessions remain, so they must be ended/reconciled before upgrade. Real installed-Service/reboot behavior still needs operational acceptance.

2. **Safe cash entry — merged to Integration by PR #117; acceptance remains open.** The implementation leaves the received-cash amount blank, requires a valid whole-Toman amount at least equal to the invoice, and requires explicit confirmation; changing the amount invalidates that confirmation. Manual WPF acceptance is still required.

3. **Unknown checkout outcome — merged to Integration by PR #117; acceptance remains open.** The Server now looks up outcomes by invoice ID, validates persisted Invoice/Payment/Ledger consistency, and exposes recent completed checkouts across Desktop restart. Automated PostgreSQL and Desktop tests cover pending/paid lookup and simulated lost responses. Manual WPF lost-network/restart acceptance and independent database reconciliation remain required; a green CI run alone does not close this item.

4. **Agent online policy — merged to Integration by PR #118 at SHA b255b8be1cbd1a917f684975eb961e379ca1b2f8.** The policy fails closed when a PC Agent is unbound, lacks an active credential, has an expired lease or a stale/future-invalid heartbeat. Station listing and Session-start admission share the policy and WPF gives a localized actionable explanation. Post-merge Quick Validation run [38880919665](https://github.com/alizebal73/5/actions/runs/38880919665) and Full Foundation certification run [38880919577](https://github.com/alizebal73/5/actions/runs/38880919577) succeeded on the merge SHA. The post-merge Runtime Payload job was skipped; the pre-merge payload passed on PR head a47441a770fd05b666cf22985941875f56c660e5 in [run 38879582918](https://github.com/alizebal73/5/actions/runs/38879582918). A payload matching the final acceptance SHA is still required. Manual WPF acceptance and independent persisted-row reconciliation remain open.

5. **Full cash-sale reversal — merged by PR #120 at merge SHA `0660e632f0e38ec1654a2830f633e2f3cd60ae5f`.** The WPF action uses the dedicated `billing.cash-reverse` permission, exact invoice/payment amount confirmation, mandatory reason, shared idempotent mutation pipeline, append-only reversal evidence and a compensating ledger entry. It preserves original Invoice/Payment/sale-ledger facts and reconciles ambiguous responses against the durable Server outcome. Manual WPF acceptance and independent financial reconciliation remain open.

6. **Cash tender correction — merged by PR #123 at Integration merge SHA `f68eecbfbb75342e24d9c1475ecc491aa131cb4e`.** An operator can correct wrongly recorded received-cash/change facts using a dedicated permission, explicit confirmation, reason, expected correction version, idempotency and Audit. Original invoice amount, Payment identity, original received/change facts and original sale ledger remain unchanged; the Server outcome exposes the corrected effective facts. PR-head Quick Validation + Full Foundation/PostgreSQL/backup-restore/Desktop/Agent certification succeeded in [run 38922693279](https://github.com/alizebal73/5/actions/runs/38922693279), and the portable Runtime payload was built successfully in [run 38922692989](https://github.com/alizebal73/5/actions/runs/38922692989). Post-merge Quick Validation [run 38924114444](https://github.com/alizebal73/5/actions/runs/38924114444) and Full Foundation [run 38924114892](https://github.com/alizebal73/5/actions/runs/38924114892) succeeded. These results do not replace interactive acceptance or independent persisted-row reconciliation.

7. **Session timing recovery after Windows boot discontinuity — merged by PR #124 at merge SHA `588bd7ea29d48ebd07fee250b1d016b96280af80`.** Normal billing uses the persisted monotonic elapsed-time baseline and fails closed if same-boot continuity cannot be proven; it must not fall back to UTC subtraction or estimated duration. Explicit recovery requires both `sessions.operate` and `sessions.timing.recover`, a verified whole-minute duration, reason/evidence note, confirmation, expected Session version and idempotency. Recovery creates the pending invoice and Audit in the existing transaction. The same combined source tree was then included in PR #123's successful exact-head Foundation and Runtime validation. **Actual Windows reboot/recovery and interactive WPF use are still unverified.** Follow the [manual timing-recovery runbook](manual-session-timing-recovery-acceptance.md).

8. **Unified cash-sale HTTP certification — merged by PR #125 at Integration SHA `753106cae7dd62002faacf96f85da7cb18e4a694`.** Tested PR head `c5a4731617c1ad34006a2157fb02de9167b7b2f7` passed exact-head Quick Validation and full Foundation/PostgreSQL/backup-restore/Desktop/Agent certification in [run 38926793469](https://github.com/alizebal73/5/actions/runs/38926793469). The run's identity/runtime script emitted the expanded cash-checkout / tender-correction / full-reversal certification banner. Post-merge Quick Validation passed on the merge SHA in [run 38928259327](https://github.com/alizebal73/5/actions/runs/38928259327), and post-merge Full Foundation also completed successfully in [run 38928259123](https://github.com/alizebal73/5/actions/runs/38928259123) (both Foundation jobs succeeded). Runtime payload was skipped because PR #125 only changed the automated verification harness and acceptance-control documentation; manual Windows acceptance still requires a Runtime payload from a matching tested checkout SHA.

9. **Manual acceptance runbooks and evidence — remains outstanding.** The updated [cash-sale acceptance runbook](manual-cash-sale-reversal-acceptance.md) covers WPF Start → End → Invoice → Cash → Correct tender facts → Reverse sale, permission-denial checks, restart/reload and read-only SQL reconciliation. The separate [timing-recovery runbook](manual-session-timing-recovery-acceptance.md) covers the actual isolated Windows reboot and explicit time reconciliation. Capture exact tested source SHA/tree, matching payload SHA-256, screenshots, operator IDs/permissions, Session/Invoice/Payment/correction/reversal/Ledger/Audit IDs, read-only SQL outputs, and independent hand calculations. Mark any unexecuted case `NOT RUN`; CI must never be represented as a manual pass. Close #98 only after all acceptance evidence is attached and reviewed.





10. **Customer profile core — merged to Integration.** [PR #127](https://github.com/alizebal73/5/pull/127) was squash-merged into `integration/runtime-operator-v1` at merge SHA `973acc523f394b2171c02db2b3836fccc58c9d67`; its exact tested PR head was `c7ed41ab508823f2a90e3b3a7901447669e70b88`. Pre-merge exact-head gates passed: [Foundation run 38938514607](https://github.com/alizebal73/5/actions/runs/38938514607), [Quick Validation run 38938522585](https://github.com/alizebal73/5/actions/runs/38938522585), and [Runtime LAN Payload run 38938522496](https://github.com/alizebal73/5/actions/runs/38938522496). Post-merge gates also passed on merge SHA: [Foundation run 38939972345](https://github.com/alizebal73/5/actions/runs/38939972345) and [Quick Validation run 38939972405](https://github.com/alizebal73/5/actions/runs/38939972405).

    Implemented scope: registered-customer create/search/edit, optional nickname/phone, activate/deactivate/block with reason and expected-version checks, Server authorization, transactional idempotency/Audit, PostgreSQL migration, WPF profile management, independent search in the Session customer picker, and Server rejection of blocked-customer Session starts. Domain, Desktop binding/selection, PostgreSQL persistence/idempotency/Audit, blocked-customer admission, backup/restore, Desktop and Agent automated gates passed.

    **Runtime artifact provenance:** the portable payload was built from tested PR head `c7ed41ab508823f2a90e3b3a7901447669e70b88`; inner package SHA-256: `32bb09e60a7f7bad4e2b2aa94688eb3a3af491a0d829e71cce888102ec2aba3b`; [download artifact from run 38938522496](https://github.com/alizebal73/5/actions/runs/38938522496). GitHub's Git commit records confirm both that tested head and Integration merge SHA `973acc523f394b2171c02db2b3836fccc58c9d67` have the identical source tree `c06a22264da10340ce3b128bcaaa004156bb77f6`. The artifact manifest must still report its actual tested checkout SHA (`c7ed...`); acceptance evidence should record the tested SHA, merge SHA, common tree SHA and package checksum without claiming the artifact was built from the merge commit. No rebuild is required solely because the commit IDs differ while the source trees match. This remains a portable payload, not Setup/MSI or a production release. Manual acceptance is still NOT RUN.

    **Manual acceptance remains NOT RUN:** no interactive WPF acceptance, disposable-VM runtime/session test, or read-only database/financial reconciliation has been recorded. Never use live shop PCs/database or real money for this slice. Customer PIN/login, multi-device policy, Wallet/debt/gifts, VIP and statement/history projections remain future work; Slice 1 cash-sale acceptance under [Issue #98](https://github.com/alizebal73/5/issues/98) remains a separate open gate.

Keep this slice narrow. Wallet/debt, free credit/time, discounts, VIP, card payments, PS5 and foosball billing are not implied complete by Slice 1.

## Deferred gates — not blanket blockers for safe product coding

- **Issue #92 — Windows client access-control feasibility:** blocked until a supported, isolated Windows PC/VM with standard non-admin user, snapshot/revert and disposable database is available. Never use the live Manager/shop PC for lock/unlock experimentation.
- **Issue #96 — 2–3 station shadow trial:** only after #98 acceptance, with test data/database and no real money or replacement of the legacy source of truth.
- **Issue #74 — security lifecycle:** credential rotation/revocation/recovery and log redaction remain pre-pilot/release gates. ADR acceptance is not implementation proof.
- **Issue #16 — Setup/update/recovery/rollback:** required before release.
- **Issue #34 — installed Windows Service, TLS private key, DPAPI/ACL and physical LAN acceptance:** required before release; process-based smoke is insufficient.
- **Issue #67 — runner policy:** recent CI uses GitHub-hosted windows-2025, not the personal self-hosted runner. Never label hosted evidence as local-runner evidence or run untrusted PR code on a persistent personal runner.

## Execution rules

- Build a coherent vertical slice end-to-end; do not create a new branch/PR for every small edit.
- Add targeted tests with the behavior that changed. Run the required exact-head CI for the PR; use full PostgreSQL/Runtime gates when persistence, transactions, contracts, migrations, security or runtime behavior are affected.
- A passing check applies only to its exact SHA. Re-run relevant gates after code changes/rebases; verify post-merge source-code checks before claiming a change is integrated.
- One current queue lives here. Requirements live in [traceability](../planning/requirements-traceability.md), product order in [Master Build Plan](../planning/master-build-plan.md), action contracts in [Action Catalog](../development/action-catalog.md), and exact execution evidence in PRs/Issues/Actions.
