# Slice 1 process-based Windows acceptance

> **Owner-run test delivery rule (binding from 2026-10-18):** This document is an internal maintainer/developer reference, not instructions for the GameNet owner to execute manually. Do not ask the owner to copy PowerShell commands, enter connection strings in a shell, or run this script directly. Owner-run acceptance must be delivered through a test-only Windows `Setup.exe`/GUI Launcher with a Desktop shortcut, saved per-user settings, automatic preflight, visible progress/errors, and evidence collection. Until that installer exists and is verified for the exact candidate SHA, the manual owner test is BLOCKED. This test-only installer is permitted before Issue #98 acceptance and is distinct from production Setup/Updater.


## Purpose and boundary

Use this procedure only for early Issue #98 functional acceptance of the PC Session → invoice → cash checkout path. The script starts the published Server, Agent and WPF Desktop as ordinary processes on one Windows PC. The Server URL is restricted to 127.0.0.1 on a temporary port.

This is a functional acceptance harness for the new, separate GameNet 5 application; an older GameNet program installed on the same PC is not a blocker by itself. This harness is not Setup/MSI, a production deployment, or the later installed-Service/TLS/DPAPI/ACL/LAN certification. It does not perform Issue #92 Windows lock/unlock, reboot, network-disruption or OS-control tests. Use disposable test identities/data and simulated amounts; never use real customer details or real cash.

The harness does not register Windows services and does not modify ProgramData, Windows certificates, firewall, registry, machine time, or system configuration. It creates a temporary config/state folder under the current user's TEMP directory, migrates only a verified empty local test database, creates throwaway test bootstrap/Server keys in process memory and temporary settings, and preserves the database and temporary evidence folder after exit.

## Before running

1. Extract the outer GitHub Actions artifact and verify the inner GameNet5-RuntimeLanSmoke ZIP against the adjacent SHA256SUMS.txt before extracting the inner ZIP. Keep that checksum file and the generated manifest with the test record.
2. Read manifest.json. Confirm the tested checkout SHA is the exact candidate SHA being accepted. The launcher also checks the SHA-256 of every packaged file against the manifest before starting anything.
3. Run Tools\inspect-manager-runtime-state.ps1 as a read-only preflight and review the report. If this new GameNet 5 application already has a GameNet 5 Server/Agent service or protected Server settings/state, stop: the process-only path is intended for first-time functional acceptance before that runtime is provisioned. This check concerns the new GameNet 5 application, not an unrelated legacy GameNet program. The launcher performs the matching service/path guard itself.
4. Use PowerShell 7 (pwsh), Windows x64, the PostgreSQL command-line client psql.exe, and a newly created local PostgreSQL database owned by a dedicated test-only login role. The test role may create tables in its own test database, but must not have superuser, create-role, create-database, replication or bypass-RLS privileges. Do not use postgres, root or administrator roles.
5. The database name must clearly identify a test database, for example gamenet_slice1_test_20261017. The harness requires the database host to be localhost/127.0.0.1/::1 and runs a read-only preflight confirming there are zero non-system tables, views, materialized views, foreign tables and sequences. If psql is missing, the database is not empty, the role is elevated, the database name is ambiguous, or the host is not loopback, the harness stops without migrating.
6. In a PowerShell 7 console, change to the extracted package root. Run Tools\run-slice1-process-acceptance.ps1. Paste the new database's PostgreSQL connection string at the hidden prompt and type exactly USE EMPTY TEST DATABASE after the preflight passes. The password is hidden while entered and is not written to the Server config, source control, or command-line arguments.
7. Choose a throwaway Owner username, display name and password when prompted, then a positive whole-Toman hourly tariff used only in the test database. Do not use a production Owner login or password.

## What the launcher does

- Verifies the package manifest and every listed file hash.
- Checks for installed GameNet 5 Server/Agent services and production Server state under ProgramData, refusing to continue if found.
- Confirms the target is a new empty local test database and the role is not elevated.
- Applies the EF migrations using the explicit Server migration-only command and the Development-only test-secret seam. The DB connection string is passed only to the Server child-process environment and is never put in the command line or config file.
- Starts the Server on http://127.0.0.1:<temporary-port>, creates the first Owner with a random one-time bootstrap secret, removes that bootstrap secret from the test config, and restarts the Server.
- Creates a single PC station and fixed hourly test tariff through the Server APIs; issues a short-lived Agent enrollment token; starts a test Agent with its own temporary identity/credential state; binds it to that station; and checks that the Server reports a current Agent lease.
- Opens the WPF Desktop with the temporary Server endpoint configured. The only app processes this launcher starts are the Server, Agent and Desktop. Server database/signing keys are not inherited by the Desktop or Agent process.

The launcher fails closed when it cannot prove the required preconditions. Do not bypass its checks, switch the Server to Production, reuse a database with existing objects, change the listener to a LAN address, or use the normal Windows-Service setup scripts for this acceptance run.

## Manual WPF acceptance and reconciliation

1. Sign into the opened WPF Desktop with the throwaway Owner created for this disposable database.
2. Verify the seeded test PC is visible, available and Agent-online, and the test tariff is available. Do not continue if the Station is offline or the app/API disagrees.
3. Follow Tools\manual-cash-sale-reversal-acceptance.md for the complete PC Session → invoice → cash checkout → tender correction → reversal sequence and read-only PostgreSQL reconciliation. It covers permission denials, reload after Desktop restart, and SQL checks for Session/Invoice/Payment/Ledger/Audit.
4. **Timing recovery is a separate mandatory #98 gate.** Read Tools\manual-session-timing-recovery-acceptance.md. Wall-clock/reboot/fail-closed experiments are NOT executed by this process launcher because they affect Windows itself. Run them only when interrupting this host is acceptable (preferably in a disposable Windows VM/PC with a revertible snapshot) and use a disposable database; do not reboot or change the clock while the PC is serving customers. The ordinary loopback process-based functional acceptance may run on this PC after preflight with a new empty test database. Until timing-recovery evidence exists, record that separate gate as BLOCKED/NOT RUN; this process bundle alone cannot accept Issue #98.
5. Use test-only values throughout. A successful button response or green CI is not financial acceptance. Save screenshots, all relevant IDs, elapsed ticks, invoice amount, original received/change, corrected received/change, reversal amount, and results from read-only queries A–D of the cash procedure.
6. Mark an unavailable step NOT RUN and record why. Do not infer success, replay an ambiguous financial request without first reconciling durable state, or edit/delete ledger/audit rows to make an assertion pass.
7. When evidence is saved, return to the PowerShell window and press Enter. The harness stops only the three processes it started. It keeps the temporary test folder for inspection and does not delete the test database. Confirm the processes have stopped before deleting that folder manually.
8. Attach evidence, the exact source SHA/tree, artifact SHA-256, environment details, SQL results, timing-recovery status and pass/fail decisions to Issue #98. Do not close #98 until all mandatory acceptance criteria have been met.

## Known limits

This process harness does not certify a real Windows Service identity, DPAPI LocalMachine protected secrets, production TLS certificate validation, service ACLs, repair/update/rollback, Agent behavior after Windows reboot, OS-level lock/unlock, or a second-PC LAN. Those are separate later gates. The old test Setup from the parked feature stack is not acceptable for this run.
