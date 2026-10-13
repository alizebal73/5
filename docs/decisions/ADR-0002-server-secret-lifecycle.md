# ADR-0002: Server and Agent Secret Lifecycle

- Status: Accepted — architecture decision approved by the product owner on 2026-10-13
- Decision scope: security architecture and required implementation contracts
- Related decision: [ADR-0001 — Server TLS Certificate Lifecycle](ADR-0001-server-tls-certificate-lifecycle.md)
- Implementation and release status: Incomplete; see “Required work and acceptance gates” below and Issues [#16](https://github.com/alizebal73/5/issues/16) and [#34](https://github.com/alizebal73/5/issues/34).

## 1. Context

GameNet has different kinds of secrets with different trust boundaries: Server deployment secrets, per-device Agent credentials, one-time enrollment tokens, and the Server TLS private key. They must not share a storage or recovery mechanism by accident.

The current Integration tree includes a DPAPI-protected Server secret store, protected bootstrap/finalization code, one-time Agent enrollment with expiry/replay protection, and automated tests for relevant Server and Agent paths. This is implementation progress, not proof that Windows Services run with the intended identities or can use DPAPI and TLS keys after a real service restart. Full secret rotation, recovery, and final Setup-based acceptance remain open work.

This ADR fixes the canonical design so dependent features and the eventual installer do not invent competing secret lifecycles.

## 2. Decision

### 2.1 Server deployment secrets

1. Production Server secrets use the versioned protected store at:
   %ProgramData%\GameNet Manager\Secrets\server-secrets.v1.dpapi
2. Protect the serialized payload with Windows DPAPI LocalMachine scope and the purpose-separation entropy label GameNet5.ServerSecrets.v1. The label is not an independent encryption key; custom cryptography and adjacent wrapping passwords are prohibited.
3. The payload is versioned and includes a generation identifier and the Server-only values required by current code: PostgreSQL runtime connection string, JWT signing key, Agent provisioning key, and a dedicated idempotency-fingerprint key. Keep the idempotency key independent from future JWT rotations.
4. Use a protected, explicit DACL on the Secrets directory and payload. The approved principals are SYSTEM, local Administrators, and the dedicated Server service SID NT SERVICE\GameNet 5 Server. Do not grant access broadly to ordinary Users, Authenticated Users, remote users, or unrelated NetworkService processes.
5. The intended Server logon account is NT AUTHORITY\NETWORK SERVICE with the dedicated Server service SID enabled. Provisioning must not create the protected payload until service registration and SID resolution are available. The final installer must create/verify ACLs before secrets are written and preserve them during replacement.
6. Production loads these values only from the protected store. Missing, corrupt, unreadable, unsupported-version, path-mismatched, reparse-point, or ACL-invalid state must fail closed with a stable diagnostic that contains no secret values. Production must not fall back to environment variables, command-line arguments, appsettings.json, server.json, generated defaults, or a silently regenerated key.
7. An explicit, guarded development-only test seam may remain for disposable automated tests. It is not a production setup or service configuration mechanism.

### 2.2 TLS certificate and private key

The Server TLS private key is not part of the DPAPI application-secret payload. ADR-0001 remains authoritative:

- Store the non-exportable certificate/private key in the Windows LocalMachine certificate store.
- Keep only the listener endpoint and local certificate thumbprint in ordinary ProgramData configuration; never put a private key or PFX password there.
- Export/distribute only the public certificate. Clients must verify its SHA-256 fingerprint out of band and continue normal certificate, hostname/IP, and date validation.
- Grant private-key access to the intended Server service identity with least privilege.
- Certificate renewal, client trust transition, retirement, and recovery must follow a separate tested procedure aligned with ADR-0001. A successful certificate-generation or loopback test is not evidence of service access or LAN trust.

### 2.3 Agent identity and per-device credential

1. Each Agent has a stable device identity and its own credential. Never ship the Server's shared provisioning key in an Agent package or use it as every Agent's credential.
2. The intended Agent logon account is NT AUTHORITY\LOCAL SERVICE with the dedicated service SID NT SERVICE\GameNet 5 Agent enabled. This is the least-privilege baseline for the current outbound-communications worker. Any future local workstation-control capability requires a separate capability review; do not switch the network-facing Agent to LocalSystem or broad administrator rights to satisfy future needs.
3. Persist the Agent credential at the canonical ProgramData Agent state path, protected with DPAPI CurrentUser under the actual Agent service identity. Apply a protected DACL to the Agent state directory so that only SYSTEM, local Administrators, and the dedicated Agent service SID can modify/read the protected credential. Validate parent-directory and file ACLs; reject unexpected reparse points.
4. A credential that is missing, corrupt, unreadable, or cannot be decrypted must not silently create a new identity or enroll again. Recovery is an explicit authorized re-enrollment/re-provisioning operation.
5. Enrollment tokens are short-lived, single-use, bound to their intended device, and stored server-side only as a verifier/hash with expiry and consumption state. Redemption must be atomic and reject expiry, replay, revocation, duplicate races, and mismatched device identity.
6. The final production setup must deliver the one-time token through an explicit local provisioning channel whose access is restricted to SYSTEM, Administrators, and the Agent service SID (a reviewed local IPC/named-pipe boundary is the intended design). Do not rely on reusable credentials in command lines, plaintext files, SMB handoffs, or persistent environment variables. Any environment-variable seam retained for tests or migration must be guarded and unavailable to normal Production startup.
7. Provision the protected credential and its restrictive ACL before removing transient enrollment material. If persistence or verification fails, report a non-secret failure and preserve a documented recovery path; never report enrollment as successful until the Server confirms redemption and the credential is durably stored.

### 2.4 Rotation contract

Rotation is an explicit, auditable state transition. No key is considered rotatable merely because it is stored in a versioned file.

**PostgreSQL runtime credential**
1. Provision a replacement least-privilege runtime credential.
2. Persist the replacement in a protected new secret generation and activate it atomically.
3. Reconnect and verify database health/readiness with the replacement.
4. Revoke the old credential only after verification and an explicit, bounded recovery window.
5. Keep schema-deployment privileges separate from the normal Server runtime database role.

**JWT signing material**
- The current single-key implementation does not provide a completed safe rotation protocol.
- Before rotation is enabled, add key identifiers and active/previous verification support. Sign with the new key; accept the old key only during a documented bounded overlap no longer than the maximum supported token lifetime plus the allowed clock skew. Retire the old key after the overlap. Emergency revocation may invalidate outstanding tokens and must be an explicit audited incident operation.

**Agent provisioning key**
- Before rotation is enabled, implement explicit key generations and a bounded staged cutover (active/previous only for the documented transition window, or an equivalent reviewed protocol).
- Rotating the shared provisioning control key must not silently change the identities or stored per-device credentials of already-enrolled Agents.

**Per-device Agent credential**
- Issue a replacement through the authenticated provisioning protocol, store it under the Agent service identity, verify that it authenticates, and then revoke the old credential as an audited operation.
- Interrupted rotation must leave a known credential state. If state is ambiguous, fail closed and require controlled recovery; do not guess which credential to accept.

**Idempotency fingerprint key**
- Keep it independent from JWT signing material. Any rotation/migration must preserve verification of retained idempotency records for the declared retention window and must include compatibility and regression tests. Do not change the fingerprint root opportunistically during a JWT rotation.

**TLS certificate/private key**
- Follow ADR-0001 and its separate certificate-rotation procedure. Do not copy TLS private-key material into the application-secret store.

### 2.5 Recovery and host replacement

1. A same-machine service restart must reuse the current protected state without an interactive re-entry of secrets.
2. Missing, corrupt, unreadable, or invalid-ACL state must not trigger automatic key generation, credential substitution, deletion of ProgramData, database reset, or silent fallback.
3. DPAPI LocalMachine ciphertext is machine-bound and is not a cross-machine recovery format. Replacing the Server host requires an authenticated, explicit re-provisioning flow for database, signing, provisioning, and fingerprint secrets, using new generation identifiers and verifying dependent Agents can reconnect/reconcile. Do not promise recovery by copying the protected file to another host.
4. Application rollback is not secret rollback. Do not silently restore old secret generations that could re-enable retired or revoked credentials. Any emergency restoration must be an explicit, authorized, audited decision that accounts for revocation state.
5. Ordinary database backups, support bundles, installer logs, and diagnostics must never include plaintext secrets, credentials, enrollment tokens, or private keys. Recovery must retain customer, wallet, inventory, audit, and backup data even when secret recovery is blocked.

### 2.6 Logging and exposure prevention

- Never log secret values, full connection strings containing passwords, access/enrollment tokens, signing/provisioning material, private-key bytes, or serialized secret payloads.
- Redact relevant configuration, headers, command arguments, exceptions, traces, crash dumps/support bundles where under product control, and provisioning diagnostics.
- Use generic stable error messages for protected-state failures; log only non-secret identifiers/generation IDs and safe failure categories.
- Secret files and temporary replacement files must never contain plaintext. Replacement must use a restrictive ACL from file creation, same-volume atomic replacement where supported, read-after-write validation, and safe cleanup.

## 3. Implementation status at decision time

Reviewed against the current Integration tree on 2026-10-13:

- Present in source: the Server DPAPI LocalMachine protected store and ProgramData path; service-SID/ACL validation in the store; production rejection of secret values from ordinary configuration/environment/command-line sources (apart from a guarded Development test seam); dedicated secret-provider wiring; one-time enrollment and expiry/replay protections; Agent DPAPI CurrentUser credential persistence and protected Agent-state ACL validation code.
- Automated tests exist for protected Server settings/bootstrap and Agent enrollment/credential-related paths. Their presence does not prove a real Windows service token, effective ACLs, DPAPI profile behavior, private-key access, or restart/reconnect behavior in the installed deployment.
- Not yet accepted as complete: installer-driven creation of the actual services and their SIDs/ACLs; validation of the effective installed-Service identity; DPAPI and TLS private-key access after a full service restart; complete credential/key rotation flows; recovery/host-replacement runbooks and tests; comprehensive redaction tests and evidence against the release package.
- The Agent production provisioning channel must be completed and verified through the final Setup/helper path; any current environment-based enrollment seam is not accepted as the final production delivery design.

These are implementation gates, not conditions that keep the architecture decision itself Proposed.

## 4. Required work and acceptance gates

Continuous automated unit, integration, contract, security, and regression testing remains required while features are developed. The following gaps must be completed and tracked explicitly:

1. Add automated tests for secret payload format/version, missing/corrupt/unreadable state, ACL/path rejection, production fallback rejection, redaction, interrupted replacement, and safe cleanup with no plaintext residue.
2. Implement and test PostgreSQL credential rotation, JWT key-id/overlap/retirement/revocation, Agent provisioning-key staged rotation, per-device credential replacement/revocation, and idempotency-fingerprint-key compatibility. Tests must include interruption, overlap expiry, stale-key rejection, and safe recovery.
3. Implement and test documented recovery for missing/corrupt protected state and Server-host replacement. Prove that recovery does not erase or reset business data and cannot silently re-enable retired/revoked keys.
4. Before final release, use isolated Windows Server and Agent test PCs plus a disposable PostgreSQL database and the actual Setup.exe/MSI candidate to verify: service logon accounts and service SIDs; effective directory/file DACLs; Server DPAPI secret load; Agent DPAPI credential load; TLS private-key access; full service stop/start and machine-restart behavior; enrollment expiry/replay/revocation; reconnect/reconciliation; log redaction; install/repair/update/rollback and uninstall without business-data loss.
5. Record exact source SHA, package and payload hashes, Windows/PostgreSQL versions, service identities/SIDs, safe ACL evidence, outcomes, and linked regression runs for every gate. Do not expose secret values or production data in evidence.

The installed-Service, effective ACL, DPAPI, TLS private-key-after-restart, two-PC LAN, and interactive WPF checks are final acceptance gates under [Issue #34](https://github.com/alizebal73/5/issues/34), and must not be run on the live GameNet/Manager PC. The Setup/update/recovery delivery sequence remains governed by [Issue #16](https://github.com/alizebal73/5/issues/16). A Runtime ZIP, loopback request, unit test, or green Foundation run is not a substitute for these checks.

## 5. Consequences

- Product work may continue in Master Build Plan order when it does not depend on an unimplemented rotation/recovery guarantee; automated security and regression tests continue throughout.
- A feature that depends on a rotation, recovery, credential-provisioning, or revocation guarantee must not be exposed for operational use until its relevant implementation and acceptance tests pass.
- Setup and the updater must consume this single contract rather than add their own secret storage, recovery, or TLS mechanisms.
- Accepted means the architecture and required behavior are decided. It does not mean every requirement above is implemented, that a Windows Service has passed acceptance, or that GameNet is ready for production.
