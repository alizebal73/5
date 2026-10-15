# Active Execution Control — GameNet 5

**Snapshot:** 2026-10-15. This is the single current execution queue, not an immutable branch pointer. Before any merge, read the live branch SHA, PR head/base and exact required checks from GitHub. Keep event-level details in the relevant Issue, PR and Actions run; do not copy them into additional status documents.

## Canonical refs

- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`; protected and intentionally unchanged.
- `foundation/runtime-final-v2`: `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; certified Foundation checkpoint. It is not a routine product-feature target.
- `integration/runtime-operator-v1`: active product-development branch. Query its live SHA; it changes with each merge.

Foundation certification proves only the exact SHA tested. It does not prove physical Windows Service, ACL/DPAPI/TLS, installed UI, two-PC LAN, Setup/update/rollback or pilot readiness.

## Current product objective — Issue #98

**Deliver and accept the first complete PC walk-in Session-to-cash journey. Status: PARTIAL / NOT ACCEPTED.** This milestone is tracked in [Issue #98](https://github.com/alizebal73/5/issues/98).

The Integration branch now has a partial WPF/API/Server/PostgreSQL path for fixed-hourly PC Sessions, invoice creation and cash checkout, plus automated Server/DB/UI-contract coverage. A green automated run is not a substitute for the remaining risk fixes and manual acceptance.

### Required order

1. **Time contract and recovery — implementation in PR #117; acceptance remains open.** The candidate persists a monotonic timestamp/frequency, independent system-uptime baseline and Windows boot identifier; billing uses persisted elapsed ticks, not UTC subtraction. UTC jumps and a Server process restart are supported only while same-boot continuity is verified. A Windows reboot or unprovable continuity fails closed without invoicing. The migration refuses to run while legacy active Sessions remain, so they must be ended/reconciled before upgrade. Re-run exact-SHA PostgreSQL/runtime gates after the final PR change; real installed-Service/reboot behavior still needs operational acceptance.
2. **Safe cash entry — implementation in PR #117; acceptance remains open.** The candidate leaves the received-cash amount blank, requires a valid whole-Toman amount at least equal to the invoice, and requires explicit confirmation; changing the amount invalidates that confirmation. Exact-head Desktop tests must remain green, followed by manual WPF acceptance.
3. **Unknown checkout outcome — implementation in PR #117; acceptance remains open.** The candidate adds Server-side lookup by invoice ID, validates the persisted invoice/payment/ledger outcome, and shows recent completed checkouts so the outcome survives Desktop restart. Automated PostgreSQL and Desktop tests cover pending/paid lookup and simulated lost responses. Manual WPF lost-network/restart acceptance and independent database reconciliation remain required; a green CI run alone does not close this item.
4. **Cash correction and Station online policy.** Define audited, permission-checked reversal/correction semantics without editing historical ledger entries. Decide whether starting a Session on an administratively available but Agent-offline PC is blocked or requires an explicit warned/audited override.
5. **Acceptance evidence.** Run WPF Start → End → Invoice → Cash on an isolated test environment. Independently reconcile Session, Invoice, Payment, Ledger and Audit against a hand calculation. Capture screenshots, the exact tested SHA and a fresh matching runtime payload. Close #98 only when its acceptance criteria are evidenced.

Keep this slice narrow. Wallet/debt, free credit/time, discounts, VIP, card payments, PS5 and foosball billing are not implied complete by Slice 1.

## Deferred gates — not blanket blockers for safe product coding

- **Issue #92 — Windows client access-control feasibility:** blocked until a supported, isolated Windows PC/VM with standard non-admin user, snapshot/revert and disposable database is available. Never use the live Manager/shop PC for lock/unlock experimentation.
- **Issue #96 — 2–3 station shadow trial:** only after #98 acceptance, with test data/database and no real money or replacement of the legacy source of truth.
- **Issue #74 — security lifecycle:** credential rotation/revocation/recovery and log redaction remain pre-pilot/release gates. ADR acceptance is not implementation proof.
- **Issue #16 — Setup/update/recovery/rollback:** required before release.
- **Issue #34 — installed Windows Service, TLS private key, DPAPI/ACL and physical LAN acceptance:** required before release; process-based smoke is insufficient.
- **Issue #67 — runner policy:** recent CI uses GitHub-hosted `windows-2025`, not the personal self-hosted runner. Never label hosted evidence as local-runner evidence or run untrusted PR code on a persistent personal runner.

## Execution rules

- Build a coherent vertical slice end-to-end; do not create a new branch/PR for every small edit.
- Add targeted tests with the behavior that changed. Run the required exact-head CI for the PR; use full PostgreSQL/Runtime gates when persistence, transactions, contracts, migrations, security or runtime behavior are affected.
- A passing check applies only to its exact SHA. Re-run relevant gates after code changes/rebases; verify post-merge source-code checks before claiming a change is integrated.
- One current queue lives here. Requirements live in [traceability](../planning/requirements-traceability.md), product order in [Master Build Plan](../planning/master-build-plan.md), action contracts in [Action Catalog](../development/action-catalog.md), and exact execution evidence in PRs/Issues/Actions.
