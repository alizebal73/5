# GameNet 5 — برنامه تثبیت CI و بستن Foundation
تاریخ ثبت: 2026-10-11

## تصمیم اجرایی فعلی (بازخوانی 2026-10-11)
- معماری محصول از صفر بازنویسی نمی‌شود؛ تغییرها کوچک و در PRهای جدا، با مدرک روی SHA دقیق انجام می‌شوند.
- Ruleset شاخهٔ `integration/runtime-operator-v1` ساخته و تأیید شده است: `protect-integration-runtime`، ID `24879997`، وضعیت Active و check اجباری `quick-validation`.
- Runner شخصی کاربر طبق تصمیم صریح او تا پایان ساخت GameNet نگه داشته می‌شود؛ در Settings با وضعیت `Idle` دیده شد. پس از تکمیل برنامه، پاک‌سازی آن پیگیری می‌شود. دسترسی Runner-group به‌عنوان محدودشده تأیید نشده؛ کاندیدهای PR #31 و #32 از `windows-2025` استفاده می‌کنند، اما این گزاره را به شاخه‌های قدیمی/باز تعمیم نده.
- هیچ تغییری مستقیم روی `main` یا `foundation/runtime-final-v2` انجام نمی‌شود؛ PR #31 و #32 باز و Draft می‌مانند تا بازبینی شوند.

## آخرین شواهد دقیق — 2026-10-11
- PR #31 head `7a8475bf862d0ecdf4cd70eb12361e0152dd4317`: Quick PR [#38138211044](https://github.com/alizebal73/5/actions/runs/38138211044)، Full Foundation [#38138208159](https://github.com/alizebal73/5/actions/runs/38138208159)، Runtime Payload [#38138211061](https://github.com/alizebal73/5/actions/runs/38138211061) موفق شدند.
- کاندید کد پیشین `d02192a43722efc1f49bf84fd24af911b3ece7e9` هر پنج گیت Quick push، Quick PR، Full Foundation، Runtime Payload و PostgreSQL diagnostic را با موفقیت گذراند؛ SHA و لینک‌ها در handoff gate ثبت شده‌اند. از اجرای آن پنج‌گانه به‌اشتباه ادعا نکن که هر run روی SHA `7a8475b...` بوده است.
- PR #32، head `4fb20dc09d1351f6dccdf6c6592a9b6b704e9cf9`: Full Foundation [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082) موفق شد؛ Foundation مرجع تغییر نکرده است.

این شواهد CI خودکارند؛ جایگزین آزمون واقعی سرویس ویندوز، ACL/DPAPI، کلید TLS، Bootstrap، Agent و LAN نمی‌شوند.

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
- [x] اثبات Quick Validation روی GitHub-hosted Windows و ثبت نتیجهٔ exact-SHA.
- [x] Full Foundation روی GitHub-hosted Windows با PostgreSQL دورریختنی، backup/restore، Desktop و Agent runtime موفق شده است.
- [x] PR #31 کاندید Workflowهای hosted را دارد؛ آخرین کاندید کد تأییدشده `7a8475bf862d0ecdf4cd70eb12361e0152dd4317`، Quick PR #38138211044، Full Foundation #38138208159 و Runtime Payload #38138211061 موفق‌اند. کاندید کد پیشین `d02192a...` هر پنج lane را گذرانده است.
- [x] Full Foundation روی کاندید نگهداری Foundation در PR #32 نیز موفق شد: [#38143687082](https://github.com/alizebal73/5/actions/runs/38143687082).
- [x] Ruleset شاخهٔ `integration/runtime-operator-v1` ساخته و read-back شد؛ ID `24879997`، check اجباری `quick-validation`، PR، conversation resolution، جلوگیری از deletion/force-push، Squash-only و bypass خالی.
- [~] Runner شخصی بنا به تصمیم مالک تا پایان GameNet حفظ می‌شود و سپس حذف خواهد شد. Runner-group restriction تأیید نشده و پاک‌سازی در Issue #33 پیگیری می‌شود؛ این مورد را «حل‌شدهٔ امنیتی» تلقی نکن.
- [ ] PRهای #31 و #32 پس از بازبینی کامل، با ترتیب و Base صحیح تعیین تکلیف شوند؛ ادغام خودکار ممنوع است.

### P1 — حاکمیت شاخه و وضعیت واقعی
- [x] وضعیت جاری `engineering-readiness-register.md`، `branch-governance.md` و `stable-checkpoint.md` با GitHub read-back هماهنگ شد؛ تاریخچهٔ قدیمی به‌عنوان سابقه حفظ می‌شود.
- [x] Ruleset واقعی برای Integration فعال و تأیید شد؛ شاخهٔ Integration هنوز روی همان SHA قبلی است و ساخت Ruleset خودش commit/merge ایجاد نکرده است.
- [x] وضعیت PRهای تاریخی #27/#28/#29 به‌عنوان بسته و غیرقابل‌ادغام، PR #25 به‌عنوان ادغام‌شده، و PR #26 به‌عنوان Draftِ superseded مستند شد.
- [ ] پیش از انتخاب PR #31 به‌عنوان Integration base، آخرین CI روی نوک نهایی آن سبز باشد و diff کامل بررسی شود.
- [x] برای هر گیت، SHA و Run جدا شده‌اند؛ evidence SHA کد قبلی به اجرای SHA مستندات جدید نسبت داده نمی‌شود.

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
