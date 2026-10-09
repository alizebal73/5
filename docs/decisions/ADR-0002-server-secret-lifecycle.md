# ADR-0002: Server and Agent Secret Lifecycle

- Status: Proposed for review; not yet accepted or implemented
- Date: 2026-10-10
- Scope: Foundation runtime, Windows service deployment, and the later Setup/Updater implementation
- Depends on: ADR-0001 Server TLS Certificate Lifecycle

## 1. Context and observed implementation

The current candidate has three different secret boundaries that must not be conflated:

1. Server deployment secrets: PostgreSQL connection credentials, the JWT signing key, and the Agent provisioning key.
2. Per-device Agent credential: issued by the Server and persisted by the Agent.
3. Server TLS private key: managed through the Windows certificate store under ADR-0001, not copied into the application secret store.

The current code shows why a single deployment contract is needed:

- Server startup currently accepts the database connection through the GAMENET_DATABASE_CONNECTION environment variable, while GameNetOptions also has configuration properties for the database connection, JWT signing key and Agent provisioning key.
- AgentCredentialStore currently protects credential.bin with DPAPI CurrentUser. The Agent state is under %ProgramData%\GameNet Manager\Agent, but the store itself does not establish a restrictive Windows ACL.
- Runtime endpoint JSON is defined for endpoint/certificate configuration. It must not become a place for credentials or key material.
- The existing LAN smoke deliberately supplied disposable secret values through process environment variables. That demonstrated a temporary test path; it is not an approved production secret-storage design.

No Setup project currently exists. This ADR defines the contract that runtime implementation and future Setup must share; it does not claim that the protected store, ACLs, rotation flow, or installer exists today.

## 2. Security objectives

- Secrets are unavailable to ordinary local users and unrelated services.
- The running Server can obtain only the secrets it needs under its actual Windows service token.
- Agent device credentials remain decryptable by the same Agent service identity after restart, without storing their plaintext.
- Missing, corrupt, unreadable or unsupported secret state fails closed; the application must not silently fall back to an unprotected value.
- Rotation and recovery are explicit, observable operations with tests.
- Logs, traces, crash diagnostics, installer command lines, repository files and ordinary configuration JSON never contain secret values.
- Secret recovery must not silently restore revoked credentials or re-enable a retired key.

## 3. Proposed canonical design

### 3.1 Server deployment-secret store

Store Server-only secret material as a versioned, binary DPAPI-protected payload at:

%ProgramData%\GameNet Manager\Secrets\server-secrets.v1.dpapi

Protect the payload using Windows DPAPI with DataProtectionScope.LocalMachine and an application-purpose entropy label, such as GameNet5.ServerSecrets.v1. The entropy label is context separation, not an independent encryption key. Do not implement custom cryptography or store a wrapping password alongside the payload.

The versioned payload contains named values for:

- PostgreSQL runtime connection string, including its credential where applicable.
- JWT signing-key material.
- Agent provisioning-key material.
- A non-secret format/schema version and secret-generation identifiers.

Do not put these values in server.json, appsettings.json, a database table, source control, a regular backup, or plaintext temporary files. server.json remains for non-secret operational endpoint/certificate configuration as specified by ADR-0001.

### 3.2 Windows service identity and file permissions

Proposed Server logon identity: NT AUTHORITY\NETWORK SERVICE, consistent with the identity used by the current TLS helper instructions. The production service must also have the dedicated service SID NT SERVICE\GameNet 5 Server enabled.

The Server secret directory and protected payload must have a deliberate, non-broad DACL. Access is limited to SYSTEM, local Administrators, and the dedicated GameNet Server service SID; ordinary Users, Authenticated Users, remote users and unrelated service identities receive no read/write access. The installer/configuration path may write the payload only through an elevated, audited provisioning operation.

DPAPI LocalMachine protection does not replace ACLs: the DACL is a required part of the boundary. Create the restrictive directory/file ACL before writing secret material, preserve it on replacement, and use a same-volume atomic replacement pattern. Do not leave a plaintext temporary file when an operation fails.

Enablement of the Server service SID and access to the TLS private key must be proven on Windows under the real service token. The current TLS helper grants access using a service-account argument; whether that helper should be changed to grant the dedicated service SID must be verified in implementation rather than assumed. Do not run a service test until the certificate ACL and protected-store ACL agree with the configured service identity.

### 3.3 Agent device credential

Keep the current per-device DPAPI CurrentUser protection model unless a reviewed implementation change demonstrates a better compatible model. The identity used to provision, decrypt and rotate the credential must be the same Agent service identity. A different service account must not be able to decrypt the stored credential.

Proposed baseline Agent logon identity: NT AUTHORITY\LOCAL SERVICE, with the dedicated service SID NT SERVICE\GameNet 5 Agent enabled. This is a least-privilege starting point, not yet an accepted product decision: before acceptance, review the Agent's actual local station-control requirements and prove that they work with this identity. Any additional privilege must have a specific capability justification and a bounded permission; LocalSystem must not be selected merely as a convenience.

Apply a deliberate DACL to %ProgramData%\GameNet Manager\Agent so that only SYSTEM, local Administrators and the dedicated Agent service SID can access its state. Include identity state in the same access review to prevent an ordinary user from replacing or cloning the local device identity.

The Agent bootstrap input currently read from an environment variable is a transitional certification seam only. Production enrollment must not pass a reusable credential through an ordinary command line, SMB handoff file, plaintext temp file or persistent machine/user environment variable. A one-time local provisioning channel must deliver the issued credential to the Agent service in memory; the exact authenticated operator/setup flow must be designed and tested before service-based enrollment is considered complete. Persist the credential only after DPAPI protection and restrictive ACLs are in place, then remove transient bootstrap material on success and failure.

### 3.4 Secret generation and purpose

- PostgreSQL: provision a dedicated runtime role with only the permissions the running Server needs. Keep schema-deployment/migration credentials separate from the normal Server runtime identity and secret set.
- JWT: generate at least 256 bits of cryptographically random key material; do not use human-created passwords or repository defaults.
- Agent provisioning: generate at least 256 bits of cryptographically random material. This shared provisioning control key is not an Agent device credential; every Agent retains its own per-device credential.
- TLS: follow ADR-0001 only. The TLS private key remains non-exportable in LocalMachine\My and is never placed in this secret store.
- Development and isolated certification may use disposable test-only inputs through an explicit, guarded mechanism. Environment variables are not the production storage contract. The current environment-based smoke path must be removed or blocked for a production service before production acceptance.

## 4. Loading and failure behavior

Introduce a typed secret-provider boundary so components receive the specific secret they need without treating secret values as ordinary application configuration. Do not log configuration snapshots containing secrets.

For a production Server:

- The protected secret store is the only supported source for the listed Server deployment secrets.
- Missing, unreadable, corrupt, unsupported-version or ACL-invalid secret state fails startup with a stable, non-secret diagnostic.
- There is no silent fallback to environment variables, server.json, appsettings.json or a generated replacement key.
- Readiness remains NotReady until required secrets and database connectivity have been verified.
- Secret values, provisioning headers, Agent credentials, access tokens and connection-string passwords are redacted from logs, traces, metrics, exception details and diagnostic bundles.

The existing public environment-variable configuration provider may continue to carry explicitly non-secret runtime overrides. Secret-name overrides must be separately guarded and unavailable to ordinary Production service startup.

## 5. Rotation contract

Rotation must be explicit, auditable and recoverable. Do not claim rotation is supported until the corresponding implementation and regression tests exist.

### PostgreSQL runtime credential

1. Provision a replacement least-privilege database credential.
2. Protect the new connection data and atomically activate the new secret generation.
3. Reconnect and verify health/readiness using the new credential.
4. Revoke the old credential only after successful verification and an explicit recovery window.
5. Never silently run schema migrations with the normal runtime credential.

### JWT signing material

The current implementation uses a single configured signing key; that is not sufficient for a safe non-disruptive rotation. Before rotation is enabled, implement a key identifier and active/previous verification support. Begin signing with the new key, continue verifying the previous key only for a bounded overlap no longer than the maximum supported token lifetime plus the documented clock-skew allowance, then retire it. Emergency revocation is allowed to invalidate outstanding Agent access tokens and must be an explicit incident action.

### Agent provisioning key

The current implementation has one configured provisioning key. Before rotation is enabled, implement a bounded active/previous key window or an equivalent staged cutover, with an explicit key generation and tests for old/new behavior. Rotating this control key must not silently change the identity or stored credential of every already-enrolled Agent.

### Per-device Agent credential

Issue replacement material through the authenticated provisioning channel, store it under the Agent service identity, verify that the replacement authenticates, and revoke the old credential as a recorded operation. The plaintext credential is returned only through the one-time provisioning response and is never included in audit/log fields. Interrupted rotation must leave either the previously valid credential or the verified new credential usable; ambiguous state must fail closed and require controlled recovery.

## 6. Recovery and backup

- Ordinary database backups, application backups and support bundles must not contain plaintext deployment secrets or Agent credentials.
- Do not automatically generate replacement keys when the secret file is missing or corrupt.
- A same-machine service restart must be able to decrypt and use the current protected state without re-entering secrets.
- A corrupt file, invalid ACL or unsupported format produces a stable failure and a documented administrative re-provision path.
- DPAPI LocalMachine ciphertext is machine-bound. Do not promise that copying the protected file to a replacement Server can recover it. Host replacement must use an authenticated re-provision operation for database credentials, JWT signing material and provisioning material, then verify Agent reconnect/reconciliation. Treat this as a new secret generation and record the change.
- Do not silently restore an old secret payload as rollback: that could reactivate retired keys. Application rollback and secret rollback are separate decisions.
- Recovery must retain business data and audit history; failure to decrypt secrets is not permission to reset the database or destroy ProgramData.

## 7. Required verification before service-runtime acceptance

All tests run with disposable credentials and isolated test state. At minimum:

1. Store format/version validation; missing, malformed, truncated and corrupt payloads fail closed.
2. DPAPI protection/unprotection on the same approved machine and service identity across process and Windows service restart.
3. A different ordinary local identity and an unrelated service identity cannot read the file or decrypt Agent state.
4. Directory/file DACL tests prove that only the documented principals have access; temporary-file and atomic-replacement failure tests prove no plaintext residue or permissive replacement.
5. Production rejects secret environment-variable fallback and never writes secret fields into JSON.
6. Redaction tests prove that known test secret values do not appear in captured logs, traces or error output.
7. Rotation tests cover successful cutover, old-key rejection after the overlap, process interruption, partial write and recovery.
8. Recovery tests cover missing/corrupt secret state and administrative re-provision without modifying business data.
9. Agent credential persistence is tested under its actual configured Windows service identity, including restart and credential rotation.
10. Server TLS private-key ACL access is tested under the actual Server service identity; do not infer this from an interactive-process test.
11. Exact tested commit, OS version, service logon identities, service SIDs, file ACL evidence, package hash and sanitized outcomes are recorded.

A successful unit test or interactive process does not satisfy the Windows service identity gate.

## 8. Implementation sequence and acceptance gates

1. Review and accept this ADR, including the final Agent service identity and its local capability map.
2. Implement the protected Server secret store and restrictive ACL creation; add typed secret-provider wiring and production fail-closed behavior.
3. Replace production use of environment-provided database/signing/provisioning secrets. Preserve only a test seam that is explicit and guarded.
4. Define and implement the one-time Agent enrollment channel; preserve DPAPI CurrentUser unless a separately reviewed change supersedes it.
5. Implement rotation, recovery and redaction behavior with the tests in Section 7 before Setup consumes the contract.
6. Run physical-LAN Windows service certification with the actual service identities and a disposable database.
7. Only once the Foundation runtime boundary is accepted, continue the master-plan order: Stage 2 module skeleton, Stage 3 identity/authorization, Stage 4 stations/agents, then Stage 5 customers.
8. Setup, update, repair, rollback and release certification remain Stages 15–18 / Gate E; they must consume this contract rather than invent a second one.

## 9. Decisions still requiring explicit review

This ADR intentionally remains Proposed until these are resolved and verified:

- Confirm that the Server service uses NetworkService with its dedicated service SID enabled, and align the TLS private-key ACL with that actual token.
- Review Agent station-control capabilities and either approve LocalService plus its service SID or record a justified, least-privilege alternative before testing.
- Select and threat-model the one-time local Agent enrollment channel; do not use the previous SMB temporary-file handoff for production.
- Confirm the protected-store ACL, DPAPI behavior, rotation/recovery semantics and test acceptance criteria against the Windows integration test design.

References:
- ADR-0001: docs/decisions/ADR-0001-server-tls-certificate-lifecycle.md
- Runtime endpoint/TLS contract: docs/operations/runtime-endpoints.md
- Runtime LAN evidence and limitations: docs/operations/runtime-lan-smoke-evidence-2026-10-10.md
- Release gate and required sequence: Issue #16
