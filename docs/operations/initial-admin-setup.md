# Initial owner setup

The first owner is created through the Server; no desktop-local administrator, default password, or database bypass is used.

1. Configure `GAMENET_BOOTSTRAP_SECRET` in the Server process environment before starting or restarting GameNet Server. Use a randomly generated value with at least 32 UTF-8 bytes. Do not commit it or put it into command-line arguments.
2. Reach the Server over HTTPS. HTTP is accepted only for a loopback address during local setup and development.
3. From a trusted management workstation run:

   ```powershell
   .\scripts\bootstrap-admin.ps1 -ServerBaseUrl "https://gamenet-server.example"
   ```

4. Enter the configured setup secret and owner password when prompted. The script does not write either value to disk.
5. Verify that the owner can sign in from the Desktop. Then remove `GAMENET_BOOTSTRAP_SECRET` from the Server environment and restart the service.

The endpoint uses a constant-time secret comparison and a PostgreSQL transaction-scoped advisory lock. It verifies inside the transaction that no operator exists, so concurrent requests cannot create multiple first owners. The setup secret is separate from the owner's password and the Agent provisioning key.

Do not expose bootstrap, operator authentication, Agent credential APIs or SignalR Agent transport over unencrypted LAN HTTP. Direct HTTPS setup is expected; forwarded HTTP must not be treated as secure unless the trusted TLS proxy and forwarded-header configuration are explicitly certified.
