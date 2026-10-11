# GameNet 5 — برنامه تثبیت CI و بستن Foundation
تاریخ ثبت: 2026-10-11

## تصمیم اجرایی فعلی
- معماری محصول از صفر بازنویسی نمی‌شود؛ تغییرها کوچک، قابل بازبینی و مبتنی بر شاخهٔ مرجع خواهند بود.
- Runner خودمیزبان کاربر از سیستم حذف یا دوباره نصب نمی‌شود؛ این تغییرات آن را هدف نمی‌گیرند.
- مسیر CI خودکار: GitHub-hosted Windows برای Quick Validation، Full Foundation و ساخت payload؛ Runner خودمیزبان فقط برای آزمون فیزیکی/شبکهٔ مورداعتماد در مرحلهٔ پذیرش.
- نکتهٔ امنیتی باز: در مخزن عمومی، تغییر `runs-on` در PR غیرقابل‌اعتماد می‌تواند خطر را برگرداند؛ تغییر YAML به‌تنهایی کنترل دسترسی Runner را اثبات نمی‌کند. تا قبل از تأیید تنظیمات دسترسی Runner، آن را در دوره‌های اجرای PRهای غیرقابل‌اعتماد آنلاین نگذارید؛ اگر runner-group محدود به workflow/ref مورداعتماد در این حساب وجود ندارد، سرویس Runner فقط برای آزمون دستی مورداعتماد فعال شود. اتصال GitHub فعلی امکان نوشتن تنظیمات Runner/Ruleset را به ما نداده است.
- تغییر هیچ‌یک از `main` و `foundation/runtime-final-v2` مجاز نیست مگر در PR نگهداری جداگانه، با دلیل و گواهی مستقل.

## شواهد Runner
- آزمون GitHub-hosted Windows با SDK دقیق .NET `10.0.401`، PowerShell parser و `scripts/verify.ps1` موفق شد: [Run #38130781617](https://github.com/alizebal73/5/actions/runs/38130781617)، SHA `c80c4dc9bf4050f706f1eb710a57f67201bede79`.
- اجرای تکراری Quick Validation پس از اصلاح portability نیز موفق شد: [Run #38131165436](https://github.com/alizebal73/5/actions/runs/38131165436)، SHA `636741d82dea5bb8f8f2c269c39784b5739316ee`.
- اجرای کامل Full Foundation روی GitHub-hosted Windows نیز موفق شد: [Run #38131165423](https://github.com/alizebal73/5/actions/runs/38131165423)، SHA `636741d82dea5bb8f8f2c269c39784b5739316ee`. همهٔ گیت‌ها سبز شدند: parser، build/unit tests، PostgreSQL migration/concurrency، backup/restore، Desktop runtime و Agent runtime.
- اجرای اول Full Foundation [#38130941325](https://github.com/alizebal73/5/actions/runs/38130941325) وابستگی پنهان به ابزار سراسری `dotnet-ef` را کشف کرد. فراخوانی worker به `dotnet tool run dotnet-ef -- ...` تغییر کرد و اجرای دوم کامل را گذراند. این اصلاح باید در PR اصلی نیز حفظ شود.
- در تصویر استاندارد فعلی GitHub-hosted Windows، PostgreSQL 17 موجود است اما سرویس به‌صورت پیش‌فرض متوقف/غیرفعال است؛ Workflow باید دیتابیس دورریختنی را فقط داخل VM همان Job راه‌اندازی کند و از Machine secret یا دیتابیس کامپیوتر مدیر وابسته نباشد.

## ترتیب الزامی کار

### P0 — ایمن‌سازی CI
- [x] اثبات Quick Validation روی GitHub-hosted Windows و ثبت نتیجهٔ دقیق (#38130781617 و #38131165436).
- [x] اثبات Full Foundation روی GitHub-hosted Windows در harness آزمایشی؛ تمام گیت‌ها در #38131165423 سبز شدند.
- [ ] انتقال Quick Validation و build/package قابل‌تکرار از Runner خودمیزبان به GitHub-hosted Windows.
- [x] مسیر Full Foundation روی VM موقت GitHub-hosted با PostgreSQL آزمایشی اثبات شد؛ از اتصال DB یا اسرار سیستم Manager استفاده نشد.
- [ ] Runner خودمیزبان حذف نشود؛ دسترسی واقعی آن از تنظیمات GitHub محدود شود یا فقط هنگام اجرای تست دستی مورداعتماد online باشد. صرفاً hosted کردن YAML فعلی، در یک مخزن عمومی، تضمین نمی‌کند PR بعدی نتواند `runs-on` را تغییر دهد.
- [ ] PR محدود Workflowها و اصلاح ابزار EF محلی را بازبینی کنیم؛ پس از push، Quick/Full/payload را در Workflowهای نهایی و روی SHA واحد تأیید کنیم؛ هیچ گیتی حذف نشود.

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

## نتیجهٔ تصمیم Runner
- انتخاب فعلی بر اساس آزمون: CI خودکار روی `windows-2025`، چون Quick و Full Foundation روی GitHub-hosted با موفقیت اجرا شدند؛ PostgreSQL/Backup-Restore/Desktop/Agent نیز در محیط موقت موفق شدند.
- Runner خودمیزبان حفظ می‌شود ولی از Workflowهای خودکار عمومی استفاده نمی‌شود. کنترل دسترسی خود Runner هنوز باید در سطح GitHub/زمان روشن بودن سرویس تعیین تکلیف شود؛ آن را Done امنیتی حساب نکنید.

## محدودیت‌های صریح
- این فایل برنامهٔ اجرا و ثبت شواهد است، نه اعلام آمادگی Production.
- تا قبل از پذیرش واقعی سرویس و LAN، Stage 1 نهایی اعلام نمی‌شود.
- `main` و `foundation/runtime-final-v2` دست‌نخورده می‌مانند.
