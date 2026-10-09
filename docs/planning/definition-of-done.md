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
