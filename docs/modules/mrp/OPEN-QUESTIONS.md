# Hadron ERP — MRP Open Questions

**Module:** MRP  
**Status:** Reconciled against MRP-01..MRP-14 PACKAGE-FINAL

> این سند فقط موارد واقعاً باز یا Future Scope را نگهداری می‌کند. موارد `CLOSED` برای حفظ Trace تصمیم قبلی باقی مانده‌اند و blocker نیستند. Codex حق ندارد موارد `OPEN` را حدس بزند.

## Closed Questions

### Q-001 — Supply Method
**Status:** CLOSED — MRP-04 / MRP-10  
**Approved:** `BUY`, `MAKE`, `OUTSOURCE`, `HYBRID`. HYBRID با دو Route آماده نیاز به تصمیم صریح Run دارد.

### Q-002 — Actual Warehouse Issue Method
**Status:** CLOSED FOR BASELINE — MRP-08  
**Approved:** فقط Posted Inventory Issue موجودی واقعی را کاهش می‌دهد. `Actual Production × BOM` مصرف محاسباتی است. Explicit Issue در برابر Backflush جزو Future Production Scope است.

### Q-003 — Multiple Warehouse Planning
**Status:** CLOSED FOR V1 BASELINE — MRP-08 / MRP-10  
**Approved:** Grain موجودی `Item + Warehouse` است؛ Company Total مشتق است؛ MRP v1 از Warehouse Pool سراسری استفاده می‌کند و allocation انجام نمی‌دهد. Allocation/Reservation انبارها Future Scope است.

### Q-004 — Component-to-Part Process Lead Time
**Status:** CLOSED FOR V1 BASELINE — MRP-10  
**Approved:** Internal Process Lead Time برابر `0 Calendar Days` و یک Run simplification است. Routing و lead time غیرصفر Future Scope است.

### Q-005 — Temporary Source of Daily Production Actual
**Status:** CLOSED — MRP-07  
**Approved:** ورودی موقت `Product + Date` در MRP-07؛ Production Module آینده بدون double counting مرجع authoritative می‌شود.

### Q-006 — Over-Receipt Policy
**Status:** CLOSED FOR V1 — MRP-13  
**Approved:** Tolerance صفر؛ `Receipt Qty > Schedule Open Qty` مسدود است.

### Q-007 — Scrap Formula Convention
**Status:** CLOSED — MRP-10  
**Approved:** `Planned Requirement = Base Requirement × (1 + Scrap% / 100)`. Yield formula در v1 استفاده نمی‌شود.

### Q-008 — BOM Revision and Effectivity
**Status:** CLOSED — MRP-06 / MRP-10  
**Approved:** Revisionهای Draft/Approved/Obsolete، Approved immutable، Effective From/To، حداکثر یک Approved Revision برای Parent/Date، انتخاب براساس Demand Date و منع cycle.

### Q-009 — Planning Horizon Defaults
**Status:** CLOSED FOR BASELINE — MRP-10  
**Approved:** Run دارای As-Of و Horizon End؛ default تقریباً ۱۲ ماه جلالی و بدون hard database limit.

### Q-010 — Lead-Time Analytics Grouping
**Status:** CLOSED FOR BASELINE — MRP-14  
**Approved:** Grain اصلی `Sourcing Option` و Observation برابر `Delivery Schedule` است.

### Q-011 — Planning Parameter Recommendation Policy
**Status:** CLOSED FOR BASELINE — MRP-14  
**Approved:** Window دوازده ماه، گسترش تا ۲۴ ماه اگر کمتر از پنج sample، حداقل پنج sample معتبر، Recommendation برابر P80 و بدون automatic overwrite.

### Q-012 — Product-Level Safety Stock Role
**Status:** CLOSED FOR BASELINE — MRP-02 / MRP-04 / MRP-10  
**Approved:** Product thresholds فقط Product-level هستند. Part/Component Safety Stock مستقل و Planning Floor در netting است.

### Q-013 — Internal Part Production Quantity / Yield
**Status:** DEFERRED FUTURE SCOPE — MRP-10  
**Baseline:** v1 از BOM Base Output Qty و Scrap allowance استفاده می‌کند؛ routing، batch/yield و capacity خارج از baseline و non-blocking است.

### Q-014 — Firm Supply ETA Source
**Status:** CLOSED — MRP-09 / MRP-10  
**Approved:** Firm ETA از PO Delivery Schedule و با ترتیب Latest Revised Planned `WAREHOUSE_RECEIPT` سپس Original Planned `WAREHOUSE_RECEIPT` و در غیر این صورت Missing است. Actual milestone و `RequestedReceiptDate` fallback مربوط به Firm ETA نیستند. ETA مفقود یا overdue بدون حدس از projection کنار گذاشته می‌شود و current Sourcing Master هرگز fallback یک PO موجود نیست.

### Q-015 — Closed Period Rules
**Status:** CLOSED — MRP-07  
**Approved:** OPEN/CLOSED؛ Close ویرایش عادی را می‌بندد؛ Reopen نیازمند permission، reason و audit است.

### Q-016 — Currency Conversion Responsibility
**Status:** CLOSED FOR MRP BASELINE — MRP-12 / MRP-14  
**Approved:** Original/PO Currency حفظ می‌شود؛ MRP FX conversion یا cross-currency aggregation ندارد. FX و Cash Flow واقعی Future Scope است.

### Q-017 — Order Planning Decision Horizon
**Status:** CLOSED — MRP-12  
**Approved:** On-time برابر `Target Receipt + Safe Start Days`؛ late برابر `Earliest Feasible Receipt + Safe Start Days`.

### Q-018 — Priority Classification
**Status:** CLOSED — MRP-12  
**Approved:** `P0_EMERGENCY`, `P1_DUE_NOW`, `P2_UPCOMING`, `P3_PLANNED`؛ P2 از Item Critical Days استفاده می‌کند؛ blocked/incomplete priority جعلی ندارد.

### Q-019 — PO Cancellation After Partial Receipt
**Status:** CLOSED — MRP-09 / MRP-13  
**Approved:** فقط remaining obligation لغو می‌شود؛ receipt history حفظ می‌شود؛ Open=0 و Received>0 وضعیت RECEIVED با outcome مناسب دارد.

### Q-020 — Inventory Adjustment Governance
**Status:** CLOSED FOR BASELINE — MRP-08  
**Approved:** Adjustment با `ADJ_IN`/`ADJ_OUT` یک posted ledger transaction با permission، reason و audit است. REVERSAL تاریخ Original را دارد، برای هر Original حداکثر یک‌بار است و خودش قابل Reverse نیست. Backdated negative-stock، Warehouse deactivation، Opening uniqueness، Warehouse Official Code و Base Unit inventory lock نیز با DEC-053 بسته شده‌اند.

## Genuinely Open Blockers

### Q-021 — Current-Day Remaining Forecast Inclusion
**Question:** توزیع Remaining Forecast در ماه جاری، روز `Run As-Of` را شامل می‌کند یا از روز بعد آغاز می‌شود؟  
**Status:** CLOSED — Owner decision: As-Of excluded; allocation begins at As-Of + 1 calendar day and remains inside the same Jalali Planning Period.  
**Source:** MRP-07 / MRP-10 §16

### Q-022 — WAREHOUSE_RECEIPT Credit Anchor Date
**Question:** برای Credit Anchor نوع `WAREHOUSE_RECEIPT`، First Receipt Date یا Final Receipt Date مرجع است؟  
**Status:** CLOSED — Owner decision: operational Final Receipt Date derived from valid Posted, non-reversed Receipt history; First Receipt and Cancellation Date are not the anchor.  
**Source:** MRP-13 §54 / MRP-14 §40

### Q-023 — Golden Dataset Numeric Approval
**Question:** ورودی synthetic و expected numeric output مورد تأیید Owner برای Golden Regression چیست؟  
**Status:** CLOSED — Owner-approved Golden Dataset v1.0 is the deterministic numerical regression baseline.  
**Source:** `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md`; MRP-10 Golden Tests / MRP-14 §63–65  
**Change Policy:** Future expected-value changes require an approved business-rule change and a versioned Golden Dataset update; values must not be silently changed to make tests pass.

### Q-024 — MRP-13 Conversion Revalidation and Draft Deviation
**Status:** CLOSED — MRP-13 Owner decision
**Approved:** Expert Decision snapshot immutable basis می‌ماند؛ material operational drift conversion را با reconfirmation reason پایدار می‌بندد و silent refresh ممنوع است. Decision-derived core PO fields در Draft immutable هستند؛ Schedule planning فقط با OrderedQty ثابت، Reason و lineage قابل تغییر است.

### Q-025 — MRP-13 Batch Conversion Atomicity
**Status:** CLOSED — MRP-13 Owner decision
**Approved:** هر PO group یک atomic transaction مستقل است؛ گروه معتبر می‌تواند با وجود failure گروه مستقل دیگر commit شود، نتیجه هر گروه صریح است و retry نباید گروه موفق قبلی را duplicate کند.

### Q-026 — MRP-13 Receipt Scope and Effective Date
**Status:** CLOSED — MRP-13 Owner decision
**Approved:** هر Receipt دقیقاً یک PO و یک Destination Warehouse دارد؛ همه Lines از همان PO هستند و `OrderedDate <= EffectiveDate <= TehranToday` با Tehran date server-owned برقرار است.

### Q-027 — Inactive Item Existing Commitment Settlement
**Status:** CLOSED — MRP-13 Owner decision
**Approved:** برای PO قبلاً ORDERED، Receipt/Cancellation/Reversal با warning صریح مجاز است؛ commitment جدید و افزایش OrderedQty برای Item غیرفعال ممنوع است.

### Q-028 — Actual Milestone Correction
**Status:** CLOSED — MRP-13 Owner decision
**Approved:** Actual milestoneها canonical chronology و منع future date دارند؛ original immutable است و correction فقط append-only supersession با Reason/audit انجام می‌شود.

### Q-029 — MRP-14 Lead-Time Population and Apply Policy
**Status:** CLOSED — MRP-14 Owner decision
**Approved:** inclusion با clean Schedule Final Receipt Completion Date و Gregorian trailing-window boundary انجام می‌شود؛ reliability metrics جدا هستند. P80 برای هر stage جدا، با Ceiling و فقط از طریق command صریح versioned/audited متعلق به Sourcing apply می‌شود؛ Total P80 ذخیره نمی‌شود.

### Q-030 — MRP-14 Closed-Period Actual Completeness
**Status:** CLOSED — MRP-14 Owner decision
**Approved:** CLOSED یعنی Actual complete؛ نبود Daily Actual برای Product in-scope برابر صفر است. Reopen/Reclose از آخرین Close مؤثر استفاده می‌کند و `ACTUAL_NOT_AVAILABLE` نیازمند legacy/import incompleteness صریح است.

### Q-031 — MRP-14 Multi-Schedule Financial Anchor
**Status:** CLOSED — MRP-14 Owner decision
**Approved:** هر required Schedule مستقل resolve می‌شود؛ PO date برابر maximum resolved dates است و unresolved required Schedule، کل PO anchor را unresolved می‌کند. Schedule کاملاً cancelled بدون obligation کنار گذاشته می‌شود.

### Q-032 — MRP-14 Inventory Analytics v1 Scope
**Status:** CLOSED — MRP-14 Owner decision
**Approved:** physical position/movement، inactive-Warehouse warning و persisted MRP Risk/Coverage داخل scope هستند؛ slow-moving، turnover، valuation، invented Days-on-Hand و generic excess خارج scope‌اند.

### Q-033 — MRP-14 Analytics Read Architecture
**Status:** CLOSED — MRP-14 Owner decision
**Approved:** v1 query-time backend read models با server aggregation/paging دارد؛ materialization/background refresh بدون نیاز اثبات‌شده ممنوع است. UI/export قرارداد backend مشترک دارند، frontend recomputation ممنوع است، Currency جدا می‌ماند، AI read/explain only است و ApplyLeadTimeRecommendation Sourcing-owned است.

## Deferred Future Scope — Non-Blocking

- Production Calendar و working-day planning
- nonzero internal production lead time، routing، yield/batch و capacity
- warehouse allocation، reservation، bin/lot/serial و WMS
- Production Backflush، Returns و Stocktake
- Quality/Quarantine و supplier quality analytics
- controlled supplier over-receipt tolerance در نسخه آینده
- Treasury/Cash Flow، FX، AP/accounting، landed cost و inventory valuation
- enterprise BI / data warehouse

این موارد blocker برای baseline فعلی MRP نیستند مگر Feature آینده صریحاً آن‌ها را وارد scope کند.
