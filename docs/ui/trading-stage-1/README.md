# فقط مرحلهٔ اول بازرگانی

این بستهٔ محدود، خروجی **خود کامپوننت React اپ** است؛ نه فرم متفاوتی که جداگانه نگهداری شود. بستهٔ قبلی `trading-prototype` برای تحلیل کل مسیر بود؛ این پوشه فقط فرم مرحلهٔ اول را پوشش می‌دهد.

## فایل‌ها

- `stage-1.html`: نسخهٔ ساده، بدون نام جدول‌ها.
- `stage-1-annotated.html`: همان فرم با مسیر ذخیرهٔ فعلی، معادل مدل پیشنهادی و منبع گزینه‌ها کنار هر فیلد.
- `hadron-logo-red.svg`: لوگوی رسمی موجود در پروژه.

ZIP را Extract کنید و فایل HTML موردنظر را باز کنید. فایل‌های HTML آفلاین‌اند؛ گزینه‌ها نمونه هستند و دکمهٔ ثبت در آن‌ها فقط پیش‌نویس مرورگر را ذخیره می‌کند، نه دیتابیس. نسخهٔ واقعی React در `/trading` و `/trading/projects/new` از API استفاده می‌کند.

## محدودهٔ دقیق فرم

۱. شمارهٔ داخلی — سیستمی و فقط‌خواندنی.
۲. تاریخ شروع / ارجاع — الزامی؛ تاریخ کاری و جدا از زمان ثبت سیستمی.
۳. موضوع پروژه — الزامی، حداکثر ۲۵۰ کاراکتر.
۴. مشتری — انتخاب از مشتری‌های فعال.
۵. کارت بازرگانی — انتخاب از کارت‌های فعال.
۶. مسئول پیگیری — الزامی؛ کاربر فعال از مرجع مرکزی هویت.
۷. وضعیت اولیه — DRAFT و فقط‌خواندنی، مطابق قرارداد دامنهٔ موجود.
۸. توضیحات — حداکثر ۴۰۰۰ کاراکتر.

اولویت طبق درخواست قبلی از فرم حذف شده است. عنوان انگلیسی، تأمین‌کننده، تاریخ هدف، خلاصهٔ کالا، کد هشت‌رقمی، قفل، ارز، بیمه، حمل، گمرک، تعهد و هزینه در این فرم نیستند. کل مدل پیشنهادی ۳۳جدولی به مرحلهٔ اول تحمیل نشده است. وضعیت «در انتظار ثبت سفارش» تصویر بدون فرمان انتقال به‌جای DRAFT قرار داده نشده است.

## ساخت مجدد از کامپوننت اپ

از ریشهٔ ریپو، پس از نصب وابستگی‌های frontend:

```bash
npm run export:trading-setup --prefix src/frontend
```

مولد `src/frontend/scripts/exportTradingSetup.mjs` کامپوننت `TradingProjectSetupView` را با React SSR رندر می‌کند؛ همان `Section` و `FormField` و همان CSS اپ را استفاده می‌کند. تنها تفاوت دو خروجی، روشن بودن annotationهاست. فایل ZIP در `output/trading-stage-1.zip` نوشته می‌شود.

تعریف مشترک هشت فیلد: `src/frontend/src/tradingProjectSetupFields.json`.

## ذخیرهٔ واقعی و تفاوت با طرح ۳۳جدولی

برای جلوگیری از بازطراحی خارج از مرحلهٔ اول، زیرساخت اجرایی فعلی حفظ شده است:

| داده | مسیر فعلی اپ | معادل در مدل پیشنهادی |
|---|---|---|
| پرونده | `trading.projects` | `trade_projects` |
| تاریخ شروع / ارجاع | `trading.projects.StartDate` | `project_stages.startedAt` برای PROJECT_SETUP |
| مشتری | `trading.projects.CustomerId` → `trading.reference_values` از نوع CUSTOMER | `trade_projects.customerPartyId` → `trade_parties` |
| کارت | `trading.projects.BusinessCardId` → `trading.reference_values` از نوع BUSINESS_CARD | `trade_projects.businessCardId` → `business_cards` |
| مسئول | `trading.projects.ResponsibleUserId` → `identity.users.Id` | `trade_projects.responsibleUserId` |

بنابراین annotationها عمداً «فعلی» و «پیشنهادی» را جدا نشان می‌دهند. نام‌های مدل پیشنهادی، نام جدول‌های نصب‌شدهٔ فعلی نیستند. ساخت TradeParty، BusinessCard کامل و ProjectStage عمومی، خارج از این تغییر است.

Migration افزودنی `20261018120000_AddTradingProjectSetup` فقط هفت ستون nullable (تاریخ، دو شناسه و چهار snapshot نام/کد)، دو FK با حذف Restrict و دو index به `trading.projects` اضافه می‌کند. تاریخ/مسئول برای رکوردهای تاریخی جعل یا backfill نشده است. این migration در محیط این سشن اجرا نشده است.

## API مرحلهٔ اول

هر سه endpoint با مجوز موجود `trading.project.manage` محافظت می‌شوند:

- `GET /api/trading/project-setup/lookups?kind=CUSTOMER|BUSINESS_CARD|ASSIGNEE&search=&page=1&pageSize=25`
- `POST /api/trading/project-setup/resolve-jalali-date`
- `POST /api/trading/project-setup`

بدنهٔ ثبت فقط `titleFa`, `startDate`, `customerId`, `businessCardId`, `responsibleUserId`, `notes` دارد. شمارهٔ رسمی، وضعیت اولیه، اولویت ذخیره‌شدهٔ سازگاری و انجام‌دهنده از فرم دریافت نمی‌شوند. پرونده و ممیزی در یک SaveChanges ذخیره می‌شوند؛ هیچ ثبت سفارش یا عملیات بعدی به‌صورت خودکار ساخته نمی‌شود.

جست‌وجو و صفحه‌بندی هر انتخاب‌گر مستقل است؛ مشتری‌ها زیر محدودیت مجموع مرجع‌های قدیمی گم نمی‌شوند. نام مسئول از قرارداد read-only متعلق به Identity می‌آید؛ اطلاعات مدیریتی حساب، ایمیل و رمز عبور افشا نمی‌شود. تغییر نام مرجع، snapshot تاریخی پرونده را بازنویسی نمی‌کند.

تبدیل شمسی از همان `IPlanningCalendar` موجود استفاده می‌کند ولی endpoint مخصوص مجوز بازرگانی دارد؛ کاربر بازرگانی مجبور به داشتن مجوز اجرای MRP نیست. الگوریتم تقویم جدید ساخته نشده است. در زبان انگلیسی تاریخ استاندارد میلادی وارد می‌شود.

قرارداد قدیمی `POST /api/trading/projects` برای سازگاری مصرف‌کننده‌های موجود حفظ شده؛ فرم جدید فقط فرمان محدود `project-setup` را صدا می‌زند. عملیات مراحل بعد تغییری نکرده‌اند. اطلاعات ذخیره‌شدهٔ مرحلهٔ اول در صفحهٔ خود پرونده نیز نمایش داده می‌شود.

## راه‌اندازی محلی

قبل از استفاده از ستون‌های جدید، migration باید روی دیتابیس محلی اعمال شود. `run-hadron.bat` مرحلهٔ build و اجرای migration دارد؛ خطا یا هشدار آن مرحله نباید نادیده گرفته شود.

اجرای دستی از ریشهٔ ریپو، با SDK و تنظیمات دیتابیس پروژه:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/backend/Hadron.Erp.Api --startup-project src/backend/Hadron.Erp.Api
```

برای دادهٔ واقعی، مشتری و کارت فعال باید از تنظیمات بازرگانی و کاربران فعال از مدیریت هویت موجود باشند؛ دادهٔ ساختگی به اپ واقعی تزریق نشده است. تهیهٔ backup و روال انتشار دیتابیس طبق قواعد پروژه انجام شود.

## بررسی‌ها

- build TypeScript/Vite: موفق.
- ۳۲۰ تست frontend: موفق.
- ESLint فایل‌های تغییرکردهٔ فرم: بدون خطا یا هشدار.
- تست مرورگر روی build واقعی اپ با API mock: فقط فیلدهای مرحلهٔ اول، نبود annotation در اپ، حفظ فرم هنگام تغییر گام، retry و صفحه‌بندی انتخاب‌گر، حفظ انتخاب، تبدیل تاریخ از قرارداد مخصوص بازرگانی، CSRF، حفظ ورودی در خطای اعتبارسنجی، انتقال به صفحهٔ پرونده بعد از موفقیت و نمایش داده‌های ذخیره‌شده.
- کاربر دارای فقط مجوز مشاهده می‌تواند وارد فضای بازرگانی شود اما فرم ایجاد و lookup مسئول را دریافت نمی‌کند؛ در تست مرورگر بررسی شد.
- تطبیق هر دو HTML با هشت فیلد همان کامپوننت و نمایش موبایل: موفق.
- پنج تست integration برای backend اضافه شده: ذخیره/ممیزی/snapshot، اعتبارسنجی، lookup و paging، مجوز و تبدیل تاریخ بدون مجوز MRP.
- **تست backend، build .NET و اجرای migration اینجا انجام نشده‌اند؛ SDK `dotnet` در محیط نصب نیست.** قبل از انتشار باید اجرا شوند. تست مرورگر mock جای تست PostgreSQL را نمی‌گیرد. migration/target model/snapshot باید با ابزار EF در محیط .NET هم بررسی شوند.

```bash
npm test --prefix src/frontend
npm run build --prefix src/frontend
CHROMIUM_PATH=/path/to/chrome node scripts/trading-prototype/test-app-stage1.mjs
dotnet test tests/backend/Hadron.Erp.IntegrationTests/Hadron.Erp.IntegrationTests.csproj --filter FullyQualifiedName~TradingTests
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/backend/Hadron.Erp.Api --startup-project src/backend/Hadron.Erp.Api
```

تست مرورگر به `playwright-core` در پوشهٔ `scripts/trading-prototype` نیاز دارد (`npm ci --prefix scripts/trading-prototype`). محیط integration باید PostgreSQL تست مستقل و متغیرهای اتصال تست پروژه را داشته باشد؛ تست‌ها را روی دیتابیس عملیاتی اجرا نکنید.

تغییرات این سشن روی `arena/d5f0785f-hadron` است. برنچ `arena/a9fe3573-hadron` و کامیت `620c750` فقط مرجع نمای شش‌گامی بوده‌اند. PR ساخته نمی‌شود.
