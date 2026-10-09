# Runtime LAN Smoke Evidence — 2026-10-10

> **Status:** Manual bounded LAN smoke succeeded for basic Server/Agent transport and lease/heartbeat. This is **not** Setup certification, a production release, or proof that Windows Service deployment is ready.

## Executive summary (Persian)

در آزمون دستی، سرور GameNet 5 روی کامپیوتر مدیر 192.168.0.9 اجرا شد و Agent روی کلاینت آزمایشی 192.168.0.108 به آن وصل شد. HTTPS و گواهی اعتبارسنجی شدند، سلامت سرور Ready بود، توکن Agent با HTTP 200 صادر شد و رکورد Lease/Heartbeat در دیتابیس آزمایشی ثبت شد. Agent بعد از توقف و اجرای مجدد نیز دوباره احراز هویت شد و در یک اجرای تمیز بعدی Lease فعال و Heartbeat تازه داشت.

این نتیجه فقط تست دستی ارتباط است. داشبورد کامل نیست؛ پنجره Desktop فقط عنوان «مدیریت گیم‌نت ۵» و متن «مدیریت مرکزی گیم‌نت» را نشان می‌دهد. Agent به‌صورت فرایند تعاملی اجرا شد، نه Windows Service. نصب‌کنندهٔ نهایی نیز وجود ندارد.

## 1. Canonical topology

- **Manager / Server / Desktop / PostgreSQL:** 192.168.0.9
- **Test client / Agent:** 192.168.0.108
- Server endpoint: https://192.168.0.9:5080
- PostgreSQL stays on the manager. **Do not install PostgreSQL on client PCs.**
- The client uses the Server HTTPS API/SignalR endpoint; neither Desktop nor Agent connects directly to PostgreSQL.
- SMB file copy used \\192.168.0.108\D\GameNet5-RuntimeLanSmoke-verified.

## 2. Source / artifact identity

- Repository: https://github.com/alizebal73/5
- Runtime endpoint/TLS candidate: [PR #15](https://github.com/alizebal73/5/pull/15), draft and unmerged at time recorded.
- Candidate branch: fix/programdata-endpoints-v1
- PR head recorded by GitHub: d9544f46c4e0e13b072a946f040f3be539d193e0
- PR #15 records the tested merge checkout as 1348cd8238c69e756056e99ce29a152afb574b94 and the LAN payload workflow as [run 37982118515](https://github.com/alizebal73/5/actions/runs/37982118515).
- PR #15 records package SHA-256 a1d03d08c990f1ee34ebf93eb00306b18be965604c34333577d3235c334589a1. This is the CI-recorded package hash; the operator did **not** independently verify the downloaded ZIP against SHA256SUMS.txt during this session.
- Extracted working folders:
  - Manager: C:\Users\gamnet-98\Desktop\New folder (6)\GameNet5-RuntimeLanSmoke-verified
  - Client: D:\GameNet5-RuntimeLanSmoke-verified
- The extracted manifest.json was copied but its contents/all listed file hashes were not independently revalidated on the PC. Five selected items were compared source-to-destination with SHA-256 and matched: Server\GameNet.Server.exe, Desktop\GameNet.Manager.Desktop.exe, Agent\GameNet.Agent.exe, Tools\GameNet.Migrations.exe, manifest.json.
- README clearly labels the artifact **not Setup** and **not production-certified**.

## 3. PostgreSQL and disposable database

- Service observed on manager: postgresql-x64-17, Running, Automatic.
- Database: gamenet5_lan_smoke_20261010
- Test role: gamenet5_lan_smoke
- The role password was set using psql \password; ALTER ROLE gamenet5_lan_smoke LOGIN returned ALTER ROLE.
- A local psql query confirmed current_database = gamenet5_lan_smoke_20261010 and current_user = gamenet5_lan_smoke.
- Connection from the manager using 127.0.0.1:5432 succeeded.
- The client-to-manager PostgreSQL port test returned TcpTestSucceeded: False, which is acceptable for the intended architecture. No firewall rule was added to expose PostgreSQL to the client.
- A backup of C:\Program Files\PostgreSQL\17\data\pg_hba.conf was created at C:\Users\gamnet-98\Desktop\pg_hba.conf.gamenet5-lan-smoke.bak. A temporary rule for only 192.168.0.108/32 and the smoke database/role was briefly appended, then the file was restored from that backup and pg_reload_conf() returned t. No remote PostgreSQL access is needed for this test.
- Migration command targeted the disposable test database only. Output: Applying migration '20261008224001_FoundationCore'. Done.
- No migration was run against the existing business database.

## 4. HTTPS / certificate

- Target SANs: IP 192.168.0.9 and DNS gamenet.local; HTTPS port 5080.
- The first TLS-script attempt failed because the service-account argument was malformed. The script cleanup ran; follow-up checks found no server.json, no exported .cer at the target path, and no CN=gamenet.local certificate in LocalMachine\My.
- A subsequent run using the script's default service account succeeded:
  - Service account ACL requested: NT AUTHORITY\NETWORK SERVICE
  - Server config created: C:\ProgramData\GameNet Manager\Config\server.json
  - Public certificate: C:\Users\gamnet-98\Desktop\New folder (6)\GameNet5-RuntimeLanSmoke-verified\artifacts\gamenet-server.cer
  - Windows certificate-store SHA-1 thumbprint (Server lookup only): F8DD0A3E70A1BE3C1FCEC1104791F2BD0465ACD8
  - Public .cer SHA-256 fingerprint: 5F74776628C99BEA1D0D6425ACB03B7B8781DA6FFA670B7198D0CDF5DFD2BE58
- The public .cer was copied to the client at D:\GameNet5-RuntimeLanSmoke-verified\artifacts\gamenet-server.cer. Source and destination SHA-256 matched the fingerprint above.
- The client's trust script imported the verified public-only certificate into Cert:\LocalMachine\Root after a process-scoped PowerShell execution-policy bypass was used. No private key was copied.
- Client endpoint config was written to C:\ProgramData\GameNet Manager\Config\agent.json; Desktop endpoint config was written to C:\ProgramData\GameNet Manager\Config\desktop.json.
- **Important environment caveat:** the runtime README says the TLS script should be executed only on an isolated test Server PC. It was actually run on 192.168.0.9, the manager machine that already had C:\ProgramData\GameNet Manager\Server\Data and Backups. Therefore this session changed machine certificate stores and added Server/Desktop config under ProgramData on the manager. Do not characterize this as an isolated-PC test. These changes should be reviewed and, if needed, cleaned up only through a controlled plan while the smoke server is stopped; do not delete the certificate or config blindly while the process is using them.

## 5. Server execution and health

- Server executable: Server\GameNet.Server.exe from the extracted payload.
- It was run interactively in Production environment, not registered as a Windows service.
- Database connection and auth/provisioning configuration were supplied through process environment variables, not written into JSON config. Ephemeral signing/provisioning values were generated with RandomNumberGenerator.Create(); their values are intentionally **not** recorded here.
- An initial run failed Options validation because the attempted PowerShell static RandomNumberGenerator.GetBytes(32) call was unavailable in this Windows PowerShell environment. The error was corrected by creating an RNG instance and filling a 32-byte array; the next run started.
- Server startup log: Now listening on https://192.168.0.9:5080; Hosting environment: Production.
- GET https://192.168.0.9:5080/health from the manager returned service GameNet.Server, version 0.1.0-foundation, status Healthy, readiness Ready.
- A later Get-Process GameNet.Server showed PID 11952, started at approximately 2026-10-10 01:51:18 local time. This was a directly started process, not a Windows service.
- The current running state was not rechecked at the time this report was written. Do not assume the PID still exists without checking.

## 6. Client network and Agent results

### Network and normal TLS validation

- Test-NetConnection 192.168.0.108 -Port 445 from manager: TcpTestSucceeded True; SMB share D was visible.
- Test-NetConnection 192.168.0.9 -Port 5432 from client: TcpTestSucceeded False; no PostgreSQL exposure was added.
- Test-NetConnection 192.168.0.9 -Port 5080 from client: TcpTestSucceeded True.
- On client, test-runtime-endpoint.ps1 Agent returned:
  SERVER_ENDPOINT_OK component=Agent scheme=https host=192.168.0.9 port=5080 readiness=Ready version=0.1.0-foundation.
  This exercised normal Windows TLS validation (no certificate-validation bypass).

### Agent identity, authentication, lease and heartbeat

- Agent executable: D:\GameNet5-RuntimeLanSmoke-verified\Agent\GameNet.Agent.exe.
- Agent was launched as an interactive process, **not** installed/running as a Windows service.
- Device ID: e74bf006964d4d9da0913bdb73d477f8.
- Provisioning endpoint POST /api/v1/agent/credentials/provision returned credential ID 4ad2fb62-cd23-41f3-9896-d93be66d66d7. The returned secret is intentionally not recorded.
- A temporary handoff file agent-bootstrap.tmp was written into the client's test folder over SMB, read by client-side PowerShell, and deleted before launch. The Agent credential store protects the secret with Windows DPAPI using DataProtectionScope.CurrentUser. Because Agent was not run as a service, this does not prove the credential is usable under the intended service identity.
- Agent token request POST https://192.168.0.9:5080/api/v1/agent/auth/token returned HTTP 200.
- Database row observed after successful connection: device_id e74bf006964d4d9da0913bdb73d477f8, agent_version 1.0.0.0, station_state Ready, non-expired lease and recent last_heartbeat_at_utc.
- Agent was stopped and restarted as an interactive process. Token acquisition again returned HTTP 200; the database showed a later lease expiry and heartbeat timestamp.
- One Agent attempt after a restart logged “Server did not grant authoritative Agent lease”. It was a real transient failure and was not hidden. Agent processes were then stopped/closed; a clean launch had no repeated transport-cycle failure observed during an approximately 15-second observation window. A following database query returned lease_active = t, heartbeat_age_seconds = 1.7, and station_state = Ready.
- The result supports basic Agent authentication, authoritative lease acquisition, heartbeat and recovery after **process** restart for this smoke run. It does not prove full service-restart behavior, prolonged stability, or access to the TLS private key under Windows Service identity.

## 7. Desktop UI observation

- Desktop\GameNet.Manager.Desktop.exe was launched on 192.168.0.9.
- The window displayed Persian title «مدیریت گیم‌نت ۵» and welcome text «مدیریت مرکزی گیم‌نت».
- Source reviewed in src/Desktop/Shell/MainWindow.xaml is only a basic WPF window with a title and a welcome text block. No navigation/workspaces for Stations, Customers, Sessions, Billing, Wallet, Reports, Agents, Settings, etc. are implemented in this view yet.
- No actual business-dashboard workflow was tested. The Desktop opened and rendered its two current strings; its API-backed visual behavior was not independently evidenced by the UI itself.

## 8. Result matrix

| Gate | Result | Evidence / limitation |
|---|---|---|
| Disposable PostgreSQL database | PASS | Local psql identity query succeeded |
| Foundation schema migration | PASS | 20261008224001_FoundationCore applied; Done |
| Server HTTPS startup | PASS | Listener https://192.168.0.9:5080 |
| Server health/readiness | PASS | Healthy / Ready on version 0.1.0-foundation |
| Client TCP reachability to Server | PASS | TCP/5080 true |
| Normal TLS validation from client | PASS | Endpoint script returned SERVER_ENDPOINT_OK; public cert SHA-256 matched |
| Agent token authentication | PASS | HTTP 200 |
| Agent lease and heartbeat | PASS after one transient lease failure | Database row had active lease, recent heartbeat, Ready |
| Agent process restart | PASS for manual process restart | Token re-issued; later fresh heartbeat/lease observed |
| Desktop shell opens / Persian strings | PASS, minimal shell only | Title and welcome message visible |
| Full dashboard / business features | NOT IMPLEMENTED / NOT TESTED | Current MainWindow contains only title + welcome text |
| Windows Service registration | NOT IMPLEMENTED in package | Payload README explicitly says it does not register services |
| Agent under intended service identity | NOT TESTED | Interactive process only; DPAPI identity differs from service identity |
| Server restart + Agent reconnect | NOT TESTED as a complete gate | Server was stopped once with Ctrl+C and restarted manually as a process; no controlled end-to-end server-restart gate was recorded |
| TLS private-key access under actual Server service identity | NOT TESTED | Server ran interactively |
| ZIP checksum against SHA256SUMS.txt / all manifest files | NOT VERIFIED on the PCs | Only five selected source/destination file hashes compared |
| Setup / update / repair / rollback / release certification | NOT IMPLEMENTED / NOT TESTED | Separate later release gate |

## 9. Safety and current-state notes

- The disposable DB is gamenet5_lan_smoke_20261010; do not repoint the payload to the live GameNet/business database.
- PostgreSQL was kept local to the manager. No firewall rule was added to expose port 5432.
- No passwords, JWT signing key, Agent provisioning key, or Agent secret is included in this report.
- Some keys and the DB connection were held in PowerShell process environment during the interactive smoke run. A child Server process inherits an environment snapshot; stop it deliberately when the test is finished, rather than assuming the keys vanish when a parent PowerShell window closes.
- The public TLS certificate/config created on the manager must be assessed in context of any pre-existing GameNet installation. Do not remove certificate/config while the server is running; plan a controlled teardown if needed.
- The temporary handoff file was explicitly removed during client bootstrap.
- This was a manual LAN smoke of a temporary payload, not a supported install. No production-readiness claim is warranted.

## 10. Recommended next sequence

1. Keep this evidence attached to the runtime/TLS candidate; do not merge it into main or foundation/runtime-final-v2 without the repository's review/certification process.
2. Define and approve the canonical secret lifecycle and Windows service identities before any service-based deployment test. Cover PostgreSQL credentials, the JWT signing key and Agent provisioning key: protected storage, least-privilege ACLs for the real service identities, redaction, rotation and recovery. Do not put secrets in server.json, source control, plaintext logs or ordinary installer command lines.
3. Review the current state of the manager machine before cleanup. This smoke created Server TLS/configuration artifacts on 192.168.0.9 even though the payload README recommends an isolated test Server PC. Do not rerun the TLS setup script or remove its certificate/configuration blindly; first determine whether the test Server process/service is still using them and whether they overlap the existing installation.
4. Complete the remaining physical runtime gate in a controlled test window with a disposable database and throwaway secrets: verify the actual Server Windows service identity can access its TLS private key; run Agent under its intended service identity with protected credentials; verify lease/heartbeat and reconnect after service restart; and record the exact tested commit/package hash, endpoint IP/SAN, public-certificate SHA-256, OS/PostgreSQL versions and results. Do not treat the previous interactive-process smoke as this proof.
5. Only after the Stage 1 runtime boundary is accepted, follow the master build plan in order: Stage 2 module skeleton, Stage 3 identity/authorization, Stage 4 stations/agents, then Stage 5 customers. The real dashboard remains native WPF per docs/ui/desktop-ux-architecture.md; do not start feature work out of sequence.
6. Implement and certify Setup, update, repair and rollback separately at Stages 15–18 / Gate E. The current ZIP is a runtime test payload, not an installer.

## References

- [PR #15 — Runtime endpoint / TLS configuration and LAN payload](https://github.com/alizebal73/5/pull/15)
- [CI Runtime LAN Smoke payload run 37982118515](https://github.com/alizebal73/5/actions/runs/37982118515)
- [Agent/Server runtime endpoint procedure](https://github.com/alizebal73/5/blob/fix/programdata-endpoints-v1/docs/operations/runtime-endpoints.md)
- [Desktop UX architecture](https://github.com/alizebal73/5/blob/fix/programdata-endpoints-v1/docs/ui/desktop-ux-architecture.md)
- [Issue #16 — Setup/update/recovery and release certification](https://github.com/alizebal73/5/issues/16)
