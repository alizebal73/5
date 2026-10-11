# Stable Checkpoint — GameNet 5 Foundation

> **Exact-SHA caveat — 2026-10-11:** this document certifies only commit c3a8ba482548e963479fbdf8538be62c4d15acfe by its linked run. The current observed foundation/runtime-final-v2 tip is be29687e637b709158a30204bb9213bfa4813950; no successful run on that exact tip is established here. Do not treat the newer tip as certified until its exact run succeeds. Current Integration/CI work is separate and tracked in the Engineering Readiness Register.

Status: **CERTIFIED — historical exact SHA only; not certification of the current Foundation tip**

- Certified code commit: `c3a8ba482548e963479fbdf8538be62c4d15acfe`
- Branch tested: `foundation/runtime-final-v2`
- Certification workflow: [run 37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Evidence artifact: [foundation-certification-evidence](https://github.com/alizebal73/5/actions/runs/37860650826/artifacts/11585812517)
- Evidence timestamp (UTC): `2026-10-08T23:41:16.3207470Z`

## Current Integration CI candidate — 2026-10-11 (not a Foundation stable checkpoint)

PR #31 remains Draft/unmerged. Latest candidate SHA `d02192a43722efc1f49bf84fd24af911b3ece7e9` passed Quick push [#38137818156](https://github.com/alizebal73/5/actions/runs/38137818156), Quick PR [#38137821693](https://github.com/alizebal73/5/actions/runs/38137821693), Full Foundation [#38137818157](https://github.com/alizebal73/5/actions/runs/38137818157), Runtime Payload [#38137821491](https://github.com/alizebal73/5/actions/runs/38137821491) and PostgreSQL Diagnostic [#38137818186](https://github.com/alizebal73/5/actions/runs/38137818186); all passed on the same exact SHA. This is an Integration/CI candidate, not a new Foundation stable checkpoint. Hosted CI does not prove installed Windows Service identity, effective ACL/DPAPI after restart, TLS private-key access under the real service, or physical two-PC LAN behavior.

## Current candidate gates (PR #31 head only; not the historical Foundation certificate)

| Gate | Exact run | Result |
|---|---|---|
| Quick push | [#38137818156](https://github.com/alizebal73/5/actions/runs/38137818156) | Passed |
| Quick PR | [#38137821693](https://github.com/alizebal73/5/actions/runs/38137821693) | Passed |
| Full Foundation | [#38137818157](https://github.com/alizebal73/5/actions/runs/38137818157) | Passed |
| Runtime Payload | [#38137821491](https://github.com/alizebal73/5/actions/runs/38137821491) | Passed |
| PostgreSQL Diagnostic | [#38137818186](https://github.com/alizebal73/5/actions/runs/38137818186) | Passed |

## Historical certified Foundation gates

| Gate | Result |
|---|---|
| PowerShell parser | Passed |
| Architecture, business, placeholder and source-size guards | Passed |
| Release restore/build/unit and contract tests | Passed |
| Clean PostgreSQL migration, schema and migration concurrency | Passed |
| Backup and isolated restore | Passed |
| Desktop → Server smoke, `fa-IR` and `en-US` | Passed |
| Agent credential bootstrap and JWT authentication | Passed |
| Authoritative Agent lease and heartbeat | Passed |
| Agent connection fencing, lease release and reconnect | Passed |

The workflow finished with conclusion `success` on the exact certified commit above. The separate installer, updater, rollback and production-release checks are **not** certified by this checkpoint and remain release gates.

## Safe next steps

1. Keep `main` unchanged for now.
2. Use this certified commit as the base for rebuilding/rebasing the business slices. Do not merge the legacy branches as-is.
3. Start with operator identity/login and Stations + the real Agent heartbeat/status path.
4. Require regression tests and a green runtime lane for each follow-on slice. Any foundation/runtime/security change invalidates the checkpoint until re-certified.
