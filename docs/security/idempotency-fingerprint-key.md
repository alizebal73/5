# Idempotency request fingerprint key

## Contract

Sensitive operator-management mutations use an HMAC-SHA-256 fingerprint of the serialized request payload. The HMAC root is a protected GameNet Server secret with a dedicated purpose; it is not the JWT signing key. The root is generated independently during Server secret-store provisioning and is stored in the DPAPI-protected Server secret payload.

The root is stable across JWT signing-key rotation, so a retained idempotency record can still recognize an exact retry during its retention window. The key must never be logged, returned to clients, or sourced from ordinary application configuration, command-line arguments, or production environment variables.

## Compatibility with pre-release secret stores

Older secret payloads do not contain the dedicated fingerprint key. The loader retains their previous JWT-derived fingerprint root so it does not silently change fingerprints for requests already recorded. This compatibility path is not rotation-independent: **do not rotate the JWT signing key on a legacy store until it has been safely re-provisioned with a dedicated fingerprint key and active idempotency records have expired or been migrated**.

Changing the dedicated fingerprint key also changes fingerprints. Until versioned key-ring support exists, do not rotate that key while idempotency records are within their retention period (currently 24 hours). Key rotation must preserve or deliberately retire the prior replay window.

## Scope

This contract covers fingerprint generation for operator-management mutations. The current Station mutations use SHA-256 over non-secret station fields; their hash format is intentionally unchanged here to preserve their existing idempotency replay behavior. Any new financial mutation must use the protected IRequestFingerprint contract and include retry/concurrency tests before money-moving behavior is enabled.

## Migrating a legacy protected Server store

For an existing pre-release installation, the migration command adds the new field without changing the effective fingerprint root. It intentionally copies the existing JWT signing-key value into the dedicated fingerprint-key field once; this is required because stored rows contain a hash, not the original request payload, so a new random key cannot reproduce retained fingerprints.

1. Back up the Server secret-store file and confirm that the backup is in a restricted location.
2. On the manager Server, run an elevated PowerShell/terminal with the Server installation directory as the working directory:
   `GameNet.Server.exe --migrate-idempotency-fingerprint-key`
3. The command validates the existing DPAPI store and ACLs, writes a new encrypted payload to a restricted temporary file, atomically replaces the original while retaining a restricted encrypted backup, and verifies the result by reloading it. It does not connect to PostgreSQL or change operator/session data.
4. Check the restricted audit records under `%ProgramData%\GameNet Manager\Audit`. Do not delete the encrypted pre-migration backup until the replacement store has been verified and the normal backup/restore procedure has been exercised.
5. After migration, manage the dedicated fingerprint field independently. A future JWT signing-key change must not modify the fingerprint key. Do not randomize the fingerprint key during active idempotency retention; the retention window for current operator mutations is 24 hours.

This command is deliberately separate from secret provisioning and is idempotent: when the dedicated field already exists and is valid, it reports that no change was made. It is not a JWT rotation command and does not close the broader operational key-rotation lifecycle by itself.
