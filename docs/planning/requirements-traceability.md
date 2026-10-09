# ماتریس مرجع نیازمندی‌ها و رهگیری — GameNet 5

این سند، فهرست مرجع نیازمندی‌های محصول و معیارهای پذیرش آن است. هر قابلیت قبل از کدنویسی باید مالک، دادهٔ مرجع، وابستگی، مجوز، رفتار در خطا/رقابت، آزمون و شواهد پایان کار مشخص داشته باشد. وجود صفحه، endpoint، entity یا تست واحد به‌تنهایی وضعیت «تکمیل‌شده» ایجاد نمی‌کند.

## مبنای ممیزی و تقدم تصمیم‌ها

این ماتریس از اسناد محصول، نقشهٔ عملیات، بک‌لاگ و دروازهٔ تست دو مخزن قدیمی استخراج شده و با قراردادهای معماری و کنترل‌های مخزن ۵ تطبیق داده شده است:

- **Repo 2**، شاخهٔ main در SHA برابر با d8031070413338c50fd67155a8074c45c91b5f56: docs/02-feature-synthesis.md، docs/03-architecture-plan.md، docs/08-action-map.md، docs/09-client-experience.md، docs/10-product-completion-backlog.md، docs/19-stage14-checkpoint-2026-10-05.md، docs/20-physical-validation-2-3pc-2026-10-05.md و docs/راه-اصلاح-باگ.md.
- **Repo 3**، شاخهٔ main در SHA برابر با 01e07fe6fe78ee7a2b078bba06edfea4252ca007: docs/planning/product-capability-map.md، docs/research/repo2-to-repo3-comprehensive-audit.md، docs/research/repo2-to-repo3-foundation-gap-matrix.md، docs/architecture/module-boundary-manifest.md، docs/architecture/data-ownership-and-integration-map.md، docs/architecture/transaction-rules.md، docs/domain/time-and-money.md، docs/domain/session-state-machine.md، docs/invariants/session.md، docs/invariants/inventory.md و docs/planning/requirements-traceability.md.
- **Repo 5**، snapshot کد پیش از تغییرات مستنداتی مرحلهٔ ۴: feature/operator-identity-v1 در SHA برابر با be88104d89d5f17ecab887cc22643967950b654e. commitهای بعدی این مرحله فقط مستندات را تغییر می‌دهند.

**تقدم تصمیم‌ها در تعارض‌ها:** مرز محصول و خواسته‌های تثبیت‌شدهٔ GameNet 98 مقدم‌اند؛ معماری جاری Repo 5 و ADRهای آن بر پیشنهادهای تاریخی Repo 2 مقدم‌اند؛ درس‌ها و قراردادهای مرزی Repo 3 برای جلوگیری از تکرار خطا حفظ می‌شوند؛ هیچ کدی از Repo 2/3 بدون ممیزی و تصمیم مستقل منتقل یا merge نمی‌شود.

**دامنهٔ واقعی هدف:** یک گیم‌نت، داشبورد اپراتوری Native ویندوز، حدود ۱۵ رایانهٔ PC در مقیاس هدف، و ایستگاه‌های PS5 و فوتبال‌دستی. پیش از مقیاس کامل، گیت فیزیکی روی ۲–۳ PC اجرا می‌شود. عددهای قدیمی Repo 2 مانند ۴۰ PC یا گرید ثابت ۶۱ ایستگاهی، ظرفیت الزام‌آور این نسخه نیستند. PS5 و فوتبال‌دستی Agent ویندوزی فرضی ندارند؛ تایمر، تعرفه و هشدار اپراتوری آن‌ها از Server/Dashboard مدیریت می‌شود.

### برچسب‌ها

- **P0 — هستهٔ اولین مسیر عملیاتی:** بدون آن چرخهٔ امن و واقعی عملیات روزانه ممکن نیست.
- **P1 — لازم برای تکمیل تجاری نسخهٔ اول:** پس از هسته و پیش از ادعای محصول کامل پیاده می‌شود.
- **P2 — توسعهٔ بعدی:** برای کارکرد پایهٔ گیم‌نت شرط اولیه نیست.
- **Deferred — خارج از نسخهٔ اول:** تا تأیید نیاز و برنامهٔ مشخص، نباید UI یا mock عملیاتی آن نمایش داده شود.
- **ریسک بحرانی/زیاد/متوسط:** اثر نقض نیاز بر پول، امنیت، مالکیت Session/Agent، موجودی، داده یا قابلیت بازیابی را نشان می‌دهد.
- **وضعیت Repo 5** وضعیت کد/شواهد در snapshot فوق است، نه وعده یا گواهی انتشار. گواهی Foundation روی SHA قبلی خود معتبر است؛ کد تجاری جدید نیاز به گواهی دقیق روی همان SHA دارد.

## ۱) نیازمندی‌های مشترک، هویت و کنترل دسترسی

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-ARCH-001 | Server تنها مرجع قواعد و state تجاری باشد. Desktop و Agent فقط از قراردادهای مجاز درخواست/مشاهده کنند؛ هیچ‌کدام به PostgreSQL وصل نشوند یا هزینه، مجوز، مالکیت Session و موجودی را مستقلاً تعیین نکنند. | Platform + همهٔ ماژول‌ها؛ Shared contracts | P0 / بحرانی | چارچوب Foundation موجود؛ گواهی هر SHA تجاری جداست. |
| REQ-ARCH-002 | Modular Monolith با مرزهای Domain/Application/Infrastructure/Api/Composition حفظ شود. Program.cs فقط composition باشد؛ دسترسی EF/DbContext در Domain/Application/Api و ارجاع مستقیم به داخلی ماژول دیگر رد شود. Guard اندازهٔ فایل و dependencyها باید در CI fail کند. | معماری + هر ماژول | P0 / زیاد | guard و اسکلت موجود؛ باید روی تغییرات بعدی حفظ شوند. |
| REQ-API-001 | قراردادهای V1 در Shared مرجع یگانه باشند؛ API خطاهای typed/stable، versioning و correlation داشته باشد. Client DTO موازی یا parsing متن exception مجاز نیست. تغییر ناسازگار نیازمند version/ADR/test است. | Shared + Server API + Desktop/Agent | P0 / زیاد | Foundation contracts/guards موجود؛ قرارداد هر قابلیت هنوز باید اضافه شود. |
| REQ-AUTH-001 | Bootstrap مالک فقط با راز اولیهٔ امن و به‌صورت create-once انجام شود؛ هیچ رمز پیش‌فرض/مخفی وجود نداشته باشد. طول username/password در مسیر ورودی محدود باشد تا ورودی بزرگ موجب هزینهٔ پردازشی/پایگاه‌دادهٔ نامتناسب نشود. Login واقعی، hash امن، logout/revoke، محدودیت تلاش و lockout، و رد توکن لغوشده با PostgreSQL اثبات شوند. | Identity؛ Settings/Security | P0 / بحرانی | کد Identity نامزد پیاده‌سازی دارد؛ گواهی کامل روی آخرین SHA هنوز شرط است. |
| REQ-AUTH-002 | مجوز هر عملیات روی Server و بر اساس actor/role/permission بررسی شود؛ مخفی‌کردن دکمه مجوز نیست. مدیریت کاربر/نقش و عملیات پرخطر (تخفیف، refund، اصلاح مالی/انبار، shutdown، recovery، تنظیمات) مجوز جدا و در صورت نیاز Approval داشته باشد. | Identity + Approvals + Audit | P0 / بحرانی | فقط بخش پایهٔ Identity موجود؛ پوشش کامل permission/approval هنوز معیار باز است. |
| REQ-AUD-001 | هر تغییر حساس یک Audit پایدار با actor، action، target، زمان UTC، علت، source/client، correlation و reference ایجاد کند؛ before/after برای تغییر حساس موجود باشد. Audit قابل ویرایش/حذف توسط اپراتور نباشد و با تغییر تجاری در همان تراکنش ذخیره شود. | Platform Audit؛ تمام use caseها | P0 / بحرانی | زیرساخت پایه وجود دارد؛ پوشش رویدادهای business باید همراه هر قابلیت تست شود. |
| REQ-DATA-001 | PostgreSQL منبع production باشد؛ schema از migration نسخه‌دار و قابل بازبینی ساخته/ارتقا یابد. Server در Production بی‌اجازه migration اجرا نکند؛ clean-from-zero، upgrade و pending-model check اجرا شوند. | Persistence/Release | P0 / بحرانی | Foundation قبلاً گواهی شده؛ تغییرات schema/business جدید باید مجدداً روی آخرین SHA تأیید شوند. |
| REQ-TXN-001 | mutation حساس از use case و Transaction Coordinator عبور کند؛ Audit/Idempotency و Outbox لازم در همان مرز اتمیک باشند. هیچ network/SignalR/Agent/file side effect داخل callback تراکنش انجام نشود؛ SaveChanges مستقیم از callback ممنوع. | Application + Transaction/Outbox | P0 / بحرانی | primitiveهای Foundation موجود؛ برای هر use case آزمون مرزی لازم است. |

## ۲) ایستگاه‌ها، Agent و کنترل PC

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-STN-001 | Station دارای شناسهٔ پایدار، نام، نوع و status مدیریتی باشد. انواع هدف: PC، PS5 و فوتبال‌دستی؛ نوع ایستگاه قابل توسعه است. Health/Online از وضعیت مدیریتی جدا باشد. هر Station حداکثر یک Session فعال و هر Agent حداکثر یک Station مجاز داشته باشد. | Stations؛ Agents/Tariffs/Sessions | P0 / بحرانی | ماژول Stations و API موجود؛ گواهی کامل latest-head پس از آخرین تغییرات لازم است. |
| REQ-STN-002 | Dashboard فهرست زندهٔ قابل‌مقیاس با sort/filter/group بر اساس نوع، وضعیت، مشتری، بدهی و زمان باقی‌مانده ارائه دهد. Start/checkout و عملیات رایج از Station card/context menu در دسترس باشند؛ برای Postpaid زمان باقی‌ماندهٔ جعلی نشان داده نشود. | Desktop UI؛ Stations/Sessions/Customers | P0 / زیاد | UI نهایی کامل نیست؛ نیازمندی ثبت‌شده است. |
| REQ-NET-001 | برای PC، مسیر اینترنت/شبکهٔ ۱ و ۲ و وضعیت قابل‌مشاهده تعریف شود؛ تغییر route یا قطع/وصل فقط اگر Agent و policy واقعی پشتیبانی می‌کنند، با مجوز، تأیید و Audit. این قابلیت به PS5/فوتبال‌دستی تعمیم داده نشود. | Settings + ClientControl + Agent | P1 / زیاد | پیاده‌سازی محصولی تأیید نشده است. |
| REQ-AGT-001 | Agent هویت پایدار، credential امن و pairing صریح داشته باشد؛ identity بر اساس ConnectionId موقت نباشد. credential revocation و binding یکتا در Server ذخیره شود. | Agents/Identity؛ PostgreSQL | P0 / بحرانی | زیرساخت identity/lease وجود دارد؛ رفتار کامل محصول/PC هنوز گواهی نشده است. |
| REQ-AGT-002 | heartbeat، lease expiry، fencing و reconnect/reconciliation مانع از اجرای فرمان با lease یا token قدیمی شوند. بعد از reconnect، snapshot مرجع از Server دوباره دریافت شود؛ SignalR event به‌تنهایی حقیقت نهایی نیست. | Agents + realtime/lease | P0 / بحرانی | Foundation transport primitives موجود؛ گواهی واقعی Agent/Windows و آخرین SHA الزامی است. |
| REQ-AGT-003 | Server مجازبودن هر فرمان را تعیین کند. Agent فقط فرمان معتبر و جاری را اجرا و نتیجهٔ هر PC را جدا گزارش کند. Launch/Stop/Lock/Unlock/Message و در صورت پشتیبانی Screenshot/Restart/Shutdown/network switch؛ فرمان ناشناخته، stale یا غیرمجاز باید fail closed شود. | ClientControl/Games + Agent + Permissions/Audit | P0 برای فرمان‌های Session؛ P1 برای remote maintenance / زیاد | اجرای کامل فرمان‌های تجاری در snapshot اثبات نشده؛ هیچ دکمهٔ نمایشی به‌جای عمل واقعی مجاز نیست. |
| REQ-AGT-004 | قطع Server/Agent، restart و timeout مسیر مشخص داشته باشد: وضعیت واقعی با lease و reconciliation بازیابی شود، commandهای تکراری موجب اثر دوباره نشوند و loss of authority پس از انقضای lease رعایت شود. | Agents + Sessions + Recovery | P0 / بحرانی | قرارداد Foundation وجود دارد؛ تست end-to-end روی PC واقعی باز است. |

## ۳) مشتری، Session و چنددستگاهی

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-CUS-001 | پروفایل مشتری شامل شناسهٔ یکتا، نام/نام خانوادگی، لقب، موبایل، وضعیت، username/PIN در صورت نیاز، مانده‌ها، VIP و تاریخچه باشد. Deactivate/Block مسیر عادی است؛ حذف فیزیکی نباید تاریخچهٔ مالی را از بین ببرد. PIN/password خام ذخیره یا در log افشا نشود. | Customers + Identity | P0 / زیاد | ماژول مستقل Customers در snapshot حاضر نیست. |
| REQ-CUS-002 | CustomerLogin، Customer، Session، Station و Agent identityهای متمایزند اما با رابطهٔ صریح مالکیت. شروع Session از Dashboard یا Agent باید قواعد مالکیت یکسان داشته باشد؛ login مشتری نباید به دستگاه یا Session اشتباه وصل شود. | Customers + Sessions + Agents | P0 / بحرانی | نیازمندی؛ implementation محصولی حاضر نیست. |
| REQ-CUS-003 | سیاست login هم‌زمان مشتری قابل تنظیم/صریح باشد. چند Session مجاز مشتری باید هرکدام SessionId و تخصیص مصرف مستقل داشته باشند؛ login/Session ناسازگار یا انتقال هم‌زمان باید با transaction/constraint رد شود. | Customers + Sessions + Wallet/Billing | P0 / بحرانی | تعریف محصولی و آزمون رقابت هنوز لازم است. |
| REQ-SES-001 | چرخهٔ صریح Session شامل start، pause، resume، extend، reduce، transfer، end/checkout، cancel و recovery باشد. هر transition precondition، actor، تاریخچه و خطای typed داشته باشد؛ UI نتواند state دلخواه بسازد. | Sessions؛ Stations/Customers/Tariffs/Agents | P0 / بحرانی | ماژول Sessions در snapshot حاضر نیست. |
| REQ-SES-002 | زمان machine در UTC ذخیره و با business timezone برای روز کاری/گزارش تفسیر شود. Billable timing یک مدل واحد داشته باشد؛ Pause از زمان قابل‌صورتحساب کم شود؛ tariff/time adjustment/cancel در محاسبه لحاظ شوند. نرخ گذشته با تغییر تعرفهٔ امروز عوض نشود. | Sessions + Time/Money + Tariffs | P0 / بحرانی | قرارداد معماری موجود؛ business implementation نیست. |
| REQ-SES-003 | Transfer به Station آزاد و سازگار انجام شود؛ claim مقصد و انتقال Session/Agent/Login/lease/ownership در یک عملیات اتمیک باشند. دو transfer هم‌زمان نتوانند مقصد واحد را تصاحب کنند؛ تاریخچهٔ مبدأ و مقصد ثبت شود. | Sessions + Stations + Agents | P0 / بحرانی | پیاده‌سازی نشده؛ Repo 2 شکست شناخته‌شدهٔ انتقال فقط StationId باید regression دائمی داشته باشد. |
| REQ-SES-004 | پس از قطع برق، restart Server یا Agent، Session باز از PostgreSQL و رخدادهای پایدار reconcile شود؛ elapsed/billable time، lease و وضعیت station دوباره محاسبه شوند. هیچ Session یتیم یا charge تکراری ایجاد نشود. | Sessions + Recovery + Agents | P0 / بحرانی | نیازمندی؛ فقط پس از runtime/physical evidence تأیید می‌شود. |

## ۴) تعرفه، صورتحساب، کیف پول و بدهی

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-TAR-001 | تعرفهٔ مجزا برای PC، PS5 و فوتبال‌دستی؛ در صورت نیاز تعداد نفرات/کنترلر و سرویس اضافی را پشتیبانی کند. Start Session باید tariff معتبر را پیش از شروع resolve کند. | Tariffs؛ Stations/Sessions | P0 / زیاد | ماژول Tariffs در snapshot حاضر نیست. |
| REQ-TAR-002 | تعرفه بتواند بازهٔ زمانی، روز هفته، zone/group، tier/VIP، حداقل مبلغ، prepaid/postpaid و package/offer را بدون شرط‌های پراکندهٔ UI تعریف کند. هم‌پوشانی یا تعرفهٔ نامعتبر باید با خطای قابل‌فهم رد شود. | Tariffs/Settings | P1 / زیاد | نیازمندی ثبت‌شده؛ implementation حاضر نیست. |
| REQ-TAR-003 | snapshot تعرفه و علت انتخاب آن در Session/Billing ذخیره شود. تغییرات بعدی تعرفه، صورتحساب گذشته را تغییر ندهد؛ تغییر تعرفه در Session فعال ثبت و برای Preview/Settlement قابل توضیح باشد. | Tariffs + Sessions + Billing | P0 / بحرانی | هنوز پیاده‌سازی نشده است. |
| REQ-BIL-001 | Preview و checkout یک breakdown مرجع بسازند: raw charge، مدت قابل‌صورتحساب، tariff snapshot، مصرف package/free time، بوفه/سرویس، تخفیف، rounding، wallet/debt impact، final amount، مبلغ دریافتی و change. محاسبهٔ نهایی فقط در Server باشد. | Billing + Sessions/Tariffs/Wallet/Buffet | P0 / بحرانی | ماژول Billing در snapshot حاضر نیست. |
| REQ-BIL-002 | نقد، کارتخوان، Wallet و Debt و پرداخت ترکیبی/تخصیص‌یافته پشتیبانی شود؛ هر payment مبلغ، روش، زمان واقعی ثبت و مرجع مشخص داشته باشد. دریافت پول واقعی با انتقال داخلی Wallet/Gift یکی شمرده نشود؛ جمع Shift/Report از یک تعریف استفاده کند. | Billing + Wallet + Shifts | P0 / بحرانی | نیازمندی ثبت‌شده؛ implementation حاضر نیست. |
| REQ-BIL-003 | «حساب باز / PendingPayment» از «Debt» مجزا باشد. برای هر مشتری تجمیع حساب باز طبق مدل مصوب انجام شود؛ پرداخت Session مشخص با SessionId تخصیص یابد و ماندهٔ Session یا Debt دیگری را مصرف نکند. Pending یا Session فعال، بدهی پرداخت‌نشده محسوب نشود. | Billing + Customers + Sessions | P0 / بحرانی | Repo 2 سابقهٔ conflation داشت؛ ماژول Billing/Customer Account در snapshot حاضر نیست. |
| REQ-BIL-004 | Refund/Reverse رکورد جدید immutable با reference تراکنش اولیه، actor، reason، permission، idempotency و اثر حسابداری معکوس باشد. حذف تراکنش اصلی یا refund تکراری ممنوع است. برگشت فروش باید stock source درست را نیز اصلاح کند. | Billing + Wallet/Inventory + Audit/Approvals | P1 / بحرانی | پیاده‌سازی نشده است. |
| REQ-WAL-001 | Wallet ledger مرجع باشد؛ هر TopUp، SessionCharge، BuffetCharge، PackagePurchase، GiftMoney، Refund، Adjustment و DebtSettlement entry قابل ردیابی داشته باشد. balance فقط projection قابل تطبیق با ledger است. مبلغ با value object/عدد صحیح و واحد تومان (TOM) محاسبه شود؛ float ممنوع. | Wallet + Shared Money | P0 / بحرانی | ماژول Wallet در snapshot حاضر نیست. |
| REQ-WAL-002 | Free Money و Free Time دو موجودی/مفهوم مستقل با دلیل، actor، زمان، مشتری و reference داشته باشند. مثال ثابت پذیرش: شارژ واردشده ۱۰۰٬۰۰۰ تومان با هدیهٔ ۱۰٪ یعنی ۱۰۰٬۰۰۰ اعتبار پولیِ خریداری‌شده + ۱۰٬۰۰۰ اعتبار هدیه + ۱۱۰٬۰۰۰ اعتبار قابل‌استفاده؛ هدیه نباید به‌عنوان وجه نقد دریافتی/درآمد جدید گزارش شود. | Wallet + Billing + Audit | P0 / بحرانی | نیازمندی؛ در snapshot حاضر پیاده‌سازی نشده است. |
| REQ-WAL-003 | Debt، پرداخت بدهی، PendingPayment و ماندهٔ Wallet در مدل و گزارش تفکیک شوند. تسویهٔ هم‌زمان با concurrency token/transaction محافظت شود؛ تعارض قابل retry یا خطای 409 مشخص بدهد، نه overwrite یا 500 مبهم. | Wallet/Billing + Customers | P0 / بحرانی | پیاده‌سازی نشده؛ از خطاهای مالی قدیمی regression الزامی است. |
| REQ-DISC-001 | تخفیف درصدی/ثابت و اعتبار هدیه با مجوز، علت، سقف و audit اعمال شود؛ raw amount، discount، rounding و final amount جدا نگه داشته شوند. اپراتور فقط حدود مجاز را اجرا کند؛ مجوزهای تخفیف از تنظیمات Server کنترل شوند. | Billing + Tariffs + Identity/Approvals | P1 / زیاد | implementation حاضر نیست. |

## ۵) VIP، بازی‌ها و Account Pool

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-VIP-001 | طرح VIP دارای نام، قیمت، مدت، تاریخ فعال/انقضا، tier، سقف روزانه و policy مصرف/مازاد باشد. سهمیه بر مبنای business timezone هر روز درست reset شود؛ paused/cancelled/free time محاسبهٔ استفاده را مخدوش نکند. مصرف روزانه و باقیمانده قابل گزارش باشد. | VIP + Customers/Tariffs/Sessions | P1 / زیاد | ماژول VIP در snapshot حاضر نیست. |
| REQ-GAME-001 | کاتالوگ بازی دارای نام، نسخه/دسته، مسیر نصب، executable/args، نوع اتصال، station group و policy مجاز/مخفی باشد. مسیر پیش‌فرض نصب بازی در تنظیمات قابل‌پیکربندی است؛ metadata بازی به معنی مجوز اجرا نیست. Launch فقط پس از اجازهٔ Server و اعتبار Session انجام شود. | Games + ClientControl/Agent/Stations | P1 / زیاد | capability در snapshot حاضر پیاده‌سازی نشده است. |
| REQ-GAME-002 | Account Pool فقط با lease دارای game/account/station/session/assigned/released/reason و نتیجهٔ login کار کند. تخصیص هم‌زمان یک account به دو Session ممنوع؛ release/recovery پس از پایان process/Session قابل تکرار و idempotent باشد. رمز بازی به Customer/Desktop/Agent logs یا UI غیرمجاز افشا نشود. | GameAccounts + Games/Sessions/Agents/Audit | P1 / بحرانی | ماژول Account Pool حاضر نیست؛ در صورت استفاده از حساب مشترک، شرط پذیرش PC release است. |

## ۶) انبار، ویترین، بوفه و فروش

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-INV-001 | محل موجودی انبار از ویترین/موجودی قابل‌فروش جدا باشد. ورود خرید به محل صحیح، انتقال به ویترین با دو حرکت مرتبط و اتمیک، فروش فقط از محل مجاز و عدم منفی‌شدن موجودی اثبات شود. گزارش موجودی باید location را نمایش دهد. | Inventory + Audit | P1 / بحرانی | ماژول Inventory حاضر نیست؛ این جداسازی regression شناخته‌شده است. |
| REQ-INV-002 | هر stock mutation (خرید/ورود، فروش، transfer، شمارش، correction، waste، return) علت، actor، source/reference و ledger movement داشته باشد. برگشت فروش تا حد امکان به محل مبدأ برگردد؛ هزینهٔ خرید و موجودی واقعی فقط برای نقش مجاز افشا شود. | Inventory + Approvals/Buffet | P1 / بحرانی | پیاده‌سازی نشده است. |
| REQ-BUF-001 | Order چرخهٔ مشخص Draft → Confirmed → Paid/OnAccount → Cancelled/Refunded داشته باشد. فروش مستقل، متصل به Session یا متصل به Customer Account طبق state معتبر Server انجام شود؛ اضافه‌کردن بوفه از چند مسیر UI برای یک مشتری Invoice دوم یا حساب مقصد اشتباه نسازد. فروش، invoice/payment، stock و audit مرز اتمیک لازم داشته باشند. | Buffet + Inventory/Billing/Customers/Sessions | P1 / بحرانی | ماژول Buffet حاضر نیست؛ مسیرهای Pending/Debt و محل موجودی از regressionهای Repo 2 هستند. |
| REQ-INV-003 | حداقل موجودی، هشدار کمبود، خرید/بهای تمام‌شده و سود فروش فقط بر اساس دادهٔ معتبر گزارش شوند. «+ محصول»، اصلاح، انتقال و برگشت باید عملیات backend واقعی باشند؛ UI نمایشی یا mock مجاز نیست. | Inventory/Buffet + Reports/Permissions | P1 / زیاد | پیاده‌سازی نشده است. |

## ۷) گزارش، شیفت، تنظیمات و تأیید

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-RPT-001 | گزارش‌های درآمد/Session/مشتری/VIP/Wallet/بوفه/موجودی/operator/audit از Server ledger و state مرجع تولید شوند. بازهٔ زمانی و filter سمت Server اجرا شود؛ تاریخ نمایش فارسی/شمسی و مرز business day ثابت باشد. درآمد نقد/کارت از Wallet/Gift transfer جدا بماند. | Reports + read models همهٔ ماژول‌ها | P1 / زیاد | ماژول Reports حاضر نیست. |
| REQ-SHIFT-001 | شیفت دارای opening cash، cash/card sales، top-up واقعی، refund، discount، expense، expected cash، counted cash، difference و handover باشد. بستن شیفت summary پایدار ایجاد و شیفت را قفل کند؛ اصلاح پس از بستن فقط با permission/audit انجام شود. Payroll کامل جداگانه و تا تصمیم صریح P2 است. | Shifts/Reports + Billing/Audit | P1 / زیاد | ماژول Shift حاضر نیست. |
| REQ-SET-001 | تنظیمات مؤثر بر رفتار Server (تعرفه، policy مشتری/Agent، شبکه، backup، update، صدا/هشدار و localization) Server-backed، validateشده، versioned و auditشده باشد. UI نباید مقدار local-only را به‌عنوان تنظیم سراسری نشان دهد. | Settings + هر مالک تنظیم | P1 / زیاد | تنظیمات تجاری کامل حاضر نیست. |
| REQ-APR-001 | Approval برای عملیات مشخص و پرخطر، با requester، approver، reason، expiry/state و audit باشد؛ درخواست‌کننده نتواند خودش approval لازم را دور بزند. | Approvals + Identity/Audit/مالک use case | P1 / بحرانی | ماژول Approvals حاضر نیست. |

## ۸) تجربهٔ کاربر، فارسی‌سازی و عملیات انتشار

| شناسه | نتیجه و معیار پذیرش | مالک و وابستگی | اولویت / ریسک | وضعیت در Repo 5 |
|---|---|---|---|---|
| REQ-UX-001 | تجربهٔ اصلی Desktop فارسی fa-IR و RTL با اعداد/پول تومان و تاریخ نمایشی شمسی باشد؛ منابع en-US هم سالم بمانند. خطاهای خام exception/HTTP به اپراتور نمایش داده نشوند؛ پیام‌ها عملی و فارسی باشند. | Desktop/Shared + Domain-specific error mapping | P0 / زیاد | پوسته و منابع localization وجود دارد؛ پوشش همهٔ صفحات محصول هنوز کامل نیست. |
| REQ-UX-002 | Dashboard Native WPF و قابل استفاده با فهرست/گروه‌بندی سریع ایستگاه‌ها باشد؛ PC و timed station از نظر کنترل و نمایش رفتار متفاوت درست داشته باشند. UI هر عملیات مالی/Agent را از API واقعی می‌گیرد؛ success optimistic بدون نتیجهٔ Server ممنوع. | Desktop + Server contracts | P0 / زیاد | پوستهٔ پایه هست؛ صفحات تجاری کامل نیستند. |
| REQ-UX-003 | Customer Client/Shell (در صورت فعال‌شدن) Game Library، وضعیت Session، زمان معتبر، Wallet/Gift/Debt، پیام اپراتور و درخواست‌های مجاز را نمایش دهد. اجرای بازی/account و command فقط از Server/Agent policy عبور کند؛ مشتری به Windows و credentialهای حساس دسترسی بیشتر نگیرد. | ClientControl/Agent + Games/Customers/Sessions | P1 / بحرانی | تجربهٔ end-user کامل در snapshot حاضر نیست. |
| REQ-OPS-001 | Backup قابل‌بررسی ایجاد شود و restore در دیتابیس ایزوله واقعاً اجرا و صحت schema/data تأیید شود. Retention، data root، failure/restore log و RPO/RTO مستند باشند؛ داشتن فایل backup بدون آزمون restore کافی نیست. | Backup/Release Operations | P0 برای release / بحرانی | primitiveها و شواهد Foundation قبلی وجود دارد؛ گواهی نهایی نسخه نیاز است. |
| REQ-OPS-002 | Health و readiness جدا باشند؛ readiness به PostgreSQL/وابستگی‌های الزامی حساس باشد. Structured log، correlation، diagnostics، وضعیت Agent/lease و reconciliation برای پشتیبانی فراهم باشد؛ diagnostic failure نباید خطای اصلی را mask کند. | Observability/Agents/Server | P0 / زیاد | Foundation موجود؛ پوشش هر ماژول باید افزوده شود. |
| REQ-REL-001 | نصب تازه Server/Desktop/Agent روی Windows، LAN binding/port/firewall، Windows Service start/recovery، secret provisioning، writable data root خارج از پوشهٔ محافظت‌شدهٔ نصب و health smoke اثبات شود. رمز یا registration token در repo/package ثابت نباشد. | Installer/Platform Release + Security | P0 پیش از بهره‌برداری / بحرانی | gate نصب/نسخهٔ نهایی هنوز بسته نیست. |
| REQ-REL-002 | Update با manifest نسخه‌دار، hash/signature مورد تأیید، سازگاری Server/Desktop/Agent/API/schema، stage/health check، repair و rollback تست شود. DB/data از فایل‌های نصب جدا بمانند؛ migration rollback باید سیاست روشن داشته باشد و rollback فایل نباید ادعای برگشت خودکار دادهٔ مخرب را ایجاد کند. | Release/Installer/Database | P0 پیش از بهره‌برداری / بحرانی | زیرساخت/طراحی پایه وجود دارد؛ smoke واقعی نصب/ارتقا/rollback لازم است. |
| REQ-REL-003 | release تنها با نسخه و SHA معین، خروجی نصب، migration artifact، SBOM/review، گواهی Windows/PostgreSQL/Agent/Desktop، backup/restore، تست فیزیکی و بستهٔ شواهد اعلام شود. CI سبز جایگزین تست فیزیکی یا گواهی آخرین SHA نیست. | Release Engineering + همهٔ تیم/ماژول‌ها | P0 برای ادعای release / بحرانی | محصول release-certified نیست. |

## ۹) توسعه‌های بعدی و مرز نسخهٔ اول

| شناسه | قابلیت | تصمیم و شرط ورود به برنامه | مالک آینده | وضعیت |
|---|---|---|---|---|
| REQ-LATER-001 | رزرو و Waitlist | P2؛ پس از هستهٔ Session و Station. رزرو باید ظرفیت/بازه/لغو/no-show و queue ordering داشته باشد؛ به مرجع جداگانه‌ای برای مالکیت Station تبدیل نشود. | Reservations + Stations | Deferred |
| REQ-LATER-002 | Tournament/مسابقات | P2؛ نیاز به برنامهٔ محصول و flow مالی/ثبت‌نام مستقل دارد. | Tournaments | Deferred |
| REQ-LATER-003 | پنل/اپ موبایل مدیر، SMS/تبلیغات، چند شعبه و multi-tenant/SaaS | خارج از نسخهٔ اول تک‌شعبه‌ای؛ فقط با نیاز صریح، threat/operations review و ADR مجدداً وارد شود. | محصول/Platform | Deferred |
| REQ-LATER-004 | CCBOOT/PXE، نصب انبوه/patch بازی‌ها، چاپگر حرارتی، QR/درگاه پرداخت بیرونی، payroll کامل و کنترل سخت‌افزار میز | قابلیت‌های بالقوه‌اند اما نباید قبل از وجود سخت‌افزار، قرارداد، مجوز، integration test و سناریوی نصب واقعی در UI عرضه شوند. | Platform/Integration یا ماژول جدید | Deferred |

## ۱۰) خطاهای قدیمی که به آزمون بازگشتی دائمی تبدیل می‌شوند

| الگوی شکست مشاهده‌شده | کنترل لازم در Repo 5 | آزمون پذیرش/بازگشتی که باید وجود داشته باشد |
|---|---|---|
| Transfer فقط StationId را عوض می‌کند و Agent/Login/lease عقب می‌مانند؛ دو انتقال مقصد واحد را می‌گیرند | transfer use case اتمیک + constraints/fencing | رقابت دو transfer؛ سازگاری Station/Session/CustomerLogin/Agent/lease؛ دقیقاً یک owner معتبر |
| Customer login/session به PC اشتباه نسبت داده می‌شود یا دو هویت موازی ساخته می‌شود | identity contract واحد و مالکیت Server-side | login درست، login غیرمجاز، دستگاه اشتباه، چند session مجاز و ناسازگار |
| PendingPayment و Debt یا چند Invoice مشتری با هم قاطی می‌شوند | AccountState و payment allocation صریح؛ هر payment مرجع دارد | Pending→Debt، Debt قبلی همراه Pending، چند Session و پرداخت یک Session بدون مصرف ماندهٔ Session دیگر |
| Wallet/Gift به شکل مبلغ نقدی وارونه یا دوباره درآمد شمرده می‌شود | ledger با نوع/منبع/reference و Money دقیق | مثال ۱۰۰٬۰۰۰ + ۱۰٪ gift؛ تطبیق Wallet، Finance، Shift و Report |
| Warehouse/Showcase یا Reverse از محل غلط موجودی را تغییر می‌دهد | stock-location ledger و source reference | ورود→انتقال→فروش→برگشت→شمارش؛ بدون موجودی منفی یا سود/فروش تکراری |
| race در Session/Station/Wallet/Inventory یا retry تکراری | transaction coordinator، idempotency با request hash، concurrency rule | دو درخواست هم‌زمان؛ همان key/same payload replay؛ same key/different payload conflict؛ ledger دقیقاً یک‌بار |
| SignalR connection lifecycle یا reconnect وضعیت stale نشان می‌دهد | Server snapshot + heartbeat/lease/fencing + reconciliation | قطع/بازگشت شبکه، Agent restart، Server restart و رد command/token منقضی |
| UI/mock به‌جای عمل واقعی success می‌گوید | حذف mock از production path؛ نتیجهٔ واقعی typed از Server/Agent | production scan برای mock/fake؛ failure command باید با خطای فارسی و بدون state جعلی نمایش داده شود |
| giant file و workflow پرهزینه مسئولیت‌های متعدد را مخلوط می‌کند | source-size/architecture guard و canonical scripts | guard عمداً روی violation fail کند؛ تغییر docs-only workflow لازم را بی‌دلیل اجرا نکند اما فایل config/runtime را از CI جا نیندازد |
| migration/model drift یا startup migration خطرناک | migrations نسخه‌دار، snapshot یکسان، clean/upgrade gate، no automatic production migration | apply همهٔ migrationها از صفر، upgrade قبلی، pending-model check و schema comparison |
| setup/service/data-root/secret یا LAN bind نادرست است | release pipeline و provisioning روشن | نصب fresh روی Windows تمیز، service auto-start/recovery، API از LAN، secrets غیرثابت، data/backup قابل‌نوشتن |
| CI موفق اما قابلیت واقعی/تست فیزیکی انجام نشده است | وضعیت requirement از code/test/evidence تفکیک شود | Desktop↔Server + دو تا سه PC واقعی، قطع شبکه/restart/restore؛ بعد scale pilot به حدود ۱۵ PC |

## ۱۱) Definition of Ready / Done

**قبل از شروع کار (Ready):** شناسهٔ REQ، کاربر/actor، نتیجهٔ تجاری، owner، منبع دادهٔ authoritative، dependencies، permission، state transition، transaction/idempotency/concurrency، رفتار failure/recovery، API/Desktop/Agent contract، risk و معیارهای قابل‌آزمون مشخص باشند. سؤال باز مؤثر روی پول، امنیت، مالکیت یا release نباید ضمن کدنویسی به تصمیم پنهان تبدیل شود.

**برای Done:** پیاده‌سازی واقعی Server/persistence، Desktop/Agent path در صورت نیاز، permission، audit، idempotency، concurrency، error mapping فارسی، migrations، tests (unit/integration/contract/E2E متناسب)، observability، update/recovery اثرپذیر و evidence روی SHA دقیق بررسی شود. برای release، تست Windows/PostgreSQL/Agent و physical gate موردنیاز است. هر الزام ناتمام باید status و مدرک صریح داشته باشد؛ «ظاهر صفحه آماده است» معیار نیست.

## ۱۲) جمع‌بندی تصمیم‌های قطعی مرحلهٔ ۴

1. معماری محصول Repo 5 حفظ می‌شود: modular monolith Server + Native WPF Desktop + Windows Agent جدا + Shared contracts + PostgreSQL. پیشنهاد تاریخی React/SQLite در Repo 2 برای این محصول مرجع فنی نیست.
2. PC دارای Agent است؛ PS5 و فوتبال‌دستی دستگاه‌های زمان‌دار هستند و Agent ساختگی دریافت نمی‌کنند.
3. ledgerهای Wallet و Inventory، AccountStateهای Pending/Debt، Session ownership، Agent lease/fencing و Audit مرزهای غیرقابل‌چشم‌پوشی‌اند.
4. قابلیت‌های کمکی مثل Reservation، Tournament، SMS، mobile app، multi-shop، CCBOOT/PXE و payroll کامل به‌عنوان توسعهٔ بعدی باقی می‌مانند، نه mock یا دکمهٔ نیمه‌فعال در هسته.
5. وضعیت فعلی Repo 5 پس از Identity و Stations هنوز محصول کامل یا release-certified نیست؛ ماژول/آزمون جدید تا عبور گیت مربوط به همان SHA «نامزد پیاده‌سازی» می‌ماند.
6. هیچ کدی از Repo 2 یا Repo 3 در این مرحله merge نشد. این مرحله فقط نیازمندی و معیار پذیرش را تثبیت می‌کند.
