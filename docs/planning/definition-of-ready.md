# Definition of Ready — معیار آمادگی کار

هیچ برش عادی محصول پیش از تکمیل این معیارها وارد پیاده‌سازی نمی‌شود. هدف، کشف نکردن تصمیم‌های حساس مربوط به پول، مالکیت، امنیت و بازیابی وسط کدنویسی است.

## اطلاعات پایه
- [ ] نوع کار مشخص است: باگ، قابلیت، hardening، refactor یا انتشار.
- [ ] شناسهٔ Issue/Work و Requirement ID از ماتریس رهگیری نیازمندی‌ها ثبت شده است.
- [ ] مشکل کاربر/اپراتور و نتیجهٔ مورد انتظار شفاف است.
- [ ] دامنه، non-goals و معیارهای پذیرش قابل‌آزمون تعریف شده‌اند.
- [ ] اولویت، شدت، وابستگی و ریسک باقی‌مانده مشخص‌اند.
- [ ] baseline branch/SHA و وضعیت فعلی کد و checkها بررسی شده‌اند.

## دامنه و مالکیت دامنه
- [ ] ماژول مالک و مرز لایه معلوم است؛ UI مالک قانون تجاری نیست.
- [ ] منبع حقیقت و داده‌های authoritative مشخص‌اند.
- [ ] invariantها و state transition/preconditionها تعریف شده‌اند.
- [ ] رفتار با دادهٔ قبلی/ناقص و نسخهٔ قدیمی مشخص است.
- [ ] تعامل بین ماژول‌ها از قرارداد صریح می‌گذرد، نه دسترسی به داخلی‌های همدیگر.

## قرارداد، persistence و تراکنش
- [ ] قراردادهای Server/Desktop/Agent/Shared و اثر versioning روشن‌اند.
- [ ] اثر schema، migration، نصب، upgrade و rollback بررسی شده است.
- [ ] مرز تراکنش و رفتار rollback تعریف شده است.
- [ ] idempotency key، scope، request hash و replay/conflict مشخص‌اند، اگر retry ممکن است.
- [ ] concurrency rule و constraint پایگاه داده برای invariant حساس معلوم‌اند.
- [ ] side effect خارجی داخل transaction callback نیست؛ در صورت نیاز از Outbox پایدار پس از commit استفاده می‌شود.

## امنیت، عملیات و UX
- [ ] actor، permission سمت Server، approval و Audit لازم تعریف شده‌اند.
- [ ] خطر افشای داده، credential و privilege escalation بررسی شده است.
- [ ] failure/timeout/retry/disconnect/restart/recovery/reconciliation مشخص است.
- [ ] log، correlation و diagnostics برای عیب‌یابی کافی است.
- [ ] loading/empty/error/offline، keyboard و فارسی RTL/en-US بررسی شده‌اند.
- [ ] اثر روی زمان، پول، حسابداری، موجودی، گزارش و history روشن است.

## برنامهٔ راستی‌آزمایی
- [ ] آزمون دامنه و invariantها مشخص‌اند.
- [ ] PostgreSQL integration/migration/concurrency tests لازم تعیین شده‌اند.
- [ ] contract، permission، idempotency و E2E در صورت کاربرد تعریف شده‌اند.
- [ ] نیاز runtime evidence، ویندوز/شبکه/سخت‌افزار مشخص است.
- [ ] معیار pass/fail، artifact و SHA مورد انتظار مشخص‌اند.

## استثنای باگ بحرانی
مهار امن و برگشت‌پذیر رخداد بحرانی می‌تواند پیش از RCA نهایی انجام شود. در این استثنا علت باید صریحاً «تأییدنشده» بماند، شواهد حفظ شود، پیگیری علت ریشه‌ای مالک داشته باشد و باگ تا عبور از Definition of Done بسته نشود.

## تصمیم آمادگی
- READY: موارد لازم تکمیل و مانع مسدودکننده‌ای باقی نمانده است.
- BLOCKED: تصمیم مهم، dependency یا دسترسی محیط نامعلوم است؛ پیاده‌سازی عادی شروع نمی‌شود.
- EMERGENCY CONTAINMENT: فقط مهار رخداد بحرانی با دامنهٔ حداقلی و پیگیری الزامی.
