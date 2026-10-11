# Pre-Windows and Pre-Commercial Readiness Gate — GameNet 5

Snapshot date: 2026-10-11. This is an execution gate, not a production-release certificate.

## Exact-SHA baseline

Current PR #31 candidate: branch `security/runner-and-stage1-stabilization-v1`, exact head `20ba74902fe620f00c58d047427c9f7d12c890f0`, targeting `integration/runtime-operator-v1` at `82d90178c0011c65eef76bd32cc33318d5bbd91c`. PR #31 remains Draft/open/unmerged.

| Gate on exact head `20ba74902fe620f00c58d047427c9f7d12c890f0` | Exact GitHub run | Result |
|---|---|---|
| Quick Validation — push | [#38144956137](https://github.com/alizebal73/5/actions/runs/38144956137) | Passed |
| Quick Validation — pull request | [#38144959579](https://github.com/alizebal73/5/actions/runs/38144959579) | Passed |
| Full Foundation — hosted Windows | [#38144956092](https://github.com/alizebal73/5/actions/runs/38144956092) | Passed |
| Runtime payload — push (not Setup/MSI) | [#38144956122](https://github.com/alizebal73/5/actions/runs/38144956122) | Passed |
| Runtime payload — pull request | [#38144959504](https://github.com/alizebal73/5/actions/runs/38144959504) | Passed |

The hosted gates cover PowerShell parser/canonical build and tests, disposable PostgreSQL migration/concurrency, backup/isolated restore, Desktop and Agent runtime, exact checkout verification, package publication, payload hashes and validation-only checks for the test-service helper. These are hosted tests, not physical Windows Service / LAN acceptance.

Current references and governance:
- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, protected and unchanged.
- `foundation/runtime-final-v2`: `be29687e637b709158a30204bb9213bfa4813950`, protected and unchanged. Stable Checkpoint remains tied to historical exact SHA `c3a8ba482548e963479fbdf8538be62c4d15acfe`; PR #32's candidate run does not silently certify the base tip.
- `integration/runtime-operator-v1`: `82d90178c0011c65eef76bd32cc33318d5bbd91c`, protected by active Ruleset `protect-integration-runtime` (ID `24879997`) requiring PR, conversation resolution and `quick-validation`, blocking force-push/deletion, Squash-only, with no bypass actor.
- PR #32: separate Foundation workflow maintenance, current head `4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9`, Draft/unmerged; Full Foundation passed in [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It adds push-on-`foundation/runtime-*` recertification so the exact resulting Foundation tip can be checked after merge. Foundation is not changed by PR #32 yet.
- PR #15 remains a separate older TLS/ProgramData branch on Foundation and is not the selected commercial/test baseline. Its own CI is evidence for its exact older branch only.
- The personal self-hosted Runner remains registered by owner decision until GameNet is complete; this is tracked in [Issue #33](https://github.com/alizebal73/5/issues/33). Runner-group restrictions are not certified as enforced. Candidate workflows #31/#32 use GitHub-hosted `windows-2025`; don't generalize this to every old/open branch.

## Repository gates before real-Windows acceptance

- [x] Quick Validation passed on the exact PR #31 head, both push and PR events.
- [x] Full Foundation passed on the exact PR #31 head; parser, canonical build/tests, disposable PostgreSQL migration/concurrency, backup/restore, Desktop/Agent runtime and evidence upload succeeded.
- [x] Runtime payload build passed on the exact PR #31 head from both push and PR events; package manifest and SHA-256 were emitted.
- [x] Integration Ruleset created and read back as active (ID `24879997`).
- [x] Test-only Server/Agent service registration helper is included in the payload; hosted build runs it with `-ValidateOnly`, and the helper checks the manifest hashes and role-specific service configuration without querying/changing services.
- [ ] Review the complete PR #31 diff and decide its disposition; keep it Draft/unmerged until reviewed.
- [~] Personal Runner cleanup is intentionally deferred per owner decision; remove it after GameNet completion and read back the result. Runner-group access is not claimed as verified.

## Required physical acceptance protocol

The portable payload now includes scripts/register-test-runtime-service.ps1 and a dedicated runbook. The test-only helper verifies the package manifest and SHA-256 of its own file and the selected executable, refuses known existing GameNet services/state, registers only a Manual/Demand Start Server or Agent service with the role-specific account and Service SID, and never starts it. CI validates this package contract; that does not count as physical service acceptance.

Run only on isolated test Windows machines with a disposable PostgreSQL database and throwaway credentials. Never test against a live Manager database.

1. Run `scripts/inspect-manager-runtime-state.ps1` read-only on the intended Manager machine and review its report before provisioning or cleanup.
2. From the verified payload, register Server and Agent on separate test PCs with Tools\\register-test-runtime-service.ps1, passing -TestOnly -PreflightReviewed only after reviewing the read-only preflight. Confirm the registration output shows Manual/Demand Start and Stopped. Record exact source SHA, payload SHA-256, Windows build, PostgreSQL version, service accounts and Service SIDs, endpoint, port and public-certificate fingerprint.
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
