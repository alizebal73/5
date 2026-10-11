# Stable Checkpoints

A checkpoint is stable only when restore/build/tests, architecture guards, PostgreSQL certification, Desktop/Agent smoke, recovery evidence and exact-commit evidence are green on the named SHA.

## Last recorded Foundation certification

- Certified exact code commit: c3a8ba482548e963479fbdf8538be62c4d15acfe
- Run: [Foundation certification #37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Current observed Foundation tip: be29687e637b709158a30204bb9213bfa4813950. It is newer than the certified SHA and needs a successful Foundation run on that exact tip before it is called the current certified checkpoint.
- The historical certificate is never silently upgraded.

## Active Integration / CI candidate — separate from Stable Foundation

- Draft, unmerged review: [PR #31](https://github.com/alizebal73/5/pull/31), current verified head `7a8475bf862d0ecdf4cd70eb12361e0152dd4317`, targeting Integration. Quick PR #38138211044, Full Foundation #38138208159 and Runtime Payload #38138211061 passed on that head. The preceding code-bearing candidate `d02192a43722efc1f49bf84fd24af911b3ece7e9` passed Quick push #38137818156 and PostgreSQL diagnostic #38137818186 as well as its other three gates.
- Separate Foundation maintenance review: [PR #32](https://github.com/alizebal73/5/pull/32), head `4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9`; Full Foundation passed in [run #38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It remains Draft/unmerged and does not make the base Foundation SHA independently certified.
- Integration now has active Ruleset [`protect-integration-runtime` #24879997](https://github.com/alizebal73/5/rules/24879997); required check is `quick-validation`.
- The personal Runner remains registered by owner decision until GameNet completion; removal is deferred to [Issue #33](https://github.com/alizebal73/5/issues/33). Settings-level runner-group restrictions are not asserted as verified. Some older open branches still contain self-hosted workflow definitions.
- These candidates are not installers, production releases or physical-LAN certification.

## Policy

Keep known-good baselines separate from development. Record exact commit and run for every certification. Documentation-only follow-ups must state the exact code SHA whose tests they reference. Hosted CI is not proof of installed Windows Service identity or physical LAN behavior.
