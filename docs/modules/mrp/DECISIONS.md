# Hadron ERP — MRP Decision Log

**Module:** MRP  
**Status:** Active  

> تصمیم‌های این فایل قفل‌شده‌اند مگر اینکه Project Owner صریحاً آن‌ها را تغییر دهد. Codex نباید این موارد را در Featureهای بعدی دوباره طراحی یا حدس بزند.

---

## DEC-001 — Unified Item Architecture

**Decision:** Product, Part and Component در Data Model بر پایه مفهوم مشترک `Item` با Type مشخص مدل می‌شوند.  
**Status:** LOCKED

---

## DEC-002 — Canonical Terminology

**Decision:**
- Product = محصول نهایی
- Part = قطعه مستقیم Final Assembly
- Component = ورودی ساخت Part/Subassembly

نام‌های Legacy Component که مستقیم در Final Assembly مصرف می‌شوند در ERP به Part تبدیل می‌شوند.  
**Status:** LOCKED

---

## DEC-003 — Multi-Level BOM

**Decision:** BOM Engine باید Recursive/Multi-Level باشد و به Product→Part محدود نشود.  
**Status:** LOCKED

---

## DEC-004 — Reference Data Classification

**Decision:** Reference Data به Shared ERP Reference، MRP Controlled Reference و Dynamic Master Data تقسیم می‌شود. Product/Supplier/Owner Filterها List تکراری نیستند و از Master Data Query می‌شوند.  
**Status:** LOCKED

---

## DEC-005 — Separate Sourcing Profile

**Decision:** Supplier, Price, Currency, MOQ و Supplier-specific Lead Time فقط در Part/Component Master نگهداری نمی‌شوند؛ Sourcing Relationship مستقل وجود دارد.  
**Status:** LOCKED

---

## DEC-006 — Price and Currency History

**Decision:** قیمت و Commercial Terms Versioned هستند و PO هنگام ایجاد Snapshot خود را حفظ می‌کند. Original Currency همیشه حفظ می‌شود.  
**Status:** LOCKED

---

## DEC-007 — Shipping Mode vs Incoterm

**Decision:** Shipping Mode و Incoterm دو مفهوم جدا هستند و نباید در یک Field/List ادغام شوند.  
**Status:** LOCKED

---

## DEC-008 — Material Planning Policy

**Decision:** Coverage/Safety thresholds برای Part/Component Planning Policy مستقل تعریف می‌شوند و مستقیماً از Product Threshold برای Shared Item استفاده نمی‌شود.  
**Status:** LOCKED

---

## DEC-009 — Shared Item Demand and Inventory

**Decision:** Demand یک Shared Part/Component از تمام Parentها Aggregate می‌شود و موجودی در حالت عادی Shared است. Allocation به Product فقط با Requirement صریح آینده ایجاد می‌شود.  
**Status:** LOCKED

---

## DEC-010 — Actual vs Projected Inventory

**Decision:** `Actual On Hand` و `Projected On Hand` دو مفهوم مستقل هستند و UI نباید آن‌ها را با هم اشتباه بگیرد.  
**Status:** LOCKED

---

## DEC-011 — Warehouse-Aware Inventory

**Decision:** Data Model موجودی از ابتدا Warehouse Context دارد، حتی اگر نسخه اولیه فقط یک Warehouse عملیاتی داشته باشد.  
**Status:** LOCKED

---

## DEC-012 — Daily Planning Grain

**Decision:** Calculation Engine روزانه است.  
**Display:** Daily / Weekly / Monthly  
**Status:** LOCKED

---

## DEC-013 — Lead Time Basis

**Decision:** Lead Timeهای فعلی بر اساس Calendar Days هستند.  
**Status:** LOCKED

---

## DEC-014 — Past Forecast Preservation

**Decision:** در ماه‌های گذشته Planning از Actual استفاده می‌کند، اما Historical Forecast حذف یا صفر نمی‌شود و برای Forecast Accuracy حفظ می‌شود.  
**Status:** LOCKED

---

## DEC-015 — Current-Month Demand Rule

**Decision:**

```text
Remaining Forecast = MAX(Monthly Forecast - Actual Production To Date, 0)
```

**Status:** LOCKED
**Source Package:** MRP-07, MRP-10

Current-period Remaining Forecast فقط روی تاریخ‌های `Demand Date > MRP Run As-Of` تا پایان همان Jalali Planning Period توزیع می‌شود. As-Of day سهم صفر دارد و تخصیص از `As-Of + 1 calendar day` آغاز می‌شود. توزیع به دوره بعد spill نمی‌کند؛ اگر روز واجد شرایطی وجود نداشته باشد remainder به‌صورت planning exception صریح نگهداری می‌شود. residual قطعی باید جمع دقیق quantity را حفظ کند.

---

## DEC-016 — Future-Month Demand Rule

**Decision:** ماه‌های آینده از Forecast استفاده می‌کنند.  
**Status:** LOCKED

---

## DEC-017 — Daily Production Actual Direction

**Decision:** در Production Module آینده Actual Production به‌صورت Daily ثبت می‌شود و Monthly Actual از مجموع آن مشتق می‌شود.  
**Status:** LOCKED

---

## DEC-018 — Firm Supply Start Status

**Decision:**
- Draft = not firm
- Approved = not firm
- Cancelled = not firm
- Ordered and applicable later in-transit statuses = firm

**Status:** LOCKED

---

## DEC-019 — Approved PO Meaning

**Decision:** Approved فقط Internal Approval است و تا `Ordered` شدن Commitment به Supplier برای MRP ایجاد نمی‌کند.  
**Status:** LOCKED

---

## DEC-020 — Partial Receipt

**Decision:** فقط Actual Received Qty وارد Actual Inventory می‌شود؛ Remaining Qty در PO به‌عنوان Firm Incoming Supply باقی می‌ماند.  
**Status:** LOCKED

---

## DEC-021 — Mapping Role

**Decision:** Mapping یک Planning Read Model / Material Timeline است و Source of Truth برای Receipt یا PO Status نیست.  
**Status:** LOCKED

---

## DEC-022 — Material-Centric Mapping UI

**Decision:** UI اصلی Mapping به‌جای Wide Spreadsheet، Material Planning Summary + Material Timeline خواهد بود.  
**Status:** LOCKED

---

## DEC-023 — Coverage Algorithm Direction

**Decision:** Coverage با Future Demand sequentially محاسبه می‌شود، نه تقسیم ساده Inventory بر مصرف امروز.  
**Status:** LOCKED

---

## DEC-024 — Two Coverage KPIs

**Decision:** حداقل دو KPI مستقل وجود دارد:
- Stock Coverage
- Firm Supply Coverage

**Status:** LOCKED

---

## DEC-025 — Machine Recommendation vs Expert Decision

**Decision:** Machine Recommendation و Expert Decision جدا ذخیره می‌شوند؛ Expert نتیجه Machine تاریخی را overwrite نمی‌کند.  
**Status:** LOCKED

---

## DEC-026 — Explainable Recommendations

**Decision:** Recommendation باید مقدار، تاریخ اقدام، دلیل، Stockout/Coverage، Lead Time و Adjustmentهایی مانند MOQ را قابل توضیح کند.  
**Status:** LOCKED

---

## DEC-027 — Planned vs Actual Supply Milestones

**Decision:** برای Milestoneهای applicable، Planned و Actual به‌صورت جدا ذخیره می‌شوند و History آن‌ها حفظ می‌شود.  
**Status:** LOCKED

---

## DEC-028 — Lead-Time Learning

**Decision:** سیستم باید Historical Lead-Time Performance را جمع کند و در آینده Recommended Lead Time بسازد، اما Historical data نباید خودکار Configured/Approved Lead Time را overwrite کند.  
**Status:** LOCKED

---

## DEC-029 — Historical Planning Snapshot Integrity

**Decision:** تغییر Policy/Lead Time آینده نباید Planned/Actual history سفارش‌های قبلی را rewrite کند.  
**Status:** LOCKED

---

## DEC-030 — Cash Flow Boundary

**Decision:** Cash Flow Module مستقل است و Procurement/PO داده‌های مالی و Milestoneهای لازم را در اختیار آن قرار می‌دهد.  
**Status:** LOCKED

---

## DEC-031 — Deterministic MRP Engine

**Decision:** Calculation Engine اصلی MRP deterministic است. AI می‌تواند Explain/Analyze کند ولی جایگزین Quantity/Inventory calculation نمی‌شود.  
**Status:** LOCKED

---

## DEC-032 — Decimal Precision

**Decision:** Quantity, BOM Usage, Scrap, Price و Exchange Rate با Decimal/Numeric محاسبه می‌شوند؛ Floating Point برای Business-critical calculations ممنوع است.  
**Status:** LOCKED

---

## DEC-033 — Golden Test Requirement

**Decision:** MRP Golden Dataset v1.0 در `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md` مرجع canonical و deterministic numerical regression است. Correctness فقط با Build موفق اثبات نمی‌شود و calculationهای بحرانی باید Golden Regression Suite را pass کنند. Expected valueها برای pass کردن test نباید silently تغییر کنند؛ تغییر آنها نیازمند business-rule decision مصوب، Golden Dataset version update و regression update است.  
**Status:** LOCKED  
**Source Package:** MRP-10, MRP-12, MRP-13, MRP-14

---

## DEC-034 — Feature-by-Feature Development

**Decision:** هر Feature از Spec → Analysis → Approval → Implementation → Tests → Audit → Manual Acceptance عبور می‌کند و Codex بدون تأیید وارد Feature بعدی نمی‌شود.  
**Status:** LOCKED

---

## DEC-035 — Item Identity and Codes

**Decision:** `Item.Id` تنها Technical UUID identity داخلی است. `Item.BusinessNumber` تنها Official Item Code و user-facing برای Product/Part/Component است و به‌ترتیب با `PRD-{sequence:D4}`، `PRT-{sequence:D4}` و `CMP-{sequence:D4}` به‌صورت server-generated، globally unique و immutable ساخته می‌شود. کاربر آن را در Create وارد یا در Edit تغییر نمی‌دهد؛ پس از ایجاد read-only نمایش داده می‌شود و search/sort/display از آن استفاده می‌کنند. `Item.Code` و `Item.NormalizedCode` معنای تجاری مستقلی ندارند و برای removal deprecate شده‌اند. پیش از حذف، مقادیر non-null در `catalog.item_legacy_codes` با `ItemId`, `LegacyCode`, `LegacyNormalizedCode`, `ArchivedAtUtc` آرشیو می‌شوند؛ این archive فقط migration/history است و current identity، UI، business logic یا search نیست. هیچ Code فعال یا خودکار دومی مجاز نیست.
**Status:** LOCKED  
**Source Package:** MRP-02, MRP-04

---

## DEC-036 — Supply Method and Route Behavior

**Decision:** Supply Method codes برابر `BUY`, `MAKE`, `OUTSOURCE`, `HYBRID` است. BUY در Item متوقف می‌شود؛ MAKE و OUTSOURCE دارای BOM پس از netting explode می‌شوند؛ HYBRID با دو route آماده نیازمند Run Route Decision است.  
**Status:** LOCKED  
**Source Package:** MRP-04, MRP-05, MRP-06, MRP-10

---

## DEC-037 — Safety Stock and Planning Threshold Ownership

**Decision:** Product Safety Stock/thresholds فقط Product-level هستند. Part/Component Material Safety Stock و Critical/Safe/Excess Days مستقل‌اند؛ Safety Stock یک Planning Floor است، نه Gross Demand یا Inventory.  
**Status:** LOCKED  
**Source Package:** MRP-02, MRP-04, MRP-10, MRP-11

---

## DEC-038 — Sourcing and Commercial Ownership

**Decision:** Supplier linkage، Price/Currency history، MOQ، route و operational configured lead time متعلق به Sourcing Option است. حداکثر یک Preferred active external option برای Item وجود دارد؛ Preferred گزینه‌ها را حذف یا route HYBRID را خودکار انتخاب نمی‌کند.  
**Status:** LOCKED  
**Source Package:** MRP-05

---

## DEC-039 — BOM Revision, Effectivity and Cycle Integrity

**Decision:** BOM recursive و multi-level است؛ Approved Revision immutable، effective-dated و برای Parent/Date یکتا است؛ Demand از Revision مؤثر همان Demand Date استفاده می‌کند؛ cycle در Approval ممنوع است.  
**Status:** LOCKED  
**Source Package:** MRP-06, MRP-10

---

## DEC-040 — Scrap Formula

**Decision:** Effective Scrap از BOM override غیرnull و در غیر این صورت Item default می‌آید؛ zero با null متفاوت است؛ فرمول v1 برابر `Base Requirement × (1 + Scrap% / 100)` است و yield formula استفاده نمی‌شود.  
**Status:** LOCKED  
**Source Package:** MRP-04, MRP-06, MRP-10

---

## DEC-041 — Forecast Revision and Production Actual

**Decision:** Forecast grain برابر Product + Jalali Month و revision history محفوظ است. Daily Actual موقت MRP برابر Product + Gregorian Date است؛ Monthly Actual مشتق می‌شود. Past=Actual، Current=`MAX(Forecast-Actual MTD,0)` و Future=Forecast.  
**Status:** LOCKED  
**Source Package:** MRP-07

---

## DEC-042 — Inventory Ledger and Warehouse Pool

**Decision:** Actual Inventory فقط از immutable Posted Inventory Transactions در Grain `Item + Warehouse` مشتق می‌شود. Company total مشتق است؛ MRP v1 از total company Warehouse Pool استفاده می‌کند و allocation انجام نمی‌دهد.  
**Status:** LOCKED FOR V1  
**Source Package:** MRP-08, MRP-10  
**Limitation:** Warehouse allocation/reservation Future Scope است.

---

## DEC-043 — MRP Run and Net-Before-Explode

**Decision:** هر official execution یک immutable، versioned و traceable MRP Run در grain روزانه است. Item-level stock و dated Firm Supply قبل از explosion net می‌شوند و فقط Net MAKE/OUTSOURCE-BOM requirement به فرزندان منتقل می‌شود.  
**Status:** LOCKED  
**Source Package:** MRP-10

---

## DEC-044 — PO, Schedule and Firm Supply

**Decision:** PO یک Supplier و Currency و چند Line دارد؛ Line می‌تواند چند Delivery Schedule داشته باشد. APPROVED Firm نیست؛ Firm Supply فقط remaining open schedule quantity در ORDERED/SHIPPED/CUSTOMS/PARTIALLY_RECEIVED با ETA معتبر است.  
**Status:** LOCKED  
**Source Package:** MRP-09, MRP-10, MRP-13

---

## DEC-045 — Recommendation, Decision Horizon and Priority

**Decision:** Machine Recommendation immutable و جدا از Expert Decision است. Decision Horizon on-time=`Target Receipt + Safe Start Days` و late=`Earliest Feasible Receipt + Safe Start Days`؛ priorities برابر P0/P1/P2/P3 و P2 based on Item Critical Days است.  
**Status:** LOCKED  
**Source Package:** MRP-12

---

## DEC-046 — Expert Decision and PO Conversion

**Decision:** Expert decisions برابر ACCEPT/MODIFY/REJECT/DEFER هستند. فقط current valid ACCEPT/MODIFY با conversion کنترل‌شده، idempotent و lineage-preserving تبدیل می‌شود؛ هر Decision یک PO Line ایجاد می‌کند.  
**Status:** LOCKED  
**Source Package:** MRP-12, MRP-13

---

## DEC-047 — Receipt, Cancellation and Reversal

**Decision:** Receipt Line به PO Line + exact Schedule متصل است؛ هر Receipt یک Destination Warehouse دارد؛ posting و reversal اثر Procurement و Inventory را atomically commit می‌کنند. Over-receipt tolerance صفر است. Cancellation فقط remaining obligation را حذف می‌کند.  
**Status:** LOCKED  
**Source Package:** MRP-13

---

## DEC-048 — Lead-Time Analytics Policy

**Decision:** Grain تحلیلی Sourcing Option و observation برابر Delivery Schedule است؛ window 12 ماه و در sample<5 تا 24 ماه گسترش می‌یابد؛ کمتر از پنج sample ناکافی است. Statistics شامل Sample Count، Mean، Median/P50، P80، P90، Min، Max، Original Plan On-Time%، Revised Plan Performance و Cancellation KPI است. Percentileها continuous و معادل PostgreSQL `percentile_cont` هستند. P50 عملکرد معمول، P80 Default System Recommended Lead Time و P90 Conservative/Risk Reference است؛ P90 خودکار جایگزین P80 نمی‌شود و application همچنان نیازمند اقدام audited است.  
**Status:** LOCKED  
**Source Package:** MRP-14

---

## DEC-049 — Lead-Time Stage Mapping

**Decision:** Stageها بدون gap/overlap عبارت‌اند از `ORDERED→SUPPLIER_READY`، `SUPPLIER_READY→SHIPPED`، `SHIPPED→ARRIVED_DESTINATION` و `ARRIVED_DESTINATION→WAREHOUSE_RECEIPT`. Handover و Customs Cleared diagnostic هستند.  
**Status:** LOCKED  
**Source Package:** MRP-14 cross-package amendment

---

## DEC-050 — Forecast Accuracy Policy

**Decision:** فقط Closed Period تحلیل می‌شود؛ Baseline Forecast آخرین Official در/قبل Period Start و Final Forecast آخرین Official قبل Close است؛ row metrics شامل Error/Absolute Error/APE و aggregate شامل WAPE/Bias است.  
**Status:** LOCKED  
**Source Package:** MRP-14

---

## DEC-051 — Currency Isolation and Reporting Provenance

**Decision:** commitmentها در PO Currency باقی می‌مانند؛ MRP FX conversion یا cross-currency total ندارد. هر report/export باید As-Of، filters، Run/algorithm version و generation provenance را حفظ کند.  
**Status:** LOCKED  
**Source Package:** MRP-12, MRP-14

---

## DEC-052 — Credit Anchor Event

**Decision:** اگر Credit%>0 باشد Credit Days>0 و Credit Anchor required است. codes برابر `ORDERED`, `SHIPPED`, `WAREHOUSE_RECEIPT` است؛ planned date fallback برابر Actual→Revised→Original→Unresolved است. برای `WAREHOUSE_RECEIPT`، anchor برابر operational `Final Receipt Date` است، نه First Receipt یا Cancellation Date. reversal که receipt completion را از بین ببرد anchor را دوباره Unresolved می‌کند.  
**Status:** LOCKED  
**Source Package:** MRP-03, MRP-09, MRP-13, MRP-14

---

## DEC-053 — Inventory Posting Integrity and Warehouse Identity

**Decision:** کدهای ledger دقیقاً `OPENING`, `RECEIPT`, `ISSUE`, `ADJ_IN`, `ADJ_OUT`, `TRANSFER`, `REVERSAL` هستند. اثر منفی باید تمام closing balanceهای پایان‌روز از EffectiveDate تا آخرین ledger date را در `Item + Warehouse` غیرمنفی نگه دارد؛ lock قطعی و DB protection الزامی است و ترتیب درون‌روز تعریف نمی‌شود. REVERSAL تاریخ Original را دارد، یک‌بار مصرف و غیرقابل Reverse است؛ future posting نسبت به Tehran business date ممنوع است. Opening مثبت و historically unique است. Base Unit پس از Inventory history قفل می‌شود. Warehouse UUID فنی و Official Code تولیدشده immutable `WH-{sequence:D4}` دارد و با stock غیرصفر غیرفعال نمی‌شود؛ exceptional inactive stock در Company Pool باقی می‌ماند و warning دارد.
**Status:** LOCKED
**Source Package:** MRP-08

---

## DEC-054 — Purchase Supply Aggregate, Lifecycle and Firm ETA

**Decision:** `PurchaseOrder.Version` تنها concurrency token کل PO aggregate است و Line/Schedule Version مستقل ندارند. `DRAFT` قابل ویرایش و refresh صریح snapshot است؛ `APPROVED` snapshot تجاری را قفل می‌کند، Firm نیست و Return-to-Draft به Reason نیاز دارد؛ `ORDERED` commitment خارجی، lock دائمی core commercial/qty/source، شروع Firm و زمان نهایی‌شدن Original Plan است. Lifecycle قابل command در MRP-09 فقط Draft/Approve/Return-to-Draft/Mark-Ordered است. هر Line از ابتدا یک default Schedule برای کل OrderedQty دارد و Planning فقط Schedule grain را مصرف می‌کند. Firm ETA برابر Latest Revised Planned `WAREHOUSE_RECEIPT`، سپس Original Planned `WAREHOUSE_RECEIPT` و سپس Missing است؛ Actual و `RequestedReceiptDate` fallback نیستند و Sourcing Master جاری هرگز fallback نیست. Received/Cancelled/Open از facts عملیاتی مشتق می‌شوند و counter قابل ویرایش ندارند.
**Status:** LOCKED
**Source Package:** MRP-09
