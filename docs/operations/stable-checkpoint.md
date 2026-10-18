# Stable Checkpoint — GameNet 5 Foundation

> **Latest exact-tip certification — 2026-10-12:** Foundation branch SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` passed Full Foundation on exact SHA in [run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053); uploaded evidence: [artifact 11718566832](https://github.com/alizebal73/5/actions/runs/38182502053). This run included runner-policy, PowerShell parser, Canonical verification, disposable PostgreSQL migration/concurrency, backup/isolated restore, Desktop runtime, Agent runtime and evidence upload. The historic certificate `c3a8ba...` below remains recorded as prior evidence, not the latest checkpoint. Current Runtime/operator blockers and execution order are tracked in [Active Execution Control](active-execution-control.md); per-change evidence remains in the matching PR and Actions run.

Status: **CERTIFIED**

- Certified code commit: `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`
- Branch tested: `foundation/runtime-final-v2`
- Certification workflow: [run 38182502053](https://github.com/alizebal73/5/actions/runs/38182502053)
- Evidence artifact: [foundation-certification-evidence](https://github.com/alizebal73/5/actions/runs/38182502053)
- Previous certified commit: `c3a8ba482548e963479fbdf8538be62c4d15acfe` — [historical run 37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)

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
