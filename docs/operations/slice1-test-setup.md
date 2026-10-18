# GameNet 5 — Slice 1 Test Setup

**This is an internal, temporary functional-test installer. It is not the production GameNet installer and does not certify release readiness.**

## What the owner does

1. Download `GameNet5-TestSetup.exe` from the exact GitHub Actions run recorded for the candidate.
2. Run Setup, click Next, choose an installation folder and finish installation.
3. Use the Desktop shortcut **GameNet 5 Test Launcher**.
4. Enter the PostgreSQL 17 administrator login for the local PostgreSQL instance and the throwaway Owner credentials. Check the explicit new-disposable-database confirmation, then click **Create fresh test database and launch**.
5. The launcher creates a newly named `gamenet_slice1_test_*` database and restricted `gamenet_slice1_app_*` role. It does not drop or reuse earlier databases. The PostgreSQL administrator password is used only for this creation step and is not stored.
6. When the WPF Desktop opens, sign in with the Owner username/password shown in the launcher, verify the test PC is online, start a session, wait about two minutes, end it, and inspect the pending-payment list. For a 1,000-Toman hourly test rate, the resulting charge should be about 33 Toman, subject to the application's rounding rule.
7. Use **Stop test processes** in the launcher when evidence is captured. Do not kill unrelated GameNet processes.

The Owner and tariff settings can be saved for this Windows user. They are encrypted with Windows DPAPI CurrentUser under LocalAppData. The PostgreSQL administrator password is never saved; it must be entered for each newly created database. A new database per run avoids mixing payments and invoices from separate test attempts.

## Isolation guarantees

- Server, Agent and Desktop are started as temporary processes bound to loopback; this test Setup does not register Windows services.
- The installer installs files to the user-selected folder and creates a Desktop shortcut. It does not write application configuration to machine-wide ProgramData, install certificates, change firewall/registry/system time, or modify LAN configuration.
- The test harness refuses to proceed when known GameNet 5 Server/Agent services or protected GameNet Manager server-secret state already exist. It checks these preconditions before creating a test database.
- The Server uses a generated temporary config/state directory with a restricted ACL. The app role used by GameNet is not a PostgreSQL superuser and cannot create roles or databases.
- Existing disposable test databases are kept for audit/evidence and are never automatically dropped. A later cleanup action must explicitly confirm exact generated test database and role names before deleting anything.
- The installer and payload are tied to the exact source SHA and SHA-256 hashes recorded in `setup-manifest.json`, `manifest.json` and `SHA256SUMS.txt`.

## Scope and limitations

This supports the bounded local PC Session → invoice → pending payment/cash UI acceptance. It does not replace independent database reconciliation of Session, Invoice, Payment, Ledger and Audit records. It does not certify Windows Service identities, installed-Service upgrades, production TLS/DPAPI/ACL, a second-PC LAN, reboot/time-continuity recovery, OS lock/unlock, production Setup, update, repair, rollback or pilot readiness. Those are separate gates requiring suitable isolated environments.

The installer requires PostgreSQL 17 and its `psql.exe` command-line tools to already be installed and running on this PC. It does not install or reconfigure PostgreSQL itself.
