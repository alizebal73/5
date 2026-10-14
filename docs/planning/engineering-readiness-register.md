# Engineering Readiness Register — GameNet 5

این سند، دفتر ریسک‌ها و ترتیب اجرای کار است. تاریخ snapshot جاری: 2026-10-13؛ بازخوانی SHAها و آزمون‌ها باید پیش از هر اقدام دوباره انجام شود. این دفتر جایگزین Requirementها، ADRها یا گواهی Foundation نیست؛ آن‌ها را به ترتیب اجرایی و وضعیت واقعی وصل می‌کند.

**سیاست زمان‌بندی پذیرش نهایی (2026-10-12):** آزمون فیزیکی Windows Service/LAN، آزمون تعاملی WPF، نصب/ارتقا/repair/rollback و pilot فقط پس از تکمیل دامنهٔ برنامه‌ریزی‌شده و ساخته‌شدن Setup.exe/MSI اجرا می‌شوند. CI، unit/integration/regression/security tests برای هر برش و هر SHA مرتبط در طول توسعه ادامه دارند؛ این تعویق فقط مربوط به پذیرش یکپارچهٔ نهایی است. هر ادعای Done باید به commit، آزمون و مدرک همان SHA متصل باشد.


## Live read-back and execution decisions — 2026-10-14 (newest snapshot)

Historical snapshots below are preserved as evidence; use this latest section plus a live GitHub read before action. Do not create a new documentation-reconciliation PR for every small status change; use the active issue/check history for event-level status and update this register only at milestone/risk decisions.

### Exact refs / current facts
- Integration after PR #90: `6a6cda5bf0c199d7163323d69f4c0fbfef09d58f`.
- Main: `b9288471f2047570eaf8d0d6552cf87bc0ddc214` (40-character SHA verified against GitHub commit API; intentionally unchanged starter).
- Foundation: `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`.
- PR #90 is merged. PR #94 is the clean one-file retry replacement for #87, directly based on post-#90 Integration at head `72a6ccbeba8536ae8734e535a7e739862bf132a8`. Runtime Payload [#38560171404](https://github.com/alizebal73/5/actions/runs/38560171404) passed; Full Foundation/PostgreSQL run [#38560171403](https://github.com/alizebal73/5/actions/runs/38560171403) was still in progress at last read-back. No merge before it and all required checks conclude success.
- Old PR #87 must be closed as superseded only after #94 succeeds and merges. PR #88 presently contains both retry and redaction changes; rebuild it redaction-only after #94 merges. PR #91 is deferred to after Issue #92 / the first Session slice as a substantial security capability; it remains a mandatory pre-pilot/release gate, not an accepted operational route yet.
- The exact `main` SHA was verified through GitHub commit metadata. Avoid copy/pasting a shortened or mistyped hash into future evidence.

### Agreed priority, with timebox
Timebox immediate defect fixes to 2–3 working days following 2026-10-14 without relaxing checks: (1) #90 merged; (2) exact-head #94 transaction retry; (3) clean redaction-only PR derived after #94. Do not include #91's feature development in this immediate bug-fix timebox. If CI exposes a real defect, record the blocker and remaining effort rather than bypassing the gate.

### Pipeline and product feedback timing
- [Issue #95](https://github.com/alizebal73/5/issues/95) is the bounded prerequisite before Session V1. The code already has Identity's private `MutateAsync` that centralizes transaction/idempotency/replay; authorization, domain validation and audit payloads remain operation-specific. Add characterization tests first and extract only the proven common contract. No broad service rewrite based on file size.
- [Issue #92](https://github.com/alizebal73/5/issues/92) requires a real, isolated PC/VM feasibility outcome by 2026-10-18 if hardware is available; otherwise mark BLOCKED with the specific missing test environment. Documentation alone is not PASS, and the live Manager/shop PC must not be modified.
- [Issue #96](https://github.com/alizebal73/5/issues/96) defines an early 2–3-station shadow trial after Slice 2/cash E2E for fast operator feedback. It uses test build/disposable DB and keeps the legacy system authoritative; no real customer money or production sessions. This is separate from the final formal pilot.
- Final Setup/update/rollback, installed Windows Service identity/ACL/DPAPI/TLS, restore and physical LAN acceptance remains with [Issue #16](https://github.com/alizebal73/5/issues/16) and [Issue #34](https://github.com/alizebal73/5/issues/34). Formal real-shop pilot/release is still gated on those final checks.

### Risks / disposition
| Risk | Disposition |
|---|---|
| .NET advisory baseline | Resolved in Integration by #90; keep vulnerability scan mandatory. |
| Wrapped PostgreSQL serialization/deadlock retry | Open in #94; verify owner-concurrency invariant and exact-head Full Foundation before merge. |
| Exception text/secret leakage in logs | #88 must be rebuilt redaction-only after #94; use sentinel regression test and no secret-bearing diagnostics. |
| Staged Agent credential rotation | Defer from immediate bug-fix queue; do not call old one-step rotate path safe. Mandatory pre-pilot/release gate; keep Issue #74 open. |
| JWT active/previous key rotation, PostgreSQL runtime credential rotation, provisioning key, protected-state recovery/host replacement and broad redaction | Track separately in Issue #74; define dependent feature, owner, proof and pre-use/release gate. No silent key regeneration. |
| Hosted vs local runner | Issue #67 remains open. Current evidence is from hosted `windows-2025`, not the local self-hosted runner. Do not change runner labels without a trust-boundary decision. |
| Control of a Windows customer PC | Issue #92; prove feasibility in an isolated environment before designing Session-to-Agent control. |
| Premature generic architecture | Issue #95 bounds the shared command-pipeline extraction; only common mechanics are abstracted, domain permission/Audit semantics stay explicit. |
| Late operator feedback | Issue #96 early shadow trial after the cash journey; final production pilot remains after Setup and Issue #34 acceptance. |
| Old-base draft PRs #15/#17/#21 | Historical comparison only; do not merge wholesale. |

### Cross-cutting requirements for each slice
- Money: checked integer `long` Toman amounts; no binary float; one documented rounding policy; append-only ledger and reversal entries; distinguish cash, wallet, free/bonus value, free time and debt.
- Session/time: Server UTC; tariff snapshot at start; explicit versioned state machine; PostgreSQL unique constraint for at most one active session per station; defined pause/resume, boundary pricing and recovery behavior.
- Concurrency: intent-stable idempotency key across retry, changed-payload rejection, duplicate click/payment/session and simultaneous operator/wallet tests; database constraints are final guards.
- Agent/recovery: Server-authoritative decisions, fencing against stale device/lease/session generations, transactional persistence before external dispatch, reconnect reconciliation.
- Data/security: immutable applied migrations; expand/compatible transition/contract; tested upgrade/restore with disposable old-version data; secrets excluded from logs/traces/support bundles; fail closed for corrupt secret state.
- Module/UI boundaries: Sessions owns lifecycle/time, Tariffs price resolution, Billing invoices/payment allocations, Wallet balances/free value/debt, Inventory stock movements, Agent OS execution. WPF displays Server response; test RTL/LTR, Persian/Arabic digits, Persian normalization and loading/empty/offline/error/forbidden states.
- Completion evidence: behavior/characterization tests, relevant PostgreSQL concurrency tests, exact candidate SHA checks and operator-facing flow test. Final installed-Service/physical acceptance remains a distinct release gate.


## Current execution snapshot — 2026-10-13 (read-back before docs reconciliation PR)

- **Integration:** `integration/runtime-operator-v1` was read at `610ad37739e4ea32ee4e0d4d24d011b8e413e908` immediately before this docs-only PR was prepared. This is an immutable evidence/ref-read-back event, not a live pointer after the docs PR merges.
- **Main / Foundation:** `main` remains at intentionally unchanged starter SHA `b9288471f2047570eaf8d0d6552cf87bc0ddc214`; Foundation branch read-back is `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`, with its prior exact-tip certification in [run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053).
- **Exact post-merge evidence for PR #82:** merge SHA `610ad37739e4ea32ee4e0d4d24d011b8e413e908`; [Quick Validation #38462799723](https://github.com/alizebal73/5/actions/runs/38462799723) and [Full Foundation #38462799563](https://github.com/alizebal73/5/actions/runs/38462799563) both succeeded on that exact SHA. The Full Foundation run includes Canonical verification, PostgreSQL, Backup/isolated Restore, Desktop, Agent and evidence upload.
- **Runner accuracy:** both runs above used GitHub-hosted `windows-2025`; they are not evidence from the owner's local self-hosted Runner. [Issue #67](https://github.com/alizebal73/5/issues/67) remains open for the local-only preference / runner trust-boundary decision.
- **Stage disposition:** Master Plan Stage 1 (Foundation) and Stage 2 (module inventory/architecture guards) are closed. Stage 3 platform/security hardening remains active: Issue #75's direct Audit gap is test-complete through PR #82, while secret rotation/recovery/redaction remains open under [Issue #74](https://github.com/alizebal73/5/issues/74). ADR-0002 was accepted through PR #73 but does not certify implementation of rotation/recovery or installed-Service behavior.
- **Current queue:** finish this docs/evidence reconciliation for Issue #75/#77, then take the focused StationBoard/Action Catalog preparation in [Issue #79](https://github.com/alizebal73/5/issues/79). Do not declare the first complete PC business journey (canonical roadmap step 4) active until Stage 3 exit criteria have exact-SHA evidence or the owner explicitly defers named remaining gates.
- **Release boundary:** Setup/update/rollback and physical Windows Service/TLS/DPAPI/ACL/LAN acceptance remain deferred until planned product scope and a real Setup.exe/MSI candidate exist ([Issue #16](https://github.com/alizebal73/5/issues/16), [Issue #34](https://github.com/alizebal73/5/issues/34)).

## Current verified baseline — 2026-10-12 (post-merge read-back; historical snapshot, not a live pointer)

This section records evidence and a timestamped ref read-back. It is not a live pointer to the current Integration head: documentation commits advance that branch. Always read the live branch from GitHub before dispatching or merging anything.

- Repository: `alizebal73/5`; default branch: `main`.
- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, protected and deliberately unchanged at the read-back.
- At the read-back before runner-policy merge, `foundation/runtime-final-v2` was `be29687e637b709158a30204bb9213bfa4813950`. PR #32 is now merged; the observed Foundation SHA is `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`. This exact SHA passed post-merge Full Foundation [run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053), with the evidence artifact uploaded. `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` is now the latest certified Foundation checkpoint; Stable Checkpoint is being updated to link this exact evidence.
- `integration/runtime-operator-v1`: `d2d8c626c678078c52b840fd0c9c751e7583fa91` was observed immediately after PR #42 merged. It is a docs-control commit, not a new runtime-code acceptance SHA.
- Runtime source merge PR [#40](https://github.com/alizebal73/5/pull/40): candidate `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd` passed [Quick #38180651116](https://github.com/alizebal73/5/actions/runs/38180651116), [Full Foundation #38180646187](https://github.com/alizebal73/5/actions/runs/38180646187), and [Runtime payload #38180651104](https://github.com/alizebal73/5/actions/runs/38180651104). The code merge SHA `58717adc...` passed [Quick #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) and [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446).
- Docs-only PR #41 merged at `362513ad...`; candidate passed [Quick #38181520088](https://github.com/alizebal73/5/actions/runs/38181520088), and that SHA passed [Quick #38181599202](https://github.com/alizebal73/5/actions/runs/38181599202). Do not reinterpret these as checks on a later commit.
- Docs-control PR #42 merged at `d2d8c626c678078c52b840fd0c9c751e7583fa91`; its exact head `623bb678263e0d42bafa35efd7c5ca0afc6064fa` passed [Quick Validation #38182154397](https://github.com/alizebal73/5/actions/runs/38182154397).
- Candidate test counts on #40's exact verified code head: Desktop 36, Server unit 84, Shared 38, Agent 33 and Contract 1; zero failures/skips in those suites. The handler-level busy-gate regression test is included.
- Source PRs #35–#39 are closed as superseded by #40; their source branches are retained. Backward PR #23 is also closed without merging or deleting its source branch.
- PR #32 has now moved Foundation automation to hosted `windows-2025`; its candidate passed Full Foundation, and merged Foundation SHA `71ef653...` passed [post-merge run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053). Integration hosted-runner migration PR #44 merged at `161b4c...`; candidate Quick push #38183164928, Quick PR #38183167294, Full Foundation #38183164941 and Runtime payload #38183167300 passed. The merge SHA passed post-merge Quick #38183458461 and Full Foundation #38183458486. PR #31 is closed as superseded for runner changes; source branch retained for the test-service helper. #15's ProgramData/TLS runtime source remains separate.
- Automated CI does not prove installed Windows Service identity/effective ACLs, DPAPI/private-key access after restart, physical two-PC LAN/reconnect, or interactive WPF RTL/focus. These checks are deliberately deferred until the complete Setup candidate exists. Do not change the installed Manager PC without read-only preflight review and explicit approval.



## Verified execution events after the timestamped baseline — 2026-10-12

- Foundation runner PR #32 merged to `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; Full Foundation [#38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) passed on that exact SHA.
- Integration hosted runner PR #44 merged at `161b4cbbbe16cd3fd4046f174a1c09380139c67b`. Candidate `92de1b1...` passed Quick push #38183164928, Quick PR #38183167294, Full Foundation #38183164941 and Runtime payload #38183167300. Exact merge SHA passed post-merge Quick #38183458461 and Full Foundation #38183458486.
- PR #31 closed without merging because #44 superseded its runner/workflow changes; its branch is retained for the separate test-only Service registration helper/runbook.

## Active gap register

| ID | اولویت | موضوع و شاهد فعلی | وضعیت | معیار پایان |
|---|---|---|---|---|
| G-01 | P1 | Self-referential branch snapshots labelled prior docs commits as live Integration heads and caused conflicting reports. | Ref tables are timestamped historical read-backs; execution-time branch heads must be read from GitHub. | No document may claim its embedded Integration SHA remains live after that same change is merged; immutable evidence SHAs and live pointers must stay separate. |
| G-02 | P1 | The older-base StationBoard extraction PR #24 is closed as superseded; the required focused presentation extraction and persistent Action Catalog are tracked by Issue #79. | Existing shell/StationBoard basics are integrated; incremental View separation and Action Catalog remain queued before the first complete business UI slice. | Base the refactor on live Integration; keep commands, API and business behavior unchanged; add binding/resource/localization/permission regressions and exact-SHA Quick/Foundation evidence. |
| G-03 | P1 | Agent health refresh coordinator, cancellation through the Server request and stale-response fencing are included in PR #40 and merged at `58717adc...`. | Exact candidate Quick/Full/payload and post-merge Quick/Full all passed; installed-Service/UI/LAN acceptance remains open. | Automated timer/state regressions remain part of CI; interactive UI and physical Service/LAN acceptance are deferred to the final post-Setup phase. |
| G-04 | P1 | Effective permissions are wired from the authenticated Server snapshot into Station Board commands. Handler-level busy guards now prevent direct `ICommand.Execute` from bypassing the busy policy; regression test is included. | Merged in #40; exact-head candidate and post-merge automated gates passed. Server remains the authorization authority. | Keep permission regression tests on future handlers and independently validate Server authorization for each new capability. |
| G-05 | P1 | XAML localization/resource parity, binding-path and PasswordBox lifecycle contracts have static regression coverage in #40. | Exact candidate and post-merge automation passed; interactive RTL/LTR, focus, keyboard and navigation are still unverified. | Maintain automated resource/binding/permission regression checks during development; perform Windows interactive smoke for RTL/LTR and loading/offline/permission-denied states only in the final post-Setup phase. |
| G-06 | P0 | حذف Bootstrap اولیه اکنون در `scripts/finalize-server-setup.ps1` پیاده‌سازی شده و تست خواندن Protected Settings بدون آن وجود دارد؛ کاندید ترکیبی در CI سبز است. اجرای واقعی replacement/ACL/DPAPI و بررسی startup پس از cleanup تحت هویت Windows Service هنوز انجام نشده است. | کد و آزمون خودکار حاضر؛ پذیرش عملی باز | روی Server آزمایشی جدا، پس از ثبت Owner، فقط همان کلید حذف شود؛ payload موقت با DPAPI بازخوانی و بررسی شود، replacement اتمیک/ACL تأیید و startup/readiness بعد از restart سرویس آزموده شود؛ شکست باید فایل اصلی را سالم نگه دارد. |
| G-18 | P0 | مخزن اسرار Server با DPAPI LocalMachine، نوع‌های مستقل برای connection/JWT/Agent provisioning و fingerprint secret، Production fail-closed، ACL مبتنی بر Service SID و مسیر حذف Bootstrap در tree ادغام‌شده موجود است. آزمون‌های خودکار Protected Settings/bootstrap و Agent enrollment/credential نیز در tree وجود دارند. ADR-0002 در تاریخ 2026-10-13 به‌عنوان تصمیم معماری پذیرفته شده و در PR مستنداتی محدود ثبت می‌شود؛ این پذیرش به معنای تکمیل rotation/recovery یا پذیرش سرویس نصب‌شده نیست. DPAPI/ACL زیر Windows Service نصب‌شده، private-key پس از restart، rotation/recovery و redaction کامل هنوز اثبات نشده‌اند. | قرارداد معماری پذیرفته؛ پیاده‌سازی/شواهد عملیاتی ناقص | طبق [ADR-0002](../decisions/ADR-0002-server-secret-lifecycle.md) و [Issue #74](https://github.com/alizebal73/5/issues/74)، rotation/recovery و آزمون‌های خودکار شکست/قطع‌فرایند/ابطال/secret-redaction را تکمیل کن؛ هویت سرویس، ACL مؤثر، DPAPI و TLS private-key پس از restart فقط در مرحلهٔ نهایی، با Setup.exe/MSI واقعی، ماشین‌های آزمایشی جدا و پایگاه دادهٔ دورریختنی طبق [Issue #34](https://github.com/alizebal73/5/issues/34) تأیید شوند. روی Manager واقعی تغییر انجام نشود. |
| G-07 | P0 | ثبت Agent توکن ۲۵۶بیتی ۱۵دقیقه‌ای، hash-only persistence، رد replay/revoke/expiry و recovery محدود با reason/audit در Integration ادغام شده است. اصلاح lifecycle، فایل توکن محافظت‌شده را تا موفقیت احراز عادی نگه می‌دارد؛ regression test و Agent runtime certification روی source، delivery PR و merge commit موفق‌اند. | کد و تست‌های خودکار green؛ نصب Windows Service/DPAPI زیر LocalService پس از restart خارج از محدوده | هویت/ACL/DPAPI سرویس واقعی و restart جداگانه تأیید شوند. |
| G-08 | P0 | PR #30 با یک commit و 67-file diff در `integration/runtime-operator-v1` ادغام شد؛ merge commit `e1626d7d1f2e62601329604b8823787f2c9597b6`. Full Foundation #38096870883، Quick push #38096870892، Quick PR #38096874251 و Runtime payload #38096874276 روی delivery SHA `f438758...` موفق شدند. بستهٔ داخلی `GameNet5-RuntimeLanSmoke-f4387584d6c1.zip` با SHA-256 `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`; outer artifact digest `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. پس از merge نیز Full Foundation #38097484132، Quick push #38097484122 و Quick PR #38097487320 موفق‌اند. ZIP بستهٔ Runtime است، نه Setup/MSI؛ نصب Windows Service/دو-PC LAN همچنان آزمایش نشده. | delivery و post-merge automated gates green؛ physical acceptance خارج از این مرحله | سرویس نصب‌شده و LAN در مرحلهٔ پذیرش نهایی، پس از تکمیل قابلیت‌ها و ساخت Setup.exe/MSI. |
| G-09 | P1 قبل از Release | مدل گواهی فعلی، private key غیرقابل‌استخراج در LocalMachine\My و trust کردن public .cer پس از SHA-256 مستقل است. چرخهٔ rotation/revocation/recovery و رفتار cert منقضی هنوز باید گواهی شود. | بخشی خودکار آزمون شده؛ lifecycle کامل باز | آزمون rotation بدون trust bypass، SAN mismatch/expiry، rollback/recovery و مجوز فایل/کلید تحت حساب سرویس واقعی. |
| G-10 | P0 پیش از نصاب | انتخاب اینکه PostgreSQL از قبل نصب‌شده فرض می‌شود یا نصاب آن را provision می‌کند هنوز تصمیم اجرایی لازم دارد. writer تنظیمات به‌تنهایی PostgreSQL/database role را نصب یا امن‌سازی نمی‌کند. | تصمیم باز | نسخهٔ پشتیبانی‌شده، service identity، database role، data path، backup/upgrade و migration identity صریح شوند؛ Production startup هرگز خودکار migrate نکند. |
| G-11 | P0 پیش از Release | ZIPهای self-contained خروجی خام‌اند، نه Setup.exe/MSI؛ Windows service registration، repair، upgrade، updater و rollback نصب واقعی گواهی نشده‌اند. | باز؛ tracked در [Issue #16](https://github.com/alizebal73/5/issues/16) | install/repair/upgrade، package checksum/signature، health gate، rollback و uninstall بدون حذف ناخواستهٔ business data روی ماشین تمیز. |
| G-12 | P0 پیش از Business vertical بعدی | Customer/Session/Tariff/Billing/Wallet/Inventory/Buffet/VIP/Reports/Shift/Approval کامل در snapshot فعال حاضر نیستند. | نیازمندی برنامه‌ریزی‌شده، نه فراموش‌شده | هر module مطابق requirement: مالک داده، permission، state machine، transaction، idempotency/concurrency، audit، failure/recovery، API/UI و تست PostgreSQL واقعی؛ هیچ financial authority در Desktop. |
| G-19 | P1 قبل از هر برش تجاری | ماتریس ۶۳ requirement ردیف دارد، اما داشتن ID و معیار کلی به‌تنهایی trace کامل requirement→slice→کد/API→تست→شاهد نیست. | باید در هر برش تکمیل شود | برای هر برش، DoR/vertical-slice ثبت کند: REQ IDs، invariant، permission، migration/data owner، transaction/idempotency/race، audit، failure/recovery، آزمون خودکار برای هر برش؛ آزمون فیزیکی/سرتاسری فقط در پذیرش نهایی پس از آماده‌شدن Setup.exe/MSI، exact SHA/evidence؛ هیچ ماژول کامل‌نشده را از روی وجود ردیف/فایل Done اعلام نکنید. |
| G-13 | P1 | Historical branch/PR inventory contained stale open labels after later merges/closures. | PRs #35–#39, #23, #31, #51, #18, #20 and #24 are closed without merge/superseded; #53 and #56 merged. At this read-back, only PRs #15, #17 and #21 remain open Drafts. Keep their branches for source/evidence comparison; never merge old bases wholesale. | Re-read live PR state before every action and port only unique, required changes through a focused PR with exact-SHA evidence. |
| G-14 | P1 governance | The three Rulesets are active and report `strict_required_status_checks_policy=true`; Integration requires the observed `quick-validation` check and Foundation requires `foundation`. However, `required_approving_review_count=0`, with no CODEOWNERS approval requirement. | Up-to-date-before-merge is enabled; Issue #54 records the owner-delegated technical-review policy, not independent human approval. Current workflows use GitHub-hosted `windows-2025`; the local-runner preference conflict remains open under Issue #67. | Keep exact-SHA gates green and record delegated review accurately; reconcile Issue #67 before changing runner labels/trust controls. |
| G-15 | P1 security | مخزن عمومی است. این ممیزی ادعا نمی‌کند تمام تاریخچهٔ همهٔ branchها از نظر secret به‌صورت خودکار اسکن شده است. | بررسی مستقل لازم | history/branches/packageها با secret scanner بررسی؛ alerts/push protection فعال در صورت دسترسی؛ اگر secret واقعی یافت شد rotate/revoke اول، بعد پاک‌سازی تاریخچه با برنامه و هماهنگی. |
| G-16 | Deferred | طرح Steam/GamingAccounts یک ADR پیشنهادی است و PR #19 بسته شده؛ مجازبودن مدل حساب و روش integration هنوز شرط ورود است. | عمداً پیاده‌سازی نشده | نیاز محصول صریح + بررسی برنامهٔ رسمی/اجازهٔ Valve + تصمیم محصول/ADR؛ تا آن زمان هیچ credential injection، MFA bypass یا Auto Login فرضی ساخته نشود. |
| G-17 | P1 پیش از Release | Foundation برای PostgreSQL/migrations، audit/idempotency/outbox و backup/restore آزمون دارد؛ اما زمان‌بندی/retention، محل امن نسخه‌های پشتیبان و RPO/RTO محصولی هنوز کامل نشده‌اند. | بخشی Foundation موجود؛ عملیات کامل باز | restore دوره‌ای و ایزوله، retention، هشدار شکست، صلاحیت دسترسی، آزمون بازیابی/تطبیق و ثبت زمان/داده از دست‌رفته. |

## Ordered execution route — 2026-10-13 (post-merge)

1. **Master Plan Stage 1 — Foundation: CLOSED.** Preserve the Foundation checkpoint and exact evidence in [Stable Checkpoint](../operations/stable-checkpoint.md). Do not reopen it for deferred installer or physical-machine work.
2. **Master Plan Stage 2 — module inventory/architecture guards: CLOSED.** PR #56 merged at `16e9abe3ac68885af7f9282c3872e47c13ff19aa` and Issue #55 closed. This is not evidence that each catalogued business module has been implemented.
3. **Identity audit contract tests: integrated and verified.** PR #82 merged as `610ad37739e4ea32ee4e0d4d24d011b8e413e908`. Its exact PR-head run #38461912514 passed; post-merge Quick #38462799723 and Full Foundation #38462799563 also passed on the same merge SHA. Issue #75 is ready for closure after this evidence-register reconciliation is merged.
4. **Master Plan Stage 3 — platform/security hardening: ACTIVE.** ADR-0002 was accepted through PR #73; this is an architecture decision only. Issue #74 still owns key/credential rotation, recovery and global redaction gates. Do not mark the stage complete without exact-SHA evidence or a clearly recorded owner decision deferring named gates.
5. **Next non-overlapping preparation:** after this documentation reconciliation, proceed with Issue #79's behavior-preserving StationBoard view extraction and the Action Catalog. Keep the existing Commands/API behavior intact. This preparation does not activate the first full PC business journey.
6. **Business implementation gate:** do not declare canonical roadmap step 4 (the complete PC business journey) active until Stage 3 exit criteria are met or the owner explicitly defers named open gates. When started, implement customer find/create → tariff snapshot → session start/end → checkout → wallet/cash/card/debt → durable ledger/audit as a tested vertical slice.
7. **Runner and branch policy:** current checks are hosted `windows-2025`; the local self-hosted preference/trust boundary remains tracked under Issue #67. `main` stays unchanged at its empty starter commit; Foundation and Integration statuses are never inferred from old documentation snapshots.
8. **Release-only gates:** Setup/update/rollback and installed-Service/TLS/DPAPI/ACL/LAN/interactive WPF acceptance remain deferred to a feature-complete Setup.exe/MSI candidate; see Issues #16 and #34.

## Branch and PR disposition — 2026-10-13 (read-back snapshot)

| PR / issue | Current disposition at this read-back |
|---|---|
| [#82 — Identity Audit contract tests](https://github.com/alizebal73/5/pull/82) | Merged at `610ad37739e4ea32ee4e0d4d24d011b8e413e908`. Exact PR-head Quick/Full Foundation and post-merge Quick [#38462799723](https://github.com/alizebal73/5/actions/runs/38462799723) / Full Foundation [#38462799563](https://github.com/alizebal73/5/actions/runs/38462799563) succeeded. |
| [#81 — Owner concurrency/retry safety](https://github.com/alizebal73/5/pull/81) | Merged; candidate Full Foundation/PostgreSQL and Quick passed; post-merge Integration exact-SHA checks recorded in Issue #75. |
| [#80 — Agent heartbeat certification polling](https://github.com/alizebal73/5/pull/80) | Merged; bounded polling preserves the timestamp-advance invariant and removes fixed-delay flakiness without weakening the check. |
| [#76 — required reasons for privileged Identity mutations](https://github.com/alizebal73/5/pull/76) | Merged with exact-head validation. The direct Audit contract gap is completed by #82. |
| [#73 — accepted ADR-0002](https://github.com/alizebal73/5/pull/73) | Merged. Architecture decision accepted; rotation/recovery/redaction remain open under Issue #74. |
| [#78 — UI/vertical-slice policy](https://github.com/alizebal73/5/pull/78) | Merged; policy is adopted and linked from product traceability/DoR/DoD. |
| [#53 — pre-merge PostgreSQL certification gate](https://github.com/alizebal73/5/pull/53) | Merged at `2bd6a65509c84a4d4bd48a5cbe7bf89eaa73259b`; exact merge evidence is preserved on its run record. |
| [#56 — module catalog/ownership/architecture guard](https://github.com/alizebal73/5/pull/56) | Merged at `16e9abe3ac68885af7f9282c3872e47c13ff19aa`; Issue #55 closed. |
| [#51 — stale readiness/governance snapshot](https://github.com/alizebal73/5/pull/51) | Closed without merge as a stale-base snapshot; retain history only and do not copy it wholesale. |
| [#15 — ProgramData/endpoints/trusted LAN TLS](https://github.com/alizebal73/5/pull/15) | Still open Draft on an older Foundation base and overlaps workflow files. Compare unique runtime source against live Integration; do not merge wholesale and do not change Manager PC. |
| [#17 — 2026-10-10 Runtime LAN smoke evidence](https://github.com/alizebal73/5/pull/17) | Still open Draft; historical process-based evidence only. Not a substitute for final installed-Service/two-PC acceptance. |
| [#21 — initial Desktop operator shell](https://github.com/alizebal73/5/pull/21) | Still open Draft on an older base. Compare its unique XAML/ViewModel/tests; port only unique behavior after the current view extraction is scoped. |
| [#18](https://github.com/alizebal73/5/pull/18), [#20](https://github.com/alizebal73/5/pull/20), [#24](https://github.com/alizebal73/5/pull/24) | Closed/superseded; source branches preserved for history. Do not merge the stacked/older-base diffs. |
| [#67 — runner trust boundary](https://github.com/alizebal73/5/issues/67) | Open. Current CI evidence is hosted `windows-2025`, not local self-hosted runner evidence; do not change runner policy without resolving this issue. |
| [#74 — secret rotation/recovery/redaction](https://github.com/alizebal73/5/issues/74) | Open. Operational use of features remains gated by the specific secret guarantees they depend on. |
| [#75 — Identity audit invariant evidence](https://github.com/alizebal73/5/issues/75) | Implementation and exact post-merge checks are complete; close after the documented evidence/status reconciliation merges. |
| [#77 — roadmap/status reconciliation](https://github.com/alizebal73/5/issues/77) | This docs-only reconciliation is the work item; close it only after exact-head documentation CI passes and the PR is merged. |
| [#79 — StationBoard extraction + Action Catalog](https://github.com/alizebal73/5/issues/79) | Open and queued after the current status reconciliation. Must preserve behavior and add automated view/binding/resource/permission regression evidence. |
| [#16 and #34 — Setup/release and physical acceptance](https://github.com/alizebal73/5/issues/16) | Deferred until planned scope is complete and a real Setup.exe/MSI candidate exists. Do not treat runtime ZIP or CI as installed-Service/LAN acceptance. |

## Stage closure rules

هر گام فقط وقتی تمام است که همهٔ این موارد حاضر باشند:

- پیش‌نیازها و مالکیت لایه روشن؛ هیچ نیاز تجاری/امنیتی/مالی مهمی به حدس پنهان واگذار نشده باشد.
- diff محدود و قابل بازگشت باشد؛ API قرارداد typed و backward compatibility ارزیابی شده باشد.
- آزمون مرتبط و آزمون regression اجرا شده باشند؛ برای تغییر runtime، هر gate لازم روی همان SHA به‌روز اجرا شود.
- نتیجهٔ CI، SHA، رفتار واقعی، خطاهای مشاهده‌شده و ریسک باقیمانده ثبت شود.
- خطای مرحلهٔ قبل باز نباشد؛ تغییر جدید برای پوشاندن خطا یا bypass امنیتی استفاده نشده باشد.
- «کدنویسی تمام شد»، «CI سبز شد»، «آزمون LAN گذشت» و «Release-certified» چهار وضعیت جدا هستند.

## Explicit non-goals

- تغییر یا پاک‌سازی ACL/Firewall/Certificate/ProgramData روی کامپیوتر کاربر بدون برنامهٔ کنترل‌شده و بررسی وضعیت موجود.
- ادغام خودکار PRها، انتقال کورکورانهٔ branchهای قدیمی، یا ویرایش main/Foundation candidate.
- ادعای نصب‌کننده، TLS rotation، service restart، یا ۱۵ ایستگاه بر اساس unit test/loopback/ZIP.
- اجرای Steam Auto Login قبل از requirement و policy/provider gate.


## CI trigger hardening follow-up — 2026-10-10

PR #25 is open and ready for review (not merged). Its Quick Validation workflow adds `security/**` to the push lane in addition to Pull Request gates targeting the active integration/operator branches. PR #27 still requires exact-head testing once that trigger change is integrated; absence of CI is not a pass.
