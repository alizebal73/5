# GameNet 5 — نقشه‌راه مرجع و وضعیت مرحله‌ای

> این سند مرجع پایدار مسیر ساخت محصول است. پس از پایان هر مرحله، وضعیت و شواهد همان مرحله را به‌روزرسانی کنید. وجود کد، پوشه، تست یا سند به‌تنهایی به معنی تکمیل قابلیت نیست.

## مرجع پروژه و وضعیت فعلی

- مخزن فعال: `alizebal73/5`.
- پایهٔ فنی مرجع/نامزد: `foundation/runtime-final-v2`. گواهی Foundation فقط برای SHA دقیق اجراشده معتبر است؛ تغییرات بعدی تا اجرای دوبارهٔ دروازه‌های لازم، خودبه‌خود تأیید نیستند.
- شاخهٔ مرجع انتشار: `foundation/runtime-final-v2` (نامزد پایه، نه مقصد ادغام مستقیم featureها). شاخهٔ فعال Runtime/operator یعنی `integration/runtime-operator-v1` در snapshot بلافاصله پس از PR #42 روی `d2d8c626c678078c52b840fd0c9c751e7583fa91` دیده شد؛ این SHA تاریخی است، نه اشاره‌گر زنده. SHA کد ادغام‌شدهٔ PR #40 برابر `58717adc6c1553ef21221c3d484f093494ba1ae7` است و PRهای #41/#42 صرفاً اسناد و کنترل اجرا را تغییر دادند. PR #23 جهت ادغام معکوس به شاخهٔ قدیمی `feature/operator-identity-v1` دارد و مسیر فعال تحویل نیست. PR #24 یک برش مستقل UI و هنوز Draft است. `main` دست‌نخورده می‌ماند. مرجع تصمیم‌های اجرایی و PRهای باز: [کنترل اجرای فعال](../operations/active-execution-control.md).
- دفتر نقص‌ها: `docs/development/bug-fix-log.md`. هر نقص باید علت ریشه‌ای، مالک لایه، اصلاح، آزمون بازگشتی، SHA و نتیجه داشته باشد.
- اصل کار: هر بار یک نقص یا برش قابلیت کوچک؛ علت‌یابی قبل از اصلاح؛ آزمون‌های خودکار و regression روی همان تغییر؛ ثبت SHA و مدرک؛ سپس مرحلهٔ بعد. آزمون فیزیکی LAN/Service، تعامل دستی UI و پذیرش نصب نهایی فقط در مرحلهٔ آخر، بعد از تکمیل قابلیت‌ها و ساخت Setup.exe/MSI انجام می‌شود.
- کیفیت پایه: Server مرجع قواعد تجاری است؛ PostgreSQL مرجع پایدار داده است؛ Desktop و Agent مستقیماً به دیتابیس وصل نمی‌شوند؛ پول/موجودی مبتنی بر ledger هستند؛ تغییرات حساس تراکنشی، قابل حسابرسی و در برابر تکرار/رقابت ایمن‌اند.

## آخرین snapshot وضعیت اجرایی — پس از ادغام PR #42 (2026-10-12)

این بخش یک snapshot تاریخی از بازخوانی GitHub بلافاصله پس از PR #42 است، نه اشاره‌گر زندهٔ branch. چون هر PR مستنداتی SHA شاخهٔ Integration را جلو می‌برد، برای هر اقدام باید SHA زنده را مستقیماً از GitHub خواند. معیارهای اجرایی و جلوگیری از تکرار در [کنترل اجرای فعال](../operations/active-execution-control.md) تعریف شده‌اند.

- `main` در بازخوانی آن زمان روی `b9288471f2047570eaf8d0d6552cf87bc0ddc214` و `foundation/runtime-final-v2` روی `be29687e637b709158a30204bb9213bfa4813950` بود؛ هر دو محافظت‌شده بودند.
- پس از ادغام PR #42، SHA مشاهده‌شدهٔ `integration/runtime-operator-v1` برابر `d2d8c626c678078c52b840fd0c9c751e7583fa91` بود. این PR فقط کنترل اجرا و مستندات را تغییر داد؛ آن SHA را به‌عنوان اشاره‌گر زنده پس از ادغام‌های بعدی استفاده نکنید.
- کد Runtime در PR #40 در SHA `58717adc6c1553ef21221c3d484f093494ba1ae7` ادغام شد. روی candidate `654bc6d49dc4fe2935c59cc5039e881e0b7a9edd`، [Quick #38180651116](https://github.com/alizebal73/5/actions/runs/38180651116)، [Full Foundation #38180646187](https://github.com/alizebal73/5/actions/runs/38180646187) و [Runtime payload #38180651104](https://github.com/alizebal73/5/actions/runs/38180651104) موفق شدند؛ روی commit ادغام کد نیز [Quick #38181118466](https://github.com/alizebal73/5/actions/runs/38181118466) و [Full Foundation #38181118446](https://github.com/alizebal73/5/actions/runs/38181118446) موفق بودند.
- PR #41 فقط اسناد را تغییر داد؛ candidate آن [Quick #38181520088](https://github.com/alizebal73/5/actions/runs/38181520088) و SHA مشاهده‌شده در آن زمان `362513ad812aebef4ad22420f9b905d79490219d` [Quick #38181599202](https://github.com/alizebal73/5/actions/runs/38181599202) را گذراند. این مدرک مربوط به همان SHA است؛ Full Foundation روی commit کد `58717adc...` اجرا شده، نه ادعای اجرای Full روی همهٔ commitهای مستنداتی بعدی.
- **گیت یکپارچه‌سازی کد بسته است.** گواهی Foundation طبق Stage 1 برنامهٔ اصلی روی SHA `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` با [Full Foundation #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053) **موفق شد**.
- سپس SHA ادغام‌شدهٔ Integration برابر `161b4cbbbe16cd3fd4046f174a1c09380139c67b`، [Quick #38183458461](https://github.com/alizebal73/5/actions/runs/38183458461) و [Full Foundation #38183458486](https://github.com/alizebal73/5/actions/runs/38183458486) را نیز گذراند. بنابراین Stage 1 برنامهٔ اصلی (گواهی Foundation) و گیت یکپارچه‌سازی کد بسته‌اند؛ این‌ها دو نام برای یک معیار متغیر نیستند.
- اسناد کنترل اجرا نیز در PR #45 با [Quick Validation #38183922997](https://github.com/alizebal73/5/actions/runs/38183922997) روی کاندید سندی `d7b25f88c932b09c3cbdebb82c21100b48a53916` تأیید و سپس به SHA `05560d1db09855ddf07ae4cad35d6d5e3ee075b3` ادغام شدند.
- اجرای Full Foundation روی pushهای `foundation/runtime-*` خودکار است؛ CI و آزمون‌های خودکار مرتبط در تمام طول توسعه ادامه دارند. پذیرش سرویس Windows واقعی، ACL/DPAPI/TLS پس از restart، اتصال دو کامپیوتر LAN، تعامل دستی WPF و آزمون نصب/به‌روزرسانی/rollback همگی به مرحلهٔ نهایی موکول‌اند: پس از تکمیل قابلیت‌های برنامه‌ریزی‌شده و ساخت Setup.exe/MSI. این گیت‌ها Stage 1 را باز نمی‌کنند.
- پذیرش فیزیکی Runtime برای مرحلهٔ آخر نگه داشته می‌شود و قبل از تکمیل محصول اجرا نمی‌شود. بعد از ساخته‌شدن Setup.exe/MSI، ابتدا گزارش فقط‌خواندنی `scripts/inspect-manager-runtime-state.ps1` بررسی شود؛ سپس روی PCهای آزمایشی ایزوله و دیتابیس دورریختنی، Service identity، ACL، DPAPI/TLS پس از restart، enrollment replay/revoke، lease/heartbeat و reconnect LAN آزموده شوند. تغییر Manager واقعی بدون preflight و تأیید صریح ممنوع است.
- آزمون تعاملی WPF برای RTL/LTR، focus/keyboard/navigation، وضعیت‌های permission/offline/loading/error و آزمون‌های نصب، Update/Rollback، امضای انتشار و pilot در همان مرحلهٔ نهایی اجرا می‌شوند؛ پیش‌نیازشان تکمیل دامنهٔ محصول و ساخت Setup.exe/MSI است. تست‌های خودکار UI/resource/binding و CI در طول توسعه برقرار می‌مانند. این کارها مرحلهٔ اول را دوباره باز نمی‌کنند.

### رویدادهای تأییدشده پس از snapshot قبلی — 2026-10-12

- [PR #44](https://github.com/alizebal73/5/pull/44) استاندارد CI شاخهٔ Integration را روی Runner میزبانی‌شدهٔ `windows-2025` تثبیت کرد. کاندید `92de1b1fb6002165fc91c7f66b4ecec1c969c0ff` روی [Quick push](https://github.com/alizebal73/5/actions/runs/38183164928)، [Quick PR](https://github.com/alizebal73/5/actions/runs/38183167294)، [Full Foundation](https://github.com/alizebal73/5/actions/runs/38183164941) و [Runtime Payload](https://github.com/alizebal73/5/actions/runs/38183167300) موفق شد.
- SHA ادغام‌شدهٔ Integration برابر `161b4cbbbe16cd3fd4046f174a1c09380139c67b` است؛ [Quick Validation پس از ادغام](https://github.com/alizebal73/5/actions/runs/38183458461) و [Full Foundation پس از ادغام](https://github.com/alizebal73/5/actions/runs/38183458486) نیز روی همان SHA موفق شدند.
- شکست ساخت Migration Bundle با تنظیم اتصال طراحیِ غیرقابل‌استفاده و موقتی، بازگرداندن متغیرهای محیطی و الزام بررسی `--connection` رفع شد؛ هیچ اعتبارنامهٔ واقعی دیتابیس وارد ZIP نمی‌شود. ZIP همچنان payload آزمایشی است، نه Setup/MSI.
- PR #31 به دلیل هم‌پوشانی کارهای Runner با #44 بدون ادغام بسته شد؛ شاخهٔ آن برای استخراج جداگانهٔ ابزار ثبت سرویس آزمایشی و runbook حفظ شده است.
- این شواهد، پذیرش فیزیکی Windows Service/ACL/DPAPI/TLS/LAN یا آزمون تعاملی WPF را اثبات نمی‌کنند؛ آن‌ها گیت‌های مستقل هستند و مرحلهٔ یکپارچه‌سازی کد را دوباره باز نمی‌کنند.


### نقطهٔ آغاز ثبت‌شده

> **توضیح مهم — آرشیو تاریخی، نه صف اجرایی زنده (به‌روزرسانی 2026-10-12):** فهرست مراحل ۰–۲۳ و گزارش‌های انباشتهٔ زیر از چرخه‌های قبلی توسعه ثبت شده‌اند. هر خطی که می‌گوید «ابتدا نصب/LAN واقعی، بعد ماژول مشتریان یا فرمان PC» یا «مرحلهٔ بعد شمارهٔ ۶/۸» تصمیم اجرایی قدیمی است و دیگر ترتیب جاری نیست. مرجع زنده فقط [Master Build Plan](../planning/master-build-plan.md) و [Active Execution Control](../operations/active-execution-control.md) هستند. مرحلهٔ ۱ برنامهٔ اصلی، یعنی گواهی Foundation، بسته است. توسعهٔ ماژول‌های محصول طبق برنامهٔ اصلی اکنون ادامه دارد؛ CI و آزمون‌های خودکار در تمام طول توسعه الزامی‌اند. آزمایش نصب/سرویس ویندوز/دو-PC LAN و پذیرش دستی WPF فقط پس از تکمیل دامنهٔ برنامه‌ریزی‌شده و ساخت Setup.exe/MSI اجرا می‌شوند. شواهد قدیمی پایین‌تر تاریخچه‌اند و نباید اولویت فعلی را عوض کنند.

- مرحلهٔ ۰: تکمیل‌شده برای شکست اخیر. لاگ GitHub Actions، اجرای Foundation شمارهٔ ۱۵۹، نشان داد Build در `Canonical verification` به‌علت تگ XML نادرست در دو فایل منابع WPF متوقف شده است.
- ریشهٔ خطا: تگ `Station.Maintenance` در `Strings.en-US.xaml` و `Strings.fa-IR.xaml` با `</system>` بسته شده بود، در حالی که تگ آغازین `system:String` است.
- مرحلهٔ ۱: **تکمیل شد**. Commit `853f23427783a93638a0212cb1dc5e97386e0e8f` اصلاح تگ‌ها را ثبت کرد؛ اجرای کامل [Foundation Certification #160](https://github.com/alizebal73/5/actions/runs/37917339204) روی همان SHA موفق شد. دروازه‌های PowerShell parser، Canonical Build/Test، PostgreSQL، Backup/Restore، Desktop Runtime، Agent Runtime و Upload Evidence همگی سبز شدند.

## فهرست مرحله‌های تاریخی (جایگزین شده توسط Master Build Plan فعلی)

| شماره | مرحله | معیار عبور |
|---:|---|---|
| ۰ | عیب‌یابی وضعیت فعلی | لاگ واقعی بررسی و علت شکست مشخص شود؛ پیش از شواهد، اصلاح حدسی انجام نشود. |
| ۱ | رفع مانع فعلی و تأیید مجدد | اصلاح حداقلی؛ Build/Test و دروازه‌های لازم روی همان SHA موفق باشند. |
| ۲ | تثبیت پایه و قواعد شاخه‌ها | SHA پایه، ancestry، سیاست شاخه‌ها و مسیر ادغام روشن باشد؛ شاخهٔ قدیمی به‌عنوان پایهٔ جدید پذیرفته نشود. |
| ۳ | یکسان‌سازی CI و دروازه‌ها | کنترل‌های سریع از گواهی کامل جدا شوند؛ اجرای تکراری بی‌دلیل کم شود، بدون حذف آزمون لازم. تناقض شروط پایه/انتشار رفع شود. |
| ۴ | تجمیع نیازمندی‌ها و درس‌های پروژه‌های ۲ و ۳ | یک ماتریس قابلیت‌ها، وابستگی‌ها، مالک داده، اولویت، ریسک و معیار پذیرش داشته باشیم؛ کد قدیمی کورکورانه منتقل نشود. |
| ۵ | تثبیت دفتر باگ و Definition of Ready/Done | هر نقص و قابلیت با علت/مالک/آزمون/مدرک قابل پیگیری باشد. |
| ۶ | هویت اپراتور و ورود امن | Bootstrap مدیر، ورود/خروج، نشست، نقش/مجوز و آزمون واقعی API؛ هیچ اعتبار پیش‌فرض پنهانی وجود نداشته باشد. |
| ۷ | پوستهٔ Desktop و ایستگاه‌ها | رابط WPF فارسی/RTL و اتصال واقعی به Server؛ فهرست و عملیات مجاز PC/PS5/فوتبال‌دستی با دادهٔ پایدار. |
| ۸ | Agent و سلامت دستگاه | هویت یکتا، pairing، heartbeat، lease/fencing، فرمان مجاز، reconnect و رد فرمان قدیمی. |
| ۹ | مشتریان | شناسه، PIN در صورت نیاز، پروفایل، جست‌وجو، تاریخچه و جداسازی دسترسی مشتریان. |
| ۱۰ | تعرفه و قیمت‌گذاری | تعرفهٔ جداگانهٔ PC/PS5/فوتبال‌دستی، قیمت دستی مجاز، قواعد تخفیف و snapshot قیمت‌گذاری سمت سرور. |
| ۱۱ | چرخهٔ Session | شروع/توقف/ادامه/پایان/آزادسازی/انتقال و بازیابی؛ بدون جلسهٔ یتیم یا هزینهٔ تکراری. |
| ۱۲ | صورتحساب و تسویه | محاسبهٔ هزینه، روش‌های پرداخت مجاز، بدهی و اصلاح/برگشت مستند؛ آزمون هم‌زمانی و تکرار. |
| ۱۳ | کیف پول و اعتبار هدیه | ledger شارژ/برداشت/بدهی/پرداخت؛ ۱۰۰٬۰۰۰ تومان + هدیهٔ ۱۰٪ = اعتبار پولی ۱۰۰٬۰۰۰، هدیه ۱۰٬۰۰۰، قابل استفاده ۱۱۰٬۰۰۰. |
| ۱۴ | انتقال، چنددستگاهی و حساب بازی | مالکیت Session/Station/Agent/Login/Lease سازگار و اتمیک؛ تخصیص هم‌زمان حساب/دستگاه تکراری نباشد. |
| ۱۵ | انبار و گردش موجودی | ورود، شمارش، اصلاح، ضایعات، برگشت و گردش کالا با کنترل و حسابرسی. |
| ۱۶ | بوفه و فروش | سبد فروش، اتصال به مشتری/جلسه، فروش و برگشت هماهنگ با ledger صحیح موجودی و پرداخت. |
| ۱۷ | VIP | طرح، مدت، فعال/منقضی‌شدن و entitlement؛ یکپارچه با تعرفه و Session. |
| ۱۸ | گزارش، شیفت و Audit | گزارش‌های درآمد/جلسه/کیف پول/بوفه/انبار/اپراتور و صندوق از دادهٔ مرجع و قابل تطبیق. |
| ۱۹ | تنظیمات، مجوزها و تأییدها | مالکیت تنظیمات مشخص، مجوز سمت Server، تأیید عملیات حساس و ثبت علت/عامل. |
| ۲۰ | Backup، بازیابی و Diagnostics | تهیه و بازیابی ایزولهٔ واقعی، restart/reconnect و گزارش خطا/تطبیق؛ فایل پشتیبان به‌تنهایی مدرک نیست. |
| ۲۱ | نصب‌کننده و به‌روزرسانی | نصب/تعمیر/ارتقا/Rollback واقعی؛ مدیریت سرویس، اسرار، نسخه و سازگاری schema. |
| ۲۲ | آزمون شبکه و دستگاه‌های واقعی | حداقل ۲–۳ PC واقعی، سپس مقیاس ۱۵ ایستگاه؛ قطع شبکه/راه‌اندازی مجدد/رقابت/پول/موجودی. |
| ۲۳ | گواهی انتشار عملیاتی | نسخه و SHA مشخص، نصب و بازیابی موفق، آزمون امنیت و انتشار، بسته‌ها و شواهد منطبق؛ تنها اینجا ادعای آمادهٔ استفاده شود. |

## روش اجباری برای هر مرحله

1. پیش از تغییر، وضعیت موجود را روی SHA دقیق بررسی کن.
2. نیاز واقعی، مالک ماژول، invariant، مجوز، مرز تراکنش، retry/idempotency، شکست و بازیابی را مشخص کن.
3. فقط تغییر محدود همان مرحله را انجام بده؛ مشکل یک لایه را با دورزدن کنترل لایهٔ دیگر نپوشان.
4. آزمون کوچک مرتبط را زود اجرا کن؛ سپس آزمون canonical و هر آزمون یکپارچه/Runtime متناسب را روی همان SHA اجرا کن.
5. نتیجهٔ موفق یا ناموفق، شناسهٔ اجرای CI، SHA، علت و ریسک باقی‌مانده را در دفتر باگ/این سند ثبت کن.
6. شاخه‌ها را کورکورانه ادغام نکن. نشانگر `certified` بدون بررسی ancestry و شواهد برای عبور کافی نیست.
7. از `main` و پایگاه دادهٔ واقعی فروشگاه در آزمون‌های مخرب یا آزمایشی استفاده نکن.
8. قابلیت وقتی کامل است که رابط کاربری، Server، PostgreSQL/Agent در صورت نیاز، خطاها و آزمون‌های پذیرش با هم کار کنند؛ نه صرفاً یک صفحه یا یک تست واحد.

## کنترل مرحلهٔ جاری — snapshot تاریخی تا چرخهٔ قبلی، نه وضعیت زنده

- مرحله‌های ۰ و ۱: تکمیل‌شده؛ اصلاح XML در [Foundation #160](https://github.com/alizebal73/5/actions/runs/37917339204) روی SHA دقیق موفق شد.
- مرحلهٔ ۲: تکمیل‌شده؛ آزمون ancestry و گواهی کامل در [#161](https://github.com/alizebal73/5/actions/runs/37918110954) موفق شدند و Ruleهای [protect-main](https://github.com/alizebal73/5/rules/24783754) و [protect-foundation-runtime](https://github.com/alizebal73/5/rules/24784035) از GitHub دوباره خوانده شدند. هر دو فعال‌اند و GitHub شاخه‌های `main` و `foundation/runtime-final-v2` را محافظت‌شده گزارش می‌کند؛ [Issue #9](https://github.com/alizebal73/5/issues/9) بسته شده است.
- مرحلهٔ ۳: **تکمیل شد** — تفکیک CI و ثبت Checkهای اجباری برای هر دو شاخهٔ محافظت‌شده.
- مرحلهٔ ۴: **تکمیل شد** — نیازمندی‌های دو مخزن قدیمی با مرز معماری Repo 5 تطبیق داده و به معیارهای قابل‌آزمون تبدیل شدند.
- مرحلهٔ ۵: **تکمیل شد** — فرایند باگ، قالب‌های کار، Definition of Ready/Done و دروازهٔ خودکار مستندات ثبت شدند.
- مرحلهٔ ۶: **گواهی API/امنیت هویت اپراتور تکمیل شد**؛ UI مدیریتی کامل اپراتورها/نقش‌ها هنوز باز است.
- مرحلهٔ ۷: **جست‌وجو/فیلترهای صفحهٔ ایستگاه‌ها گواهی شدند**؛ Quick Validation #96 و Full Foundation #171 روی SHA `cba105cd4bea69e76691fbbf8cd8fed30d69d332` هر دو موفق‌اند: [quick #96](https://github.com/alizebal73/5/actions/runs/37935419095)، [full #171](https://github.com/alizebal73/5/actions/runs/37935419140). Draft validation PR [#14](https://github.com/alizebal73/5/pull/14) بدون ادغام بسته شد.
- مرحلهٔ ۸-A: **Agent identity/pairing، heartbeat، lease/fencing، reconnect/recovery و نمایش سلامت PC** روی SHA `bd635748b5189153ba7a9d5873833220923a9ce4` گواهی شد: [Quick #103](https://github.com/alizebal73/5/actions/runs/37938397742)، [Full Foundation #178](https://github.com/alizebal73/5/actions/runs/37938397675). گواهی جدید #191 نیز همین مسیر را روی آخرین SHA بازآزمایی کرد. PS5 و فوتبال‌دستی timer-only هستند و Agent ویندوزی ندارند.
- مرحلهٔ ۸ به‌طور کلی **هنوز کامل نیست**: فقط مسیر تشخیصی بی‌خطر Health Probe وجود دارد؛ اجرای Launch/Stop/Lock/Unlock/Message و Restart/Shutdown/network profile، مجوز/Policy جداگانهٔ هر قابلیت و تست فیزیکی روی ۲–۳ PC باز است. رد فرمان منقضی/lease نامعتبر و dedup در آزمون‌های واحد پوشش دارد؛ آزمون رقابت stale-command به‌صورت فیزیکی/End-to-End هنوز شرط باز است. **مدیریت مشتریان را فعلاً شروع نکن**.
- یادداشت تاریخی 2026-10-09 (جایگزین‌شده): در آن مقطع نصب‌پذیری پیش از مشتریان انتخاب شده بود. اکنون ترتیب زنده تغییر کرده است: تکمیل قابلیت‌های محصول طبق Master Build Plan بدون انتظار برای پذیرش فیزیکی؛ سپس Setup.exe/MSI؛ سپس آزمایش نهایی نصب، Windows Service، LAN و UI.
- یادداشت تاریخی 2026-10-09: موارد URL/ProgramData، اعتماد TLS و enrollment از موضوعات کار Runtime بودند و بخشی از کد آن‌ها بعداً در شاخهٔ فعال ادغام شده است؛ وضعیت دقیق روز را در Capability Matrix و Requirements Traceability بخوانید. آزمون واقعی HTTPS/Login/Agent enrollment در LAN به پذیرش نهایی موکول شده است. HTTP bypass یا نادیده‌گرفتن اعتبار گواهی همچنان ممنوع است.
- **بخش HTTPS سمت Server/بسته انجام و روی CI تأیید شد:** commit `7ab5108b342c74d88faf87ba6303a1c08ac9de9b`، [Quick #128](https://github.com/alizebal73/5/actions/runs/37961799437)، [Full Foundation #196](https://github.com/alizebal73/5/actions/runs/37961799463)، artifact [#11632326010](https://api.github.com/repos/alizebal73/5/actions/artifacts/11632326010). Kestrel endpoint، DPAPI certificate settings، تست ساخت/صادرات/بازخوانی PFX و manifest/ZIPها با موفقیت گذشتند. این هنوز اثبات اتصال واقعی LAN یا نصب روی دو سیستم نیست.
- **زیرگام اخیر تأیید شد:** Agent اکنون HTTP را فقط برای loopback توسعه‌ای قبول می‌کند؛ Remote HTTP پیش از اتصال رد می‌شود. Regression tests و [Quick Validation #127](https://github.com/alizebal73/5/actions/runs/37959248404) + [Foundation #195](https://github.com/alizebal73/5/actions/runs/37959248381) روی SHA `b95eab6c68e89b4d8f4500f714f1445ffd9ca4c4` موفق‌اند. این اصلاح فقط parity پالیسی را می‌بندد؛ خود TLS/Kestrel و نصب فیزیکی هنوز باز هستند.
- **زیرگام هماهنگ‌سازی URL نیز تأیید شد:** `ServerEndpointAddress` در `Shared` مالک واحد قانون URL است و Desktop و Agent هر دو از آن استفاده می‌کنند. URL باید origin ریشه باشد؛ HTTPS برای LAN و HTTP فقط برای loopback توسعه‌ای مجاز است. مسیر اضافی، query، fragment و credentials در URL رد می‌شوند. تست مشترک + consumer tests روی SHA `553dc80f6f553b0e793b7cbb2b82ba9136b116c0` در [Quick Validation](https://github.com/alizebal73/5/actions/runs/37963001084) و [Foundation Certification](https://github.com/alizebal73/5/actions/runs/37963001068) هر دو موفق‌اند. هنوز trust/runtime config و ارتباط واقعی LAN تأیید نشده‌اند.
- این برنامهٔ نصب نهایی همچنان معتبر است، اما زمان‌بندی آن تغییر کرده: پس از تکمیل قابلیت‌های برنامه‌ریزی‌شده و ساخته‌شدن Setup.exe/MSI اجرا شود. ZIPهای Runtime فعلی Setup.exe/MSI نیستند و نصب واقعی را ثابت نمی‌کنند.
- **دستور قدیمی منسوخ‌شده (2026-10-09):** مسدودکردن ماژول مشتریان تا آزمون LAN دیگر برقرار نیست. اکنون برش‌های محصول طبق Master Build Plan مجازند، مشروط به CI خودکار سبز، رهگیری نیازمندی و review؛ پذیرش فیزیکی در مرحلهٔ آخر می‌آید.
- زیرمرحلهٔ ۶-A: محدودسازی ورودی‌های مسیر Login تأیید شد. ورودی username/password بیش از حدود پشتیبانی‌شده پیش از query/hash رد می‌شود؛ آزمون regression در PasswordHasher و harness واقعی Identity اضافه است. [quick-validation #78](https://github.com/alizebal73/5/actions/runs/37926450614) و [Full Foundation #166](https://github.com/alizebal73/5/actions/runs/37926586429) روی SHA دقیق 7909acba2c922f102e18693e3842743f7ad16afc موفق شدند.
- زیرمرحلهٔ ۶-B: **تأیید Runtime شد** روی کد SHA `bf0c355837c15f1cf9dedf324779fa1f09f4b180`. API مدیریت کاربر/نقش، سقف مجوز مدیر، محافظت Owner/system role، ابطال نشست پس از تغییر نقش/غیرفعال‌سازی و idempotency با PostgreSQL واقعی و ایزوله آزمایش شدند. [Foundation Certification run #168](https://github.com/alizebal73/5/actions/runs/37930045661) تمام دروازه‌ها را با موفقیت گذراند؛ artifact شواهد نیز بارگذاری شد.
- دروازهٔ سریع روی SHA `2e6013c98501f2fbac106cba866074598e3f7851` موفق شد: [quick-validation run 89](https://github.com/alizebal73/5/actions/runs/37929829011) passed before docs-only follow-ups.
- زیرمرحلهٔ ۶-C: تغییر گذرواژهٔ خود اپراتور و بازنشانی توسط مدیر مجاز پیاده‌سازی شد. مسیر خودخدمتی گذرواژهٔ فعلی را بررسی می‌کند، گذرواژهٔ بدون تغییر را رد می‌کند، نشست فعلی را حفظ و نشست‌های دیگر را باطل می‌کند. بازنشانی مدیریتی مجوز مستقل `identity.users.password-reset`، دلیل اجباری، حفاظت از Owner و ابطال همهٔ نشست‌های هدف دارد. هیچ گذرواژه یا hash آن در Audit/اثر انگشت idempotency ذخیره نمی‌شود.
- اثرانگشت درخواست‌های Identity که payload حساس دارند از SHA-256 بدون کلید به HMAC-SHA256 با کلید مشتق‌شدهٔ اختصاصی تغییر کرد؛ آزمون واحد، پایداری replay، تفاوت payload، چرخش کلید و رد پیکربندی ضعیف را بررسی می‌کند.
- آزمون PostgreSQL ایزوله مسیر گذرواژه را پوشش می‌دهد: گذرواژهٔ فعلی اشتباه، رد گذرواژهٔ یکسان، تغییر موفق، رد گذرواژهٔ قدیمی، ورود با گذرواژهٔ جدید، تکرار idempotent، رد استفادهٔ دوباره از کلید با payload متفاوت، مجوز مستقل Reset، محافظت Owner، ابطال نشست‌ها و Reset مدیریتی.
- گواهی کامل [Foundation #170](https://github.com/alizebal73/5/actions/runs/37933516853) و گیت سریع [#94](https://github.com/alizebal73/5/actions/runs/37933516714) هر دو روی SHA دقیق `c0406a4e6d88fc20eb5f7100e7d38c9448e584de` موفق شدند. Artifact شواهد: [foundation-certification-evidence](https://api.github.com/repos/alizebal73/5/actions/artifacts/11617233775/zip).
- **مرز تکمیل:** مرحلهٔ ۶ برای هویت، API/امنیت و آزمون Runtime تکمیل شد. صفحه‌های گرافیکی مدیریت اپراتور/نقش‌ها و اجرای بازنشانی مدیریتی از داخل Desktop همچنان بخشی از کار UI/Settings هستند؛ این‌ها را از روی موفقیت endpointها کامل فرض نکن. هر تغییر بعدی در کد نیازمند گواهی SHA جدید خودش است.
- مشکل قبلی: push به شاخهٔ کاری و شاخه‌های Foundation گواهی کامل را تکرار می‌کرد؛ هم‌زمان `gamenet.yml` با Pull Request، Canonical Build/Test را جداگانه اجرا می‌کرد و شاخه‌های Foundation به علت شرط `if` از این lane کنار گذاشته می‌شدند. `foundation-runtime.yml` نیز lane محدود PostgreSQL داشت که از نامش می‌توانست با گواهی کامل اشتباه شود.
- طرح مرحلهٔ ۳:
  - `gamenet.yml` یک lane سریع است: push به `feature/operator-identity-v1` (به‌جز pushهای Markdown-only) و PR به `main`؛ parser و Canonical build/test را اجرا می‌کند، ولی PostgreSQL/Backup/Desktop/Agent runtime certification را تکرار نمی‌کند.
  - `foundation-certification.yml` گواهی کامل را با همهٔ دروازه‌های فعلی روی PRهای مقصد `foundation/runtime-*`، به‌صورت دستی، و روی تغییر خود فایل تعریف workflow اجرا می‌کند. push عادی کد به شاخهٔ feature دیگر گواهی کامل را تکرار نمی‌کند.
  - PRها عمداً با فیلتر مسیر حذف نمی‌شوند تا در صورت اجباری‌شدن status check، PR صرفاً مستنداتی با check مفقود/در حالت Pending قفل نشود. فیلتر Markdown-only فقط روی push lane سریع اعمال می‌شود.
  - `foundation-runtime.yml` به‌عنوان `foundation-postgresql-diagnostic` صریحاً فقط lane تشخیصی PostgreSQL است، نه جایگزین گواهی کامل.
  - فیلتر مسیر هرگز کل `docs/**` را حذف نمی‌کند؛ فایل‌های غیر-Markdown داخل docs، از جمله marker عملیاتی JSON، نباید از بررسی خودکار جا بمانند.
- نتیجهٔ تفکیک CI روی SHA `aaea71212111aec24f688ae791aabb2543b5633b` تأیید شد: [quick #73](https://github.com/alizebal73/5/actions/runs/37921715915) و [full #163](https://github.com/alizebal73/5/actions/runs/37921715979) هر دو سبز شدند.
- Check اجباری Foundation نیز از روی PR واقعی تأیید شد: [Draft PR #10](https://github.com/alizebal73/5/pull/10) اجرای [#164](https://github.com/alizebal73/5/actions/runs/37922148983) را با Check دقیق `foundation` با موفقیت گذراند؛ سپس Ruleset `protect-foundation-runtime` به‌صورت read-back بررسی شد و `required_status_checks` شامل `{ context: foundation, integration: GitHub Actions }` بود. PR #10 بدون ادغام بسته شد چون فقط برای اعتبارسنجی CI ساخته شده بود.
- مرحلهٔ ۳ با read-back نهایی هر دو Ruleset بسته شد: `protect-main` (ID `24783754`) اکنون `quick-validation` را الزامی می‌کند و `protect-foundation-runtime` (ID `24784035`)، Check به نام `foundation` را الزامی می‌کند.
- شواهد PR واقعی: [quick-validation #74](https://github.com/alizebal73/5/actions/runs/37923320241) روی SHA `685301a6973666b0dca0aee27739f5afccdc9124` موفق شد؛ Check دقیق `quick-validation` از GitHub Actions در `protect-main` ذخیره و سپس از API دوباره خوانده شد. Foundation نیز پیش‌تر در [#164](https://github.com/alizebal73/5/actions/runs/37922148983) موفق بود و Check `foundation` در Ruleset مربوطه read-back شده است.
- [Draft PR #11](https://github.com/alizebal73/5/pull/11) و [Draft PR #10](https://github.com/alizebal73/5/pull/10) فقط برای تأیید CI/Ruleset ساخته و هر دو بدون ادغام بسته شدند. هیچ تغییری از آن‌ها وارد `main` یا Foundation نشد.
- مرحلهٔ ۴ تکمیل شد: [ماتریس مرجع نیازمندی‌ها](../planning/requirements-traceability.md) اکنون ۵۶ ردیف نیازمندی با شناسه، معیار پذیرش، مالک/وابستگی، اولویت/ریسک، وضعیت فعلی Repo 5 و خطاهای قدیمیِ تبدیل‌شده به آزمون بازگشتی دارد. [Capability Matrix](../planning/capability-matrix.md) نیز به‌عنوان نمای خلاصه به‌روزرسانی شد.
- منابع مرجع بررسی‌شده: Repo 2 در SHA `d8031070413338c50fd67155a8074c45c91b5f56` و Repo 3 در SHA `01e07fe6fe78ee7a2b078bba06edfea4252ca007`; وضعیت کد Repo 5 در snapshot `be88104d89d5f17ecab887cc22643967950b654e` ثبت شد. تفاوت پیشنهاد قدیمی SQLite/React با معماری فعلی PostgreSQL/WPF/Agent صریح شد؛ اعداد قدیمی ۴۰ PC/۶۱ ایستگاه نیز با دامنهٔ واقعی هدف اشتباه گرفته نمی‌شوند.
- وضعیت پیاده‌سازی به‌صورت صادقانه ثبت شد: Identity و Stations کد نامزد دارند ولی گواهی latest-head لازم است؛ بقیهٔ ماژول‌های تجاری در snapshot هنوز نیازمندی/برنامه هستند، نه محصول آماده. هیچ کدی از Repo 2 یا 3 merge نشد.
- مرحلهٔ ۵ شامل اسناد [Definition of Ready](../planning/definition-of-ready.md)، [Definition of Done](../planning/definition-of-done.md)، [فرایند باگ](bug-fix-process.md)، [قالب باگ](../templates/bug-fix-template.md) و [قالب برش قابلیت](../templates/vertical-slice-template.md) است. دروازهٔ `scripts/check-work-item-docs.ps1` به `scripts/verify.ps1` وصل شد و وجود این قراردادها و یکتایی شناسه‌های نیازمندی را بررسی می‌کند.
- تأیید CI روی SHA دقیق `bcc18e998962ad724498f69d289ca8f9c084006f` موفق شد: [quick-validation run 77](https://github.com/alizebal73/5/actions/runs/37925813224). PowerShell parser و Canonical build/tests سبز شدند؛ دروازهٔ جدید مستندات نیز در همان Canonical verification اجرا شد.
- دو اجرای اولیهٔ دروازه ناموفق بودند چون عبارات الزامی validator با عنوان‌های واقعی اسناد تطبیق نداشتند: [run 75](https://github.com/alizebal73/5/actions/runs/37925664994) روی عنوان بخش رهگیری و [run 76](https://github.com/alizebal73/5/actions/runs/37925776663) روی عبارت خلاصهٔ قابلیت‌ها. شرط‌ها مطابق عناوین واقعی اصلاح شدند؛ اجرای نهایی run 77 موفق شد. این خطاهای validator هم در دفتر باگ ثبت می‌شوند.
- گارد `main` فعال است؛ `main` خالی و دست‌نخورده می‌ماند. مرحلهٔ بعد، مرحلهٔ ۶ است: ممیزی و تکمیل مسیر هویت اپراتور/ورود امن، با گواهی runtime روی همان SHA و بدون فرض اینکه وجود کد به معنی قبولی است.


## 2026-10-09 — تغییر اولویت: آزمون نصب یکپارچه پیش از فرمان PC بعدی

- علت: مرور نیازمندی نشان داد آزمون Agent از سورس در CI، نصب واقعی Server/Agent روی دو دستگاه را اثبات نمی‌کند؛ در مخزن فعلی طرح نصب وجود دارد اما اسکریپت/بستهٔ نصاب عملیاتی تأییدشده موجود نیست.
- ترتیب مقدماتی جدید: (۱) تعیین قرارداد و prerequisites انتشار؛ (۲) تولید خروجی‌های self-contained و manifest از SHA مشخص؛ (۳) بستهٔ Server + Desktop و بستهٔ مستقل Agent؛ (۴) تعیین/اعتبارسنجی امن PostgreSQL، schema migrations و bootstrap مدیر؛ (۵) نصب تمیز روی دو Windows PC، Health/Readiness/Agent heartbeat/Health Probe، disconnect/reconnect/Restart؛ (۶) ثبت شواهد و نقص‌ها. هیچ installer artifact تا زمان build، parse/validation و اجرای نصب، «آمادهٔ نصب» اعلام نشود.
- تصمیم اجرایی باز: معلوم شود PostgreSQL را نصاب مدیریت/نصب می‌کند یا نسخهٔ ازپیش‌نصب‌شده را می‌پذیرد؛ تصمیم باید بر اساس نسخه/امنیت/backup/upgrade واقعی باشد، نه پنهان‌کردن نیاز به نصب.
- این زیربخش، جایگزین مرحلهٔ کامل ۲۱ (repair/update/rollback و انتشار) نیست؛ تنها پیش‌نیاز عملیاتی برای تست زودهنگام پایه است. `main` و `foundation/runtime-final-v2` دست‌نخورده می‌مانند.


## 2026-10-09 — گواهی خروجی خام و گیت مهاجرت صریح

- SHA c5918d4627955acc46eab1bfeac6501f05be3a00: [Quick Validation #123](https://github.com/alizebal73/5/actions/runs/37954272590) موفق؛ build خروجی نسخه‌دار و Upload Artifact هم موفق شد. Artifact gamenet-deployment-payload-c5918d4627955acc46eab1bfeac6501f05be3a00 شناسهٔ 11627097942 است. این ZIP خروجی خام است، نه Setup.exe/MSI و نه تأیید نصب مشتری.
- گام فعلی، اجرای Full Foundation روی harness مهاجرت است تا فرمان واقعی GameNet.Server.exe --migrate-only در PostgreSQL تازه، با deadline و بررسی صریح عدم شروع listener آزموده شود.
- manifest کلی build جدا می‌ماند؛ هر ZIP manifest مختص payload خودش خواهد داشت. اسکریپت ساخت تنظیمات DPAPI نیز در بستهٔ Server/Database قرار می‌گیرد.


- تکمیل گواهی فنی روی SHA `c8a20caabad2c963c6a4b1cbcac7e3cbcb3575f7`: [Full Foundation #194](https://github.com/alizebal73/5/actions/runs/37955024118) موفق؛ PostgreSQL clean migration از apphost واقعی با timeout و بررسی عدم شروع listener، Backup/Restore، Desktop Runtime و Agent Runtime همگی گذشتند.
- SHA فعلی `b7e232d068fb6c085716980249a83abb205961b3` نسبت به SHA گواهی‌شدهٔ `c8a20ca...` فقط اسکریپت بسته‌بندی و مستندات را تغییر می‌دهد؛ [Quick Validation + payload #125](https://github.com/alizebal73/5/actions/runs/37955819051) روی همان SHA جاری موفق شدند.
- Artifact آخر: `gamenet-deployment-payload-b7e232d068fb6c085716980249a83abb205961b3`, ID `11628326329`; شامل دو ZIP self-contained، manifest مجزای سازگار با هر ZIP و `SHA256SUMS.txt` است. بررسی قبل از upload، اندازه و SHA فایل‌های داخل هر ZIP را دوباره محاسبه می‌کند. این‌ها **خروجی خام هستند، نه Setup.exe/MSI** و روی دو رایانهٔ واقعی نصب نشده‌اند.
