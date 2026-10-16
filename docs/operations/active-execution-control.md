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

5. **Cash sale reversal — candidate on `work/billing-cash-sale-reversal-v1`.** The candidate now connects a WPF operator action to the dedicated `billing.cash-reverse` API permission, requires an explanatory reason and explicit confirmation of the exact payment/invoice amount, uses the shared idempotent mutation pipeline, preserves original Invoice/Payment/`CashSale` facts, writes an append-only reversal record plus typed positive-magnitude compensating ledger entry, and reconciles an ambiguous response against the durable Server outcome. Unit tests cover domain invariants and signed ledger effect; Desktop tests cover a reversal response lost after commit; PostgreSQL tests cover concurrent duplicate reversals, replay, stored rows/Audit and read access with the reversal permission alone. This remains a candidate: exact-SHA Quick/Foundation/Runtime checks and manual WPF plus independent financial reconciliation acceptance are required before operational use. Cash tender correction remains a separate unimplemented follow-on.

6. **Acceptance evidence.** Run WPF Start → End → Invoice → Cash on an isolated test environment. Independently reconcile Session, Invoice, Payment, tender-correction/reversal records (once implemented), Ledger and Audit against a hand calculation. Capture screenshots, the exact tested SHA and a matching runtime payload hash. Close #98 only when its complete acceptance criteria are evidenced.

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
