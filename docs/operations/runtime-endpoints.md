# Runtime Endpoint Configuration and TLS

Desktop and Agent endpoints are installed configuration, not source-code constants.

## Configuration files

- Server: %ProgramData%\GameNet Manager\Config\server.json
- Desktop: %ProgramData%\GameNet Manager\Config\desktop.json
- Agent: %ProgramData%\GameNet Manager\Config\agent.json
- Agent identity and protected credential state: %ProgramData%\GameNet Manager\Agent

These files contain endpoints and certificate thumbprints, not passwords, provisioning secrets, or signing keys. Environment variables remain explicit higher-priority overrides for diagnostics and certification.

## 1. Configure HTTPS on the Server

Run an elevated PowerShell session on the Server PC. Replace the sample IPv4 address, port, and service account with the actual values for this installation:

    .\scripts\configure-server-tls.ps1 -ServerIp "192.168.0.9" -DnsName "gamenet.local" -HttpsPort 5080 -ServiceAccount "NT AUTHORITY\NETWORK SERVICE"

This creates a machine certificate with a SAN for the provided IP and DNS name, places it in the LocalMachine certificate store, configures the Server endpoint and certificate thumbprint in ProgramData, and exports only the public .cer file for client trust. It also grants the configured Windows service identity access to the private key. The private key is not exported.

The script's service account must match the actual Windows account that runs GameNet 5 Server. When setup registers the service with a different account, pass that account explicitly. Restart the Server service after the configuration is in place.

The configuration binds HTTPS to the selected server IP and port. Production startup refuses a non-loopback HTTP listener or an HTTPS listener without a valid certificate thumbprint.

## 2. Trust the public certificate on each client PC

Copy gamenet-server.cer to the Desktop PC and each Agent PC. Run elevated PowerShell on each:

    .\scripts\trust-server-certificate.ps1 -CertificatePath "C:\Temp\gamenet-server.cer"

Only the public certificate is copied. Never distribute the Server's private key. This local single-site certificate is trusted explicitly in Windows LocalMachine\Root; Windows clients continue to perform normal certificate-chain, validity, and host-name/IP SAN verification.

## 3. Configure the endpoint on Desktop and Agent PCs

Run elevated PowerShell on each client PC, using the exact host or IP present in the certificate SAN:

    .\scripts\configure-runtime-endpoint.ps1 -ServerBaseUrl "https://192.168.0.9:5080" -Component Both
    .\scripts\test-runtime-endpoint.ps1 -Component Desktop
    .\scripts\test-runtime-endpoint.ps1 -Component Agent

If a computer only needs one component, use -Component Desktop or -Component Agent. Restart the Desktop app and the Agent Windows service to load the files.

The endpoint helper accepts HTTPS for network hosts. Plain HTTP is accepted only for loopback development/certification. URLs containing user information, query strings, fragments, or application paths are rejected. Neither HttpClient nor SignalR has a certificate-validation bypass.

## 4. Network validation

The endpoint test uses normal Windows TLS validation and calls /health, requiring data.readiness to be Ready. Also confirm TCP port 5080 is allowed from the local subnet by Windows Firewall and that the Server is listening on the configured LAN address.

A green build, a loopback smoke test, or a successful /health request from the Server itself is not proof of two-computer connectivity.

## Remaining release gate

The final proof must run on the actual LAN with the Server PC, Desktop PC, and at least one Agent PC on the tested build: trusted certificate, real health/readiness response, Agent credential provisioning, authoritative lease acquisition, heartbeat/reconciliation, and successful reconnect after service restart. Record the exact tested commit and evidence. Until then, the final Setup and two-PC installation remain uncertified.
