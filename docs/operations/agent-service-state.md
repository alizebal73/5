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

## Recovery after server redemption but local credential persistence fails

A one-time token cannot be redeemed again after the Server commits it. The Agent intentionally keeps the local protected token file if it cannot save the returned credential, but that retained token is no longer redeemable. Do not repeatedly restart the service or manually edit/delete protected files.

1. Keep the Agent service stopped and verify that `agent.json`, `identity.json`, the credential file, and the protected token file all belong to the intended DeviceId. Never use recovery to move an identity to another station.
2. Run the same provisioning helper with the explicit recovery switch and a reason, for example:

       .\scripts\provision-agent-enrollment.ps1 -DeviceId "station-pc-01" -Recover -RecoveryReason "Credential could not be persisted after token redemption"

   The helper requires an elevated session and exact DeviceId confirmation. It authenticates the operator over HTTPS and calls the dedicated recovery endpoint; it does not print the token.
3. The Server permits recovery only if this DeviceId has no credential history with a successful authentication. If a current credential exists, it must be the credential created by the most recent enrollment redemption and must never have authenticated; the operation revokes it before issuing the replacement. If there is no current credential, recovery may refresh an unredeemed/pending token only when no prior credential has authenticated. All pending tokens are revoked before the new short-lived token is issued. A credential that has ever authenticated is not revoked or recreated by this endpoint—even if it was later revoked—so ordinary credential revocation cannot be bypassed.
4. The helper atomically replaces the protected token file, verifies its ACL, and removes the old local credential only after the replacement token is safely committed. Start the service only after the helper reports success, then verify enrollment, credential persistence, token-file deletion, authentication, and heartbeat.
5. If the recovery handoff fails, keep the service stopped and review the state before retrying. The helper attempts to revoke the newly issued pending token when it cannot commit the local handoff. A second explicit recovery can be performed after confirming the on-disk state.

Recovery is an auditable, intentionally destructive operation for a never-authenticated enrollment credential. It is not a general-purpose credential rotation command and must not be used to reset an Agent that is already connected or has authenticated successfully.

After provisioning, start the Windows service and verify successful enrollment, credential persistence, token-file deletion, normal reauthentication after a controlled service restart, and heartbeat/lease recovery. DPAPI CurrentUser data created by an interactive user is not evidence that the LocalService service can decrypt it.

## Limitations

This is a service-state and enrollment provisioner, not an installer. It does not register the Agent service, install PostgreSQL, configure the Server's protected deployment-secret store, or certify a physical LAN deployment. Initial provisioning is create-only and refuses existing identity/credential/token state. The explicit `-Recover` path is allowed only when this DeviceId has no history of successful credential authentication. It can replace an active enrollment credential that has never authenticated or refresh pending/unredeemed enrollment tokens when no credential has authenticated; it is not general re-enrollment or credential rotation, and it refuses any DeviceId with prior successful authentication, even if that credential was later revoked. Use only isolated test machines, a disposable test database, and throwaway credentials until the combined Server-secret-store + Agent-enrollment candidate has been reviewed and tested on the actual Windows service identities.
