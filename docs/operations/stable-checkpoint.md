# Stable Checkpoint — GameNet 5 Foundation

> **Exact-SHA caveat — 2026-10-10:** this record names the last SHA for which this document holds explicit certification evidence; it does not certify every newer tip of foundation/runtime-final-v2. The observed branch tip is be29687e637b709158a30204bb9213bfa4813950, two commits ahead of this recorded SHA (workflow/marker/log/checkpoint changes). GitHub read-back for that tip was pending and no successful workflow run for that exact SHA was found. Re-run the required Foundation workflow on the exact tip before treating it as the current certified checkpoint. Current Runtime/operator PR evidence is recorded separately in the [Engineering Readiness Register](../planning/engineering-readiness-register.md).

Status: **CERTIFIED**

- Certified code commit: `c3a8ba482548e963479fbdf8538be62c4d15acfe`
- Branch tested: `foundation/runtime-final-v2`
- Certification workflow: [run 37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Evidence artifact: [foundation-certification-evidence](https://github.com/alizebal73/5/actions/runs/37860650826/artifacts/11585812517)
- Evidence timestamp (UTC): `2026-10-08T23:41:16.3207470Z`

## Verified gates

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
