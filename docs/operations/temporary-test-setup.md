# Temporary Test Setup — Slice 1 manual acceptance

**Issue:** [#131](https://github.com/alizebal73/5/issues/131)  
**Related business acceptance:** [#98](https://github.com/alizebal73/5/issues/98)

This package is a disposable, isolated Windows test installer, not a production release.

## Required test environment

- Two fresh Windows x64 VMs or dedicated PCs on an isolated LAN: one Manager/Server + Desktop and one Agent client.
- PostgreSQL 17 installed and running locally on the Manager/Server VM. The temporary Setup does not install PostgreSQL.
- Administrative rights on both test machines. Setup bundles its own PowerShell 7.6.4 runtime and self-contained app binaries; no Python, standalone .NET SDK or separate PowerShell installation is required on the test PCs.
- A VM snapshot taken before setup. Revert it after acceptance, especially if setup fails partway through.

Do not run this package on the live GameNet/shop PC, do not reuse a production database, and do not import any private server certificate or secret onto the Agent client.

## Manager/Server + Desktop

1. Start `GameNet5-TestSetup.exe`, choose **Manager / Server + Desktop**, and install to the desired directory.
2. In the setup console, type the exact isolation confirmation. Select the reserved IPv4 address for the Manager test VM.
3. Enter the local PostgreSQL username, port, and password at the prompts. Setup creates a new `gamenet_test_*` database and `gamenet_test_app_*` role with a generated password. The role is not a superuser and the database must be empty.
4. Enter a temporary Owner bootstrap secret of at least 32 characters. Re-enter that secret when the initial Owner account helper prompts. Choose and retain the test Owner password only for the isolated test.
5. Wait for the HTTPS readiness check, then open **GameNet Manager (TEST)** from the Start Menu and sign in with the Owner account. Return to the setup console and press Enter only after a successful sign-in. Setup removes the one-time bootstrap secret and restarts the Server.
6. Record the displayed Server URL, public certificate path, certificate SHA-256 fingerprint and generated test database name.

If setup reports pre-existing GameNet services or `%ProgramData%\GameNet Manager` state, stop. Do not remove or overwrite that state to make the installer continue.

## Agent client

1. Run the same Setup executable on the separate fresh Agent VM and choose **Dedicated Agent client**.
2. Copy only the public `server-trust.cer` file from the Manager VM. Verify the 64-character SHA-256 fingerprint against the value shown independently on the Manager screen.
3. Enter the exact HTTPS URL, certificate path and fingerprint. Choose a stable DeviceId, such as `station-pc-01`.
4. Provide authorized Owner/operator credentials when the enrollment helper prompts. The enrollment token is never printed.
5. In Manager Desktop, bind that DeviceId to the intended PC Station and confirm the Agent is online/leased.

## Manual business acceptance

Create a PC Station and fixed hourly Tariff. Bind the Agent to that Station. Use test customer data only, then exercise the complete WPF journey:

1. select an existing test customer or Walk-in;
2. start the PC Session;
3. end the Session and inspect the computed duration/amount;
4. inspect the invoice preview;
5. enter the received cash and explicitly confirm payment;
6. verify the UI outcome and station availability;
7. exercise an ambiguous checkout response/restart without re-submitting a paid checkout blindly;
8. reconcile persisted Session, Invoice, Payment allocation, Ledger and Audit rows against an independent whole-Toman hand calculation in the disposable database.

Capture screenshots, exact build SHA, checksum, test steps and observed results for Issue #98. An automated green build alone does not satisfy the manual acceptance criteria.

## Safety and limitations

- The Server special test-secret provisioning command accepts only a loopback PostgreSQL connection to a database named `gamenet_test_*`, a dedicated `gamenet_test_app_*` non-superuser login, pooling disabled and an empty database.
- Database migrations are explicit and are run only after this disposable database was created. The installer never provisions credentials for a remote database.
- HTTPS certificate validation is not disabled.
- This temporary installer does not install PostgreSQL, perform updates/repair/rollback certification, certify production Windows service operations, or prove OS-level PC lock/unlock enforcement.
- Uninstall removes only services that still point to this exact install folder and the matching test firewall rule. ProgramData DPAPI state, the test database and certificates are deliberately retained. Revert the VM snapshot for full cleanup.
- Do not use this package to charge real customers or against any live business database.
