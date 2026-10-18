# ADR-0001: Server TLS Certificate Lifecycle

- Status: Accepted
- Date: 2026-10-09
- Scope: GameNet 5 Foundation and all descendants

## Context

Candidate branches implemented two certificate lifecycles: (1) a non-exportable certificate held in the Windows LocalMachine certificate store, selected by thumbprint, and (2) an exportable PFX whose path and password are held in DPAPI-protected settings. Porting both unchanged would create competing sources of certificate identity, configuration, permissions, and rotation behavior. Green loopback runtime checks cannot decide between these deployment designs.

## Decision

The canonical Server TLS lifecycle is the Windows certificate-store design.

1. Generate the Server certificate directly into Cert:\LocalMachine\My with a non-exportable private key. Never export or package the private key.
2. Validate SAN entries for the configured Server IPv4 address and DNS name, TLS Server Authentication EKU, private-key presence, and certificate validity before use.
3. Grant the actual Server Windows service identity read access to the private-key file. Do not silently assume a service account.
4. Export only the public .cer. Place only that public certificate, without a private key, in the local machine Root store when needed for the self-signed local setup.
5. The Server's 40-character Windows store thumbprint is solely a local certificate lookup identifier. Client trust must independently verify the SHA-256 hash of the exact exported .cer bytes, then import that public certificate. Never disable normal TLS chain, date, or hostname/IP validation.
6. Store the listener URL and local certificate thumbprint in %ProgramData%\GameNet Manager\Config\server.json. This is operational configuration, not a location for secrets. Remote endpoints use HTTPS; HTTP remains loopback-only for development/certification.
7. DPAPI-protected application secrets (database credentials, signing/provisioning material, bootstrap data) remain a separate concern. If ProtectedServerSettings is selectively ported from another branch, remove its PFX path/password responsibilities and preserve a single certificate lifecycle.

## Alternatives rejected

- PFX plus password in protected settings as a second TLS implementation: rejected as the canonical certificate lifecycle because it introduces exportable private-key material and a second configuration/permission/rotation path. The existing PFX helper and writer from feature/operator-identity-v1 must not be merged as-is. Non-TLS secrets from that branch can be reviewed and ported separately.
- Trusting certificates by an unverified thumbprint copied between PCs: rejected. The out-of-band integrity check for the public file is SHA-256.
- TLS validation bypasses or automatic HTTP fallback on LAN: rejected.

## Consequences and required gates

- The initial configuration script refuses to overwrite an existing server.json or public certificate. Certificate rotation is not implemented here and must be a separate, tested procedure.
- Automated validation must cover certificate profile, non-exportable private key, public-only export, SHA-256 matching, and no-overwrite/rollback behavior.
- Full release validation must additionally prove ProgramData configuration loading and normal TLS validation from a separate Desktop PC and Agent PC, including Agent provisioning, lease/heartbeat/reconnect, and service restart.
- Foundation CI and endpoint health are necessary but do not substitute for LAN proof. Setup/updater/rollback remain independent release gates. Keep the PR Draft until those gates are met.
