# Pre-Windows and Pre-Commercial Readiness Gate — GameNet 5

Snapshot date: 2026-10-11. This is an execution gate, not a production-release certificate.

## Exact-SHA baseline

Current PR #31 candidate: branch security/runner-and-stage1-stabilization-v1, exact head e5caceb11968512fb91b5f808cbd4afd24be22ad, targeting integration/runtime-operator-v1 at 82d90178c0011c65eef76bd32cc33318d5bbd91c. PR #31 remains Draft/open/unmerged.

| Gate on exact head e5caceb11968512fb91b5f808cbd4afd24be22ad | Exact GitHub run | Result |
|---|---|---|
| Quick Validation — push | [#38146060096](https://github.com/alizebal73/5/actions/runs/38146060096) | Passed |
| Quick Validation — pull request | [#38146064180](https://github.com/alizebal73/5/actions/runs/38146064180) | Passed |
| Full Foundation — hosted Windows | [#38146060115](https://github.com/alizebal73/5/actions/runs/38146060115) | Passed |
| Runtime payload — push (not Setup/MSI) | [#38146060239](https://github.com/alizebal73/5/actions/runs/38146060239) | Passed |
| Runtime payload — pull request | [#38146064208](https://github.com/alizebal73/5/actions/runs/38146064208) | Passed |

All five runs above identify the same exact candidate SHA. Full Foundation covers the PowerShell parser/canonical build and tests, disposable PostgreSQL migration/concurrency, backup/isolated restore, Desktop and Agent runtime, and evidence upload. The payload job also validates the test-only service helper using -ValidateOnly and builds a package-specific runtime endpoint guide with commands under Tools. These are hosted tests; they do not certify an installed Windows Service, effective ACL/DPAPI under the actual service identity, or physical LAN operation.

Current references and governance:
- main: b9288471f2047570eaf8d0d6552cf87bc0ddc214, protected and unchanged.
- foundation/runtime-final-v2: be29687e637b709158a30204bb9213bfa4813950, protected and unchanged. Stable Checkpoint remains tied to historical exact SHA c3a8ba482548e963479fbdf8538be62c4d15acfe; PR #32's candidate run does not silently certify the Foundation base tip.
- integration/runtime-operator-v1: 82d90178c0011c65eef76bd32cc33318d5bbd91c, now protected by active Ruleset protect-integration-runtime (ID 24879997), requiring PR, conversation resolution and quick-validation, blocking force-push/deletion, Squash-only, with no bypass actor.
- PR #32 is separate Foundation workflow maintenance, head 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9, Draft/unmerged; Full Foundation passed [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It adds a post-merge exact-tip certification trigger; Foundation itself has not been changed.
- PR #15 is an older TLS/ProgramData candidate based on Foundation, head 15402b2758808327874c18e21753120ef9cfccce, Draft/unmerged; its own Quick, Full Foundation and payload runs passed on that exact SHA. Do not merge #15/#31/#32 without reviewing their overlap and choosing an explicit order.
- The personal self-hosted Runner remains registered by owner decision until GameNet completion. Eventual removal is tracked in [Issue #33](https://github.com/alizebal73/5/issues/33); runner-group restrictions are not claimed as enforced, and old branches may still contain self-hosted declarations.

## Repository gates before real-Windows acceptance

- [x] Quick Validation passed on the exact PR #31 head, both push and PR events.
- [x] Full Foundation passed on the exact PR #31 head.
- [x] Runtime payload was built on the exact PR #31 head from both push and PR events; a manifest and SHA-256 checksum were emitted.
- [x] Integration Ruleset was created and read back as active (ID 24879997).
- [x] Test-only Server/Agent service registration is in the payload; CI validates role-specific manifest/executable/helper hashes using -ValidateOnly, without querying or changing Windows services.
- [ ] Review the complete PR #31 diff and decide its disposition; keep it Draft/unmerged until review and cross-PR ordering are settled.
- [~] Personal Runner cleanup is deliberately deferred per owner decision; remove it after GameNet completion and verify it no longer appears in repository/organization Runner settings.

## Required physical acceptance protocol

The portable payload includes a hash-verified, test-only service registration helper and a dedicated runbook. CI executes the helper only with -ValidateOnly, which must not query or change Windows services. Its automated state check covers known services/markers, not every possible file under ProgramData; the runbook therefore requires an operator hard stop whenever %ProgramData%\GameNet Manager exists at all, including an empty directory. This is not a service installer or physical acceptance.

Run only on isolated test Windows machines with a disposable PostgreSQL database and throwaway credentials. Never test against a live Manager database.

1. On each isolated test PC, run Tools/inspect-manager-runtime-state.ps1 read-only and review the report. If %ProgramData%\GameNet Manager exists at all (even if empty), or any unexpected service, configuration, protected file, certificate, ACL or listener is present, stop; do not register a service and use a fresh dedicated test PC.
2. On the two isolated PCs, register only the matching test service using Tools\register-test-runtime-service.ps1 -Component Server -PayloadRoot $PWD -TestOnly -PreflightReviewed or -Component Agent. Verify the output shows Manual/Demand Start and Stopped. Do not register a second copy or replace an existing service.
3. Prove effective DACLs for ProgramData, secret files, Agent state and TLS private-key file. Verify unauthorized local principals cannot read/write protected paths and the intended Server/Agent service identities can access only the files they need.
4. Record exact source SHA, payload SHA-256, Windows build, PostgreSQL version, Server/Agent service accounts and Service SIDs, endpoint, port and public-certificate fingerprint.
5. Configure TLS on the isolated Server using the dedicated NT SERVICE\GameNet 5 Server identity. Provision the Server secret store once; start the stopped service only after protected settings, secrets, TLS access and the disposable database are ready. Prove DPAPI loading after restart and fail-closed behavior for missing/corrupt/ACL-invalid state.
6. Complete Owner bootstrap/finalization; prove the temporary bootstrap secret is absent afterward and the service remains ready after a controlled restart.
7. Enroll the Agent over normally validated HTTPS. Test first redemption, expiry, replay, revocation, duplicate/racing redemption, credential persistence under the actual Agent service identity and reconnect after Agent/Server restarts.
8. Test lease expiry/fencing, stale-command rejection and recovery after network interruption. Do not claim full Agent command capability from a health-probe-only path.
9. Review logs/artifacts for credential, token, connection-string and private-key leakage. Record every pass/fail/blocked item with evidence.

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
