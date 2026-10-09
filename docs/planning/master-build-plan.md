# GameNet 5 Master Build Plan

## Purpose

This is the single delivery plan for building GameNet 5 from the clean foundation into a production-usable Windows GameNet/CyberCafe platform.

The plan is intentionally sequential. A later stage cannot silently compensate for an incomplete earlier stage.

## Product target

GameNet 5 is a Windows-first, single-site cybercafe management platform for:

- PC stations managed through the Server + Agent model.
- PS5 stations that need controlled timers, pricing and operator alerts without pretending they are PC Agents.
- Foosball/timed activities that need timers, pricing and alerts without direct workstation control.
- Customer identity, PIN/profile, wallet/debt/payment, discounts and VIP.
- Session start/stop/pause/transfer/release with concurrency protection.
- Station health, Agent pairing, reconnect/recovery and operator diagnostics.
- Optional Steam/Gaming Account management and session-scoped launch/login orchestration, strictly gated by the account model and authorization permitted by Valve's current PC Café terms.
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
18. External gaming accounts are not customer identities or Server deployment secrets; manage them through a separate, audited GamingAccounts boundary.
19. Never automate Steam sign-in through UI scripting, credential injection or Steam Guard bypass unless the exact mechanism is expressly supported or authorized by Valve; unresolved authorization keeps automation disabled.

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

## Stage 1 — Finish Foundation certification

Work only in platform infrastructure:

- harden configuration/secret handling;
- normalize health/readiness statuses through shared constants;
- enforce V1 API version/header behavior;
- implement real PostgreSQL design-time/runtime migration path;
- implement real audit storage boundary;
- implement idempotency storage boundary;
- implement outbox storage and dispatcher boundary;
- implement correlation/diagnostic context;
- add transaction/concurrency integration tests;
- add Agent device identity, authentication, lease/fencing and reconnect transport;
- add Desktop API client and typed error handling;
- add fa-IR/en-US localization resources;
- add native Desktop smoke harness;
- add backup/restore proof;
- add updater/rollback proof;
- record exact evidence.

Exit condition: Foundation Certification document can honestly be marked green.

## Stage 2 — Repository and module skeleton

Create the production directory skeleton without placing business logic in composition files.

Server module shape:

`Modules/<Module>/Domain`
`Modules/<Module>/Application`
`Modules/<Module>/Infrastructure`
`Modules/<Module>/Api`
`Modules/<Module>/Tests`

Initial modules:

- Identity
- Customers
- Stations
- Agents
- GamingAccounts
- Sessions
- Tariffs
- Billing
- Wallet
- Inventory
- Buffet
- VIP
- Games
- Reports
- Settings
- Approvals
- Backup

Cross-cutting platform remains outside modules:

- Persistence
- Transactions
- Authentication
- Authorization
- Audit
- Idempotency
- Outbox
- Observability
- Configuration

Exit condition: all module boundaries exist, dependency rules are guarded, and no module contains fake business behavior.

## Stage 3 — Identity and authorization

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

## Stage 4 — Stations and Agents

Separate these concepts:

- Station identity and type.
- Agent device identity and pairing.
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
- Agent reports do not overwrite authoritative Server decisions;
- account-related Agent commands, when introduced, must be Server-authorized and constrained by the current lease and station policy.

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

### Optional Steam account lifecycle

Steam account automation is a separately gated integration within the session workflow; it must not become a second authority for Station or Session state.

- For the standard Steam PC Café model, patrons use their own Steam accounts while the venue offers commercial licenses through the Steam PC Café license pool. GameNet must not collect or store patrons' personal Steam passwords.
- Dedicated accounts assigned to individual stations are allowed only when the venue's actual Steam model and applicable authorization permit them. Valve's current documentation describes the dedicated-account-per-station model for VR arcades; do not assume it is the ordinary PC Café model.
- Auto-login is disabled by default until the exact integration method is verified as supported/authorized under the current Steam agreements or written authorization is recorded. Do not use credential injection, GUI scripts/macros, process/UI tampering, Steam Guard bypass or account-creation automation.
- A future GamingAccounts module owns account metadata, allowed station assignment, enable/disable, credential rotation state and audit. Steam passwords are never returned in normal UI/API reads. If an authorized provider requires a credential at a station, release it only for that permitted account + station + active session over authenticated transport; do not persist it in Agent files, environment variables or logs.
- The external-account credential vault is separate from PostgreSQL/Steam metadata and from Server deployment secrets. Store only non-secret metadata plus, where needed, encrypted per-account credential material; keep its versioned vault key outside the database and rotate it independently. Do not include plaintext external credentials in backups, logs, diagnostics or JSON configuration.
- Account assignment and session transitions must be idempotent and concurrency-protected. One dedicated account cannot be assigned to two active stations unless the authorized Steam model expressly permits it. Steam login state never overwrites authoritative session state; failed login/authorization becomes a typed, audited station/session outcome.

Acceptance for any enabled Steam mode: documented licensing/account model; permission checks; no plaintext secrets in logs/storage; concurrent-assignment tests; single-use/short-lived credential handoff where supported; restart/reconnect reconciliation; login-failure and logout/cleanup behavior; and a policy gate that prevents unsupported automation from being enabled.

## Stage 7 — Tariffs, billing and wallet

Implement:

- PC tariffs;
- PS5 tariffs;
- foosball tariffs;
- time-based pricing;
- configurable promotions;
- rounding rules;
- billing preview;
- settlement;
- wallet top-up/debit/payment;
- debt;
- debt payment;
- non-cash debt where explicitly permitted;
- immutable correction/adjustment records.

Example promotion:

Pay 100,000 Toman and receive 10% promotional credit => usable balance 110,000 Toman.

Never calculate money in the UI as an authority.

Acceptance:

- every balance mutation has a ledger entry;
- duplicate payment request is idempotent;
- concurrent wallet updates preserve the ledger invariant;
- completed sessions cannot be silently repriced.

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

## Stage 11 — Reports

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

### Hardware pilot
- actual shop PCs
- real LAN
- 15-PC scale test
- real operator workflows
- real money/session/stock tests

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
