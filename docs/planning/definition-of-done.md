# Definition of Done — معیار پایان کار

Done فقط زمانی ثبت می‌شود که شواهد دقیقاً با ادعای پایان کار منطبق باشند. وجود کد، صفحه یا یک تست محدود کافی نیست.

## رفتار و پذیرش
- [ ] تمام معیارهای پذیرش پاس شده‌اند.
- [ ] رفتار نامعتبر و خطای قابل‌فهم فارسی پوشش داده شده است.
- [ ] loading/empty/offline/reconnect در UI لازم پیاده شده است.
- [ ] mock، پاسخ موفقیت جعلی و مسیر نمایشی در production وجود ندارد.
- [ ] Cancel/Refund/Reverse برای عملیات قابل‌برگشت تعریف شده یا صریحاً عدم امکان آن ثبت شده است.

## معماری و صحت داده
- [ ] قانون در ماژول مالک و لایهٔ درست قرار دارد.
- [ ] architecture/source-size/placeholder guards پاس شده‌اند.
- [ ] Shared contract مرجع یگانه است و اثر versioning روشن است.
- [ ] migration/model snapshot و clean/upgrade در صورت تغییر schema معتبرند.
- [ ] permission سمت Server، Approval و Audit لازم فعال‌اند.
- [ ] transaction، rollback، idempotency و concurrency مطابق طراحی آزموده شده‌اند.
- [ ] failure/recovery/reconciliation برای state پایدار بررسی شده است.
- [ ] اثر update/rollback و سازگاری Server/Desktop/Agent مشخص است.


## UI و اتصال عملیات (در صورت اثر بر Desktop)
- [ ] View فقط presentation/Binding است؛ قانون تجاری، مالی، permission یا دسترسی PostgreSQL به XAML/code-behind منتقل نشده است.
- [ ] Command/ ViewModelها از قرارداد typed و API client استفاده می‌کنند؛ چند نقطهٔ ورود UI قانون دامنه را تکثیر نمی‌کنند.
- [ ] code-behind به رفتار محدود WPF نیازمند آن محدود است؛ مسیر موفقیت جعلی و mock ماندگار در production وجود ندارد.
- [ ] وضعیت‌های loading/empty/offline/timeout/denied/conflict و خطاهای فارسی مطابق نیازمندی پیاده شده‌اند.
- [ ] Binding/Resource/localization و ViewModel/Command tests متناسب با تغییر اجرا شده‌اند؛ سطح RTL/LTR، keyboard/focus و PasswordBox در صورت اثر بررسی شده است.
- [ ] عملیات موفقیت مالی/نشست فقط بعد از تأیید معتبر Server نمایش داده می‌شود؛ رفتار retry/timeout با idempotency/reload معتبر است.
- [ ] اگر UI چند محل دسترسی به یک عملیات دارد، هرکدام به use case/contract واحد متصل است و regression آن‌ها بررسی شده است.
- [ ] [قرارداد UI و برش عمودی](../development/ui-architecture-and-vertical-slice-policy.md) و Action Catalog (در صورت کاربرد) به‌روز است.

## شواهد آزمون
- [ ] unit tests پاس‌اند.
- [ ] PostgreSQL integration/migration/concurrency tests در صورت کاربرد پاس‌اند.
- [ ] contract/E2E/Desktop/Agent tests متناسب پاس‌اند.
- [ ] canonical یا سریع‌ترین gate مجاز و تمام gateهای لازم سطح بالاتر اجرا شده‌اند.
- [ ] check موفق مربوط به همان PR/SHA است؛ نتیجهٔ SHA قدیمی قابل تعمیم نیست.
- [ ] لینک workflow، artifact، test name، محیط و exact tested SHA ثبت شده‌اند.
- [ ] warning جدید حل‌نشده بدون دلیل و استثنای مصوب باقی نمانده است.

## قواعد ویژهٔ باگ
- [ ] علت ریشه‌ای با evidence تأیید شده یا صریحاً تأییدنشده و باز مانده است.
- [ ] regression test پیش از fix شکست رفتار معیوب را نشان می‌دهد و پس از fix پاس می‌شود؛ در غیر این صورت استثنا و آزمون جایگزین بازبینی شده است.
- [ ] مشکل در لایهٔ مالک اصلاح شده؛ workaround در UI جایگزین اصلاح Server/domain/data نشده است.
- [ ] repro، نتیجهٔ قبل/بعد و خطر باقی‌مانده ثبت شده‌اند.

## مستندات و closure
- [ ] REQ/bug record و Bug-Fix Log همراه evidence به‌روزند.
- [ ] exact SHA و baseline ثبت شده‌اند.
- [ ] ADR/migration/release note/runbook در صورت تأثیر به‌روز است.
- [ ] محدودیت، ریسک باقی‌مانده و rollback مشخص است.
- [ ] وضعیت یکی از DONE-VERIFIED، FIXED-UNVERIFIED، BLOCKED یا NOT-VERIFIED است؛ کار نیمه‌آزموده Done/Closed نمی‌شود.

## مرز Done در برابر Release
Done برای یک برش، قبولی معیارهای همان برش است؛ به معنی Foundation Certified، Physical Pilot Passed یا Production Release Ready نیست مگر گیت جداگانهٔ آن ادعا روی نسخه و SHA دقیق پاس شده باشد.
