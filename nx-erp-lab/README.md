# Nx ERP Lab — دفترچهٔ بک‌اند محدود

**شروع:** `implementation-guide.html` را آفلاین در مرورگر باز کنید. نیاز به نصب برای خواندن ندارد. این پوشه یک راهنمای ساخت و starter recipe است؛ اپ کامل با APIهای تجاریِ آماده تحویل داده نشده است.

## محدوده

فقط محصولات/قطعات/کامپوننت‌ها، تأمین‌کنندگان و منابع تأمین موردنیاز مواد، BOM، برنامه‌ریزی مواد و بازرگانی فعلی (پروژهٔ اولیه + سربرگ سفارش). React فقط پوستهٔ health check است.

`scope.json` انتخاب تک‌به‌تک را مشخص می‌کند: **۴۲ جدول از ۱۸۰ جدول منبع**، شامل ۲۹ جدول دامنه و snapshot برنامه‌ریزی + ۱۳ پشتیبان. وجود مرجع کاربر/کارت/ارز مجوز ساخت کل آن ماژول نیست. هیچ گسترش خودکار بر پایهٔ FK وجود ندارد. دامنه‌های مالی، فروش، انبار کامل، سفارش خرید/تولید، What-if و مراحل آیندهٔ Trading خارج‌اند.

## محتوا

- `implementation-guide.html`: دفترچهٔ فارسی؛ فرمان‌ها، checkpointها، قرارداد API هدف، منطق و تست، فهرست جست‌وجوپذیر جدول‌ها، متن کامل فایل‌های starter و دانلود آفلاین Schema.
- `sources/`: هر دو HTML اصلی دست‌نخورده و JSON منتخب مشتق‌شده.
- `prisma/schema.prisma`: مدل آزمایش، نه migration اپ اصلی.
- `prisma/decisions.json`: منشأ، استنباط‌ها و تغییرهای صریح نسبت به منبع؛ قبل از migration مرور شود.
- `prisma/legacy-checks.sql` و `legacy-indexes.sql`: SQL مکمل برای بازبینی و افزودن به migration اولیه، نه اجرای کور روی اپ اصلی.
- `recipe/`: overlay روی scaffold تازهٔ Nx؛ اتصال DB، health، guard توکن موقت آزمایش، توابع دامنه و تست، React shell، Compose و helperها.
- `tools/`: بازتولید Schema و HTML، اعمال recipe و بررسی خروجی‌ها؛ مولدها DB را تغییر نمی‌دهند.
- `evidence/`: شواهد انتقال و گزارش بررسی‌ها. دادهٔ محرمانه/DB واقعی در این پوشه نیست.

## ساخت اپ آزمایش

دستورهای دقیق را به ترتیب فصل‌های ۵ تا ۱۲ HTML اجرا کنید. `workspace/` را خودتان با generator می‌سازید؛ پوشهٔ تولیدشده در این تحویل commit نشده است. تنظیمات مستقل تولید کنید؛ هرگز `.env` اپ اصلی را برای آزمایش کپی نکنید.

- Nx/plugins 23.3.0، Nest 11، Prisma/client/adapter-pg همگی 7.10.1، PostgreSQL 18.
- Node بررسی‌شدهٔ آزمایش 22.22.3 است؛ اپ قدیمی Node 26.x می‌خواهد.
- DB: `hadron_nx_lab`، پورت `5544`، Compose project: `hadron-nx-lab`، volume: `hadron-nx-lab-pg`.
- API: `3101`؛ web: `4201`، درخواست مرورگر نسبی `/api`.
- guard فقط service token توسعه است، نه ورود کاربر/RBAC و نه مجوز انتشار production.

## وضعیت اعتبارسنجی

Scaffold واقعی در مسیر موقت ساخته شد؛ Prisma validate/generate، build API/Web و ۲۴ تست نمونه در ۳ suite موفق. .NET، Docker، SQL migration و integration واقعی PostgreSQL در این محیط اجرا نشده‌اند. قراردادهای API تجاری در HTML مسیر ادامهٔ ساخت هستند، نه endpointهای پیاده‌شده. SQL، nullability/precision پیشنهادی و تفاوت‌های source/deployed باید قبل از استفادهٔ واقعی تأیید شوند.

برای بازتولید از ریشهٔ repository:

```bash
python nx-erp-lab/tools/build_schema.py
python nx-erp-lab/tools/build_guide.py
python nx-erp-lab/tools/check_deliverables.py
```

مولد Schema برای metadata تکمیلی legacy به snapshot داخل `main-erp/` نیاز دارد؛ متن منبع Schema همان Data Map است. ZIP همراه صرفاً برای خواندن و استفاده از recipe آماده است؛ برای بازتولید schema، clone کامل لازم است.

دریافت امن تغییرات و کنترل شاخه/working tree: `../docs/SAFE-UPDATE-FA.md`. شاخهٔ تحویل `arena/d5f0785f-hadron` است؛ بدون PR و بدون force.
