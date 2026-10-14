# Action Catalog — GameNet 5

**Status:** Identity/Station actions plus the Draft Slice 1 PC cash candidate, updated 2026-10-15. Entries in section 3 describe the candidate under PRs #100–#103; they are not a claim that the complete product or release is ready.

**Baseline:** `integration/runtime-operator-v1` was read at `1541714bd4acd7ef74c1dee002e30d92a95c6643` before this refactor branch was created. Re-read the live Integration SHA before each later change.

## Rules

- Server remains the authority for identity, permission, station state, audit, idempotency and persistence. ViewModel `CanExecute` and hidden buttons are usability controls only; the Server must check permission for every operation.
- A Desktop operation uses `IGameNetServerClient` and typed Shared V1 contracts. Desktop/Agent never connect directly to PostgreSQL.
- Mutations use the Server's stable error contract, actor/correlation data, idempotency and Audit behavior as listed below. Never invent a local success path or assume a timeout means the mutation failed.
- `Desktop UI: yes` means an action is reachable in the current WPF shell. `Server API only` means the API exists, but no operator-management screen/command is currently wired in this Desktop shell.
- This catalog covers current Identity/Station operations plus the Slice 1 PC cash candidate in section 3. It does not describe the full future scope: customer profiles/login, Wallet/debt, card payments/refunds, PS5/foosball timed billing, Buffet/Inventory, VIP, Reports, Shifts and full Settings workflows remain future work. Define any new action contract before adding its UI Command.

## 1. Current Desktop Identity operations

### `identity.login`
- **Owner / outcome:** Identity; authenticate the operator and start a Server-issued session.
- **Contract:** `POST /api/v1/auth/login`; `LoginRequest → LoginResponse`.
- **Permission / preconditions:** unauthenticated endpoint; non-empty username/password; Server performs credential verification, account/lockout policy and issues the token.
- **Idempotency / concurrency:** no `Idempotency-Key` in the current login client contract. Do not treat a timeout as evidence that no Server-side login/audit occurred; on transport failure the client clears its local session.
- **Audit:** Identity Server audits accepted and rejected login attempts. Unauthenticated response deliberately does not disclose whether an account is disabled or locked.
- **UI entry / states:** `LoginCommand` in `LoginViewModel`; busy/invalid input, invalid credentials, HTTPS requirement, network timeout/failure, success followed by `GET /api/v1/auth/me` to load authoritative permissions.
- **Evidence:** `LoginViewModel`, `IdentityEndpoints`, `IdentityService`, `GameNetServerClient`; Desktop binding/password contract tests.

### `identity.current-operator.read`
- **Owner / outcome:** Identity; retrieve the authenticated operator's current identity and effective permissions after login.
- **Contract:** `GET /api/v1/auth/me`; `CurrentOperatorResponse`.
- **Permission / preconditions:** authenticated `Operator` policy; no separate named permission in this endpoint. Server-side session validation is authoritative.
- **Idempotency / concurrency:** read-only; no idempotency key.
- **Audit:** no mutation Audit expected; correlated API diagnostics apply.
- **UI entry / states:** invoked by `LoginViewModel.LoginAsync` after login; a failed read causes local session clearing and login failure messaging.
- **Evidence:** `LoginViewModel`, `IdentityEndpoints`, `GameNetServerClient`, `StationBoardPermissionTests`.

### `identity.logout`
- **Owner / outcome:** Identity; revoke the current authenticated Server session, then clear this Desktop session and sensitive UI state.
- **Contract:** `POST /api/v1/auth/logout`; `LogoutResponse`.
- **Permission / preconditions:** authenticated `Operator` policy and a valid current user/session identifier.
- **Idempotency / concurrency:** no `Idempotency-Key` in the current logout contract. Desktop clears local session even if the network call fails; the Server's persisted session revocation remains authoritative.
- **Audit:** Server records logout/session-revocation Audit.
- **UI entry / states:** `LogoutCommand`; if network logout fails, the local session is still cleared and a warning is shown.
- **Evidence:** `LoginViewModel`, `IdentityEndpoints`, `IdentityService`, `DesktopBindingAndPasswordContractTests`.

### `identity.change-own-password`
- **Owner / outcome:** Identity; change the current operator's password and revoke other sessions according to the Server result.
- **Contract:** `PUT /api/v1/identity/me/password`; `ChangeOwnPasswordRequest → ChangeOwnPasswordResponse`; `Idempotency-Key` header required by service.
- **Permission / preconditions:** authenticated `Operator`; current password valid; new password 10–256 chars and confirmation matches in UI. The Server independently validates.
- **Idempotency / concurrency:** Desktop retains the same key for the same values after a timeout; it resets the pending key when values change or on definitive 4xx failure.
- **Audit:** Server records the password-change mutation; passwords must never appear in Audit JSON/logging.
- **UI entry / states:** `ChangePasswordCommand`; validation, current-password rejection, unchanged/invalid password, timeout with same-value retry, session revocation and success. PasswordBoxes are cleared after success/logout.
- **Evidence:** `LoginViewModel`, `OperatorManagementEndpoints`, `OperatorManagementService`, `DesktopBindingAndPasswordContractTests`.

## 2. Current Desktop Station operations

### `stations.refresh`
- **Owner / outcome:** Stations; retrieve the Server-authoritative station list and apply local search/filter state.
- **Contract:** `GET /api/v1/stations`; `StationResponse[]`.
- **Permission / preconditions:** authenticated `Operator` plus Server-side `stations.read`.
- **Idempotency / concurrency:** read-only; no idempotency key. A generation fence prevents a late response repopulating local station data after logout/permission revocation.
- **Audit:** no mutation Audit expected.
- **UI entry / states:** No dedicated refresh button is wired in the current XAML. The ViewModel refresh path runs on post-login initial load and re-read following successful station mutations/probes; busy, empty/no-results, denied, offline, timeout and success.
- **Evidence:** `StationBoardViewModel`, `StationBoardPermissionTests`, `StationBoardFilterTests`, `StationEndpoints`, `StationService`.

### `stations.create`
- **Owner / outcome:** Stations; create a station of type PC, PS5 or Foosball.
- **Contract:** `POST /api/v1/stations`; `CreateStationRequest → StationResponse`.
- **Permission / preconditions:** authenticated `Operator` and Server-side `stations.operate`; code/name required; type must be a known `StationTypeContract`.
- **Idempotency / concurrency:** `Idempotency-Key` required; replay/conflict handled by the Server. The same pending key is reused after ambiguous timeout and cleared after definitive failure/success.
- **Audit:** Server writes `stations.create` Audit in the mutation transaction.
- **UI entry / states:** create section and `CreateCommand); invalid data, duplicate code, forbidden, key reused/in-flight, network timeout, no success until the Server result is read back.
- **Evidence:** `StationBoardViewModel`, `StationService`, `StationEndpoints`, `StationBoardPermissionTests`.

### `stations.rename`
- **Owner / outcome:** Stations; rename the selected station.
- **Contract:** `PUT /api/v1/stations/{id}`; `RenameStationRequest(Name, ExpectedVersion) → StationResponse`.
- **Permission / preconditions:** authenticated `Operator` and `stations.operate); selected station and non-empty name; optimistic concurrency via `ExpectedVersion`.
- **Idempotency / concurrency:** required `Idempotency-Key); Server rejects stale station version or re-used key with different payload.
- **Audit:** Server writes `stations.rename` Audit with before/after state.
- **UI entry / states:** selected-station details and `RenameCommand); missing/not found, version conflict, forbidden, key conflict, timeout/retry and success after re-read.
- **Evidence:** `StationBoardViewModel`, `StationService`, `StationEndpoints`.

### `stations.bind-agent`
- **Owner / outcome:** Stations; bind the selected PC station to a provisioned Agent DeviceId.
- **Contract:** `PUT /api/v1/stations/{id}/agent`; `BindStationAgentRequest(DeviceId, ExpectedVersion) → StationResponse`.
- **Permission / preconditions:** authenticated `Operator` and `stations.operate); PC station; active provisioned Agent credential; a DeviceId may not be bound to another station.
- **Idempotency / concurrency:** required `Idempotency-Key`; expected-version check prevents stale updates.
- **Audit:** Server writes `stations.bind_agent` Audit with before/after state.
- **UI entry / states:** Device ID field and `BindAgentCommand); Agent not provisioned/not applicable, already bound, version conflict, forbidden, timeout and success after re-read.
- **Evidence:** `StationBoardViewModel`, `StationService`, `StationEndpoints`.

### `stations.set-status`
- **Owner / outcome:** Stations; set administrative status to Available or Maintenance in the current UI.
- **Contract:** `PUT /api/v1/stations/{id}/status`; `SetStationStatusRequest(Status, ExpectedVersion) → StationResponse`.
- **Permission / preconditions:** authenticated `Operator` and `stations.operate); selected station; the Server validates allowed status transitions.
- **Idempotency / concurrency:** required `Idempotency-Key`; `ExpectedVersion` protects against concurrent updates.
- **Audit:** Server writes `stations.set_status` Audit with before/after state.
- **UI entry / states:** `MaintenanceCommand` and `AvailableCommand); forbidden, invalid transition, version conflict, key conflict, timeout and confirmed success after re-read.
- **Evidence:** `StationBoardViewModel`, `StationService`, `StationEndpoints`.

### `stations.agent-health-probe`
- **Owner / outcome:** Stations/Agent dispatch; send a health probe to the Agent bound to a PC and show the acknowledged result.
- **Contract:** `POST /api/v1/stations/{id}/agent/health-probe`; `AgentCommandAcknowledgement`.
- **Permission / preconditions:** authenticated `Operator` and Server-side `stations.read`; selected PC station with a bound Agent. PS5/Foosball do not use the Agent.
- **Idempotency / concurrency:** current Desktop contract has no `Idempotency-Key`; treat retry after timeout as a new probe, not guaranteed replay of the same command. This remains a limitation to revisit if health probing becomes a high-risk workflow.
- **Audit:** Server records Requested and terminal outcome (Succeeded/Rejected/Unavailable/Cancelled), including denied/not-found conditions.
- **UI entry / states:** `AgentHealthProbeCommand`; disabled for non-PC/unbound station; offline/timeout/denied/command rejection and confirmed acknowledgement followed by station re-read.
- **Evidence:** `StationBoardViewModel`, `StationAgentHealthService`, `StationEndpoints`, `StationBoardPermissionTests`.

## 3. Existing Identity Server operations not yet exposed in current Desktop

The endpoints below exist in the current Server, but this initial Desktop shell has no operator-management screen or command wired to them. Do not add a button for any of these until the associated requirement/vertical slice, typed ViewModel command, UI states, tests and exact-SHA evidence are ready.

| Action ID | Server contract | Permission (plus authenticated Operator) | Idempotency / Audit / reason | Desktop entry |
|---|---|---|---|---|
| `identity.users.list` | `GET /api/v1/identity/users` → operator summaries | `identity.users.read` | Read-only; no mutation Audit expected | Not wired in current Desktop |
| `identity.users.create` | `POST /api/v1/identity/users` → `CreateOperatorRequest` / operator summary | `identity.users.write` | `Idempotency-Key`; reason required; mutation Audit; never audit plaintext password | Not wired in current Desktop |
| `identity.users.assign-role` | `PUT /api/v1/identity/users/{id}/role` → operator summary | `identity.users.write` | `Idempotency-Key`; reason required; mutation Audit | Not wired in current Desktop |
| `identity.users.set-active` | `PUT /api/v1/identity/users/{id}/active` → operator summary | `identity.users.write` | `Idempotency-Key`; reason required; mutation Audit; last-Owner/session invariants | Not wired in current Desktop |
| `identity.users.reset-password` | `PUT /api/v1/identity/users/{id}/password` → reset result | `identity.users.password-reset` | `Idempotency-Key`; reason required; mutation Audit; redact all credentials | Not wired in current Desktop |
| `identity.roles.list` | `GET /api/v1/identity/roles` → role summaries | `identity.users.read` | Read-only; no mutation Audit expected | Not wired in current Desktop |
| `identity.roles.create` | `POST /api/v1/identity/roles` → role summary | `identity.roles.manage` | `Idempotency-Key`; reason required; mutation Audit | Not wired in current Desktop |
| `identity.roles.update-permissions` | `PUT /api/v1/identity/roles/{id}/permissions` → role summary | `identity.roles.manage` | `Idempotency-Key`; reason required; before/after permissions in Audit | Not wired in current Desktop |

## 4. Cross-cutting error and permission rules

- Station list and health probe require `stations.read`; station mutation requires `stations.operate`. The Server checks these; UI `CanExecute` never grants authority.
- Current station mutation typed error codes include `stations.forbidden`, `stations.invalid`, `stations.code_exists`, `stations.not_found`, `stations.agent_not_provisioned`, `stations.agent_not_applicable`, `stations.agent_not_bound`, `stations.device_already_bound`, `stations.version_conflict`, `stations.invalid_transition`, `idempotency.required`, `idempotency.in_flight`, `idempotency.key_reused` and `security.https_required`.
- Login's current unauthenticated response normalizes invalid credentials, disabled accounts and locked accounts to avoid account-state disclosure; treat any client message as presentation, not as a second authority.
- The current Windows shell uses inherited `LoginViewModel` DataContext for both shell and StationBoard View. The View has no direct database access, no business code-behind, and no invented success path.

## 5. Verification evidence and future updates

- View extraction / binding checks: update `tests/Desktop.Tests/DesktopBindingAndPasswordContractTests.cs`, `DesktopResourceContractTests.cs`, and `StationBoardViewBoundaryTests.cs` when applicable.
- Existing permission, filter and client/server behavior coverage: `StationBoardPermissionTests.cs`, `StationBoardFilterTests.cs`, `GameNetServerClientStationTests.cs`, `StationService` and `StationAgentHealthService` tests.
- For any new action, add Requirement ID, stable Action ID, owner module, Shared request/response, server permission, idempotency/optimistic-concurrency semantics, Audit/outbox effects, every UI entry point, disabled/busy/loading/empty/offline/timeout/denied/conflict/success states, and exact-SHA tests.
- **Catalog completeness is intentionally limited** to current Identity/Station API operations. Future customer/session/checkout/buffet/inventory/VIP/report/settings actions do not yet exist in this Catalog.

## 3. Draft Slice 1 PC hourly Session-to-cash candidate

**Acceptance boundary:** These entries describe a narrow PC-only candidate, not completion of the corresponding P0 requirements. The candidate supports fixed hourly PC tariffs, a generated Walk-in customer when no customer is selected, Active → PendingSettlement → Paid, and cash-only checkout. Fractional Toman amounts are rounded up to the next whole Toman by the current calculator. Pause/resume, transfer, prepaid/free-time, wallet/debt, card payments, refunds, and PS5/foosball billing are not included.

### `tariffs.active-list`
- **Owner / outcome:** Tariffs; read active tariffs filtered by station type.
- **Contract:** `GET /api/v1/tariffs?stationType=PC`; `TariffResponse[]`.
- **Permission / preconditions:** authenticated Operator and either `sessions.operate` or `settings.write`; the Server filters to active tariffs.
- **Idempotency / concurrency:** read-only; no idempotency key.
- **Audit:** no mutation Audit expected.
- **UI entry / states:** `RefreshCommand` in `SessionWorkflowViewModel`; the current feature asks for PC tariffs only. Empty/denied/offline/timeout states must not imply that a tariff was created or changed.
- **Evidence:** `TariffEndpoints`, `TariffService`, `IGameNetServerClient.GetTariffsAsync`, Desktop binding/resource tests.

### `tariffs.create-fixed-hourly`
- **Owner / outcome:** Tariffs; create an active fixed-hourly tariff with an integer Toman-per-hour rate and an optional default designation.
- **Contract:** `POST /api/v1/tariffs`; `CreateTariffRequest → TariffResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator with Server-side `settings.write`; current UI creates PC tariffs. The Server validates station type, code, name, and positive rate. A default tariff replaces the prior default for that station type inside the mutation boundary.
- **Idempotency / concurrency:** request fingerprint and stable key; the Server rejects duplicate code, conflicts, missing keys, and key reuse with a different payload.
- **Audit:** create and default-removal transitions are audited by the Server.
- **UI entry / states:** `CreateTariffCommand`; no local success before the Server response. Full scheduled/tiered pricing is not implemented.
- **Evidence:** `TariffService`, `TariffEndpoints`, tariff domain tests and Slice 1 PostgreSQL certification.

### `sessions.active-list`
- **Owner / outcome:** Sessions; read authoritative active Sessions, optionally filtered by station.
- **Contract:** `GET /api/v1/sessions/active`; `SessionResponse[]`.
- **Permission / preconditions:** authenticated Operator with Server-side `sessions.operate`.
- **Idempotency / concurrency:** read-only; no idempotency key. The response includes Session version; a later mutation must check it.
- **Audit:** no mutation Audit expected.
- **UI entry / states:** `RefreshCommand`; active Sessions are read from the Server, not maintained as an authoritative local timer.
- **Evidence:** `SessionEndpoints`, `SessionService.ListActiveAsync`, `SessionWorkflowPostgresTests`.

### `sessions.start-pc`
- **Owner / outcome:** Sessions; start a Session on one available PC with an active applicable hourly tariff.
- **Contract:** `POST /api/v1/sessions`; `StartSessionRequest → SessionResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator with Server-side `sessions.operate`; current UI allows only a PC station and active PC tariff. When `CustomerId` is null, the Server creates and attaches a Walk-in customer identity.
- **Idempotency / concurrency:** stable operation key/request fingerprint plus persisted station/session constraints prevent duplicate starts and more than one unsettled Session on a station.
- **Audit:** the Server writes `sessions.start` Audit within the mutation transaction.
- **UI entry / states:** `StartSessionCommand`; busy/error/conflict states remain controlled by the feature port. Agent lock/unlock is not implemented by this slice.
- **Evidence:** Session domain tests and PostgreSQL replay/concurrency tests.

### `sessions.end`
- **Owner / outcome:** Sessions; end an active Session and create the pending invoice needed for settlement.
- **Contract:** `POST /api/v1/sessions/{id}/end`; `EndSessionRequest(ExpectedVersion) → InvoicePreviewResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator with Server-side `sessions.operate`; the expected Session version must match.
- **Idempotency / concurrency:** stable key/request fingerprint, expected-version check, transaction coordinator and unique unsettled-session constraint.
- **Audit:** Session end and invoice issuance are audited in the same atomic mutation boundary.
- **UI entry / states:** `EndSessionCommand`; success produces `PendingPayment`, not a cash receipt or Debt. Pricing uses the fixed hourly snapshot and exact elapsed UTC duration; fractional Toman rounds up.
- **Evidence:** `SessionService.EndAsync`, `BillingInvoiceWriter` and PostgreSQL certification.

### `billing.pending-invoices.read`
- **Owner / outcome:** Billing; list pending invoices with the current Session version used for optimistic settlement.
- **Contract:** `GET /api/v1/billing/pending`; `InvoicePreviewResponse[]`.
- **Permission / preconditions:** authenticated Operator with Server-side `billing.write` (the current contract uses the write permission for this read).
- **Idempotency / concurrency:** read-only; no key. Checkout re-checks the Session version, so stale preview state cannot authorize stale payment.
- **Audit:** no read mutation Audit expected.
- **UI entry / states:** `RefreshCommand`; Session version facts are supplied by the Sessions-owned `ISessionCheckoutAccess` contract.
- **Evidence:** `BillingEndpoints`, `BillingService.ListPendingInvoicesAsync`, `SessionCheckoutAccess` and PostgreSQL workflow tests.

### `billing.cash-checkout`
- **Owner / outcome:** Billing; accept full cash payment for a pending invoice, compute change, settle the Session and append payment and cash-sale ledger records.
- **Contract:** `POST /api/v1/billing/invoices/{invoiceId}/cash`; `CashCheckoutRequest → CashCheckoutResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator with Server-side `billing.write`; received cash must cover the full invoice and expected Session version must match.
- **Idempotency / concurrency:** stable key/request fingerprint plus one transaction spanning invoice, cash payment, ledger, Audit and Session settlement; replay must not append duplicate payment or ledger entries.
- **Audit:** Server records checkout, Session-paid and ledger-entry transitions.
- **UI entry / states:** `CheckoutCashCommand`; timeout requires refresh/reconciliation or same-key retry, not optimistic success. Card payments, Wallet, Debt, split allocation and refunds/reversals are outside this slice.
- **Evidence:** `BillingService.CheckoutCashAsync`, `SessionSettlement.MarkPaidAsync`, billing/payment tests and `SessionWorkflowPostgresTests`.
