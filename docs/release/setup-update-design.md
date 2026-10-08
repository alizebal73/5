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
