# Hadron ERP — MRP Package 07 Final

**Package:** MRP-07 Forecast / Actual  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 07-FORECAST-ACTUAL

## 1. Purpose

این Feature منبع زمانی Demand برای Planning را ایجاد می‌کند.

MRP-07 باید بتواند برای هر Product:

- Forecast ماهانه
- Forecast Revision History
- Daily Production Actual
- Monthly Actual Derived
- Current-Month Remaining Forecast
- Past / Current / Future planning truth
- Period Close / Reopen
- Forecast traceability

را مدیریت کند.

این Feature نباید هنوز:

- BOM Explosion
- Material Requirement Calculation
- Inventory Projection
- Coverage
- Order Recommendation
- Purchase Order

را پیاده‌سازی کند.

---

## 2. Business Period Calendar

### LOCKED DECISION

Business Planning Period در هادرون بر اساس **ماه شمسی** است.

Example:

```text
1405/05
```

یعنی:

```text
1405/05/01 → 1405/05/31
```

### Canonical Storage

Date storage and date arithmetic remain Gregorian.

Therefore:

```text
Business Period = Jalali Year/Month
Canonical Dates = Gregorian
```

The system must never perform business calculations by comparing formatted Persian date strings.

---

## 3. Business Period Entity / Value Model

Each monthly Planning Period must deterministically represent:

- Jalali Year
- Jalali Month
- Gregorian Start Date
- Gregorian End Date

Example conceptually:

```text
Business Period: 1405/05
Start Date: Gregorian equivalent of 1405/05/01
End Date:   Gregorian equivalent of 1405/05/31
```

The exact persistence strategy is determined in MRP-07A.

Do not duplicate inconsistent period boundaries in multiple tables.

---

## 4. Period Length

The system must correctly handle Jalali month lengths.

It must not assume every month has:

- 30 days
or
- 31 days

and must correctly handle Esfand and leap-year behavior.

Date conversion logic should reuse a trusted existing platform date implementation if available.

Do not implement ad-hoc calendar arithmetic in multiple places.

---

## 5. Planning Horizon

### LOCKED DECISION

Database design must not impose a hard 20-year storage limit.

The system may store long-term Forecast history and future periods.

Typical UI working horizon will usually be approximately:

```text
12 months forward
```

but user can select a different range.

UI must load only the selected range, not decades of data at once.

---

## 6. Forecast Grain

### LOCKED DECISION

Forecast grain:

```text
Product + Business Month
```

Forecast is not stored as a wide spreadsheet column structure.

Normalized conceptual model:

```text
Product
Period
Forecast Revision
Forecast Qty
```

---

## 7. Forecast Quantity

Forecast Qty rules:

- decimal/numeric
- >= 0
- expressed in Product Base Unit
- no binary floating point

A zero Forecast is valid and distinct from missing Forecast data.

---

## 8. Forecast Revision History

### LOCKED DECISION

Forecast changes must preserve history.

Example:

```text
Product: C1011
Period: 1405/05

Revision 1 = 30,000
Revision 2 = 34,000
Revision 3 = 32,000
```

Previous revisions must not be:

- deleted
- overwritten
- zeroed

through normal operations.

---

## 9. Official Current Forecast

For each:

```text
Product + Business Period
```

there is at most one Official Current Forecast Revision.

Historical revisions remain immutable records.

The exact implementation may use:

- explicit Current Revision reference
or
- another robust approved revision-state mechanism

but must not maintain two independently editable Forecast values that can drift.

MRP-07A must propose the smallest clean implementation.

---

## 10. Forecast Revision Metadata

Each Forecast Revision must preserve enough information for traceability.

At minimum conceptually:

- Revision identity
- Product
- Business Period
- Forecast Qty
- Created At
- Created By where user/audit infrastructure exists
- Revision Reason / Notes where approved

A Forecast Revision should later be referenceable by MRP Calculation Runs.

---

## 11. Forecast Change Reason

Forecast Revision may include:

`Change Reason / Revision Note`

This is strongly recommended for traceability.

Whether it is mandatory for every change is not a locked Business Rule and may be proposed in MRP-07A.

Do not build a heavy approval workflow solely for Forecast revisions.

---

## 12. Forecast History and Accuracy

Historical Forecast revisions exist so future analytics can compare:

- Original Forecast
- Later Forecast revisions
- Final/Latest Forecast
- Actual Production
- Forecast Error
- Forecast Accuracy

MRP-07 stores the history.

It does NOT yet implement a full Forecast Analytics engine.

---

## 13. Daily Production Actual

### LOCKED DECISION

Until the future Production Module exists, MRP-07 provides a simple temporary operational source for Daily Production Actual.

Grain:

```text
Product + Date
```

Field:

```text
Actual Production Qty
```

Rules:

- Gregorian canonical date
- Jalali display/input allowed through shared date layer
- Qty >= 0
- decimal/numeric
- Product Base Unit

---

## 14. Daily Actual Aggregation

### LOCKED DECISION

For v1:

```text
One aggregate Actual record per Product + Date
```

Do not introduce:

- Shift
- Production Line
- Machine
- Work Center
- Batch

level production tracking in MRP-07.

Those belong to a future Production Module.

---

## 15. Daily Actual Correction

An existing Product + Date Actual may be corrected while its Business Period is Open.

Correction must be auditable.

Do not create duplicate same-day records whose sum becomes ambiguous.

The persistence model should enforce one logical aggregate per:

```text
Product + Production Date
```

---

## 16. Future Production Module Integration

### LOCKED DECISION

MRP-07 Daily Actual is a temporary source.

Future architecture:

```text
Production Module Daily Actual
            ↓
MRP reads Actual
```

When Production Module becomes source of truth:

- duplicate manual monthly Actual must not remain
- MRP must not double-count both sources
- integration must preserve historical Actual traceability

MRP-07 must preserve a clean source boundary so this replacement is possible.

---

## 17. Monthly Actual

### LOCKED DECISION

Monthly Actual is Derived.

Formula:

```text
Monthly Actual
=
SUM(Daily Production Actual within Business Period)
```

There is no independently editable Monthly Actual field.

This prevents:

```text
Daily Actual Total ≠ Manually Entered Monthly Actual
```

---

## 18. Actual MTD

For current Business Period:

```text
Actual MTD
=
SUM(Daily Production Actual from Period Start through evaluation date)
```

Future planning uses this quantity when calculating Remaining Forecast.

---

## 19. Past Month Planning Truth

### LOCKED DECISION

For a Past Closed Business Period:

```text
Planning Truth = Actual Production
```

Historical Forecast remains stored.

It must NOT be:

- deleted
- replaced with Actual
- reset to zero

Forecast is retained for traceability and future Forecast Accuracy.

---

## 20. Current Month Planning Truth

### LOCKED DECISION

For the current Business Period:

```text
Remaining Forecast
=
MAX(Official Current Forecast - Actual Production To Date, 0)
```

Example:

```text
Forecast      = 32,000
Actual MTD    = 3,150
Remaining     = 28,850
```

### Current-Period Allocation Contract — LOCKED OWNER DECISION

MRP-07 supplies Actual MTD through the MRP Run As-Of cut-off and the Remaining Forecast quantity. MRP-10 owns daily allocation, using only dates:

```text
Demand Date > As-Of Date
AND
Demand Date <= Jalali Planning Period End
```

The As-Of date receives zero additional Remaining Forecast allocation. Allocation begins at `As-Of + 1 calendar day`, preventing the same cut-off date from carrying both realized Actual Production and a Remaining Forecast share. The quantity remains in its original Jalali Planning Period and must not spill into the next period.

If Remaining Forecast is positive and no eligible future calendar day remains, the quantity must be surfaced as an explicit current-period unallocatable planning state. It must not be allocated to As-Of, moved forward, discarded, or silently rounded away. The exact stable machine code may be finalized by MRP-10A. Deterministic residual allocation must preserve the exact Remaining Forecast total.

---

## 21. Actual Greater Than Forecast

### LOCKED DECISION

If:

```text
Actual MTD > Forecast
```

then:

```text
Remaining Forecast = 0
```

Actual Production must still preserve its real quantity.

The system must NOT cap Actual at Forecast.

Example:

```text
Forecast = 30,000
Actual   = 33,500
Remaining Forecast = 0
```

---

## 22. Future Month Planning Truth

### LOCKED DECISION

For future Business Periods:

```text
Planning Demand Source = Official Current Forecast
```

Actual is normally zero/nonexistent until production occurs.

---

## 23. Missing Forecast

Missing Forecast is different from:

```text
Forecast = 0
```

The system must preserve this distinction.

A missing Forecast should not silently become zero if that would hide incomplete planning data.

Exact readiness/warning behavior is determined in MRP-07A.

---

## 24. Forecast and BOM Independence

Forecast is a Business Demand input and may be recorded even if a Product's BOM is not yet Planning Ready.

However:

```text
Forecast exists
≠
Material Planning Eligible
```

Material planning eligibility still requires the approved effective BOM rules.

This prevents losing valid commercial/production planning data merely because Engineering Master Data is incomplete.

---

## 25. Product Eligibility in Forecast UI

### LOCKED DECISION

Default Forecast planning view should prioritize:

```text
Active Products
+
Valid Effective BOM for selected Period
```

Products with Forecast-relevant issues such as:

- Missing BOM
- No effective Approved BOM
- Inactive Product

must not silently disappear.

They should be visible through explicit warnings/filter/state so incomplete planning data can be corrected.

---

## 26. Valid BOM by Period

Because BOM is effective-dated, Product BOM readiness may vary across months.

The system must evaluate BOM validity against the selected Business Period / relevant planning date.

MRP-07 does not perform material explosion, but its UI/readiness integration must not assume today's BOM is valid for every future month.

---

## 27. Actual Production and BOM Date

### LOCKED DECISION

Future material Actual Consumption must use the BOM that was effective on the **actual production date**.

Conceptually:

```text
Daily Production Actual Date
          ↓
Resolve Effective BOM for that Date
```

not:

```text
Use today's BOM
```

MRP-07 stores Actual dates so this future calculation remains possible.

MRP-07 does not calculate material consumption yet.

---

## 28. Forecast Revision Traceability for Future MRP Runs

### LOCKED DECISION

Future MRP Calculation Run must be able to identify which Forecast Revision was used.

Therefore Forecast Revision IDs must be stable and referenceable.

Future run trace conceptually:

```text
MRP Run
  ↓
Product + Period
  ↓
Forecast Revision ID
```

MRP-07 does not implement the MRP Run entity yet.

---

## 29. Period Lifecycle

### LOCKED DECISION

Business Period status:

```text
OPEN
CLOSED
```

These are system-controlled machine states.

They are not user-configurable Reference Data.

---

## 30. Open Period

While a Period is Open, authorized users may:

- create/change Forecast through new Forecast Revisions
- enter Daily Actual
- correct Daily Actual

subject to normal validation and audit rules.

---

## 31. Closed Period

### LOCKED DECISION

When a Period is Closed:

- normal Forecast changes are blocked
- normal Daily Actual edits are blocked
- Monthly Actual becomes operationally final for that close
- historical Forecast revisions remain visible
- all data remains readable

Close does not delete or snapshot by copying redundant values unless MRP-07A identifies a concrete integrity need.

---

## 32. Reopen

### LOCKED DECISION

Closed Period may be reopened only with:

- explicit permission
- Reason
- Audit trail

Reopen is not a normal casual edit action.

After correction, Period may be Closed again.

---

## 33. Reopen Audit

Audit must preserve conceptually:

- Period
- previous status
- new status
- Reason
- actor
- timestamp

Changes made while reopened must be traceable through their own revision/audit records.

---

## 34. Close Preconditions

MRP-07A should propose the minimum useful Close validation.

It must not invent excessive bureaucracy.

Potential validation examples:

- no invalid Daily Actual records
- no unresolved data integrity error

Missing Forecast or missing BOM may be surfaced as warnings depending on business context rather than always blocking Close.

Any blocking rule beyond core integrity requires explicit approval.

---

## 35. Current Period Determination

Current Business Period must be derived from:

- current business date
- Jalali calendar conversion
- `Asia/Tehran` timezone

Do not rely on browser timezone alone.

---

## 36. Forecast UI Shape

### LOCKED DECISION

Primary Forecast view is row-oriented:

```text
Product
Forecast
Actual
Remaining
Status
```

not a giant wide sheet with repeated column groups per Product.

Recommended additional columns:

- Official Item Code (`Item.BusinessNumber`, read-only `PRD-{sequence:D4}`)
- Product Category
- Business Unit
- BOM Readiness / Warning
- Forecast Revision
- Period Status

Exact column set may be refined in MRP-07A without changing core meaning.

---

## 37. Forecast Filters

Baseline filters:

- Business Period / Period Range
- Product
- Product Category
- Business Unit
- Active / Inactive
- BOM readiness/warning state where useful

UI should support typical 12-month planning without loading all long-term history.

---

## 38. Forecast Range View

Although storage grain is Product + Month, UI may show:

- one selected month
- several months
- rolling 12 months

The implementation must avoid returning a spreadsheet-style uncontrolled 20-year matrix.

Server-side query/paging/range design should fit expected volume.

---

## 39. Daily Actual UI

MRP-07 should provide a simple Daily Actual entry/review interface.

Baseline:

- Date
- Product
- Actual Qty

Filters:

- Date range
- Product
- Product Category where useful

No Production Module complexity should be introduced.

---

## 40. Derived Forecast Status

Useful derived states may include:

```text
Future
Current
Past
Missing Forecast
BOM Missing
Closed
```

These are UI/read-model states, not necessarily persistent machine workflow fields.

MRP-07A must avoid storing redundant flags that can drift from dates/status.

---

## 41. Active / Inactive Product Behavior

New Forecast entry should normally target Active Products.

Historical Forecast and Actual for later-deactivated Products must remain resolvable.

Inactive Product history must not be deleted.

If an inactive Product still has a future Forecast, UI should surface it explicitly rather than silently ignoring it.

---

## 42. Forecast Deletion

Normal Forecast history must not be hard deleted.

If an incorrectly created revision requires correction, use the approved revision/history model.

MRP-07A should propose the smallest safe mechanism.

Do not implement arbitrary destructive history deletion.

---

## 43. Daily Actual Deletion / Correction

Normal correction should preserve auditability.

Whether correction is implemented as:

- update with audit
or
- explicit adjustment history

is a technical choice for MRP-07A based on existing audit infrastructure.

Do not create a full production ledger in this Package.

---

## 44. API / Application Use Cases

Required use cases include:

### Business Period
- list/query Periods
- determine current Period
- close Period
- reopen Period with Reason

### Forecast
- list/query Forecast by Product/Period/range
- get Forecast revision history
- create initial Forecast revision
- create new Forecast revision
- resolve Official Current Forecast
- derive Past/Current/Future planning source
- derive Remaining Forecast

### Actual
- list Daily Actual
- create Daily Actual
- correct Daily Actual in Open Period
- derive Monthly Actual
- derive Actual MTD

Avoid unrestricted dangerous generic CRUD.

---

## 45. Permissions

At minimum architecture should distinguish:

- View Forecast / Actual
- Manage Forecast
- Manage Daily Actual
- Close Period
- Reopen Period

Reopen permission should remain separate from ordinary entry where authorization architecture supports it.

Do not create parallel Identity infrastructure.

---

## 46. Auditability

Important events must be compatible with Audit:

- Forecast Revision created
- Forecast Qty changed through new Revision
- Daily Actual created
- Daily Actual corrected
- Period Closed
- Period Reopened
- Reopen Reason

Forecast history itself is a primary historical record and must not rely solely on generic audit logs.

---

## 47. Database Constraints

Expected protections include where practical:

- unique logical Product + Date Daily Actual
- valid non-negative quantities
- stable Forecast Revision identity
- one logical Official Current Forecast per Product + Period
- valid Period status
- valid Product references
- valid Period boundaries
- no destructive cascade of historical Forecast/Actual

Exact implementation strategy is determined in MRP-07A.

---

## 48. Numeric Precision

Use decimal/numeric for:

- Forecast Qty
- Daily Actual Qty
- Monthly Actual aggregate
- Remaining Forecast

Do not use binary floating point.

---

## 49. Date / Time Rules

Canonical:

- dates: Gregorian
- timezone: Asia/Tehran

Business presentation:

- Jalali

Business Period identity:

- Jalali Year / Month

Daily Actual belongs to the Business Period determined from its actual production date.

---

## 50. Performance

Support efficient:

- Product + Period Forecast lookup
- Product forecast revision history
- selected Period range query
- Monthly Actual aggregation
- Actual MTD
- Daily Actual by date/product
- BOM readiness integration without N+1

Avoid:

- loading 20 years by default
- recalculating all historical Actual for every grid cell
- client-side aggregation of large datasets
- N+1 Product/BOM lookup
- speculative Redis/caching

---

## 51. Forecast Revision Concurrency

Concurrency-sensitive operations include:

- creating simultaneous Forecast revisions for same Product + Period
- selecting/marking Official Current revision

MRP-07A must propose a robust strategy so two concurrent edits cannot leave:

- two current revisions
or
- lost history.

---

## 52. Daily Actual Concurrency

Concurrent entry/correction for the same:

```text
Product + Date
```

must not create duplicate logical Actual records.

Database/application design should enforce this.

---

## 53. Period Close Concurrency

Closing a Period must be transactional enough to prevent normal edits from being committed as if the Period were still Open after Close.

Reopen similarly must preserve clear status transitions.

MRP-07A must propose the smallest robust approach.

---

## 54. Forecast vs Actual Planning Read Model

The Feature should expose enough derived information for a future Planning Engine:

### Past Period
```text
Demand Source = Actual
```

### Current Period
```text
Actual To Date
+
Remaining Forecast
```

### Future Period
```text
Demand Source = Forecast
```

MRP-07 stops at this demand-source/read-model boundary.

It does not explode this demand through BOM.

---

## 55. Current-Month Future-Day Distribution Boundary

The future MRP engine may distribute:

```text
Remaining Forecast
```

over remaining planning days.

Until a Production Calendar exists, baseline future logic may use remaining calendar days.

However, MRP-07 does NOT implement that daily distribution.

It only supplies:

- period dates
- Actual to date
- Remaining Forecast

Daily demand distribution belongs to the Calculation Engine.

---

## 56. Production Calendar Boundary

Future Production Calendar may introduce:

- Working Days
- Shifts
- Capacity
- Holidays
- planned production schedule

MRP-07 must not create these structures prematurely.

Its Business Period/date design must remain compatible with them.

---

## 57. Seed / Migration

Codex must not invent Forecast or Production Actual data.

If legacy Forecast/Actual dataset is not explicitly supplied:

- build capability
- do not fabricate production planning records

Legacy import will be handled as a controlled migration/import task.

---

## 58. Explicit Non-Scope

MRP-07 must NOT implement:

- BOM Explosion
- Scrap material formula
- daily material demand distribution
- Inventory
- Opening Balance
- Warehouse transactions
- Coverage
- Mapping
- MRP Calculation Engine
- Order Planning
- Purchase Orders
- Supply recommendation
- Cash Flow
- Production shifts
- Production lines
- Work Centers
- Capacity planning
- full Production Module
- Forecast Accuracy analytics dashboard
- Workflow Engine beyond Period Open/Close lifecycle

---

## 59. Acceptance Criteria

MRP-07 is acceptable when:

1. Business Planning Period is Jalali month.
2. Canonical dates remain Gregorian.
3. Period boundaries correctly support Jalali month lengths/leap years.
4. Database has no hard 20-year limit.
5. Forecast grain is Product + Month.
6. Forecast Qty is decimal/numeric and >= 0.
7. Forecast revisions preserve history.
8. Exactly one Official Current Forecast is resolvable per Product + Period.
9. Historical Forecast revisions are not overwritten/zeroed.
10. Daily Actual grain is Product + Date.
11. Only one logical Daily Actual aggregate exists per Product + Date.
12. Daily Actual is temporary MRP input pending future Production Module.
13. Monthly Actual is derived from Daily Actual.
14. No separate manually editable Monthly Actual exists.
15. Past planning truth uses Actual while Forecast history remains.
16. Current Remaining Forecast uses `MAX(Forecast - Actual MTD, 0)`.
17. Current-period allocation excludes As-Of and begins at As-Of + 1 calendar day.
18. Allocation stays within the original Jalali Planning Period; zero eligible days produces an explicit unallocatable state.
19. Daily residual handling preserves the exact Remaining Forecast total and prevents same-day Actual + Remaining double counting.
20. Actual may exceed Forecast.
21. Future planning source uses Forecast.
22. Missing Forecast differs from zero Forecast.
23. Forecast may exist even if BOM is missing.
24. BOM-missing Products are surfaced as warnings rather than silently lost.
25. Future actual-material calculation can resolve BOM by Actual Production Date.
26. Forecast Revision IDs are stable for future MRP Run traceability.
27. Period lifecycle supports OPEN/CLOSED.
28. Closed Period blocks normal Forecast/Actual edits.
29. Reopen requires permission + Reason + Audit.
30. Forecast UI is row-oriented.
31. Required filters exist.
32. Long-range data is queried by selected range.
33. Server-side validation exists.
34. Forecast/Actual/Close concurrency rules are tested.
35. No BOM explosion/Inventory/PO logic leaks into MRP-07.

---

## 60. Locked Decisions

The following are locked for MRP-07:

1. Forecast Business Period = Jalali month.
2. Canonical date storage = Gregorian.
3. Business timezone = Asia/Tehran.
4. Forecast grain = Product + Month.
5. Forecast Qty >= 0 and decimal/numeric.
6. Daily Actual is temporary MRP input until Production Module.
7. Daily Actual grain = Product + Date.
8. One logical Daily Actual aggregate per Product + Date.
9. Monthly Actual is Derived.
10. No manual Monthly Actual field.
11. Forecast History is preserved.
12. One Official Current Forecast per Product + Period.
13. Past Period planning truth = Actual.
14. Historical Forecast is preserved for past periods.
15. Current Remaining Forecast = MAX(Forecast - Actual MTD, 0).
16. Current-period allocation contract uses `Demand Date > As-Of` through the same Jalali Period End; As-Of allocation is zero.
17. No cross-period spill; zero eligible future days surfaces an explicit unallocatable state.
18. Deterministic residual handling preserves the exact Remaining Forecast total.
19. Actual may exceed Forecast.
20. Future Period planning truth = Forecast.
21. OPEN/CLOSED Period lifecycle.
22. Closed Period blocks normal edits.
23. Reopen requires permission, Reason and Audit.
24. Forecast UI uses Product rows, not repeated Product column groups.
25. Filters include Product/Category/Business Unit/Status/Period.
26. Default planning view prioritizes Active Products with valid BOM.
27. Missing/invalid BOM must be surfaced explicitly.
28. Forecast may be stored even before BOM readiness.
29. Actual Production Date is preserved for future effective-BOM resolution.
30. Future MRP Run can reference exact Forecast Revision.
31. Database does not impose 20-year limit.
32. UI loads selected ranges rather than all history.
33. Daily demand distribution remains owned by MRP-10; MRP-07 exposes the locked allocation contract only.

---

## 61. Open Question Resolution

The previous open question:

`Temporary source of Daily Production Actual until Production Module exists`

is now CLOSED.

Approved answer:

```text
MRP-07 provides temporary Product + Date aggregate Daily Actual input.
Future Production Module becomes the authoritative source without double counting.
```

The previous open question:

`Closed-period editing`

is now CLOSED.

Approved answer:

```text
Period = OPEN / CLOSED
Closed blocks normal Forecast/Actual edits.
Reopen requires explicit permission + Reason + Audit.
```

The Business Period calendar decision is also CLOSED:

```text
Business Month = Jalali
Canonical Date Storage = Gregorian
```

---

## 62. Remaining Relevant Open Questions

The following are intentionally deferred to later Packages:

- Exact Scrap Formula
- daily Remaining Forecast distribution method inside Calculation Engine
- future Production Calendar / working-day behavior
- MRP Calculation Run snapshot architecture
- Forecast Accuracy metrics/dashboard details

None blocks MRP-07.

---

# B. Prompt 07A — Forecast / Actual Analysis

```text
# MRP-07A — Forecast / Actual Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior Packages were implemented merely because they were planned.

TASK:
Analyze MRP-07 Forecast / Actual.

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

1. Understanding of Forecast/Actual role as future MRP demand source.
2. Actual Repository findings.
3. Jalali Business Period design with Gregorian canonical dates.
4. Propose one trusted centralized Jalali↔Gregorian conversion approach.
5. Ensure correct Jalali month lengths and leap-year handling.
6. Forecast relational model:
   - Product
   - Product identity resolves and displays only the read-only Official Item Code from `Item.BusinessNumber`; do not consume deprecated `Item.Code`/`Item.NormalizedCode`
   - Period
   - Forecast Revision
   - Official Current Forecast
7. Propose robust one-current-revision mechanism without duplicate editable Forecast values.
8. Forecast Revision concurrency strategy.
9. Forecast revision metadata/history.
10. Daily Production Actual model:
    - Product + Date
    - one logical aggregate per day
    - correction/audit
11. Future Production Module source-of-truth migration boundary.
12. Monthly Actual and Actual MTD derivation.
13. Past/Current/Future planning-source rules.
14. Confirm:
    Remaining Forecast = MAX(Current Forecast - Actual MTD, 0)
    Analyze the MRP-10 allocation contract: exclude As-Of, start at As-Of + 1 calendar day, remain within the same Jalali period, prevent same-day Actual + Remaining double counting, preserve exact residual totals, and surface zero-eligible-day remainder explicitly.
15. Actual > Forecast behavior.
16. Missing Forecast vs zero Forecast.
17. Forecast storage independent from BOM readiness.
18. Product/BOM readiness warning integration.
19. Effective BOM-by-Actual-Date future boundary.
20. Forecast Revision traceability for future MRP Runs.
21. OPEN/CLOSED Period model.
22. Close/Reopen permissions, Reason and audit.
23. Transaction/concurrency strategy for Close vs concurrent edits.
24. Minimal Close validation; do not invent excessive approval workflow.
25. Forecast UI:
    - row-oriented
    - selected period/range
    - Product/Category/Business Unit filters
    - Actual/Remaining/Status
    - BOM warnings
26. Daily Actual UI.
27. API/application use cases.
28. Permissions/Audit.
29. DB constraints/indexes.
30. Numeric precision.
31. Performance for long history and typical 12-month range.
32. Testing plan.
33. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..07 and actual Repository.
34. Smallest implementation sequence.
35. Explicit non-scope.

Testing proposal must cover at minimum:
- Jalali period conversion
- Jalali leap-year/Esfand boundaries
- Product+Period Forecast
- Forecast Revision history
- exactly one Official Current Forecast
- concurrent Forecast revisions
- Forecast zero vs missing
- Daily Actual unique Product+Date
- concurrent Daily Actual entry
- Daily Actual correction
- Monthly Actual sum
- Actual MTD
- past planning source
- current Remaining Forecast
- Actual > Forecast
- future planning source
- Forecast allowed before BOM
- missing BOM warning/readiness
- inactive Product history
- OPEN/CLOSED edits
- Close blocks normal edit
- Reopen requires Reason/permission boundary
- close/edit concurrency
- no manual Monthly Actual
- no BOM explosion
- no daily material-demand distribution

Output:
1. Understanding
2. Repository Findings
3. Business Period / Calendar Design
4. Forecast Revision Model
5. Daily Actual Model
6. Planning Truth Rules
7. BOM Readiness Boundary
8. Period Lifecycle
9. Validation / Constraints
10. API Design
11. UI Design
12. Permissions / Audit
13. Performance / Concurrency
14. Tests
15. Risks / Contradictions
16. Implementation Plan
17. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 07B — Forecast / Actual Implementation

```text
# MRP-07B — Implement Forecast / Actual

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-07A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-07 Forecast / Actual design.

If MRP-07A contains unresolved material contradictions, STOP.

BUSINESS PERIOD
Implement approved Jalali Business Month representation.

Rules:
- Business Period = Jalali Year/Month
- canonical dates = Gregorian
- timezone = Asia/Tehran
- correct Jalali month/leap-year boundaries
- reuse centralized trusted conversion/date logic
- do not scatter custom conversion code

FORECAST
Implement approved:
- Product + Period Forecast structure
- Product references use UUID internally and expose `Item.BusinessNumber` as the only read-only Official Item Code; do not add, accept or derive a second Product Code
- immutable Forecast Revisions
- Forecast Qty >= 0
- one Official Current Forecast per Product+Period
- historical revisions retained
- stable Forecast Revision ID

Do not create independently editable duplicate Current Forecast values.

DAILY ACTUAL
Implement temporary MRP Daily Production Actual:
- Product
- Production Date
- Actual Qty

Rules:
- one logical record per Product+Date
- Qty >= 0
- decimal/numeric
- correction only while Period Open
- audit-compatible

Do not add Shift/Line/Work Center/Batch.

MONTHLY ACTUAL
- derive from Daily Actual
- do not create manual Monthly Actual input

ACTUAL MTD
- derive from Daily Actual through evaluation date

PLANNING SOURCE
Implement read/application logic:

Past:
- Actual is planning truth
- Forecast history remains

Current:
- Remaining Forecast = MAX(Official Forecast - Actual MTD, 0)
- expose the approved allocation contract to MRP-10: `Demand Date > As-Of` and `Demand Date <= Planning Period End`
- As-Of receives zero Remaining Forecast; do not spill into the next period
- when zero eligible future days remain with positive quantity, return an explicit unallocatable planning state rather than allocating or discarding it

Future:
- Official Forecast is planning source

Do not cap Actual to Forecast.

MISSING FORECAST
Preserve distinction:
- missing
- explicit zero

BOM BOUNDARY
Forecast may exist without BOM.

Integrate approved derived/warning view so Products with:
- missing effective BOM
- invalid planning BOM
are surfaced rather than silently excluded.

Do NOT perform BOM explosion.

PERIOD LIFECYCLE
Implement:
- OPEN
- CLOSED

Closed:
- blocks normal Forecast revisions
- blocks normal Daily Actual changes

Reopen:
- explicit permission boundary
- Reason required
- auditable

Implement approved concurrency/transaction behavior from MRP-07A.

FORECAST HISTORY
Do not hard-delete/overwrite normal history.

FUTURE TRACEABILITY
Keep Forecast Revision stable/referenceable for future MRP Run.

Do not implement MRP Run yet.

FRONTEND
Implement approved:

Forecast:
- row-oriented Product grid
- Forecast
- Actual
- Remaining
- status/warnings
- period/range selection
- Product/Category/Business Unit/Active filters
- revision history access

Daily Actual:
- simple date/product/qty entry and review
- filters
- correction subject to Open Period

Period:
- Open/Close/Reopen UI
- Reopen Reason
- clear Closed state

Follow UI-BRAND-RULES.md.
Reuse approved grid/form patterns.

PERMISSIONS/AUDIT
Reuse existing platform.
Do not build parallel Identity/Audit systems.

BACKEND
Implement only approved:
- domain/data model
- EF Core configuration
- migrations
- application use cases/services
- API
- validation
- errors

TESTS
At minimum:
- Jalali period conversion
- leap-year/Esfand
- Forecast Qty validation
- revision history
- one current Forecast
- concurrent revisions safe
- missing vs zero
- one Daily Actual per Product+Date
- concurrent daily entry safe
- Daily Actual correction
- Monthly Actual derived
- Actual MTD derived
- past/current/future planning-source rules
- Remaining Forecast floor at zero
- As-Of excluded from current-period Remaining Forecast allocation
- next-day start and Jalali period-end boundary
- normal mid-period allocation: As-Of zero, next day positive, exact total preserved
- exact-division and deterministic decimal-residual scenarios
- final Jalali calendar day with positive remainder produces explicit unallocatable state and no next-period spill
- Actual >= Forecast produces no future allocation
- Jalali leap/month boundary uses canonical Gregorian conversion
- no same-day Actual + Remaining Forecast double count
- Actual > Forecast preserved
- Forecast without BOM allowed
- missing BOM warning
- active/inactive historical behavior
- Closed Period blocks normal edits
- Reopen requires Reason
- close/edit concurrency integrity
- no manual Monthly Actual
- no BOM explosion
- no Production Module scope leakage

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend install if required
- frontend build/lint
- startup/health checks

Do not claim checks not run.

SCOPE GUARD
Do NOT implement:
- BOM explosion
- Scrap requirement formula
- daily material demand distribution
- Inventory
- Coverage
- Mapping
- MRP Calculation Engine
- Order Planning
- Purchase Orders
- Cash Flow
- Production shifts
- Production lines
- Work Centers
- Capacity planning
- full Production Module
- Forecast Accuracy dashboard

Final report:
- Changed
- Database
- Business Period
- Forecast Revisions
- Daily Actual
- Derived Actual / Remaining Forecast
- Period Lifecycle
- BOM Readiness Boundary
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-08.
```

---

# D. Prompt 07C — Forecast / Actual Independent Audit

```text
# MRP-07C — Forecast / Actual Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-07A Analysis
- MRP-07B implementation report

Inspect all MRP-07 implementation files.

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
   - BOM explosion
   - material-demand distribution
   - Inventory
   - Mapping
   - Order Planning
   - PO
   - full Production Module

2. Business Period
   - Jalali Year/Month
   - Gregorian canonical storage
   - Asia/Tehran
   - correct month boundaries
   - leap-year/Esfand correctness
   - no formatted-string date logic

3. Forecast Model
   - Product+Period grain
   - Product UUID is the relationship key and `Item.BusinessNumber` is the only displayed/searched Official Item Code
   - no dependency on deprecated `Item.Code`/`Item.NormalizedCode` and no second active Product Code
   - Qty >= 0
   - decimal/numeric
   - immutable Revision history
   - exactly one Official Current Forecast
   - no duplicate editable current value

4. Forecast History
   - no overwrite
   - no zeroing past Forecast
   - stable revision identity
   - future MRP traceability preserved

5. Daily Actual
   - Product+Date aggregate
   - one logical record/day
   - no Shift/Line/Batch leakage
   - correction/audit behavior

6. Monthly Actual
   - Derived from Daily Actual
   - no independent manual Monthly Actual

7. Planning Truth
   Verify:
   - Past = Actual
   - Current Remaining = MAX(Forecast - Actual MTD, 0)
   - Future = Forecast
   - Actual can exceed Forecast
   - Forecast history remains

8. Missing vs Zero Forecast
   - not conflated

9. BOM Boundary
   - Forecast may exist without BOM
   - BOM missing surfaced
   - no material explosion
   - no use of today's BOM for historical Actual boundary

10. Period Lifecycle
    - OPEN/CLOSED
    - Closed blocks normal edits
    - Reopen permission boundary
    - Reopen Reason
    - auditability
    - close/edit concurrency

11. Historical Integrity
    - inactive Products remain resolvable
    - no destructive cascades
    - old Forecast/Actual retained

12. Database Integrity
    - constraints
    - indexes
    - one Product+Date actual
    - one current Forecast
    - revision concurrency
    - numeric precision
    - date integrity

13. API
    - safe use cases
    - server-side validation
    - no unrestricted dangerous CRUD

14. UI
    - Brand Rules
    - RTL
    - row-oriented Forecast
    - filters
    - Actual/Remaining
    - BOM warnings
    - revision history
    - Daily Actual
    - Close/Reopen clarity

15. Permissions / Auditability

16. Performance
    - selected-range queries
    - no 20-year default load
    - Monthly Actual aggregation
    - no N+1 Product/BOM lookup
    - no premature Redis/caching

17. Tests
    Verify meaningful assertions especially:
    - Jalali leap-year boundaries
    - concurrent Forecast revisions
    - Product+Date Actual uniqueness
    - Closed Period concurrency
    - Current Remaining Forecast
    - As-Of excluded and allocation starts at As-Of + 1
    - no same-day Actual + Remaining Forecast double count
    - no cross-period spill at Jalali Period End
    - exact division and deterministic residual preserve the exact total
    - zero eligible future days produces an explicit unallocatable state
    - Actual >= Forecast produces no allocation
    - Jalali leap/month boundary uses canonical Gregorian conversion

18. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..07
    - approved MRP-07A
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

# E. Final Pre-Codex Audit — MRP-07

## Result

`PASS`

## Audited Approved Decisions

تمام تصمیم‌های تأییدشده Package 07 پوشش داده شده‌اند:

1. Forecast grain = Product + Month.
2. Business Month = Jalali.
3. Canonical date storage = Gregorian.
4. Forecast Qty >= 0 / Decimal.
5. Monthly Actual is Derived.
6. Daily Actual temporary MRP source.
7. Daily Actual grain = Product + Date.
8. One logical Daily Actual record per Product+Date.
9. Forecast History preserved.
10. One Official Current Forecast per Product+Period.
11. Past planning truth = Actual.
12. Current Remaining = MAX(Forecast - Actual MTD, 0).
13. Future planning truth = Forecast.
14. Actual may exceed Forecast.
15. Period lifecycle = Open / Closed.
16. Reopen requires Permission + Reason + Audit.
17. Row-oriented Forecast UI.
18. Product/Category/Business Unit/Period filters.
19. BOM readiness integrated as warning/eligibility.
20. Actual Production Date preserved for effective-BOM resolution.
21. Forecast Revision traceability preserved.
22. Long-term history has no hard 20-year DB cap.
23. UI queries selected ranges.
24. No manual Monthly Actual.

## Open Question Resolution

Closed:

### Temporary Daily Production Actual Source

```text
MRP-07 temporary Product+Date aggregate input
→ future Production Module becomes source of truth
```

### Closed-Period Editing

```text
OPEN / CLOSED
Closed blocks normal edits.
Reopen requires Permission + Reason + Audit.
```

### Planning Calendar

```text
Business Period = Jalali Month
Canonical Date Storage = Gregorian
```

## Cross-Package Consistency

- MRP-02 Product remains Forecast owner.
- MRP-06 BOM remains independent and effective-dated.
- Forecast can exist before BOM completeness.
- Material Planning eligibility still depends on valid effective BOM.
- Actual Production Date supports future historical BOM resolution.
- Forecast History supports later Forecast Accuracy.
- Forecast Revision ID supports future MRP Calculation Run traceability.
- No duplicate manual Monthly Actual source exists.
- MRP-07 supplies demand-source facts but does not explode demand through BOM.

## Architecture Corrections Applied

- Explicitly separated missing Forecast from zero Forecast.
- Prevented duplicate monthly Actual source.
- Added Product+Date logical uniqueness for Daily Actual.
- Forecast revision concurrency marked as integrity-sensitive.
- Period Close vs concurrent edit marked as transactional/concurrency-sensitive.
- Forecast and BOM readiness were separated so Engineering incompleteness does not destroy demand data.
- Long-term storage is decoupled from UI working range.
- Jalali Business Period is separated from Gregorian canonical date arithmetic.
- Future Production Module migration boundary prevents double counting.

## Remaining Intentional Open Questions

Not blockers for MRP-07:

- Exact Scrap Formula.
- Current-month remaining Forecast daily distribution in Calculation Engine.
- future Production Calendar / Working Days.
- exact MRP Calculation Run snapshot model.
- Forecast Accuracy reporting details.

## Prompt Audit

### MRP-07A
PASS

Analysis-only, Repository-aware, and specifically audits calendar correctness, revision concurrency, Period Close concurrency and BOM boundary.

### MRP-07B
PASS

Implementation is bounded to Forecast/Actual and explicitly blocks BOM Explosion, Inventory, MRP Calculation and Production Module scope creep.

### MRP-07C
PASS

Independent read-only audit checks Jalali calendar integrity, revisions, actual aggregation, planning truth, period locking, performance and specification drift.

## Final Decision

`MRP-07 Forecast / Actual` is approved for the project Prompt Baseline.

It should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives the final cross-package audit.
