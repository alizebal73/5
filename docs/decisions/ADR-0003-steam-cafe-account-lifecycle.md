# ADR-0003: Steam Café Account and Auto-Login Boundary

- Status: Proposed; policy/provider decision and implementation remain open
- Date: 2026-10-10
- Scope: GamingAccounts module, WPF Gaming Accounts workspace, Agent command boundary and session lifecycle
- Depends on: ADR-0002 Server and Agent Secret Lifecycle, Station/Agent model, Session state machine

## 1. Purpose

GameNet must keep the planned Steam/gaming-account capability compatible with the whole product. Steam accounts are neither customer identities nor Server deployment credentials. They have their own ownership model, authorization rules, station assignment, session lifecycle, external-platform constraints and credential lifecycle.

The current Foundation candidate contains Agent identity/authentication/lease transport, but no Steam account model, Steam integration provider or Steam Auto Login implementation was found in the reviewed repository tree. This ADR is therefore a product boundary and implementation gate, not a statement that Steam integration already exists.

## 2. External policy gate — do not assume generic Auto Login is authorized

The currently published Steam PC Café documentation describes two operating models:

- Standard PC Café model: patrons log in using their own personal Steam accounts. The venue manages commercial game licenses through its PC Café license pool and PC Café Server.
- Dedicated account per station: explicitly described in the current Steam documentation for VR arcades. Steam notes that each station account needs its own commercial game licenses and that this model does not provide the PC Café Server/license-pool/content-cache benefits described for the standard model.

The Steam Subscriber Agreement states that an account is personal and restricts sharing/transferring account access except where expressly permitted. It also restricts scripts, bots, macros and other non-human-controlled automation to interact with Steam, and restricts tampering with Steam's execution or controlling its processes/UI except where authorized.

Therefore:

1. Do not assume that a normal gaming café may keep a pool of ordinary end-user Steam accounts and auto-log them in for patrons.
2. Do not implement credential typing/injection, Steam UI scripting, process tampering, Steam Guard/MFA bypass, account-creation automation, or other login automation unless the exact mechanism is documented as supported or expressly authorized by Valve for this venue/model.
3. The standard PC Café mode must allow patrons to sign in themselves and use authorized café commercial licenses; GameNet must not collect or store their personal Steam passwords.
4. Dedicated station-account mode may only be enabled after the operator documents that its venue and account/license setup is permitted by the applicable Steam program/agreement. The VR-arcade model must not silently be assumed to cover a conventional PC gaming café.
5. If no supported/authorized automation method is established, keep Auto Login disabled and provide only the allowed account-state/launch workflow. Do not implement a technical workaround to evade a policy restriction.

Official references reviewed on 2026-10-10:
- Steam PC Café Program — Getting Started: https://partner.steamgames.com/doc/sitelicense/licensees/gettingstarted
- Steam PC Café Program — Licensees: https://partner.steamgames.com/doc/sitelicense/licensees
- Steam Subscriber Agreement: https://store.steampowered.com/subscriber_agreement/

## 3. Separate identity and secret domains

GameNet must distinguish at least these identities/secrets:

1. **Customer identity:** GameNet business identity, profile, PIN/credential where applicable, wallet, debt and session history. Never equate it with a Steam account.
2. **Steam personal account:** owned and authenticated by the customer. GameNet does not collect its password, Steam Guard code, recovery code, session cookie or access token.
3. **Steam café/master account and commercial license pool:** controlled by the venue in the official PC Café program. Account administration stays on the supported Steam/Steamworks management path; the GameNet UI may store non-secret site/model/status metadata but must not imitate or bypass the partner portal.
4. **Authorized dedicated station account:** venue-controlled external account associated with a station only under a supported/authorized model. This account's secrets require a dedicated external-account vault domain.
5. **GameNet Server deployment secrets:** PostgreSQL runtime credential, JWT signing key and Agent provisioning control key, as covered by ADR-0002. These must not be conflated with the credentials of external gaming accounts.
6. **Agent per-device credential:** the credential used by GameNet Agent to authenticate to the GameNet Server; it is not a Steam credential.

## 4. Module and data ownership

Create a `GamingAccounts` module boundary, following the standard module layers (Domain, Application, Infrastructure, Api, Tests).

The module owns:

- venue's configured gaming-account operating mode and authorization/evidence state;
- non-secret account metadata and external Steam identity/reference;
- station assignment and availability;
- allowed-mode validation;
- credential configured/rotation/re-authentication state, without revealing the credential;
- typed login/launch outcomes and their link to the session, without becoming authoritative for session or billing state;
- audit events for account assignment, enable/disable, credential replacement/rotation, supported sign-in attempt and failures.

The module does not own customer identity, station lease ownership, session state machine, commercial Steam license validity, or Steam's authentication service. Server remains the authority for GameNet permissions, station/session assignment and command authorization. Steam remains the authority for its own accounts and licenses.

## 5. Credential vault contract for authorized dedicated accounts

If and only if the account mode is permitted and a supported/authorized provider exists:

- Store the account metadata relationally. Never store the password, MFA/recovery material, session cookie or refresh/access token in plaintext in PostgreSQL, JSON, environment variables, command lines, logs, diagnostics or ordinary backup exports.
- Use field/record-level envelope encryption for any persistent dedicated-account credential material. Protect the versioned vault wrapping key outside PostgreSQL through the Windows protected-secret boundary; keep this vault key, its permissions, rotation and recovery lifecycle distinct from the Server's startup secrets. Do not invent custom cryptography.
- Enforce DACLs and access through a typed vault interface; only the Server service may unwrap credentials for an authorized operation. Ordinary WPF API reads return metadata/status only. No normal password-reveal endpoint is provided.
- Any credential transfer to an Agent must be disabled until the supported integration method is established. If the authorized method requires a credential at the station, transfer only to the paired Agent for the assigned station and valid session, over authenticated HTTPS, with minimum lifetime/scope. Keep it in memory only; never persist the Steam credential to Agent files.
- Never bypass Steam Guard/MFA or collect the customer's recovery codes. If a supported interactive re-authentication is required, mark the account as requiring operator action and fail closed.
- Account-vault encryption keys cannot be kept only in the same database as the encrypted credentials. Secret-key rotation and vault recovery need explicit version identifiers, tests and documented recovery behavior. Losing the vault key must not trigger silent account reset or a destructive database reset.
- Logs and audit record the actor, account ID, station, session/correlation ID, outcome code, timestamp and key/version metadata as appropriate, but never credential values, tokens, MFA codes or raw HTTP request bodies containing secret material.

Encrypted credential data may be included in a database backup only if the vault key is independently protected and the restore contract is tested; plaintext external account credentials must never be backed up. Cross-machine restore must explicitly re-provision or recover the vault key through the approved secure process.

## 6. Station/session integration

Steam account state is separate from both Agent connectivity and GameNet session state.

Suggested account/station lifecycle labels (not yet implemented):

- Unconfigured
- Available
- Assigned
- LoginPending (only if authorized integration is enabled)
- Ready
- ReauthenticationRequired
- LoginFailed
- Releasing
- Disabled

Rules:

- A GameNet Session transition remains transactional and authoritative in the Server. Steam provider status cannot mark a session financially complete or change station ownership on its own.
- Any account assignment/login command is authorized by Server policy and the current authoritative Agent lease. A stale Agent cannot claim or sign in to an account after its lease is fenced.
- The default is at most one active station assignment per dedicated account. Any exception must be explicit in the supported Steam model and tested.
- Start/stop/transfer/release must use idempotent commands with operation IDs, typed outcomes and reconciliation after Agent or Server restart.
- An account sign-in failure must not silently report the station as ready for game play. The configured business policy decides whether the customer session remains active, is paused, or requires operator recovery; billing logic must not be owned by the Steam integration.
- On session end, run only the logout/cleanup method supported for the enabled model. Do not claim credentials were cleared merely because the Steam window or game process exited.
- PC stations may use Agent commands; PS5/Foosball station types must not inherit Steam account automation or be represented as having a PC Agent.

## 7. WPF workspace and permissions

Add a native WPF **Gaming Accounts** workspace to the existing navigation architecture.

Show according to operator permissions:

- selected Steam operating model and its authorization state;
- non-secret account label/Steam identity/reference;
- allowed station mapping and current assignment;
- configured/disabled/rotation-needed/re-authentication-needed state;
- last operation outcome and relevant audit history.

Protected actions include add/replace credential for an authorized venue account, rotate, disable, assign/unassign station and require re-authentication. Treat credential entry as write-only: there is no ordinary “show password” feature. Hide/disable Auto Login controls when the selected model or provider has not passed its authorization gate. Server authorization still applies; hiding a control is not a security boundary.

Do not add a customer Steam-password field to the Customer profile. Customer Steam accounts remain outside GameNet's credential store.

## 8. Required tests and acceptance

Before an authorized dedicated-account provider is enabled:

1. Policy/configuration test fails closed when no authorized account mode/provider is configured.
2. Personal Steam account credentials cannot be submitted to or retrieved from GameNet customer/profile APIs.
3. Secret redaction covers credential input/output, logs, traces, exceptions and diagnostics.
4. Unauthorized operators cannot inspect, set, rotate, assign, disable or trigger account operations.
5. A dedicated-account credential can be decrypted only by the approved Server service/vault boundary; ciphertext alone and normal database readers cannot recover plaintext.
6. Concurrent assignment prevents the same account being assigned to two stations unless a supported model explicitly allows it.
7. Session/Agent lease fencing, duplicate commands, failed login, Agent restart, Server restart, disconnect/reconnect and cleanup outcomes reconcile safely.
8. Credential rotation/restore and vault-key recovery are tested without plaintext exports or unintended credential reactivation.
9. An exact written authorization or current official supported mechanism is recorded with the tested integration method. Agreement/policy review is repeated before enabling automation after relevant terms change.

## 9. Delivery sequence

1. Accept the product/security policy boundary in this ADR before any implementation of Steam login automation.
2. Include the GamingAccounts module boundary in Stage 2; implement metadata, permissions and audit only after the corresponding stages/gates.
3. Build the vault boundary independently of Server deployment secrets, alongside the protected-secret implementation.
4. Integrate the authorized provider with Stations/Agents and Sessions only after Stage 3 permissions and the Stage 4/6 ownership/fencing rules are present.
5. Implement and test WPF account-management UI in the Desktop UX stage.
6. Keep Auto Login disabled until the exact venue/account model and integration method are supported or expressly authorized. If not authorized, ship the non-automating PC Café mode instead.

References:
- ADR-0002 (proposed in draft PR #18): https://github.com/alizebal73/5/pull/18
- Master build plan: docs/planning/master-build-plan.md
- Desktop UX architecture: docs/ui/desktop-ux-architecture.md
