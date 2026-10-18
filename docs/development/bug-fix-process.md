# فرایند استاندارد باگ و تغییر — GameNet 5

دفتر docs/development/bug-fix-log.md یک ثبت تاریخی و append-only است. برای هر ورودی جدید از docs/templates/bug-fix-template.md و برای هر برش قابلیت از docs/templates/vertical-slice-template.md استفاده کن. نیازمندی مرتبط باید به docs/planning/requirements-traceability.md متصل باشد.

## شدت و وضعیت

شدت:
- S0 Critical: خطر پول/موجودی، credential، دورزدن مجوز، مالکیت دوگانه Session/Agent یا تخریب داده.
- S1 High: یکی از مسیرهای اصلی عملیات یا قابلیت بازیابی از کار افتاده است.
- S2 Medium: رفتار مهم اشتباه است ولی اثر محدود و قابل‌کنترل است.
- S3 Low: polish یا مسئلهٔ کم‌اثر؛ مشکل امنیتی/مالی را کم‌اهمیت برچسب نزن.

جریان وضعیت:
NEW → TRIAGED → READY → IN_PROGRESS → FIXED_UNVERIFIED → VERIFIED → CLOSED
BLOCKED می‌تواند برای مورد باز استفاده شود ولی علت، مالک و شرط رفع مانع باید نوشته شود. ROOT_CAUSE_UNCONFIRMED برچسب تشخیصی است؛ فرضیه را واقعیت اعلام نکن.

## جریان اجباری
1. ثبت ID پایدار، severity، actor، expected/actual، repro، محیط و اثر؛ secrets/tokens/PII از log حذف شوند.
2. baseline branch/SHA، working tree، آخرین checkها و محیط واقعی را بررسی و ثبت کن.
3. شواهد پیش از fix را حفظ کن. اگر repro وجود ندارد، دقیقاً بگو چه داده‌ای کم است.
4. علت ریشه‌ای، evidence، invariant نقض‌شده و مالک لایه را تعیین کن؛ فرضیه‌های رقیب را بررسی کن.
5. کوچک‌ترین اصلاح لایهٔ مالک و regression test را قبل از fix تعریف کن. اثر migration، مالی، permission، audit، retry/concurrency و rollback را لحاظ کن.
6. یک شاخه/برش را به یک علت یا خروجی منسجم محدود کن؛ refactor نامرتبط، حذف تست یا دورزدن gate ممنوع.
7. ابتدا تست کوچک/regression؛ سپس canonical/quick؛ سپس integration/runtime/Foundation gate متناسب. نتیجهٔ ناموفق نیز ثبت می‌شود.
8. SHA دقیق، لینک CI/check، test name، log/artifact، نتیجهٔ قبل/بعد و ریسک باقی‌مانده را ثبت کن.
9. regression دائمی و traceability را به‌روز کن؛ سپس فقط با شواهد مطابق DoD ببند.

## قواعد وضعیت
- علت نامعلوم: باز یا BLOCKED بماند.
- patch بدون آزمون لازم: FIXED_UNVERIFIED بماند.
- CI/runtime نامعلوم یا fail: CLOSED نشود.
- شکست Runner/محیط جداگانه ثبت شود؛ باگ محصول از روی حدس بسته نشود.
- باگ تاریخی دوباره بروز کرد؟ regression موجود را تقویت کن، نه اینکه فقط workaround تازه بسازی.
- تاریخچهٔ قبلی دفتر بازنویسی نمی‌شود؛ اصلاح factual اشتباه با توضیح شفاف افزوده می‌شود.

## نگهداری Bug-Fix Log
هر ورودی جدید باید symptom، expected/actual، repro، exact SHA/environment، impact/severity، root cause/evidence، owner/invariant، fix، regression, verification URL/result، migration/rollback و status داشته باشد. اگر فیلدی واقعاً کاربرد ندارد، N/A همراه دلیل ثبت شود؛ فیلد حیاتی بی‌توضیح خالی نماند.
