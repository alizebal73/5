# Test-only Windows service registration for Runtime LAN acceptance

This is a narrowly scoped test helper for the self-contained Runtime LAN Smoke payload. It is NOT a production installer, Setup/MSI, updater, repair tool, or service recovery tool.

## Preconditions

1. Use a newly isolated Windows Server test PC and a separate dedicated Windows Agent test PC. Use a new disposable PostgreSQL database and throwaway credentials.
2. Extract the inner payload only after verifying the ZIP against SHA256SUMS.txt.
3. On each PC, run Tools\inspect-manager-runtime-state.ps1 read-only and review the report. On the Agent PC use -ServiceName "GameNet 5 Agent". Stop if an unexpected service, configuration, protected file, certificate, ACL or listener exists. If %ProgramData%\GameNet Manager exists at all, including an empty directory, stop and use a fresh isolated PC. This manual hard stop is broader than the helper's automated known-marker check.
4. Open elevated PowerShell from the extracted payload root. The registration helper checks the package manifest and the SHA-256 of the executable and of itself. It refuses to replace an existing GameNet service or reuse known ProgramData runtime state.

## Register exactly one stopped service

On the dedicated Server PC:

    .\Tools\register-test-runtime-service.ps1 -Component Server -PayloadRoot $PWD -TestOnly -PreflightReviewed

The Server is registered as NT SERVICE\GameNet 5 Server; this dedicated virtual account is the intended identity for Server settings, the TLS private key and protected secret ACLs.

On the separate Agent PC:

    .\Tools\register-test-runtime-service.ps1 -Component Agent -PayloadRoot $PWD -TestOnly -PreflightReviewed

The Agent is registered as NT AUTHORITY\LocalService, because its persistent device credential uses DPAPI CurrentUser. The helper enables and verifies the dedicated NT SERVICE\GameNet 5 Agent Service SID.

For both roles, the service is registered as Manual / Demand Start and remains stopped. The helper never starts a service, configures TLS, writes secrets, modifies ACLs, touches PostgreSQL, or creates an Owner. It must not be used to upgrade, repair, replace or clean a service on an existing Manager computer.

## Configure TLS on the isolated Server PC

Only after reviewing the read-only preflight and registering the Server test service under the intended identity, configure TLS from the extracted payload root in an elevated PowerShell session. Replace all placeholders with values reserved for the isolated test LAN; the DNS name must resolve to the selected Server IP.

    .\Tools\configure-server-tls.ps1 -ServerIp "<TEST_SERVER_IPV4>" -DnsName "<TEST_SERVER_DNS>" -HttpsPort <TEST_HTTPS_PORT> -ServiceAccount "NT SERVICE\GameNet 5 Server"

Do not run this on a live Manager PC. The packaged Tools\runtime-endpoints.md uses paths that match the extracted payload; follow it for public-certificate export/trust and endpoint configuration.

## Continue the role-specific sequence

- Server PC: configure TLS for the exact service account NT SERVICE\GameNet 5 Server (not NetworkService) using the command in the Configure TLS on the isolated Server PC section. Then write protected setup settings, provision the protected Server secret store explicitly, and only then start the stopped Server service. Complete Owner bootstrap/finalization and prove readiness after a service restart.
- Agent PC: trust the public Server certificate, configure the endpoint, run Tools\configure-agent-runtime-state.ps1, issue an enrollment using Tools\provision-agent-enrollment.ps1, and only then start the Agent. Test redemption, replay/revoke, credential persistence under LocalService, restart and reconnect.

The payload and helper do not prove any physical Windows service or LAN behavior until the above steps have been executed and evidenced on the isolated test machines.
