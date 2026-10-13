# Idempotency request fingerprint key

## Contract

Sensitive operator-management mutations use an HMAC-SHA-256 fingerprint of the serialized request payload. The HMAC root is a protected GameNet Server secret with a dedicated purpose; it is not the JWT signing key. The root is generated independently during Server secret-store provisioning and is stored in the DPAPI-protected Server secret payload.

The root is stable across JWT signing-key rotation, so a retained idempotency record can still recognize an exact retry during its retention window. The key must never be logged, returned to clients, or sourced from ordinary application configuration, command-line arguments, or production environment variables.

## Compatibility with pre-release secret stores

Older secret payloads do not contain the dedicated fingerprint key. The loader retains their previous JWT-derived fingerprint root so it does not silently change fingerprints for requests already recorded. This compatibility path is not rotation-independent: **do not rotate the JWT signing key on a legacy store until it has been safely re-provisioned with a dedicated fingerprint key and active idempotency records have expired or been migrated**.

Changing the dedicated fingerprint key also changes fingerprints. Until versioned key-ring support exists, do not rotate that key while idempotency records are within their retention period (currently 24 hours). Key rotation must preserve or deliberately retire the prior replay window.

## Scope

This contract covers fingerprint generation for operator-management mutations. The current Station mutations use SHA-256 over non-secret station fields; their hash format is intentionally unchanged here to preserve their existing idempotency replay behavior. Any new financial mutation must use the protected IRequestFingerprint contract and include retry/concurrency tests before money-moving behavior is enabled.
