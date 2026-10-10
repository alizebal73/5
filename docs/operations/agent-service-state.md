# Agent Windows Service State Provisioning

The Agent stores its stable DeviceId and its DPAPI CurrentUser credential below:

    %ProgramData%\GameNet Manager\Agent

The directory must be prepared for the actual Agent Windows service identity before first service start. DPAPI protects the credential bytes, but it does not replace a restrictive filesystem DACL. Do not rely on an interactive administrator account to create this directory and do not launch the Agent interactively as a substitute for the service test.

## Supported preconditions

- Run only on a dedicated Agent client. This helper refuses a machine that has the GameNet Server service or Server configuration/secret-store markers.
- Register GameNet 5 Agent as a Windows service first, with NT AUTHORITY\LocalService as its logon account.
- Stop the Agent service before running the helper. The helper never stops a running service.
- Run the helper from elevated PowerShell. It enables the dedicated NT SERVICE\GameNet 5 Agent service SID and configures protected DACLs.
- Configure the Server endpoint and trust its public TLS certificate separately. Never copy a server private key or put a token/password into endpoint JSON.

## Setup sequence on the Agent client

After the service is registered but stopped, run the endpoint configuration appropriate to this machine:

    .\scripts\configure-runtime-endpoint.ps1 -ServerBaseUrl "https://192.168.0.9:5080" -Component Agent

Then prepare the service-owned state directory:

    .\scripts\configure-agent-runtime-state.ps1

The helper preserves existing files; it does not display, decrypt, delete, or re-enroll an existing identity or credential. It refuses reparse points and validates the explicit DACLs before it reports success.

It protects the %ProgramData%\GameNet Manager parent and Config with SYSTEM/Administrators full control and read/execute for ordinary Users and the Agent service SID. The Agent state directory is inheritance-protected and grants Modify only to SYSTEM, Administrators, and the dedicated Agent service SID. The service must be restarted after service-SID changes so its process token contains that SID.

## One-time enrollment token delivery

After the endpoint and state DACLs are configured, leave the Agent service stopped and run this helper elevated on the **dedicated Agent PC**:

    .\scripts\provision-agent-enrollment.ps1 -DeviceId "station-pc-01"

Use a stable DeviceId that matches the station being enrolled. The helper prompts for an operator username/password without putting them in command-line arguments. The operator must have the `agents.enrollment.manage` permission. It uses the Server's authenticated HTTPS API to issue a short-lived, one-use token and writes only a DPAPI LocalMachine-protected payload to:

    %ProgramData%\GameNet Manager\Agent\enrollment-token.dpapi

The token file's DACL is inheritance-protected and grants read access only to SYSTEM, local Administrators, and the dedicated Agent service SID. DPAPI optional entropy binds the protected payload to the configured DeviceId. The token and bearer credentials are never printed or placed in an environment variable. If local handoff fails after the Server issues a token, the helper attempts to revoke the pending token and does not print the token value.

At first start, the Agent reads the DPAPI file, redeems the token over HTTPS, writes the returned per-device credential using DPAPI CurrentUser under the actual LocalService identity, and deletes the token file only after the credential has been saved successfully. In Production, an environment-only `GAMENET_AGENT_ENROLLMENT_TOKEN` is rejected; the environment seam remains for Development/CI only. Do not manually copy token text into service environment settings.

After provisioning, start the Windows service and verify successful enrollment, credential persistence, token-file deletion, normal reauthentication after a controlled service restart, and heartbeat/lease recovery. DPAPI CurrentUser data created by an interactive user is not evidence that the LocalService service can decrypt it.

## Limitations

This is a service-state and enrollment provisioner, not an installer. It does not register the Agent service, install PostgreSQL, configure the Server's protected deployment-secret store, or certify a physical LAN deployment. It refuses to overwrite existing identity/credential/token state; use a separately reviewed recovery/re-enrollment procedure for existing state. Use only isolated test machines, a disposable test database, and throwaway credentials until the combined Server-secret-store + Agent-enrollment candidate has been reviewed and tested on the actual Windows service identities.
