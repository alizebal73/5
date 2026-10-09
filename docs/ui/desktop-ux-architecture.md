# Desktop UX Architecture

## Primary shell

The native WPF Desktop is the only operator UI.

Shell responsibilities:

- authentication bootstrap;
- navigation;
- global connection/readiness state;
- global notifications;
- current operator context;
- content region;
- application shutdown/update prompts.

The shell does not own business rules.

## Navigation

The main navigation tree is feature-based:

- Overview
- Stations
- Customers
- Gaming Accounts
- Sessions
- Billing
- Wallet
- Inventory
- Buffet
- VIP
- Reports
- Agents
- Settings
- Approvals
- Backup & Recovery
- Audit
- Diagnostics

Permissions hide or disable unavailable features. Authorization is still enforced by Server.

## Station workspace

The station workspace is the operational center.

For each station show, as allowed:

- station name/code;
- type: PC / PS5 / Foosball;
- current state;
- customer;
- elapsed/remaining time;
- current charge/preview;
- Agent health for PC;
- actionable status badges.

The presentation scales beyond the initial 15 PCs by supporting both card and dense list modes.

Right-click/context actions are feature commands, not code-behind mutations:

- Open
- Start
- Pause/Resume
- Stop
- Transfer
- Release
- Change station settings (permission protected)
- Pair/recover Agent (PC only)
- Disconnect/logout where applicable

## Gaming Accounts workspace

The workspace manages only venue-controlled external gaming accounts that are permitted by the selected Steam PC Café/VR Arcade model. It is not a place to collect customer-owned Steam credentials.

Show, according to permissions:

- account label and Steam identity/reference;
- operating model and authorization state;
- station assignment and availability;
- connection/login readiness, last result and reauthentication requirement;
- credential state such as Not configured / Configured / Rotation required, never the stored password;
- audit history for assignment, enable/disable, rotation and session use.

Credential creation/replacement occurs through a protected write-only flow. Never offer a normal “reveal password” view, put passwords in logs, persist customer personal credentials, or bypass Steam Guard. Auto-login controls stay unavailable until the integration is explicitly supported/authorized for the configured model. The Server owns permission and station/session assignment; the Agent may execute only a scoped authorized command and reports the result.

## Customer workspace

List -> search -> profile -> ledger/session history.

Profile actions include:

- credit/top-up;
- free credit where permitted;
- non-cash debt;
- pay debt;
- deduct/adjust with permission;
- session history;
- VIP;
- audit/history.

All financial outcomes are server-confirmed.

## Localization

Primary culture:

- fa-IR
- RTL
- Persian labels
- Toman formatting
- Persian-friendly dates/numbers where configured

Secondary culture:

- en-US
- LTR
- English resources

No business screen may hard-code localized strings in code-behind.

## UX reliability

Every feature must define:

- loading state;
- empty state;
- error state;
- offline/reconnecting state;
- permission-denied state;
- confirmation for destructive actions;
- server-confirmed success.

Optimistic UI is allowed only when it cannot contradict an authoritative invariant.

## UI file rules

- No giant MainWindow.
- No DataTrigger-driven business engine.
- No direct DbContext usage.
- No direct financial calculations that become authoritative.
- Views remain presentation.
- ViewModels coordinate UI state, commands and API calls.
- Domain/application tests remain outside the WPF project.
