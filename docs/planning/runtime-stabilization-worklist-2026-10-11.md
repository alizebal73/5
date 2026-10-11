# GameNet 5 — برنامه تثبیت CI و بستن Foundation
تاریخ ثبت: 2026-10-11

## تصمیم اجرایی فعلی
- معماری محصول از صفر بازنویسی نمی‌شود؛ تغییرها کوچک، قابل بازبینی و مبتنی بر شاخهٔ مرجع خواهند بود.
- Runner خودمیزبان کاربر از سیستم حذف یا دوباره نصب نمی‌شود؛ این تغییرات آن را هدف نمی‌گیرند.
- تصمیم CI فعلی: هر چهار Workflow روی GitHub-hosted windows-2025 اجرا می‌شوند. Runner شخصی حذف، بازنصب یا دست‌کاری نشده و هیچ Workflow فعلی آن را انتخاب نمی‌کند.
- مرز امنیتی باز: guard داخل مخزن فقط دفاع در عمق است؛ دسترسی Runner/runner-group باید جداگانه در GitHub Settings محدود شود. تا تأیید، Runner محلی هنگام بررسی/اجرای PR غیرقابل‌اعتماد offline بماند.
- تغییر هیچ‌یک از `main` و `foundation/runtime-final-v2` مجاز نیست مگر در PR نگهداری جداگانه، با دلیل و گواهی مستقل.

## آخرین گواهی روی کاندید دقیق — 2026-10-11

روی SHA `53743eda950c057d435fc082994bc183e7cd723b` هر پنج مسیر موفق شدند:
- Quick push [#38135640632](https://github.com/alizebal73/5/actions/runs/38135640632)
- Quick PR [#38135643357](https://github.com/alizebal73/5/actions/runs/38135643357)
- Full Foundation [#38135640645](https://github.com/alizebal73/5/actions/runs/38135640645)
- Runtime Payload [#38135643366](https://github.com/alizebal73/5/actions/runs/38135643366)
- PostgreSQL Diagnostic [#38135640573](https://github.com/alizebal73/5/actions/runs/38135640573)

این گواهی مربوط به کاندید PR #31 است، نه نوک Integration یا Stable Foundation. PR هنوز Draft و ادغام‌نشده است.

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

### P0 — ایمن‌سازی CI
- [x] اثبات Quick Validation روی GitHub-hosted Windows و ثبت نتیجهٔ دقیق (#38130781617 و #38131165436).
- [x] اثبات Full Foundation روی GitHub-hosted Windows در harness آزمایشی؛ تمام گیت‌ها در #38131165423 سبز شدند.
- [x] انتقال در کاندید PR به GitHub-hosted Windows انجام و روی یک SHA هر سه مسیر Quick/Full/payload سبز شده است؛ ادغام این تغییر در Integration هنوز منتظر بازبینی PR است.
- [x] مسیر Full Foundation روی VM موقت GitHub-hosted با PostgreSQL آزمایشی اثبات شد؛ از اتصال DB یا اسرار سیستم Manager استفاده نشد.
- [x] Runner شخصی حفظ شده و هیچ‌یک از چهار Workflow فعلی آن را انتخاب نمی‌کند.
- [ ] دسترسی مدیریتی Runner/runner-group از GitHub Settings تأیید و محدود شود؛ guard در مخزن جایگزین این کنترل نیست. تا آن زمان Runner هنگام بررسی PR غیرقابل‌اعتماد offline بماند.
- [ ] PR #31 پس از CI سبز روی SHA دقیق `53743eda950c057d435fc082994bc183e7cd723b` بازبینی شود؛ Ruleset Integration و دسترسی Runner/runner-group باید در GitHub Settings اعمال و read-back شوند. ادغام خودکار ممنوع است.

### P1 — حاکمیت شاخه و وضعیت واقعی
- [x] به‌روزرسانی `engineering-readiness-register.md`، `branch-governance.md` و `stable-checkpoint.md` با SHA، PR و Runهای واقعی؛ حذف وضعیت‌های منسوخ و متناقض.
- [ ] برای integration/runtime-operator-v1 Ruleset واقعی PR + quick-validation + حل مکالمات بسازیم و read-back کنیم؛ فعلاً protected نیست و ابزار فعلی اجازهٔ نوشتن Ruleset ندارد.
- [ ] وضعیت PRهای باز و بسته را تعیین تکلیف کنیم؛ PRهای #27/#28/#29 شاخهٔ ادغام نیستند و نباید دوباره ادغام شوند. PR #30 قبلاً ادغام شده است.
- [x] برای هر گیت، SHA دقیقِ checkout و لینک Run را ثبت کنیم؛ مدرک SHA قدیمی به نوک جدید شاخه نسبت داده نشود.

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
