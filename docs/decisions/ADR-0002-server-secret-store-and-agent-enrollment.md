# ADR-0002: Server Secret Store and Agent Enrollment Contract

- Status: Accepted — implementation contract; operational acceptance pending
- Date: 2026-10-11
- Scope: GameNet 5 Foundation and all descendants
- Related decision: ADR-0001 — Server TLS Certificate Lifecycle

## Context

The integrated Server needs durable secrets that are unavailable to ordinary application configuration, command-line arguments, logs and machine-level environment overrides in Production. Agent devices need a controlled one-time first-enrollment operation with expiry and replay protection and durable local credentials. Secret storage must remain separate from the TLS private-key lifecycle and from non-secret listener configuration.

Hosted Foundation tests prove code paths and database behavior but cannot by themselves prove effective ACLs and DPAPI behavior under an installed Windows service identity or physical two-PC LAN behavior.

## Decision

### 1. Canonical Server runtime-secret store

Production Server runtime uses the versioned DPAPI store at %ProgramData%/GameNet Manager/Secrets/server-secrets.v1.dpapi.

Schema version 1 contains a generation identifier, PostgreSQL connection string, JWT signing key and Agent provisioning key. It uses Windows DPAPI DataProtectionScope.LocalMachine with application-specific entropy GameNet5.ServerSecrets.v1. The code zeroes serialized plaintext and protected byte buffers where practical.

The canonical DACL is protected from inheritance and allows only LocalSystem, Builtin Administrators and NT SERVICE/GameNet 5 Server. The service identity has directory read/execute and file read rights, but no write, delete or permission-management rights. Loading rejects missing or invalid paths, reparse points, invalid sizes, unsupported payloads, unapproved ACL entries and DPAPI failures. Production startup fails closed.

Provisioning is a one-shot administrative operation: run the Server executable with --provision-secrets as its only argument on Windows after the named service exists and its Service SID resolves. The operator enters the PostgreSQL connection string without console echo; independent random signing and Agent provisioning keys are generated. Provisioning refuses to overwrite an existing canonical store and writes restricted metadata-only audit records. If the outcome is uncertain, stop and inspect; do not rerun blindly.

### 2. Protected setup settings are separate

%ProgramData%/GameNet Manager/Config/server-secrets.bin is a separate DPAPI LocalMachine-protected setup/configuration file validated by ProtectedServerSettings. It can contain non-secret authentication settings, the TLS certificate thumbprint and the temporary GameNet:Setup:BootstrapSecret during initial Owner setup.

This setup file is not the authoritative runtime source for the PostgreSQL connection string, JWT signing key or Agent provisioning key. ProtectedServerSettings.LoadInto filters legacy copies of those secret fields, while ServerSecretBootstrap loads the canonical Secrets/server-secrets.v1.dpapi store. Production requires the canonical setup path and restrictive ACLs; arbitrary protected-file path overrides are rejected.

TLS certificate/private-key storage and trust continue to follow ADR-0001. This ADR does not introduce a PFX/password TLS lifecycle; the private key is not exported.

The initial Owner bootstrap secret is temporary setup state. After the Server reports bootstrap completion, scripts/finalize-server-setup.ps1 removes only the bootstrap setting. It creates a temporary DPAPI payload under restrictive ACLs, verifies decryption/content before atomic replacement, and validates the resulting file. It does not create a backup retaining the old bootstrap secret. If a precondition or verification fails, stop and inspect rather than editing the encrypted file manually.

### 3. Secret source and startup rules

In Production, the three canonical runtime secrets are loaded only from the DPAPI store. Values in ordinary application configuration, environment or command-line are rejected. The explicit Development-only test seam GAMENET_ALLOW_UNPROTECTED_TEST_SECRETS=true is not a deployment mode. The initial Owner bootstrap secret is also rejected from environment and command-line arguments.

The store is Windows-only. Missing, corrupt or ACL-invalid state is an operational failure and must never cause automatic key generation or silent re-provisioning on normal Server startup. Production startup does not apply database migrations; schema changes are explicit operations against an approved or disposable database.

### 4. Agent enrollment and credential storage

- Issue, recovery and revoke operations require an authenticated Operator with agents.enrollment.manage permission. Responses use Cache-Control: no-store. Only redemption is anonymous because possession of the one-time token is its authorization proof.
- Enrollment token material is generated from 32 cryptographically random bytes and encoded as a 43-character base64url string. The Server sets a 15-minute expiry and stores only its SHA-256 hash. Redemption checks DeviceId, hash, expiry, revoked/redeemed state and existing credential state inside the transactional service.
- Redemption marks the token redeemed and creates the durable Agent credential. The Server stores a hash of that credential; clear credential material is returned only in the redemption response. The Agent stores its credential in credential.bin protected with DPAPI DataProtectionScope.CurrentUser, so its execution identity must be verified across restart.
- Production first start reads enrollment-token.dpapi only from the canonical Agent state root. The token file uses DPAPI LocalMachine with entropy bound to DeviceId and restrictive exact DACLs. Production environment-variable enrollment is disabled. The provisioning helper requires an elevated operator, an already registered stopped Agent service running as LocalService with its Service SID enabled, and normally validated HTTPS.
- Issue, redeem, recovery and revoke operations emit audit events. Recovery is narrow: enrollment history for the exact DeviceId must exist, a previously authenticated credential is not eligible for this recovery path, and a reason is required.

### 5. Logging and diagnostics

Secret values, clear enrollment tokens, credentials and database connection strings must not appear in logs, provisioning audit records or CI artifacts. Diagnostic records may include operation IDs, outcome, actor identity and bounded failure categories only. scripts/inspect-manager-runtime-state.ps1 is read-only: it does not decrypt/display protected payloads or mutate services, certificates, ACLs, listeners or ProgramData state. Its report must be reviewed before provisioning or cleanup on an existing Manager PC.

## Alternatives rejected

- Runtime secrets in ordinary configuration, command-line arguments or machine-level environment in Production.
- Treating Config/server-secrets.bin as a second authoritative runtime-secret provider; legacy secret fields are filtered out.
- Automatic creation, overwrite or key regeneration after a secret-store load failure.
- Persisting only an enrollment-token hash on the Agent; first start needs the clear short-lived token, therefore it is locally DPAPI-protected and bound to DeviceId, expiry and DACL while the Server persists only a hash.
- Retaining or logging clear enrollment tokens/credentials after use.
- A second PFX/password TLS design; ADR-0001 governs the private-key lifecycle.

## Consequences and required operational acceptance

This ADR accepts the implementation/security contract only. It does not certify Production or close physical-service gates. Before Stage 1 can be declared operationally complete, record evidence from isolated real Windows test machines, disposable PostgreSQL and throwaway credentials:

1. Register the intended Server service; verify its actual Service SID and effective DACLs for both protected files/directories and the TLS private-key file. Confirm unapproved principals cannot read/write protected state.
2. Provision once interactively, restart the installed Server service and prove DPAPI decryption and HTTPS readiness under the real service identity. Missing, corrupt or ACL-invalid state must fail closed without replacing keys.
3. Prove the TLS private key opens under that service identity after restart without exporting the key or disabling normal TLS validation.
4. Complete first-Owner bootstrap and finalization; prove the bootstrap secret is absent from the replacement DPAPI file, the replacement passes validation, and readiness succeeds after restart.
5. Install Agent as LocalService with the intended Service SID and restricted DACL. Provision an enrollment token over validated HTTPS; test first redemption, expiry/replay/revocation rejection, credential persistence under the actual service identity, and reconnect after Agent/Server restart.
6. Document credential revoke/replacement, Server secret rotation and disaster recovery as separate procedures. Automatic rotation of the three canonical Server secrets is not implemented; future rotation needs compatibility, invalidation, audit and recovery tests.
7. Bind every CI claim to its exact SHA. Hosted Full Foundation is required/repeatable but does not substitute for installed service/ACL/DPAPI/TLS/LAN evidence.

Until these gates pass, report the code and automated tests as implemented/CI-verified, live service acceptance as unverified, and Setup/upgrade/repair/uninstall/update/rollback/release certification as open.
