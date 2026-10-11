# Engineering Readiness Register — GameNet 5

این سند، دفتر زندهٔ ریسک‌ها و ترتیب اجرای کار است. تاریخ snapshot: 2026-10-11. این دفتر جایگزین Requirementها، ADRها یا گواهی Foundation نیست؛ آن‌ها را به ترتیب اجرایی و وضعیت واقعی وصل می‌کند. هر ادعای Done باید به commit، آزمون و مدرک همان SHA متصل باشد.

## Current verified baseline — 2026-10-11 (latest exact-SHA refresh)

- Repository: alizebal73/5, public.
- main: protected and unchanged at b9288471f2047570eaf8d0d6552cf87bc0ddc214.
- foundation/runtime-final-v2: protected and unchanged at be29687e637b709158a30204bb9213bfa4813950. Stable Checkpoint still certifies only c3a8ba482548e963479fbdf8538be62c4d15acfe; exact-tip certification of the base SHA remains pending.
- integration/runtime-operator-v1: unchanged at 82d90178c0011c65eef76bd32cc33318d5bbd91c; active Ruleset protect-integration-runtime ID 24879997 protects it with PR, conversation resolution, quick-validation, no deletion/force-push, Squash-only and no bypass actors.
- PR #31: last code-bearing candidate e5caceb11968512fb91b5f808cbd4afd24be22ad; its five gates passed on that exact SHA. Subsequent documentation-only updates are tracked on the live PR; consult its current HEAD and attached runs before review or merge.
- PR #31 includes scripts/register-test-runtime-service.ps1 and docs/operations/test-runtime-service-registration.md. The payload validates manifest/executable/helper SHA-256 in -ValidateOnly; no Windows service was queried, registered or started on hosted CI.
- The Runtime payload build rewrites the packaged runtime-endpoints guide to use Tools paths instead of source-tree scripts paths; that change is green on the current exact SHA. The ZIP remains a portable test payload, not Setup.exe/MSI.
- PR #32: head 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9, Draft/unmerged; Full Foundation passed in [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It adds an exact-tip Full Foundation push trigger for Foundation refs. The protected base tip remains unchanged pending review/merge and post-merge certification.
- PR #15: head 15402b2758808327874c18e21753120ef9cfccce, Draft/unmerged; Quick, Full Foundation and Runtime payload passed on that exact SHA ([#38144366659](https://github.com/alizebal73/5/actions/runs/38144366659), [#38144369176](https://github.com/alizebal73/5/actions/runs/38144369176), [#38144369207](https://github.com/alizebal73/5/actions/runs/38144369207)). It is an older Foundation-based TLS/ProgramData branch and is not the selected runtime acceptance baseline.
- Personal self-hosted Runner cleanup is deliberately deferred until GameNet completion by owner decision; Issue #33 tracks removal. Runner-group restrictions are not claimed as verified, and old open branches may still contain self-hosted workflow declarations.
- Actual installed Windows Service identity/effective ACL, DPAPI after restart, TLS private-key access, bootstrap finalization, Agent enrollment/replay/revocation/restart and physical LAN acceptance remain open.
- PR #25 is merged; #27/#28/#29 are closed historical records, not merge candidates; PR #30 is merged at e1626d7d1f2e62601329604b8823787f2c9597b6. Keep main and Foundation unchanged outside reviewed PRs.
- The Pre-Windows and Pre-Commercial Readiness Gate remains the mandatory handoff.

## Current priority gaps — 2026-10-11

| ID | Priority | Current status | Closure criterion |
|---|---|---|---|
| C-01 | P0 | PR #31 exact head e5caceb11968512fb91b5f808cbd4afd24be22ad passed Quick push/PR, Full Foundation, and Runtime payload push/PR. | Keep evidence tied to exact SHA and rerun applicable gates after source/workflow changes. |
| C-02 | Deferred by owner | Personal Runner remains registered by decision; runner-group restrictions are not read back as enforced. | Remove after GameNet completion and verify disappearance from repository/organization Runner settings; avoid third-party/untrusted execution while retained. |
| C-03 | Done | Active Integration Ruleset ID 24879997 was read back with exact target and required quick-validation. | Keep active and verify the check name whenever workflow names change. |
| C-04 | P0 before commercial slices | Hosted runtime tests are green; actual installed-service and physical-LAN acceptance is open. | Pass isolated Windows Server/Agent service, ACL/DPAPI, TLS, bootstrap, enrollment/replay/restart and LAN protocol with evidence. |
| C-05 | P1 before calling Foundation tip current-certified | Foundation tip be29687e637b709158a30204bb9213bfa4813950 differs from stable-certified SHA c3a8ba482548e963479fbdf8538be62c4d15acfe. | After reviewing/merging PR #32, require Full Foundation on the exact resulting Foundation tip; only then update Stable Checkpoint. |
| C-06 | P0 before any slice relying on Outbox delivery | Outbox append/leased claim/published marker exist; worker, retry/final-failure policy, event consumer and idempotency proof are absent. | Implement and certify those semantics before asynchronous delivery is needed, especially Session-to-Agent. |
| C-07 | P1 | Historical snapshots intentionally preserve old facts. | Keep live status blocks synchronized; canonical docs validator and relevant exact-SHA CI must pass.

## Historical baseline snapshot (2026-10-10; superseded by current read-back)

- Repository: alizebal73/5 (عمومی). Default branch: main.
- Foundation candidate ref: foundation/runtime-final-v2، tip فعلی be29687e637b709158a30204bb9213bfa4813950، protected=true.
- آخرین SHA ثبت‌شده به‌عنوان گواهی‌شده در Stable Checkpoint: c3a8ba482548e963479fbdf8538be62c4d15acfe. Tip فعلی Foundation دو commit جلوتر است؛ مقایسه نشان می‌دهد تغییرات آن دو commit محدود به workflow/marker/log/checkpoint هستند، اما read-back وضعیت commit جاری pending بود و اجرای موفقی برای SHA فعلی پیدا نشد. تا اجرای مجدد Foundation روی SHA دقیق be296...، آن را خودکار «گواهی‌شدهٔ جاری» تلقی نکنید.
- main: b9288471f2047570eaf8d0d6552cf87bc0ddc214، protected=true. تا تصمیم انتشار دست‌نخورده بماند.
- Runtime/operator integration: integration/runtime-operator-v1 در SHA 55a340305f9eba3bc8f9a1ce65fb37bedbae7580. Quick Validation [#38041093148](https://github.com/alizebal73/5/actions/runs/38041093148) و Full Foundation [#38041093167](https://github.com/alizebal73/5/actions/runs/38041093167) روی همین SHA موفق‌اند. PR [#23](https://github.com/alizebal73/5/pull/23) هنوز Draft است و ادغام نشده.
- اولین استخراج UI: branch refactor/desktop-view-boundaries-v1 در SHA 9a058043ca8ba69f2b0bd23777263063ee681c92. Quick Validation [#38041986482](https://github.com/alizebal73/5/actions/runs/38041986482) و Full Foundation [#38041986502](https://github.com/alizebal73/5/actions/runs/38041986502) روی همین SHA موفق‌اند. PR [#24](https://github.com/alizebal73/5/pull/24) هنوز Draft است و ادغام نشده.
- این دفتر زنده در Draft PR [#25](https://github.com/alizebal73/5/pull/25) پیشنهاد شده است؛ CI روی هر به‌روزرسانی PR باید دقیقاً روی آخرین SHA موفق شود.
- کاندید امنیتی ترکیبی PR [#29](https://github.com/alizebal73/5/pull/29) روی head `f5dd302a5245562d5e9f30fbb66b615a6152cea1`، validation-only و Draft است؛ Full Foundation #38096700124، Quick PR #38096701675، Quick push #38096700116 و Runtime payload #38096701718 همگی سبز. PR تحویل [#30](https://github.com/alizebal73/5/pull/30) با چهار CI موفق روی SHA دقیق `f4387584d6c14fb55cbfca20521e221ea2462a4f` با Squash ادغام شد؛ merge commit در Integration `e1626d7d1f2e62601329604b8823787f2c9597b6` است. آزمون‌های بعد از merge نیز کامل سبز شده‌اند: [Full Foundation #38097484132](https://github.com/alizebal73/5/actions/runs/38097484132)، [Quick push #38097484122](https://github.com/alizebal73/5/actions/runs/38097484122) و [Quick PR #38097487320](https://github.com/alizebal73/5/actions/runs/38097487320). آخرین مورد باقی‌مانده، Quick Validation روی این نسخهٔ مستندات است.
- گواهی خودکار بالا اثبات نصب Windows Service یا تست تازهٔ LAN روی همین SHA نیست. گزارش دستی قبلی [PR #17](https://github.com/alizebal73/5/pull/17) روی کاندید قدیمی‌تر و با اجرای تعاملی processها نوشته شده؛ آن را نباید با آزمون جاری Service-backed یکی دانست.

## Active gap register

| ID | اولویت | موضوع و شاهد فعلی | وضعیت | معیار پایان |
|---|---|---|---|---|
| G-01 | P1 | اسناد زنده باید به baseline همان PR و گیت پیش از پذیرش واقعی وصل باشند. | اصلاح وضعیت زنده در کاندید PR #31؛ هنوز ادغام نشده | Full Foundation و Quick روی SHA دقیق جاری، سپس بررسی نبود تناقض در وضعیت ADR/شاخه/گواهی. |
| G-02 | P1 | MainWindow پیش‌تر پوسته، ورود، تغییر گذرواژه و Workspace ایستگاه‌ها را در یک XAML حمل می‌کرد. PR #24 فقط Workspace ایستگاه‌ها را به UserControl مستقل منتقل کرده است. | گام اول پیاده و CI سبز؛ ادامه باز است | استخراج مرحله‌ای timer سلامت Agent، سپس views ورود/تغییر گذرواژه و content host پوسته؛ حفظ تمام bindingها/فرمان‌ها و تست‌های UI. |
| G-03 | P1 | ViewModel سلامت Agent را هر ۱۰ ثانیه از DispatcherTimer داخل MainWindow به‌روزرسانی می‌کند. چرخهٔ شروع بعد از Login، توقف بعد از Logout و بستن پنجره باید مستقل از View آزمون‌پذیر باشد. | باز | coordinator/worker لغوپذیر؛ cadence و شرایط authentication/busy حفظ شود؛ تست start/stop/cancel و عدم اجرای هم‌زمان. |
| G-04 | P1 | همهٔ عملیات قابل‌نمایش باید بر اساس permissionهای اپراتور hide/disable و از طریق ICommand قابل‌کنترل شوند. مخفی‌کردن دکمه امنیت نیست و Server باید همیشه مجوز را مجدداً بررسی کند. | نیازمند گسترش UI | تست CanExecute/visibility با role/permission؛ درخواست مستقیم غیرمجاز به API در Server رد شود؛ هیچ موفقیت ساختگی/optimistic نتیجهٔ مالی وجود نداشته باشد. |
| G-05 | P1 | UI نهایی و آزمون تعاملی برای fa-IR/RTL و en-US/LTR هنوز کامل نیست؛ CI فقط با موفقیت build یا startup، رفتار هر screen را اثبات نمی‌کند. | باز | smoke برای view/binding/resources/navigation، Loading/Empty/Error/Offline/PermissionDenied، پاک‌شدن فیلد گذرواژه، keyboard/focus و پیام موفقیت تأییدشدهٔ Server. |
| G-06 | P0 | حذف Bootstrap اولیه اکنون در `scripts/finalize-server-setup.ps1` پیاده‌سازی شده و تست خواندن Protected Settings بدون آن وجود دارد؛ کاندید ترکیبی در CI سبز است. اجرای واقعی replacement/ACL/DPAPI و بررسی startup پس از cleanup تحت هویت Windows Service هنوز انجام نشده است. | کد و آزمون خودکار حاضر؛ پذیرش عملی باز | روی Server آزمایشی جدا، پس از ثبت Owner، فقط همان کلید حذف شود؛ payload موقت با DPAPI بازخوانی و بررسی شود، replacement اتمیک/ACL تأیید و startup/readiness بعد از restart سرویس آزموده شود؛ شکست باید فایل اصلی را سالم نگه دارد. |
| G-18 | P0 | مخزن اسرار Server با DPAPI LocalMachine، نوع‌های مستقل برای connection/JWT/Agent provisioning secret، Production fail-closed، ACL مبتنی بر Service SID و حذف Bootstrap پس از ساخت Owner در tree ادغام‌شده موجود است. Full Foundation روی source candidate #29، delivery PR #30 و merge commit `e1626d7...` موفق است. ADR-0002 پذیرفته شده است به‌عنوان قرارداد پیاده‌سازی؛ پذیرش عملیاتی Windows Service هنوز باز است؛ DPAPI/ACL زیر Windows Service نصب‌شده، private-key پس از restart، rotation و recovery فیزیکی اثبات نشده‌اند. | کد و exact-SHA CI سبز؛ سرویس واقعی بیرون از این مرحله | پیش از انتشار، ADR-0002 و شواهد سرویس واقعی لازم است؛ تغییر Manager واقعی بدون preflight ممنوع. |
| G-07 | P0 | ثبت Agent توکن ۲۵۶بیتی ۱۵دقیقه‌ای، hash-only persistence، رد replay/revoke/expiry و recovery محدود با reason/audit در Integration ادغام شده است. اصلاح lifecycle، فایل توکن محافظت‌شده را تا موفقیت احراز عادی نگه می‌دارد؛ regression test و Agent runtime certification روی source، delivery PR و merge commit موفق‌اند. | کد و تست‌های خودکار green؛ نصب Windows Service/DPAPI زیر LocalService پس از restart خارج از محدوده | هویت/ACL/DPAPI سرویس واقعی و restart جداگانه تأیید شوند. |
| G-08 | P0 | PR #30 با یک commit و 67-file diff در `integration/runtime-operator-v1` ادغام شد؛ merge commit `e1626d7d1f2e62601329604b8823787f2c9597b6`. Full Foundation #38096870883، Quick push #38096870892، Quick PR #38096874251 و Runtime payload #38096874276 روی delivery SHA `f438758...` موفق شدند. بستهٔ داخلی `GameNet5-RuntimeLanSmoke-f4387584d6c1.zip` با SHA-256 `3a1d51e89be86fb672ecc7d5a7bd65df26caf97244daffc2cb75ad452a60d819`; outer artifact digest `sha256:f3c6629cdf78a68fa1ea6dafc515114f5538af3787c60c370de8c7e2544b6cc2`. پس از merge نیز Full Foundation #38097484132، Quick push #38097484122 و Quick PR #38097487320 موفق‌اند. ZIP بستهٔ Runtime است، نه Setup/MSI؛ نصب Windows Service/دو-PC LAN همچنان آزمایش نشده. | delivery و post-merge automated gates green؛ physical acceptance خارج از این مرحله | سرویس نصب‌شده و LAN جداگانه، طبق برنامهٔ بعدی. |
| G-09 | P1 قبل از Release | مدل گواهی فعلی، private key غیرقابل‌استخراج در LocalMachine\My و trust کردن public .cer پس از SHA-256 مستقل است. چرخهٔ rotation/revocation/recovery و رفتار cert منقضی هنوز باید گواهی شود. | بخشی خودکار آزمون شده؛ lifecycle کامل باز | آزمون rotation بدون trust bypass، SAN mismatch/expiry، rollback/recovery و مجوز فایل/کلید تحت حساب سرویس واقعی. |
| G-10 | P0 پیش از نصاب | انتخاب اینکه PostgreSQL از قبل نصب‌شده فرض می‌شود یا نصاب آن را provision می‌کند هنوز تصمیم اجرایی لازم دارد. writer تنظیمات به‌تنهایی PostgreSQL/database role را نصب یا امن‌سازی نمی‌کند. | تصمیم باز | نسخهٔ پشتیبانی‌شده، service identity، database role، data path، backup/upgrade و migration identity صریح شوند؛ Production startup هرگز خودکار migrate نکند. |
| G-11 | P0 پیش از Release | ZIPهای self-contained خروجی خام‌اند، نه Setup.exe/MSI؛ Windows service registration، repair، upgrade، updater و rollback نصب واقعی گواهی نشده‌اند. | باز؛ tracked در [Issue #16](https://github.com/alizebal73/5/issues/16) | install/repair/upgrade، package checksum/signature، health gate، rollback و uninstall بدون حذف ناخواستهٔ business data روی ماشین تمیز. |
| G-12 | P0 پیش از Business vertical بعدی | Customer/Session/Tariff/Billing/Wallet/Inventory/Buffet/VIP/Reports/Shift/Approval کامل در snapshot فعال حاضر نیستند. | نیازمندی برنامه‌ریزی‌شده، نه فراموش‌شده | هر module مطابق requirement: مالک داده، permission، state machine، transaction، idempotency/concurrency، audit، failure/recovery، API/UI و تست PostgreSQL واقعی؛ هیچ financial authority در Desktop. |
| G-19 | P1 قبل از هر برش تجاری | ماتریس ۶۳ requirement ردیف دارد، اما داشتن ID و معیار کلی به‌تنهایی trace کامل requirement→slice→کد/API→تست→شاهد نیست. | باید در هر برش تکمیل شود | برای هر برش، DoR/vertical-slice ثبت کند: REQ IDs، invariant، permission، migration/data owner، transaction/idempotency/race، audit، failure/recovery، automated + physical tests (در صورت نیاز)، exact SHA/evidence؛ هیچ ماژول کامل‌نشده را از روی وجود ردیف/فایل Done اعلام نکنید. |
| G-13 | P1 | در هشت PR باز، چند draft بر پایه‌های قدیمی/واگرا وجود دارد. حذف/ادغام دسته‌جمعی ممکن است شواهد LAN یا کار امنیتی یکتا را از بین ببرد. | نیازمند disposition موردی | برای هر PR: compare ancestry/diff، استخراج شواهد مفید، ثبت مقصد و سپس close/retain؛ شاخه فقط پس از تأیید هیچ کار یکتای موردنیازی حذف شود. |
| G-14 | P1 governance | Rulesetهای main و foundation فعال‌اند، strict_required_status_checks_policy=false است و integration/runtime-operator-v1 هیچ Ruleset فعالی ندارد. | hardening باقی‌مانده | پس از معتبرشدن CI روی PR مقصد integration، Require PR + quick-validation + conversation resolution؛ برای main/Foundation سیاست up-to-date بودن branch پیش از merge را بررسی و تنظیم کنید. تغییر Ruleset از اتصال فعلی قابل‌نوشتن نبود و این PR ادعای تغییر آن را ندارد. |
| G-15 | P1 security | مخزن عمومی است. این ممیزی ادعا نمی‌کند تمام تاریخچهٔ همهٔ branchها از نظر secret به‌صورت خودکار اسکن شده است. | بررسی مستقل لازم | history/branches/packageها با secret scanner بررسی؛ alerts/push protection فعال در صورت دسترسی؛ اگر secret واقعی یافت شد rotate/revoke اول، بعد پاک‌سازی تاریخچه با برنامه و هماهنگی. |
| G-16 | Deferred | طرح Steam/GamingAccounts یک ADR پیشنهادی است و PR #19 بسته شده؛ مجازبودن مدل حساب و روش integration هنوز شرط ورود است. | عمداً پیاده‌سازی نشده | نیاز محصول صریح + بررسی برنامهٔ رسمی/اجازهٔ Valve + تصمیم محصول/ADR؛ تا آن زمان هیچ credential injection، MFA bypass یا Auto Login فرضی ساخته نشود. |
| G-17 | P1 پیش از Release | Foundation برای PostgreSQL/migrations، audit/idempotency/outbox و backup/restore آزمون دارد؛ اما زمان‌بندی/retention، محل امن نسخه‌های پشتیبان و RPO/RTO محصولی هنوز کامل نشده‌اند. | بخشی Foundation موجود؛ عملیات کامل باز | restore دوره‌ای و ایزوله، retention، هشدار شکست، صلاحیت دسترسی، آزمون بازیابی/تطبیق و ثبت زمان/داده از دست‌رفته. |

## Ordered execution route — 2026-10-11 (latest exact-SHA refresh)

1. PR #31 last code-bearing candidate e5caceb11968512fb91b5f808cbd4afd24be22ad passed Quick push/PR, Full Foundation and Runtime payload push/PR. Later documentation-only updates remain part of the live PR; review its current HEAD and run results before disposition.
2. PR #32 Foundation workflow maintenance candidate 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9 passed Full Foundation [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). It remains Draft/unmerged; do not claim the current Foundation base SHA is certified.
3. Integration protection is complete and read back. Active Ruleset protect-integration-runtime ID 24879997 requires PR, conversation resolution and quick-validation, blocks deletion/force-push, allows Squash only and has no bypass actor.
4. Owner keeps the personal Runner registered until GameNet completion, then intends to remove it. Issue #33 tracks cleanup. Runner-group access is not certified as restricted; don't generalize the current hosted-runner setup to all old branches.
5. ADR-0002 is an implementation contract, not operational acceptance. Installed-service identity, effective ACL, DPAPI after restart, TLS private-key access, bootstrap cleanup, expiry/replay/revocation, credential persistence and physical LAN remain open.
6. Before touching an existing Manager PC, run Tools/inspect-manager-runtime-state.ps1 from the verified package (read-only) and review the report; do not provision secrets, change TLS or clean ACLs blindly.
7. Start commercial work only after isolated real-Windows acceptance passes and the baseline is reviewed/adopted. Outbox is append/lease-claim/published-marker only; before async-dependent work, especially Session-to-Agent, implement/test worker, retry/final-failure, idempotent consumer and crash/race/recovery.
8. Setup/update/repair/uninstall/rollback, DB lifecycle, package signing, restore and a 2–3-PC then ~15-station pilot remain release gates.

## Branch and PR disposition

| PR / branch | وضعیت و تصمیم فعلی |
|---|---|
| #25 — docs/engineering-readiness-audit-v1 | ادغام شده به Integration؛ دیگر PR فعال نیست. |
| #23 — integration/runtime-operator-v1 | Draft فعال؛ Quick و Full Foundation روی SHA 55a340... سبز. ادغام هنوز نیازمند بازبینی ancestry/diff و gate سرویس/شبکه است. |
| #24 — refactor/desktop-view-boundaries-v1 | Draft فعال؛ Quick و Full Foundation روی SHA 9a058... سبز. هنوز ادغام نشده؛ گام اول جداسازی UI است، نه پایان معماری. |
| #27 — security/server-secret-store-active-v1 | بسته‌شده و فقط سابقهٔ منبع؛ کاندید ادغام نیست. تغییرهای منتخب از مسیر #30 تحویل شده‌اند؛ پذیرش Windows Service/DPAPI واقعی باز است. |
| #28 — security/agent-one-time-enrollment-v1 | بسته‌شده و فقط سابقهٔ منبع؛ ادغام مستقل نکنید. مسیر تحویل منتخب در #30 ثبت شده است؛ پذیرش فیزیکی Agent باز است. |
| #29 — security/combined-runtime-validation-v1 | بسته‌شده و validation-only، **DO NOT MERGE**؛ فقط سابقهٔ آزمون است. PR #30 با درخت انتخاب‌شده و تاریخچهٔ تمیز ادغام شده است. |
| #31 — security/runner-and-stage1-stabilization-v1 | Draft/open, not merged; last code-bearing baseline e5caceb11968512fb91b5f808cbd4afd24be22ad passed all five hosted gates. Subsequent documentation-only updates and their runs are in the live PR. |
| #32 — security/foundation-hosted-runner-isolation-v1 | Draft/open, not merged; Full Foundation passed on candidate 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9 in [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082). Foundation base unchanged. |
| #26 — fix/foundation-workflow-paths-v1 | Closed as superseded by the broader #32; not merged and branch not deleted. |
| #30 — security/runtime-enrollment-delivery-v1 | ادغام شد؛ head `f4387584d6c14fb55cbfca20521e221ea2462a4f`، merge commit `e1626d7d1f2e62601329604b8823787f2c9597b6`. چهار CI روی head PR و Full Foundation #38097484132، Quick push #38097484122 و Quick PR #38097487320 پس از merge همه سبز. |
| #15 — fix/programdata-endpoints-v1 | Draft قدیمی به مقصد Foundation؛ به‌عنوان منبع مقایسه نگه دارید. تغییر TLS آن را بدون مقایسه با مدل فعلی certificate-store merge نکنید. |
| #17 — docs/runtime-lan-smoke-evidence-2026-10-10 | شواهد تاریخی ارزشمند است ولی روی branch/candidate قدیمی. شواهد و محدودیت‌ها باید مرجع داشته باشند؛ branch کامل آن ادغام نشود. |
| #18 — security/server-secret-lifecycle-contract-v1 | طرح امنیتی مفید ولی base قدیمی؛ قراردادهای لازم انتخابی به سند مرجع وارد شوند، بدون merge کورکورانه. |
| #20 — fix/server-secret-store-v1 | کد مخزن اسرار DPAPI/Service SID و اسکریپت preflight خواندنی دارد، اما روی PR #18 و base قدیمی stack شده؛ diff به‌صورت قطعه‌ای بازبینی و فقط قسمت‌های سازگار port شود. هیچ --provision-secrets یا تغییر ماشین تا بررسی وضعیت فعلی Manager و تأیید ADR اجرا نشود. |
| #21 — feature/desktop-operator-shell-v1 | نمونه/طرح UI روی runtime branch قدیمی؛ ایده‌ها ممکن است مرجع طراحی باشند اما XAML/VM آن نباید بدون diff به‌صورت کامل ادغام شود. |
| #19 — Steam account lifecycle | PR بسته شده؛ branch را به‌عنوان آزمایش تاریخی نگه دارید تا تصمیم دامنه و سیاست بیرونی قطعی شود. |

 
**نتیجهٔ مقایسهٔ شاخه‌های امنیتی (2026-10-11):** PR #27 تغییر ۳۴ فایل دارد و همهٔ این فایل‌ها در diff کاندید #29 دیده می‌شوند؛ PR #28 تغییر ۴۰ فایل دارد، ۳۹ فایل آن در diff #29 دیده می‌شوند. فایل باقی‌ماندهٔ PR #28 یعنی `src/Server/InternalTestAccess.cs` در شاخهٔ ترکیبی جداگانه لازم نیست، زیرا `src/Server/Properties/AssemblyInfo.cs` همان `InternalsVisibleTo("GameNet.Server.UnitTests")` را دارد و Foundation روی کاندید ترکیبی سبز شده است. PR #27 و #28 شش مسیر مشترک دارند؛ #29 برای آزمودن درخت ترکیبی است، نه اجازهٔ merge فوری. هیچ PR یا branch در این مرحله merge/حذف نشود.
 
**سیاست پاک‌سازی:** فعلاً branch یا PR را دسته‌جمعی حذف/بسته نکنید. از وضعیت remote تأیید شده که main و همهٔ foundation/runtime-* به‌وسیلهٔ Ruleset محافظت می‌شوند؛ integration و branchهای feature محافظت‌شده نیستند. بعد از منتقل‌شدن شواهد یا رد صریح تغییر منحصربه‌فرد، PR قدیمی را با توضیح روشن ببندید؛ حذف branch آخرین قدم است، نه اولین قدم.

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
