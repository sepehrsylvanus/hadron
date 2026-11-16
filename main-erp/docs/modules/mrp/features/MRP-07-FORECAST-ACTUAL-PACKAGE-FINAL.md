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

The client identifies a Business Period by Jalali Year and Jalali Month. The server generates the canonical Gregorian Start Date and End Date through the centralized calendar service; these boundaries are immutable after creation. A newly persisted Business Period defaults to `OPEN`.

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

### Forecast Base Unit Snapshot — LOCKED OWNER DECISION

`ProductBaseUnitId` belongs to the Product + Business Period Forecast Series and is captured by the server once when that Series is created. It is immutable and inherited by every Forecast Revision in the Series; revisions must not carry independently changeable unit snapshots.

If the Product's current Base Unit later differs from the Series snapshot, Forecast mutation is blocked with `BASE_UNIT_CHANGED`. The system must not convert quantities, rewrite history, or permit mixed units inside one Product + Period.

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

For Revision 1, Reason is optional. For Revision 2 and every later Revision, Reason is required.

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

MRP-07 continues to provide the v1 persistence/API source for Daily Production Actual, while its user-facing entry, review, and correction surface belongs to the top-level Production Workspace. This navigation separation does not create a second Actual source or change MRP planning formulas.

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
- Production Date after the current `Asia/Tehran` business date is blocked with `FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED`
- immutable Unit snapshot captured by the server
- no mixed units, UOM conversion, or historical unit rewrite inside one Product + Period

On entry or correction, the server compares the Product's current Base Unit with the ForecastSeries snapshot when present and with every existing Daily Actual Unit snapshot in that Product + Period. Any drift or mismatch is blocked with `BASE_UNIT_CHANGED`.

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

Those belong to a future full Production domain expansion and are not introduced by the minimal Production Workspace.

---

## 15. Daily Actual Correction

An existing Product + Date Actual may be corrected while its Business Period is Open.

Initial entry requires no correction Reason. Every correction requires Reason and creates durable Actual Revision history.

Do not create duplicate same-day records whose sum becomes ambiguous.

The persistence model should enforce one logical aggregate per:

```text
Product + Production Date
```

---

## 16. Future Production Module Integration

### LOCKED DECISION

MRP-07 Daily Actual remains the v1 technical source behind the Production Workspace.

Future architecture:

```text
Production Module Daily Actual
            ↓
MRP reads Actual
```

If a future full Production module later replaces this technical source of truth:

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

MRP-07 performs no daily allocation. It exposes `EligibleAllocationStart = evaluationDate + 1`, the current Period End, and Remaining Forecast. If Remaining Forecast is positive and no eligible future calendar day remains, it returns the stable state `CURRENT_PERIOD_REMAINDER_UNALLOCATABLE`. It must not allocate to As-Of, spill into another Period, discard, or round the quantity. Deterministic residual allocation and exact daily-total preservation remain owned by MRP-10.

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

Readiness/warning behavior must preserve this distinction with stable machine-readable states.

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

The readiness evaluation window is locked:

- Future Period: evaluate approved effective BOM coverage for the full Period.
- Current Period: evaluate only `evaluationDate + 1` through Period End.
- Past Period: BOM readiness is historical context, not a planning gate.

Forecast remains storable regardless of BOM readiness. MRP-07 does not perform material explosion, and its UI/readiness integration must not assume today's BOM is valid for every future month.

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

The client supplies Jalali Year/Month; server-generated Gregorian boundaries are immutable. New persisted Periods default to `OPEN`.

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

Until central Identity exists, domain history uses a nullable `ActorIdentifier` and request/audit context rather than a User or Employee foreign key. Forecast Revisions, Actual Revisions, and Period Lifecycle records are durable domain history and must not depend solely on the best-effort generic audit sink.

---

## 34. Close Preconditions

Close blocks only on core data-integrity errors, such as invalid Daily Actual or unresolved transaction/concurrency integrity. Missing Forecast or missing BOM is surfaced as a warning and does not independently block Close. Any additional blocking business rule requires separate Owner approval.

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

The Production Workspace must provide a simple Daily Actual entry/review interface, separate from the MRP Production Forecast destination.

Baseline:

- Date
- Product
- Actual Qty

Filters:

- Date range
- Product
- Product Category where useful

No Shift, Line, Machine, Work Center, Batch, or other full Production Module complexity should be introduced.

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

If an incorrectly created revision requires correction, create a new immutable Forecast Revision with the required Reason. Do not expose normal Forecast deletion.

Do not implement arbitrary destructive history deletion.

---

## 43. Daily Actual Deletion / Correction

Normal correction creates a new immutable Actual Revision with required Reason and advances the aggregate's current revision pointer. Do not expose hard delete or rely only on generic audit.

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

Reopen permission remains separate from ordinary entry. Authorization enforcement is deferred to central Identity; preserve distinct permission/use-case boundaries now, but do not create parallel Identity infrastructure or fake User/Employee records.

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
- one immutable Product Base Unit snapshot per Forecast Series
- immutable Daily Actual Unit snapshots and no mixed Product+Period units
- durable immutable Forecast/Actual revision history

Application transactions, aggregate Version checks, row locking in deterministic Period-then-aggregate order, and database uniqueness/check constraints must jointly enforce these protections.

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
12. Daily Actual is entered from the Production Workspace and remains the temporary MRP-07 technical input pending a future full Production domain module.
13. Monthly Actual is derived from Daily Actual.
14. No separate manually editable Monthly Actual exists.
15. Past planning truth uses Actual while Forecast history remains.
16. Current Remaining Forecast uses `MAX(Forecast - Actual MTD, 0)`.
17. The current-period allocation contract excludes As-Of and exposes a start of As-Of + 1 calendar day; MRP-07 does not allocate daily quantities.
18. Allocation stays within the original Jalali Planning Period; zero eligible days produces an explicit unallocatable state.
19. MRP-07 performs no daily allocation; it exposes the boundary contract and MRP-10 preserves exact residual totals.
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
36. Forecast Revision 1 Reason is optional; Revision 2+ Reason is required.
37. Initial Daily Actual needs no correction Reason; each correction requires Reason and durable history.
38. Future Production Actual is blocked using the Asia/Tehran business date.
39. ForecastSeries owns one immutable ProductBaseUnitId snapshot inherited by all revisions.
40. Daily Actual owns an immutable Unit snapshot; mixed Product+Period units and UOM conversion are forbidden.
41. Future BOM readiness covers the full Period; current readiness covers evaluationDate+1 through Period End; past readiness is not a planning gate.
42. Jalali Year/Month is client input; Gregorian boundaries are server-generated and immutable; new Period defaults OPEN.
43. Forecast/Actual/Period histories are durable domain history and use nullable ActorIdentifier until Identity exists.
44. Reference Data ownership, lifecycle, historical resolution, and bilingual display pass the MRP-01 integrity gate.

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
34. Revision 1 Reason is optional; Revision 2+ Reason is required.
35. Initial Daily Actual needs no correction Reason; every correction requires Reason.
36. Future-dated Production Actual is blocked with `FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED` against the Asia/Tehran business date.
37. ForecastSeries owns one immutable ProductBaseUnitId snapshot; all revisions inherit it.
38. Daily Actual owns an immutable Unit snapshot; mixed Product+Period units, conversion, and history rewrite are forbidden; drift returns `BASE_UNIT_CHANGED`.
39. Future BOM readiness covers the full Period; current readiness covers only evaluationDate+1 through Period End; past readiness is not a planning gate.
40. MRP-07 returns `CURRENT_PERIOD_REMAINDER_UNALLOCATABLE` when positive Remaining has no eligible current-period date and performs no daily allocation.
41. Client identifies Jalali Year/Month; server owns immutable Gregorian boundaries; new Period defaults OPEN.
42. Forecast Revision, Actual Revision, and Period Lifecycle records are durable domain history with nullable ActorIdentifier until central Identity exists.
43. Authorization enforcement is deferred to central Identity while permission/use-case boundaries remain explicit.

---

## 61. Open Question Resolution

### MRP-14 Forecast Accuracy Amendment

For Forecast Accuracy, a `CLOSED` Business Period certifies that Product Actual Production for that Period is complete. For an in-scope Product with no Daily Actual rows in that Closed Period, Actual Production is exactly zero, not `ACTUAL_NOT_AVAILABLE`.

Forecast Accuracy cutoffs are locked as follows:

- Baseline cutoff is the start of `GregorianStartDate` in `Asia/Tehran`, converted to UTC; the last Official Forecast available at or before that instant is the Baseline.
- Final cutoff is the lifecycle timestamp of the latest effective `OPEN → CLOSED` event that leaves the Period currently Closed.
- If a Period is Reopened and Closed again, the later effective Close controls Final Forecast selection.
- Reopen/Close history remains durable; Analytics must not infer the cutoff from current timestamps or live mutable context.
- `ACTUAL_NOT_AVAILABLE` is reserved for explicitly identified legacy/import incompleteness and must never be inferred merely from absence of Daily Actual rows in a canonical Closed Period.

The previous open question:

`Temporary technical source of Daily Production Actual until a full Production domain module exists`

is now CLOSED.

Approved answer:

```text
MRP-07 provides temporary Product + Date aggregate Daily Actual persistence/API.
The v1 user-facing surface is the Production Workspace; a future full Production domain module may become the authoritative technical source without double counting.
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

- application of the already locked additive Scrap formula (`BaseRequirement × (1 + Scrap% / 100)`); MRP-07 does not calculate Scrap
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
11. Production Workspace UI separation and future full Production domain source-of-truth migration boundary.
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
- client supplies Jalali Year/Month; server generates immutable Gregorian Start/End
- newly persisted Period defaults to OPEN

FORECAST
Implement approved:
- Product + Period Forecast structure
- Product references use UUID internally and expose `Item.BusinessNumber` as the only read-only Official Item Code; do not add, accept or derive a second Product Code
- immutable Forecast Revisions
- Forecast Qty >= 0
- one Official Current Forecast per Product+Period
- historical revisions retained
- stable Forecast Revision ID
- one server-captured immutable `ProductBaseUnitId` on ForecastSeries, inherited by every revision
- base-unit drift blocks mutation with `BASE_UNIT_CHANGED`; no conversion or historical rewrite
- Revision 1 Reason optional; Revision 2+ Reason required

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
- ProductionDate after current Asia/Tehran business date is blocked with `FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED`
- initial entry needs no correction Reason; every correction requires Reason and durable Actual Revision history
- server-captured immutable Unit snapshot; no mixed units inside Product+Period and no UOM conversion
- compare against the ForecastSeries/current Product Base Unit and existing Product+Period Actual snapshots; mismatch returns `BASE_UNIT_CHANGED`

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
- expose `EligibleAllocationStart = evaluationDate + 1`, Period End, and Remaining Forecast to MRP-10
- As-Of receives zero Remaining Forecast; do not spill into the next period
- when zero eligible future days remain with positive quantity, return `CURRENT_PERIOD_REMAINDER_UNALLOCATABLE`
- perform no daily allocation or residual distribution in MRP-07

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

Evaluate readiness by window:
- Future Period = full Period
- Current Period = evaluationDate + 1 through Period End
- Past Period = not a planning gate

Do NOT perform BOM explosion.

REFERENCE DATA BOUNDARY
- reuse MRP-01 ownership for PRODUCT_CATEGORY, BUSINESS_UNIT, and Unit
- require active references for new assignment and preserve inactive historical resolution
- display reference names as `fa-IR => NameFa` and `en => non-empty NameEn, otherwise NameFa`
- never translate stored reference data or create shadow reference masters

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

Workspace separation:
- Production Forecast is under the MRP Workspace.
- Daily Actual entry/review/correction is under the top-level Production Workspace.
- Forecast may display Actual MTD/Remaining as read-only planning context.
- no duplicate Actual source, API, or persistence model is created.

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
Preserve explicit permission/use-case boundaries; central Identity enforcement remains deferred.
Use nullable ActorIdentifier/context and no User/Employee FK.
Persist Forecast Revision, Actual Revision, and Period Lifecycle as durable domain history.
Reuse the existing generic audit boundary without treating its current best-effort/no-op implementation as the sole historical record.
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
- MRP-07 exposes next-day EligibleAllocationStart and Jalali period-end boundary without allocating daily quantities
- final Jalali calendar day with positive remainder returns `CURRENT_PERIOD_REMAINDER_UNALLOCATABLE` and no next-period spill
- Actual >= Forecast produces Remaining Forecast zero
- Jalali leap/month boundary uses canonical Gregorian conversion
- no same-day Actual + Remaining Forecast double count
- Actual > Forecast preserved
- Revision 1 Reason optional and Revision 2+ Reason required
- initial Actual without correction Reason and correction Reason required
- future Production Actual blocked by Asia/Tehran business date
- immutable ForecastSeries ProductBaseUnitId inherited by every revision
- immutable Actual Unit snapshot, mixed-unit prevention, and `BASE_UNIT_CHANGED`
- future/current/past BOM readiness windows
- Forecast without BOM allowed
- missing BOM warning
- active/inactive historical behavior
- Closed Period blocks normal edits
- Reopen requires Reason
- close/edit concurrency integrity
- no manual Monthly Actual
- no BOM explosion
- no Production Module scope leakage
- Reference Data integrity: PRODUCT_CATEGORY/BUSINESS_UNIT/Unit ownership, active new assignment, inactive historical resolution, `fa-IR => NameFa`, `en => NameEn ?? NameFa`, and no shadow reference masters

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
- Scrap requirement calculation (the additive formula is already locked elsewhere)
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
   - client supplies Jalali Year/Month; server generates immutable Start/End
   - new persisted Period defaults OPEN

3. Forecast Model
   - Product+Period grain
   - Product UUID is the relationship key and `Item.BusinessNumber` is the only displayed/searched Official Item Code
   - no dependency on deprecated `Item.Code`/`Item.NormalizedCode` and no second active Product Code
   - Qty >= 0
   - decimal/numeric
   - immutable Revision history
   - exactly one Official Current Forecast
   - no duplicate editable current value
   - ForecastSeries owns one immutable server-captured ProductBaseUnitId inherited by every Revision
   - no mixed units, conversion or history rewrite; drift returns BASE_UNIT_CHANGED
   - Revision 1 Reason optional; Revision 2+ Reason required

4. Forecast History
   - no overwrite
   - no zeroing past Forecast
   - stable revision identity
   - future MRP traceability preserved

5. Daily Actual
   - Product+Date aggregate
   - one logical record/day
   - no Shift/Line/Batch leakage
   - initial entry needs no correction Reason; each correction requires Reason and durable Actual Revision history
   - future ProductionDate blocked with FUTURE_PRODUCTION_ACTUAL_NOT_ALLOWED using Asia/Tehran business date
   - immutable Unit snapshot, no mixed Product+Period units, and no UOM conversion

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
   - EligibleAllocationStart = evaluationDate + 1
   - no daily allocation or residual distribution in MRP-07
   - positive remainder with zero eligible days returns CURRENT_PERIOD_REMAINDER_UNALLOCATABLE and never spills

8. Missing vs Zero Forecast
   - not conflated

9. BOM Boundary
   - Forecast may exist without BOM
   - BOM missing surfaced
   - no material explosion
   - no use of today's BOM for historical Actual boundary
   - Future readiness evaluates the full Period
   - Current readiness evaluates evaluationDate+1 through Period End
   - Past readiness is not a planning gate

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
    - Forecast and Daily Actual are separate Workspace destinations
    - Actual mutation is not exposed from the MRP Forecast destination
    - Close/Reopen clarity

15. Permissions / Auditability
    - permission/use-case boundaries remain explicit while enforcement awaits central Identity
    - no User/Employee FK; nullable ActorIdentifier/context is used
    - Forecast Revision, Actual Revision and Period Lifecycle are durable domain history, not generic-audit-only

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
    - Revision 1 Reason optional and Revision 2+ Reason required
    - immutable ForecastSeries ProductBaseUnitId inherited by revisions
    - Product+Date Actual uniqueness
    - initial Actual without correction Reason and every correction requiring Reason
    - future ProductionDate rejected against Asia/Tehran business date
    - immutable Actual Unit snapshots, mixed-unit rejection, and BASE_UNIT_CHANGED
    - Closed Period concurrency
    - Current Remaining Forecast
    - MRP-07 exposes As-Of + 1 as EligibleAllocationStart without creating daily allocations
    - no same-day Actual + Remaining Forecast double count
    - no cross-period spill at Jalali Period End
    - zero eligible future days produces an explicit unallocatable state
    - Actual >= Forecast produces Remaining Forecast zero
    - Jalali leap/month boundary uses canonical Gregorian conversion
    - Future/current/past BOM readiness windows

18. Reference Data Integrity Gate
    - PRODUCT_CATEGORY, BUSINESS_UNIT and Unit remain owned by MRP-01
    - active references are required for new assignment while inactive historical references remain resolvable
    - `fa-IR` displays NameFa; `en` displays non-empty NameEn with NameFa fallback
    - no shadow Category, Business Unit or Unit master exists

19. Specification Drift
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
25. Forecast Revision 1 Reason is optional; Revision 2+ Reason is required.
26. Initial Actual needs no correction Reason; each correction requires Reason and durable history.
27. Future Production Actual is blocked by the Asia/Tehran business date.
28. ForecastSeries owns the immutable Product Base Unit snapshot; revisions inherit it.
29. Daily Actual owns an immutable Unit snapshot; mixed units and conversion are forbidden.
30. Future/current/past BOM readiness uses the approved evaluation windows.
31. MRP-07 exposes the next-day boundary and unallocatable state but performs no daily allocation.
32. Client supplies Jalali Year/Month; the server owns immutable Gregorian boundaries and OPEN default.
33. Durable histories use nullable ActorIdentifier until central Identity exists.
34. MRP-01 Reference Data ownership and bilingual historical resolution remain intact.

## Open Question Resolution

Closed:

### Temporary Daily Production Actual Source

```text
Production Workspace → MRP-07 temporary Product+Date aggregate technical input
→ future full Production domain module may become source of truth
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
- Production Workspace UI separation creates no duplicate source, and the future full Production domain migration boundary prevents double counting.

## Deferred Downstream Scope

These are not open MRP-07 business decisions and do not block MRP-07:

- Application of the already locked additive Scrap formula; MRP-07 does not calculate Scrap.
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

Implementation is bounded to Forecast/Actual and explicitly blocks BOM Explosion, Inventory, MRP Calculation and full Production Module scope creep. The minimal Production Workspace is only the user-facing Daily Actual operational surface.

### MRP-07C
PASS

Independent read-only audit checks Jalali calendar integrity, revisions, actual aggregation, planning truth, period locking, performance and specification drift.

## Final Decision

`MRP-07 Forecast / Actual` is approved for the project Prompt Baseline.

MRP-07A is finalized and synchronized. MRP-07B is ready for separate Owner authorization; implementation has not started.
