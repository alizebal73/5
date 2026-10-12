# GameNet 5 — Capability Matrix (خلاصهٔ محصول)

این جدول فهرست سریع ماژول‌ها و اولویت آن‌هاست. **مرجع جزئیات، وابستگی‌ها، معیار پذیرش، ریسک، وضعیت واقعی و آزمون بازگشتی هر نیازمندی** در [ماتریس رهگیری نیازمندی‌ها](requirements-traceability.md) قرار دارد. Roadmap مسیر تحویل را مشخص می‌کند؛ وجود کد یا صفحه به‌تنهایی وضعیت Done نمی‌سازد.

| حوزه | قابلیت‌های الزامی | مالک داده/تصمیم | وابستگی اصلی | اولویت |
|---|---|---|---|---|
| Foundation/Contracts | Modular Monolith، قرارداد V1، typed errors، migration policy، transaction/idempotency/audit/outbox | Server/Shared | Persistence/Security | P0 |
| Identity | bootstrap مالک امن، login/logout، password hash، revoke/lockout، نقش و مجوز | Server / Identity | PostgreSQL/Audit | P0 |
| Stations | PC/PS5/فوتبال‌دستی، وضعیت مدیریتی و online جدا، فهرست/فیلتر/گروه‌بندی | Server / Stations | Sessions/Tariffs/Agents | P0 |
| Agents | هویت/ثبت دستگاه، credential، heartbeat، lease، fencing، reconnect/reconciliation | Server / Agents | Identity/Stations | P0 |
| Client Control | سیاست فرمان‌های مجاز PC، چرخهٔ فرمان و نتیجهٔ هر رایانه؛ Remote maintenance فقط با پشتیبانی واقعی | Server / ClientControl | Agents/Identity/Audit | P0/P1 |
| Customers | شناسه و پروفایل/PIN، جست‌وجو، وضعیت، مالی/VIP/history، قواعد login چنددستگاهی | Server / Customers | Identity/Sessions/Wallet | P0 |
| Sessions | start/pause/resume/extend/reduce/transfer/end/recovery، ownership اتمیک | Server / Sessions | Stations/Customers/Agents/Tariffs | P0 |
| Tariffs | تعرفهٔ مجزای PC/PS5/فوتبال‌دستی؛ زمان/روز/نفرات/group/VIP؛ snapshot قیمت | Server / Tariffs | Stations/Sessions/VIP | P0/P1 |
| Billing/Invoices | preview/checkout، تفکیک اجزای مبلغ، فاکتور و پرداخت چندروش، refund/reverse | Server / Billing | Sessions/Wallet/Buffet | P0 |
| Wallet/Gifts/Debt | ledger شارژ/مصرف/پرداخت؛ Free Money و Free Time مستقل؛ Debt جدا از PendingPayment | Server / Wallet + Billing | Customers/Audit/Idempotency | P0 |
| Discounts | قابلیت تخفیف/هدیه با سقف، علت، permission و Audit؛ ماژول جدا نیست | Server / Billing + Tariffs | Identity/Approvals | P1 |
| VIP | طرح، مدت، سطح، سقف روزانه، مصرف و مازاد | Server / VIP | Customers/Tariffs/Sessions | P1 |
| Games | کاتالوگ و metadata بازی و سیاست مجاز اجرای Server-side | Server / Games | ClientControl/Agents/Stations | P1 |
| Game Account Pool | lease و تخصیص حساب به game/session/station، release و audit؛ بعد از تصویب ADR-0003 | Server / GameAccounts | Games/Sessions/Agents/secret lifecycle | P1 / مشروط |
| Network policy | تنظیم versioned در Settings و اجرای فقط از ClientControl/Agent واقعی؛ بدون رفتار جعلی | Settings + ClientControl | Agents/Permissions/Hardware support | P1 |
| Inventory | ledger موجودی، انبار/ویترین، خرید، انتقال، شمارش، ضایعات، برگشت | Server / Inventory | Audit/Approvals | P1 |
| Buffet/POS | سفارش، فروش مستقل یا مرتبط به Session/Customer Account، پرداخت و برگشت | Server / Buffet | Inventory/Billing/Customers | P1 |
| Reports | گزارش فقط‌خواندنی درآمد/Session/Wallet/VIP/Inventory/Operators/Audit و شیفت، فیلتر Server-side | Server / Reports | Ledgerها و read modelهای Shifts | P1 |
| Shift/Cash | افتتاح صندوق، expected-vs-counted، reconciliation، بستن/تحویل شیفت؛ موجودی مستقل | Server / Shifts | Billing/Wallet/Audit | P1 |
| Settings | تنظیمات مؤثر و versioned سمت Server، validation و Audit | Server / Settings | Identity/هر ماژول | P1 |
| Approvals | درخواست/تأیید/رد عملیات حساس با actor/reason/expiry/Audit | Server / Approvals | Identity/Audit | P1 |
| Audit | actor/action/target/time/reason/correlation/reference، تغییرناپذیری | Server / Platform Audit | همهٔ mutationها | P0 |
| Backup/Recovery | ایجاد/تأیید backup، restore ایزوله، reconciliation و runbook | Server/Operations | PostgreSQL/Release | P0 برای بهره‌برداری |
| Diagnostics/Observability | health در مقابل readiness، structured logs، correlation، Agent status و reconciliation | Server / Platform | همهٔ ماژول‌ها | P0 |
| Setup/Update | نصب Windows service، secret provisioning، LAN binding، data root، signed/hash-checked update، repair/rollback | Platform Release | Server/Desktop/Agent/Database | P0 پیش از release |
| Localization | فارسی RTL، تومان، تاریخ نمایشی شمسی، منابع en-US | Desktop/Shared | تمام صفحات | P0 |
| Reservation/Waitlist | نوبت و ظرفیت/لغو/no-show | Server / Reservations | Stations/Sessions | P2 |
| Payroll | دفتر حقوق/پیش‌پرداخت/طلب کارکنان | در صورت تأیید، ماژول مستقل | Shift/Reports/Audit | P2 |
| Tournament/Mobile/SMS/Multi-shop | امکانات تمایزدهندهٔ آتی | ماژول/Integration مستقل | تصمیم محصول و ADR | Deferred |
| CCBOOT/PXE/Printer/External gateway | فقط بعد از نیاز مشخص، سخت‌افزار/Provider و آزمون واقعی | Integration مستقل | Security/Release | Deferred |

## قواعد تجاری‌ای که هر UI باید از Server بگیرد

- مبلغ نهایی، ماندهٔ Wallet، Debt، Free Money، Free Time، اعتبار VIP و مجوز تصمیمات UI نیستند.
- Station، Session، CustomerLogin و Agent connection چهار مفهوم جدا با مالکیت قابل‌تست‌اند.
- موجودی، ورودی/انتقال/فروش/برگشت را از ledger واقعی می‌خواند؛ انبار و ویترین یک موجودی قابل‌تعویض نیستند.
- عملیات حساس باید تراکنشی، idempotent و audited باشند و خطای رقابت را واضح برگردانند.
- وضعیت آنلاین از lease/heartbeat معتبر حاصل می‌شود، نه از آخرین event دیده‌شده در UI.
- هر دکمهٔ عملیاتی باید backend واقعی، مجوز Server، رفتار خطا و آزمون داشته باشد؛ mock، success ساختگی و placeholder در production ممنوع‌اند.
- فارسی RTL و واحد تومان در کل مسیر UI/گزارش سازگارند؛ محاسبات زمان UTC/business-timezone را از هم جدا می‌کنند.

## وضعیت جاری پیاده‌سازی — بازبینی 2026-10-12 (snapshot تاریخی، نه اشاره‌گر زنده)

- **Foundation:** شاخهٔ `foundation/runtime-final-v2` روی SHA دقیق `71ef6534331e6ba5a8a460fd5b8846fe40e8567a` گواهی Full Foundation دارد ([run #38182502053](https://github.com/alizebal73/5/actions/runs/38182502053)). این گواهی مربوط به همان SHA است.
- **Runtime / Identity / Stations / Outbox:** کد اصلی Runtime/Operator در PR #40 ادغام شد؛ SHA کد `58717adc6c1553ef21221c3d484f093494ba1ae7` آزمون‌های Quick و Full Foundation را گذراند. تغییرات CI در PR #44 ادغام شد؛ SHA ادغام `161b4cbbbe16cd3fd4046f174a1c09380139c67b` نیز Quick و Full Foundation را گذراند. این شواهد آزمون خودکارند، نه گواهی نصب واقعی یا نمایش تعاملی نهایی.
- **Agent credentials/enrollment و Server secret store:** مسیر کد ثبت Agent یک‌بارمصرف، جلوگیری از replay، ذخیرهٔ protected و DPAPI secret-store در شاخهٔ Integration وجود دارد و پوشش خودکار دارد. ADR-0002 هنوز Proposed است؛ مجوز مؤثر Windows Service، DPAPI/TLS private-key زیر هویت سرویس و رفتار بعد از restart تا پذیرش واقعی مرحلهٔ آخر اثبات نشده‌اند.
- **Desktop:** پوستهٔ WPF، Login و StationBoard پایه، resourceهای `fa-IR`/`en-US`، بررسی‌های binding/resource و کنترل‌های permission موجودند. استخراج کامل viewها، همهٔ صفحه‌های تجاری و پذیرش تعاملی RTL/LTR، keyboard/focus و وضعیت‌های offline/error تکمیل نشده‌اند. PR #24 همچنان یک تغییر جداگانه برای مقایسه/استخراج view است و نباید کورکورانه ادغام شود.
- **ماژول‌های تجاری باقی‌مانده:** Customers، Sessions، Tariffs، Billing، Wallet/Debt، ClientControl، Games/GameAccounts، Inventory، Buffet، VIP، Reports، Shifts، Settings و Approvals هنوز به‌عنوان قابلیت‌های کامل و تأییدشده موجود نیستند. باید به‌ترتیب نقشهٔ راه، با رهگیری نیازمندی→کد/API→تست/شاهد پیاده‌سازی شوند؛ صفحه یا mock به معنی تکمیل نیست.
- **نصاب و انتشار:** ZIP دارای hash/manifest با Setup.exe/MSI برابر نیست. Setup واقعی، provisioning مدل PostgreSQL، نصب/repair/upgrade، updater/rollback، امضای انتشار، SBOM و مسیر بازیابی هنوز تکمیل/گواهی نشده‌اند.
- **زمان‌بندی آزمایش‌های واقعی:** بر اساس تصمیم 2026-10-12، آزمایش فیزیکی Windows Service/LAN، پذیرش دستی WPF و پذیرش یکپارچهٔ نصب فقط پس از تکمیل دامنهٔ برنامه‌ریزی‌شده و ساخت Setup.exe/MSI انجام می‌شوند. CI، unit/integration/regression/security tests در طول توسعه ادامه دارد.
- **موارد عمداً Deferred:** Reservations/Waitlist، Tournament، موبایل/SMS، چندشعبه‌ای، CCBOOT/PXE، چاپگر و درگاه بیرونی تا تصمیم مستقل محصول/امنیت/مجوز پیاده‌سازی نمی‌شوند.

مبنای وضعیت و ترتیب: [Active Execution Control](../operations/active-execution-control.md)، [Master Build Plan](master-build-plan.md) و [Requirements Traceability](requirements-traceability.md). SHAهای بالا شواهد تاریخی دقیق‌اند؛ برای عملیات بعدی همیشه head زندهٔ شاخه را از GitHub بخوانید.
