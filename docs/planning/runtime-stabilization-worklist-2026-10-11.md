# GameNet 5 — برنامه تثبیت CI و بستن Foundation
تاریخ ثبت: 2026-10-11

## تصمیم اجرایی فعلی
- معماری محصول از صفر بازنویسی نمی‌شود؛ تغییرها کوچک، قابل بازبینی و مبتنی بر شاخهٔ مرجع خواهند بود.
- Runner خودمیزبان کاربر حذف یا دست‌کاری نمی‌شود.
- چون مخزن عمومی است، هیچ Pull Request عمومی/غیرقابل‌اعتماد نباید کد خود را روی Runner خودمیزبان اجرا کند.
- مسیر هدف: GitHub-hosted Windows برای Quick Validation و گیت‌های کدی PR؛ Runner خودمیزبان فقط برای آزمون‌های کنترل‌شده‌ای که واقعاً به ماشین/شبکهٔ اختصاصی نیاز دارند، نه اجرای خودکار PR.
- تغییر هیچ‌یک از `main` و `foundation/runtime-final-v2` مجاز نیست مگر در PR نگهداری جداگانه، با دلیل و گواهی مستقل.

## شواهد Runner
- آزمون GitHub-hosted Windows با SDK دقیق .NET `10.0.401`، PowerShell parser و `scripts/verify.ps1` موفق شد.
- Run: https://github.com/alizebal73/5/actions/runs/38130781617
- SHA آزمایش Quick Validation: `c80c4dc9bf4050f706f1eb710a57f67201bede79`
- آزمون Full Foundation روی GitHub-hosted Windows در اجرای اول به گیت هم‌زمانی Migration رسید و آنجا شکست خورد: کارگر فرعی دستور سراسری `dotnet-ef` را می‌خواست، درحالی‌که فقط ابزار محلی مخزن restore شده بود. این وابستگی پنهان ثبت شد؛ فراخوانی به اجرای ابزار محلی تغییر داده شده و اجرای مجدد روی شاخهٔ آزمایشی در حال انجام است. تا موفقیت اجرای مجدد، انتقال کامل گیت Foundation را نهایی نمی‌کنیم.
- در تصویر استاندارد فعلی GitHub-hosted Windows، PostgreSQL 17 موجود است اما سرویس به‌صورت پیش‌فرض متوقف/غیرفعال است؛ Workflow باید دیتابیس دورریختنی را فقط داخل VM همان Job راه‌اندازی کند و از Machine secret یا دیتابیس کامپیوتر مدیر وابسته نباشد.

## ترتیب الزامی کار

### P0 — ایمن‌سازی CI
- [ ] تکمیل آزمون Quick Validation روی GitHub-hosted Windows و ثبت نتیجهٔ دقیق.
- [ ] تعیین نتیجهٔ آزمون Full Foundation روی GitHub-hosted Windows؛ در صورت شکست، علت را جداگانه رفع/ثبت کنیم، نه اینکه گیت را حذف کنیم.
- [ ] انتقال Quick Validation و build/package قابل‌تکرار از Runner خودمیزبان به GitHub-hosted Windows.
- [ ] Full Foundation را روی GitHub-hosted با PostgreSQL آزمایشی و موقت اجرا کنیم؛ هیچ رمز، متغیر Machine-level یا دادهٔ واقعی Manager به VM ارسال نشود.
- [ ] Runner خودمیزبان حذف نشود؛ هر Workflow خودمیزبان نباید برای رویداد `pull_request` کد شاخهٔ ورودی را اجرا کند.
- [ ] پس از بازبینی diff و اجرای هر دو مسیر، PR محدود برای Workflowها باز کنیم؛ هیچ Workflow گیتی را برای سبزشدن دور نزنیم.

### P1 — حاکمیت شاخه و وضعیت واقعی
- [ ] به‌روزرسانی `engineering-readiness-register.md`، `branch-governance.md` و `stable-checkpoint.md` با SHA، PR و Runهای واقعی؛ حذف وضعیت‌های منسوخ و متناقض.
- [ ] حفاظت `integration/runtime-operator-v1` با PR و بررسی‌های اجباری را تنظیم/تأیید کنیم؛ وضعیت Ruleset را از GitHub دوباره بخوانیم.
- [ ] وضعیت PRهای باز و بسته را تعیین تکلیف کنیم؛ PRهای #27/#28/#29 شاخهٔ ادغام نیستند و نباید دوباره ادغام شوند. PR #30 قبلاً ادغام شده است.
- [ ] برای هر گیت، SHA دقیقِ checkout و لینک Run را ثبت کنیم؛ مدرک SHA قدیمی به نوک جدید شاخه نسبت داده نشود.

### P1 — قرارداد و پذیرش امنیت Runtime
- [ ] ADR-0002 را با کد فعلی Secret Store و Enrollment تطبیق و به قرارداد پذیرفته‌شده تبدیل کنیم.
- [ ] هویت Windows Service، Service SID، ACL مؤثر، محدوده DPAPI، مسیرهای دو فایل Secret، Bootstrap cleanup، چرخش و بازیابی را مستند کنیم.
- [ ] پیش از هر اقدام روی کامپیوتر مدیر، `inspect-manager-runtime-state.ps1` را فقط‌خواندنی اجرا و خروجی آن را بازبینی کنیم؛ Provisioning/پاک‌سازی ACL یا گواهی کورکورانه ممنوع است.
- [ ] در محیط آزمایشی جدا، بعد از نصب واقعی سرویس Server/Agent، دسترسی DPAPI و کلید TLS پس از Restart را اثبات کنیم.
- [ ] HTTPS با اعتبارسنجی عادی گواهی، Enrollment یک‌بارمصرف و اتمیک، انقضا/Replay/Revoke/رقابت، ذخیرهٔ Credential، lease/heartbeat، reconnect و Restart را روی دو کامپیوتر آزمایش کنیم.
- [ ] آزمون مقدماتی LAN قدیمی را شاهد تاریخی نگه داریم، نه تأیید سرویس واقعی نسخهٔ جاری.

### P1 — بستن Foundation و مستندات
- [ ] تناقض Stage 1 و Gate A را رفع کنیم؛ Setup/Updater/Repair/Rollback در Stageهای انتشار خود باقی بمانند.
- [ ] Outbox را دقیق طبقه‌بندی کنیم: ذخیره/Claim در برابر تحویل واقعی؛ پیش از هر وابستگی محصولی، Worker، retry/backoff، شکست نهایی، Idempotency مصرف‌کننده و تست رقابت/بازیابی تکمیل شود یا صریحاً به گیت وابسته موکول شود.
- [ ] برای هر یک از نیازمندی‌ها، زنجیرهٔ REQ → تصمیم/Invariant → کد/API → تست → Run/SHA ثبت شود.
- [ ] آزمون‌های Auth/Owner/permissions/lockout/session revoke و رد درخواست غیرمجاز مستقیم به Server کامل شوند.
- [ ] آزمون ساختاریافتهٔ logging/correlation، حذف Secret از log/artifact، readiness/health، backup retention، هشدار شکست و آزمون دوره‌ای restore با RPO/RTO تعریف شود.
- [ ] اسکن تاریخچهٔ کامل Git/branch/artifact برای Secret، اسکن آسیب‌پذیری وابستگی‌ها، مجوز کتابخانه‌ها، SBOM و امضای release در گیت‌های مناسب ثبت شود؛ در صورت یافتن Secret واقعی، اول revoke/rotate.

### P2 — ادامهٔ محصول پس از تثبیت Foundation
- [ ] Desktop را مرحله‌ای تکمیل کنیم: lifecycle/cancel timerها، view boundaries، permission-aware commands، UI smoke، RTL/LTR، loading/error/offline/permission-denied و تأیید نتیجه از Server.
- [ ] هر قابلیت را در یک vertical slice محدود با migration، مالک داده، permission، transaction، idempotency/concurrency، audit، recovery و رگرسیون PostgreSQL توسعه دهیم.
- [ ] قابلیت‌های محصول طبق Capability Matrix و Master Build Plan پیش بروند؛ از ادغام کامل شاخه‌های قدیمی خودداری شود.
- [ ] Games و Game Account Pool به‌عنوان نیاز محصولی پیگیری شوند؛ Steam Auto-login تا تصویب نیاز و روش مجاز، جدا و Deferred بماند.

### P3 — پیش از استفادهٔ واقعی در گیم‌نت
- [ ] تصمیم و اجرای امن نصب/provision PostgreSQL؛ جداسازی حساب Runtime از حساب Migration/DDL و ممنوعیت migration خودکار در startup تولید.
- [ ] Setup/upgrade/repair/uninstall/update/rollback، امضای بسته، checksum/manifest و بازیابی نصب واقعی را آزمایش کنیم.
- [ ] پذیرش ۲–۳ کامپیوتر، سپس پایلوت حدود ۱۵ ایستگاه، همراه با قطع شبکه/برق، Restart، بازیابی و بررسی عدم ازدست‌رفتن داده.
- [ ] Runbook نصب، Backup/Restore، چرخش گواهی و Secret، عیب‌یابی و بهره‌برداری ۲۴ساعته را نهایی کنیم.

## تعریف بستن هر کار
یک کار فقط وقتی بسته است که diff محدود و بازبینی‌شده باشد، آزمون لازم روی همان SHA اجرا شود، مدرک Run قابل دسترسی باشد، نتیجهٔ ناموفق/اجرا‌نشده پنهان نشود، ریسک باقی‌مانده ثبت شود و مسیر بازگشت روشن باشد.

## محدودیت‌های صریح
- این فایل برنامهٔ اجرا و ثبت شواهد است، نه اعلام آمادگی Production.
- تا قبل از پذیرش واقعی سرویس و LAN، Stage 1 نهایی اعلام نمی‌شود.
- `main` و `foundation/runtime-final-v2` دست‌نخورده می‌مانند.
