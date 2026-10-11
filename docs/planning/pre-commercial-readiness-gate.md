# Pre-Windows and Pre-Commercial Readiness Gate — GameNet 5

Snapshot date: 2026-10-11. This is an execution gate, not a production-release certificate.

## Exact-SHA baseline

The current hardening candidate is PR #31, branch `security/runner-and-stage1-stabilization-v1`, SHA `d02192a43722efc1f49bf84fd24af911b3ece7e9`. The PR targets `integration/runtime-operator-v1`, whose branch tip remains `82d90178c0011c65eef76bd32cc33318d5bbd91c`. The candidate is 14 commits ahead and 0 behind that base, but it is still Draft and unmerged.

| Gate | Exact-SHA run | Result |
|---|---|---|
| Quick Validation — push | [#38137818156](https://github.com/alizebal73/5/actions/runs/38137818156) | Passed |
| Quick Validation — pull request | [#38137821693](https://github.com/alizebal73/5/actions/runs/38137821693) | Passed |
| Full Foundation | [#38137818157](https://github.com/alizebal73/5/actions/runs/38137818157) | Passed |
| Runtime payload build (not Setup/MSI) | [#38137821491](https://github.com/alizebal73/5/actions/runs/38137821491) | Passed |
| PostgreSQL diagnostic lane | [#38137818186](https://github.com/alizebal73/5/actions/runs/38137818186) | Passed |

All five runs above identify the same candidate SHA. This proves hosted automated gates for that tree; it does not prove real Windows Service ACL/DPAPI behavior or physical LAN operation.

Current references:
- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, protected; do not modify in this work.
- `foundation/runtime-final-v2`: `be29687e637b709158a30204bb9213bfa4813950`, protected. Stable Checkpoint still certifies only `c3a8ba482548e963479fbdf8538be62c4d15acfe`; do not describe the newer Foundation tip as independently certified without its own exact-tip run.
- `integration/runtime-operator-v1`: `82d90178c0011c65eef76bd32cc33318d5bbd91c`, not protected by an active Ruleset.

## Repository gates before real-Windows acceptance

- [x] Quick push and PR validation passed on the exact candidate SHA.
- [x] Full Foundation passed on the exact candidate SHA: parser, canonical build/tests, temporary PostgreSQL migration/concurrency, backup/isolated restore, Desktop and Agent runtime, and evidence upload.
- [x] Runtime payload was built on the exact candidate SHA. It is a self-contained payload, not a Setup.exe/MSI and not proof of installation.
- [x] Repository workflows use GitHub-hosted `windows-2025`; the personal self-hosted Runner is retained and no current workflow selects it.
- [ ] Create/read back an active Ruleset for `integration/runtime-operator-v1`, requiring pull requests, the observed Quick Validation check and conversation resolution.
- [ ] Verify/restrict GitHub-side Runner/repository/runner-group access. Until confirmed, keep the personal self-hosted Runner offline while untrusted PRs are evaluated.
- [ ] Review the complete PR #31 diff and settle its disposition only after the two GitHub-side controls above are confirmed. Do not auto-merge.

The current GitHub connection can read Rulesets but does not provide administrative Ruleset or runner-group writes. Those unchecked items require a repository administrator in GitHub Settings; a repository code change cannot complete them.

## Required physical acceptance protocol

Run only on isolated test Windows machines with a disposable PostgreSQL database and throwaway credentials. Never test against a live Manager database.

1. Run `scripts/inspect-manager-runtime-state.ps1` read-only on the intended Manager machine and review its report before provisioning or cleanup.
2. Record exact source SHA, payload SHA-256, Windows build, PostgreSQL version, Server/Agent service accounts and Service SIDs, endpoint, port and public-certificate fingerprint.
3. Install/register Server and Agent as intended Windows services; prove effective file/directory DACLs and that unauthorized principals cannot read/write protected files.
4. Provision the Server secret store once; restart the actual service and prove DPAPI loading, TLS private-key access, HTTPS readiness and fail-closed behavior for missing/corrupt/ACL-invalid state.
5. Complete Owner bootstrap and finalization; prove the temporary bootstrap secret is absent afterward and the service remains ready after restart.
6. Enroll an Agent over normally validated HTTPS. Test first redemption, expiry, replay, revocation, duplicate/racing redemption, credential persistence under the actual Agent service identity and reconnect after Agent/Server restarts.
7. Test lease expiry/fencing, stale-command rejection and recovery after network interruption. Do not claim full Agent command capability from a health-probe-only path.
8. Review logs/artifacts for credential, token, connection-string and private-key leakage. Record every pass/fail/blocked item with evidence.

## Gate before the first commercial vertical slice

Commercial implementation remains blocked until the physical runtime acceptance above passes and the candidate baseline is governed/reviewed. Once admitted, each slice must satisfy Definition of Ready/Done and map REQ IDs to:
- authoritative data owner and explicit state transitions/invariants;
- Server-side permission and any required approval;
- reviewed migration, transaction boundary, idempotency and concurrency rules;
- audit event, failure/recovery/reconciliation and typed error behavior;
- unit/contract/PostgreSQL integration/concurrency/UI/runtime tests as applicable;
- exact tested SHA, run URLs and unresolved-risk disposition.

### Outbox maturity boundary

The current Outbox provides durable append plus leased batch claim and a guarded published marker. It does **not** currently provide a hosted delivery worker, event-type routing/consumer, retry/backoff schedule, final-failure/dead-letter policy, delivery observability or consumer idempotency proof. Do not call the current boundary “end-to-end event delivery.” Before a slice needs asynchronous/external delivery—especially Session-to-Agent command dispatch—the worker, retry/final-failure contract, idempotent consumer and crash/race/recovery tests must be part of that slice's DoR and pass before that slice is declared complete. Do not invent a no-op publisher to make the gate appear green.

## Release-only gates (not silently pulled into Foundation)

Setup.exe/MSI, install/repair/upgrade/uninstall, staged updater, rollback policy, signed artifacts, SBOM/license/dependency review, backup retention/RPO/RTO and a 2–3-PC then ~15-station pilot remain later release gates. Their absence does not change the green hosted Foundation result, but it prevents a production-readiness claim.
