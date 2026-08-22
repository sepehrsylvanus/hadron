# Hadron ERP — MRP Business Scenario

**Document Type:** Business Requirement Source  
**Module:** MRP  
**Owner:** Hadron Tavan  
**Status:** Source Scenario / Business Baseline  

> این سند خواسته و منطق کسب‌وکاری MRP را ثبت می‌کند. هدف آن حفظ سناریوی اصلی کسب‌وکار است، نه تحمیل معماری نرم‌افزار. طراحی فنی نهایی در `MRP-SYSTEM-SPEC.md` انجام می‌شود.

---

## 1. هدف MRP

MRP برای پشتیبانی از فرآیند تولید هادرون ایجاد می‌شود تا شرکت بتواند:

- نیاز مواد و قطعات را از قبل پیش‌بینی کند؛
- تأمین کالا را بدون نقص و قابل پیگیری انجام دهد؛
- زمان‌بندی تأمین، خرید، حمل، گمرک و دریافت را کنترل کند؛
- برنامه‌ریزی مالی و Cash Flow را روی Timeline انجام دهد؛
- خطای انسانی و وابستگی به Excel/Google Sheets را به حداقل برساند؛
- داده‌ها را قابل Audit، Automation و پیگیری کند.

سیستم قبلی سال‌ها به‌صورت دستی در Excel و سپس Google Sheets کار کرده است. ERP باید همان منطق کسب‌وکار را به یک سیستم پایدار، قابل تست و قابل توسعه تبدیل کند.

---

## 2. List / Reference Data

در سیستم قبلی Sheet با عنوان `List` موتور Dropdownها و یکسان‌سازی اصطلاحات بوده است.

پیشنهاد کسب‌وکاری این است که هر Module در ERP Reference Data موردنیاز خود را داشته باشد و در صورت نیاز توسعه پیدا کند.

Baseline Listها:

- Country / کشور
- Payment Term / شرایط پرداخت
- Credit Days / روزهای اعتبار
- Score 1–10 / امتیاز ۱ تا ۱۰
- Active Status / وضعیت فعال
- Product Category / گروه محصول
- Component Category / گروه قطعه
- Unit / واحد
- Shipping Term / روش حمل
- Component Count / تعداد قطعات
- Contact Person / شخص تماس
- Hadron Owner / مسئول هادرون
- Product Name / نام محصول
- Component Name / نام قطعه
- Supplier Name (Auto) / نام تأمین‌کننده خودکار
- Product Filter (Auto) / فیلتر محصول
- Supplier Filter (Auto) / فیلتر تأمین‌کننده
- Owner Filter (Auto) / فیلتر مسئول
- Order Status / وضعیت سفارش
- Business Unit / واحد سازمانی

---

## 3. Product Master

Product Master برای نگهداری مشخصات پایه محصول نهایی استفاده می‌شود.

Baseline fields:

- Product ID / شناسه محصول
- Product Code / کد محصول
- Product Name (FA) / نام فارسی محصول
- Product Name (EN) / نام انگلیسی محصول
- Product Category / گروه محصول
- Safety Stock Qty / موجودی اطمینان
- Active Status / وضعیت فعال
- Notes / توضیحات
- Critical Days / حد قرمز
- Safe Start Days / شروع محدوده امن
- Excess Start Days / شروع مازاد موجودی

---

## 4. Supplier Master

Supplier Master برای نگهداری اطلاعات تأمین‌کننده و شرایط همکاری استفاده می‌شود.

Baseline fields:

- Supplier ID / شناسه تأمین‌کننده
- Supplier Name / نام تأمین‌کننده
- Country / کشور
- Contact Person / شخص تماس
- Hadron Follow-up Owner / مسئول پیگیری هادرون
- Payment Term / شرایط پرداخت
- Advance % / درصد پیش‌پرداخت
- Before Shipment % / درصد قبل از حمل
- Credit % / درصد اعتباری
- Credit Days / روزهای اعتبار
- Lead Time Score / امتیاز خوش‌قولی
- Quality Score / امتیاز کیفیت
- Active Status / وضعیت فعال
- Notes / توضیحات

---

## 5. Part Master

در تعریف کسب‌وکاری هادرون، `Part` قطعه‌ای است که **مستقیماً در Final Assembly Line محصول استفاده می‌شود**.

تمام نام‌هایی که در ساختار قدیمی برای قطعات مستقیم مونتاژ تحت عنوان Component استفاده شده‌اند، در ERP جدید باید در مفهوم **Part** قرار گیرند.

Baseline fields پیشنهادی کسب‌وکاری:

- Part ID / شناسه قطعه
- Part Code / کد قطعه
- Part Name / نام قطعه
- Category / گروه
- Unit / واحد
- Net Weight (kg) / وزن خالص
- Gross Weight (kg) / وزن ناخالص
- Image URL / لینک عکس
- Scrap % / ضایعات
- MOQ / حداقل سفارش
- Lead Time (Days) / زمان تأمین
- Supplier / تأمین‌کننده
- Shipping Term / روش حمل
- Shipping Period (Days) / مدت حمل
- Customs Period (Days) / مدت گمرک
- Forwarder to Shipment (Days) / فورواردر تا حمل
- Active Status / وضعیت فعال
- Initial Inventory Qty / موجودی اولیه
- Notes / توضیحات
- Purchase Currency / ارز خرید مانند IRR, CNY, AED
- Purchase Price / قیمت خرید

---

## 6. Component Master

در تعریف کسب‌وکاری هادرون، `Component` ورودی ساخت یک Part یا Subassembly است و الزاماً مستقیم وارد Final Assembly محصول نمی‌شود.

مثال:

```text
PCB + LED + Capacitor + IC + ...
                ↓
             PCBA Part
                ↓
           Final Product
```

Componentها نیز اطلاعاتی مشابه Part دارند، اما Timeline تأمین آن‌ها زودتر شروع می‌شود؛ چون ابتدا Componentها باید تأمین و سپس به Part تبدیل شوند.

سیستم باید بتواند تشخیص دهد که برای سفارش/ساخت یک Part مانند PCBA، زنجیره تأمین Components قبلی نیز باید برنامه‌ریزی شود.

---

## 7. BOM Master

کاربر Product را انتخاب می‌کند و Parts تشکیل‌دهنده آن را به همراه ضریب مصرف مشخص می‌کند.

در مدل قدیمی ابتدا تعداد Parts انتخاب می‌شد و سپس همان تعداد Row باز می‌شد.

برای هر BOM Line حداقل موارد زیر لازم است:

- Product / Output
- Part / Input
- Usage Qty / ضریب مصرف

برای Parts ساخته‌شده از Components نیز ساختار مشابه لازم است.

---

## 8. Forecast and Actual Production

سیستم باید افق زمانی بلندمدت را نگهداری کند، ولی فقط Range انتخاب‌شده را نمایش دهد.

کاربر می‌تواند مثلاً سال/ماه شمسی `1405/05` را انتخاب کند و معادل میلادی نیز نمایش داده شود.

محصولات Active که BOM دارند نمایش داده می‌شوند و برای هر Product حداقل دو مقدار وجود دارد:

- Forecast
- Actual

### قاعده زمانی کسب‌وکار

- ماه‌های آینده بر اساس Forecast برنامه‌ریزی می‌شوند.
- ماه جاری بر اساس `Forecast - Actual Production To Date` برنامه‌ریزی می‌شود.
- ماه‌های گذشته برای Planning بر اساس Actual نهایی می‌شوند.
- Forecast تاریخی نباید حذف شود؛ برای Forecast Accuracy نگهداری می‌شود.

### Daily Production Actual

در Module تولید آینده، تولید واقعی هر روز ثبت خواهد شد:

```text
Date + Product + Actual Production Qty
```

Monthly Actual از مجموع Daily Actual به دست می‌آید.

برای ماه جاری:

```text
Remaining Forecast = MAX(Monthly Forecast - Actual Production To Date, 0)
```

---

## 9. Mapping / Material Planning Timeline

Mapping محل مشاهده و تحلیل Timeline مصرف، تأمین، موجودی و Coverage است.

در مدل قدیمی Calculation روزانه بوده و هر Row یک روز را نمایش می‌داده است.

اطلاعات موردنیاز کسب‌وکاری برای هر Material شامل:

1. Daily Consumption / مصرف روزانه
2. Warehouse Receipt / دریافت انبار
3. Order Status / وضعیت سفارش
4. Inventory / موجودی
5. Coverage Days / روزهای پوشش

### Shared Material

اگر یک Part در چند Product استفاده شود، Demand آن باید به‌صورت ترکیبی از همه Parentها محاسبه شود. موجودی در حالت عادی Shared است، مگر اینکه در آینده Allocation صریح تعریف شود.

---

## 10. Daily vs Weekly Planning

تصمیم کسب‌وکاری/معماری فعلی:

- Calculation Engine روزانه باشد.
- UI بتواند Daily / Weekly / Monthly نمایش دهد.
- Weekly و Monthly فقط Aggregation View باشند و منطق Planning را تغییر ندهند.

---

## 11. Inventory Concepts

دو مفهوم باید از هم جدا باشند:

### Actual On Hand

موجودی واقعی که از Transactionهای انبار به دست می‌آید.

### Projected On Hand

موجودی پیش‌بینی‌شده برای Planning که از Demand و Firm Incoming Supply محاسبه می‌شود.

Mapping نباید Projected Inventory را با عنوان موجودی واقعی نمایش دهد.

---

## 12. Coverage

Coverage باید نشان دهد Material چند روز از Demand آینده را پوشش می‌دهد.

تقسیم ساده Inventory بر مصرف امروز کافی نیست، چون Demand آینده متغیر است.

دو KPI موردنیاز:

- Stock Coverage
- Firm Supply Coverage

Coverage باید Future Demand را به‌ترتیب زمانی مصرف کند تا تاریخ اتمام یا تعداد روز پوشش مشخص شود.

---

## 13. Order Planning

Order Planning بخش اصلی تصمیم‌گیری MRP است و باید بسیار User Friendly باشد.

سیستم باید با بررسی کامل:

- Forecast
- Actual
- BOM
- Inventory
- Open Orders
- Firm Incoming Supply
- Lead Times
- MOQ
- Supplier
- Price
- Currency

پیشنهاد دهد که:

- چه Itemی نیاز به اقدام دارد؛
- چه زمانی باید سفارش داده شود؛
- چه مقدار سفارش داده شود؛
- چرا این Recommendation ایجاد شده است.

Baseline output fields:

- انتخاب
- شناسه قطعه
- نام قطعه
- تأمین‌کننده
- مسئول پیگیری
- وضعیت تأمین
- اولویت
- اقدام بعدی
- آخرین تاریخ سفارش
- موجودی تاریخ گزارش
- دریافت قطعی آینده
- مانده سفارش‌های باز
- تقاضا تا افق تصمیم
- تاریخ احتمالی اتمام
- روزهای پوشش
- زمان تأمین کامل
- حداقل سفارش
- مقدار پیشنهادی سامانه
- تصمیم کارشناس
- مقدار نهایی کارشناس
- دلیل تغییر یا رد

Machine Recommendation و Expert Decision باید جدا باقی بمانند.

---

## 14. Purchase Order

Recommendation تأییدشده توسط کارشناس به Draft Purchase Order تبدیل می‌شود.

Baseline statuses:

- Draft
- Approved
- Ordered
- Shipped
- Customs
- Partially Received
- Received
- Cancelled

### Firm Supply Rule

این Statusها Firm Supply نیستند:

- Draft
- Approved
- Cancelled

از `Ordered` به بعد سفارش در محاسبات MRP به‌عنوان Firm Incoming Supply در نظر گرفته می‌شود.

`Partially Received` فقط مقدار باقی‌مانده را Firm Incoming Supply نگه می‌دارد.

`Received` دیگر Incoming نیست و باید به Actual Inventory منتقل شده باشد.

---

## 15. Purchase Order Detail

PO باید بتواند تمام اطلاعات عملیاتی و مالی مرتبط با سفارش را نگهداری کند، از جمله:

- Item
- Supplier
- Quantity
- Price
- Currency
- Payment Terms
- Responsible Person
- Dates
- Status
- Shipment/Customs/Receipt Milestones
- Received Qty
- Remaining Qty
- Notes

Mapping باید Status را از PO و Moduleهای مرتبط بخواند؛ Mapping منبع اصلی Status نیست.

---

## 16. Planned vs Actual Lead Time History

برای تمام مراحل مهم تأمین باید از روز اول Planned و Actual ثبت شوند.

نمونه مراحل:

- Supplier Production
- Supplier Ready
- Forwarder Pickup/Handover
- Forwarder to Shipment
- Shipment Departure
- Transit
- Arrival
- Customs Entry
- Customs Clearance
- Domestic Transport
- Warehouse Receipt
- Internal Conversion/Assembly where applicable

برای هر مرحله باید در حد نیاز نگهداری شود:

- Planned Date
- Actual Date
- Planned Duration
- Actual Duration
- Variance

تغییر Planning Parameterهای آینده نباید تاریخ Planned/Actual تاریخی POهای قبلی را تغییر دهد.

---

## 17. Lead Time Learning

هدف این است که با تکرار پروژه‌ها، Lead Time Planning دقیق‌تر شود.

سیستم در آینده باید بتواند از History معیارهایی مانند این‌ها را بسازد:

- Sample Count
- Average Actual Lead Time
- Median Actual Lead Time
- Min / Max
- Recent Average
- P80 / P90 where useful
- Delay Frequency
- On-Time Percentage

اما Historical Data نباید خودکار Planning Parameter فعلی را overwrite کند.

سه مفهوم باید جدا باشند:

1. Configured Lead Time
2. Historical Actual Performance
3. System Recommended Lead Time
4. Expert Approved Lead Time

هر تغییر Planning Parameter باید قابل Audit باشد.

---

## 18. Lead Time Basis

تمام Lead Timeهای فعلی بر اساس **Calendar Days** هستند.

Working Day / Factory Calendar / Supplier Calendar می‌تواند در آینده اضافه شود، اما Requirement نسخه فعلی نیست.

---

## 19. Cash Flow Integration

Cash Flow یک Module مستقل آینده است و یکی از ورودی‌های اصلی آن MRP / Procurement / PO خواهد بود.

مثال:

```text
PO Total = 100,000 CNY
Advance = 30%
Before Shipment = 70%
```

Cash Flow باید بتواند Payment Milestoneها را بر اساس PO Amount، Currency، Payment Terms و Timeline ایجاد کند.

Original Currency باید حفظ شود.

---

## 20. Traceability Objective

سیستم باید در نهایت بتواند هر Recommendation و PO را به منبعش Trace کند:

```text
PO
↓
Expert Decision
↓
Machine Recommendation
↓
Projected Shortage
↓
Material Demand
↓
Forecast / Actual
↓
BOM
↓
Product
```

---

## 21. Accuracy Objective

هدف کسب‌وکار دستیابی به فرآیندی با بیشترین دقت ممکن، حداقل خطای انسانی و قابلیت کنترل کامل است.

این هدف باید از طریق:

- deterministic calculations
- validation
- audit
- golden tests
- data quality checks
- automation
- traceability

پیاده شود، نه با ادعای تضمین «صفر خطا» بدون کنترل و تست.
