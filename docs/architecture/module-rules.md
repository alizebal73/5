# Module Rules

Domain -> Shared
Application -> Domain + Shared + explicit contracts
Infrastructure -> Application + Domain + Persistence + Shared
Api -> Application + Shared
Composition -> registration only

Forbidden: EF/DbContext in Domain/Application/Api; Infrastructure from Domain; cross-module internal references; network/file/process side effects in Domain; authoritative calculations in clients.

## مرزهای سطح محصول

- Manager Dashboard و Customer Client دو سطح مستقل‌اند؛ Client به اسمبلی/پوسته/ViewModelهای Dashboard وابسته نمی‌شود و Dashboard نیز State محلی Client را منبع حقیقت قرار نمی‌دهد.
- ورود مشتری فقط دو مسیر دارد: Dashboard → انتخاب PC → شناسهٔ مشتری → تأیید؛ یا Client locked screen → username + password مشتری. PIN یا روش سوم فقط با تصمیم صریح Owner قابل افزودن است؛ پیش‌فرض وجود ندارد.
- «ورود مشتری» و «آزادسازی/خروج مدیریتی از قفل Client» دو Use Case جدا هستند. منطق و مجوز آزادسازی در Server/Application/Policy مالک مشخص دارد؛ ظاهر/کلید مخفی فقط آن را صدا می‌زند.
- Owner اعطای مجوزهای حساس به نقش/اپراتور را کنترل می‌کند. هر عملیات unlock نیازمند authorization سمت Server، Audit و فرمان Agent محدود به Station/credential/lease است؛ UI و Agent حق تعیین نتیجهٔ تجاری ندارند.
- قابلیت از Domain/Application/Contracts/Infrastructure ساخته و آزمون می‌شود؛ اتصال به View/Client/Dashboard از طریق پورت‌های پایدار در لایهٔ presentation انجام می‌شود. بازطراحی ظاهر نباید منطق، قرارداد مالی یا دسترسی را جابه‌جا کند.
