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


## Permission-aware station workspace

The Desktop consumes only the effective permission snapshot returned by the Server for the authenticated operator. `stations.read` is required to display or refresh the station list and request a read-only Agent health probe; `stations.operate` together with `stations.read` is required for station creation, rename, Agent binding and administrative status changes.

Command `CanExecute`, edit-field enabled state and workspace visibility must all reflect the same permission policy. Handlers still re-check permission immediately before an HTTP operation because a caller can invoke `ICommand.Execute` directly without consulting `CanExecute`. Permission loss/log out clears the cached station list and disables its commands. The UI must display a localized permission-denied message rather than a blank or falsely successful workspace.

These controls are not a security boundary: the Server continues to authenticate and authorize every request independently. The UI permission snapshot is a usability cache; after permission changes, re-authentication or a refreshed authoritative snapshot is needed for presentation to update.


## Permission-aware station workspace

The Desktop consumes only the effective permission snapshot returned by the Server for the authenticated operator. `stations.read` is required to view/refresh stations and request a read-only Agent health probe; `stations.operate` together with `stations.read` is required for station creation, rename, Agent binding and administrative status changes.

Command `CanExecute`, edit-field enabled state and workspace visibility must reflect the same policy. Handlers still re-check authorization immediately before each HTTP operation because a caller may invoke `ICommand.Execute` without consulting `CanExecute`. Logout/permission changes clear the local list and fence older in-flight list responses so a late result cannot restore cached station data after access is lost. Present a localized permission-denied state instead of a blank workspace or false success.

These controls are not a security boundary: the Server authenticates and authorizes every request independently. The Desktop's permission snapshot is a usability cache. If an operator's role changes while logged in, the UI must refresh the authoritative snapshot or require re-authentication; the Server must never rely on the client cache.
