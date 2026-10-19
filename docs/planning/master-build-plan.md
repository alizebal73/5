# GameNet 5 Master Build Plan

## Purpose

This is the single delivery plan for building GameNet 5 from the clean foundation into a production-usable Windows GameNet/CyberCafe platform.

The plan is intentionally sequential. A later stage cannot silently compensate for an incomplete earlier stage.

## Canonical delivery roadmap — agreed 2026-10-12

This numbered list is the owner-facing, high-level order of delivery. It groups the detailed Stage 1–19 instructions below; it does not replace their detailed acceptance criteria. A phase closes only with requirement-linked evidence on the exact candidate SHA. CI, security, architecture and regression checks continue throughout. Every usable vertical slice must also receive isolated manual acceptance as soon as its complete UI/runtime path is runnable, before dependent or adjacent product scope expands. Final release still has separate Setup, installed-Service, LAN, rollback and pilot gates.

### How the owner's 21-topic route maps to this canonical plan
The 21 topics discussed in planning are a coverage checklist, not a second competing roadmap. This document remains the canonical delivery order; `docs/operations/active-execution-control.md` is the single current queue; `docs/planning/requirements-traceability.md` is the requirement/status source; the capability matrix is only its summary. Map the topics into the existing plan rather than maintaining a duplicate numbered plan: current stabilization/Slice 1/PC feasibility/class refactor map to canonical steps 0–4 and Issues #98/#92/#138; session/customer/tariff/financial/operations capabilities map to steps 5–9 and detailed implementation Stages 5–14; release, migration/training, isolated certification, pilot, rollout and maintenance map to step 10 and detailed Stages 15–19 plus Issues #16/#34. A numbered stage or a green CI run is not itself acceptance.

### Mandatory dependency rules
- A station type's base tariff and billable-session contract must be delivered before or with its first usable Session flow; do not build PS5/foosball timers ahead of a Server-authoritative price path.
- Customer PIN/login and wallet display are not UI-only work: the owning Server contracts/data and permission rules must exist first. Free Money and Free Time are separate Wallet concepts/ledgers; Debt is distinct from a pending invoice/payment state.
- Basic structured redacted logging, correlation, health/readiness and status evidence are cross-cutting from each slice. Detailed operator diagnostics, backup verification, restore orchestration and crisis runbooks are expanded in detailed Stage 13.
- Settings and approval workflows must precede configuration-driven or separately approved operations; the exact permission/approval rules are implemented with the owning mutation.
- The first `main` promotion may be proposed only after the first core journey is accepted; it still requires explicit owner authorization, a separate reviewed PR and exact-SHA checks. It is not an automatic result of reaching a phase number.
- Record durable architecture/security decisions in ADRs, requirements in the existing traceability matrix, and only the current active queue in Active Execution Control. Do not create a parallel decision-log or duplicate progress roadmap.

### 0. Lock the version-one product scope
- Maintain one canonical requirements inventory with stable IDs, priorities, ownership, dependencies, user-visible outcomes, error/recovery behavior and measurable acceptance criteria.
- Confirm explicit non-goals for version one; do not enable mock or placeholder product paths.
- Record cross-cutting product constraints, including Persian RTL/Toman, native WPF, configurable installation location, no authoritative hourly price on station cards, and Server-owned business truth.
- Exit: every planned version-one capability maps to a requirement and owner; open decisions that affect money, security, ownership or release are assigned before dependent work starts.

### 1. Establish and protect the engineering foundation
- Preserve the closed Foundation certification as evidence for its exact certified SHAs; do not relabel older evidence as a pass for new code.
- Require the relevant Quick Validation and full PostgreSQL/runtime certification before source-sensitive Integration changes merge.
- Keep protected-branch policy and CI evidence accurate: require the observed exact checks, document the owner-authorized delegated technical-review process where used, and never describe that as independent human approval.
- Exit: required gates actually run on the intended candidate, failures block merge, and review/merge rules match the agreed no-unreviewed-merge process.

### 2. Close the canonical architecture and module inventory — COMPLETED (2026-10-12)
- PR #56 merged and [Issue #55](https://github.com/alizebal73/5/issues/55) closed after the canonical 19-module catalog, ownership boundaries, module-root READMEs, requirements alignment and architecture guards were integrated.
- The guard and inventory are ongoing regression requirements; any future change must keep them green.
- **Scope boundary:** Stage 2 completion certifies the module inventory/architecture controls only. It does not claim that Customers, Sessions, Tariffs, Billing/Wallet, Inventory/Buffet, VIP, Reports or other listed business capabilities are implemented.
- Exit: satisfied by merged PR #56 and the recorded Stage 2 closeout; continue following the product roadmap below.

### 3. Harden the existing platform; do not rebuild it (cross-cutting work; selected gates remain pre-pilot)
- Compare Identity/operator roles and permissions, Stations, Agent enrollment/credentials, heartbeat/lease/fencing/reconnect, ClientControl authorization/results, audit, idempotency and Outbox against their requirements.
- Fix demonstrated gaps and preserve regression coverage; do not rebuild working foundations or create generic frameworks without a proven second use case.
- ADR-0002 is the architecture contract, not implementation or release proof. Credential rotation/recovery, wider redaction and installed Windows Service ACL/DPAPI/TLS proof remain explicit pre-pilot/release gates. Resolve any issue that directly makes the current slice unsafe, but do not treat every deferred release gate as a blanket blocker to isolated product development.
- Exit: blocking gaps are closed or explicitly deferred with owner, reason and evidence; any slice depending on an unresolved security boundary stays disabled from operational use.

### 4. Deliver the first complete PC business journey (CURRENT PRODUCT DELIVERY — Slice 1 partial; Issue #98 open)
- Build one real end-to-end path across WPF, Server and PostgreSQL: operator authorization -> PC/station -> find/create customer -> resolve and snapshot tariff -> start/end Session -> bill preview/checkout -> payment -> durable ledger and audit.
- Deliver this in narrow sub-slices. The first accepted milestone is fixed-hourly PC walk-in Session -> invoice -> cash only, under [Issue #98](https://github.com/alizebal73/5/issues/98). The implementation is partial; timing/restart behavior, deliberate cash entry, lost-response recovery, correction semantics and manual database reconciliation remain acceptance work. Wallet/debt/free time/discounts/card/PS5/foosball are not implied complete by this first milestone.
- **Slice 1 money/time contract (owner decision, 2026-10-17):** for a normal PC Session with provable same-boot monotonic continuity, derive billable duration from Server-persisted monotonic ticks/frequency without rounding duration to whole minutes. Apply the immutable fixed hourly tariff pro rata at full precision, then round only the final fractional-Toman charge upward to the next whole Toman (ceiling); do not apply a separate minimum charge. UTC timestamps are event/audit facts, not duration inputs. If boot continuity cannot be proven, do not estimate or fall back to UTC subtraction: fail closed and use only the separately authorized/audited timing-recovery path; its verified whole-minute input is a recovery rule, not normal duration rounding. Any future minimum-charge, minute-rounding or alternative tariff rule requires an explicit owner decision before implementation.
- Keep domain ownership separate: Customers owns profile/PIN; Sessions owns lifecycle/time; Tariffs resolves price; Billing owns invoice/payment allocation; Wallet owns purchased value, free money, free time and debt ledgers.
- Add the actual API, migrations, validation, permission checks, failure handling, idempotency and relevant concurrency tests as part of the same slice. No UI success state may be fabricated before the Server confirms the result.
- Exit: the complete operator journey succeeds in an isolated Windows/WPF environment; an independent hand calculation reconciles persisted Session, Invoice, Payment, Ledger and Audit; relevant error/permission/retry paths are exercised; exact-head PostgreSQL/runtime gates pass; tested source SHA, matching payload checksum and manual evidence are attached to Issue #98. CI alone does not accept this slice.

### 5. Complete session rules and timed station types
- **Entry gate:** do not begin or merge this stage until Stage 4 / Issue #98 has passed its Slice 1 acceptance gate. While #98 is open, only fix demonstrated Slice 1 blockers and complete its acceptance evidence; do not start #92 or adjacent capability work. Preserve existing unaccepted work parked, not deleted. After #98 acceptance, run the separately scoped #92 disposable-VM feasibility gate, then proceed to the behavior-preserving #138 refactor before reassessing parked capabilities.
- Add pause/resume, extension/reduction, transfer/release/recovery, concurrent-session protection, customer multi-device limits and stale-command fencing.
- Implement PS5 and foosball as timed/non-Agent stations. Their first usable Session slice must include a Server-owned base tariff/pricing contract, operator visibility and timer rules; the detailed tariff/billing implementation below is a dependency, not a justification for a mock or UI-only rate.
- Add tariff variations/time bands/packages and discounts/promotions with Server-side permissions, caps, reasons and audit; then implement VIP plans/entitlements and daily-usage limits. These expanded rules remain outside Issue #98 Slice 1.
- Exit: concurrency/reconnect/recovery tests demonstrate no duplicated Session, double charge, wrong ownership or invalid tariff snapshot.

### 6. Add Games and, only after its security gate, GameAccounts
- Implement the game catalog and Server-authorized launch policy through ClientControl/Agent after Sessions are authoritative.
- Do not build or expose shared game-account secrets until ADR-0003 and the credential lifecycle/recovery design are accepted.
- Implement exclusive account lease, allocation, release, expiry/recovery and audit with concurrency/idempotency tests.
- Exit: a Session can launch/stop only an authorized game on its owning station; any shared account is never concurrently leased twice and is safely released/recovered.

### 7. Deliver inventory and buffet as one financially consistent flow
- Implement stock locations and immutable stock-movement ledger before connecting sale workflows.
- Implement warehouse-to-showcase transfer, purchase/receiving, sales, returns, waste, counts, low-stock thresholds and role/approval controls.
- Connect Buffet orders to Billing and stock movement atomically where required; distinguish a real payment from a wallet transfer or pending customer account.
- Exit: receive -> transfer -> sell -> refund/return -> reconcile tests preserve stock and money; duplicate requests cannot duplicate sales or movements.

### 8. Finish daily operations and control surfaces
- Implement Shifts/Cash as a write-owning module for opening, expected-vs-counted reconciliation, closing/handover and audited corrections.
- Implement Reports as read-only projections of source ledgers/state; complete Server-backed settings and approval workflows before dependent configuration/privileged operations, plus backup/restore orchestration and operator diagnostics.
- Basic structured redacted logs, correlation IDs, health/readiness, and Agent/station status are required in every slice; this step completes the richer operator-facing diagnostics and recovery controls rather than postponing baseline observability.
- Ensure backup restore is actually tested in an isolated database and that sensitive operations enforce authorization on Server, not only in WPF.
- Exit: shift totals reconcile with actual payments/refunds; reports do not mutate source state; privileged changes are audited/approved; recovery evidence is retained.

### 9. Complete and harden the WPF operator experience
- UI is implemented alongside every slice, not postponed until the end. This phase is for full navigation, consistency and acceptance across all implemented domains.
- Finish scalable station list/filter/grouping, customer/session/finance workflows, loading/empty/offline/error/permission-denied states, keyboard/focus/navigation, PasswordBox clearing, Persian RTL/Toman/Jalali presentation and en-US LTR resources.
- Exit: automated resource/binding/regression checks and the planned interactive WPF acceptance pass on the release candidate succeed; UI actions use real Server contracts.

### 10. Build, install, update, certify and pilot the release
- Produce a real Setup.exe/MSI and versioned manifest; declare PostgreSQL provisioning/schema rules, component versions, hashes/signatures, SBOM, protected secrets, service identities, ProgramData paths and install location.
- Build the separate updater with integrity verification, staged switch, compatibility checks, health confirmation and a documented rollback rule. Test clean install, repair, upgrade, uninstall and failure recovery on isolated machines.
- Rehearse any initial customer/configuration data import against a disposable copy, reconcile source/target row counts and financial totals, obtain owner sign-off, and document rollback before importing anything; never blindly copy legacy database files or assume schema compatibility.
- Deliver the one-page operator guide, owner troubleshooting/recovery guide, paper/offline fallback, and named support/escalation responsibilities before pilot.
- After product scope and Setup are complete, run the final Windows Service/TLS/DPAPI/ACL/restart and two-PC LAN acceptance with a disposable database; review the read-only manager-runtime preflight before any machine changes.
- Run the controlled 2–3-PC parallel pilot first, reconcile sessions/payments/voids each day and explain every difference; expand toward the planned ~15 stations only after pilot findings are resolved and recorded.
- Exit: all release-critical requirements are accepted on the exact candidate, install/update/restore evidence is complete, no unresolved critical defects remain, and pilot sign-off is recorded.

### When code first enters `main`, and how it stays current
- Keep `main` free of direct development commits and unreviewed changes. Continue day-to-day development on `integration/runtime-operator-v1` through focused PRs.
- Stage 2 is closed, but that alone does not trigger a promotion. Keep `main` at its intentionally empty starter commit until the owner explicitly authorizes a coherent, reviewed promotion milestone and its exact-SHA gates pass; then use a PR and verify the merge SHA.
- After that, promote stable, coherent milestones to `main` at defined delivery checkpoints (especially the first usable core journey and later release-ready increments), not after every commit and not only once at the very end.
- Every promotion is an explicit PR with the required independent review and exact-SHA checks. Never copy files manually, merge backwards from `main` to Integration as a substitute for a promotion, or use a green run on an older SHA as evidence for the new merge.
- A `main` merge means a reviewed stable development checkpoint; it does not mean the product is ready for the live shop. Production release still requires Phase 10 acceptance.

### Rules that apply to every phase
- One primary active phase at a time; parallel work is allowed only when dependencies and file ownership do not conflict.
- Each slice uses Definition of Ready, requirement IDs and Definition of Done. Keep a change/evidence ledger with PR, exact SHA, checks, conclusion and unresolved risks.
- Complete one coherent capability before opening the next. Add focused tests with changed behavior, then run the full relevant unit/integration/contract/security/regression and PostgreSQL/runtime set on the integrated exact candidate.
- Perform isolated manual acceptance as soon as each usable end-to-end slice can run; never defer all product feedback until final release. Do not start adjacent scope while the current slice's acceptance gate is incomplete.
- Run automated unit/integration/contract/security/regression checks throughout development; run PostgreSQL/runtime certification for source-sensitive changes as required by the gate.
- Do not reopen a closed phase without a specific defect or changed requirement. If later changes affect its invariant, reopen only the affected checks.
- Do not merge old Draft branches wholesale. Compare each needed change with live Integration and port only unique, reviewed behavior.

## Product requirements source of truth

- Detailed requirement IDs, acceptance criteria, ownership/dependencies, priority/risk and current implementation status are maintained in docs/planning/requirements-traceability.md.
- docs/planning/capability-matrix.md is the summarized domain/priority view; it does not claim feature completion.
- Before implementing a slice, complete its traceability row and Definition of Ready. Before marking it Done, attach tests and exact-SHA evidence according to the row and the current checkpoint.
- The engineering roadmap's governance stages (Foundation/CI/requirements consolidation) are separate from the implementation-stage numbers below; do not confuse the stage numbering.

## Current execution queue

Do not maintain live branch SHAs, PR queues or repeated Actions-run snapshots in this product roadmap. The sole current queue, blockers and deferred release gates are in [Active Execution Control](../operations/active-execution-control.md). Exact historical decisions and test evidence remain attached to their GitHub PRs, Issues and Actions runs. The Foundation certification record remains in [Stable Checkpoint](../operations/stable-checkpoint.md).

## Work item readiness and closure (mandatory)

- [Definition of Ready](definition-of-ready.md) is the entry gate for every feature/bug slice; [Definition of Done](definition-of-done.md) is the closure gate.
- Follow [bug-fix process](../development/bug-fix-process.md) and use [bug-fix template](../templates/bug-fix-template.md) for defects.
- Use [vertical-slice template](../templates/vertical-slice-template.md) for product slices and link every slice to IDs in docs/planning/requirements-traceability.md.
- scripts/check-work-item-docs.ps1 validates the required policies/templates and unique stable requirement IDs; scripts/verify.ps1 runs this gate during canonical verification.

## Product target

GameNet 5 is a Windows-first, single-site cybercafe management platform for:

- PC stations managed through the Server + Agent model.
- PS5 stations that need controlled timers, pricing and operator alerts without pretending they are PC Agents.
- Foosball/timed activities that need timers, pricing and alerts without direct workstation control.
- Customer identity, PIN/profile, wallet/debt/payment, discounts and VIP.
- Session start/stop/pause/transfer/release with concurrency protection.
- Station health, Agent pairing, reconnect/recovery and operator diagnostics.
- Inventory and buffet sales with ledger-based stock accounting.
- Reports, audit, approvals, backup/recovery and controlled updates.
- Persian RTL/Toman as the primary operating experience, with localization infrastructure for en-US.

## Non-negotiable architecture

1. Server is the only business authority.
2. PostgreSQL is the production source of truth.
3. Desktop never accesses PostgreSQL directly.
4. Agent never accesses PostgreSQL directly.
5. Realtime transport is not authoritative; reconnect reconciles with Server state.
6. Business rules live in Domain/Application use cases, not endpoint code, WPF code-behind or Agent transport.
7. Money uses integer minor units/value objects; no floating-point financial calculations.
8. Wallet and inventory are ledger-first.
9. Session, Station and Agent state are separate models.
10. Financial, inventory, session and privileged mutations are transactional, audited and idempotent.
11. Every race-sensitive invariant has a database/concurrency test.
12. No browser Dashboard runtime.
13. No persistent production mocks/fakes.
14. No giant composition files.
15. Every production defect becomes regression evidence.
16. Main only receives reviewed, verified work.
17. Installer/update logic is a first-class product boundary, not a post-build script pile.

## Delivery gates

### Gate A — Foundation certification

Before the first business vertical slice is merged:

- clean Windows restore/build/test;
- architecture, source-size, placeholder and business gates;
- durable Audit, Idempotency and Outbox persistence;
- PostgreSQL connectivity, migration and concurrency evidence;
- native Desktop runtime smoke in fa-IR and en-US;
- authenticated Agent identity, heartbeat, reconnect and lease/fencing evidence;
- Desktop -> Server smoke;
- backup + isolated restore evidence;
- exact evidence recorded in the stable checkpoint.

Installer, updater and rollback are intentionally not part of Gate A. They are release-boundary work and must be complete before production release, not before business development.

### Gate B — First usable core

Gate B is a later, broader operational milestone, not permission to enlarge Slice 1. First complete and accept the bounded Issue #98 PC walk-in-to-cash journey before adding its adjacent capabilities.

The first real operational milestone must support:

- operator login and permissions;
- station inventory/list;
- Agent pairing and station health;
- customer create/search/profile;
- start/stop a PC session;
- timed PS5/foosball activity;
- tariff resolution;
- bill calculation;
- wallet top-up/debit/payment;
- debt recording/payment;
- operator audit.

### Gate C — Commercial operations

Add:

- discounts/promotions;
- VIP subscriptions and entitlements;
- transfer/release;
- multi-device customer session rules;
- inventory/buffet sales;
- stock adjustments with approval;
- cash/settlement reports;
- operator shift controls.

### Gate D — Administration/recovery

Add:

- settings and permission administration;
- approval workflows for privileged actions;
- backup/restore UI;
- diagnostics;
- recovery/reconciliation;
- update/repair/rollback UI;
- audit/report exports.

### Gate E — Release certification

Required on real Windows machines:

- clean install;
- upgrade from previous version;
- repair;
- uninstall;
- rollback after failed update;
- server restart;
- desktop restart;
- agent restart;
- network interruption/reconnect;
- PostgreSQL restart/recovery;
- 15-station pilot behavior;
- money/session/inventory concurrency;
- no data-loss evidence;
- signed release artifacts and release manifest.

## Stage 1 — Foundation certification — CLOSED (2026-10-12)

**Status: CLOSED.** The Master Build Plan Stage 1 exit condition is satisfied on exact tested SHAs:

- Foundation checkpoint `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`: [Full Foundation #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) passed, including canonical verification, disposable PostgreSQL, migration/concurrency checks, backup/isolated restore, Desktop runtime, Agent runtime and evidence upload.
- Integration merge `161b4cbbbe16cd3fd4046f174a1c09380139c67b`: post-merge [Quick Validation #38183458461](https://github.com/alizebal73/5/actions/runs/38183458461) and [Full Foundation #38183458486](https://github.com/alizebal73/5/actions/runs/38183458486) passed on the exact merge SHA. Runner migration candidate also passed Quick PR/push and Runtime payload checks; see [Active Execution Control](../operations/active-execution-control.md).
- Documentation-only follow-up PR #45 passed [Quick Validation #38183922997](https://github.com/alizebal73/5/actions/runs/38183922997) on candidate `d7b25f88c932b09c3cbdebb82c21100b48a53916` and merged as `05560d1db09855ddf07ae4cad35d6d5e3ee075b3`; it did not change application code.

The catalogue below records the original Foundation scope, not a live implementation to-do queue:

- configuration and secret-handling hardening;
- shared health/readiness status normalization;
- V1 API version/header enforcement;
- PostgreSQL design-time/runtime migration path;
- audit storage and idempotency storage boundaries;
- durable Outbox append/leased-claim/published-state path, hosted delivery worker, versioned routing, capped retry/backoff, dead-letter and delivery observability, PostgreSQL concurrency/stale-lease/retry/recovery checks;
- correlation/diagnostic context and transaction/concurrency integration tests;
- Agent identity/authentication/lease/fencing/reconnect transport;
- Desktop API client, typed errors, fa-IR/en-US resources and native smoke harness;
- backup/restore proof and exact-SHA evidence.

**Scope boundary:** this closes hosted Foundation certification and automated code verification only. Installed Windows Service identity/ACLs, DPAPI/TLS access after restart, physical two-PC LAN/reconnect, production Setup/updater/repair/uninstall/rollback and commercial pilot remain separate final gates, normally deferred until the product scope and a real Setup.exe/MSI candidate are ready. **Do not defer slice-level WPF/runtime acceptance**: Issue #98 and every later usable vertical slice must receive isolated functional acceptance before adjacent scope expands. For #98 only, a local process-based/loopback test may use the Manager PC after a reviewed read-only preflight and with a dedicated disposable database/role and separate user-local paths; it must not alter legacy GameNet services/data, register Windows services, change firewall/certificates/registry/system time, reboot Windows or disrupt the LAN. This narrow exception does not satisfy Issue #92's disposable-VM OS lock/unlock test or Issue #34's separate-machine installed-Service/TLS/DPAPI/two-PC LAN gate. Never use real customer/financial data. Automated tests still run throughout development. These final gates do not reopen Stage 1 unless a later change invalidates its tested code.

## Stage 2 — Canonical module inventory and skeleton

**Status: COMPLETE (2026-10-12; PR #56 merged; Issue #55 closed).** Foundation Stage 1 is a separate closed gate. Stage 2 completion covers only the module inventory, ownership documentation and architecture guards; product capability work remains in the later roadmap steps.

The single canonical list is `src/Server/Modules/README.md`. It must remain aligned with `docs/architecture/repository-tree.md`, this plan, the capability matrix and the requirement owners.

### Canonical modules and ownership

- **Existing:** Identity, Stations.
- **Core runtime/control:** Agents, ClientControl.
- **Core business:** Customers, Sessions, Tariffs, Billing, Wallet.
- **Commercial operations:** Inventory, Buffet, VIP, Games, GameAccounts, Shifts, Reports.
- **Administration/operations:** Settings, Approvals, Backup.

Ownership decisions:
- Operator accounts, roles and permissions remain in **Identity**; there is no separate Users module.
- **Agents** owns device identity/enrollment/heartbeat/lease/fencing; **ClientControl** owns permissioned commands and their outcomes.
- **Customers** owns profile/customer PIN only, not wallet, debt, station or Session ownership.
- **Sessions** owns lifecycle and timing state; **Tariffs** resolves pricing; **Billing** owns invoices/payment allocation/pending settlement; **Wallet** owns stored-value/free-money/free-time/debt ledgers.
- **Discounts/promotions** start as capabilities within Billing/Tariffs, not a separate module.
- **Games** owns catalog/launch policy; **GameAccounts** owns shared-account leases and is blocked until ADR-0003 and secret/recovery requirements are accepted.
- **Shifts** owns cash opening/reconciliation/close; **Reports** is read-only.
- Network policy is configured/validated under Settings and executed only via ClientControl/Agent when genuinely supported.
- Recovery/reconciliation stays with the module that owns the state; Backup owns backup/restore orchestration.

### Module/layer shape

`src/Server/Modules/<Module>/Domain`  
`src/Server/Modules/<Module>/Application`  
`src/Server/Modules/<Module>/Infrastructure`  
`src/Server/Modules/<Module>/Api`

These layer folders are created as the first real slice needs them. Each module root has ownership documentation now; no empty C# interfaces, dummy projects, fake endpoints or placeholder-success code is introduced to simulate implementation.

Tests belong in root `tests/` projects under `tests/Server.UnitTests/Modules/<Module>/` and the appropriate contract/PostgreSQL suites. A separate `Server.IntegrationTests` project is added only when a real suite needs it; don't create a dummy project solely to mirror a target diagram.

### Cross-cutting services (not duplicate modules)

Persistence/migrations, transactions/concurrency, authentication/authorization primitives, audit, idempotency, Outbox/Inbox, observability/health, versioned Shared contracts, money/time primitives and release/secret/TLS integration remain platform infrastructure.

### Stage 2 exit conditions

1. All 19 module roots and their ownership READMEs exist and match the canonical inventory.
2. `scripts/check-architecture.ps1` rejects a missing module root/catalog/README and continues to enforce cross-module implementation isolation, layer and persistence rules.
3. Requirements remain traceable through `docs/planning/requirements-traceability.md`; each real slice must pass Definition of Ready before coding.
4. Quick Validation passes after this guard/catalog change. For business-source PRs, full pre-merge PostgreSQL/runtime certification must also pass.
5. Stage 2 is closed after PR #56 merged and Issue #55 closed. Preserve exact-SHA review/CI evidence for future changes; do not merge an old feature branch wholesale.

## Stage 3 — Identity and authorization

**Status: ACTIVE (2026-10-13).** The privileged-reason contract and direct Audit test gap are covered by PRs #76/#81/#82; exact post-merge Quick Validation and Full Foundation passed for PR #82. Secret rotation/recovery/redaction remains open under [Issue #74](https://github.com/alizebal73/5/issues/74). ADR-0002 acceptance is an architecture decision, not proof that those lifecycle guarantees are implemented.

Implement:

- operator users;
- roles;
- permission assignments;
- login/session lifecycle;
- password/credential policy;
- station/customer/business permissions;
- sensitive-action authorization;
- audit for permission changes;
- approval requirement evaluation.

Permissions must be granular enough to prevent an operator who can run sessions from silently editing money, discounts, logs or configuration.

Acceptance:

- unauthorized mutation returns a stable authorization failure;
- privileged operations are audited;
- permission changes have actor + time + reason.

## Stage 4 — Stations, Agents and ClientControl

Separate these concepts:

- Station identity and type.
- Agent device identity and pairing.
- ClientControl command authorization/result lifecycle; device connectivity does not imply permission to issue a command.
- Agent connection/heartbeat/lease state.
- Session occupancy state.
- Operator availability/health state.

Station types:

- PC
- PS5
- Foosball
- Future timed/non-PC station types

PC flow:

Server decides permission/state -> Agent executes -> Agent reports -> Server reconciles.

PS5/Foosball flow:

Server manages timer/pricing/operator state; no fake Agent ownership is created.

Acceptance:

- duplicate station code rejected safely;
- one active Agent lease owns a PC station at a time;
- reconnect cannot resurrect stale control;
- Agent reports do not overwrite authoritative Server decisions.

## Stage 5 — Customers

Implement:

- customer identifier;
- PIN/credential policy where applicable;
- profile fields;
- search/list;
- balance;
- debt;
- session history;
- VIP state;
- operator notes with audit policy.

Identity rule:

Customer identity is a stable business identity. Login/session tokens are transport/security artifacts, not customer identity.

## Stage 6 — Sessions

Implement a formal session state machine.

Core states:

- Planned
- Active
- Paused
- Ending
- Completed
- Cancelled
- RecoveryRequired

Operations:

- start;
- pause/resume where allowed;
- stop;
- transfer;
- release;
- recover after disconnect;
- reconcile from Agent reports.

Every transition validates ownership and current version/state.

Acceptance:

- two operators cannot start two authoritative sessions for one PC;
- stale transfers fail safely;
- releasing a PC cannot leave an orphan session;
- reconnect/restart does not double-charge.

## Stage 7 — Tariffs, billing and wallet

**Current bounded candidate (2026-10-19):** Issue #137 / PR #145 adds operator-only purchased-value cash top-up through WPF → Server → PostgreSQL with an actual cash receipt, immutable Wallet ledger, balance projection, idempotency, Audit and dedicated tests. It remains a candidate until the latest exact-SHA CI and review/acceptance checks are green; do not describe the whole Wallet module as complete or promote it before that evidence. Customer PIN/self-login, Wallet spending against Sessions, countdown/PC control, debt/free credit/time, discounts/VIP, card/terminal and refunds remain separate later slices.

Implement:

- a Server-owned base tariff for every supported station type before the first usable Session flow for that type;
- PC, PS5 and foosball tariffs;
- time-band/day/group/package pricing where approved;
- configurable promotions/discounts with role caps, reason and Audit;
- an explicit whole-Toman rounding/minimum-charge/time-precision policy;
- bill preview and settlement;
- approved payment methods (cash, manually recorded card terminal, Wallet, and combined allocation only after the allocation rules are accepted);
- wallet top-up/debit/payment and debt payment;
- separate Free Money and Free Time grants/consumption with independent units, expiry/restrictions where approved, actor/reason/reference and ledger evidence;
- Debt separate from PendingPayment and active Session; debt limits and credit policy only after owner decision;
- immutable correction/adjustment/refund/reversal records linked to their original operation; never delete the original financial facts.

Example promotion:

Pay 100,000 Toman and receive 10% promotional credit => usable balance 110,000 Toman.

Never calculate money in the UI as an authority.

Acceptance:

- every balance/value mutation has a ledger entry and balances reconcile to immutable entries;
- Free Money, Free Time, purchased Wallet balance, Debt and PendingPayment remain separate concepts;
- duplicate/retried payment and refund/reversal requests are idempotent;
- concurrent Wallet updates preserve the ledger invariant and enforce the approved no-negative-balance/debt-limit policy;
- a reversal creates compensating facts without deleting the original Payment/ledger records; inventory-linked refunds also restore the correct stock source;
- completed Sessions cannot be silently repriced; independent integer-Toman reconciliation matches the Server outcome.

## Stage 8 — Multi-device and transfer rules

Implement:

- customer active-device limits;
- allowed concurrent sessions;
- time splitting/entitlement rules;
- station transfer;
- operator release;
- forced termination with permission/approval;
- stale-command fencing.

Acceptance includes concurrent operator simulation and reconnect scenarios.

## Stage 9 — Inventory and Buffet

Implement ledger-first stock:

- products;
- categories;
- purchase/receiving;
- sale;
- return;
- waste;
- adjustment;
- stock count;
- reversal/correction;
- approval for protected movements.

Buffet sale can be attached to a customer/session or closed-sale transaction.

Critical invariant:

Physical stock is derived from immutable movements/controlled adjustments, not from arbitrary direct quantity edits.

## Stage 10 — VIP

Implement:

- VIP plans;
- duration;
- activation;
- expiration;
- entitlements;
- eligibility;
- upgrades/downgrades;
- audit.

VIP must integrate with tariff resolution and customer/session rules without duplicating pricing logic.

## Stage 10A — Games and shared game accounts (after core billing; shared accounts are separately gated)

Implement the Games catalog and Server-side launch policy after Sessions, ClientControl and the core tariff/billing path exist. The Agent executes only a Server-authorized, current-session command; it does not own launch policy or credentials.

Implement the GameAccounts/shared-account pool only after ADR-0003 is accepted and the secret lifecycle, account lease, isolation, audit, expiry, release and recovery requirements are READY. Never expose shared account passwords in Desktop/Agent/API logs.

## Stage 11 — Reports and Shifts/Cash

Implement Shifts as a separate write-owning module for opening cash, cash/card reconciliation, expected-vs-counted difference, close/handover and audited correction. Reports remain read-only projections of Billing, Wallet, Inventory, VIP, Sessions, Shifts and Audit; they never change the authoritative ledger.

### Reports

Implement read-only/reporting views:

- active stations;
- customer balance/debt;
- session history;
- revenue;
- wallet ledger;
- inventory movement;
- buffet sales;
- VIP;
- operator activity;
- audit;
- shift/cash settlement.

Reports never become a second source of truth.

## Stage 12 — Settings and approvals

Implement settings by ownership:

- station defaults;
- tariff configuration;
- discount/promotion rules;
- session policies;
- VIP plans;
- inventory policy;
- audit retention policy;
- backup schedule/configuration;
- update channel.

Protected changes use approval where configured.

## Stage 13 — Backup, recovery and diagnostics

Basic health/readiness distinction, structured logs without secrets, correlation, and Agent/station status are required by the cross-cutting rules on every earlier slice. This stage adds the richer operator-visible diagnostic, backup/recovery and crisis-response tooling; it does not postpone basic observability until late in development.

Implement operator-visible tooling for:

- backup status;
- backup creation;
- backup verification;
- isolated restore test;
- database/repository health;
- Agent connectivity;
- queue/outbox status;
- recovery-required items;
- reconciliation exceptions.

The UI may request recovery; the Server decides whether recovery is safe.

## Stage 14 — Desktop UI system

The Desktop is the single operator UI.

UI principles:

- Persian RTL first;
- consistent Toman formatting;
- keyboard-friendly operator workflows;
- list/grid views for scale beyond 15 PCs;
- no giant MainWindow triggers or business code-behind;
- feature-based navigation;
- view model/state ownership per feature;
- typed API client;
- centralized notifications/errors/loading;
- reusable dialog, confirmation, validation and table components.

Primary shell:

- Login
- Top toolbar
- Main navigation
- Workspace/content region
- Status/connection area
- Global notifications

Feature navigation:

1. Overview
2. Stations
3. Customers
4. Sessions
5. Billing
6. Wallet
7. Inventory
8. Buffet
9. VIP
10. Reports
11. Agents
12. Settings
13. Approvals
14. Backup & Recovery
15. Audit
16. Diagnostics

Station board requirements:

- scalable card/list representation;
- PC/PS5/Foosball differentiated visually by type;
- current state;
- customer;
- elapsed/remaining time;
- price/balance information where authorized;
- Agent health for PC;
- context menu for allowed operations;
- no direct data mutation from view code.

## Stage 15 — Setup / Installer

The installer is a product, not a one-off script.

Install modes:

- Server machine
- Operator Desktop machine
- Client Agent machine
- Combined Server + Desktop for the initial single-site setup

Installer responsibilities:

- prerequisite check;
- .NET/runtime prerequisite handling;
- PostgreSQL connection/setup guidance;
- Windows Service registration;
- firewall/network guidance;
- Program Files deployment;
- ProgramData directory creation;
- secure configuration initialization;
- desktop shortcuts;
- start/stop/status actions;
- health check;
- uninstall cleanup policy.

Target machine layout:

- `C:\Program Files\GameNet Manager\Server`
- `C:\Program Files\GameNet Manager\Desktop`
- `C:\Program Files\GameNet Manager\Agent`
- `C:\Program Files\GameNet Manager\Updater`
- `C:\ProgramData\GameNet Manager\Config`
- `C:\ProgramData\GameNet Manager\Logs`
- `C:\ProgramData\GameNet Manager\State`
- `C:\ProgramData\GameNet Manager\Backups`
- `C:\ProgramData\GameNet Manager\Updates`

Rules:

- Program Files is immutable application payload.
- Runtime state/config/logs/backups never live beside binaries.
- Secrets are never committed into the repository or embedded in installer source.
- Install and uninstall are idempotent.
- Service creation/start is verified, not assumed.

## Stage 16 — Update engine

Updates use a separate updater process so locked executables are never replaced in-place by themselves.

Update flow:

1. Read signed release manifest.
2. Compare installed version and compatibility.
3. Download package to staging.
4. Verify package hash/signature.
5. Stop services/components in dependency order.
6. Snapshot/backup update-sensitive state.
7. Apply staged files.
8. Run database migration only through the explicit deployment step.
9. Start services.
10. Run health checks and version checks.
11. Mark update successful.
12. On failure, reverse to previous application payload and restore only the state that is explicitly safe to restore.

Required update states:

- Available
- Downloading
- Verified
- Staged
- Applying
- Validating
- Succeeded
- Failed
- RolledBack

Acceptance:

- interrupted download does not alter the installed version;
- checksum/signature mismatch does not install;
- failed startup automatically enters rollback path;
- rollback leaves the previous version runnable;
- database compatibility is checked before application switch.

## Stage 17 — Release artifact and file layout

Every release produces:

- Server package;
- Desktop package;
- Agent package;
- Updater package;
- Installer/bootstrapper;
- release manifest;
- hashes;
- compatibility metadata;
- migration manifest;
- SBOM;
- release notes;
- verification evidence.

Versioning:

- single product version;
- component versions are traceable to the same release;
- database schema version is separate and explicit;
- rollback compatibility is declared, not guessed.

## Stage 18 — Certification matrix

Before calling a version production-ready:

### Build
- restore
- Release build
- Release tests
- architecture guards
- source-size guard
- placeholder guard

### Database
- clean migration
- upgrade migration
- rollback-compatible path
- concurrency
- unique constraints
- transaction retry
- idempotency

### Security
- auth enabled in production
- permission checks
- audit
- secret/config handling
- no direct client DB access

### Agent
- install
- start
- pair
- authenticate
- heartbeat
- reconnect
- fencing
- stale command rejection

### Desktop
- install
- launch
- fa-IR
- en-US
- login
- server discovery/config
- network loss/reconnect
- feature navigation

### Business
- station
- customer
- session
- billing
- wallet
- inventory
- VIP
- reports
- approvals

### Recovery
- backup
- restore
- service restart
- DB restart
- failed update
- rollback

### Data migration, training and operational readiness
- rehearsed dry-run import of approved customers/opening balances/settings only; source/target row counts, balance totals and exceptions reconciled;
- explicit owner sign-off and a tested rollback/backout plan before any import;
- one-page operator guide, owner troubleshooting/recovery notes, paper/offline fallback and named support/escalation ownership;
- one full-shift simulation with planned and injected errors before live pilot.

### Hardware pilot
- actual shop PCs;
- real LAN;
- 2–3 PC / one-operator parallel pilot, daily financial reconciliation and no unexplained differences before expansion;
- controlled progression toward 15-PC scale test;
- real operator workflows;
- no real customer/financial data until pilot authorization and accounting controls are verified.

## Stage 19 — Defect discipline

For every discovered defect:

- reproduce;
- assign an ID;
- record root cause;
- identify violated invariant;
- fix the owning layer;
- add a regression test;
- re-run the smallest relevant verification first;
- re-run canonical verification;
- record exact commit and evidence.

No “fixed in UI” workaround is accepted when the bug belongs to Server/domain/data authority.

## Definition of Done

A feature is done only when all are true:

- requirement is traceable;
- owner and invariant are explicit;
- contract exists;
- authorization is enforced;
- transaction/idempotency behavior is explicit;
- audit behavior is explicit;
- concurrency behavior is tested when relevant;
- UI is connected to real Server behavior;
- no fake source is involved;
- automated verification is green;
- runtime evidence exists;
- rollback impact is known;
- documentation is updated.
