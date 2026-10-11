# Stable Checkpoints

A checkpoint is stable only when restore/build/tests, architecture guards, PostgreSQL certification, Desktop/Agent smoke, recovery evidence and exact-commit evidence are green on the named SHA.

## Last recorded Foundation certification

- Certified exact code commit: c3a8ba482548e963479fbdf8538be62c4d15acfe
- Run: [Foundation certification #37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Current observed Foundation tip: be29687e637b709158a30204bb9213bfa4813950. It is newer than the certified SHA and needs a successful Foundation run on that exact tip before it is called the current certified checkpoint.
- The historical certificate is never silently upgraded.

## Active Integration / CI candidate — separate from Stable Foundation

- Draft, unmerged review: [PR #31](https://github.com/alizebal73/5/pull/31), targeting integration/runtime-operator-v1.
- Latest exact-SHA candidate `53743eda950c057d435fc082994bc183e7cd723b` passed Quick push [#38135640632](https://github.com/alizebal73/5/actions/runs/38135640632), Quick PR [#38135643357](https://github.com/alizebal73/5/actions/runs/38135643357), Full Foundation [#38135640645](https://github.com/alizebal73/5/actions/runs/38135640645), Runtime Payload [#38135643366](https://github.com/alizebal73/5/actions/runs/38135643366) and PostgreSQL Diagnostic [#38135640573](https://github.com/alizebal73/5/actions/runs/38135640573); all five passed on the same SHA.
- Earlier candidates `991152b...`, `3fef98d...` and `77507f4...` remain historical evidence and do not supersede the latest exact-SHA result.
- integration/runtime-operator-v1 currently has no active Ruleset. Green CI does not make the branch protected.
- The local self-hosted Runner is retained and not selected by current workflow jobs; GitHub-side Runner access policy remains open. This candidate is not an installer, Production release or physical-LAN certification.

## Policy

Keep known-good baselines separate from development. Record exact commit and run for every certification. Documentation-only follow-ups must state the exact code SHA whose tests they reference. Hosted CI is not proof of installed Windows Service identity or physical LAN behavior.
