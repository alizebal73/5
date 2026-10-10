# Engineering Readiness Register — GameNet 5

این سند، دفتر زندهٔ ریسک‌ها و ترتیب اجرای کار است. تاریخ snapshot: 2026-10-10. این دفتر جایگزین Requirementها، ADRها یا گواهی Foundation نیست؛ آن‌ها را به ترتیب اجرایی و وضعیت واقعی وصل می‌کند. هر ادعای Done باید به commit، آزمون و مدرک همان SHA متصل باشد.

## Current verified baseline

- Repository: alizebal73/5 (عمومی). Default branch: main.
- Foundation candidate ref: foundation/runtime-final-v2، tip فعلی be29687e637b709158a30204bb9213bfa4813950، protected=true.
- آخرین SHA ثبت‌شده به‌عنوان گواهی‌شده در Stable Checkpoint: c3a8ba482548e963479fbdf8538be62c4d15acfe. Tip فعلی Foundation دو commit جلوتر است؛ مقایسه نشان می‌دهد تغییرات آن دو commit محدود به workflow/marker/log/checkpoint هستند، اما read-back وضعیت commit جاری pending بود و اجرای موفقی برای SHA فعلی پیدا نشد. تا اجرای مجدد Foundation روی SHA دقیق be296...، آن را خودکار «گواهی‌شدهٔ جاری» تلقی نکنید.
- main: b9288471f2047570eaf8d0d6552cf87bc0ddc214، protected=true. تا تصمیم انتشار دست‌نخورده بماند.
- Runtime/operator integration: integration/runtime-operator-v1 در SHA 55a340305f9eba3bc8f9a1ce65fb37bedbae7580. Quick Validation [#38041093148](https://github.com/alizebal73/5/actions/runs/38041093148) و Full Foundation [#38041093167](https://github.com/alizebal73/5/actions/runs/38041093167) روی همین SHA موفق‌اند. PR [#23](https://github.com/alizebal73/5/pull/23) هنوز Draft است و ادغام نشده.
- اولین استخراج UI: branch refactor/desktop-view-boundaries-v1 در SHA 9a058043ca8ba69f2b0bd23777263063ee681c92. Quick Validation [#38041986482](https://github.com/alizebal73/5/actions/runs/38041986482) و Full Foundation [#38041986502](https://github.com/alizebal73/5/actions/runs/38041986502) روی همین SHA موفق‌اند. PR [#24](https://github.com/alizebal73/5/pull/24) هنوز Draft است و ادغام نشده.
- این دفتر زنده در Draft PR [#25](https://github.com/alizebal73/5/pull/25) پیشنهاد شده است؛ CI روی هر به‌روزرسانی PR باید دقیقاً روی آخرین SHA موفق شود.
- گواهی خودکار بالا اثبات نصب Windows Service یا تست تازهٔ LAN روی همین SHA نیست. گزارش دستی قبلی [PR #17](https://github.com/alizebal73/5/pull/17) روی کاندید قدیمی‌تر و با اجرای تعاملی processها نوشته شده؛ آن را نباید با آزمون جاری Service-backed یکی دانست.

## Active gap register

| ID | اولویت | موضوع و شاهد فعلی | وضعیت | معیار پایان |
|---|---|---|---|---|
| G-01 | P1 | نقشه‌راه و چند سند، شاخه/وضعیت قدیمی را به‌عنوان وضعیت جاری نشان می‌دادند؛ این PR آن‌ها را هم‌راستا می‌کند. | در حال اصلاح در PR مستندات | Quick Validation روی PR مستندات موفق شود؛ مرجع جاری و تاریخ snapshot واضح بماند. |
| G-02 | P1 | MainWindow پیش‌تر پوسته، ورود، تغییر گذرواژه و Workspace ایستگاه‌ها را در یک XAML حمل می‌کرد. PR #24 فقط Workspace ایستگاه‌ها را به UserControl مستقل منتقل کرده است. | گام اول پیاده و CI سبز؛ ادامه باز است | استخراج مرحله‌ای timer سلامت Agent، سپس views ورود/تغییر گذرواژه و content host پوسته؛ حفظ تمام bindingها/فرمان‌ها و تست‌های UI. |
| G-03 | P1 | ViewModel سلامت Agent را هر ۱۰ ثانیه از DispatcherTimer داخل MainWindow به‌روزرسانی می‌کند. چرخهٔ شروع بعد از Login، توقف بعد از Logout و بستن پنجره باید مستقل از View آزمون‌پذیر باشد. | باز | coordinator/worker لغوپذیر؛ cadence و شرایط authentication/busy حفظ شود؛ تست start/stop/cancel و عدم اجرای هم‌زمان. |
| G-04 | P1 | همهٔ عملیات قابل‌نمایش باید بر اساس permissionهای اپراتور hide/disable و از طریق ICommand قابل‌کنترل شوند. مخفی‌کردن دکمه امنیت نیست و Server باید همیشه مجوز را مجدداً بررسی کند. | نیازمند گسترش UI | تست CanExecute/visibility با role/permission؛ درخواست مستقیم غیرمجاز به API در Server رد شود؛ هیچ موفقیت ساختگی/optimistic نتیجهٔ مالی وجود نداشته باشد. |
| G-05 | P1 | UI نهایی و آزمون تعاملی برای fa-IR/RTL و en-US/LTR هنوز کامل نیست؛ CI فقط با موفقیت build یا startup، رفتار هر screen را اثبات نمی‌کند. | باز | smoke برای view/binding/resources/navigation، Loading/Empty/Error/Offline/PermissionDenied، پاک‌شدن فیلد گذرواژه، keyboard/focus و پیام موفقیت تأییدشدهٔ Server. |
| G-06 | P0 | راز Bootstrap اولیه در فایل Protected Settings باقی می‌ماند، حتی پس از ساخته‌شدن Owner. | مانع نصب امن | حذف فقط همان کلید، رمزنگاری مجدد اتمیک، حفظ ACL، تست startup/readiness پس از پاک‌سازی و آزمون شکست/بازیابی. |
| G-18 | P0 | قرارداد مخزن اسرار Server هنوز Proposed است (ADR-0002 در PR #18)؛ پیاده‌سازی DPAPI/ACL/type-safe در PR #20 روی شاخه‌ای stack‌شده و قدیمی مانده و وارد candidate فعال نشده است. | مانع خدمت‌رسانی/نصب | مرور و پذیرش ADR-0002؛ diff دقیق PR #20 با کد جاری؛ انتقال حداقلیِ تأییدشده؛ Production fail-closed، عدم fallback به env/JSON/CLI، ACL بر اساس Service SID واقعی، redaction، rotation/recovery و تست identity-denial. ابتدا وضعیت ماشین مدیر با اسکریپت صرفاً خواندنی بررسی شود؛ تا آن زمان `--provision-secrets`، TLS setup یا cleanup اجرا نشود. |
| G-07 | P0 | AgentCredentialStore از DPAPI CurrentUser استفاده می‌کند؛ آزمون دستی LAN قبلی Agent را به‌شکل process تعاملی اجرا کرده، نه تحت identity سرویس نصب‌شده. | مانع service commissioning | انتخاب identity واقعی سرویس، ACLها، credential یکتای یک‌بارمصرف برای هر Agent، persistence بعد از restart، رد identity کپی‌شده و تست protect/unprotect تحت همان حساب سرویس. |
| G-08 | P0 | Server/Agent روی LAN در آزمون تاریخی HTTPS/lease/heartbeat را گذرانده‌اند، ولی آزمون تحت Windows Service identity روی SHA جاری ثبت نشده است؛ یک lease failure گذرا نیز در گزارش تاریخی وجود دارد. | مانع قابلیت PC/Customers بعدی | تست کنترل‌شده با DB دورریختنی: گواهی/SAN/fingerprint، Server service و دسترسی private key، Agent service، login/enrollment، lease/heartbeat، stop/restart/reconnect و نتیجهٔ تکرارپذیر. |
| G-09 | P1 قبل از Release | مدل گواهی فعلی، private key غیرقابل‌استخراج در LocalMachine\My و trust کردن public .cer پس از SHA-256 مستقل است. چرخهٔ rotation/revocation/recovery و رفتار cert منقضی هنوز باید گواهی شود. | بخشی خودکار آزمون شده؛ lifecycle کامل باز | آزمون rotation بدون trust bypass، SAN mismatch/expiry، rollback/recovery و مجوز فایل/کلید تحت حساب سرویس واقعی. |
| G-10 | P0 پیش از نصاب | انتخاب اینکه PostgreSQL از قبل نصب‌شده فرض می‌شود یا نصاب آن را provision می‌کند هنوز تصمیم اجرایی لازم دارد. writer تنظیمات به‌تنهایی PostgreSQL/database role را نصب یا امن‌سازی نمی‌کند. | تصمیم باز | نسخهٔ پشتیبانی‌شده، service identity، database role، data path، backup/upgrade و migration identity صریح شوند؛ Production startup هرگز خودکار migrate نکند. |
| G-11 | P0 پیش از Release | ZIPهای self-contained خروجی خام‌اند، نه Setup.exe/MSI؛ Windows service registration، repair، upgrade، updater و rollback نصب واقعی گواهی نشده‌اند. | باز؛ tracked در [Issue #16](https://github.com/alizebal73/5/issues/16) | install/repair/upgrade، package checksum/signature، health gate، rollback و uninstall بدون حذف ناخواستهٔ business data روی ماشین تمیز. |
| G-12 | P0 پیش از Business vertical بعدی | Customer/Session/Tariff/Billing/Wallet/Inventory/Buffet/VIP/Reports/Shift/Approval کامل در snapshot فعال حاضر نیستند. | نیازمندی برنامه‌ریزی‌شده، نه فراموش‌شده | هر module مطابق requirement: مالک داده، permission، state machine، transaction، idempotency/concurrency، audit، failure/recovery، API/UI و تست PostgreSQL واقعی؛ هیچ financial authority در Desktop. |
| G-19 | P1 قبل از هر برش تجاری | ماتریس ۶۱ requirement ردیف دارد، اما داشتن ID و معیار کلی به‌تنهایی trace کامل requirement→slice→کد/API→تست→شاهد نیست. | باید در هر برش تکمیل شود | برای هر برش، DoR/vertical-slice ثبت کند: REQ IDs، invariant، permission، migration/data owner، transaction/idempotency/race، audit، failure/recovery، automated + physical tests (در صورت نیاز)، exact SHA/evidence؛ هیچ ماژول کامل‌نشده را از روی وجود ردیف/فایل Done اعلام نکنید. |
| G-13 | P1 | در هشت PR باز، چند draft بر پایه‌های قدیمی/واگرا وجود دارد. حذف/ادغام دسته‌جمعی ممکن است شواهد LAN یا کار امنیتی یکتا را از بین ببرد. | نیازمند disposition موردی | برای هر PR: compare ancestry/diff، استخراج شواهد مفید، ثبت مقصد و سپس close/retain؛ شاخه فقط پس از تأیید هیچ کار یکتای موردنیازی حذف شود. |
| G-14 | P1 governance | Rulesetهای main و foundation فعال‌اند، اما read-back نشان می‌دهد strict_required_status_checks_policy=false. شاخهٔ integration نیز فعلاً protected=false است. | hardening باقی‌مانده | پس از معتبرشدن CI روی PR مقصد integration، Require PR + quick-validation + conversation resolution؛ برای main/Foundation سیاست up-to-date بودن branch پیش از merge را بررسی و تنظیم کنید. تغییر Ruleset از اتصال فعلی قابل‌نوشتن نبود و این PR ادعای تغییر آن را ندارد. |
| G-15 | P1 security | مخزن عمومی است. این ممیزی ادعا نمی‌کند تمام تاریخچهٔ همهٔ branchها از نظر secret به‌صورت خودکار اسکن شده است. | بررسی مستقل لازم | history/branches/packageها با secret scanner بررسی؛ alerts/push protection فعال در صورت دسترسی؛ اگر secret واقعی یافت شد rotate/revoke اول، بعد پاک‌سازی تاریخچه با برنامه و هماهنگی. |
| G-16 | Deferred | طرح Steam/GamingAccounts یک ADR پیشنهادی است و PR #19 بسته شده؛ مجازبودن مدل حساب و روش integration هنوز شرط ورود است. | عمداً پیاده‌سازی نشده | نیاز محصول صریح + بررسی برنامهٔ رسمی/اجازهٔ Valve + تصمیم محصول/ADR؛ تا آن زمان هیچ credential injection، MFA bypass یا Auto Login فرضی ساخته نشود. |
| G-17 | P1 پیش از Release | Foundation برای PostgreSQL/migrations، audit/idempotency/outbox و backup/restore آزمون دارد؛ اما زمان‌بندی/retention، محل امن نسخه‌های پشتیبان و RPO/RTO محصولی هنوز کامل نشده‌اند. | بخشی Foundation موجود؛ عملیات کامل باز | restore دوره‌ای و ایزوله، retention، هشدار شکست، صلاحیت دسترسی، آزمون بازیابی/تطبیق و ثبت زمان/داده از دست‌رفته. |

## Ordered execution route

این ترتیب، وضعیت فعلی را به مراحل قابل‌بستن تبدیل می‌کند؛ شمارهٔ Stageهای محصول در Master Build Plan همچنان مرجع باقی می‌ماند.

1. **تثبیت مستندات و CI (PR #25):** Quick Validation باید روی آخرین SHA موفق بماند؛ این مرحلهٔ مستنداتی است و Foundation/LAN را گواهی نمی‌کند.
2. **تأیید خط مبنا و بازبینی شاخه‌ها:** Foundation روی tip فعلی be29687e637b709158a30204bb9213bfa4813950 دقیقاً دوباره گواهی شود. PR #23 و #24 با ancestry/diff و CI همان SHA بازبینی شوند؛ merge مستقیم به main یا Foundation ممنوع است.
3. **قرارداد و مخزن امن اسرار Server:** ADR-0002 در PR #18 را با ADR-0001 و threat model مرور کنید؛ PR #20 را قطعه‌به‌قطعه با candidate فعلی مقایسه کنید، نه اینکه branch قدیمی/stack‌شده را یک‌جا merge کنید. DPAPI LocalMachine، Service SID و ACL دقیق، fail-closed، redaction، rotation/recovery و حذف فقط BootstrapSecret باید با آزمون پوشش داده شوند. اسکریپت inspect-manager-runtime-state.ps1 در PR #20 صرفاً برای preflight خواندنی است؛ گزارش آن قبل از هر عملیات نصب/گواهی/cleanup روی Manager بررسی شود. تا آن زمان --provision-secrets و TLS/ACL cleanup اجرا نشود.
4. **گواهی واقعی Windows Service و LAN:** با DB دورریختنی و اسرار آزمایشی، هویت و Service SID واقعی Server/Agent، private-key ACL، خواندن secret تحت همان توکن، Agent enrollment با توکن صادرشدهٔ Server و redemption اتمیک یک‌بارمصرف، رد replay/رقابت، lease/heartbeat و reconnect پس از restart آزمایش شود. هیچ انتقال SMB/فایل دستی، env مشترک یا TLS bypass به‌عنوان production راه‌حل پذیرفته نیست.
5. **ادامهٔ مرزبندی UI در شاخهٔ جدا:** PR #24 فقط اولین slice است. پس از پذیرش runtime boundary، Timer سلامت Agent، Login/Change Password views، navigation/content host، permission-aware commands و تست‌های binding/locale به‌صورت تغییرهای کوچک انجام شوند؛ بازطراحی بصری بزرگ با این مراحل مخلوط نشود.
6. **قابلیت‌های محصولی:** پس از gate سرویس/LAN، Stageهای Module skeleton، Identity/Authorization، Stations/Agents و Customers طبق Master Build Plan و requirement→test trace اجرا شوند؛ سپس Session/Finance/Inventory و بقیه به‌صورت vertical slice. هیچ financial authority داخل Desktop نیست.
7. **Release boundary:** Setup/update/repair/rollback/signature/backup recovery و تست ۲–۳ دستگاه، سپس pilot مقیاس ۱۵ ایستگاه؛ این‌ها اجباری پیش از production هستند، نه جایگزین توسعهٔ صحیح ماژول‌ها.
8. **Steam و قابلیت‌های Deferred:** هیچ feature پیشنهادی از branch قدیمی بدون requirement/ADR و مجوز بیرونی وارد برنامه نمی‌شود.

## Branch and PR disposition

| PR / branch | وضعیت و تصمیم فعلی |
|---|---|
| #25 — docs/engineering-readiness-audit-v1 | Draft فعال؛ این PR همین register و هماهنگی مستندات/CI را پیشنهاد می‌کند. باید Quick Validation روی آخرین SHA موفق شود؛ ادغام نشده است. |
| #23 — integration/runtime-operator-v1 | Draft فعال؛ Quick و Full Foundation روی SHA 55a340... سبز. ادغام هنوز نیازمند بازبینی ancestry/diff و gate سرویس/شبکه است. |
| #24 — refactor/desktop-view-boundaries-v1 | Draft فعال؛ Quick و Full Foundation روی SHA 9a058... سبز. هنوز ادغام نشده؛ گام اول جداسازی UI است، نه پایان معماری. |
| #15 — fix/programdata-endpoints-v1 | Draft قدیمی به مقصد Foundation؛ به‌عنوان منبع مقایسه نگه دارید. تغییر TLS آن را بدون مقایسه با مدل فعلی certificate-store merge نکنید. |
| #17 — docs/runtime-lan-smoke-evidence-2026-10-10 | شواهد تاریخی ارزشمند است ولی روی branch/candidate قدیمی. شواهد و محدودیت‌ها باید مرجع داشته باشند؛ branch کامل آن ادغام نشود. |
| #18 — security/server-secret-lifecycle-contract-v1 | طرح امنیتی مفید ولی base قدیمی؛ قراردادهای لازم انتخابی به سند مرجع وارد شوند، بدون merge کورکورانه. |
| #20 — fix/server-secret-store-v1 | کد مخزن اسرار DPAPI/Service SID و اسکریپت preflight خواندنی دارد، اما روی PR #18 و base قدیمی stack شده؛ diff به‌صورت قطعه‌ای بازبینی و فقط قسمت‌های سازگار port شود. هیچ --provision-secrets یا تغییر ماشین تا بررسی وضعیت فعلی Manager و تأیید ADR اجرا نشود. |
| #21 — feature/desktop-operator-shell-v1 | نمونه/طرح UI روی runtime branch قدیمی؛ ایده‌ها ممکن است مرجع طراحی باشند اما XAML/VM آن نباید بدون diff به‌صورت کامل ادغام شود. |
| #19 — Steam account lifecycle | PR بسته شده؛ branch را به‌عنوان آزمایش تاریخی نگه دارید تا تصمیم دامنه و سیاست بیرونی قطعی شود. |

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
