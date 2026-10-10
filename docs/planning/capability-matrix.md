# GameNet 5 — Capability Matrix (خلاصهٔ محصول)

این جدول فهرست سریع ماژول‌ها و اولویت آن‌هاست. **مرجع جزئیات، وابستگی‌ها، معیار پذیرش، ریسک، وضعیت واقعی و آزمون بازگشتی هر نیازمندی** در [ماتریس رهگیری نیازمندی‌ها](requirements-traceability.md) قرار دارد. Roadmap مسیر تحویل را مشخص می‌کند؛ وجود کد یا صفحه به‌تنهایی وضعیت Done نمی‌سازد.

| حوزه | قابلیت‌های الزامی | مالک داده/تصمیم | وابستگی اصلی | اولویت |
|---|---|---|---|---|
| Foundation/Contracts | Modular Monolith، قرارداد V1، typed errors، migration policy، transaction/idempotency/audit/outbox | Server/Shared | Persistence/Security | P0 |
| Identity | bootstrap مالک امن، login/logout، password hash، revoke/lockout، نقش و مجوز | Server / Identity | PostgreSQL/Audit | P0 |
| Stations | PC/PS5/فوتبال‌دستی، وضعیت مدیریتی و online جدا، فهرست/فیلتر/گروه‌بندی | Server / Stations | Sessions/Tariffs/Agents | P0 |
| Agents | pairing، هویت دستگاه، credential، heartbeat، lease، reconnect/fencing/reconciliation | Server / Agents + مشاهدهٔ Agent | Identity/Stations | P0 |
| Client Control | فرمان‌های مجاز واقعی، lock/unlock، launch/stop، نتیجهٔ per-PC، Remote maintenance در صورت پشتیبانی | Server / ClientControl | Agent/Permissions/Audit | P0/P1 |
| Customers | شناسه و پروفایل/PIN، جست‌وجو، وضعیت، مالی/VIP/history، قواعد login چنددستگاهی | Server / Customers | Identity/Sessions/Wallet | P0 |
| Sessions | start/pause/resume/extend/reduce/transfer/end/recovery، ownership اتمیک | Server / Sessions | Stations/Customers/Agents/Tariffs | P0 |
| Tariffs | تعرفهٔ مجزای PC/PS5/فوتبال‌دستی؛ زمان/روز/نفرات/group/VIP؛ snapshot قیمت | Server / Tariffs | Stations/Sessions/VIP | P0/P1 |
| Billing/Invoices | preview/checkout، تفکیک اجزای مبلغ، فاکتور و پرداخت چندروش، refund/reverse | Server / Billing | Sessions/Wallet/Buffet | P0 |
| Wallet/Gifts/Debt | ledger شارژ/مصرف/پرداخت؛ Free Money و Free Time مستقل؛ Debt جدا از PendingPayment | Server / Wallet + Billing | Customers/Audit/Idempotency | P0 |
| Discounts | تخفیف با سقف، علت، permission و Audit | Server / Billing | Tariffs/Identity/Approvals | P1 |
| VIP | طرح، مدت، سطح، سقف روزانه، مصرف و مازاد | Server / VIP | Customers/Tariffs/Sessions | P1 |
| Games | catalog، مسیر/پارامتر اجرا و launch policy مجاز | Server / Games | ClientControl/Agent/Stations | P1 |
| Game Account Pool | lease و تخصیص account به game/session/station، release و audit | Server / GameAccounts | Games/Sessions/Agents | P1 |
| Network policy | انتخاب مسیر اینترنت ۱/۲ و کنترل آنلاین/آفلاین برای PC، فقط با Agent واقعی | Server / Settings/ClientControl | Agent/Permissions | P1 |
| Inventory | ledger موجودی، انبار/ویترین، خرید، انتقال، شمارش، ضایعات، برگشت | Server / Inventory | Audit/Approvals | P1 |
| Buffet/POS | سفارش، فروش مستقل یا مرتبط به Session/Customer Account، پرداخت و برگشت | Server / Buffet | Inventory/Billing/Customers | P1 |
| Reports | گزارش درآمد/Session/Wallet/VIP/Inventory/Operators/Audit، فیلتر Server-side و تاریخ فارسی | Server / Reports | Ledgerها و read modelها | P1 |
| Shift/Cash | cash opening، فروش واقعی، refund/discount/expense، expected-vs-counted و close lock | Server / Shift/Reports | Billing/Audit | P1 |
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

## وضعیت جاری پیاده‌سازی — 2026-10-10

- **Foundation reference:** آخرین stable-checkpoint ثبت‌شده برای SHA دقیق c3a8ba482548e963479fbdf8538be62c4d15acfe است. Tip فعلی Foundation branch برابر be29687e637b709158a30204bb9213bfa4813950 است و باید روی همان SHA مجدداً full certification شود؛ این دو status یکی نیستند.
- **Runtime / Identity / Stations:** integration/runtime-operator-v1 روی SHA 55a340305f9eba3bc8f9a1ce65fb37bedbae7580 از Quick Validation و Full Foundation عبور کرده است؛ PR #23 Draft است و physical Windows-Service/LAN gate هنوز باز است.
- **Desktop UI boundary:** PR #24 اولین استخراج StationBoardView را روی SHA 9a058043ca8ba69f2b0bd23777263063ee681c92 با Quick و Full Foundation سبز کرده است؛ ادغام نشده و جداسازی timer/Login/Shell/command permissions و UI interaction tests باقی‌اند.
- **Customers, Sessions, Tariffs, Billing, Wallet/Debt, Inventory, Buffet, VIP, Reports, Shift, Approvals:** نیازمندی‌های محصولی‌اند؛ در snapshot یکپارچهٔ جاری ماژول‌های کامل و تأییدشدهٔ آن‌ها وجود ندارد. تا عبور cross-PC Foundation runtime gate نباید با دکمه/صفحهٔ نمایشی به‌عنوان قابلیت آماده عرضه شوند.
- **Setup/update/repair/rollback:** خروجی ZIP با manifest با Setup.exe/MSI برابر نیست. Windows Service commissioning، انتخاب مدل PostgreSQL provisioning، دو-PC install/restart/reconnect و Release gate هنوز باز هستند.
- **Steam/GamingAccounts:** خارج از اجرای فعلی و Deferred است تا requirement محصولی و مدل integration مجاز/پشتیبانی‌شده به‌صورت روشن تأیید شود؛ branch آزمایشی قدیمی مجوز پیاده‌سازی خودکار نیست.

مبنای مرجع وضعیت و ترتیب: [Engineering Readiness Register](engineering-readiness-register.md). اجرای موفق روی SHA قبلی، وضعیت candidate فعلی یا Release را خودکار گواهی نمی‌کند.
