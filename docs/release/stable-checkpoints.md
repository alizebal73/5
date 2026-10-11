# Stable Checkpoints

A checkpoint is stable only when restore/build/tests, architecture guards, PostgreSQL certification, Desktop/Agent smoke, recovery evidence and exact-commit evidence are green on the named SHA.

## Last recorded Foundation certification

- Certified exact code commit: c3a8ba482548e963479fbdf8538be62c4d15acfe
- Run: [Foundation certification #37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Current observed Foundation tip: be29687e637b709158a30204bb9213bfa4813950. It is newer than the certified SHA and needs a successful Foundation run on that exact tip before it is called the current certified checkpoint.
- The historical certificate is never silently upgraded.

## Active Integration / CI candidate — separate from Stable Foundation

- Draft, unmerged review: [PR #31](https://github.com/alizebal73/5/pull/31), current exact head `20ba74902fe620f00c58d047427c9f7d12c890f0`, targeting Integration. Quick push/PR, Full Foundation, and Runtime Payload push/PR passed on that exact SHA (links in the handoff gate and Stable Checkpoint).
- PR #31's payload has a hash-verified, test-only service-registration helper for isolated Server/Agent PCs. The hosted build runs `-ValidateOnly` only; no actual service was installed or started.
- Separate Foundation maintenance review: [PR #32](https://github.com/alizebal73/5/pull/32), head `4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9`; Full Foundation passed [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It remains Draft/unmerged; the actual Foundation branch is not yet certified at its exact tip.
- Integration is protected by active Ruleset [`protect-integration-runtime` #24879997](https://github.com/alizebal73/5/rules/24879997); required check is `quick-validation`.
- Personal Runner removal is deferred by owner to [Issue #33](https://github.com/alizebal73/5/issues/33); runner-group restrictions are not claimed as enforced.
- These candidates are not Setup.exe/MSI, production releases or physical-LAN certification.

## Policy

Keep known-good baselines separate from development. Record exact commit and run for every certification. Documentation-only follow-ups must state the exact code SHA whose tests they reference. Hosted CI is not proof of installed Windows Service identity or physical LAN behavior.
