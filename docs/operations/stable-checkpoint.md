# Stable Checkpoint — GameNet 5 Foundation

> **Exact-SHA caveat — 2026-10-11:** this document certifies only commit c3a8ba482548e963479fbdf8538be62c4d15acfe by its linked run. The current observed foundation/runtime-final-v2 tip is be29687e637b709158a30204bb9213bfa4813950; no successful run on that exact tip is established here. Do not treat the newer tip as certified until its exact run succeeds. Current Integration/CI work is separate and tracked in the Engineering Readiness Register.

Status: **CERTIFIED — historical exact SHA only; not certification of the current Foundation tip**

- Certified code commit: `c3a8ba482548e963479fbdf8538be62c4d15acfe`
- Branch tested: `foundation/runtime-final-v2`
- Certification workflow: [run 37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Evidence artifact: [foundation-certification-evidence](https://github.com/alizebal73/5/actions/runs/37860650826/artifacts/11585812517)
- Evidence timestamp (UTC): `2026-10-08T23:41:16.3207470Z`

## Current Integration CI candidate — 2026-10-11 (not a Foundation stable checkpoint)

PR #31 remains Draft/unmerged and targets Integration. Its last code-bearing source baseline e5caceb11968512fb91b5f808cbd4afd24be22ad

PR #32 is a separate Foundation workflow maintenance candidate at 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9; Full Foundation passed in [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It remains Draft/unmerged and does not certify the Foundation base SHA. Ruleset protect-integration-runtime ID 24879997 is active.

The historical Stable Checkpoint remains exactly c3a8ba482548e963479fbdf8538be62c4d15acfe; no new Foundation checkpoint is declared until a successful run certifies the exact resulting Foundation tip after reviewed maintenance.

## Current candidate gates

| Candidate | Gate | Exact run | Result |
|---|---|---|---|
| PR #31 head e5caceb... | Quick push | [#38146060096](https://github.com/alizebal73/5/actions/runs/38146060096) | Passed |
| PR #31 head e5caceb... | Quick PR | [#38146064180](https://github.com/alizebal73/5/actions/runs/38146064180) | Passed |
| PR #31 head e5caceb... | Full Foundation | [#38146060115](https://github.com/alizebal73/5/actions/runs/38146060115) | Passed |
| PR #31 head e5caceb... | Runtime payload push | [#38146060239](https://github.com/alizebal73/5/actions/runs/38146060239) | Passed |
| PR #31 head e5caceb... | Runtime payload PR | [#38146064208](https://github.com/alizebal73/5/actions/runs/38146064208) | Passed |
| PR #32 head 4fb20dc... | Full Foundation | [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082) | Passed |

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
