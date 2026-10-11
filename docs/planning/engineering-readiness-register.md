# Engineering Readiness Register — GameNet 5

این سند، دفتر زندهٔ ریسک‌ها و ترتیب اجرای کار است. تاریخ snapshot: 2026-10-11. این دفتر جایگزین Requirementها، ADRها یا گواهی Foundation نیست؛ آن‌ها را به ترتیب اجرایی و وضعیت واقعی وصل می‌کند. هر ادعای Done باید به commit، آزمون و مدرک همان SHA متصل باشد.

## Current verified baseline — 2026-10-12 (post-merge read-back)

- Repository: `alizebal73/5`; default branch: `main`.
- `main`: `b9288471f2047570eaf8d0d6552cf87bc0ddc214`, protected and deliberately unchanged.
- `foundation/runtime-final-v2`: current tip `be29687e637b709158a30204bb9213bfa4813950`, protected and unchanged. Last explicitly recorded Stable Checkpoint certification remains `c3a8ba482548e963479fbdf8538be62c4d15acfe`; dispatch Full Foundation at the exact current tip before advancing the active certification marker.
- `integration/runtime-operator-v1`: current tip `362513ad812aebef4ad22420f9b905d79490219d`, protected. Runtime source merge is `58717adc6c1553ef21221c3d484f093494ba1ae7`; PR #41 added docs-only changes after it.
- PR [#40](https://github.com/alizebal73/5/pull/40) source candidate `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd` passed [Quick #38180651116](https://github.com/alizebal73/5/actions/runs/38180651116), [Full Foundation #38180646187](https://github.com/alizebal73/5/actions/runs/38180646187) and [Runtime payload #38180651104](https://github.com/alizebal73/5/actions/runs/38180651104). The merge SHA `58717adc...` passed [Quick #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) and [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446).
- PR [#41](https://github.com/alizebal73/5/pull/41) was docs-only, merged at `362513ad...`; its candidate passed [Quick #38181520088](https://github.com/alizebal73/5/actions/runs/38181520088) and the current Integration tip passed [Quick #38181599202](https://github.com/alizebal73/5/actions/runs/38181599202). We do not claim Full Foundation ran on the later docs-only SHA.
- Candidate test counts on #40's exact verified code head: Desktop 36, Server unit 84, Shared 38, Agent 33 and Contract 1; zero failures/skips in those suites. The handler-level busy-gate regression test is included.
- Source PRs #35–#39 are closed as superseded by #40; their branches are retained.
- PRs #15/#31/#32 are overlapping runner/runtime proposals and must follow the ordered disposition in [Active Execution Control](../operations/active-execution-control.md). The successful #40 Foundation run used `[self-hosted, Windows, X64]`; it is not hosted-runner certification.
- Automated CI does not prove installed Windows Service identity/effective ACLs, DPAPI/private-key access after restart, physical two-PC LAN/reconnect, or interactive WPF RTL/focus behavior. Do not change the installed Manager PC without read-only preflight review and approval.



## Active gap register

| ID | اولویت | موضوع و شاهد فعلی | وضعیت | معیار پایان |
|---|---|---|---|---|
| G-01 | P1 | Current source of truth moved through PR #40 (runtime code) and PR #41 (docs only); stale branch snapshots caused conflicting reports. | Exact current refs and evidence aligned to Integration tip `362513ad...`; future changes must update the active execution control and referenced current-state docs in one focused docs PR. | Current SHA and gate ledger must agree across the active execution control, roadmap, readiness register and branch governance. |
| G-02 | P1 | PR #24 only extracts the StationBoard view. PR #40 adds the health-refresh coordinator, but Login/Password views and the shell content host are not yet extracted. | First slices implemented in the integration candidate; more view boundaries remain | Continue small view extractions with binding/command regression tests, then perform interactive WPF acceptance. |
| G-03 | P1 | Agent health refresh coordinator, cancellation through the Server request and stale-response fencing are included in PR #40 and merged at `58717adc...`. | Exact candidate Quick/Full/payload and post-merge Quick/Full all passed; installed-Service/UI/LAN acceptance remains open. | Interactive timer/UI checks and physical Service/LAN acceptance remain separate from CI. |
| G-04 | P1 | Effective permissions are wired from the authenticated Server snapshot into Station Board commands. Handler-level busy guards now prevent direct `ICommand.Execute` from bypassing the busy policy; regression test is included. | Merged in #40; exact-head candidate and post-merge automated gates passed. Server remains the authorization authority. | Keep permission regression tests on future handlers and independently validate Server authorization for each new capability. |
| G-05 | P1 | XAML localization/resource parity, binding-path and PasswordBox lifecycle contracts have static regression coverage in #40. | Exact candidate and post-merge automation passed; interactive RTL/LTR, focus, keyboard and navigation are still unverified. | Perform Windows interactive smoke for RTL/LTR and loading/offline/permission-denied states. |
| G-06 | P0 | حذف Bootstrap اولیه اکنون در `scripts/finalize-server-setup.ps1` پیاده‌سازی شده و تست خواندن Protected Settings بدون آن وجود دارد؛ کاندید ترکیبی در CI سبز است. اجرای واقعی replacement/ACL/DPAPI و بررسی startup پس از cleanup تحت هویت Windows Service هنوز انجام نشده است. | کد و آزمون خودکار حاضر؛ پذیرش عملی باز | روی Server آزمایشی جدا، پس از ثبت Owner، فقط همان کلید حذف شود؛ payload موقت با DPAPI بازخوانی و بررسی شود، replacement اتمیک/ACL تأیید و startup/readiness بعد از restart سرویس آزموده شود؛ شکست باید فایل اصلی را سالم نگه دارد. |
| G-18 | P0 | مخزن اسرار Server با DPAPI LocalMachine، نوع‌های مستقل برای connection/JWT/Agent provisioning secret، Production fail-closed، ACL مبتنی بر Service SID و حذف Bootstrap پس از ساخت Owner در tree ادغام‌شده موجود است. Full Foundation روی source candidate #29، delivery PR #30 و merge commit `e1626d7...` موفق است. ADR-0002 همچنان Proposed است؛ DPAPI/ACL زیر Windows Service نصب‌شده، private-key پس از restart، rotation و recovery فیزیکی اثبات نشده‌اند. | کد و exact-SHA CI سبز؛ سرویس واقعی بیرون از این مرحله | پیش از انتشار، ADR-0002 و شواهد سرویس واقعی لازم است؛ تغییر Manager واقعی بدون preflight ممنوع. |
| G-07 | P0 | ثبت Agent توکن ۲۵۶بیتی ۱۵دقیقه‌ای، hash-only persistence، رد replay/revoke/expiry و recovery محدود با reason/audit در Integration ادغام شده است. اصلاح lifecycle، فایل توکن محافظت‌شده را تا موفقیت احراز عادی نگه می‌دارد؛ regression test و Agent runtime certification روی source، delivery PR و merge commit موفق‌اند. | کد و تست‌های خودکار green؛ نصب Windows Service/DPAPI زیر LocalService پس از restart خارج از محدوده | هویت/ACL/DPAPI سرویس واقعی و restart جداگانه تأیید شوند. |
| G-08 | P0 | PR #30 با یک commit و 67-file diff در `integration/runtime-operator-v1` ادغام شد؛ merge commit `e1626d7d1f2e62601329604b8823787f2c9597b6`. Full Foundation #38096870883، Quick push #38096870892، Quick PR #38096874251 و Runtime payload #38096874276 روی delivery SHA `f438758...` موفق شدند. بستهٔ داخلی `GameNet5-RuntimeLanSmoke-f4387584d6c1.zip` با SHA-256 `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`; outer artifact digest `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. پس از merge نیز Full Foundation #38097484132، Quick push #38097484122 و Quick PR #38097487320 موفق‌اند. ZIP بستهٔ Runtime است، نه Setup/MSI؛ نصب Windows Service/دو-PC LAN همچنان آزمایش نشده. | delivery و post-merge automated gates green؛ physical acceptance خارج از این مرحله | سرویس نصب‌شده و LAN جداگانه، طبق برنامهٔ بعدی. |
| G-09 | P1 قبل از Release | مدل گواهی فعلی، private key غیرقابل‌استخراج در LocalMachine\My و trust کردن public .cer پس از SHA-256 مستقل است. چرخهٔ rotation/revocation/recovery و رفتار cert منقضی هنوز باید گواهی شود. | بخشی خودکار آزمون شده؛ lifecycle کامل باز | آزمون rotation بدون trust bypass، SAN mismatch/expiry، rollback/recovery و مجوز فایل/کلید تحت حساب سرویس واقعی. |
| G-10 | P0 پیش از نصاب | انتخاب اینکه PostgreSQL از قبل نصب‌شده فرض می‌شود یا نصاب آن را provision می‌کند هنوز تصمیم اجرایی لازم دارد. writer تنظیمات به‌تنهایی PostgreSQL/database role را نصب یا امن‌سازی نمی‌کند. | تصمیم باز | نسخهٔ پشتیبانی‌شده، service identity، database role، data path، backup/upgrade و migration identity صریح شوند؛ Production startup هرگز خودکار migrate نکند. |
| G-11 | P0 پیش از Release | ZIPهای self-contained خروجی خام‌اند، نه Setup.exe/MSI؛ Windows service registration، repair، upgrade، updater و rollback نصب واقعی گواهی نشده‌اند. | باز؛ tracked در [Issue #16](https://github.com/alizebal73/5/issues/16) | install/repair/upgrade، package checksum/signature، health gate، rollback و uninstall بدون حذف ناخواستهٔ business data روی ماشین تمیز. |
| G-12 | P0 پیش از Business vertical بعدی | Customer/Session/Tariff/Billing/Wallet/Inventory/Buffet/VIP/Reports/Shift/Approval کامل در snapshot فعال حاضر نیستند. | نیازمندی برنامه‌ریزی‌شده، نه فراموش‌شده | هر module مطابق requirement: مالک داده، permission، state machine، transaction، idempotency/concurrency، audit، failure/recovery، API/UI و تست PostgreSQL واقعی؛ هیچ financial authority در Desktop. |
| G-19 | P1 قبل از هر برش تجاری | ماتریس ۶۳ requirement ردیف دارد، اما داشتن ID و معیار کلی به‌تنهایی trace کامل requirement→slice→کد/API→تست→شاهد نیست. | باید در هر برش تکمیل شود | برای هر برش، DoR/vertical-slice ثبت کند: REQ IDs، invariant، permission، migration/data owner، transaction/idempotency/race، audit، failure/recovery، automated + physical tests (در صورت نیاز)، exact SHA/evidence؛ هیچ ماژول کامل‌نشده را از روی وجود ردیف/فایل Done اعلام نکنید. |
| G-13 | P1 | Older-base Draft PRs remain, but #35–#39 were source/traceability records fully represented by merged PR #40. | #35–#39 closed as superseded; branches retained. #15/#17/#18/#20/#21/#23/#24/#31/#32 require separate disposition. | Compare ancestry/diff and preserve unique evidence before closing/deleting any remaining source branch. |
| G-14 | P1 governance | Rulesets for main, Foundation and Integration are active; Integration is protected at `58717adc...` and requires PR + `quick-validation` + conversation resolution. All three rulesets have strict/up-to-date policy false. | Base protections active; strict-status hardening still open, and this read-only pass did not change rulesets. | Review up-to-date-before-merge policy when administrative write access is available. |
| G-15 | P1 security | مخزن عمومی است. این ممیزی ادعا نمی‌کند تمام تاریخچهٔ همهٔ branchها از نظر secret به‌صورت خودکار اسکن شده است. | بررسی مستقل لازم | history/branches/packageها با secret scanner بررسی؛ alerts/push protection فعال در صورت دسترسی؛ اگر secret واقعی یافت شد rotate/revoke اول، بعد پاک‌سازی تاریخچه با برنامه و هماهنگی. |
| G-16 | Deferred | طرح Steam/GamingAccounts یک ADR پیشنهادی است و PR #19 بسته شده؛ مجازبودن مدل حساب و روش integration هنوز شرط ورود است. | عمداً پیاده‌سازی نشده | نیاز محصول صریح + بررسی برنامهٔ رسمی/اجازهٔ Valve + تصمیم محصول/ADR؛ تا آن زمان هیچ credential injection، MFA bypass یا Auto Login فرضی ساخته نشود. |
| G-17 | P1 پیش از Release | Foundation برای PostgreSQL/migrations، audit/idempotency/outbox و backup/restore آزمون دارد؛ اما زمان‌بندی/retention، محل امن نسخه‌های پشتیبان و RPO/RTO محصولی هنوز کامل نشده‌اند. | بخشی Foundation موجود؛ عملیات کامل باز | restore دوره‌ای و ایزوله، retention، هشدار شکست، صلاحیت دسترسی، آزمون بازیابی/تطبیق و ثبت زمان/داده از دست‌رفته. |

## Ordered execution route — 2026-10-12 (post-merge)

1. **Stage-one integration — completed:** PR #40 was Squash-merged into `integration/runtime-operator-v1` at `58717adc6c1553ef21221c3d484f093494ba1ae7`. The candidate at `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd` passed Quick Validation, Full Foundation and Runtime payload. Post-merge [Quick #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) and [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446) also passed on the exact merge SHA.
2. **Source PR traceability — completed:** PRs #35–#39 were closed as superseded after recording the disposition. Their source branches were retained; none was merged individually or deleted.
3. **Keep branch boundaries:** `main` stays at `b9288471...`, Foundation stays at `be29687e...`, and Integration stays on reviewed changes only. Review #15/#31/#32 as separate, bounded Runner/TLS maintenance changes; no wholesale merge of overlapping workflow files.
4. **Re-certify Foundation baseline:** run Full Foundation on the exact current `foundation/runtime-final-v2` tip before calling it the active certified checkpoint. The historical Stable Checkpoint remains tied to its recorded SHA.
5. **Physical Runtime acceptance:** first review the read-only `inspect-manager-runtime-state.ps1` output; then use isolated Windows Server/Agent PCs and a disposable DB to verify service identity/ACLs, DPAPI and TLS private-key access after restart, enrollment replay/revoke, lease/heartbeat and LAN reconnect. Do not perform provisioning or ACL/TLS cleanup on the existing Manager PC before preflight approval.
6. **Interactive Desktop acceptance:** test WPF RTL/LTR, keyboard/focus, navigation, loading/offline/permission-denied and password clearing on Windows.
7. **Product slices:** after the Runtime acceptance gate, follow the Master Build Plan order with requirement-to-code/test/evidence traceability for Customers, Tariffs, Sessions, Billing/Wallet/Debt, Inventory/Buffet and later modules.
8. **Release remains separate:** Setup/MSI, install/upgrade/repair/uninstall, updater/rollback, signed packages, tested backup recovery, 2–3-PC test and then ~15-station pilot remain mandatory.

## Branch and PR disposition — 2026-10-12 (post-merge)

| PR / branch | Current disposition |
|---|---|
| #40 — `refactor/outbox-desktop-safety-integration-v1` | **Merged by Squash** into `integration/runtime-operator-v1`. Merge SHA `58717adc6c1553ef21221c3d484f093494ba1ae7`; candidate SHA `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd`. Exact candidate Quick, Full Foundation and Runtime payload all passed; post-merge Quick #38181118466 and Full #38181118446 also passed. |
| #35 — Outbox worker/retry/dead-letter | Closed as superseded by #40; source branch retained, not deleted. |
| #36 — Agent health refresh coordinator | Closed as superseded by #40; source branch retained, not deleted. |
| #37 — permission-aware Station Board commands | Closed as superseded by #40; source branch retained, not deleted. |
| #38 — XAML localization/resource tests | Closed as superseded by #40; source branch retained, not deleted. |
| #39 — Desktop binding/PasswordBox tests | Closed as superseded by #40; source branch retained, not deleted. |
| #31 — hosted Windows CI/runtime stabilization | Separate Draft; overlaps #15/#32 in workflows and PostgreSQL verification. Review as a bounded maintenance change; no wholesale merge. |
| #32 — hosted Windows Foundation workflow | Separate Draft targeting Foundation; a PR candidate run does not certify the unchanged Foundation base tip. |
| #15 — ProgramData endpoints and TLS | Older Foundation-based Draft. Preserve as evidence/source; compare with current TLS and selectively port only if still needed. |
| #23 — Runtime/operator integration | Draft whose head is Integration at the pre-#40 SHA; review ancestry/diff before any future disposition. |
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


## CI trigger hardening follow-up — 2026-10-10

PR #25 is open and ready for review (not merged). Its Quick Validation workflow adds `security/**` to the push lane in addition to Pull Request gates targeting the active integration/operator branches. PR #27 still requires exact-head testing once that trigger change is integrated; absence of CI is not a pass.
