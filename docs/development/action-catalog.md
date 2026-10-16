# Action Catalog — GameNet 5

**Status:** Customer profile core was squash-merged into `integration/runtime-operator-v1` by [PR #127](https://github.com/alizebal73/5/pull/127) at merge SHA `973acc523f394b2171c02db2b3836fccc58c9d67` after exact PR-head Foundation, Quick Validation and Runtime Payload all passed; post-merge Foundation and Quick Validation also passed. The portable Runtime Payload was built from tested PR head `c7ed41ab508823f2a90e3b3a7901447669e70b88`; Git commit records confirm its source tree and the merge SHA share tree `c06a22264da10340ce3b128bcaaa004156bb77f6`. Keep the manifest's actual tested checkout SHA and artifact checksum; manual evidence must record the tested SHA, merge SHA, shared tree SHA and package hash without claiming the payload was built from the merge commit. No rebuild is needed solely for the commit-ID difference while the source trees are identical. Manual isolated acceptance remains NOT RUN, and this portable payload is not Setup/MSI or a production release. Customer PIN/login, Wallet/debt, card/partial refunds, Buffet/Inventory, VIP, Reports, Shifts and full Settings workflows remain future work. Slice 1 is not accepted under [Issue #98](https://github.com/alizebal73/5/issues/98); cash reversal is not approved for real-money use.

Use the live Integration branch and relevant PR/Actions evidence when verifying code. This durable catalog deliberately avoids a mutable branch SHA.

## Rules

- Server remains the authority for identity, permission, station state, audit, idempotency and persistence. ViewModel `CanExecute` and hidden buttons are usability controls only; the Server must check permission for every operation.
- A Desktop operation uses `IGameNetServerClient` and typed Shared V1 contracts. Desktop/Agent never connect directly to PostgreSQL.
- Mutations use the Server's stable error contract, actor/correlation data, idempotency and Audit behavior as listed below. Never invent a local success path or assume a timeout means the mutation failed.
- `Desktop UI: yes` means an action is reachable in the current WPF shell. `Server API only` means the API exists, but no operator-management screen/command is currently wired in this Desktop shell.
- This catalog covers current Identity/Station operations, the partial Slice 1 Session-to-cash implementation, and the integrated full cash-sale reversal action. It does not describe the full future scope: customer profiles/login, Wallet/debt, card payments, partial refunds, PS5/foosball timed billing, Buffet/Inventory, VIP, Reports, Shifts and full Settings workflows remain future work. Cash tender correction is implemented as a separate audited operation; its manual acceptance remains open. Define any new action contract before adding its UI Command.

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

## 3. Customer profile core

### `customers.search-list`
- **Owner / outcome:** Customers; return registered-customer profiles for management and Session selection.
- **Contract:** `GET /api/v1/customers?query={query}&includeInactive={bool}&take={1..100}` → `CustomerResponse[]`.
- **Permission / preconditions:** authenticated Operator with Server-side `customers.read`; query length is limited to 80 and results are capped at 100. Walk-in identities are excluded from profile management.
- **Idempotency / Audit:** read-only; no mutation key or Audit.
- **UI entry / states:** Customer manager search/refresh and a separate searchable Session customer picker (code/name/nickname/phone); only Active customers are offered for a new Session. Manager filtering does not disturb the Session selection. Empty, denied, timeout and offline states do not create a customer.
- **Evidence:** CustomerEndpoints, CustomerManagementService, GameNetServerClient.GetCustomersAsync and customer resource/binding tests.

### `customers.create`
- **Owner / outcome:** Customers; create a registered profile with unique code, display name, optional nickname and optional telephone.
- **Contract:** `POST /api/v1/customers` → `CustomerResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator and Server-side `customers.write`; the Server validates normalized code/name/nickname/phone and uniqueness.
- **Idempotency / Audit:** shared transactional mutation pipeline and request fingerprint; success writes `customers.create` Audit. Reconcile/search after an ambiguous transport failure.
- **UI entry / states:** Customer manager create command; code conflict, invalid profile, denied and unknown-response states are localized.
- **Evidence:** Customer domain and opt-in PostgreSQL customer management tests.

### `customers.update-profile`
- **Owner / outcome:** Customers; edit display name, nickname and phone without changing customer identity/code or financial history.
- **Contract:** `PUT /api/v1/customers/{id}`; `ExpectedVersion` and `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator and Server-side `customers.write`; registered customer must exist and the expected version must match.
- **Audit / concurrency:** before/after Audit deliberately excludes phone and nickname contact details; stale writes fail with `customers.version_conflict`.
- **UI entry / states:** select a registered customer, edit profile and save; reconcile an unknown write result before a new mutation.
- **Evidence:** CustomerManagementService, domain version regression and PostgreSQL tests.

### `customers.set-status`
- **Owner / outcome:** Customers; activate, deactivate or block a registered profile.
- **Contract:** `PUT /api/v1/customers/{id}/status`; expected version, 3–256 character reason and `Idempotency-Key` are required.
- **Permission / preconditions:** authenticated Operator and Server-side `customers.write`; Walk-in identities cannot be changed through customer management.
- **Audit / concurrency:** change is transactional, version-checked and audited with reason. Blocked customers are rejected by Session start; inactive customers cannot be selected for a new Session.
- **UI entry / states:** explicit Activate, Deactivate and Block commands; status reason is mandatory.
- **Evidence:** CustomerManagementService, SessionService blocked-customer policy and opt-in PostgreSQL tests.

## 4. Existing Identity Server operations not yet exposed in current Desktop

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

## 5. Partially implemented Slice 1 PC hourly Session-to-cash

**Acceptance boundary:** This path is present in Integration but remains partial and is **not accepted as a complete feature**; see [Issue #98](https://github.com/alizebal73/5/issues/98). It supports fixed-hourly PC tariffs, a generated Walk-in customer when no customer is selected, Active → PendingSettlement → Paid, invoice creation and cash-only checkout. Fractional Toman amounts are rounded upward to the next whole Toman. Timing/restart semantics, deliberate cash entry, recovery after a committed-but-unacknowledged checkout, correction/reversal semantics, Agent-offline policy, full manual WPF acceptance and independent database reconciliation remain open. Pause/resume, transfer, prepaid/free time, wallet/debt, card payments, refunds and PS5/foosball billing are out of scope for this slice.

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
- **UI entry / states:** `RefreshCommand`; each active row identifies the station code, tariff and local display of the Server-issued start timestamp. The view does not calculate authoritative elapsed time or charges locally. Selection remains tied to the wrapped Session ID after refresh.
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
- **Owner / outcome:** Sessions; end an active or paused Session and create the pending invoice needed for settlement.
- **Contract:** `POST /api/v1/sessions/{id}/end`; `EndSessionRequest(ExpectedVersion) → InvoicePreviewResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator with Server-side `sessions.operate`; the expected Session version must match. A paused Session can be ended using its already accumulated active segments.
- **Idempotency / concurrency:** stable key/request fingerprint, expected-version check, transaction coordinator and unique unsettled-session constraint.
- **Audit:** Session end and invoice issuance are audited in the same atomic mutation boundary; the audit snapshot records accumulated and final-segment ticks.
- **UI entry / states:** `EndSessionCommand`; success produces `PendingPayment`, not a cash receipt or Debt. Pricing uses the fixed hourly snapshot and summed trusted monotonic active segments; paused time is excluded, and a running segment that loses continuity fails closed for timing recovery.
- **Evidence:** `SessionService.EndAsync`, `BillingInvoiceWriter` and PostgreSQL certification.


### `sessions.pause`
- **Owner / outcome:** Sessions; pause an active PC Session and persist the trusted monotonic duration of its current active segment.
- **Contract:** `POST /api/v1/sessions/{id}/pause`; `SessionLifecycleRequest(ExpectedVersion) → SessionResponse`; required `Idempotency-Key`.
- **Permission / preconditions:** authenticated Operator with Server-side `sessions.operate`; Session must be Active and the current monotonic/boot baseline must be verifiable. If continuity is uncertain, no pause state is written and the existing timing-recovery path is required.
- **Idempotency / concurrency:** shared transactional mutation pipeline, request fingerprint and optimistic expected Session version. A replay cannot accumulate the same segment twice.
- **Audit:** before/after Session identity/state and accumulated ticks plus the paused segment's elapsed ticks are recorded in the same transaction.
- **UI entry / states:** `PauseSessionCommand` in the Session workflow; paused Sessions remain in the station's occupied/active list, with Resume and End enabled and Pause disabled. A transport-ambiguous outcome is reconciled by refresh and the retained idempotency key.
- **Evidence / acceptance:** `Session.Pause`, `SessionService.PauseAsync`, `SessionEndpoints`, `SessionWorkflowViewModel.Lifecycle`, domain tests and disposable-PostgreSQL pause/resume tests. Issue #129 tracks exact-head CI and manual acceptance.

### `sessions.resume`
- **Owner / outcome:** Sessions; resume a paused Session by opening a fresh authoritative monotonic timing segment.
- **Contract:** `POST /api/v1/sessions/{id}/resume`; `SessionLifecycleRequest(ExpectedVersion) → SessionResponse`; required `Idempotency-Key`.
- **Permission / preconditions:** authenticated Operator with Server-side `sessions.operate`; Session must be Paused and the Server must capture a valid current timing snapshot. A new Windows boot is allowed because the paused interval is excluded, not reconstructed.
- **Idempotency / concurrency:** shared transactional mutation pipeline, request fingerprint and expected-version check; duplicate or stale resumes are rejected/replayed safely.
- **Audit:** state and accumulated billable ticks are recorded with the new timing-segment boot identity.
- **UI entry / states:** `ResumeSessionCommand` in the same replaceable Session capability; localized status indicates Active versus Paused. Resume does not reset accumulated ticks or the original tariff snapshot.
- **Evidence / acceptance:** `Session.Resume`, `SessionService.ResumeAsync`, typed Shared V1 contracts, WPF commands/resources, domain tests and PostgreSQL test that resumes after a simulated Windows reboot. Issue #129 tracks exact-head gates and acceptance.

### `billing.pending-invoices.read`
- **Owner / outcome:** Billing; list pending invoices with the current Session version used for optimistic settlement.
- **Contract:** `GET /api/v1/billing/pending`; `InvoicePreviewResponse[]`.
- **Permission / preconditions:** authenticated Operator with Server-side `billing.write` (the current contract uses the write permission for this read).
- **Idempotency / concurrency:** read-only; no key. Checkout re-checks the Session version, so stale preview state cannot authorize stale payment.
- **Audit:** no read mutation Audit expected.
- **UI entry / states:** `RefreshCommand`; each pending invoice row identifies the station code, tariff, Server-calculated elapsed duration and due amount. Session version facts are supplied by the Sessions-owned `ISessionCheckoutAccess` contract; checkout still revalidates the expected version on the Server.
- **Evidence:** `BillingEndpoints`, `BillingService.ListPendingInvoicesAsync`, `SessionCheckoutAccess` and PostgreSQL workflow tests.

### `billing.cash-checkout`
- **Owner / outcome:** Billing; accept full cash payment for a pending invoice, compute change, settle the Session and append payment and cash-sale ledger records.
- **Contract:** `POST /api/v1/billing/invoices/{invoiceId}/cash`; `CashCheckoutRequest → CashCheckoutResponse`; `Idempotency-Key` required.
- **Permission / preconditions:** authenticated Operator with Server-side `billing.write`; received cash must cover the full invoice and expected Session version must match.
- **Idempotency / concurrency:** stable key/request fingerprint plus one transaction spanning invoice, cash payment, ledger, Audit and Session settlement; replay must not append duplicate payment or ledger entries.
- **Audit:** Server records checkout, Session-paid and ledger-entry transitions.
- **UI entry / states:** `CheckoutCashCommand`; timeout requires refresh/reconciliation or same-key retry, not optimistic success. Checkout remains cash-only; reversals are a separate operation and never run through this command.
- **Evidence:** `BillingService.CheckoutCashAsync`, `SessionSettlement.MarkPaidAsync`, billing/payment tests and `SessionWorkflowPostgresTests`.

### `billing.cash-sale-reversal`
- **Owner / outcome:** Billing; reverse one fully paid cash sale by the exact invoice amount applied. The original invoice amount, Payment, paid timestamp/operator and original `CashSale` ledger entry remain historical facts; the Invoice lifecycle becomes `Reversed`, and append-only reversal + typed compensating ledger records are added atomically.
- **Contract:** `POST /api/v1/billing/invoices/{invoiceId}/cash-reversal`; `CashSaleReversalRequest → CashSaleReversalResponse`; required `Idempotency-Key`.
- **Permission / preconditions:** authenticated Operator with the dedicated `billing.cash-reverse` permission. Server verifies a paid cash Payment, consistent original sale/ledger, matching Payment ID, exact confirmed invoice amount, and a mandatory 3–256 printable-character reason. Wallet/card payments and partial reversals are rejected/out of scope. Never refund gross tender or previously given change.
- **Idempotency / concurrency:** shared `CommandMutationPipeline`, request fingerprint and transactional persistence; filtered unique indexes and a unique reversal record enforce at most one full reversal per invoice/payment under races. Same-intent retry replays the original response; changed intent conflicts.
- **Audit:** reversal record, compensating ledger entry, invoice state and both Audit events share the same transaction. Original Payment/Ledger/Audit history is never edited or deleted.
- **UI entry / states:** `ReverseCashSaleCommand` in the Session workflow WPF view. The operator selects a recent paid cash checkout, reviews the amount, enters a reason and explicitly confirms. If a response is lost, the command remains blocked until the durable Server outcome proves whether it committed; no optimistic success or blind second refund.
- **Evidence / acceptance:** `BillingService.ReverseCashSaleAsync`, `CashSaleReversal`, migration `20261016100000_CashSaleReversal`, `CashSaleReversalTests`, `SessionWorkflowPostgresTests` and `SessionWorkflowViewModelTests`. PR #120 was squash-merged to `integration/runtime-operator-v1` at merge SHA `0660e632f0e38ec1654a2830f633e2f3cd60ae5f`. Its exact PR head `ad92960382088872b1da0441241f4d71ff3603f9` passed Quick Validation, Full Foundation (PostgreSQL, backup/restore, Desktop and Agent runtime certification), and the Runtime LAN Smoke Payload artifact build. Post-merge Quick Validation passed on the merge SHA. This action remains unaccepted for operational use until manual WPF acceptance/recovery testing and independent Invoice/Payment/Reversal/Ledger/Audit reconciliation are recorded. Cash tender correction remains a separate not-yet-implemented action.

## 6. Cross-cutting error and permission rules

- Station list and health probe require `stations.read`; station mutation requires `stations.operate`. The Server checks these; UI `CanExecute` never grants authority.
- Current station mutation typed error codes include `stations.forbidden`, `stations.invalid`, `stations.code_exists`, `stations.not_found`, `stations.agent_not_provisioned`, `stations.agent_not_applicable`, `stations.agent_not_bound`, `stations.device_already_bound`, `stations.version_conflict`, `stations.invalid_transition`, `idempotency.required`, `idempotency.in_flight`, `idempotency.key_reused` and `security.https_required`.
- Login's current unauthenticated response normalizes invalid credentials, disabled accounts and locked accounts to avoid account-state disclosure; treat any client message as presentation, not as a second authority.
- The current Windows shell uses inherited `LoginViewModel` DataContext for both shell and StationBoard View. The View has no direct database access, no business code-behind, and no invented success path.

## 7. Verification evidence and future updates

- View extraction / binding checks: update `tests/Desktop.Tests/DesktopBindingAndPasswordContractTests.cs`, `DesktopResourceContractTests.cs`, and `StationBoardViewBoundaryTests.cs` when applicable.
- Existing permission, filter and client/server behavior coverage: `StationBoardPermissionTests.cs`, `StationBoardFilterTests.cs`, `GameNetServerClientStationTests.cs`, `StationService` and `StationAgentHealthService` tests.
- For any new action, add Requirement ID, stable Action ID, owner module, Shared request/response, server permission, idempotency/optimistic-concurrency semantics, Audit/outbox effects, every UI entry point, disabled/busy/loading/empty/offline/timeout/denied/conflict/success states, and exact-SHA tests.
- **Catalog completeness is intentionally limited** to current Identity/Station actions, Customer profile core in section 3, and the partial Slice 1 Session-to-cash actions in section 4. Customer PIN/login, Wallet/debt, refunds/card, Buffet/Inventory, VIP, Reports, Shifts and full Settings workflows remain future work.

