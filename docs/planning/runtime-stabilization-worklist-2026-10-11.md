# GameNet 5 — برنامه تثبیت CI و بستن Foundation
تاریخ ثبت: 2026-10-11

## تصمیم اجرایی فعلی (بازخوانی نهایی 2026-10-11)
- تغییرها کوچک و در PRهای جدا می‌مانند؛ main و Foundation مستقیم تغییر نمی‌کنند.
- Ruleset شاخهٔ integration/runtime-operator-v1 فعال و read-back شده است: protect-integration-runtime، ID 24879997، با check اجباری quick-validation، PR، حل مکالمات، منع force-push/deletion، Squash-only و bypass خالی.
- PR #31 روی head e5caceb11968512fb91b5f808cbd4afd24be22ad پنج گیت hosted را دقیقاً روی همان SHA سبز گذرانده است؛ PR همچنان Draft/ادغام‌نشده است.
- PR #32 کاندید نگهداری Foundation جداگانه و Draft است؛ Full Foundation روی SHA 4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9 موفق بوده است.
- Runner شخصی بنا به تصمیم مالک تا پایان GameNet حفظ می‌شود و سپس حذف خواهد شد؛ Issue #33 پیگیری می‌کند. Runner-group restrictions تأیید نشده‌اند.
- تست واقعی Windows Service و LAN انجام نشده؛ تجاری‌سازی تا گذراندن این پذیرش آغاز نمی‌شود.

## آخرین شواهد دقیق — 2026-10-11

کاندید PR #31، SHA e5caceb11968512fb91b5f808cbd4afd24be22ad، هر پنج گیت زیر را روی همان SHA موفق گذرانده است:
- Quick push [#38146060096](https://github.com/alizebal73/5/actions/runs/38146060096)
- Quick PR [#38146064180](https://github.com/alizebal73/5/actions/runs/38146064180)
- Full Foundation [#38146060115](https://github.com/alizebal73/5/actions/runs/38146060115)
- Runtime payload push [#38146060239](https://github.com/alizebal73/5/actions/runs/38146060239)
- Runtime payload PR [#38146064208](https://github.com/alizebal73/5/actions/runs/38146064208)

این CI خودکار است، نه اثبات نصب/اجرای سرویس ویندوز، DPAPI/ACL زیر identity واقعی یا LAN فیزیکی.

## شواهد Runner
- آزمون GitHub-hosted Windows با SDK دقیق .NET `10.0.401`، PowerShell parser و `scripts/verify.ps1` موفق شد: [Run #38130781617](https://github.com/alizebal73/5/actions/runs/38130781617)، SHA `c80c4dc9bf4050f706f1eb710a57f67201bede79`.
- اجرای تکراری Quick Validation پس از اصلاح portability نیز موفق شد: [Run #38131165436](https://github.com/alizebal73/5/actions/runs/38131165436)، SHA `636741d82dea5bb8f8f2c269c39784b5739316ee`.
- اجرای کامل Full Foundation روی GitHub-hosted Windows نیز موفق شد: [Run #38131165423](https://github.com/alizebal73/5/actions/runs/38131165423)، SHA `636741d82dea5bb8f8f2c269c39784b5739316ee`. همهٔ گیت‌ها سبز شدند: parser، build/unit tests، PostgreSQL migration/concurrency، backup/restore، Desktop runtime و Agent runtime.
- اجرای اول Full Foundation [#38130941325](https://github.com/alizebal73/5/actions/runs/38130941325) وابستگی پنهان به ابزار سراسری `dotnet-ef` را کشف کرد. فراخوانی worker به `dotnet tool run dotnet-ef -- ...` تغییر کرد و اجرای دوم کامل را گذراند. این اصلاح باید در PR اصلی نیز حفظ شود.
- روی کاندید نهایی شاخهٔ تثبیت، هر سه Workflow با موفقیت و روی SHA یکسان `991152bf60676b2a03c0625780b5dcc16f365032` تمام شدند: Quick Validation [#38131788387](https://github.com/alizebal73/5/actions/runs/38131788387)، Full Foundation [#38131788388](https://github.com/alizebal73/5/actions/runs/38131788388) و Runtime LAN Smoke Payload [#38131788408](https://github.com/alizebal73/5/actions/runs/38131788408). هر سه روی Runnerهای GitHub-hosted اجرا شدند.
- اجرای قبلی payload روی SHA `91b892...` به‌دلیل نیاز EF design-time factory به `GAMENET_DATABASE_CONNECTION` شکست خورد (#38131514035). در کاندید نهایی، bundle فقط با یک connection ساختگیِ process-only، محیط `Production` و آزمون غیرتغییردهندهٔ `--help` ساخته می‌شود؛ بررسی می‌شود که گزینهٔ `--connection` دارد. اجرای نهایی payload موفق شد و هیچ migration روی دیتابیس واقعی اجرا نشد.
- Full Foundation نهایی شامل parser، build/unit tests، PostgreSQL migration/concurrency، backup/restore، Desktop و Agent runtime بود و همهٔ گیت‌ها سبز شدند. این مدرک CI روی SHA کاندید است، نه تأیید نصب واقعی Windows Service یا آزمون فیزیکی LAN.
- در تصویر استاندارد فعلی GitHub-hosted Windows، PostgreSQL 17 موجود است اما سرویس به‌صورت پیش‌فرض متوقف/غیرفعال است؛ Workflow باید دیتابیس دورریختنی را فقط داخل VM همان Job راه‌اندازی کند و از Machine secret یا دیتابیس کامپیوتر مدیر وابسته نباشد.


- نتیجهٔ exact-SHA `77507f4e208efc4cceadeb3c08d34eea01a69660`: Quick [#38134009251](https://github.com/alizebal73/5/actions/runs/38134009251)، Full Foundation [#38134005849](https://github.com/alizebal73/5/actions/runs/38134005849) و Runtime Payload [#38134009263](https://github.com/alizebal73/5/actions/runs/38134009263) همه موفق‌اند.

## ترتیب الزامی کار

### P0 — ایمن‌سازی CI و candidate evidence
- [x] Quick push و PR، Full Foundation و Runtime Payload push و PR روی SHA دقیق e5caceb11968512fb91b5f808cbd4afd24be22ad همگی سبز شدند.
- [x] Integration Ruleset protect-integration-runtime، ID 24879997، ایجاد و read-back شد؛ check اجباری quick-validation.
- [x] Workflowهای PR #31 از hosted windows-2025 استفاده می‌کنند؛ Runner guard و exact-checkout assertion سبز است.
- [x] ابزار تستی ثبت Windows Service به payload اضافه شد. -ValidateOnly روی Server و Agent، manifest/هش executable و helper را بررسی می‌کند بدون query یا تغییر سرویس.
- [x] راهنمای Runtime در payload مسیر دستورها را با پوشهٔ Tools سازگار می‌کند؛ build روی SHA جاری موفق است.
- [~] Runner شخصی بنا به تصمیم مالک تا پایان GameNet حفظ و سپس حذف می‌شود؛ Issue #33 پیگیری می‌کند. Runner-group restriction تأیید نشده است.
- [ ] PR #31 و #32 پس از بازبینی کامل و با Base صحیح تعیین تکلیف شوند؛ ادغام خودکار ممنوع است.

### P1 — حاکمیت شاخه و وضعیت واقعی
- [x] اسناد جاری SHA/Run/PR و Ruleset با GitHub read-back هماهنگ شدند؛ تاریخچهٔ قدیمی به‌عنوان تاریخچه حفظ می‌شود.
- [x] Ruleset واقعی Integration فعال و تأیید شد.
- [x] PR #25 merged؛ #27/#28/#29 closed history-only؛ #26 closed as superseded؛ #30 merged؛ #15/#31/#32 Draft و ادغام‌نشده باقی مانده‌اند.
- [ ] پیش از انتخاب PR #31 به‌عنوان Integration base، diff کامل و ترتیب/هم‌پوشانی با PR #15 و #32 بررسی شود.
- [x] Evidence هر run به SHA دقیق خودش نسبت داده شده است.

### P1 — قرارداد و پذیرش امنیت Runtime
- [x] ADR-0002 به‌عنوان قرارداد پیاده‌سازی Secret Store/Enrollment پذیرفته شد؛ پذیرش عملی Windows Service/DPAPI/ACL/TLS/LAN همچنان باز است.
- [ ] هویت Windows Service، Service SID، ACL مؤثر، محدوده DPAPI، مسیرهای دو فایل Secret، Bootstrap cleanup، چرخش و بازیابی را مستند کنیم.
- [ ] پیش از هر اقدام روی کامپیوتر مدیر، `inspect-manager-runtime-state.ps1` را فقط‌خواندنی اجرا و خروجی آن را بازبینی کنیم؛ Provisioning/پاک‌سازی ACL یا گواهی کورکورانه ممنوع است.
- [ ] در محیط آزمایشی جدا، بعد از نصب واقعی سرویس Server/Agent، دسترسی DPAPI و کلید TLS پس از Restart را اثبات کنیم.
- [ ] HTTPS با اعتبارسنجی عادی گواهی، Enrollment یک‌بارمصرف و اتمیک، انقضا/Replay/Revoke/رقابت، ذخیرهٔ Credential، lease/heartbeat، reconnect و Restart را روی دو کامپیوتر آزمایش کنیم.
- [ ] آزمون مقدماتی LAN قدیمی را شاهد تاریخی نگه داریم، نه تأیید سرویس واقعی نسخهٔ جاری.

### P1 — بستن Foundation و مستندات
- [x] تناقض Stage 1 و Gate A در Master Build Plan رفع شد؛ Setup/Updater/Repair/Rollback در Gate انتشار باقی می‌مانند. Outbox مرز ثبت/Claim است، نه تحویل end-to-end.
- [x] Outbox به‌صورت «ثبت/Claim/علامت‌گذاری Published» در برابر «تحویل واقعی» طبقه‌بندی شد. [ ] Worker، retry/backoff، final-failure/dead-letter، Idempotency مصرف‌کننده و تست رقابت/بازیابی پیش از هر slice وابسته به تحویل ناهمگام، به‌ویژه Session-to-Agent، تکمیل شود.
- [ ] برای هر برش تجاری، زنجیرهٔ REQ → تصمیم/Invariant → کد/API → تست → Run/SHA ثبت شود؛ شناسه و معیار کلی ماتریس به‌تنهایی ادعای Done نمی‌سازد.
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

## مرجع گیت پیش از انتقال مرحله

[Pre-Windows and Pre-Commercial Readiness Gate](pre-commercial-readiness-gate.md) مرجع واحد فهرست پایان کارهای مخزنی، موانع GitHub-side و پروتکل پذیرش فیزیکی است. Stage 1 hosted certification سبز است؛ پذیرش عملیاتی/Commercial به گیت فیزیکی وابسته است.

## تعریف بستن هر کار
یک کار فقط وقتی بسته است که diff محدود و بازبینی‌شده باشد، آزمون لازم روی همان SHA اجرا شود، مدرک Run قابل دسترسی باشد، نتیجهٔ ناموفق/اجرا‌نشده پنهان نشود، ریسک باقی‌مانده ثبت شود و مسیر بازگشت روشن باشد.

## نتیجهٔ تصمیم Runner
- انتخاب فعلی بر اساس آزمون: CI خودکار روی `windows-2025`، چون Quick و Full Foundation روی GitHub-hosted با موفقیت اجرا شدند؛ PostgreSQL/Backup-Restore/Desktop/Agent نیز در محیط موقت موفق شدند.
- Runner خودمیزبان حفظ می‌شود ولی از Workflowهای خودکار عمومی استفاده نمی‌شود. کنترل دسترسی خود Runner هنوز باید در سطح GitHub/زمان روشن بودن سرویس تعیین تکلیف شود؛ آن را Done امنیتی حساب نکنید.

## محدودیت‌های صریح
- این فایل برنامهٔ اجرا و ثبت شواهد است، نه اعلام آمادگی Production.
- تا قبل از پذیرش واقعی سرویس و LAN، Stage 1 نهایی اعلام نمی‌شود.
- `main` و `foundation/runtime-final-v2` دست‌نخورده می‌مانند.
