# GameNet 5 Installability Integration Audit — 2026-10-09

## Current verified state — 2026-10-09, SHA `553dc80f6f553b0e793b7cbb2b82ba9136b116c0`

- Production Server HTTPS listener configuration and certificate material generation were implemented and CI-certified at parent SHA `7ab5108b342c74d88faf87ba6303a1c08ac9de9b`; this means the packaged Server knows how to bind HTTPS when provisioned with protected settings. It does **not** establish that Desktop/Agent have trusted the certificate or that LAN traffic has been exercised.
- The shared root-origin policy for Desktop and Agent passed the exact-SHA Quick and Foundation gates linked in the roadmap. The policy allows remote HTTPS only, permits HTTP only for loopback development, and rejects URL credentials, query, fragment and path prefixes.
- **Current next boundary:** client-side trust installation after out-of-band SHA-256 fingerprint verification, ProgramData runtime endpoint configuration for Desktop/Agent, then secure bootstrap/enrollment lifecycle and real LAN integration. Setup.exe/MSI, Windows service commissioning and two-PC validation are not complete.

**Status:** Audit complete; implementation gates remain open. This document is not a claim that Setup or two-PC installation is complete.

**Reviewed checkpoint:** `feature/operator-identity-v1`, repository head `8a89a6863bc83bd2c6fd4329e0484f1e8ca72e78`. The latest application/payload implementation under review was certified at code SHA `c8a20caabad2c963c6a4b1cbcac7e3cbcb3575f7` and payload-builder SHA `b7e232d068fb6c085716980249a83abb205961b3`; later commits in this branch were documentation-only. The existing evidence proves Foundation/runtime tests and ZIP payload integrity, not installation on clean physical PCs.

## Verified integration findings

### 1. Production listener and TLS are not yet deployment-configured

- `src/Server/Program.cs` configures Windows Service hosting but does not declare a LAN HTTPS listener in code.
- `scripts/publish-deployment-payloads.ps1` generates `appsettings.Production.json` with only `GameNet:ProtectedSettings:Enabled=true`; it does not generate an HTTPS endpoint or a server certificate configuration.
- Microsoft’s [Kestrel documentation for ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0) says production HTTPS must be explicitly configured and a default certificate supplied.

**Risk:** a self-contained Server package cannot be assumed to accept secure LAN traffic merely because the process builds or localhost runtime tests pass.

**Required gate:** define and test the Kestrel listener, certificate selection, SAN/hostname or stable-IP match, Windows trust distribution, firewall/port behavior, and startup failure when the certificate is absent/invalid. Never disable certificate validation to make Agent pairing work.

### 2. Desktop and Agent default to loopback URLs

- `src/Desktop/appsettings.json` defaults `GameNet:Server:BaseUrl` to `http://127.0.0.1:5080`.
- `src/Client/appsettings.json` defaults `GameNet:AgentTransport:ServerBaseUrl` to `http://127.0.0.1:5080`.

These are local-development defaults, not correct values for an Agent on another computer. The installation/runtime configuration must set the real Server HTTPS origin for each component without editing signed or immutable binaries, and must verify that the URL host matches the certificate SAN. Do not treat a successful loopback test as LAN proof.

### 3. Sensitive remote routes already require HTTPS

`src/Server/Infrastructure/Security/Transport/SecureTransportMiddleware.cs` rejects HTTP for `/api/v1/auth`, `/api/v1/bootstrap/admin`, `/api/v1/agent` and `/hubs/agent` unless the request is loopback. This is a deliberate security boundary and must remain intact. A real remote Desktop login and Agent connection need a working HTTPS endpoint; no HTTP fallback should be added.

### 4. Server bootstrap secret source and lifecycle need alignment

- `scripts/write-protected-server-settings.ps1` writes `GameNet:Setup:BootstrapSecret` into a DPAPI LocalMachine-protected file.
- `src/Server/Program.cs` loads that protected file before considering the `GAMENET_BOOTSTRAP_SECRET` environment fallback. In the normal protected-settings deployment, a machine-wide environment variable is not required.
- `scripts/bootstrap-admin.ps1` previously described the supplied value as an environment variable. Its prompt/success message is corrected in this audit commit to reflect the protected file source.
- The one-time secret still remains in the DPAPI file after the first owner is created. The bootstrap endpoint checks whether bootstrap is still required, but retaining the secret is still an unnecessary secret-lifecycle gap.

**Required gate:** add a tested, local post-bootstrap operation that removes only `GameNet:Setup:BootstrapSecret`, re-encrypts the remaining settings atomically, preserves ACLs, and verifies the Server still starts/readiness passes. Do not delete or rewrite the entire settings file as a shortcut.

### 5. Agent enrollment is not install-ready yet

- Agent runtime config names `GAMENET_AGENT_BOOTSTRAP_SECRET` as the initial one-time credential source.
- `src/Client/Identity/AgentCredentialStore.cs` stores the resulting credential at the Agent ProgramData root using DPAPI CurrentUser and clears the environment variable only from that process after use.
- Agent deployment therefore needs an explicit service identity, ACL-protected ProgramData, a unique one-time bootstrap credential for each Agent, a secure way to place that credential into the intended service process, and cleanup of the temporary provisioned value after enrollment.

**Required gate:** do not put a shared provisioning key or per-site bootstrap secret into the Agent ZIP. Test two different Agent identities, persistence across service restart, credential isolation, and rejection of a copied/duplicate identity. Keep the service account consistent with the DPAPI scope unless the storage contract is deliberately changed and tested.

### 6. PostgreSQL installation remains an explicit prerequisite

The current protected-settings writer accepts a PostgreSQL host, database, user and password; it does not install PostgreSQL or create/secure the database role. The current migration command is explicit (`GameNet.Server.exe --migrate-only`) and routine Server startup must remain non-migrating.

For the first two-PC installability slice, either document and validate an already-installed supported PostgreSQL instance or separately engineer and certify a PostgreSQL provisioning path. Do not silently assume a database exists.

## Required execution order

1. Specify and test the HTTPS listener/certificate/trust contract end to end. The server and client must agree on a stable Server HTTPS origin.
2. Make Server/Desktop/Agent runtime settings deployment-owned and stored outside immutable binaries; validate URLs, TLS readiness and missing/invalid settings at startup.
3. Implement one-time bootstrap-secret removal from protected Server settings and service-scoped Agent enrollment secret lifecycle.
4. Implement idempotent Server+Desktop installation and separate Agent installation, including service registration, ACLs, checked exit codes, health/readiness and diagnostic preservation.
5. Install cleanly on two real Windows PCs and test migration, owner bootstrap, unique Agent enrollment, heartbeat, reconnect/restart, and uninstall data preservation. Record OS/PostgreSQL versions, source SHA, package checksum and evidence.
6. Only after these gates pass, resume the next small PC-control capability. Customer/billing modules remain blocked until the cross-PC Foundation slice is proven.

## Non-negotiable rules

- No HTTP bypass, no certificate-validation bypass, no production mocks, and no shared Agent secret baked into packages.
- Do not run destructive migration/installation tests against the live GameNet database.
- The current CI results remain payload/Foun­dation evidence only. No `Setup.exe`/MSI has been certified; no physical two-PC installation has been recorded.


## Exact-SHA update — Agent URL validation

On SHA `b95eab6c68e89b4d8f4500f714f1445ffd9ca4c4`, `AgentTransportOptions.Validate()` was aligned with Desktop and Server policy: remote HTTPS is accepted, remote HTTP is rejected, and loopback HTTP remains available only for local development. Regression tests and [Quick Validation #127](https://github.com/alizebal73/5/actions/runs/37959248404) passed; [Full Foundation #195](https://github.com/alizebal73/5/actions/runs/37959248381) also passed, including PostgreSQL, backup/restore, Desktop Runtime, Agent Runtime and evidence upload.

This closes the insecure-remote-HTTP configuration inconsistency only. It does not close the TLS listener, server certificate/trust, external runtime configuration, unique Agent enrollment, protected bootstrap-secret cleanup, or two-physical-PC installation gates.

## Exact-SHA update — Production HTTPS endpoint

Production configuration and certificate creation are now implemented and automated-tested on `7ab5108b342c74d88faf87ba6303a1c08ac9de9b`. Kestrel listens at `https://0.0.0.0:5081`; the PFX password and absolute certificate path are loaded only from the DPAPI-protected settings file. The interactive settings writer exports a public `server-trust.cer` and reports a SHA-256 fingerprint; it does not place the private PFX in client packages.

Evidence: [Quick Validation #128](https://github.com/alizebal73/5/actions/runs/37961799437), [Full Foundation #196](https://github.com/alizebal73/5/actions/runs/37961799463), payload artifact [#11632326010](https://api.github.com/repos/alizebal73/5/actions/artifacts/11632326010). The canonical test invokes the Windows certificate profile/create/export/reopen/fingerprint/no-overwrite verification. The payload build produced and uploaded successfully.

Still open: distribute/configure client trust only after out-of-band fingerprint confirmation; load shared, non-secret runtime endpoint settings from a protected ProgramData file in Desktop and Agent; test Production HTTPS health/login/Agent enrollment across LAN; remove the one-time bootstrap secret after owner creation; create service installers and perform the real two-PC validation. Do not mark installability complete until those gates pass together.
