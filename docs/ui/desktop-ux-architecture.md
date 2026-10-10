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


## Incremental implementation boundary

The station workspace is hosted by `Features/Stations/StationBoardView.xaml` as a presentation-only WPF `UserControl`. It deliberately inherits the shell's current `LoginViewModel` DataContext for this first extraction, preserving the existing `StationBoard.*` bindings, commands, filters, and server-backed outcomes without changing API contracts or operational behavior. The control contains no code-behind event handlers or business logic.

This is a compatibility-preserving first slice, not the completed shell refactor. Before adding substantial new screens, continue with these isolated steps:

1. Move periodic Agent health refresh lifecycle out of `Shell/MainWindow.xaml.cs` into a testable feature/coordinator boundary, preserving the ten-second cadence, authenticated-only behavior, and cancellation on logout/shutdown.
2. Extract login and password-change presentation into dedicated views while keeping credential handling narrowly scoped and avoiding password persistence.
3. Reduce `MainWindow` to shell composition, authentication/navigation state, and the content host; add view-model/command smoke coverage for the resulting bindings.

Each step must retain server-side authorization and must not move financial or operational authority into the UI.
