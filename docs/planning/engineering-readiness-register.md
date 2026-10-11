# Engineering Readiness Register — GameNet 5

این سند، دفتر زندهٔ ریسک‌ها و ترتیب اجرای کار است. تاریخ snapshot: 2026-10-11. این دفتر جایگزین Requirementها، ADRها یا گواهی Foundation نیست؛ آن‌ها را به ترتیب اجرایی و وضعیت واقعی وصل می‌کند. هر ادعای Done باید به commit، آزمون و مدرک همان SHA متصل باشد.

## Current verified baseline — 2026-10-12

- Repository: `alizebal73/5`; default branch: `main`.
- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, protected. Keep unchanged.
- `foundation/runtime-final-v2`: tip `be29687e637b709158a30204bb9213bfa4813950`, protected. The last explicit Stable Checkpoint certification remains `c3a8ba482548e963479fbdf8538be62c4d15acfe`; no successful run for the exact current Foundation tip was found in the latest read-back, so re-certify that SHA before calling it the current certified baseline.
- `integration/runtime-operator-v1`: current tip `82d90178c0011c65eef76bd32cc33318d5bbd91c`, protected. Its Quick Validation passes on that exact tip: [push #38097833205](https://github.com/alizebal73/5/actions/runs/38097833205) and [PR #38097836002](https://github.com/alizebal73/5/actions/runs/38097836002).
- Reconciled candidate PR [#40](https://github.com/alizebal73/5/pull/40), branch `refactor/outbox-desktop-safety-integration-v1`, was at SHA `a0944c33c6ff24df7a518d4ab0f108f298e51dbc`. That prior SHA passed [Quick Validation #38178587675](https://github.com/alizebal73/5/actions/runs/38178587675), [Full Foundation #38178583532](https://github.com/alizebal73/5/actions/runs/38178583532), a second full run [#38179024615](https://github.com/alizebal73/5/actions/runs/38179024615) and [Runtime payload #38178587734](https://github.com/alizebal73/5/actions/runs/38178587734). Final review found missing handler-level busy guards for direct `ICommand.Execute`; this follow-up adds those guards and a regression test, so all gates must pass again on the new exact HEAD.
- The candidate combines the exact 42 changed paths from source PRs #35–#39, with no omitted or extra paths. Keep those source PRs unmerged individually; their station-board, workflow and append-only documentation changes overlap.
- PRs #15, #31 and #32 are separate Drafts with overlapping CI/workflow/PostgreSQL-verification paths. The reconciled candidate currently uses `runs-on: [self-hosted, Windows, X64]`; this is not hosted-runner certification. Review their unique changes as a separate bounded maintenance sequence.
- CI still does not prove actual installed Windows Service identity, effective ACLs, DPAPI/private-key access after restart, physical two-PC LAN/reconnect or interactive WPF RTL/focus behavior. The Runtime ZIP is not Setup.exe/MSI.
- No merge into `main` or `foundation/runtime-final-v2`, no change to the installed Manager PC, and no production provisioning/ACL/TLS cleanup is authorized by these CI results.

## Active gap register

| ID | اولویت | موضوع و شاهد فعلی | وضعیت | معیار پایان |
|---|---|---|---|---|
| G-01 | P1 | Roadmap/governance docs had stale branch and stage snapshots; this follow-up aligns the reference sections with the GitHub read-back from 2026-10-12. | Updated in this candidate; latest-head validation required | Keep the live SHA/PR/check links aligned after merge; retain the old SHA statements only as explicitly historical evidence. |
| G-02 | P1 | PR #24 only extracts the StationBoard view. PR #40 adds the health-refresh coordinator, but Login/Password views and the shell content host are not yet extracted. | First slices implemented in the integration candidate; more view boundaries remain | Continue small view extractions with binding/command regression tests, then perform interactive WPF acceptance. |
| G-03 | P1 | Health timer coordinator, cancellation through the Server request and stale-response fencing are implemented in #40. Previous candidate SHA a0944c33... passed the four CI runs; final review found a separate handler busy-gate gap. | Handler guard + direct-Execute regression added; new exact HEAD must be retested | Quick Validation, Full Foundation and Runtime payload must pass on one exact current SHA; real WPF/Service/LAN remain separate gates. |
| G-04 | P1 | Effective permissions are wired from the authenticated Server snapshot into Station Board commands and visibility. Review found that direct ICommand.Execute can bypass CanExecute, including the busy gate; guards/tests are now added inside handlers. | Follow-up validation pending on the new exact HEAD | Verify read/operate matrix, direct Execute, busy gate, logout/cache clear and stale-response behavior; Server remains authoritative. |
| G-05 | P1 | Static resource/localization parity, Binding path checks and PasswordBox contract tests are included. They do not prove interactive RTL/LTR, focus, keyboard or navigation behavior. | Prior candidate was green; follow-up exact-head gates are required | Pass exact-head CI and then the interactive Windows UI smoke across RTL/LTR and loading/offline/permission-denied states. |
| G-06 | P0 | حذف Bootstrap اولیه اکنون در `scripts/finalize-server-setup.ps1` پیاده‌سازی شده و تست خواندن Protected Settings بدون آن وجود دارد؛ کاندید ترکیبی در CI سبز است. اجرای واقعی replacement/ACL/DPAPI و بررسی startup پس از cleanup تحت هویت Windows Service هنوز انجام نشده است. | کد و آزمون خودکار حاضر؛ پذیرش عملی باز | روی Server آزمایشی جدا، پس از ثبت Owner، فقط همان کلید حذف شود؛ payload موقت با DPAPI بازخوانی و بررسی شود، replacement اتمیک/ACL تأیید و startup/readiness بعد از restart سرویس آزموده شود؛ شکست باید فایل اصلی را سالم نگه دارد. |
| G-18 | P0 | مخزن اسرار Server با DPAPI LocalMachine، نوع‌های مستقل برای connection/JWT/Agent provisioning secret، Production fail-closed، ACL مبتنی بر Service SID و حذف Bootstrap پس از ساخت Owner در tree ادغام‌شده موجود است. Full Foundation روی source candidate #29، delivery PR #30 و merge commit `e1626d7...` موفق است. ADR-0002 همچنان Proposed است؛ DPAPI/ACL زیر Windows Service نصب‌شده، private-key پس از restart، rotation و recovery فیزیکی اثبات نشده‌اند. | کد و exact-SHA CI سبز؛ سرویس واقعی بیرون از این مرحله | پیش از انتشار، ADR-0002 و شواهد سرویس واقعی لازم است؛ تغییر Manager واقعی بدون preflight ممنوع. |
| G-07 | P0 | ثبت Agent توکن ۲۵۶بیتی ۱۵دقیقه‌ای، hash-only persistence، رد replay/revoke/expiry و recovery محدود با reason/audit در Integration ادغام شده است. اصلاح lifecycle، فایل توکن محافظت‌شده را تا موفقیت احراز عادی نگه می‌دارد؛ regression test و Agent runtime certification روی source، delivery PR و merge commit موفق‌اند. | کد و تست‌های خودکار green؛ نصب Windows Service/DPAPI زیر LocalService پس از restart خارج از محدوده | هویت/ACL/DPAPI سرویس واقعی و restart جداگانه تأیید شوند. |
| G-08 | P0 | PR #30 با یک commit و 67-file diff در `integration/runtime-operator-v1` ادغام شد؛ merge commit `e1626d7d1f2e62601329604b8823787f2c9597b6`. Full Foundation #38096870883، Quick push #38096870892، Quick PR #38096874251 و Runtime payload #38096874276 روی delivery SHA `f438758...` موفق شدند. بستهٔ داخلی `GameNet5-RuntimeLanSmoke-f4387584d6c1.zip` با SHA-256 `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`; outer artifact digest `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. پس از merge نیز Full Foundation #38097484132، Quick push #38097484122 و Quick PR #38097487320 موفق‌اند. ZIP بستهٔ Runtime است، نه Setup/MSI؛ نصب Windows Service/دو-PC LAN همچنان آزمایش نشده. | delivery و post-merge automated gates green؛ physical acceptance خارج از این مرحله | سرویس نصب‌شده و LAN جداگانه، طبق برنامهٔ بعدی. |
| G-09 | P1 قبل از Release | مدل گواهی فعلی، private key غیرقابل‌استخراج در LocalMachine\My و trust کردن public .cer پس از SHA-256 مستقل است. چرخهٔ rotation/revocation/recovery و رفتار cert منقضی هنوز باید گواهی شود. | بخشی خودکار آزمون شده؛ lifecycle کامل باز | آزمون rotation بدون trust bypass، SAN mismatch/expiry، rollback/recovery و مجوز فایل/کلید تحت حساب سرویس واقعی. |
| G-10 | P0 پیش از نصاب | انتخاب اینکه PostgreSQL از قبل نصب‌شده فرض می‌شود یا نصاب آن را provision می‌کند هنوز تصمیم اجرایی لازم دارد. writer تنظیمات به‌تنهایی PostgreSQL/database role را نصب یا امن‌سازی نمی‌کند. | تصمیم باز | نسخهٔ پشتیبانی‌شده، service identity، database role، data path، backup/upgrade و migration identity صریح شوند؛ Production startup هرگز خودکار migrate نکند. |
| G-11 | P0 پیش از Release | ZIPهای self-contained خروجی خام‌اند، نه Setup.exe/MSI؛ Windows service registration، repair، upgrade، updater و rollback نصب واقعی گواهی نشده‌اند. | باز؛ tracked در [Issue #16](https://github.com/alizebal73/5/issues/16) | install/repair/upgrade، package checksum/signature، health gate، rollback و uninstall بدون حذف ناخواستهٔ business data روی ماشین تمیز. |
| G-12 | P0 پیش از Business vertical بعدی | Customer/Session/Tariff/Billing/Wallet/Inventory/Buffet/VIP/Reports/Shift/Approval کامل در snapshot فعال حاضر نیستند. | نیازمندی برنامه‌ریزی‌شده، نه فراموش‌شده | هر module مطابق requirement: مالک داده، permission، state machine، transaction، idempotency/concurrency، audit، failure/recovery، API/UI و تست PostgreSQL واقعی؛ هیچ financial authority در Desktop. |
| G-19 | P1 قبل از هر برش تجاری | ماتریس ۶۳ requirement ردیف دارد، اما داشتن ID و معیار کلی به‌تنهایی trace کامل requirement→slice→کد/API→تست→شاهد نیست. | باید در هر برش تکمیل شود | برای هر برش، DoR/vertical-slice ثبت کند: REQ IDs، invariant، permission، migration/data owner، transaction/idempotency/race، audit، failure/recovery، automated + physical tests (در صورت نیاز)، exact SHA/evidence؛ هیچ ماژول کامل‌نشده را از روی وجود ردیف/فایل Done اعلام نکنید. |
| G-13 | P1 | Open Drafts #15/#17/#18/#20/#21/#23/#24/#31/#32/#35–#40 have different bases; several share workflow, docs or UI files. | #40 reconciliation reviewed; remaining disposition is per-PR | After #40 is merged and validated, close #35–#39 as superseded with traceability comments but retain branches; review old-base PRs for unique evidence before close/delete. |
| G-14 | P1 governance | Rulesets for main, Foundation and Integration are active; Integration now reports protected=true and requires PR + quick-validation + conversation resolution. All three still have strict/up-to-date policy false. | Base protection active; strict-status hardening remains | When administrative write access is available, review up-to-date-before-merge policy. This audit did not change rulesets. |
| G-15 | P1 security | مخزن عمومی است. این ممیزی ادعا نمی‌کند تمام تاریخچهٔ همهٔ branchها از نظر secret به‌صورت خودکار اسکن شده است. | بررسی مستقل لازم | history/branches/packageها با secret scanner بررسی؛ alerts/push protection فعال در صورت دسترسی؛ اگر secret واقعی یافت شد rotate/revoke اول، بعد پاک‌سازی تاریخچه با برنامه و هماهنگی. |
| G-16 | Deferred | طرح Steam/GamingAccounts یک ADR پیشنهادی است و PR #19 بسته شده؛ مجازبودن مدل حساب و روش integration هنوز شرط ورود است. | عمداً پیاده‌سازی نشده | نیاز محصول صریح + بررسی برنامهٔ رسمی/اجازهٔ Valve + تصمیم محصول/ADR؛ تا آن زمان هیچ credential injection، MFA bypass یا Auto Login فرضی ساخته نشود. |
| G-17 | P1 پیش از Release | Foundation برای PostgreSQL/migrations، audit/idempotency/outbox و backup/restore آزمون دارد؛ اما زمان‌بندی/retention، محل امن نسخه‌های پشتیبان و RPO/RTO محصولی هنوز کامل نشده‌اند. | بخشی Foundation موجود؛ عملیات کامل باز | restore دوره‌ای و ایزوله، retention، هشدار شکست، صلاحیت دسترسی، آزمون بازیابی/تطبیق و ثبت زمان/داده از دست‌رفته. |

## Ordered execution route — 2026-10-12

1. **Close the #40 code-review gap:** guard every Station Board handler against direct command execution while busy and keep the regression test. Do not merge until Quick Validation, Full Foundation and Runtime payload pass on one exact latest candidate SHA.
2. **Integrate once, not five times:** PR #40 is the single reconciled candidate containing all 42 paths from #35–#39. After final diff inspection, mark #40 ready and Squash-merge only into `integration/runtime-operator-v1`; never merge #35–#39 individually.
3. **Prove the resulting target SHA:** read back the target branch and run/read back Quick Validation, Full Foundation and the relevant Runtime payload after merge. Preserve `main` and `foundation/runtime-final-v2`.
4. **Resolve workflow-maintenance overlap separately:** review #15/#31/#32 as bounded changes. The candidate currently uses a self-hosted Windows runner; do not describe it as hosted-runner certified and do not merge overlapping workflows wholesale.
5. **Re-certify Foundation baseline:** run Full Foundation on the exact current `foundation/runtime-final-v2` tip before declaring it the active certified checkpoint. Keep the historical Stable Checkpoint tied to its actual SHA.
6. **Run physical Runtime acceptance:** first review the read-only `inspect-manager-runtime-state.ps1` output; then use isolated Windows Server/Agent PCs and a disposable database to verify service identity/ACLs, DPAPI and TLS private-key access after restart, enrollment replay/revoke, lease/heartbeat and LAN reconnect. Do not perform provisioning or ACL/TLS cleanup on the existing Manager PC before preflight approval.
7. **Complete interactive Desktop acceptance:** WPF RTL/LTR, keyboard/focus, navigation, loading/offline/permission-denied and password clearing.
8. **Then build product slices:** follow the Master Build Plan order and complete Customers, Tariffs, Sessions, Billing/Wallet/Debt, Inventory/Buffet and subsequent modules with REQ-to-code/test/evidence traceability.
9. **Release remains separate:** Setup/MSI, install/upgrade/repair/uninstall, updater/rollback, signed packages, tested backup recovery, 2–3-PC test and then ~15-station pilot.

## Branch and PR disposition — 2026-10-12

| PR / branch | Current disposition |
|---|---|
| #40 — `refactor/outbox-desktop-safety-integration-v1` | Single integration candidate for Outbox + Desktop changes. Prior SHA `a0944c33...` passed all four runs, but review found a busy-state handler bypass; this follow-up adds guards and a regression test. Re-run Quick Validation, Full Foundation and Runtime payload on the new exact HEAD. If green and the diff remains acceptable, Squash-merge only to `integration/runtime-operator-v1`, then validate the merge SHA. |
| #35 — Outbox worker/retry/dead-letter | Source/traceability PR; overlaps the Outbox portion of #40. Do not merge independently. After successful #40 merge, close as superseded with a comment and retain the source branch until evidence disposition is complete. |
| #36 — Agent health refresh coordinator | Source/traceability PR; its changes are carried into #40. Do not merge independently. Close as superseded only after #40 has merged and been verified; do not delete the branch yet. |
| #37 — permission-aware Station Board commands | Source/traceability PR; changes are reconciled in #40 plus the handler-level busy fix. Do not merge independently. |
| #38 — XAML localization/resource tests | Source/traceability PR, carried into #40. Do not merge independently. |
| #39 — Desktop binding/PasswordBox tests | Source/traceability PR, carried into #40. Do not merge independently. |
| #31 — hosted Windows CI/runtime stabilization | Separate Draft based on Integration. Overlaps #40 in workflow/readiness/PostgreSQL verifier paths. Review unique CI changes after the product candidate is integrated; no wholesale merge. |
| #32 — hosted Windows Foundation workflow | Separate Draft targeting Foundation; overlaps #15/#31 workflow paths. Review separately and preserve the rule that a PR candidate run does not certify its unchanged Foundation base tip. |
| #15 — ProgramData endpoints and TLS | Older Foundation-based Draft with unique TLS/endpoint changes but overlapping workflows. Preserve as evidence/source; compare with current TLS and selectively port only if still needed. Never merge wholesale to Integration. |
| #23 — Runtime/operator integration | Draft whose head is the Integration branch at `82d90178...` and whose base is the older operator-identity branch. Keep this relationship explicit; no direct merge into Foundation/main. |
| #24 — Desktop view extraction | Draft on a divergent older base. The initial StationBoard view extraction remains separately traceable; compare against #40 and port only unique changes still needed. |
| #17 — LAN smoke evidence | Historical, older runtime/process-based evidence. It does not satisfy current installed-Service/two-PC acceptance. Do not merge its old branch wholesale. |
| #18 — secret lifecycle ADR | Older-base design proposal. Decide ADR-0002 separately; a proposed ADR is not accepted policy without an explicit decision. |
| #20 — DPAPI Server secret store | Stacked on #18/older base. Review and selectively port against the current tree after ADR/preflight; no machine provisioning or secret cleanup before the read-only Manager report is reviewed. |
| #21 — Desktop operator shell | Older-base UI source. Use only as reference after diff comparison; no wholesale merge. |

## Required close-out after #40 merge

- Confirm target branch HEAD equals the returned Squash merge SHA.
- Confirm required `quick-validation` and full Foundation/runtime gates on the resulting exact tree, not only on the PR head.
- Leave `main` and `foundation/runtime-final-v2` unchanged.
- Close #35–#39 as superseded only after preserving source PR links/evidence; do not delete their branches as part of this step.
- Update this register and branch-governance with the actual post-merge SHA and check URLs before calling the integration stage Done.

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
