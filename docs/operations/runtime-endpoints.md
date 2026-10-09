# Runtime Endpoint Configuration

Desktop and Agent endpoints are installed configuration, not source-code constants.

## Files

- Desktop: %ProgramData%\GameNet Manager\Config\desktop.json
- Agent: %ProgramData%\GameNet Manager\Config\agent.json
- Agent identity and protected credential state: %ProgramData%\GameNet Manager\Agent

The configuration script creates the files. Environment variables remain explicit higher-priority overrides for diagnostics and certification; packaged defaults no longer point silently to 127.0.0.1.

## Configure a real server address

Run an elevated PowerShell on the Desktop PC and on every Agent PC. Replace the example address and port with the actual address and HTTPS port of the Server:

    .\scripts\configure-runtime-endpoint.ps1 -ServerBaseUrl "https://192.168.0.9:5080" -Component Both
    .\scripts\test-runtime-endpoint.ps1 -Component Desktop
    .\scripts\test-runtime-endpoint.ps1 -Component Agent

The Both option writes only the two client endpoint files; it does not modify server/database configuration or Agent credentials. Run with -Component Desktop or -Component Agent when configuring only one component.

## Transport security rules

- A network endpoint must use HTTPS.
- HTTP is accepted only for loopback development/certification.
- Base URLs cannot contain credentials, query strings, fragments or application paths.
- Desktop's HttpClient and Agent's SignalR/HTTP clients retain normal operating-system certificate validation. There is no callback that accepts invalid certificates.
- The server certificate must be trusted by Windows and its Subject Alternative Name must match the host/IP in ServerBaseUrl. Do not use a certificate bypass to make a failed connection pass.

The endpoint test calls /health using normal TLS validation and requires readiness to be Ready. A successful build or a loopback test does not certify LAN connectivity.

## Current release gate

The final proof is still a two-computer LAN test: configure the Server's HTTPS listener and certificate, establish trust on the Desktop/Agent computer(s), run the endpoint test on each client, then confirm the real Agent acquires its authoritative lease and remains connected after a service restart. Until this evidence is recorded for the same tested commit, Setup and two-PC installation remain uncertified.
