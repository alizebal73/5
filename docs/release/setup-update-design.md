# Setup and Update Design

## Deployment components

GameNet ships as:

- Server
- Desktop
- Agent
- Updater
- Setup/Bootstrapper

The initial installer supports a combined Server + Desktop install for the single-site deployment.

## Windows locations

Application binaries:

- `C:\Program Files\GameNet Manager\Server`
- `C:\Program Files\GameNet Manager\Desktop`
- `C:\Program Files\GameNet Manager\Agent`
- `C:\Program Files\GameNet Manager\Updater`

Mutable runtime data:

- `C:\ProgramData\GameNet Manager\Config`
- `C:\ProgramData\GameNet Manager\Logs`
- `C:\ProgramData\GameNet Manager\State`
- `C:\ProgramData\GameNet Manager\Backups`
- `C:\ProgramData\GameNet Manager\Updates`

## Installation sequence

1. Detect OS/runtime prerequisites.
2. Detect an existing installation.
3. Detect current product/schema versions.
4. Validate installation mode.
5. Create required ProgramData directories.
6. Install immutable application payload.
7. Register Windows Services.
8. Apply initial configuration.
9. Validate PostgreSQL connectivity where relevant.
10. Run explicit schema deployment/migration step.
11. Start services in dependency order.
12. Run health/readiness checks.
13. Create shortcuts and operator entry points.
14. Record installed release metadata.

## Repair

Repair restores application binaries/config structure without silently destroying:

- customer data;
- financial ledgers;
- inventory ledgers;
- audit;
- backups.

Repair must be idempotent and end with the same verification checks as install.

## Uninstall

Uninstall removes application components and services.

It must clearly distinguish:

- application binaries;
- runtime state;
- logs;
- backups;
- customer/business database.

Business data is not deleted automatically by a normal uninstall.

## Update sequence

1. Receive release manifest.
2. Verify version compatibility.
3. Download to staging.
4. Verify hash/signature.
5. Snapshot update-sensitive application state.
6. Stop components safely.
7. Validate migration compatibility.
8. Apply application package.
9. Execute explicit database deployment.
10. Start components.
11. Verify version + health + readiness.
12. Mark successful release.
13. On failure, invoke rollback.

## Rollback rule

Rollback restores the previous application payload only when the declared database compatibility contract permits it.

If a database migration is not backward-compatible, the update must be blocked before the application switch or use a defined forward-compatible recovery path.

## Release manifest

Every release records:

- product version;
- component versions;
- minimum/maximum supported database schema;
- package hashes;
- package signatures;
- release channel;
- required migration IDs;
- rollback compatibility;
- minimum operating system/runtime prerequisites.

## Update safety

The updater process must never overwrite binaries that are executing itself.

The update implementation therefore uses:

- staging directory;
- separate updater process;
- validated package;
- atomic/controlled payload switch;
- explicit health gate;
- rollback marker/state.

## Proof required

For every release candidate:

- clean install;
- same-version repair;
- upgrade from previous release;
- interrupted download;
- checksum failure;
- startup failure;
- rollback;
- service restart;
- uninstall without business-data loss.


## Early installability track — 2026-10-09

This is an early, internal installability track. It does **not** claim that the final installer/updater/rollback is complete and does not replace the release gates below.

### First delivery shape

- **Server + Desktop package:** installed on the designated server PC.
- **Agent package:** independently installed on a different Windows PC.
- **PostgreSQL:** the setup path must make its provisioning model explicit. It may not silently assume an undocumented database, and Server readiness must fail if the required schema is missing or migrations remain pending. Before bundling a PostgreSQL installer, validate the supported major version, service account, data path, backup/upgrade behavior and license/distribution implications.
- **Published payloads:** self-contained `win-x64` outputs where supported; no reliance on the developer's source checkout or global `dotnet run` at the destination. Generate a manifest with exact source SHA, component versions, payload hashes and runtime target. Database schema deployment uses the explicit `GameNet.Server.exe --migrate-only` mode after protected settings are configured; normal service startup must not migrate automatically.
- **Security:** secrets are created/provided during setup and not committed to the repository, embedded as shared constants, printed in logs, or left in package archives. Remote bootstrap/provisioning uses HTTPS. Each Agent must receive a unique identity/credential; a shared server provisioning key must never be shipped inside every client installer.
- **State separation:** application binaries live below Program Files; mutable configuration, identity, logs and backups live in protected ProgramData locations. Normal uninstall may remove services/binaries but must preserve database/business data unless an explicit, separately confirmed destructive action exists.

### Execution order

1. Audit existing runtime configuration, Windows Service behavior, database migration path and Agent enrollment/provisioning before writing installer code.
2. Build/publish Server, Desktop and Agent from one source SHA. The Server executable contains a separate `--migrate-only` entry point for explicit schema deployment.
3. Validate the payload and create hashes/manifest; fail if any expected output is missing or any development secret/default is packaged. Server secrets live in a DPAPI LocalMachine-protected file at `C:\\ProgramData\\GameNet Manager\\Config\\server-secrets.bin`; setup must restrict file ACLs to SYSTEM and Administrators. The file is not packaged.
4. Add the Server + Desktop setup path and independent Agent setup path. Run install/uninstall service operations with checked exit codes and preserve diagnostics on failure.
5. Run a clean-install test on the server Windows PC and a different Windows client PC. Verify database connectivity/schema/readiness, secure first-owner bootstrap, Agent enrollment/unique credential, heartbeat, Health Probe, restart/reconnect and uninstall data preservation.
6. Record package checksum, tested source SHA, Windows/PostgreSQL versions, log/evidence references and each unresolved defect in the roadmap/bug log.

### Explicit status

As of this checkpoint, this is **not implemented or installer-certified**. The repository's current Agent runtime test launches the project with `dotnet run` against an isolated test Server; that is useful integration evidence but not a substitute for installing the packaged services on clean Windows machines. Do not publish a `Setup.exe`/MSI download link until the package has actually been built and tested.


## Explicit Server configuration and schema deployment

- The Server may read the allowlisted DPAPI-protected configuration file only on Windows. The default ProgramData file is enabled only by `GameNet:ProtectedSettings:Enabled=true` in Production configuration; explicit override paths are reserved for controlled tests/operations.
- Allowed values cover PostgreSQL connection string, authentication issuer/audience/signing key, Agent provisioning key, and the initial bootstrap secret. No secret is stored in appsettings or a release ZIP. Corrupt ciphertext, unknown/duplicate fields, missing DB/auth keys and weak key material fail startup closed.
- `GameNet.Server.exe --migrate-only` applies EF migrations and exits without starting a listener. Routine Server service startup never auto-migrates. The installer must configure the protected file and ACL, then call this one-shot mode and verify its exit code/readiness before the long-running Server service is started.
- The new mode does not itself create the protected settings file; a separate setup/commissioning step must collect/configure values safely. This is still payload/deployment groundwork, not an end-user installer.
