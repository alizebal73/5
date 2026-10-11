# Stable Checkpoints

A checkpoint is stable only when restore/build/tests, architecture guards, PostgreSQL certification, Desktop/Agent smoke, recovery evidence and exact-commit evidence are green on the named SHA.

## Last recorded Foundation certification

- Certified exact code commit: c3a8ba482548e963479fbdf8538be62c4d15acfe
- Run: [Foundation certification #37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Current observed Foundation tip: be29687e637b709158a30204bb9213bfa4813950. It is newer than the certified SHA and needs a successful Foundation run on that exact tip before it is called the current certified checkpoint.
- The historical certificate is never silently upgraded.

## Active Integration / CI candidate — separate from Stable Foundation

- Draft, unmerged review: [PR #31](https://github.com/alizebal73/5/pull/31), targeting integration/runtime-operator-v1.
- Candidate SHA 991152bf60676b2a03c0625780b5dcc16f365032 passed [Quick](https://github.com/alizebal73/5/actions/runs/38131788387), [Full Foundation](https://github.com/alizebal73/5/actions/runs/38131788388) and [Runtime Payload](https://github.com/alizebal73/5/actions/runs/38131788408). Docs-only SHA 3fef98dd40cfd87da6dba6450824baff366bdf24 passed Quick #38132197464 and Payload #38132197411.
- Isolation SHA 77507f4e208efc4cceadeb3c08d34eea01a69660 passed [Quick #38134009251](https://github.com/alizebal73/5/actions/runs/38134009251), [Full Foundation #38134005849](https://github.com/alizebal73/5/actions/runs/38134005849) and [Runtime Payload #38134009263](https://github.com/alizebal73/5/actions/runs/38134009263).
- integration/runtime-operator-v1 currently has no active Ruleset. Green CI does not make the branch protected.
- The local self-hosted Runner is retained and not selected by current workflow jobs; GitHub-side Runner access policy remains open. This candidate is not an installer, Production release or physical-LAN certification.

## Policy

Keep known-good baselines separate from development. Record exact commit and run for every certification. Documentation-only follow-ups must state the exact code SHA whose tests they reference. Hosted CI is not proof of installed Windows Service identity or physical LAN behavior.
