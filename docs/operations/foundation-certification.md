# Foundation Certification

Foundation is not certified by file existence or green unit tests alone.

## Current status and scope boundary — 2026-10-12

**Gate A / Master Plan Stage 1 is CLOSED** on Foundation SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`, validated by [Full Foundation #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053). The integrated Runtime/Operator code also passed Quick and Full Foundation on its recorded exact code SHA `58717adc6c1553ef21221c3d484f093494ba1ae7`, and the later hosted-runner Integration merge SHA `161b4cbbbe16cd3fd4046f174a1c09380139c67b` passed Quick #38183458461 and Full Foundation #38183458486.

The automated gate uses hosted Windows and disposable PostgreSQL for build/tests, migration/concurrency, backup/restore, Desktop runtime and Agent runtime proof. These exact-SHA results satisfy the automated Foundation exit gate; they do not claim that an installed production Windows Service, effective OS ACLs, DPAPI/TLS private-key access after restart, physical two-PC LAN or interactive WPF acceptance has passed.

Required evidence before the first business vertical slice:
- clean Windows restore/build/test;
- architecture/source-size/placeholder/business guards;
- native Desktop launch and typed Server boundary;
- real PostgreSQL clean migration and concurrency lane;
- durable Audit, Idempotency and Outbox persistence;
- authenticated Agent identity, credential, connection lease, heartbeat, reconnect and fencing proof;
- Desktop -> Server real smoke;
- backup and isolated restore smoke;
- exact commit evidence recorded in the stable-checkpoint document.

Installer, updater and rollback are release gates, not prerequisites for starting business modules. They remain mandatory before a production release.

**Business implementation is unblocked by the closed Foundation gate.** Continue the Master Build Plan product stages in order and keep automated CI/unit/integration/regression/security tests green for each changed SHA. Do not block product modules on physical-machine acceptance: actual Windows Service identity/ACL/DPAPI/TLS, physical two-PC LAN, manual WPF interaction, and installation/recovery are explicitly deferred to final acceptance after the planned product scope and Setup.exe/MSI candidate are complete. This scheduling decision does not weaken the automated checks or reopen Stage 1.


## Additional Foundation audit requirements

- Server readiness must require both PostgreSQL connectivity and zero pending EF migrations.
- Correlation IDs must fit the durable Audit storage contract.
- Agent credential provisioning/rotation/revocation failures must use stable shared error envelopes, never exception-message parsing.
- Agent credential persistence must have automated Protect/Unprotect symmetry and access-control regression coverage during development. Proof under the actual installed Windows Service identity, effective ACLs and restart is a final acceptance requirement after the Setup candidate exists.
- Foundation certification must include a zero-state PostgreSQL migration lane in addition to the dedicated database concurrency lane; the recorded Full Foundation gate covers the disposable-database automated lane for its exact SHA.
