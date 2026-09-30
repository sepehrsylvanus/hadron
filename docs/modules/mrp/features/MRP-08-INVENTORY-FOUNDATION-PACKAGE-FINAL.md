# Hadron ERP — MRP Package 08 Final

**Package:** MRP-08 Inventory Foundation  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

## Owner-approved post-RC workspace extension — 2026-08-31

این بخش مرجع تکمیلی و ناسخ هر عبارت قدیمی درباره قرارگیری Inventory زیر Navigation برنامه‌ریزی است:

- Inventory یک workspace مستقل در Launchpad اصلی `Hadron ERP System` است؛ MRP فقط مصرف‌کننده read contract موجودی است.
- Warehouse جدید دارای Official Code تولیدشده، نام، نشانی/موقعیت و مسئول اصلی فعال است. رکوردهای تاریخی قبلی بدون حدس یا backfill ساختگی حفظ می‌شوند.
- انتقال داخلی جدید تک‌مرحله‌ای نیست. Aggregate انتقال با شماره تولیدشده و lifecycle زیر ثبت می‌شود:

```text
DRAFT
→ DISPATCHED
→ PARTIALLY_RECEIVED
→ RECEIVED
```

- Dispatch به‌صورت atomic یک `ISSUE` در مبدأ ایجاد می‌کند. هر Receipt جزئی/نهایی به‌صورت atomic یک `RECEIPT` در مقصد ایجاد می‌کند. Receipt/Dispatch ثبت‌شده immutable است و اصلاح با reversal کنترل‌شده انجام می‌شود.
- `In Transit = dispatched quantity - effective received quantity` است. این مقدار در company ownership باقی می‌ماند اما موجودی فیزیکی قابل‌استفاده نیست.
- MRP فقط جمع موجودی فیزیکی Warehouseها را مصرف می‌کند؛ In Transit تا زمان Receipt وارد netting نمی‌شود.
- دسترسی Warehouse یک Data Scope واقعی و Backend-enforced است. هر انتقال به دسترسی همزمان به مبدأ و مقصد نیاز دارد.
- Logistics، route/carrier/shipment/customs و دسترسی لجستیک به برخی انبارها scope آینده و جداگانه‌اند و در این extension وارد نشده‌اند.

---

# A. Final Feature Specification — 08-INVENTORY-FOUNDATION

## 1. Purpose

این Feature منبع حقیقت موجودی واقعی MRP را ایجاد می‌کند.

Inventory Foundation باید بتواند:

- Warehouse Master
- Opening Balance
- Inventory Transaction Ledger
- Actual On Hand
- Historical On Hand as-of-date
- Receipt
- Issue
- Adjustment
- Transfer
- Reversal / Correction
- Active / Inactive warehouse behavior
- auditability

را مدیریت کند.

این Feature نباید هنوز:

- Projected On Hand
- Coverage
- Firm Incoming
- Reservation
- Production Backflush
- PO Lifecycle
- Warehouse Allocation Planning
- Lot / Serial / Costing

را پیاده‌سازی کند.

---

## 2. Inventory Scope

### LOCKED DECISION

Inventory Foundation must support:

- Product
- Part
- Component

even though early MRP calculations may focus mainly on Part/Component.

This avoids a second inventory architecture later for Finished Goods.

---

## 3. Warehouse as Entity

### LOCKED DECISION

Warehouse is a real Entity.

It must NOT be stored as:

- free text
- arbitrary dropdown text
- comma-separated values

A Warehouse should have a stable technical identity.

Baseline fields may include:

- Internal ID
- Official Warehouse Code (`WH-{sequence:D4}`)
- Name FA
- Name EN
- Active Status
- Notes
- Created At
- Updated At

Warehouse uses a UUID technical identity. Its Official Warehouse Code is server-generated from a PostgreSQL sequence, globally unique, immutable, absent from Create input and read-only in ordinary UI. `MAX()+1` is prohibited.

---

## 4. Multiple Warehouses

### LOCKED DECISION

Multiple Warehouses are supported from day one.

Inventory balance grain:

```text
Item + Warehouse
```

Total company-wide inventory is Derived.

The system must not assume a single global warehouse.

---

## 5. Bin / Shelf / Location

### LOCKED DECISION

MRP-08 does NOT implement:

- Bin
- Shelf
- Rack
- Sub-location

Inventory granularity remains:

```text
Item + Warehouse
```

A future Warehouse Management feature may extend this.

---

## 6. Inventory Source of Truth

### LOCKED DECISION

Actual inventory is derived only from Inventory Transactions.

There is no manually editable:

```text
Current Stock
Current Inventory
On Hand Qty
```

field on Item Master or Warehouse balance rows.

Users never directly overwrite balance.

---

## 7. Inventory Transaction Ledger

Inventory truth is represented by immutable posted ledger movements.

Baseline machine transaction types:

```text
OPENING
RECEIPT
ISSUE
ADJ_IN
ADJ_OUT
TRANSFER
REVERSAL
```

`REVERSAL` is an explicit linked opposite transaction with the locked semantics in this Package.

Machine meanings are system-controlled.

They are not user-editable Reference Data.

---

## 8. Opening Balance

### LOCKED DECISION

Opening Balance is an Inventory Transaction, not an Item Master field.

Conceptually:

```text
Item
Warehouse
Opening Qty
Effective Date
```

Example:

```text
Item:           PCBA-25W
Warehouse:      WH-RAW-01
Qty:            10,000
Effective Date: 1405/01/01
```

Canonical date is stored Gregorian.

---

## 9. Opening Balance Rules

Opening quantity:

- decimal/numeric
- must be > 0
- expressed in Item Base Unit

Opening Balance must be traceable and auditable.

At most one historical `OPENING` may exist for each `Item + Warehouse`, even when that Opening was later reversed. Zero stock requires no Opening row. Correction is the linked `REVERSAL` followed, when needed, by a reasoned `ADJ_IN` or `ADJ_OUT`; a second Opening is prohibited.

---

## 10. Actual On Hand

### LOCKED DECISION

Actual On Hand is Derived.

Conceptually:

```text
Actual On Hand
=
Opening
+ Receipts
- Issues
+ Adjustment In
- Adjustment Out
+ Transfers In
- Transfers Out
± Reversal effects
```

No separate mutable balance field is the business source of truth.

---

## 11. Historical On Hand

### LOCKED DECISION

The system must support:

```text
On Hand As Of Date
```

for:

- Item + Warehouse
- Item across all Warehouses

This must be based on Inventory Transaction Effective Date.

This is required for historical audit and later MRP traceability.

---

## 12. Transaction Effective Date vs Created At

### LOCKED DECISION

These are separate concepts.

### Effective / Transaction Date

The business date when the inventory movement affects stock.

### Created At

When the record was entered into the system.

Example:

```text
Effective Date: 1405/05/10
Created At:     1405/05/12 09:30
```

Backdated entry can therefore be represented without falsifying the audit timeline.

Future physical posting is prohibited. If `EffectiveDate` is later than the current `Asia/Tehran` business date, posting fails with `FUTURE_INVENTORY_POSTING_NOT_ALLOWED`.

---

## 13. Canonical Date / Time

Canonical:

- business dates: Gregorian
- timestamps: timezone-aware according to platform conventions
- business timezone: Asia/Tehran

Jalali display/input may be supported by shared date infrastructure.

Inventory calculations must not compare formatted Persian date strings.

---

## 14. Posted Transaction Immutability

### LOCKED DECISION

Once an Inventory Transaction is Posted/finalized:

- Item
- Warehouse
- Qty
- Effective Date
- movement direction

must not be casually edited in-place.

Correction must use:

```text
Reversal / Correction Transaction
```

This preserves ledger integrity.

---

## 15. Draft vs Posted Boundary

MRP-08A may propose a minimal:

```text
Draft
Posted
```

entry lifecycle if needed for safe user entry.

If implemented:

- Draft is editable.
- Posted is immutable.
- Only Posted transactions affect Actual On Hand.

Do not introduce a large workflow engine.

If implementation can safely post atomically without Draft state, Analysis may propose that instead.

---

## 16. Adjustment

### LOCKED DECISION

Inventory balance cannot be directly typed over.

Correction of physical difference uses:

```text
ADJ_IN
ADJ_OUT
```

Adjustment requires:

- Reason
- actor/audit identity where available
- Effective Date

A generic editable `balance` field is prohibited.

---

## 17. Adjustment Reason

Reason is required for manual adjustment.

Reason is human-readable audit context.

It must not be parsed as machine business logic.

MRP-08 does not build a full stocktake module.

---

## 18. Receipt

`RECEIPT` increases Actual On Hand.

MRP-08 creates the generic Inventory Receipt foundation.

Future MRP-13 Purchase Order Operations will connect actual PO receipts to this same ledger.

MRP-08 must not create Purchase Order lifecycle logic.

---

## 19. Issue

`ISSUE` decreases Actual On Hand.

Actual warehouse consumption exists only when an Inventory Issue is posted.

This is independent from analytical expected consumption.

---

## 20. Expected Consumption vs Actual Warehouse Issue

### LOCKED DECISION

These are different.

Future analytical concept:

```text
Actual Production
×
Effective BOM
=
Expected Material Consumption
```

But Inventory truth changes only through:

```text
Posted Inventory Issue
```

Therefore:

```text
Expected Consumption
≠
Actual Warehouse Issue
```

MRP-08 must not automatically decrease inventory from Production Actual × BOM.

---

## 21. Production Integration Boundary

### LOCKED DECISION

Future Production Module may create Inventory Issues using:

- explicit material issue
or
- approved backflush logic

That decision is deferred.

MRP-08 only exposes a clean Inventory Issue posting boundary.

No production backflush is implemented here.

---

## 22. Purchase Receipt Integration Boundary

Future PO Receipt flow:

```text
PO / Receipt Event
        ↓
Inventory RECEIPT
        ↓
Actual On Hand
```

MRP-08 provides the ledger destination.

MRP-13 will own PO-specific receipt orchestration.

---

## 23. Transfer

### LOCKED DECISION

Warehouse Transfer is one atomic business operation.

Conceptually:

```text
From Warehouse  -Qty
To Warehouse    +Qty
```

Both legs must commit together or neither commits.

The system must not allow half-completed transfer.

---

## 24. Transfer Identity

Transfer should have a stable operation identity linking both warehouse movement legs.

The exact relational model is proposed in MRP-08A.

Requirements:

- one source warehouse
- one destination warehouse
- source != destination
- same Item
- same Qty
- same business transfer operation
- atomic transaction

---

## 25. Transfer Quantity

Rules:

- Qty > 0
- Item Base Unit
- cannot transfer from and to same Warehouse

Normal negative-stock rule applies to source warehouse.

---

## 26. Base Unit

### LOCKED DECISION

All Inventory quantities in v1 are expressed in:

```text
Item Base Unit
```

No hidden Unit Conversion is allowed.

The transaction Unit is a server-captured immutable snapshot of the Item Base Unit. Once any Inventory transaction exists for an Item, an ordinary Item Base Unit change is blocked with `ITEM_BASE_UNIT_LOCKED_BY_INVENTORY`. Inventory never converts quantities or rewrites history.

Examples:

- PCS
- KG
- M

Supplier packaging/order UOM conversion is outside this Package.

---

## 27. Negative Inventory

### LOCKED DECISION

Normal operations must not create negative Actual On Hand.

Example:

```text
On Hand = 500
Issue   = 700
```

Normal result:

```text
BLOCK
```

The system should show the shortage.

---

## 28. Future Negative-Stock Exception

A future exceptional override may be introduced only if real operational need is demonstrated.

If ever added, it must require:

- specific permission
- Reason
- Audit

MRP-08 baseline does NOT enable ordinary negative inventory.

---

## 29. Negative Inventory Check Grain

Negative-stock validation applies at:

```text
Item + Warehouse
```

not only company-wide total.

A positive balance in another Warehouse does not automatically authorize issuing from the selected Warehouse.

---

## 30. Backdated Transaction and Negative Stock

Backdated entries can change historical balance sequence.

Ledger validation is at business-date grain. For every posting that has a negative Warehouse effect, every end-of-day closing balance from its `EffectiveDate` through the latest ledger date for the same `Item + Warehouse` must remain nonnegative. Same-day movement ordering is not invented.

Posting uses deterministic `Item + Warehouse` locking, with all affected pairs acquired in stable order, plus DB-side protection so concurrent application calls or direct SQL cannot bypass the invariant.

---

## 31. Actual vs Projected Inventory

### LOCKED DECISION

Inventory Foundation owns only:

```text
Actual On Hand
```

Future Planning Engine owns:

```text
Projected On Hand
```

These must not be merged.

---

## 32. Projected On Hand Boundary

Future concept:

```text
Projected On Hand
=
Previous Projected
+ Firm Incoming
- Projected Demand
```

This is a Planning calculation.

It must not be written into the Inventory Ledger as if it were physical stock.

---

## 33. Inventory Total

For one Item:

```text
Total Actual On Hand
=
SUM(Actual On Hand across Warehouses)
```

Total is Derived.

Warehouse-level balances remain independently visible.

---

## 34. Warehouse Allocation Boundary

### LOCKED DECISION

MRP-08 stores actual inventory per Warehouse.

It does NOT decide how future MRP demand is allocated across Warehouses.

Questions such as:

- which Warehouse supplies which Product
- priority between warehouses
- reserved warehouse stock
- cross-warehouse planning

belong to future Planning policy.

---

## 35. Reservation / Allocated Stock

MRP-08 does NOT implement:

- Reserved Qty
- Allocated Qty
- Available-to-Promise
- Available-to-Promise by customer/order

Actual On Hand remains physical ledger stock.

---

## 36. Lot / Serial / Batch

MRP-08 does NOT implement:

- Lot
- Serial Number
- Batch
- Expiry

Future Inventory/WMS extension may add them.

The current data model must avoid assumptions that make future extension impossible, but must not overengineer now.

---

## 37. Inventory Costing

MRP-08 does NOT implement:

- FIFO
- LIFO
- Weighted Average
- Standard Cost
- Inventory Valuation
- COGS

Quantity ledger only.

Financial inventory costing belongs to future ERP financial scope.

---

## 38. Stocktake

A full Stocktake / Cycle Count module is not part of MRP-08.

Physical-count differences can be entered through controlled Adjustment transactions.

Future Stocktake may orchestrate those adjustments.

---

## 39. Warehouse Active / Inactive Lifecycle

Warehouse supports:

- Active
- Inactive

Rules:

- inactive Warehouse cannot be selected for new ordinary transactions
- historical transactions remain resolvable
- deactivation does not delete inventory history
- deactivation is blocked if any current Item balance in the Warehouse is nonzero, with `WAREHOUSE_HAS_NONZERO_STOCK`
- if controlled reversal or legacy circumstances later leave nonzero stock in an inactive Warehouse, company-wide MRP inventory still counts it and surfaces a warning

---

## 40. Item Active / Inactive Behavior

Historical transactions for inactive Items remain resolvable.

New ordinary movement against inactive Item should normally be blocked.

Administrative correction/reversal behavior for historical inactive Items must remain possible through controlled logic.

MRP-08A should propose the smallest safe policy.

---

## 41. Inventory Transaction Conceptual Fields

A posted movement conceptually needs:

- Transaction ID
- Transaction Type
- Item
- Warehouse
- Quantity
- direction/effect
- Effective Date
- Reason / Reference where applicable
- Source document type/reference where future integration uses it
- Created At
- Created By where available
- Reversal link where applicable

Do not duplicate unrelated PO/Production entities inside Inventory.

---

## 42. Source Document Integration Boundary

Inventory transaction should be capable of future traceability to a source such as:

- PO Receipt
- Production Issue
- Transfer
- Stock Adjustment

MRP-08A must propose a clean, non-EAV integration boundary.

Do not build every source module now.

---

## 43. Source Reference Safety

Do not store opaque uncontrolled free-text source references as the only link for future transactional integrity.

If a generic source-reference pattern is proposed, it must be type-safe enough for future integration and remain consistent with modular-monolith boundaries.

Avoid speculative polymorphic architecture unless justified.

---

## 44. Inventory Read Models

MRP-08 should expose efficient read models/use cases for:

### Current Balance

```text
Item + Warehouse → Actual On Hand
```

### Company Total

```text
Item → Total Actual On Hand
```

### Historical Balance

```text
Item + Warehouse + AsOfDate → On Hand
```

### Transaction History

filtered by:

- Item
- Warehouse
- Date range
- Type

These are Derived/query models over the ledger.

---

## 45. Inventory UI

Required baseline views:

### Warehouse Master

- list
- search
- Active/Inactive
- Add/Edit
- deactivate/reactivate

### Inventory Balance

One row per:

```text
Item + Warehouse
```

Suggested columns:

- Official Item Code (`Item.BusinessNumber`)
- Item Name
- Item Type
- Warehouse
- Actual On Hand
- Base Unit

Filters:

- Item
- Item Type
- Category where useful
- Warehouse
- Active/Inactive

### Transaction Ledger

- Date
- Type
- Item
- Warehouse
- Qty
- Direction/effect
- Reason/reference
- Created At

### Transaction Entry

- Opening
- Receipt
- Issue
- Adjustment
- Transfer

according to permission.

Follow `docs/brand/UI-BRAND-RULES.md`.

---

## 46. UI Balance Editing

### LOCKED DECISION

Balance grid is read-only.

There is no:

```text
Edit Stock
```

cell.

Changes happen through explicit Inventory operations.

---

## 47. Issue UX

Before posting Issue:

- show current Warehouse balance
- show requested Qty
- show resulting balance
- block if resulting balance would be negative

Server-side validation remains authoritative.

---

## 48. Transfer UX

Transfer entry should clearly show:

- Item
- From Warehouse
- To Warehouse
- Qty
- Effective Date
- resulting source balance where available

Posting must be atomic.

---

## 49. Adjustment UX

Adjustment must clearly distinguish:

- Adjustment In
- Adjustment Out

and require Reason.

Do not present a vague "new balance" field.

---

## 50. Opening Balance UX

Opening balance entry must clearly show:

- Item
- Warehouse
- Qty
- Effective Date

It should be distinguishable from normal Receipt.

MRP-08A should propose how to prevent accidental repeated Opening entries.

---

## 51. Permissions

At minimum architecture should distinguish:

- View Inventory
- Manage Warehouse
- Post Receipt
- Post Issue
- Post Transfer
- Post Adjustment
- Post Opening Balance

Exact permission granularity may be simplified if the existing authorization model uses broader roles, but Adjustment should remain more sensitive than ordinary viewing.

Do not build parallel Identity infrastructure.

---

## 52. Auditability

Important events must be compatible with Audit:

- Warehouse create/edit/deactivate/reactivate
- Opening Balance post
- Receipt post
- Issue post
- Transfer post
- Adjustment post
- Reversal/correction

Ledger history itself is the primary inventory history.

Audit logs supplement but do not replace ledger immutability.

---

## 53. Reversal / Correction

### LOCKED DECISION

Posted transactions are corrected through a compensating/reversal mechanism.

A correction must preserve:

- original transaction
- reversal/correction link
- Reason
- actor
- timestamp

Original posted business movement must remain readable.

`REVERSAL.EffectiveDate` is server-owned and exactly equals the original transaction `EffectiveDate`. One original transaction may be reversed at most once. A `REVERSAL` cannot itself be reversed. A correction that must affect the current business date uses a reasoned `ADJ_IN`/`ADJ_OUT` instead of changing the reversal date.

---

## 54. Reversal Effect

Reversal must create the exact opposite inventory effect of the original movement where applicable.

It must not mutate the original movement quantity to zero.

A reversed transaction remains part of history.

---

## 55. Transfer Correction

A Transfer correction must preserve atomic integrity across both warehouse effects.

The system must not reverse only one leg and leave warehouses inconsistent.

Exact implementation is proposed in MRP-08A.

---

## 56. Inventory Transaction Status

If a Draft/Posted lifecycle is approved:

```text
DRAFT
POSTED
REVERSED
```

may be represented as stable system states.

However:

- `REVERSED` must not mean deleting the original
- balance impact must remain ledger-derived

Do not turn transaction status into user-configurable Reference Data.

---

## 57. Inventory and Future PO Partial Receipt

Future PO handling may post:

- partial Receipt now
- remaining open supply later

MRP-08 only handles the actual received quantity posted to inventory.

It does not track remaining PO supply.

That belongs to Purchase Supply / PO modules.

---

## 58. Inventory and Future Production Returns

Future Production may create:

- ISSUE to production
- RETURN / reverse issue / receipt back to warehouse

MRP-08A may preserve an integration path without adding Production-specific transaction types unless strictly needed.

Do not implement Production returns workflow now.

---

## 59. Quantity Precision

Use decimal/numeric for all inventory quantities.

Do not use binary floating point.

Precision should be compatible with Item Base Units, including:

- integer-like PCS
- fractional KG
- fractional M

Do not hard-code whole-number-only inventory.

---

## 60. Database Integrity

Expected protections where practical:

- valid Item FK
- valid Warehouse FK
- Qty constraints
- Transaction Type integrity
- unique operation identity
- transfer source != destination
- linked transfer legs
- reversal relationship integrity
- posted immutability at application/domain boundary
- no destructive cascading delete of historical transactions

Exact PostgreSQL constraints are proposed in Analysis.

---

## 61. Balance Calculation Strategy

Actual On Hand business source remains the ledger.

MRP-08A may evaluate whether performance requires:

- direct ledger aggregation only
or
- a derived/materialized balance projection maintained transactionally

If a performance projection is introduced:

- it is NOT the business source of truth
- it must be fully reconcilable to ledger
- it must not be manually editable
- implementation must remain simple and deterministic

Do not add caching/materialization without demonstrated need.

---

## 62. As-Of Calculation

Historical On Hand must use transaction Effective Date.

Multiple transactions on one Effective Date contribute to one end-of-day closing balance. No intra-day stock ordering is invented; the locked negative-stock invariant is evaluated by business date.

Do not invent precision beyond actual business need.

---

## 63. Performance

Support efficient:

- current balance by Item+Warehouse
- total balance by Item
- transaction history by Item/Warehouse/date
- As-Of balance
- negative-stock validation before posting

Indexes should match likely query predicates.

Avoid:

- N+1 Item/Warehouse resolution
- recomputing all historical ledger rows for every grid cell if volume makes it impractical
- speculative Redis
- uncontrolled 20-year full-table client load

---

## 64. Concurrency

Concurrency-sensitive operations include:

- simultaneous Issues against same Item+Warehouse
- Transfer posting
- Reversal
- current-balance validation
- backdated entries

Two concurrent Issues must not both pass based on the same pre-issue balance and create unintended negative inventory.

MRP-08A must propose the smallest robust PostgreSQL/EF Core locking/concurrency strategy.

---

## 65. Transaction Atomicity

Posting one inventory business operation must be atomic.

Especially:

- Transfer
- Reversal of Transfer
- any future document posting that creates multiple inventory effects

Partial commits are prohibited.

---

## 66. Historical Integrity

Historical Inventory records must remain resolvable after:

- Item deactivation
- Warehouse deactivation
- Reference changes
- later integrations

Do not cascade-delete inventory history.

---

## 67. Seed / Migration

Codex must not invent real inventory balances.

If Opening Balance dataset is not explicitly supplied:

- implement capability
- do not fabricate stock

Legacy inventory migration will be a controlled future import.

Opening balances must use real effective dates and warehouses supplied by the user/business.

---

## 68. Explicit Non-Scope

MRP-08 must NOT implement:

- Projected On Hand
- Firm Incoming
- Coverage Days
- MRP demand allocation by Warehouse
- Reservations
- ATP
- Purchase Order lifecycle
- PO remaining supply
- Production Backflush
- Production material consumption engine
- Production Return workflow
- Lot
- Batch
- Serial
- Expiry
- Bin/Shelf/Rack
- Inventory Costing
- FIFO/LIFO/Average Cost
- Stocktake module
- Warehouse picking
- Packaging/UOM Conversion
- Barcode
- WMS
- Cash Flow
- Workflow Engine beyond minimal transaction posting lifecycle

---

## 69. Acceptance Criteria

MRP-08 is acceptable when:

1. Inventory supports Product/Part/Component.
2. Multiple Warehouses are supported.
3. Warehouse is an Entity.
4. Inventory grain is Item + Warehouse.
5. No manual editable Current Stock exists.
6. Actual On Hand is ledger-derived.
7. Opening Balance is a transaction.
8. Receipt increases Actual On Hand.
9. Issue decreases Actual On Hand.
10. Adjustment uses explicit In/Out + Reason.
11. Transfer is atomic between Warehouses.
12. Historical On Hand As-Of-Date is supported.
13. Effective Date is distinct from Created At.
14. Posted transactions are immutable.
15. Correction uses reversal/compensating transaction.
16. Normal negative inventory is blocked.
17. Negative-stock validation is Warehouse-specific.
18. Inventory quantities use Item Base Unit.
19. Decimal/fractional quantities are supported.
20. Actual warehouse Issue is distinct from analytical BOM consumption.
21. Production Actual × BOM does not automatically decrement stock.
22. PO Receipt future integration uses same ledger.
23. Inventory owns Actual only.
24. Projected inventory is not stored as physical stock.
25. Warehouse Allocation Planning is deferred.
26. No Lot/Serial/Cost/Reservation/WMS scope leaks in.
27. Historical inactive Item/Warehouse data remain resolvable.
28. Balance grid is read-only.
29. Server-side validation exists.
30. Concurrent Issues cannot bypass negative-stock protection.
31. Transfer/Reversal operations are atomic.
32. No real stock data is fabricated.
33. Canonical machine codes use `ADJ_IN`/`ADJ_OUT`, not alternate adjustment codes.
34. Future physical posting is blocked using the Asia/Tehran business date.
35. Backdated negative effects preserve every later end-of-day nonnegative closing balance.
36. Reversal EffectiveDate equals the original, is single-use and cannot itself be reversed.
37. Warehouse Official Code is server-generated immutable `WH-{sequence:D4}`.
38. Warehouse deactivation is blocked while any current Item balance is nonzero.
39. Opening Qty is positive and at most one historical Opening exists per Item+Warehouse.
40. Item Base Unit is locked after Inventory history and transaction Unit snapshots are immutable.

---

## 70. Locked Decisions

The following are locked for MRP-08:

1. Inventory supports Product / Part / Component.
2. Multiple Warehouses from day one.
3. Warehouse is Entity.
4. No Bin/Shelf/Location in v1.
5. Actual Inventory source of truth = Inventory Transaction Ledger.
6. No manually editable Current Stock.
7. Transaction types = `OPENING` / `RECEIPT` / `ISSUE` / `ADJ_IN` / `ADJ_OUT` / `TRANSFER` / `REVERSAL`.
8. Opening Balance = Item + Warehouse + Qty + Effective Date transaction.
9. Actual On Hand is Derived.
10. Historical On Hand As-Of-Date is required.
11. Effective Date and Created At are separate.
12. Posted Transaction is immutable.
13. Correction uses Reversal/Correction transaction.
14. Adjustment requires Reason.
15. Normal negative inventory is blocked.
16. Negative-stock check is Item + Warehouse specific.
17. Transfer is atomic.
18. Inventory Qty uses Item Base Unit.
19. Expected BOM Consumption is not Inventory Truth.
20. Only posted Inventory Issue reduces Actual stock.
21. Production Backflush decision is deferred.
22. Future PO Receipt posts to this ledger.
23. Inventory owns Actual On Hand only.
24. Projected On Hand belongs to Planning Engine.
25. Actual stock stored per Warehouse; company total is Derived.
26. Warehouse demand allocation policy is deferred.
27. Lot/Serial/Cost/Reservation/Stocktake/WMS are out of scope.
28. No destructive historical delete.
29. Negative-stock validation protects all end-of-day closings from a negative-effect posting date through the latest ledger date.
30. Reversal date equals the original Effective Date; reversal is single-use and non-reversible.
31. Future physical postings are blocked by the Tehran business date.
32. Warehouse deactivation is blocked while any current Item balance is nonzero.
33. At most one historical Opening exists per Item+Warehouse and Opening Qty is positive.
34. Item Base Unit is locked after the first Inventory transaction.
35. Official Warehouse Code is server-generated immutable `WH-{sequence:D4}`.

---

## 71. Open Question Resolution

The previous open question:

`Multiple Warehouse planning/allocation`

is PARTIALLY CLOSED.

Approved:

```text
Inventory supports multiple Warehouses from day one.
Actual stock is tracked per Item + Warehouse.
Company total is Derived.
```

Still deferred:

```text
How MRP allocates demand/supply across Warehouses.
```

This remains a Planning policy question.

The previous open question:

`Actual warehouse issue methodology`

is PARTIALLY CLOSED.

Approved:

```text
Actual inventory decreases only through Posted Inventory Issue transactions.
Expected consumption from Production Actual × BOM is analytical, not inventory truth.
```

Still deferred:

```text
Whether future Production Module creates Issues explicitly or through Backflush.
```

That belongs to future Production integration.

---

## 72. Remaining Relevant Open Questions

Intentionally deferred:

- Warehouse allocation policy for MRP
- Production explicit issue vs backflush
- future Production Return orchestration
- future Stocktake architecture

None blocks the core MRP-08 ledger architecture.

---

# B. Prompt 08A — Inventory Foundation Analysis

```text
# MRP-08A — Inventory Foundation Analysis

Read and follow:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-02-PRODUCT-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-03-SUPPLIER-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-04-PART-COMPONENT-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-05-SOURCING-COMMERCIAL-TERMS-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-06-MULTI-LEVEL-BOM-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-07-FORECAST-ACTUAL-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-08-INVENTORY-FOUNDATION-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior Packages were implemented merely because they were planned.

TASK:
Analyze MRP-08 Inventory Foundation.

ANALYSIS ONLY.

Do not modify:
- code
- database
- migrations
- configuration
- documentation
- repository structure

Do not install packages.
Do not generate implementation code.

Required analysis:

1. Understand Inventory as Actual physical stock source of truth.
2. Inspect actual Repository and reusable Item/Reference/User/Audit patterns.
3. Warehouse entity design:
   - UUID technical identity
   - server-generated immutable `WH-{sequence:D4}` Official Warehouse Code
   - name/status
   - multiple Warehouses
   - no bins/locations
4. Inventory Transaction Ledger relational model.
5. Verify the canonical transaction codes exactly: `OPENING`, `RECEIPT`, `ISSUE`, `ADJ_IN`, `ADJ_OUT`, `TRANSFER`, `REVERSAL`.
6. Decide minimum Draft/Posted model, if any.
7. Opening Balance Qty > 0 and at-most-one historical Opening per Item+Warehouse, including after reversal.
8. Actual On Hand derivation.
9. Historical As-Of balance.
10. Effective Date vs Created At.
11. Posted immutability.
12. Reversal/correction model:
    - preserve original
    - opposite ledger effect
    - linked correction
    - server-owned EffectiveDate equal to the original
    - one reversal maximum; reversal cannot be reversed
13. Adjustment with mandatory Reason.
14. Receipt/Issue behavior.
15. Atomic Transfer model with linked legs or equivalent clean relational pattern.
16. Negative inventory protection at Item+Warehouse.
17. Analyze concurrency-safe Issue posting so simultaneous Issues cannot overspend balance.
18. Verify the locked business-date negative-stock rule across every end-of-day closing from EffectiveDate through the latest ledger date, deterministic Item+Warehouse locking and DB-side protection; do not invent same-day ordering.
19. Confirm Item Base Unit only, immutable transaction Unit snapshot, and `ITEM_BASE_UNIT_LOCKED_BY_INVENTORY` after any Item transaction.
20. Confirm Actual Warehouse Issue is separate from Expected BOM Consumption.
21. Production integration boundary:
    explicit issue/backflush deferred.
22. PO Receipt integration boundary.
23. Actual vs Projected ownership separation.
24. Multiple-Warehouse planning allocation boundary.
25. Item/Warehouse Active/Inactive historical behavior, including `WAREHOUSE_HAS_NONZERO_STOCK` on deactivation and counting/warning for exceptional inactive-Warehouse stock.
26. Source-document integration boundary without speculative generic/EAV architecture.
27. Read models:
    - current balance
    - total item balance
    - as-of balance
    - transaction history
28. Evaluate whether direct ledger aggregation is sufficient.
    Do not add balance projection/materialization unless justified.
29. API/application use cases.
30. UI:
    - Warehouse Master
    - Balance grid
    - Ledger
    - Opening
    - Receipt
    - Issue
    - Adjustment
    - Transfer
31. Permissions/Audit.
32. DB constraints/indexes.
33. Decimal precision/date handling.
34. Performance.
35. Concurrency/atomicity.
36. Testing plan.
37. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..08 and actual Repository.
38. Smallest implementation sequence.
39. Explicit non-scope.

Testing proposal must cover at minimum:
- multiple Warehouses
- Warehouse lifecycle
- Opening Balance
- Opening Qty > 0 and duplicate historical Opening rejection after reversal
- Receipt
- Issue
- Adjustment In/Out
- Adjustment Reason required
- Transfer source/destination validation
- Transfer atomicity
- Actual On Hand derivation
- total Item balance
- As-Of balance
- Effective Date vs Created At
- posted immutability
- reversal preserves original
- reversal inventory effect
- reversal date equals original date; second reversal and reversal-of-reversal blocked
- current-date correction uses reasoned `ADJ_IN`/`ADJ_OUT`
- future Effective Date blocked by Tehran business date
- concurrent Issues against same stock
- negative Issue blocked
- warehouse-specific negative rule
- backdated negative effect preserves every later end-of-day nonnegative closing
- no same-day ordering semantics
- Warehouse Official Code generation/immutability/concurrency and no Create Code input
- Warehouse deactivation with nonzero balance blocked
- inactive Warehouse exceptional stock counted company-wide with warning
- Item Base Unit change blocked after any Inventory transaction
- transaction Unit snapshot immutable
- decimal quantities
- inactive historical Item/Warehouse resolution
- no editable Current Stock
- no Projected On Hand
- no Production backflush
- no PO lifecycle
- no Lot/Serial/Cost/Reservation

Output:
1. Understanding
2. Repository Findings
3. Warehouse Model
4. Ledger Model
5. Posting / Immutability / Reversal
6. Balance / As-Of Strategy
7. Negative Stock / Concurrency
8. Transfer Strategy
9. Integration Boundaries
10. Validation / Constraints
11. API Design
12. UI Design
13. Permissions / Audit
14. Performance
15. Tests
16. Risks / Contradictions
17. Implementation Plan
18. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 08B — Inventory Foundation Implementation

```text
# MRP-08B — Implement Inventory Foundation

Read and follow:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-02-PRODUCT-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-03-SUPPLIER-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-04-PART-COMPONENT-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-05-SOURCING-COMMERCIAL-TERMS-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-06-MULTI-LEVEL-BOM-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-07-FORECAST-ACTUAL-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-08-INVENTORY-FOUNDATION-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-08A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-08 Inventory Foundation design.

If the actual repository cannot implement the locked DB-side negative-stock integrity or sequence-backed Warehouse identity without weakening it, STOP and report the material blocker.

WAREHOUSE
Implement approved Warehouse entity and lifecycle:
- multiple Warehouses
- Active/Inactive
- no Bin/Shelf/Rack
- UUID technical identity
- server-generated globally unique immutable `WH-{sequence:D4}` Official Warehouse Code
- PostgreSQL sequence; no `MAX()+1`
- no Code in Create input; generated Code is read-only in UI
- block deactivation on any current nonzero Item balance with `WAREHOUSE_HAS_NONZERO_STOCK`
- include exceptional inactive-Warehouse stock in the company pool and surface a warning

INVENTORY LEDGER
Implement approved Inventory Transaction model and stable types:
- OPENING
- RECEIPT
- ISSUE
- ADJ_IN
- ADJ_OUT
- TRANSFER / linked transfer effects
- REVERSAL

SOURCE OF TRUTH
- Actual On Hand must be ledger-derived
- no manually editable Current Stock field

OPENING BALANCE
Implement:
- Item
- Warehouse
- Qty > 0
- Effective Date
- at most one historical `OPENING` per Item+Warehouse, even if reversed
- zero stock needs no Opening row
- correction uses REVERSAL and then reasoned ADJ_IN/ADJ_OUT when required

POSTING
Implement approved posting lifecycle.
Only final/posted movements affect Actual On Hand.

POSTED IMMUTABILITY
Do not allow ordinary edit of posted:
- Item
- Warehouse
- Qty
- Effective Date
- movement type

CORRECTION
Implement the locked reversal/compensating model:
- preserve original
- linked correction/reversal
- Reason
- actor/audit compatibility
- opposite inventory effect
- server-owned EffectiveDate exactly equal to Original.EffectiveDate
- one reversal maximum per original
- REVERSAL cannot be reversed
- current-date compensation uses reasoned ADJ_IN/ADJ_OUT

ADJUSTMENT
- explicit IN / OUT
- Reason required
- no direct balance overwrite

RECEIPT
- increases Actual On Hand

ISSUE
- decreases Actual On Hand
- normal posting must not make selected Item+Warehouse negative

TRANSFER
Implement atomic transfer:
- From Warehouse
- To Warehouse
- Item
- Qty
- Effective Date
- source != destination
- both inventory effects commit together

NEGATIVE STOCK
For every posting with a negative Warehouse effect, reject when any end-of-day closing balance from EffectiveDate through the latest ledger date would become negative. Use deterministic locking of all affected Item+Warehouse pairs and DB-side protection. Do not invent same-day ordering.

Two concurrent Issues must not both succeed if combined result would be negative.

Reject `EffectiveDate` after the current Asia/Tehran business date with `FUTURE_INVENTORY_POSTING_NOT_ALLOWED`.

BASE UNIT
All Qty uses Item Base Unit.
No UOM conversion.
Capture an immutable Unit snapshot on each transaction. Block ordinary Item Base Unit change after any Inventory transaction with `ITEM_BASE_UNIT_LOCKED_BY_INVENTORY`.

BALANCE
Implement approved query/read model:
- Item+Warehouse current Actual On Hand
- Item total
- As-Of-Date balance
- transaction history

If a derived balance projection is approved by 08A for performance:
- ledger remains business source of truth
- projection cannot be manually edited
- reconciliation must be testable

ACTUAL VS PROJECTED
Do NOT create Projected On Hand in inventory persistence.

EXPECTED CONSUMPTION
Do NOT decrement inventory from Production Actual × BOM.

PRODUCTION BOUNDARY
No backflush.
No Production Module.

PO BOUNDARY
Generic Receipt foundation only.
No PO lifecycle.

ACTIVE/INACTIVE
Preserve historical Item/Warehouse resolution.
Follow approved rules for new movement and correction against inactive entities.

BACKEND
Implement only approved:
- domain/data model
- EF Core config
- migrations
- application use cases/services
- API
- validation
- errors
- transaction/locking behavior

FRONTEND
Implement approved:
- Warehouse Master
- Inventory Balance grid
- Transaction Ledger
- Opening Balance entry
- Receipt entry
- Issue entry
- Adjustment entry
- Transfer entry
- Reversal/correction UX where approved

Balance grid is read-only.

Follow UI-BRAND-RULES.md.
Reuse approved grid/form patterns.

PERMISSIONS/AUDIT
Reuse existing platform.
Do not build parallel Identity/Audit infrastructure.

TESTS
At minimum:
- multiple Warehouses
- Opening
- Receipt
- Issue
- Adjustment In/Out
- exact canonical transaction codes; no alternate adjustment machine codes
- Adjustment Reason
- Transfer validation
- Transfer atomicity
- current balance
- item total
- as-of balance
- Effective Date behavior
- posted immutability
- reversal preservation/effect
- reversal EffectiveDate equality, one-reversal limit and reversal-of-reversal prohibition
- current-date ADJ_IN/ADJ_OUT compensation
- future posting rejection using the Tehran business date
- negative Issue rejection
- concurrent Issue integrity
- backdated negative-effect validation across all later end-of-day balances
- warehouse-specific balance rule
- Warehouse code generation, concurrency, format and DB/API immutability
- no Warehouse Code in Create contract
- nonzero-stock deactivation block and inactive-Warehouse company-pool warning
- Opening positive quantity and historical uniqueness after reversal
- Item Base Unit lock after Inventory history and immutable Unit snapshot
- decimal quantities
- historical inactive resolution
- no editable Current Stock
- no Projected On Hand
- no Production backflush
- no PO lifecycle
- no Lot/Serial/Cost/Reservation leakage

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend install if required
- frontend build/lint
- startup/health checks

Do not claim unrun checks.

SCOPE GUARD
Do NOT implement:
- Projected On Hand
- Firm Incoming
- Coverage
- Warehouse allocation planning
- Reservations/ATP
- PO lifecycle
- Production Backflush
- Production consumption engine
- Lot/Serial/Batch/Expiry
- Bin/Shelf/Rack
- inventory costing
- stocktake module
- WMS
- barcode
- Cash Flow

Final report:
- Changed
- Database
- Warehouse
- Inventory Ledger
- Posting / Reversal
- Balance / As-Of
- Negative Stock / Concurrency
- Transfer
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-09.
```

---

# D. Prompt 08C — Inventory Foundation Independent Audit

```text
# MRP-08C — Inventory Foundation Independent Audit

READ-ONLY audit.

Read:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-02-PRODUCT-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-03-SUPPLIER-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-04-PART-COMPONENT-MASTER-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-05-SOURCING-COMMERCIAL-TERMS-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-06-MULTI-LEVEL-BOM-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-07-FORECAST-ACTUAL-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-08-INVENTORY-FOUNDATION-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-08A Analysis
- MRP-08B implementation report

Inspect all MRP-08 implementation files.

Do NOT modify:
- code
- database
- migrations
- tests
- docs
- configuration

Do not auto-fix.

Audit:

1. Scope
   Confirm no premature:
   - Projected On Hand
   - Coverage
   - Warehouse allocation planning
   - PO lifecycle
   - Production Backflush
   - Lot/Serial
   - Costing
   - WMS

2. Warehouse
   - Entity
   - multiple Warehouses
   - Active/Inactive
   - no Bin/Shelf/Rack scope leak
   - UUID technical identity
   - server-generated immutable globally unique `WH-{sequence:D4}` Official Code
   - no client Code on Create and no ordinary rename
   - no `MAX()+1`
   - nonzero-stock deactivation blocked with `WAREHOUSE_HAS_NONZERO_STOCK`
   - exceptional inactive-Warehouse stock remains in company total with warning

3. Ledger Source of Truth
   - no editable Current Stock
   - Actual On Hand ledger-derived
   - no hidden mutable stock source

4. Transaction Types
   - exactly `OPENING`, `RECEIPT`, `ISSUE`, `ADJ_IN`, `ADJ_OUT`, `TRANSFER`, `REVERSAL`
   - correct effects
   - not user-configurable arbitrary lists
   - no conflicting alternate adjustment machine codes

5. Opening Balance
   - transaction-based
   - Item+Warehouse+Qty+Effective Date
   - Qty > 0
   - at most one historical Opening per Item+Warehouse, including after reversal
   - zero stock requires no Opening

6. Receipt / Issue
   - correct inventory effect
   - Item Base Unit
   - server-side validation

7. Adjustment
   - In/Out explicit
   - Reason required
   - no direct balance overwrite

8. Transfer
   - one atomic business operation
   - source != destination
   - linked effects
   - no half-commit

9. Posted Immutability
   - posted movement fields cannot be casually edited

10. Reversal / Correction
    - original preserved
    - linked reversal/correction
    - opposite inventory effect
    - Reason/audit
    - transfer reversal integrity
    - EffectiveDate is server-owned and equals Original.EffectiveDate
    - one reversal maximum; REVERSAL cannot be reversed
    - current-date compensation uses reasoned ADJ_IN/ADJ_OUT

11. Actual On Hand
    - current balance correct
    - item total correct
    - Warehouse-level separation

12. Historical As-Of
    - Effective Date used
    - Created At not confused with stock date

13. Negative Inventory
    - normal negative blocked
    - Item+Warehouse grain
    - concurrent Issues cannot bypass rule
    - every end-of-day closing from a negative-effect EffectiveDate through latest ledger date stays nonnegative
    - deterministic Item+Warehouse locking and DB-side protection are real
    - no same-day ordering is invented
    - future posting is rejected with `FUTURE_INVENTORY_POSTING_NOT_ALLOWED`

14. Actual vs Expected Consumption
    - no Production Actual×BOM decrement
    - only Inventory Issue changes stock

15. Actual vs Projected
    - no Projected On Hand persisted as physical stock

16. Historical Integrity
    - inactive Item/Warehouse history resolves
    - no destructive cascading delete

17. Source Integration Boundary
    - clean future PO/Production integration
    - no speculative EAV/polymorphic overengineering

18. Database Integrity
    - FK
    - Qty precision
    - indexes
    - transaction/reversal relationships
    - atomicity
    - migration safety
    - direct SQL cannot bypass posted immutability, reversal uniqueness/date, Opening uniqueness, Warehouse Code immutability/format, future-date or negative-stock protections

19. API
    - use-case boundaries
    - safe posting
    - no unrestricted dangerous generic CRUD

20. UI
    - Brand Rules
    - RTL
    - read-only Balance grid
    - clear Receipt/Issue/Adjustment/Transfer UX
    - no direct stock edit

21. Permissions / Auditability

22. Performance
    - current balance query
    - as-of query
    - transaction history
    - no N+1
    - no unjustified materialization/caching
    - ledger remains source of truth

23. Tests
    Verify meaningful assertions especially:
    - concurrent Issue integrity
    - atomic Transfer
    - Reversal
    - As-Of balance
    - posted immutability
    - transaction code exactness
    - Warehouse sequence/concurrency/immutability and no client Code
    - Opening positive quantity and uniqueness after reversal
    - future posting around Asia/Tehran business date
    - backdated end-of-day negative-stock integrity and same-day aggregation
    - reversal date/uniqueness/non-reversibility and compensation
    - Warehouse deactivation/current balance and inactive-Warehouse company-pool warning
    - Item Base Unit lock and immutable transaction Unit snapshot

24. Reference Data Integrity Gate
    - Unit remains owned by MRP-01; no shadow Unit master
    - active Unit is required for a new assignment and historical inactive Unit remains resolvable
    - `fa-IR` displays `NameFa`
    - `en` displays non-empty `NameEn`, otherwise falls back to `NameFa`
    - stored Reference Data is never auto-translated or modified by locale

25. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..08
    - approved MRP-08A
    - implementation

Severity:
- Critical
- Important
- Medium
- Low

For each finding:
- exact file/location
- violated rule
- impact
- recommended correction

Final output:
- Audit Result: PASS / PASS WITH FINDINGS / FAIL
- Critical Findings
- Important Findings
- Medium Findings
- Low Findings
- Test / Build Results
- Specification Compliance
- Scope Compliance
- Recommendation

Do not modify anything.
STOP.
```

---

# E. Final Pre-Codex Audit — MRP-08

## Result

`PASS`

## Audited Approved Decisions

تصمیم‌های تأییدشده Package 08، شامل Owner closure نهایی MRP-08A، پوشش داده شده‌اند:

1. Inventory supports Product / Part / Component.
2. Multiple Warehouses from day one.
3. Warehouse is Entity.
4. No Bin/Shelf/Location in v1.
5. Ledger is Actual Inventory source of truth.
6. Baseline transaction types are explicit.
7. Opening Balance is transaction-based.
8. Actual On Hand is Derived.
9. Historical As-Of balance is required.
10. Effective Date and Created At are separate.
11. Posted Transactions are immutable.
12. Correction uses Reversal/Correction.
13. Adjustment requires Reason.
14. Normal negative inventory is blocked.
15. Transfer is atomic.
16. Inventory uses Item Base Unit.
17. Actual Warehouse Issue is separate from analytical expected consumption.
18. Future PO Receipt integrates into the same ledger.
19. Inventory owns Actual, not Projected.
20. Warehouse allocation / Lot / Serial / Cost / Reservation are deferred.
21. Machine codes are exactly `OPENING` / `RECEIPT` / `ISSUE` / `ADJ_IN` / `ADJ_OUT` / `TRANSFER` / `REVERSAL`.
22. Negative-stock protection uses business-date end-of-day closings from EffectiveDate through the latest ledger date, without invented same-day ordering.
23. Reversal date equals the original EffectiveDate, is single-use and cannot be reversed.
24. Future physical posting is blocked by the Asia/Tehran business date.
25. Warehouse deactivation is blocked on any current nonzero Item balance; exceptional inactive-Warehouse stock remains counted with warning.
26. Opening is positive and historically unique per Item+Warehouse.
27. Item Base Unit is locked after Inventory history and Unit snapshots are immutable.
28. Warehouse Official Code is server-generated immutable `WH-{sequence:D4}`.

## Open Question Resolution

### Multiple Warehouse

Partially CLOSED:

```text
Actual Inventory is tracked by Item + Warehouse.
Multiple Warehouses are supported from day one.
Company-wide total is Derived.
```

Still deferred:

```text
MRP demand/supply allocation across Warehouses.
```

### Actual Warehouse Issue Methodology

Partially CLOSED:

```text
Only Posted Inventory Issue changes actual stock.
Production Actual × BOM is analytical expected consumption only.
```

Still deferred:

```text
Explicit Issue vs Backflush automation in future Production integration.
```

## Cross-Package Consistency

- MRP-04 removed Initial Inventory from Item Master; MRP-08 now owns Opening Balance correctly.
- MRP-07 Daily Production Actual does not directly alter Inventory.
- MRP-06 BOM remains an engineering structure, not warehouse truth.
- MRP-05 Sourcing contains configured supply assumptions, not actual receipts.
- Future MRP-09/13 can connect Purchase Supply and PO Receipt without creating a second stock model.
- Future Planning Engine can calculate Projected On Hand while Inventory keeps Actual On Hand authoritative.
- Multiple Warehouse support does not prematurely impose allocation policy.

## Architecture Corrections Applied

- Current Stock is explicitly non-editable and non-authoritative.
- Actual On Hand is defined from immutable ledger movements.
- Opening Balance is a dated Warehouse transaction.
- Effective Date is separated from system entry timestamp.
- Adjustments are explicit movement transactions, not balance overwrites.
- Transfer is defined as atomic.
- Negative-stock protection is Warehouse-specific and concurrency-sensitive.
- Expected BOM consumption and actual warehouse issue are explicitly separated.
- Reversal preserves original transactions.
- Inventory quantity ledger is separated from future costing and WMS concerns.
- Any performance balance projection remains secondary and reconcilable to ledger.
- Backdated negative-stock behavior is closed at end-of-day business-date grain with deterministic locks and DB protection.
- Reversal date/uniqueness and future-posting rules are explicit.
- Warehouse identity and deactivation semantics are explicit.
- Opening uniqueness and the Item Base Unit inventory lock are explicit.

## Remaining Intentional Open Questions

Not blockers for MRP-08:

- MRP Warehouse allocation policy.
- Production explicit material Issue vs Backflush.
- future Stocktake process.

## Prompt Audit

### MRP-08A
PASS

Analysis-only and Repository-aware; the Owner-approved conclusions are now the canonical implementation baseline rather than open design questions.

### MRP-08B
PASS

Implementation remains limited to Actual Inventory Foundation, implements the exact locked Warehouse/ledger/reversal/date/unit rules, and prevents Planning, PO, Production and WMS scope leakage.

### MRP-08C
PASS

Independent read-only audit checks source-of-truth integrity, exact codes, Warehouse identity/lifecycle, transaction immutability, date-grain negative-stock protection, reversal rules, Unit Reference Data integrity, As-Of history, atomic transfers and specification drift.

## Final Decision

`MRP-08 Inventory Foundation` is approved for the project Prompt Baseline.

It should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives the final cross-package audit.
