# Initial owner setup

The first owner is created through the Server; no desktop-local administrator, default password, or database bypass is used.

## Secret boundaries

- Do **not** set `GAMENET_BOOTSTRAP_SECRET` in the Server process or machine environment, and do not pass it on the command line. The current Server candidate rejects those override paths.
- The initial Owner bootstrap secret is stored as `GameNet:Setup:BootstrapSecret` inside the DPAPI-protected setup settings file at `%ProgramData%\GameNet Manager\Config\server-secrets.bin`. The elevated `scripts/write-protected-server-settings.ps1` workflow prompts for this secret and writes the protected setup settings. The file must not contain the PostgreSQL connection string, JWT signing key, or Agent provisioning key.
- The PostgreSQL connection string, JWT signing key, and Agent provisioning key belong in the dedicated DPAPI-protected Server store at `%ProgramData%\GameNet Manager\Secrets\server-secrets.v1.dpapi`. Its provisioning command is `GameNet.Server.exe --provision-secrets`; the database connection string is prompted without console echo and the signing/provisioning keys are generated randomly.

## Required safe order

1. Configure and verify the Server TLS endpoint. Confirm that the Server Windows service is registered with its intended logon identity and dedicated service SID.
2. Before provisioning anything on an existing Manager PC, capture and review the read-only report from `scripts/inspect-manager-runtime-state.ps1`. Do not run secret provisioning, TLS setup, cleanup, ACL changes, or service changes until the observed service identity, certificate state, ProgramData ACLs and listener bindings have been reviewed.
3. Run `scripts/write-protected-server-settings.ps1` from an elevated PowerShell session to create the non-secret setup settings and temporary Owner bootstrap secret. It refuses to overwrite an existing settings file.
4. Only after the service identity has been confirmed and the store/ACL design approved, run the built Server executable with `--provision-secrets` from an elevated console. This command must run alone as the only argument; enter the PostgreSQL connection string only at its hidden prompt. It refuses to overwrite an existing store.
5. Start the Server and confirm health/readiness over HTTPS. From a trusted management workstation run:

   ```powershell
   .\scripts\bootstrap-admin.ps1 -ServerBaseUrl "https://gamenet-server.example"
   ```

6. Enter the same Owner bootstrap secret that was entered in step 3, followed by the chosen Owner password. The management script prompts securely, sends the secret only in the protected HTTPS request header, and does not write the values to disk.

**Current acceptance limit:** this sequence is the intended code path, not a certification of the installed service. The real Windows service identity, effective ACLs, DPAPI access after restart, and TLS private-key access still require isolated Windows-service/LAN tests. Do not run provisioning on a live Manager PC until its read-only preflight has been reviewed.

## Bootstrap cleanup still open

After confirming that the Owner can sign in, the setup secret remains inside the protected settings file today. A supported, atomic post-bootstrap removal step that preserves the other settings and ACLs has not yet been implemented. Until it is implemented and tested, do not claim the installation is fully hardened and do not delete or rewrite the protected settings file manually.

The bootstrap endpoint uses a constant-time secret comparison and a PostgreSQL transaction-scoped advisory lock. It verifies inside the transaction that no operator exists, so concurrent requests cannot create multiple first owners. The setup secret is separate from the Owner password and the Agent provisioning key.

Do not expose bootstrap, operator authentication, Agent credential APIs or SignalR Agent transport over unencrypted LAN HTTP. Direct HTTPS setup is expected; forwarded HTTP must not be treated as secure unless the trusted TLS proxy and forwarded-header configuration are explicitly certified.
