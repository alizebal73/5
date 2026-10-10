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


## Incremental implementation and acceptance status — 2026-10-10

The live status is tracked in [Engineering Readiness Register](../planning/engineering-readiness-register.md); the requirements below remain design rules, not claims that every screen exists.

1. First isolated slice: StationBoardView was extracted from Shell/MainWindow.xaml to Features/Stations/StationBoardView.xaml on PR #24 head 9a058043ca8ba69f2b0bd23777263063ee681c92. Both Quick Validation and Full Foundation passed on that exact SHA. PR #24 remains Draft/unmerged.
2. Next small slice: move the ten-second Agent-health refresh timer out of MainWindow.xaml.cs into a cancellation-aware coordinator/worker. Preserve authenticated-only refresh, busy suppression, the manual refresh message behavior, and stop on logout/window shutdown. Add deterministic tests for start/stop/cancel and prevention of overlapping polls.
3. Then extract login and password-change presentation into dedicated views and reduce MainWindow to shell composition/content host. Keep PasswordBox code-behind only as a narrow secure bridge until an equally safe adapter replaces it; never persist or log password values.
4. Feature commands expose UI availability from the current operator permissions, but Server remains the final authorization boundary. Test both disabled/hidden UI controls and a rejected direct unauthorized API request.
5. Add WPF binding/resource/launch smoke checks for fa-IR RTL and en-US LTR, keyboard focus, loading/empty/error/offline/permission-denied states, and server-confirmed outcomes before adding substantial new workspaces.

Do not combine these extractions with a full color/theme overhaul, TLS changes or business-domain implementation in one change. Avoid a new MVVM framework or further class splitting without a demonstrated responsibility/testing problem.
