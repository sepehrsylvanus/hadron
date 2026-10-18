# Hadron ERP — MRP System Specification

**Document Type:** Authoritative System Specification  
**Module:** MRP  
**Status:** Baseline v1.0  
**Owner:** Hadron Tavan  
**Architecture:** Modular Monolith  
**Business Source:** `MRP-BUSINESS-SCENARIO.md`

> این سند مرجع اصلی طراحی و پیاده‌سازی MRP است. Feature Specs و Codex Prompts باید با این سند سازگار باشند. در صورت تعارض با Business Scenario یا Decision Log، پیاده‌سازی باید متوقف و تعارض گزارش شود.

---

# 1. System Purpose

MRP هادرون یک موتور تصمیم‌گیری عملیاتی برای تبدیل برنامه تولید به برنامه تأمین است.

سیستم باید زنجیره زیر را کنترل کند:

```text
Production Plan
→ Material Demand
→ Supply Position
→ Shortage Risk
→ Procurement Recommendation
→ Expert Decision
→ Purchase Order
→ Inbound Supply
→ Actual Receipt
→ Inventory Update
→ Financial Commitment
```

هدف، جایگزینی محاسبات پراکنده Excel/Google Sheets با فرآیندی سریع، deterministic، قابل Audit، قابل Test و قابل Automation است.

---

# 2. Design Principles

MRP باید:

- Business Logic را از Spreadsheet Structure جدا کند؛
- از محاسبات Cell-based و Formula-heavy اجتناب کند؛
- هر Recommendation را قابل Explain و Trace نگه دارد؛
- Historical Data را حفظ کند؛
- از Data Duplication جلوگیری کند؛
- برای Datasetهای بزرگ Server-side و Batch-oriented باشد؛
- برای اتصال آینده به Production, Inventory, Trading, Logistics, Finance و AI آماده باشد؛
- Calculation Engine اصلی را مستقل از AI نگه دارد.

---

# 3. Canonical Terminology

## 3.1 Product

محصول نهایی قابل تولید/فروش هادرون.

## 3.2 Part

Itemی که مستقیماً در Final Assembly یک Product مصرف می‌شود.

## 3.3 Component

Itemی که برای تولید یک Part یا Subassembly مصرف می‌شود و الزاماً مستقیم وارد Final Assembly نمی‌شود.

## 3.4 Item

مفهوم فنی مشترک برای Product / Part / Component.

---

# 4. Unified Item Model

## LOCKED

Data Model باید یک `Item` مشترک با Type داشته باشد:

```text
Product
Part
Component
```

UI می‌تواند صفحات جدا داشته باشد، ولی Database نباید سه مدل تکراری و مستقل ایجاد کند.

اهداف:

- Multi-Level BOM
- Shared attributes
- Data consistency
- Future extensibility

Item Type باید Business Meaning را حفظ کند و نباید باعث Generic/EAV Design شود.

---

# 5. Reference Data Architecture

Reference Data به سه دسته تقسیم می‌شود.

## 5.1 Shared ERP Reference Data

نمونه:

- Country
- Currency
- Unit
- Business Unit

## 5.2 MRP Controlled Reference Data

نمونه:

- Product Category
- Part Category
- Component Category
- Payment Term
- Active Status
- Shipping Mode
- Incoterm
- Order Status
- Planning Policy Type

## 5.3 Dynamic Master Data

موارد زیر List مستقل ذخیره نمی‌شوند و از Master Data Query می‌شوند:

- Product Name
- Part Name
- Component Name
- Supplier Name
- Product Filter
- Supplier Filter
- Owner Filter

---

## 5.4 Canonical Item Identity

`Item.Id` is the internal UUID technical identity. `Item.BusinessNumber` is the single Official Item Code used for user-facing search, sort and display:

```text
PRODUCT   → PRD-{sequence:D4}
PART      → PRT-{sequence:D4}
COMPONENT → CMP-{sequence:D4}
```

The Official Item Code is server-generated, globally unique and immutable. It is absent from Create/Edit input and shown read-only after creation. `Item.Code` and `Item.NormalizedCode` are deprecated for removal and must not form a second active identity. Before removal, non-null historical values are archived in migration/history-only `catalog.item_legacy_codes` (`ItemId`, `LegacyCode`, `LegacyNormalizedCode`, `ArchivedAtUtc`) with no UI, business-logic or search use.

---

# 6. Product Master

Product Master حداقل شامل:

- Technical Product ID
- Official Item Code (`Item.BusinessNumber`, `PRD-{sequence:D4}`)
- Product Name FA
- Product Name EN
- Product Category
- Active Status
- Notes

Product-specific Safety/Planning parameters می‌توانند وجود داشته باشند، اما Material Coverage Policy برای Shared Part/Component نباید مستقیماً از Product Threshold گرفته شود.

---

# 7. Planning Policy

## LOCKED

Part و Component باید Planning Policy مستقل داشته باشند.

Baseline parameters:

- Safety Stock Qty
- Critical Days
- Safe Start Days
- Excess Start Days

Policy باید روی Item مورد Planning اعمال شود.

اگر یک Item در چند Product مصرف شود، یک Policy مشترک Material Planning دارد مگر در آینده Allocation/Segmentation صریح تعریف شود.

---

# 8. Supplier Master

حداقل اطلاعات:

- Supplier ID
- Supplier Name
- Country
- Contact Person
- Hadron Follow-up Owner
- Payment Term
- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Lead Time Score
- Quality Score
- Active Status
- Notes

Payment Term Structure باید قابل Validation باشد.

Contact Person و Hadron Owner در آینده باید به People/User entities قابل اتصال باشند و نباید معماری به String آزاد دائمی محدود شود.

---

# 9. Sourcing Profile

## LOCKED

Supplier, Price, Currency, MOQ و Supplier-specific Lead Time نباید فقط داخل Part/Component Master ذخیره شوند.

یک Item می‌تواند چند Sourcing Profile داشته باشد.

Baseline conceptual fields:

- Item
- Supplier
- Currency
- Unit Price
- Valid From
- Valid To
- MOQ
- Supplier Lead Time
- Shipping Mode
- Incoterm
- Preferred Supplier
- Active

در صورت نیاز Route-specific timing نیز می‌تواند در همین Domain یا Entity مرتبط نگهداری شود.

---

# 10. Price and Commercial History

## LOCKED

Price و Commercial Terms باید Versioned باشند.

تغییر قیمت یا Payment Term جدید نباید اسناد تاریخی را تغییر دهد.

هنگام ایجاد PO حداقل موارد زیر Snapshot می‌شوند:

- Supplier
- Currency
- Unit Price
- Quantity
- Payment Terms
- Commercial Terms required for the PO

هیچ Price بدون Currency معتبر نیست.

Original Currency همیشه حفظ می‌شود.

---

# 11. Shipping Mode vs Incoterm

## LOCKED

`Shipping Mode` و `Incoterm` دو مفهوم جدا هستند.

Shipping Mode نمونه:

- Sea
- Air
- Road
- Internal

Incoterm نمونه:

- EXW
- FOB
- CFR
- CIF

این دو نباید در یک Dropdown یا Field مشترک ادغام شوند.

---

# 12. Part Master

Part یک Item از نوع `Part` است و مستقیماً در Final Assembly مصرف می‌شود.

Intrinsic/master attributes ممکن است شامل:

- Official Item Code (`Item.BusinessNumber`, `PRT-{sequence:D4}`)
- Name
- Category
- Unit
- Net Weight
- Gross Weight
- Image Reference
- Default Scrap % or Engineering Scrap Policy
- Active Status
- Notes

Supplier/Price/Currency/Route-specific fields باید از Sourcing Model بیایند، نه اینکه تنها نسخه آن‌ها در Part Master باشد.

---

# 13. Component Master

Component یک Item از نوع `Component` است که برای تولید Part/Subassembly مصرف می‌شود.

Intrinsic/master attributes مشابه Part هستند.

Official Item Code آن `Item.BusinessNumber` با قالب `CMP-{sequence:D4}` است.

Component باید بتواند در Multi-Level BOM ظاهر شود و Supply/Inventory/Sourcing مستقل داشته باشد.

---

# 14. Supply Method

## LOCKED

Machine codes:

- `BUY`: external requirement at Item level; no lower BOM explosion.
- `MAKE`: net Item supply, then explode the effective BOM.
- `OUTSOURCE`: with a valid planning BOM, net then explode; without BOM, external requirement at Item level.
- `HYBRID`: if only one route is Planning Ready it may be used and traced; if both are ready, an explicit Run Route Decision is required.

Supply Method is owned by Item Master. Sourcing and BOM do not independently redefine it.

---

# 15. Multi-Level BOM

## LOCKED

BOM Engine باید Multi-Level و Recursive باشد.

نمونه:

```text
Product
  ↓
Part
  ↓
Component
  ↓
Sub-Component
```

BOM نباید به Level ثابت محدود شود.

هر BOM Line حداقل شامل:

- Output Item / BOM Header
- Input Item
- Usage Qty

BOM Engine باید:

- Decimal Usage را پشتیبانی کند؛
- Circular BOM را جلوگیری کند؛
- Inactive Item را کنترل کند؛
- Duplicate accidental lines را کنترل کند؛
- Versioning/Revision را در معماری آینده‌پذیر در نظر بگیرد.

---

# 16. BOM UI

Legacy `Component Count` فقط رفتار UI قدیمی بوده است.

ERP نباید تعداد BOM Lines را قبل از ساخت BOM به‌عنوان محدودیت Data Model ذخیره کند.

UI باید Add/Remove Line داشته باشد و Count از تعداد واقعی Lines به دست آید.

---

# 17. Scrap

Scrap در Material Requirement Calculation لحاظ می‌شود.

Effective Scrap از BOM Line override غیرnull و در غیر این صورت Item Default Planning Scrap می‌آید. `NULL` با explicit zero متفاوت است.

فرمول v1:

```text
Planned Requirement = Base Requirement × (1 + Scrap% / 100)
```

Yield-based formula در baseline استفاده نمی‌شود و Scrap نباید برای یک Requirement چندبار اعمال شود.

تمام محاسبات Qty باید Decimal/Numeric باشند؛ Floating Point ممنوع است.

---

# 18. Forecast Data Model

Forecast/Actual نباید به شکل Spreadsheet Wide ذخیره شود.

Baseline conceptual model:

```text
Product
Period
Forecast Qty
Actual Qty / derived monthly actual
Period Status
```

Forecast تاریخی حفظ می‌شود حتی زمانی که Planning برای آن دوره از Actual استفاده می‌کند.

---

# 19. Planning Horizon

Data Model باید افق بلندمدت تا 20 سال را پشتیبانی کند.

اما UI و Planning Engine فقط Range لازم را Query/Calculate می‌کنند.

هر MRP Run دارای As-Of Date و Horizon End است. Default baseline تقریباً ۱۲ ماه جلالی است و hard database horizon limit وجود ندارد.

هیچ Recalculation عمومی 20 ساله برای یک تغییر کوچک مجاز نیست مگر دلیل فنی روشن وجود داشته باشد.

---

# 20. Calendar and Timezone

## LOCKED

Canonical computational calendar: `Gregorian`  
Business timezone: `Asia/Tehran`

Jalali برای Input/Display/Reporting پشتیبانی می‌شود ولی نباید به‌صورت String اصلی محاسباتی ذخیره شود.

تمام Lead Timeهای فعلی بر اساس **Calendar Days** هستند.

---

# 21. Forecast / Actual Time Rules

## LOCKED

### Past Months

- Planning Demand از Actual نهایی استفاده می‌کند.
- Historical Forecast حفظ می‌شود.
- Forecast Accuracy در آینده قابل محاسبه است.

### Current Month

```text
Remaining Forecast = MAX(Monthly Forecast - Actual Production To Date, 0)
```

### Future Months

Forecast مرجع Demand است.

---

# 22. Daily Production Actual

Production Module آینده باید بتواند ثبت کند:

- Date
- Product
- Actual Production Qty

Monthly Actual باید از مجموع Daily Production مشتق شود.

Duplicate manual monthly entry نباید منبع دوم Truth ایجاد کند.

تا قبل از ساخت Production Module، MRP-07 ورودی موقت aggregate در Grain `Product + Date` را نگهداری می‌کند. Production Module آینده بدون double counting مرجع authoritative می‌شود.

---

# 23. Current-Month Demand Distribution

## BASELINE LOCKED / FUTURE ENHANCEMENT ALLOWED

تا قبل از Production Calendar مستقل:

- Past days می‌توانند از Actual Production × BOM برای Calculated Consumption استفاده کنند.
- Remaining Forecast فقط روی Calendar Dayهای همان Jalali Planning Period که `Demand Date > Run As-Of` و `Demand Date <= Planning Period End` دارند توزیع می‌شود.
- روز `Run As-Of` سهم صفر دارد؛ Actual MTD تا cut-off همان روز تولید محقق‌شده را پوشش می‌دهد و Remaining Forecast فقط تقاضای محقق‌نشده بعد از cut-off است.
- توزیع از `As-Of + 1 calendar day` آغاز می‌شود، به دوره بعد spill نمی‌کند و با residual قطعی باید دقیقاً با Remaining Forecast reconcile شود.
- اگر Remaining Forecast مثبت ولی هیچ روز واجد شرایطی در دوره جاری وجود نداشته باشد، مقدار حذف یا جابه‌جا نمی‌شود و به‌صورت planning exception صریحِ current-period remainder unallocatable گزارش می‌شود؛ کد دقیق در MRP-10A تثبیت می‌گردد.

در آینده امکان جایگزینی Distribution با:

- Production Days
- Shift Calendar
- Capacity
- Factory Calendar

باید بدون بازطراحی هسته امکان‌پذیر باشد.

---

# 24. Daily Planning Grain

## LOCKED

Calculation Grain اصلی MRP: `Daily`

Display Grain می‌تواند:

- Daily
- Weekly
- Monthly

باشد.

Weekly/Monthly فقط Aggregation View هستند و Calculation Logic را تغییر نمی‌دهند.

---

# 25. Demand Explosion

Material Demand باید از Multi-Level BOM و Product Demand محاسبه شود.

برای Shared Item:

```text
Total Demand = Sum(Demand from all active parent paths)
```

Demand نباید به‌صورت تصادفی بین Productها Allocation شود.

Allocation فقط در صورت Requirement صریح آینده اضافه می‌شود.

---

# 26. Initial / Opening Inventory

Legacy `Initial Inventory Qty` نباید منبع دائمی Inventory باشد.

Opening Inventory در ERP باید حداقل شامل:

- Item
- Warehouse
- Quantity
- Effective Date

باشد.

Quantity باید `> 0` باشد و برای هر `Item + Warehouse` حداکثر یک `OPENING` تاریخی مجاز است، حتی اگر بعداً Reverse شده باشد. موجودی صفر به Opening row نیاز ندارد.

Migration Rule از Legacy Initial Inventory در Inventory Feature نهایی می‌شود.

---

# 27. Actual Inventory

## LOCKED

Actual On Hand و Projected On Hand جدا هستند.

Conceptual Actual Inventory:

```text
Opening Balance
+ Actual Receipts
- Actual Issues
± Adjustments
= Actual On Hand
```

Actual Inventory باید از Transactionها مشتق شود.

کدهای canonical موجودی دقیقاً عبارت‌اند از:

```text
OPENING / RECEIPT / ISSUE / ADJ_IN / ADJ_OUT / TRANSFER / REVERSAL
```

Posting فیزیکی آینده نسبت به business date تهران با `FUTURE_INVENTORY_POSTING_NOT_ALLOWED` مسدود است. برای هر اثر منفی، هیچ closing balance پایان‌روز از `EffectiveDate` تا آخرین تاریخ ledger در `Item + Warehouse` نباید منفی شود؛ lock قطعی و DB protection الزامی است و ترتیب درون‌روز اختراع نمی‌شود.

`REVERSAL.EffectiveDate` server-owned و برابر تاریخ Original است؛ هر Original حداکثر یک بار Reverse می‌شود و REVERSAL قابل Reverse نیست. جبران در تاریخ جاری با `ADJ_IN`/`ADJ_OUT` دارای Reason انجام می‌شود.

---

# 28. Calculated Consumption vs Actual Warehouse Issue

## LOCKED BASELINE

`Actual Production × BOM` یک Calculated Material Consumption است.

این مقدار الزاماً برابر Actual Warehouse Issue نیست.

دو مفهوم باید جدا بمانند:

- Calculated Material Consumption
- Actual Warehouse Issue

فقط Posted Inventory Issue موجودی واقعی را کاهش می‌دهد. Calculated Consumption analytical است و نباید ledger transaction تلقی شود. Explicit Issue در برابر Backflush automation جزو Future Production Scope است.

---

# 29. Projected Inventory

## LOCKED

Projected On Hand برای Planning است.

Concept:

```text
Projected End = Previous Projected End
              + Firm Incoming Supply
              - Projected Demand
```

Projected Inventory هرگز نباید با عنوان Actual Inventory نمایش داده شود.

---

# 30. Warehouse Context

Inventory باید از ابتدا Warehouse-aware باشد.

## LOCKED V1 BASELINE

Actual Inventory در Grain `Item + Warehouse` نگهداری و Company Total مشتق می‌شود. MRP v1 از total company inventory به‌عنوان Warehouse Pool استفاده می‌کند و demand/supply را به Warehouse تخصیص نمی‌دهد. Allocation، reservation و WMS Future Scope هستند.

Warehouse دارای UUID فنی و Official Code تولیدشده و immutable با قالب `WH-{sequence:D4}` است؛ Create ورودی Code ندارد و `MAX()+1` ممنوع است. Deactivation هنگام هر current Item balance غیرصفر با `WAREHOUSE_HAS_NONZERO_STOCK` مسدود می‌شود. اگر در شرایط کنترل‌شده reversal/legacy انبار غیرفعال بعداً stock غیرصفر داشته باشد، Company Pool آن را همچنان می‌شمارد و warning نشان می‌دهد.

Inventory Unit snapshot را از Item Base Unit به‌صورت server-captured و immutable نگه می‌دارد. پس از وجود هر Inventory transaction، تغییر عادی Base Unit با `ITEM_BASE_UNIT_LOCKED_BY_INVENTORY` مسدود است؛ conversion یا historical rewrite وجود ندارد.

---

# 31. Purchase Order Status Model

## LOCKED BASELINE

Baseline statuses:

- Draft
- Approved
- Ordered
- Shipped
- Customs
- Partially Received
- Received
- Cancelled

Logistics Milestones می‌توانند در آینده جزئی‌تر از Status اصلی باشند و نباید بدون تحلیل با Status ادغام شوند.

---

# 32. Firm Incoming Supply Rule

## LOCKED

Firm Supply نیستند:

- Draft
- Approved
- Cancelled

از `Ordered` به بعد Supply Firm محسوب می‌شود.

Firm Supply Statusهای Baseline:

- Ordered
- Shipped
- Customs
- Partially Received — فقط Remaining Qty

`Received` دیگر Incoming نیست؛ Actual Receipt/Inventory شده است.

`Approved` فقط Internal Approval است و Commitment به Supplier محسوب نمی‌شود.

---

# 33. Partial Receipt

## LOCKED

اگر PO Qty = 10,000 و Actual Receipt = 6,000:

- Actual Inventory فقط 6,000 افزایش می‌یابد؛
- Remaining Open Supply = 4,000؛
- Status = Partially Received.

Over-receipt tolerance در v1 صفر است؛ Receipt بیشتر از Schedule Open Qty مسدود می‌شود.

---

# 34. Purchase Order Snapshot

PO باید حداقل Snapshot این موارد را حفظ کند:

- Supplier
- Item
- Quantity
- Unit
- Currency
- Unit Price
- Total Amount
- Payment Terms
- Relevant Sourcing/Commercial terms
- Created/Approved/Ordered actor and timestamps as applicable

تغییر Master Data آینده نباید PO تاریخی را بازنویسی کند.

---

# 35. Planned vs Actual Milestones

## LOCKED

برای هر Supply Flow، Milestoneهای applicable باید Planned و Actual را جدا حفظ کنند.

Possible milestone set:

- Supplier Production Start/Finish
- Supplier Ready
- Forwarder Handover/Pickup
- Shipment Planned/Actual Departure
- Arrival
- Customs Entry
- Customs Clearance
- Domestic Transport
- Warehouse Receipt
- Internal Conversion/Assembly

Exact milestone set به Flow بستگی دارد و نباید همه Milestoneها برای همه Sourcingها اجباری شوند.

برای هر applicable milestone باید بتوان در حد نیاز نگه داشت:

- Planned Date
- Actual Date
- Planned Duration
- Actual Duration
- Variance
- Source
- Last Updated At

Historical Planned/Actual data immutable-in-meaning است؛ تغییر Policy آینده نباید آن را rewrite کند.

---

# 36. Lead Time Structure

Baseline stage mapping بدون gap/overlap:

```text
Supplier Lead:         ORDERED → SUPPLIER_READY
Forwarder/Pre-Shipment: SUPPLIER_READY → SHIPPED
Shipping:              SHIPPED → ARRIVED_DESTINATION
Destination/Customs:   ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
```

`HANDOVER_TO_FORWARDER` و `CUSTOMS_CLEARED` diagnostic milestones داخل Stageها هستند. فیلد قدیمی Customs Days در v1 معنای Destination/Customs Days دارد.

تمام Stageها Calendar Day هستند. Internal Process Lead Time در v1 برابر صفر است و nonzero routing lead time Future Scope است.

---

# 37. Lead Time Learning Architecture

## LOCKED CONCEPT

سیستم باید از روز اول داده کافی برای مقایسه Planned vs Actual جمع کند.

سه/چهار مفهوم جدا هستند:

1. Configured Lead Time
2. Historical Actual Performance
3. System Recommended Lead Time
4. Expert Approved Lead Time

Historical Analytics شامل:

- Sample Count
- Average
- Median / P50
- P80
- P90
- Min
- Max
- On-Time Percentage
- Revised Plan Performance
- Cancellation KPI

Historical statistics نباید خودکار Configured Lead Time را overwrite کنند.

تغییر Approved Planning Parameter باید Audit شود.

Policy baseline: Grain=`Sourcing Option`، Observation=`Delivery Schedule`، Window=12 months و در sample<5 تا 24 months؛ کمتر از پنج sample=`INSUFFICIENT_SAMPLE`. Percentileها continuous و معادل PostgreSQL `percentile_cont` هستند. P50/Median عملکرد معمول، P80 توصیه استاندارد سیستم و P90 مرجع محافظه‌کارانه/ریسک است. P90 به‌صورت خودکار جایگزین P80 یا Configured Lead Time نمی‌شود.

---

# 38. Lead Time Context

Analytics باید Context-aware باشد.

مثلاً Supplier Production performance ممکن است به Supplier + Item/Category وابسته باشد و Shipping performance به Route/Mode/Forwarder.

Exact grouping/fallback strategy هنوز قفل نشده و باید بعد از وجود Sample Data واقعی تصمیم‌گیری شود.

Data Model باید Context لازم را از دست ندهد.

---

# 39. Mapping Definition

## LOCKED

Mapping در ERP یک `Planning Read Model / Material Timeline` است، نه Source of Truth Transactionها.

Mapping داده را از:

- Forecast
- Production Actual
- BOM
- Inventory
- Purchase Orders
- Receipts
- Logistics/Trading status

می‌خواند.

Receipt و Status نباید به‌صورت مستقل و تکراری فقط در Mapping ذخیره شوند.

---

# 40. Mapping Primary UI

## LOCKED DIRECTION

به‌جای Spreadsheet بسیار Wide با پنج ستون برای هر Item، UI اصلی Material-centric است.

### 40.1 Material Planning Summary

یک Row برای هر planned Item با اطلاعاتی مانند:

- Item
- Supplier / preferred source where applicable
- Actual On Hand
- Firm Incoming Supply
- Projected Position
- Stockout Date
- Stock Coverage
- Firm Supply Coverage
- Next Receipt
- Supply Status
- Risk
- Recommended Action

### 40.2 Material Timeline

با انتخاب یک Item:

- Date
- Demand
- Actual Receipt
- Firm Incoming Supply
- Actual On Hand where meaningful
- Projected On Hand
- Stock Coverage
- Firm Supply Coverage
- PO/Logistics Event

UI می‌تواند Chart مکمل داشته باشد ولی Grid منبع تحلیل باقی می‌ماند.

---

# 41. Coverage Engine

## LOCKED

Coverage نباید فقط `Inventory / Today's Demand` باشد.

Engine باید Future Demand را Sequentially مصرف کند.

### Stock Coverage

چند روز Demand آینده فقط با Stock فعلی پوشش داده می‌شود.

### Firm Supply Coverage

چند روز Demand آینده با Stock + Firm Incoming Supply پوشش داده می‌شود.

اگر Future Demand وجود ندارد، سیستم باید `No Future Demand` یا وضعیت معادل نشان دهد و عدد جعلی تولید نکند.

---

# 42. Stockout Date

Stockout Date باید از Daily projected demand/supply timeline مشتق شود.

اگر Stockout در Planning Horizon رخ نمی‌دهد، سیستم باید Horizon state را شفاف نشان دهد و تاریخ ساختگی نسازد.

---

# 43. Order Planning Purpose

Order Planning هسته تصمیم‌گیری MRP است.

برای هر Item باید بتواند پاسخ دهد:

- آیا اقدام لازم است؟
- چه زمانی؟
- چه مقدار؟
- بر اساس کدام داده؟
- چه Supplyهایی در راه هستند؟
- ریسک عدم اقدام چیست؟

---

# 44. Order Planning Inputs

Recommendation Engine حسب Requirement از موارد زیر استفاده می‌کند:

- Product Forecast
- Production Actual
- Multi-Level BOM
- Scrap
- Actual Inventory
- Projected Inventory
- Firm Incoming Supply
- Open PO
- Planning Policy
- Stockout Date
- MOQ
- Sourcing Profile
- Supplier
- Lead Time Stages
- Price
- Currency

Missing critical data باید Recommendation certainty را کاهش دهد یا Item را Block کند.

---

# 45. Recommendation Output

Baseline fields:

- Select
- Item ID
- Item Name
- Supplier
- Hadron Owner
- Supply Status
- Priority
- Next Action
- Last Order Date
- Inventory at Report Date
- Future Firm Receipts
- Remaining Open Orders
- Demand Through Decision Horizon
- Expected Stockout Date
- Coverage
- Total Supply Lead Time
- MOQ
- System Recommended Qty
- Expert Decision
- Expert Final Qty
- Change/Rejection Reason

Decision Horizon:

```text
On-time = Target Receipt Date + Safe Start Days
Late    = Earliest Feasible Receipt + Safe Start Days
```

Priority codes برابر `P0_EMERGENCY`, `P1_DUE_NOW`, `P2_UPCOMING`, `P3_PLANNED` است؛ P2 از Item Critical Days استفاده می‌کند و blocked/incomplete priority جعلی ندارد.

---

# 46. Explainability

## LOCKED

Recommendationهای مهم باید Explainable باشند.

حداقل باید بتوان نشان داد:

- Current/Projected position
- Relevant firm supply
- Demand horizon
- Stockout date
- Total lead time
- Raw requirement
- MOQ adjustment
- Final recommended quantity
- Reason/action date

Black-box recommendation پذیرفته نیست.

---

# 47. Machine vs Expert Decision

## LOCKED

Machine Recommendation و Expert Decision دو Record/Concept جدا هستند.

حداقل باید حفظ شود:

- Machine Recommended Qty
- Machine Reason
- Engine Version
- Expert Decision
- Expert Final Qty
- Expert Reason
- Actor
- Timestamp

Expert action نباید Machine result تاریخی را overwrite کند.

Baseline Expert decisions می‌تواند شامل:

- Accept
- Modify
- Reject
- Defer

باشد.

---

# 48. Purchase Order Creation

فقط Expert Decision جاری و معتبر از نوع ACCEPT/MODIFY می‌تواند به Draft PO تبدیل شود. Conversion کنترل‌شده، idempotent و lineage-preserving است و هر Decision یک PO Line ایجاد می‌کند.

Draft هنوز Firm Supply نیست.

PO implementation باید Idempotent باشد تا یک Recommendation دوبار ناخواسته Draft ایجاد نکند.

---

# 49. Mapping Status Source

Mapping Status از PO/Logistics/Trading/Inventory flows خوانده می‌شود.

در آینده Trading Module می‌تواند Milestone/Status را update کند.

Mapping نباید status copy مستقل و authoritative ایجاد کند.

---

# 50. Cash Flow Integration Contract

Cash Flow یک Module مستقل آینده است.

MRP/Procurement باید داده کافی برای تولید Financial Commitments فراهم کند.

مثال:

```text
PO Total: 100,000 CNY
Advance: 30%
Before Shipment: 70%
```

Cash Flow باید بتواند Milestone Amount/Date را از:

- PO Snapshot
- Currency
- Payment Terms
- Planned/Actual Milestones

دریافت کند.

MRP-14 یک read-only Procurement Financial Commitment Contract تعریف می‌کند؛ Cash Flow واقعی همچنان Module آینده است.

اگر Credit%>0 باشد Credit Days>0 و Credit Anchor Event الزامی است. codes:

```text
ORDERED
SHIPPED
WAREHOUSE_RECEIPT
```

تاریخ expected payment از Actual سپس Revised سپس Original و در غیر این صورت Unresolved می‌آید. برای `WAREHOUSE_RECEIPT`، Credit Anchor برابر `Final Receipt Date` عملیاتیِ مشتق‌شده از valid Posted و non-reversed Receipt history است؛ First Receipt و Cancellation Date anchor نیستند. Partial Receipt anchor را حل نمی‌کند، Fully Cancelled anchor ندارد، و reversal که completion را از بین ببرد anchor را دوباره Unresolved می‌کند.

---

# 51. Forecast Accuracy Preservation

## LOCKED

Forecast ماه‌های گذشته حذف یا صفر نمی‌شود.

Planning از Actual استفاده می‌کند، ولی Forecast تاریخی برای:

- Forecast Accuracy
- Production Planning Accuracy
- Cash Flow Accuracy
- Model evaluation

حفظ می‌شود.

Baseline analytics فقط Closed Period را ارزیابی می‌کند. Baseline Forecast آخرین Official در/قبل Period Start و Final Forecast آخرین Official قبل Close است. Row metrics شامل Error/Absolute Error/APE و aggregate شامل WAPE/Bias است.

---

# 52. Supplier Performance

Supplier `Lead Time Score` و `Quality Score` می‌توانند فعلاً Manual باشند.

در آینده باید امکان نگهداری جداگانه:

- Expert Score
- System-measured performance

وجود داشته باشد.

System performance ممکن است از Planned vs Actual, On-Time %, Variability و Sample Count مشتق شود.

هیچ System Score نباید Expert Score تاریخی را overwrite کند.

---

# 53. Audit and Traceability

هر تصمیم مهم باید تا Source Data قابل Trace باشد.

Target chain:

```text
PO
→ Expert Decision
→ Machine Recommendation
→ Projected Shortage
→ Material Demand
→ Forecast/Actual
→ BOM
→ Product
```

Audit حداقل برای:

- Planning policy changes
- Sourcing changes
- Price changes
- PO status changes
- Receipt
- Expert recommendation changes
- Lead time approved changes

باید قابل اعمال باشد.

---

# 54. Engine Versioning

Recommendation و Calculationهای Business-critical باید Engine/Rule Version قابل ردیابی داشته باشند.

تغییر Algorithm آینده نباید Interpretation نتیجه تاریخی را مبهم کند.

---

# 55. Data Quality

MRP نباید در نبود Data حیاتی Recommendation ظاهراً قطعی بسازد.

Missing/invalid data مانند:

- Missing BOM
- Missing Sourcing
- Missing Supplier
- Missing Lead Time
- Missing MOQ when required
- Missing Currency/Price when needed
- Missing Forecast

باید Warning/Block ایجاد کند.

Baseline readiness states می‌توانند شامل:

- Ready
- Incomplete Data
- Review Required
- Blocked

باشند.

---

# 56. Performance Requirements

MRP باید از Spreadsheet-style processing اجتناب کند.

ممنوع/نامطلوب:

- thousands of formulas
- per-cell processing
- per-row DB calls
- N+1 queries
- loading full history by default
- recalculating unrelated items
- client-side calculation on huge datasets

ترجیح:

- indexed queries
- batch reads/writes
- server-side calculations
- dependency-aware recalculation
- read models
- precomputation only where justified
- background processing for heavy workloads

Performance باید اندازه‌گیری شود، نه حدس زده شود.

---

# 57. Recalculation Strategy

معماری باید امکان Recalculation هدفمند داشته باشد.

مثلاً تغییر Forecast یک Product باید عمدتاً Items وابسته به همان Product/BOM path و Date Range را invalidate/recalculate کند، نه کل MRP بیست‌ساله را.

Exact incremental strategy بعد از مشاهده حجم واقعی تعیین می‌شود.

---

# 58. Numeric Precision

برای:

- Quantity
- BOM Usage
- Scrap
- Price
- Exchange Rate

از Decimal/Numeric استفاده شود.

Floating Point برای Business-critical calculation ممنوع است.

---

# 59. Golden Tests

Golden Dataset v1.0 در مسیر `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md` مرجع canonical و deterministic برای numerical regression است. Calculationهای مهم MRP باید Golden Regression Suite مبتنی بر همین ورودی‌ها و expected outputهای مستقل داشته باشند.

Correctness صرفاً با build موفق پذیرفته نمی‌شود. expected valueهای Golden برای عبور دادن test نباید silently تغییر کنند؛ هر تغییر نیازمند business-rule decision مصوب، نسخه جدید Golden Dataset و regression update است.

هر Scenario حداقل:

- Input
- Expected Output
- Business Explanation
- Tolerance if explicitly required

دارد.

Minimum scenarios:

1. Single Product / Single Part
2. Shared Part across Products
3. Multi-Level BOM
4. Scrap
5. Zero inventory
6. Existing inventory
7. Ordered PO
8. Draft PO
9. Approved not Ordered PO
10. Partial receipt
11. Cancelled PO
12. MOQ adjustment
13. Current month Actual + Remaining Forecast
14. Stockout inside lead time
15. Supply before stockout
16. Supply after stockout
17. No future demand
18. Expert modifies recommendation
19. Expert rejects recommendation
20. Price/Currency snapshot
21. Planned vs Actual milestone preservation
22. Lead-time history does not overwrite configured value
23. Historical forecast remains preserved after period close

---

# 60. UI Principles

MRP UI باید:

- fast
- data-oriented
- dense but readable
- RTL-ready
- filterable
- searchable
- explainable
- consistent with Hadron Brand Rules

باشد.

Order Planning باید به‌سرعت پاسخ دهد:

- What needs attention?
- Why?
- When?
- How much?
- What happens if no action is taken?

---

# 61. Reporting Direction

Future reports ممکن است شامل:

- Critical Materials
- Stockout Risk
- Coverage
- Firm Incoming Supply
- Purchase Requirement
- PO Pipeline
- Inventory Projection
- Forecast vs Actual
- Forecast Accuracy
- Recommendation vs Expert Decision
- Supplier Lead-Time Performance
- Lead-Time Forecast vs Actual

باشند.

Report featureها قبل از Data Foundation ساخته نمی‌شوند.

---

# 62. AI Boundary

## LOCKED

MRP Calculation Engine deterministic است.

AI می‌تواند در آینده:

- توضیح Recommendation
- Risk summary
- Anomaly detection
- Missing-data detection
- management narrative

انجام دهد.

AI نباید Quantity Calculation یا Stock Projection اصلی را به‌صورت غیرقطعی جایگزین کند و حق direct unrestricted database write ندارد.

---

# 63. Open Decisions Summary

هیچ design blocker واقعاً باز برای baseline باقی نمانده است. Golden Dataset v1.0 تصویب و Q-023 بسته شده است.

Future Production Calendar، nonzero routing lead time، warehouse allocation، quality، FX/accounting و controlled future over-receipt tolerance جزو Deferred Future Scope و non-blocking هستند.

وضعیت و source دقیق در `OPEN-QUESTIONS.md` نگهداری می‌شود.

---

# 64. Locked Decisions Summary

تصمیم‌های فعلی قفل‌شده:

1. Unified Item architecture
2. Product / Part / Component terminology
3. Multi-Level BOM
4. Separate Sourcing Profile
5. Price/Currency versioning and PO snapshot
6. Shipping Mode != Incoterm
7. Item-centric shared material planning
8. Separate Material Planning Policy
9. Actual vs Projected Inventory separation
10. Warehouse-aware inventory model
11. Daily calculation grain
12. Daily/Weekly/Monthly display
13. Calendar-day lead times
14. Past planning uses Actual while historical Forecast remains preserved
15. Current month = Forecast - Actual Production To Date, floored at zero
16. Future months use Forecast
17. Approved PO is not Firm Supply
18. Ordered and later applicable statuses create Firm Incoming Supply
19. Partial receipt affects inventory only by actual received quantity
20. Mapping is a Read Model, not transactional source
21. Coverage is future-demand sequential, not simple division
22. Stock Coverage and Firm Supply Coverage remain separate
23. Machine Recommendation and Expert Decision remain separate
24. Planned vs Actual supply milestones are preserved
25. Historical lead-time performance does not silently overwrite configured lead time
26. Cash Flow consumes Procurement commitments later
27. AI does not replace deterministic MRP calculations
28. Supply Method = BUY / MAKE / OUTSOURCE / HYBRID with explicit HYBRID route decision when required
29. Effective-dated immutable BOM revisions and demand-date resolution
30. Scrap formula = Base Requirement × (1 + Scrap%)
31. Actual Inventory source = posted Inventory Ledger; v1 Warehouse Pool = company total
32. Immutable/versioned MRP Run and net-before-explode
33. Decision Horizon and P0/P1/P2/P3 priority semantics
34. Idempotent Recommendation→PO conversion with lineage
35. Over-receipt tolerance zero and exact Schedule receipt allocation
36. Atomic Receipt/Reversal across Procurement and Inventory
37. Lead-Time analytics grain=Sourcing Option, observation=Schedule, recommendation=P80
38. Closed-period Forecast Accuracy with WAPE/Bias
39. Original-currency isolation and no cross-currency total
40. Credit Anchor Event and canonical lead-time stage mapping

---

# 65. Development Method

هر Feature از چرخه زیر عبور می‌کند:

```text
Business Scenario
→ Feature Specification
→ Codex Analysis
→ Approval
→ Implementation
→ Automated Tests
→ Independent Audit
→ Manual Acceptance
→ Feature Close
```

Codex بدون Approval وارد Feature بعدی نمی‌شود.

---

# 66. Canonical Package Implementation Sequence

ترتیب Baseline:

1. MRP-01 Reference Data
2. MRP-02 Product Master
3. MRP-03 Supplier Master
4. MRP-04 Part & Component Master
5. MRP-05 Sourcing & Commercial Terms
6. MRP-06 Multi-Level BOM
7. MRP-07 Forecast / Actual
8. MRP-08 Inventory Foundation
9. MRP-09 Purchase Supply Foundation
10. MRP-10 MRP Calculation Engine
11. MRP-11 Material Planning UI
12. MRP-12 Order Planning Recommendation
13. MRP-13 Purchase Order Operations & Receipt
14. MRP-14 Analytics, Reporting & Final Integration

Sequence ممکن است با دلیل فنی و Approval تغییر کند.

---

# 67. Canonical Authority Hierarchy

ترتیب مرجع:

```text
AGENTS.md — repository/global engineering rules
→ docs/ARCHITECTURE.md — platform architecture
→ MRP-SYSTEM-SPEC.md — canonical MRP-wide specification
→ DECISIONS.md — canonical approved decision register
→ OPEN-QUESTIONS.md — only unresolved/deferred questions
→ MRP-01..14 PACKAGE-FINAL — detailed feature specifications and prompts
→ Approved Analysis
→ Implementation
→ Tests/Audit
```

PACKAGE-FINALها آخرین approved package-level inputs این reconciliation بوده‌اند. پس از reconciliation، هر Prompt یا implementation متعارض با canonical high-level documents باید متوقف و اصلاح شود. Business Owner Requirement همچنان مرجع نهایی تصمیم کسب‌وکاری است و تعارض آن باید به Owner ارجاع شود.

---

# 68. Completion Standard

MRP زمانی کامل محسوب می‌شود که بتواند برای هر planned Item پاسخ دهد:

- What is it?
- Where is it used?
- What is the current actual inventory position?
- What is the projected inventory position?
- What firm supply is expected?
- What demand is expected?
- When will stock run out?
- How much coverage exists?
- When must procurement act?
- How much should be ordered?
- Why is that quantity recommended?
- What did the expert decide?
- What has already been ordered?
- Where is the order in its lifecycle?
- What was planned vs what actually happened?
- How much has actually been received?
- What financial commitment has been created?
- Can the result be traced back to Forecast, BOM, Inventory and PO data?

اگر این زنجیره قابل Trace و Explain نباشد، MRP کامل نیست.
