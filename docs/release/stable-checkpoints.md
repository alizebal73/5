# Stable Checkpoints

A checkpoint is stable only when restore/build/tests, architecture guards, PostgreSQL certification, Desktop/Agent smoke, recovery evidence and exact-commit evidence are green on the named SHA.

## Last recorded Foundation certification

- Certified exact code commit: c3a8ba482548e963479fbdf8538be62c4d15acfe
- Run: [Foundation certification #37860650826](https://github.com/alizebal73/5/actions/runs/37860650826)
- Current observed Foundation tip: be29687e637b709158a30204bb9213bfa4813950. It is newer than the certified SHA and needs a successful Foundation run on that exact tip before it is called the current certified checkpoint.
- The historical certificate is never silently upgraded.

## Active Integration / CI candidate — separate from Stable Foundation

- Draft, unmerged review: [PR #31](https://github.com/alizebal73/5/pull/31); last code-bearing baseline e5caceb11968512fb91b5f808cbd4afd24be22ad passed five exact-SHA gates. The live PR body records current documentation-only HEAD and latest run URLs.
- PR #31 includes the guarded test-only service helper and package-aware Tools runtime instructions. CI uses -ValidateOnly; this is not physical service/LAN acceptance.
- Separate Foundation maintenance review: [PR #32](https://github.com/alizebal73/5/pull/32), head 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9; Full Foundation passed in [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It remains Draft/unmerged and does not make the base Foundation SHA independently certified.
- Integration has active Ruleset [protect-integration-runtime #24879997](https://github.com/alizebal73/5/rules/24879997); required check is quick-validation.
- PR #15 remains Draft/unmerged, an older Foundation-based TLS/ProgramData branch at head 15402b2758808327874c18e21753120ef9cfccce. Its own three lanes passed, but workflow overlap needs a reviewed merge order.
- Personal Runner removal is deferred by owner to [Issue #33](https://github.com/alizebal73/5/issues/33); Runner-group restrictions are not claimed as enforced.
- These candidates are not Setup.exe/MSI, production releases or physical-LAN certification.

## Policy

Keep known-good baselines separate from development. Record exact commit and run for every certification. Documentation-only follow-ups must state the exact code SHA whose tests they reference. Hosted CI is not proof of installed Windows Service identity or physical LAN behavior.
