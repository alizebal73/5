# Engineering Readiness Register — GameNet 5

این سند، دفتر ریسک‌ها و ترتیب اجرای کار است. snapshot اولیهٔ آن مربوط به 2026-10-11 است و چند ثبت تکمیلی تا 2026-10-12 دارد. این دفتر جایگزین Requirementها، ADRها یا گواهی Foundation نیست؛ موارد تاریخ‌دار آن باید پیش از اقدام با وضعیت زندهٔ GitHub تطبیق داده شوند.

**سیاست زمان‌بندی پذیرش نهایی (2026-10-12):** آزمون فیزیکی Windows Service/LAN، آزمون تعاملی WPF، نصب/ارتقا/repair/rollback و pilot فقط پس از تکمیل دامنهٔ برنامه‌ریزی‌شده و ساخته‌شدن Setup.exe/MSI اجرا می‌شوند. CI، unit/integration/regression/security tests برای هر برش و هر SHA مرتبط در طول توسعه ادامه دارند؛ این تعویق فقط مربوط به پذیرش یکپارچهٔ نهایی است. هر ادعای Done باید به commit، آزمون و مدرک همان SHA متصل باشد.

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
| G-02 | P1 | PR #24 only extracts the StationBoard view. PR #40 adds the health-refresh coordinator, but Login/Password views and the shell content host are not yet extracted. | First slices implemented in the integration candidate; more view boundaries remain | Continue small view extractions with binding/command regression tests during feature completion; perform interactive WPF acceptance only in the final post-Setup phase. |
| G-03 | P1 | Agent health refresh coordinator, cancellation through the Server request and stale-response fencing are included in PR #40 and merged at `58717adc...`. | Exact candidate Quick/Full/payload and post-merge Quick/Full all passed; installed-Service/UI/LAN acceptance remains open. | Automated timer/state regressions remain part of CI; interactive UI and physical Service/LAN acceptance are deferred to the final post-Setup phase. |
| G-04 | P1 | Effective permissions are wired from the authenticated Server snapshot into Station Board commands. Handler-level busy guards now prevent direct `ICommand.Execute` from bypassing the busy policy; regression test is included. | Merged in #40; exact-head candidate and post-merge automated gates passed. Server remains the authorization authority. | Keep permission regression tests on future handlers and independently validate Server authorization for each new capability. |
| G-05 | P1 | XAML localization/resource parity, binding-path and PasswordBox lifecycle contracts have static regression coverage in #40. | Exact candidate and post-merge automation passed; interactive RTL/LTR, focus, keyboard and navigation are still unverified. | Maintain automated resource/binding/permission regression checks during development; perform Windows interactive smoke for RTL/LTR and loading/offline/permission-denied states only in the final post-Setup phase. |
| G-06 | P0 | حذف Bootstrap اولیه اکنون در `scripts/finalize-server-setup.ps1` پیاده‌سازی شده و تست خواندن Protected Settings بدون آن وجود دارد؛ کاندید ترکیبی در CI سبز است. اجرای واقعی replacement/ACL/DPAPI و بررسی startup پس از cleanup تحت هویت Windows Service هنوز انجام نشده است. | کد و آزمون خودکار حاضر؛ پذیرش عملی باز | روی Server آزمایشی جدا، پس از ثبت Owner، فقط همان کلید حذف شود؛ payload موقت با DPAPI بازخوانی و بررسی شود، replacement اتمیک/ACL تأیید و startup/readiness بعد از restart سرویس آزموده شود؛ شکست باید فایل اصلی را سالم نگه دارد. |
| G-18 | P0 | مخزن اسرار Server با DPAPI LocalMachine، نوع‌های مستقل برای connection/JWT/Agent provisioning secret، Production fail-closed، ACL مبتنی بر Service SID و حذف Bootstrap پس از ساخت Owner در tree ادغام‌شده موجود است. Full Foundation روی source candidate #29، delivery PR #30 و merge commit `e1626d7...` موفق است. ADR-0002 همچنان Proposed است؛ DPAPI/ACL زیر Windows Service نصب‌شده، private-key پس از restart، rotation و recovery فیزیکی اثبات نشده‌اند. | کد و exact-SHA CI سبز؛ سرویس واقعی بیرون از این مرحله | پیش از انتشار، ADR-0002 و شواهد سرویس واقعی لازم است؛ تغییر Manager واقعی بدون preflight ممنوع. |
| G-07 | P0 | ثبت Agent توکن ۲۵۶بیتی ۱۵دقیقه‌ای، hash-only persistence، رد replay/revoke/expiry و recovery محدود با reason/audit در Integration ادغام شده است. اصلاح lifecycle، فایل توکن محافظت‌شده را تا موفقیت احراز عادی نگه می‌دارد؛ regression test و Agent runtime certification روی source، delivery PR و merge commit موفق‌اند. | کد و تست‌های خودکار green؛ نصب Windows Service/DPAPI زیر LocalService پس از restart خارج از محدوده | هویت/ACL/DPAPI سرویس واقعی و restart جداگانه تأیید شوند. |
| G-08 | P0 | PR #30 با یک commit و 67-file diff در `integration/runtime-operator-v1` ادغام شد؛ merge commit `e1626d7d1f2e62601329604b8823787f2c9597b6`. Full Foundation #38096870883، Quick push #38096870892، Quick PR #38096874251 و Runtime payload #38096874276 روی delivery SHA `f438758...` موفق شدند. بستهٔ داخلی `GameNet5-RuntimeLanSmoke-f4387584d6c1.zip` با SHA-256 `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`; outer artifact digest `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. پس از merge نیز Full Foundation #38097484132، Quick push #38097484122 و Quick PR #38097487320 موفق‌اند. ZIP بستهٔ Runtime است، نه Setup/MSI؛ نصب Windows Service/دو-PC LAN همچنان آزمایش نشده. | delivery و post-merge automated gates green؛ physical acceptance خارج از این مرحله | سرویس نصب‌شده و LAN در مرحلهٔ پذیرش نهایی، پس از تکمیل قابلیت‌ها و ساخت Setup.exe/MSI. |
| G-09 | P1 قبل از Release | مدل گواهی فعلی، private key غیرقابل‌استخراج در LocalMachine\My و trust کردن public .cer پس از SHA-256 مستقل است. چرخهٔ rotation/revocation/recovery و رفتار cert منقضی هنوز باید گواهی شود. | بخشی خودکار آزمون شده؛ lifecycle کامل باز | آزمون rotation بدون trust bypass، SAN mismatch/expiry، rollback/recovery و مجوز فایل/کلید تحت حساب سرویس واقعی. |
| G-10 | P0 پیش از نصاب | انتخاب اینکه PostgreSQL از قبل نصب‌شده فرض می‌شود یا نصاب آن را provision می‌کند هنوز تصمیم اجرایی لازم دارد. writer تنظیمات به‌تنهایی PostgreSQL/database role را نصب یا امن‌سازی نمی‌کند. | تصمیم باز | نسخهٔ پشتیبانی‌شده، service identity، database role، data path، backup/upgrade و migration identity صریح شوند؛ Production startup هرگز خودکار migrate نکند. |
| G-11 | P0 پیش از Release | ZIPهای self-contained خروجی خام‌اند، نه Setup.exe/MSI؛ Windows service registration، repair، upgrade، updater و rollback نصب واقعی گواهی نشده‌اند. | باز؛ tracked در [Issue #16](https://github.com/alizebal73/5/issues/16) | install/repair/upgrade، package checksum/signature، health gate، rollback و uninstall بدون حذف ناخواستهٔ business data روی ماشین تمیز. |
| G-12 | P0 پیش از Business vertical بعدی | Customer/Session/Tariff/Billing/Wallet/Inventory/Buffet/VIP/Reports/Shift/Approval کامل در snapshot فعال حاضر نیستند. | نیازمندی برنامه‌ریزی‌شده، نه فراموش‌شده | هر module مطابق requirement: مالک داده، permission، state machine، transaction، idempotency/concurrency، audit، failure/recovery، API/UI و تست PostgreSQL واقعی؛ هیچ financial authority در Desktop. |
| G-19 | P1 قبل از هر برش تجاری | ماتریس ۶۳ requirement ردیف دارد، اما داشتن ID و معیار کلی به‌تنهایی trace کامل requirement→slice→کد/API→تست→شاهد نیست. | باید در هر برش تکمیل شود | برای هر برش، DoR/vertical-slice ثبت کند: REQ IDs، invariant، permission، migration/data owner، transaction/idempotency/race، audit، failure/recovery، آزمون خودکار برای هر برش؛ آزمون فیزیکی/سرتاسری فقط در پذیرش نهایی پس از آماده‌شدن Setup.exe/MSI، exact SHA/evidence؛ هیچ ماژول کامل‌نشده را از روی وجود ردیف/فایل Done اعلام نکنید. |
| G-13 | P1 | Older-base Draft PRs remain, but #35–#39 were source/traceability records fully represented by merged PR #40. | #35–#39 and backward PR #23 are closed; #31 is also closed as superseded for runner changes. Branches retained. #32/#44 are merged. Six remaining Draft PRs (#15/#17/#18/#20/#21/#24) have explicit later-gate dispositions. | Compare exact diff and preserve unique evidence before closing/deleting any remaining source branch. |
| G-14 | P1 governance | All three live Rulesets are active and require the expected CI check; latest GitHub read-back confirms `strict_required_status_checks_policy=true`. They still set `required_approving_review_count=0`, so GitHub does not itself enforce a human approval. | Hosted Runner migration merged (#32 Foundation, #44 Integration); required checks are configured and strict. Human review remains an explicit operational gate unless a suitable approval rule is configured. | Continue exact-SHA review of each PR; do not merge without a documented review decision. If an independent reviewer becomes available, configure and verify an approval requirement. |
| G-15 | P1 security | مخزن عمومی است. این ممیزی ادعا نمی‌کند تمام تاریخچهٔ همهٔ branchها از نظر secret به‌صورت خودکار اسکن شده است. | بررسی مستقل لازم | history/branches/packageها با secret scanner بررسی؛ alerts/push protection فعال در صورت دسترسی؛ اگر secret واقعی یافت شد rotate/revoke اول، بعد پاک‌سازی تاریخچه با برنامه و هماهنگی. |
| G-16 | Deferred | طرح Steam/GamingAccounts یک ADR پیشنهادی است و PR #19 بسته شده؛ مجازبودن مدل حساب و روش integration هنوز شرط ورود است. | عمداً پیاده‌سازی نشده | نیاز محصول صریح + بررسی برنامهٔ رسمی/اجازهٔ Valve + تصمیم محصول/ADR؛ تا آن زمان هیچ credential injection، MFA bypass یا Auto Login فرضی ساخته نشود. |
| G-17 | P1 پیش از Release | Foundation برای PostgreSQL/migrations، audit/idempotency/outbox و backup/restore آزمون دارد؛ اما زمان‌بندی/retention، محل امن نسخه‌های پشتیبان و RPO/RTO محصولی هنوز کامل نشده‌اند. | بخشی Foundation موجود؛ عملیات کامل باز | restore دوره‌ای و ایزوله، retention، هشدار شکست، صلاحیت دسترسی، آزمون بازیابی/تطبیق و ثبت زمان/داده از دست‌رفته. |

## Ordered execution route — 2026-10-12 (post-merge)

1. **Code-integration checkpoint — completed:** PR #40 integrated runtime/operator code at `58717adc6c1553ef21221c3d484f093494ba1ae7`; candidate Quick, Full Foundation and Runtime payload passed, and post-merge checks passed on that exact merge SHA. The later runner/CI changes are separately recorded below; this line is the code-integration checkpoint, not the numbered Stage 1 in the Master Build Plan.
2. **Source PR traceability — completed:** PRs #35–#39 were closed as superseded after recording the disposition. Their source branches were retained; none was merged individually or deleted.
3. **Keep branch boundaries:** `main` remains untouched. Foundation SHA `71ef653...` is certified by #38182502053. Integration Runner PR #44 merged at `161b4c...`; exact post-merge Quick #38183458461 and Full Foundation #38183458486 passed. PR #31 is closed as superseded for runner changes; #15's TLS/runtime diff stays separate from workflow edits.
4. **Master Build Plan Stage 1 — Foundation certification — CLOSED:** [Full Foundation #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) passed on exact Foundation SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; [Stable Checkpoint](../operations/stable-checkpoint.md) records that commit. This exit criterion closes hosted Foundation certification; physical Runtime and release acceptance remain separate.
5. **Final acceptance prerequisites:** finish the planned features/modules, complete requirement traceability, keep automated CI green on the exact release candidate SHA, and build the real Setup.exe/MSI.
6. **Final Runtime acceptance:** after those prerequisites, inspect the read-only `inspect-manager-runtime-state.ps1` output, then use isolated Windows Server/Agent PCs and a disposable DB for service identity/ACLs, DPAPI/TLS after restart, enrollment replay/revoke, lease/heartbeat and LAN reconnect. Do not modify the existing Manager PC before preflight review and explicit approval.
7. **Final Desktop/installer acceptance:** verify WPF RTL/LTR, keyboard/focus/navigation, loading/offline/permission-denied and password clearing, along with Setup install/upgrade/repair/uninstall, updater/rollback, signed packages, SBOM and backup/recovery.
8. **Pilot and release:** only after final acceptance passes, run the staged 2–3-PC pilot and then the ~15-station pilot. Automated regression tests remain active throughout feature development.

## Branch and PR disposition — 2026-10-12 (post-merge)

| PR / branch | Current disposition |
|---|---|
| #40 — `refactor/outbox-desktop-safety-integration-v1` | **Merged by Squash** into `integration/runtime-operator-v1`. Merge SHA `58717adc6c1553ef21221c3d484f093494ba1ae7`; candidate SHA `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd`. Exact candidate Quick, Full Foundation and Runtime payload all passed; post-merge Quick #38181118466 and Full #38181118446 also passed. |
| #35 — Outbox worker/retry/dead-letter | Closed as superseded by #40; source branch retained, not deleted. |
| #36 — Agent health refresh coordinator | Closed as superseded by #40; source branch retained, not deleted. |
| #37 — permission-aware Station Board commands | Closed as superseded by #40; source branch retained, not deleted. |
| #38 — XAML localization/resource tests | Closed as superseded by #40; source branch retained, not deleted. |
| #39 — Desktop binding/PasswordBox tests | Closed as superseded by #40; source branch retained, not deleted. |
| #31 — hosted Windows CI/runtime stabilization | Closed without merge because #44 superseded overlapping runner/workflow changes. Source branch retained for later focused extraction of the test-only Service registration helper and runbook. |
| #32 — hosted Windows Foundation workflow | Merged as `71ef6534331e6ba5a8a460fd5b8846fe40e8567a`; exact post-merge Full Foundation #38182502053 passed. |
| #44 — Integration hosted Windows CI | Merged as `161b4cbbbe16cd3fd4046f174a1c09380139c67b`; candidate Quick push/PR, Full Foundation and Runtime payload passed; post-merge Quick #38183458461 and Full Foundation #38183458486 passed. |
| #15 — ProgramData endpoints and TLS | Older Foundation-based Draft. Preserve as evidence/source; compare with current TLS and selectively port only if still needed. |
| #23 — Runtime/operator integration | Closed as obsolete backward PR; not merged; source branch retained. Active Integration remains the delivery direction. |
| #24 — Desktop view extraction | Older-base Draft. Compare with the now-integrated UI changes and port only unique changes still needed. |
| #17 — LAN smoke evidence | Historical older process-based evidence; it does not satisfy installed-Service/two-PC acceptance. |
| #18 — secret lifecycle ADR | Older-base design proposal; ADR-0002 still needs an explicit decision. |
| #20 — DPAPI Server secret store | Stacked on #18/older base. Review/port against current tree after ADR/preflight; no provisioning or secret cleanup on the existing Manager PC before read-only report review. |
| #21 — Desktop operator shell | Older-base UI source; use only as reference after diff comparison, not as a wholesale merge. |

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


## CI trigger hardening follow-up — completed; record refreshed 2026-10-12

The earlier statement that PR #25 remained open and PR #27 still awaited CI was stale and has been superseded:

- PR [#25](https://github.com/alizebal73/5/pull/25) is closed and merged. Merge commit: `82d90178c0011c65eef76bd32cc33318d5bbd91c`.
- PRs [#27](https://github.com/alizebal73/5/pull/27), [#28](https://github.com/alizebal73/5/pull/28), and [#29](https://github.com/alizebal73/5/pull/29) are closed without merge. The bounded implementation was delivered through [PR #30](https://github.com/alizebal73/5/pull/30), merged at `e1626d7d1f2e62601329604b8823787f2c9597b6`.
- PR #30's recorded exact-head Quick, Full Foundation and Runtime Payload checks passed, and its post-merge automated checks are recorded in this register and the Active Execution Control. Those checks certify their recorded SHAs only.
- The six open Draft PRs verified on 2026-10-12 are #15, #17, #18, #20, #21 and #24. Their current dispositions are listed in [Active Execution Control](../operations/active-execution-control.md); do not infer status from this historical follow-up or merge older-base branches wholesale.

## Live branch-ruleset read-back — 2026-10-12

GitHub's live Ruleset responses were re-read for all three protected targets:

| Ruleset | Required status check | Active |
|---|---|---|
| `protect-main` | `quick-validation` | Yes |
| `protect-foundation-runtime` | `foundation` | Yes |
| `protect-integration-runtime` | `quick-validation` | Yes |

All three require pull requests, block branch deletion and non-fast-forward updates, require review-thread resolution, allow Squash as the only merge method, and report no bypass actors. The latest live GitHub read-back confirms `strict_required_status_checks_policy=true` on all three Rulesets, so the branch must be up to date with its base for required checks. However, every Ruleset has `required_approving_review_count=0`, `require_code_owner_review=false`, `required_reviewers=[]`, and `require_last_push_approval=false`; GitHub does not enforce an approving human review. This remains a governance gap for the no-unreviewed-merge policy, not an application-code defect.

The available GitHub connection can read these Rulesets but does not expose a Ruleset-write operation, so this audit did not change repository settings. Strict/up-to-date-before-merge is already enabled on all three. Before regular feature PR merging expands, enforce the stated no-unreviewed-merge process manually, or configure an appropriate approving-review requirement when a suitable independent reviewer is available. Do not guess required-check names or remove the existing checks.
