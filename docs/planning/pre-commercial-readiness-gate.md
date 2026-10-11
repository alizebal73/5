# Pre-Windows and Pre-Commercial Readiness Gate — GameNet 5

Snapshot date: 2026-10-11. This is an execution gate, not a production-release certificate.

## Exact-SHA baseline

The latest code-bearing CI candidate in PR #31 is `security/runner-and-stage1-stabilization-v1`, head `7a8475bf862d0ecdf4cd70eb12361e0152dd4317`, targeting `integration/runtime-operator-v1` at `82d90178c0011c65eef76bd32cc33318d5bbd91c`. PR #31 is still Draft and unmerged.

| Candidate / gate | Exact GitHub run | Result |
|---|---|---|
| PR #31 head `7a8475bf...`: Quick Validation — PR | [#38138211044](https://github.com/alizebal73/5/actions/runs/38138211044) | Passed |
| PR #31 head `7a8475bf...`: Full Foundation | [#38138208159](https://github.com/alizebal73/5/actions/runs/38138208159) | Passed |
| PR #31 head `7a8475bf...`: Runtime payload (not Setup/MSI) | [#38138211061](https://github.com/alizebal73/5/actions/runs/38138211061) | Passed |
| Immediately preceding source candidate `d02192a43722efc1f49bf84fd24af911b3ece7e9`: Quick push | [#38137818156](https://github.com/alizebal73/5/actions/runs/38137818156) | Passed |
| Same preceding source candidate: Quick PR | [#38137821693](https://github.com/alizebal73/5/actions/runs/38137821693) | Passed |
| Same preceding source candidate: Full Foundation | [#38137818157](https://github.com/alizebal73/5/actions/runs/38137818157) | Passed |
| Same preceding source candidate: Runtime payload | [#38137821491](https://github.com/alizebal73/5/actions/runs/38137821491) | Passed |
| Same preceding source candidate: PostgreSQL diagnostic | [#38137818186](https://github.com/alizebal73/5/actions/runs/38137818186) | Passed |
| Separate Foundation maintenance PR #32, head `4fb20dc0...`: Full Foundation | [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082) | Passed |

The transition from `d02192a...` to `7a8475b...` was documentation-only. The last three applicable runs above prove the updated PR #31 candidate's PR gate, full Foundation gate, and runtime payload; the Quick push and PostgreSQL diagnostic runs are evidence from the immediately preceding source-identical candidate, not separate runs on `7a8475b...`.

Current references and governance:
- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, protected; unchanged.
- `foundation/runtime-final-v2`: `be29687e637b709158a30204bb9213bfa4813950`, protected and unchanged. Stable Checkpoint still certifies only the historical exact SHA `c3a8ba482548e963479fbdf8538be62c4d15acfe`; PR #32's successful check is attached to its review candidate and does not silently certify the base branch tip.
- `integration/runtime-operator-v1`: `82d90178c0011c65eef76bd32cc33318d5bbd91c`. Active Ruleset `protect-integration-runtime` (ID `24879997`) now targets this exact branch. Read-back confirms pull requests, conversation resolution, required `quick-validation`, no deletion/force-push, Squash-only merging and an empty bypass list.
- PR #32, `security/foundation-hosted-runner-isolation-v1`, remains Draft/unmerged; Full Foundation passed in run #38143687082. It proposes moving the Foundation workflow set to GitHub-hosted `windows-2025` and fixing the repository-local EF concurrency invocation; it has not changed the Foundation branch.

## Repository gates before real-Windows acceptance

- [x] Full hosted Foundation passed on PR #31's latest previously verified head; parser, canonical build/tests, disposable PostgreSQL migration/concurrency, backup/restore, Desktop and Agent runtime, and evidence upload were green.
- [x] Runtime payload was built and checksummed. It is a self-contained payload, not Setup.exe/MSI and not proof of installation.
- [x] Integration Ruleset was created and read back as active (ID `24879997`).
- [x] Full Foundation passed on the separate PR #32 review candidate (run #38143687082); the protected Foundation branch remains unchanged.
- [ ] Review the full diffs and decide the disposition/order of PR #31 and PR #32. They are still Draft and unmerged; do not auto-merge or treat either as a production release.
- [~] Personal Runner cleanup is deliberately deferred by the owner until GameNet development is complete. It remains registered and was shown as `Idle`; hosted candidate workflows use `windows-2025`, but this does not establish organization/runner-group restrictions for every older open branch. Avoid running third-party/untrusted code on the personal Runner wherever practical. Track eventual removal in [Issue #33](https://github.com/alizebal73/5/issues/33).

This gate does not claim that real Windows Service ACL/DPAPI, TLS private-key access under the installed service identity, bootstrap cleanup, Agent enrollment/replay/revocation, or physical LAN operation have passed. These are the next hands-on acceptance stage, followed by commercial slices only after the candidate baseline is reviewed and adopted.

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
