# Hadron ERP — MRP Package 10 Final

**Package:** MRP-10 MRP Calculation Engine  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

## Owner Correction — Current BOM Run Snapshot (2026-08-30)

MRP-10 دیگر BOM را بر اساس Demand Date از Master زنده resolve نمی‌کند. در transaction کوتاه Source Snapshot، موتور فقط BOM جاری `APPROVED` هر Parent قابل‌دسترسی را capture می‌کند و همان ساختار تغییرناپذیر را برای تمام تاریخ‌های Run به کار می‌برد. تغییر یا جایگزینی BOM بعدی، Run تاریخی را تغییر نمی‌دهد. Machine code قدیمی `MISSING_EFFECTIVE_BOM` برای سازگاری contract باقی می‌ماند، اما معنای آن «نبود BOM جاری Approved در snapshot Run» است. هر wording پایین‌تر درباره EffectiveFrom/EffectiveTo یا انتخاب BOM بر اساس Demand Date توسط این تصمیم supersede می‌شود. فرمول‌ها، Golden Dataset و تاریخ‌های Forecast/MRP تغییر نمی‌کنند.

---

# A. Final Feature Specification — 10-MRP-CALCULATION-ENGINE

## 1. Purpose

این Feature هسته محاسباتی MRP است.

MRP-10 باید تمام Master Data و Transactional Planning Data ساخته‌شده در Packageهای قبل را به یک Calculation Engine:

- deterministic
- explainable
- reproducible
- auditable
- date-aware
- multi-level

تبدیل کند.

هدف موتور این است که برای هر Item و هر تاریخ در Planning Horizon مشخص کند:

- Gross Demand
- Firm Incoming
- Projected On Hand
- Net Requirement
- Expected Stockout
- Stock Coverage
- Firm Supply Coverage
- Planning Completeness
- Calculation Exceptions
- Demand / BOM / Supply Traceability

این Package نباید هنوز Supplier Selection، MOQ rounding، Order Quantity recommendation یا Purchase Order creation را انجام دهد.

---

## 2. Calculation Grain

### LOCKED DECISION

Core calculation grain:

```text
Daily
```

Weekly and Monthly views are presentation/reporting aggregations only.

The engine itself must calculate supply and demand at daily grain.

---

## 3. Planning Horizon

### LOCKED DECISION

Each MRP Run has:

- As-Of Date
- Horizon End Date

Default horizon:

```text
Jalali Period containing As-Of
+
next 11 Jalali Periods
```

The default Horizon End is the canonical Gregorian end date of the twelfth Period. An explicit override is allowed when `HorizonEnd >= AsOf`. There is no hard database horizon limit.

The user/system may run a shorter or longer horizon when needed.

---

## 4. MRP Calculation Run

### LOCKED DECISION

Each calculation is first produced as an immutable candidate Run. Operational publication uses two replaceable pointers:

```text
CURRENT   = latest atomically published operational plan
CERTIFIED = last audited prior-day operational plan
```

The Run rows and children remain immutable while referenced. Replacing a slot never mutates its prior Run; it atomically points the slot to the completed candidate and then removes the superseded full operational Run only when no business lineage references it.

Explicit decision-bearing executions are `PUBLISHED` snapshots and remain immutable:

```text
MRP-RUN-000127
MRP-RUN-000128
```

Daily audit history is kept as lightweight immutable certificates with deterministic content hashes, not an unbounded sequence of full calculation datasets. This storage lifecycle does not change calculation formulas or trace content inside a retained snapshot.

---

## 5. MRP Run Identity

Each Run should conceptually preserve:

- Internal Technical ID
- Run Number
- As-Of Date
- Horizon End Date
- Engine Version
- Started At
- Completed At
- Status
- initiated by actor where available
- simplifications / warnings
- input trace references

Run Number is server-generated, immutable and formatted as:

```text
MRP-RUN-{sequence:D6}
```

The technical ID remains an internal UUID.

---

## 6. MRP Run Status

Suggested stable machine states:

```text
RUNNING
COMPLETED
COMPLETED_WITH_EXCEPTIONS
FAILED
```

These are system-controlled states, not configurable reference values.

The engine must not claim a successful complete run when material input errors prevented valid calculation.

---

## 7. Engine Version

### LOCKED DECISION

Every Run must record an Engine Version.

Purpose:

When calculation logic changes in the future, the system must still be able to explain which logic produced an older result.

Example:

```text
Engine Version = MRP-1.0
```

Exact versioning format is an implementation choice.

---

## 8. Input Traceability

### LOCKED DECISION

An MRP Run must preserve enough traceability to explain its historical result.

At minimum, the Run must be able to identify the material inputs used, including:

- Forecast Revisions
- BOM Revisions
- Starting Actual Inventory
- Firm Supply Schedule Qty + ETA
- HYBRID route decisions
- planning policy inputs relevant to the result
- known simplifications

For every NEW Run, MRP-10 must also persist the actual resolved planning route at:

```text
Run + Item + DemandDate
```

Canonical `ResolvedPlanningRoute` values are:

```text
EXTERNAL
INTERNAL_BOM
INCOMPLETE
```

The snapshot/result also preserves a traceable route origin/reason:

```text
BUY
MAKE
OUTSOURCE_EXTERNAL
OUTSOURCE_BOM
HYBRID_EXTERNAL
HYBRID_INTERNAL
```

This immutable route truth is consumed by MRP-12. Historical route eligibility must not be reconstructed from current SupplyMethod, BOM, Sourcing or Catalog. Existing Runs are not backfilled from live Master Data. This contract extension does not change MRP-10 formulas, NetRequirement, Golden Dataset outputs or existing historical Run results.

For every NEW Run, the immutable Item input snapshot owned by MRP-10 must also preserve the Material Planning context required by MRP-11:

- `CategoryId`
- `CategoryCode`
- `CategoryNameFa`
- `CategoryNameEn`
- `CriticalDays`
- `SafeStartDays`
- `ExcessStartDays`

These are Run inputs, not live Catalog joins. Existing historical Runs are not backfilled from current Master Data.

The Run result must not silently change when Master Data changes later.

---

## 9. Forecast Input Snapshot / Reference

MRP-07 provides stable Forecast Revision identity.

### LOCKED DECISION

The Run must record exactly which Forecast Revision was used for each relevant:

```text
Product + Period
```

Historical Run explanation must not depend on whichever Forecast revision is current today.

---

## 10. BOM Input Trace

### LOCKED DECISION

For each demand date where a BOM is required, the engine uses the BOM Revision effective on that demand date.

The Run must preserve the exact BOM Revision used in the calculation trace.

---

## 11. Starting Inventory Snapshot

### LOCKED DECISION

At the Run As-Of Date, starting Actual On Hand is taken from MRP-08 Inventory.

The Run must preserve the actual starting quantity used.

Changing later inventory transactions must not rewrite the historical Run result.

The Run persists an immutable inventory input snapshot containing the actual starting quantity and source trace used by calculation.

---

## 12. Firm Supply Snapshot

### LOCKED DECISION

The Run must preserve the Firm Supply facts used:

- PO / Schedule identity
- Item
- Supplier identity and historical display context:
  - `SupplierId`
  - `SupplierNumber`
  - `SupplierNameFa`
  - `SupplierNameEn`
- Remaining Qty
- ETA
- relevant status

Changing a PO ETA after the Run must not alter the stored historical result of that Run.

Supplier context is preserved only for historical Supply Trace. It does not select, recommend or rank Suppliers.

For NEW Runs the canonical persistence contracts therefore include:

```text
MrpRunItemInput
    CategoryId
    CategoryCode
    CategoryNameFa
    CategoryNameEn
    CriticalDays
    SafeStartDays
    ExcessStartDays

MrpRunFirmSupplyInput
    SupplierId
    SupplierNumber
    SupplierNameFa
    SupplierNameEn

MrpRunResolvedRouteResult
    RunId
    ItemId
    DemandDate
    ResolvedPlanningRoute
    RouteOrigin
    RouteReasonCode
```

MRP-10 calculation formulas and Golden Dataset outputs are unchanged by these presentation/trace snapshot additions.

---

## 13. Planning Demand Source by Period

MRP-07 rules remain authoritative.

### Past Period

```text
Planning Truth = Actual
```

### Current Period

```text
Remaining Forecast
=
MAX(Current Forecast - Actual MTD, 0)
```

### Future Period

```text
Planning Truth = Forecast
```

MRP-10 consumes these rules.

---

## 14. No Double Counting Past Current-Month Actual

### LOCKED DECISION

Within the current month, historical days already reflected in Actual Inventory must not create a second future demand deduction.

For future planning from the Run As-Of Date:

- starting inventory is Actual On Hand as of As-Of Date
- Remaining Forecast represents future planned production
- past Actual Production is traceable but not re-consumed from today's stock

This avoids double counting.

---

## 15. Monthly to Daily Demand Distribution

### LOCKED DECISION

Until a Production Calendar exists:

### Future Month

Monthly Forecast is distributed evenly across calendar days of that Jalali Business Month.

### Current Month

Remaining Forecast is distributed across eligible calendar days where `Demand Date > Run As-Of Date` and `Demand Date <= Jalali Planning Period End`. The As-Of date receives zero Remaining Forecast allocation; allocation starts at `As-Of + 1 calendar day` and never spills into the next Planning Period.

The engine must use canonical Gregorian dates internally while respecting Jalali Business Period boundaries.

---

## 16. Current Day Treatment

### LOCKED OWNER DECISION

Actual MTD through the As-Of planning cut-off represents realized production through that date. Remaining Forecast represents only unrealized production after that cut-off. Therefore:

```text
Demand Date > Run As-Of Date
```

The As-Of date receives zero additional Remaining Forecast allocation and the first eligible date is `As-Of + 1 calendar day`. This prevents same-day Actual Production plus Remaining Forecast double counting.

Eligibility ends at the current Jalali Planning Period End. If Remaining Forecast is positive but zero eligible calendar days remain, the engine must surface `CURRENT_PERIOD_REMAINDER_UNALLOCATABLE`. It must not allocate to As-Of, spill into the next period, discard the quantity, or invent a cross-period allocation.

---

## 17. Daily Distribution Residual

### LOCKED DECISION

Daily distribution must preserve exact monthly total.

Do not independently round every daily share in a way that changes total demand.

Allocation is deterministic at 12 decimal places:

```text
Monthly Qty = 10,000
Daily allocation scale = 12 decimal places
LAST eligible day receives the exact residual
```

Required invariant:

```text
SUM(Daily Distributed Qty)
=
Monthly Forecast / Remaining Forecast exactly
```

The sum must equal the exact Period quantity at the approved 12-decimal persistence scale. The As-Of date receives zero and no residual may spill into another Period.

---

## 18. Numeric Precision

Use decimal/numeric for:

- Daily Demand
- Gross Requirement
- Net Requirement
- Inventory balances
- Firm Incoming
- Safety Stock
- Scrap calculations
- BOM Usage

Do not use binary floating point.

Do not prematurely round intermediate calculations.

Persisted MRP calculation quantities use PostgreSQL `numeric(28,12)`. Internal calculation uses .NET `decimal` without premature business rounding.

---

## 19. Rounding

### LOCKED DECISION

Intermediate MRP calculations retain high decimal precision.

Rounding is primarily a display concern until Order Planning.

MRP-10 must NOT round Net Requirement to:

- whole units
- MOQ
- carton
- purchase pack

Those decisions belong to Package 12.

---

## 20. BOM by Demand Date

### LOCKED DECISION

Every daily demand must use the BOM Revision effective on that same business date.

Example:

```text
REV-1 valid through 1405/05/24
REV-2 valid from    1405/05/25
```

Then:

```text
Demand 1405/05/23 → REV-1
Demand 1405/05/26 → REV-2
```

The engine must not use today's BOM for the entire horizon.

---

## 21. BOM Base Output Qty

MRP-06 defines:

```text
Normalized Child Usage
=
BOM Line Usage Qty / Base Output Qty
```

MRP-10 uses this normalized ratio before Scrap allowance.

Example:

```text
Base Output Qty = 12
Master Carton Usage = 1

Usage per parent unit
=
1 / 12
```

---

## 22. Scrap Resolution

### LOCKED DECISION

Effective Scrap:

```text
if BOM Line Scrap Override is NOT NULL
    use BOM Line Scrap Override
else
    use Item Default Planning Scrap %
```

Semantics:

```text
NULL = use Item default
0    = explicit zero
```

---

## 23. Scrap Formula

### LOCKED DECISION

The previously open Scrap Formula is now CLOSED.

`Planning Scrap %` means additional planning allowance.

Formula:

```text
Planned Requirement
=
Net Base Requirement × (1 + Scrap % / 100)
```

Example:

```text
Base Requirement = 1,000
Scrap = 5%

Planned Requirement = 1,050
```

Yield-based formula:

```text
Usage / (1 - Scrap%)
```

is NOT used in MRP v1.

---

## 24. Scrap Application Boundary

Scrap applies to lower-level material requirement according to BOM Line / Item Planning Scrap rules.

MRP-10 applies the resolved Scrap exactly once on each BOM edge and must not apply it repeatedly to the same edge requirement.

The engine should expose traceability:

- base usage
- effective scrap %
- post-scrap required qty

---

## 25. Gross Demand

Gross Demand means demand for an Item before considering that Item's:

- Actual On Hand
- Firm Incoming

Gross Demand may originate from:

- Product Forecast-derived requirements
- Parent Item Net Requirements exploded through BOM

Safety Stock is not Gross Demand.

---

## 26. Multi-Level Netting

### LOCKED DECISION

The engine must perform Netting at each Item level before exploding a MAKE-capable parent requirement downward.

Example:

```text
Gross PCBA Requirement = 100,000
PCBA available supply  = 40,000
Net PCBA Requirement   = 60,000
```

Only:

```text
60,000
```

is exploded into lower-level BOM requirements.

Do NOT explode gross 100,000 when 40,000 PCBA already exists/arrives as usable supply.

---

## 27. Sequential Time-Phased Netting

Netting must be time-phased.

Supply arriving on a later date cannot satisfy earlier demand.

Conceptually each day:

```text
Previous Projected Balance
+ Firm Incoming today
- Gross Demand today
=
Projected Balance before planning recommendation
```

The engine must preserve date order.

---

## 28. Starting Balance

For each Item at Run As-Of:

```text
Starting Balance
=
Actual On Hand snapshot
```

Baseline v1 uses company-wide total inventory across Warehouses according to the Warehouse Pool rule below.

---

## 29. Warehouse Pool Rule

### LOCKED DECISION

For MRP v1:

```text
MRP Available Actual Inventory
=
Total company Actual On Hand across Warehouses
```

The engine does not allocate demand by Warehouse yet.

This is an explicit simplification.

---

## 30. Inactive Warehouse Stock

### LOCKED DECISION

If an inactive Warehouse still has actual nonzero stock:

- quantity remains physically real
- v1 total company inventory may count it
- engine/result must surface a warning

Do not silently discard real physical stock solely because Warehouse status changed.

Warehouse-allocation policy remains deferred.

---

## 31. Safety Stock

### LOCKED DECISION

Safety Stock is a Planning Floor.

It is not:

- Gross Demand
- Firm Supply
- Actual Inventory

The engine should recognize Net Requirement needed both to:

- prevent negative/shortage
- restore/protect Safety Stock target

For Part and Component time-phased planning, use two distinct balances:

```text
FirmProjected
= previous FirmProjected + eligible Firm Incoming - Gross Demand

PlanningBeforeNeed
= previous PlanningBalance + eligible Firm Incoming - Gross Demand

NetRequirement
= MAX(SafetyStock - PlanningBeforeNeed, 0)

PlanningBalance
= PlanningBeforeNeed + NetRequirement
```

`NetRequirement` is hypothetical planning need and never Firm Supply. Stockout and Coverage use factual supply views, not `PlanningBalance`. For MAKE and OUTSOURCE-with-BOM, only `NetRequirement` is exploded to children.

Root forecasted PRODUCT demand is excluded from this material netting rule: its planned production quantity explodes directly through the effective Product BOM. Product Finished Goods inventory is not netted against Forecast and Product Safety Stock is not applied as a production adjustment in MRP-10 v1.

---

## 32. Projected On Hand

### LOCKED DECISION

Firm Projected On Hand is calculated from actual/firm facts only:

```text
Projected On Hand
=
Previous Projected
+ Firm Incoming
- Gross Demand
```

It may become negative.

Negative value is useful because it exposes shortage.

---

## 33. Recommended / Planned Supply Excluded from Firm Projection

### LOCKED DECISION

Future recommendations created by Package 12 must not be treated as Firm Incoming until they become an eligible real PO status according to MRP-09 rules.

Therefore MRP-10 Projected On Hand uses only:

- Actual starting inventory
- Firm Supply

not hypothetical recommendation quantities.

---

## 34. Shortage

A negative Projected On Hand means shortage risk.

The engine should derive at minimum:

- first stockout date
- shortage quantity
- safety stock breach date where applicable

The Item summary fields below, the locked Coverage states and separate safety-breach result form the v1 baseline.

---

## 35. Expected Stockout Date

Expected Stockout Date:

```text
first date within horizon where Firm Projected On Hand < 0
```

Safety-stock breach is a separate concept.

Do not conflate:

```text
Below Safety Stock
```

with:

```text
Physically negative Projected On Hand
```

---

## 36. Firm Incoming

Firm Incoming is consumed from MRP-09 Purchase Supply.

Eligible status:

```text
ORDERED
SHIPPED
CUSTOMS
PARTIALLY_RECEIVED
```

Quantity:

```text
Remaining Open Schedule Qty
```

Date:

```text
Current/Revised planned Warehouse Receipt date
else Original planned Warehouse Receipt date
```

according to MRP-09 rules.

---

## 37. Missing ETA Firm Supply

### LOCKED DECISION

Firm Qty with no usable ETA is not placed on an invented date.

It is reported separately:

```text
Firm Qty — ETA Missing
```

It does not participate in date-phased Projected On Hand until a valid ETA exists.

---

## 38. Overdue Firm Supply

### LOCKED DECISION

If:

```text
ETA < Run As-Of Date
and Remaining Qty > 0
and Actual Receipt has not occurred
```

the supply is:

```text
OVERDUE_FIRM_SUPPLY
```

It must NOT be assumed available today.

It does not participate in future Projected availability until ETA is revised to a valid future/current planning date or receipt is actually posted.

---

## 39. Why Overdue Supply Is Excluded

The system must not make this unsafe assumption:

```text
Past ETA + not received
=
available now
```

Instead it must surface the data-quality/operational exception.

This protects MRP from overstating supply.

---

## 40. Stock Coverage

### LOCKED DECISION

`Stock Coverage` measures how far current Actual Stock alone covers sequential future demand.

Future Firm Incoming is ignored for this KPI.

Coverage uses actual time-phased demand, not:

```text
Inventory / Average Daily Demand
```

---

## 41. Firm Supply Coverage

### LOCKED DECISION

`Firm Supply Coverage` measures how far:

```text
Starting Actual Stock
+
dated Firm Incoming
```

covers sequential future demand.

Firm supply becomes available only on its dated ETA.

---

## 42. Coverage State Model

Coverage should not invent fake large numbers.

Support exactly these stable states:

```text
DATE
BEYOND_HORIZON
NO_FUTURE_DEMAND
INCOMPLETE_DATA
```

`DATE` means the last fully covered demand date. Stock Coverage uses Starting Actual On Hand only. Firm Supply Coverage uses Starting Actual On Hand plus eligible dated Firm Supply. Safety Stock breach remains a separate result. Do not generate misleading Finished-Goods coverage for a root forecasted Product.

---

## 43. No Future Demand

If no future demand exists inside the relevant horizon:

```text
Coverage State = NO_FUTURE_DEMAND
```

Do not return:

- infinity as a normal number
- 9999
- arbitrary 3650 days

---

## 44. Beyond Horizon

If supply covers all known demand through Horizon End:

```text
Coverage State = BEYOND_HORIZON
```

The system should distinguish this from true unlimited coverage.

---

## 45. Incomplete Demand

If demand cannot be fully calculated because of material missing data:

```text
Coverage State = INCOMPLETE_DATA
```

Do not present an apparently precise coverage number based on incomplete demand.

---

## 46. BUY Route

### LOCKED DECISION

For:

```text
Supply Method = BUY
```

flow:

```text
Gross Demand
↓
Actual/Firm Netting
↓
Net External Requirement
```

BOM explosion stops at this Item.

MRP-10 does not choose Supplier or Order Qty.

---

## 47. MAKE Route

### LOCKED DECISION

For:

```text
Supply Method = MAKE
```

flow:

```text
Gross Demand
↓
Actual/Firm Netting
↓
Net Make Requirement
↓
Effective BOM Explosion
```

If required valid BOM is missing:

```text
Planning Exception
```

Do not silently treat MAKE as BUY.

---

## 48. OUTSOURCE Route

### LOCKED DECISION

For:

```text
Supply Method = OUTSOURCE
```

baseline:

### If valid Planning BOM exists

```text
Gross Demand
↓
Netting
↓
Net Requirement
↓
BOM Explosion
```

This supports Hadron-provided/planned input materials for outsourced assembly.

### If no planning BOM exists

Treat Item as requiring external supply at its own level.

The Run trace records whether OUTSOURCE used its BOM route or remained an external requirement because no planning BOM existed.

---

## 49. HYBRID Route

### LOCKED DECISION

If:

```text
Supply Method = HYBRID
```

and both:

- valid BOM route
- valid External Sourcing route

are Planning Ready:

```text
MRP MUST NOT GUESS
```

A Route Decision is required for the Run.

---

## 50. HYBRID Auto Resolution

If only one valid route is Planning Ready:

- only BOM route ready → use MAKE route
- only external route ready → use external route

The engine may automatically select the only usable route.

The selected route must be recorded in Run trace.

---

## 51. HYBRID Missing Route Decision

If both routes are valid and no decision is supplied:

```text
Exception = HYBRID_ROUTE_DECISION_REQUIRED
```

Affected demand/result is Incomplete.

Do not arbitrarily prefer:

- Make
- Buy
- cheaper
- faster

Those policies belong to later Planning/Order Planning decisions.

---

## 52. Hybrid Route Decision Grain

### LOCKED OWNER DECISION

```text
Item + Run
```

A selected HYBRID route must remain valid for every affected Demand Date in that Run. Date-specific Make/Buy split is forbidden in v1. If no single route covers all affected dates, create an explicit exception and mark the affected result `INCOMPLETE`.

---

## 53. Internal Process Lead Time

### LOCKED DECISION

Until Production/Routing capability exists:

```text
Internal Process Lead Time = 0 Calendar Days
```

Therefore lower-level component demand date equals parent Net Make Requirement date in v1.

---

## 54. Process Lead-Time Simplification

This zero-day assumption must be explicitly recorded as a known simplification in the Run/Engine documentation.

The system must not present component timing as fully realistic when internal production lead time is unknown.

Future Production/Routing may replace this assumption.

---

## 55. External Lead Time in MRP-10

External configured Supplier Lead Time is NOT used to move Firm PO ETA.

Existing PO supply uses PO Schedule ETA.

Configured Sourcing lead time will be used later by Order Planning to determine when new procurement must be initiated.

MRP-10 focuses on requirement and firm-supply projection.

---

## 56. Planning Exceptions

### LOCKED DECISION

Missing planning data must not silently become zero.

Examples:

- Missing Forecast
- Missing effective BOM
- invalid BOM
- missing HYBRID route decision
- missing required Sourcing readiness where external route trace is required
- Firm Supply ETA missing
- overdue Firm Supply
- unresolved reference/integrity issue

The Run must create explicit Exception records/states.

---

## 57. Exception Severity

MRP-10A may propose baseline exception levels such as:

```text
ERROR
WARNING
INFO
```

but Business meaning must remain clear.

An ERROR that prevents valid demand calculation must mark affected result:

```text
Incomplete
```

---

## 58. Planning Completeness

Each Item summary should expose a derived planning completeness state.

Suggested:

```text
COMPLETE
COMPLETE_WITH_WARNINGS
INCOMPLETE
```

This is derived from calculation exceptions.

It is not a manual status.

---

## 59. No Silent Zero Rule

### LOCKED PRINCIPLE

The engine must never convert unknown critical input into zero just to finish calculation.

Examples:

```text
Missing BOM ≠ zero material demand
Missing Forecast ≠ zero forecast
Missing ETA ≠ today
Missing Scrap config ≠ silently guessed if required
```

Where a valid default is explicitly defined by prior Package rules, that default may be used.

---

## 60. Item Daily Result

Conceptually, the engine may persist/read-model daily result fields including:

- Run
- Date
- Item
- Gross Demand
- Firm Incoming
- Projected On Hand
- Net Requirement / Net Need
- Safety Stock target
- Stockout state
- trace identifiers
- exception/completeness state

Official Runs persist immutable daily Item results at `numeric(28,12)` together with trace and completeness references.

---

## 61. Item Summary Result

For each Item, summary should support at minimum:

- Starting Actual On Hand
- Firm Incoming within Horizon
- Firm Qty ETA Missing
- Overdue Firm Qty
- Expected Stockout Date
- Safety Stock Breach Date
- Stock Coverage
- Firm Supply Coverage
- Net Requirement within Horizon
- Planning Completeness
- primary Exception count/state

This becomes the input to Package 11 Material Planning UI.

---

## 62. Net Requirement Meaning

MRP-10 Net Requirement is a planning quantity need.

It does NOT mean:

```text
Buy this exact quantity
```

It is before Package 12 decisions such as:

- MOQ
- Supplier selection
- Sourcing alternative
- purchase timing
- expert override
- defer/reject

---

## 63. External Net Requirement

For BUY/external route Item:

MRP-10 should expose the unmet external requirement and timing.

It does not yet produce a Purchase Recommendation document.

---

## 64. MAKE Net Requirement

For MAKE route Item:

Net Make Requirement is what gets exploded to children.

The Item's own shortage may still appear in trace/result as a parent requirement.

---

## 65. OUTSOURCE Net Requirement

OUTSOURCE BOM route may create child demand.

External service procurement itself is out of scope.

MRP-10 does not create service PO requirements.

---

## 66. Demand Traceability

The engine should preserve traceability sufficient to explain where demand came from.

At minimum conceptually:

```text
Product Forecast
↓
BOM Revision
↓
Parent Requirement
↓
Child Requirement
```

Persist direct, material calculation contributions and source references sufficient for deterministic explanation; avoid recursive path duplication that creates trace explosion.

Do not build an unbounded verbose trace that makes the engine unusable, but preserve explainability.

---

## 67. Trace to Product Demand

For shared Parts/Components used by multiple Products, demand must aggregate across all parent paths.

The system should still be able to explain major source contributions.

Inventory remains shared across those demands.

Do not randomly allocate physical inventory to Products.

---

## 68. Shared Material Aggregation

### LOCKED DECISION

If the same Part/Component is used by multiple Products:

```text
Total Daily Gross Demand
=
SUM(all valid demand paths)
```

Netting then occurs against shared Item inventory/firm supply.

Do not separately net stock for each Product and double-count inventory.

---

## 69. Recursive Processing

The engine must support recursive BOM traversal without fixed level count.

It must rely on MRP-06 cycle prevention but should still defensively fail if an invalid cycle somehow exists.

Do not enter infinite recursion.

---

## 70. Determinism

### LOCKED DECISION

Given:

- same Engine Version
- same Run inputs
- same route decisions
- same calculation settings

the engine must produce the same result.

No AI or probabilistic logic participates in deterministic MRP calculation.

---

## 71. AI Boundary

Future AI may:

- explain results
- summarize exceptions
- suggest investigation

It must not silently change deterministic MRP formulas or source data.

MRP-10 contains no AI dependency.

---

## 72. Run Reproducibility

Historical Run should remain explainable even after:

- Forecast changes
- BOM revisions
- Inventory changes
- PO ETA changes
- Sourcing configuration changes

This requires storing official calculated results plus the material immutable input snapshots/references used by the Run.

---

## 73. Run Persistence Strategy

### LOCKED OWNER DECISION

Official Runs persist:

- full daily results
- summary results
- trace tables
- input snapshot records

> historical official Run result must not drift.

Therefore purely recomputing later from mutable current data is insufficient.

Do not overbuild, but preserve historical results.

---

## 74. Draft / Preview Run

MRP-10A may propose:

- Preview calculation
- Official persisted Run

if useful.

However, it must not create an unnecessary workflow.

If Preview exists, only Official persisted Run needs historical audit guarantees unless explicitly approved otherwise.

---

## 75. Run Failure

If a technical failure occurs:

- Run status = FAILED
- incomplete partial result must not be presented as a valid complete Run

The engine should preserve useful failure diagnostics without exposing internal secrets.

---

## 76. Exception-Tolerant Completion

A Run may complete with exceptions where unaffected Items remain calculable.

Example:

```text
COMPLETED_WITH_EXCEPTIONS
```

Affected Items must be marked Incomplete.

Do not fail the entire company plan merely because one unrelated Product is missing a BOM, unless calculation dependency requires it.

---

## 77. Business Date

Run As-Of Date is a business date.

Canonical storage is Gregorian.

Jalali display/input uses shared calendar infrastructure.

Business timezone:

```text
Asia/Tehran
```

---

## 78. Horizon End

Horizon End must be:

```text
>= As-Of Date
```

The engine should not accidentally pull demand beyond requested horizon except where limited look-ahead is explicitly needed for a defined KPI.

MRP-10 v1 has no implicit look-ahead beyond the explicit Horizon End.

---

## 79. Coverage Horizon Semantics

Coverage is calculated against the Run's known future demand horizon.

If stock survives Horizon End:

```text
BEYOND_HORIZON
```

not an invented exact exhaustion date beyond known demand.

---

## 80. Safety Stock Coverage Distinction

Stock Coverage and Firm Supply Coverage measure demand coverage.

Safety Stock Breach may be shown separately.

Coverage is factual demand coverage and is not redefined as "days until safety stock". Safety Stock breach is labeled separately.

---

## 81. Overdue Demand / Past-Dated Requirement

No future planning demand is generated before or on Run As-Of Date from monthly distribution.

Historical actual/past demand is trace only.

The forward projection for current-period Remaining Forecast begins strictly after As-Of, at `As-Of + 1 calendar day`.

---

## 82. Sourcing Readiness Boundary

MRP-10 does not select Supplier.

However, for an external requirement, the engine may derive an exception such as:

```text
NO_PLANNING_READY_EXTERNAL_SOURCE
```

if Package 05 indicates no usable external source exists.

This does not change Net Requirement quantity.

It informs downstream planning/actionability completeness. `NO_PLANNING_READY_EXTERNAL_SOURCE` must never alter the computed Net Requirement quantity.

---

## 83. Source Alternatives

MRP-10 does not compare:

- Price
- MOQ
- faster Supplier
- cheaper route

That belongs to Package 12.

It only identifies source readiness/incompleteness where relevant.

---

## 84. Daily Firm Supply from Split Schedules

Each PO Delivery Schedule contributes Firm Incoming only on its own expected receipt date.

Example:

```text
1405/08/15 +40,000
1405/09/05 +60,000
```

Do not merge both into first or last ETA.

---

## 85. Partially Received Schedule

Actual Received Qty is already reflected in Inventory when Package 13 posts it.

Only remaining open schedule quantity can be Firm Incoming.

Do not double count:

```text
Inventory Receipt
+
original full PO schedule qty
```

---

## 86. Cancelled Supply

Cancelled Qty does not contribute to Firm Incoming.

Cancelled PO/Schedule is historical trace only.

---

## 87. Forecast and Active Products

Future demand generation normally uses active Products.

If an inactive Product still has future Forecast, the Run should surface a warning/exception rather than silently ignoring a material forecast record.

This behavior is aligned with the canonical MRP-07 current-period contract.

---

## 88. BOM Effective Gap

If Product/MAKE demand date has no Approved effective BOM:

```text
Exception = MISSING_EFFECTIVE_BOM
```

Do not use previous/next BOM automatically unless a specific effective-date rule explicitly provides that behavior.

---

## 89. BOM Overlap Integrity Failure

MRP-06 should prevent overlapping Approved BOMs.

If engine encounters ambiguous multiple effective BOMs:

```text
ERROR
INCOMPLETE
```

Do not arbitrarily choose one.

---

## 90. Safety Stock Data

Use Part/Component Safety Stock from MRP-04.

Product Safety Stock is not applied as a production adjustment in MRP-10 v1. Part and Component Safety Stock remains the material planning floor.

---

## 91. Product Inventory Role

Inventory supports Products, but core material requirement flow begins from Product Forecast into Product BOM.

The current manufacturing planning scenario is locked:

```text
Forecast is Production Plan demand,
not Sales Demand.
```

Therefore Product finished-goods stock should NOT automatically reduce planned production Forecast unless the Master Scenario explicitly states Forecast is market/sales demand.

Product Finished Goods inventory is not netted against production Forecast in MRP-10 v1.

---

## 92. Forecast Meaning Guard

MRP-07 language describes Forecast as planned production quantity.

Therefore baseline MRP-10 treats Product Forecast as intended production requirement, not customer shipment demand.

Do not subtract Finished Goods stock from production plan unless explicitly approved later.

Part/Component inventory is netted normally.

---

## 93. Product BOM Explosion

Product planned production demand is exploded through the Product BOM using effective revision by demand date.

Product-level inventory does not automatically offset the production plan under the approved baseline meaning above.

---

## 94. Material Netting

Part and Component demand is netted against:

- total company Actual On Hand
- dated usable Firm Incoming

before route-specific lower-level explosion.

This is the core material MRP flow.

---

## 95. Package 11 Boundary

MRP-10 generates/read-models calculation output.

MRP-11 owns user-facing Material Planning / Mapping experience.

Do not overbuild dashboards, interactive material timelines or charts inside MRP-10 beyond minimal verification UI if required.

---

## 96. Package 12 Boundary

MRP-12 owns:

- System Recommended Order Qty
- MOQ application
- Supplier/Source recommendation
- Next Action
- Priority
- Expert Decision
- Expert Final Qty
- Reason

MRP-10 must not produce these as final procurement decisions.

---

## 97. Package 13 Boundary

MRP-13 owns:

- Recommendation→Draft PO
- final PO lifecycle operations
- Receipt
- Inventory integration

MRP-10 consumes only Firm Supply foundation from MRP-09.

---

## 98. Package 14 Boundary

MRP-14 owns:

- lead-time performance analytics
- final reporting/integration
- cash-flow contract
- regression/audit integration

MRP-10 records trace data needed for later analytics but does not calculate them.

---

## 99. API / Application Use Cases

Required conceptual use cases include:

- create/start MRP Run
- validate Run inputs
- supply HYBRID route decisions
- execute Run
- get Run status
- get Run exceptions
- get Item Summary results
- get Item Daily Timeline result
- get demand/BOM trace for selected Item
- compare/basic inspect historical Runs if useful
- retrieve Run input trace

Avoid unrestricted calculation endpoints that silently change master/transaction data.

---

## 100. Read-Only Calculation Principle

MRP Calculation must not mutate source-of-truth operational data such as:

- Forecast
- BOM
- Inventory
- Sourcing
- PO

Running MRP creates:

- Run
- Calculation Results
- Exception/Trace records

It does not "fix" source data.

---

## 101. Permissions

At minimum architecture should distinguish:

- View MRP Results
- Run MRP
- Manage HYBRID Route Decisions for Run where needed

Do not create parallel Identity infrastructure.

---

## 102. Auditability

Important Run events should be auditable:

- Run created
- parameters
- route decisions
- engine version
- completion/failure
- input snapshot/reference
- actor
- timestamp

Calculation outputs themselves are the primary historical record of the Run.

---

## 103. UI Scope in MRP-10

Minimal engine administration/result inspection may include:

- Run list
- Run detail
- status
- As-Of / Horizon
- exception count
- engine version
- execution time
- high-level completeness

Material Planning operational UI belongs to Package 11.

---

## 104. Performance

The engine must be designed for:

- daily grain
- multi-level BOM
- shared Items
- multiple Products
- 12-month typical horizon
- long-term storage
- repeated Runs

Avoid:

- N+1 queries
- one DB query per Item per day
- recursive round-trips to database
- client-side core MRP calculation
- speculative distributed architecture

---

## 105. Calculation Architecture

### LOCKED OWNER DECISION

MRP-10 v1 uses bounded synchronous execution. Do not hold a long `REPEATABLE READ` transaction during calculation.

Required pipeline:

1. create the immutable-identity `RUNNING` Run
2. open a short `REPEATABLE READ` transaction and bulk-read the source snapshot
3. persist immutable Run input snapshots and commit the snapshot transaction
4. calculate deterministically from those persisted snapshots outside the source-snapshot transaction
5. atomically persist all official results, exceptions, summaries and final status

A `FAILED` Run must not expose partial official results. The calculation must use only the persisted Run inputs and must not re-read mutable source truth during calculation.

Do not introduce microservices/Kafka/Redis solely for MRP calculation.

---

## 106. Database Load Strategy

The engine should load relevant datasets in efficient batches.

Do not repeatedly fetch:

- BOM lines
- Item policy
- Inventory
- PO schedules

inside deeply nested loops.

MRP-10B must use bounded bulk internal read contracts and must not introduce N+1 or per-day database queries.

---

## 107. Recursive Calculation Safety

Use deterministic cycle-safe traversal.

Although MRP-06 blocks cycles, engine should fail safely if corrupt data contains one.

Maximum technical recursion depth may be protected for safety, but Business BOM depth must not be artificially fixed.

---

## 108. Calculation Order

Shared Item aggregation means calculation cannot simply independently explode every Product to final leaves and then separately consume stock.

The algorithm must preserve:

```text
aggregate demand at Item/date
→ net Item supply
→ explode only Net Make/Outsource requirement
```

This requirement is LOCKED.

---

## 109. Time-Phased Netting and Parent Explosion

For MAKE/OUTSOURCE BOM route, lower-level child demand is created from the Parent Net Requirement on the relevant date after parent stock/firm supply has been considered.

With v1 internal process lead time = 0:

```text
Child Demand Date = Parent Net Requirement Date
```

---

## 110. Future Process Lead Time Compatibility

Engine architecture must permit later date offset:

```text
Child Requirement Date
=
Parent Requirement Date
-
Internal Process Lead Time
```

when Production/Routing is introduced.

Do not hard-code assumptions in a way that prevents future replacement.

---

## 111. Run Warnings / Known Simplifications

At minimum v1 Run metadata/documentation should make visible:

- Warehouse pooled inventory
- Internal Process Lead Time = 0 days
- Calendar-day monthly Forecast distribution
- no production capacity constraints
- no MOQ/order rounding in MRP-10
- no automatic HYBRID route choice when both routes ready

These are explicit model assumptions, not hidden deficiencies.

---

## 112. No Capacity Planning

MRP-10 assumes forecasted/planned production can occur according to distributed dates.

It does not verify:

- machine capacity
- labor capacity
- line capacity
- shift availability

Production Capacity planning is out of scope.

---

## 113. No Working-Day Calendar Yet

Until Production Calendar exists:

- all calendar days may receive distributed Forecast demand
- internal process lead time baseline uses calendar days

This is explicit and replaceable later.

---

## 114. Testing Philosophy

Calculation tests must use small deterministic fixtures with manually verifiable expected results.

Tests should cover both:

- unit/domain formulas
- integration scenarios across Forecast/BOM/Inventory/Firm Supply

Avoid tests that only assert "no exception thrown".

---

## 115. Required Core Test Scenarios

At minimum:

### Forecast Distribution
- monthly exact total
- current month remaining
- decimal residual
- Jalali month boundaries

### BOM
- effective revision by date
- base output qty
- multi-level chain
- shared child across products

### Scrap
- Item default
- BOM override
- explicit zero
- formula ×(1+%)

### Netting
- stock reduces requirement
- Firm supply arrives later
- parent stock reduces child explosion
- safety stock need

### Projected
- can go negative
- first stockout date

### Firm Supply
- split schedules
- partial receipt remaining only
- ETA missing
- overdue ETA excluded
- cancelled excluded

### Routes
- BUY
- MAKE
- OUTSOURCE with BOM
- OUTSOURCE without BOM
- HYBRID only one route ready
- HYBRID both ready without decision → exception

### Coverage
- Stock Coverage
- Firm Supply Coverage
- Beyond Horizon
- No Future Demand
- Incomplete Data

### Exceptions
- missing Forecast
- missing BOM
- invalid/ambiguous effective BOM
- missing source readiness where relevant

### Reproducibility
- same inputs + same engine version = same result
- later source-data changes do not mutate historical Run

---

## 116. Concurrency

Two MRP Runs may potentially execute near the same time.

They must remain independent.

One Run must not overwrite another's:

- route decisions
- snapshots
- results
- exceptions

MRP-10A approved bounded synchronous execution with independent immutable input and result snapshots per Run.

No distributed job infrastructure is required unless actual Repository needs it.

---

## 117. Background Execution

Asynchronous processing and Hangfire are not introduced in MRP-10 v1. Future measured need may add background execution without changing persisted Run or calculation semantics.

Execution v1 is bounded synchronous. Do not add Hangfire solely for MRP-10. The persisted Run boundary may support future background execution without changing calculation semantics.

---

## 118. Run Cancellation

Run cancellation is not implemented in v1. Do not add unnecessary orchestration complexity.

---

## 119. Storage Retention

Historical decision snapshots are valuable for:

- audit
- recommendation explanation
- regression
- planning comparison

Keep only the full `CURRENT` and `CERTIFIED` operational datasets plus immutable `PUBLISHED`/business-lineage snapshots. After an atomic slot replacement, delete the superseded unreferenced operational Run and retain its lightweight daily certificate. Never prune a Run referenced by Recommendation Generation, Expert Decision/PO lineage, What-If Scenario or another immutable business record.

---

## 120. Seed / Migration

Codex must not invent calculation business data.

Tests may create synthetic fixtures.

The canonical deterministic numerical baseline is `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md` (v1.0). MRP-10 Golden Regression must reproduce GD-01, GD-02, GD-03, the MRP portions of GD-08, and GD-09. Approved expected values are read-only and must be encoded independently from production-engine outputs; discrepancies require defect analysis or an approved versioned business-rule change, never silent expected-value replacement.

Production Runs must use actual configured project data.

---

## 121. Explicit Non-Scope

MRP-10 must NOT implement:

- Supplier recommendation
- Preferred Supplier selection policy
- MOQ rounding
- Purchase quantity recommendation
- Order Date recommendation
- Purchase Order creation
- Recommendation→PO
- Expert Decision workflow
- warehouse demand allocation
- production capacity
- working-day calendar
- production routing
- internal process lead time beyond explicit 0-day baseline
- projected-supply from unapproved recommendations
- inventory posting
- PO receipt
- Cash Flow
- Lead-Time Analytics
- AI-driven calculation
- auto-fixing missing data

---

## 122. Acceptance Criteria

MRP-10 is acceptable when:

1. Core calculation grain is daily.
2. Run has As-Of Date + Horizon End.
3. Default horizon is the Jalali Period containing As-Of plus the next 11 Periods; Horizon End is the Gregorian end of the twelfth Period, explicit override requires `HorizonEnd >= AsOf`, and there is no hard DB cap.
4. Every official Run is immutable and independent.
5. Engine Version is preserved.
6. Forecast Revision inputs are traceable.
7. BOM Revisions used by date are traceable.
8. Starting Inventory used is traceable.
9. Firm Schedule Qty/ETA used is traceable.
10. HYBRID route decisions are traceable.
11. Future Monthly Forecast distributes to daily demand.
12. Current month uses Remaining Forecast only.
13. Current-period Remaining Forecast uses `Demand Date > As-Of` through the same Jalali Period End; As-Of allocation is zero.
14. Zero eligible future days with positive remainder produces an explicit unallocatable-current-period state and no cross-period spill.
15. Daily distribution uses deterministic 12-decimal allocation, assigns the exact residual to the last eligible day, and exactly reconciles to Period quantity.
16. Persisted calculation quantities use `numeric(28,12)` and internal decimal calculation has no premature business rounding.
17. BOM is selected by demand date.
18. Base Output Qty is applied correctly.
19. Scrap resolution respects Override / Default / explicit zero.
20. Scrap formula = ×(1 + Scrap%).
21. Shared Material demand aggregates across all parents.
22. Item stock is shared and not double-counted by Product.
23. Multi-level netting occurs before lower-level explosion.
24. Part/Component Safety Stock uses the locked two-balance Planning Floor algorithm and is not Gross Demand.
25. Firm Projected On Hand uses actual stock + Firm Incoming - Gross Demand; PlanningBalance and hypothetical NetRequirement remain separate.
26. Projected On Hand may become negative.
27. Recommendations are not counted as Firm Incoming.
28. First Expected Stockout Date is derived.
29. Stock Coverage uses sequential demand.
30. Firm Supply Coverage uses dated Firm Incoming.
31. Coverage uses exactly DATE / BEYOND_HORIZON / NO_FUTURE_DEMAND / INCOMPLETE_DATA; DATE is the last fully covered demand date.
32. Firm Qty with missing ETA is excluded from timed projection and surfaced.
33. Overdue unreceived Firm Supply is excluded until ETA correction.
34. BUY stops BOM explosion.
35. MAKE nets then explodes BOM.
36. OUTSOURCE behavior follows approved BOM/no-BOM rule.
37. HYBRID decision grain is Item + Run, must remain valid for all affected Demand Dates, and never silently splits by date.
38. Internal Process Lead Time baseline = 0 Calendar Days, child demand equals parent Net Requirement date, and the assumption is stored on the Run.
39. Missing critical data creates exceptions, not silent zero.
40. Affected results expose Planning Completeness.
41. Historical Run result does not drift with later source changes.
42. Engine is deterministic.
43. Calculation does not mutate Forecast/BOM/Inventory/PO source data.
44. No MOQ/Supplier/Order recommendation logic leaks into MRP-10.
45. Tests validate fixed Golden Dataset v1 expected outputs independently from the production calculator, not only execution success.
46. Root forecasted PRODUCT explodes planned production directly; Product Finished Goods inventory and Product Safety Stock do not adjust it in v1.
47. Missing/Overdue Firm Supply is excluded and surfaced but normally does not make demand unknown.
48. `NO_PLANNING_READY_EXTERNAL_SOURCE` does not alter Net Requirement quantity.
49. Run Number is server-generated immutable `MRP-RUN-{sequence:D6}`.
50. Execution follows the short-snapshot-transaction, out-of-transaction calculation, atomic-finalization pipeline and exposes no partial official result on failure.
51. MRP-01 Reference Data/Unit ownership, ReferenceType count 8, Item.BusinessNumber identity and FA/EN fallback rules remain intact with no shadow masters.
52. NEW Run Item snapshots preserve Category identity/display and Critical/Safe/Excess Day thresholds for historical MRP-11 risk presentation.
53. NEW Run Firm Supply snapshots preserve Supplier identity/number/names for historical Supply Trace only; this creates no Supplier ranking or recommendation.
54. Existing historical Runs are never backfilled from live Catalog/Supplier/PO data, and the presentation/trace snapshot additions do not change MRP formulas or Golden outputs.
55. NEW Runs persist immutable Run+Item+DemandDate `ResolvedPlanningRoute` (`EXTERNAL` / `INTERNAL_BOM` / `INCOMPLETE`) plus traceable route origin/reason; MRP-12 never needs live route inference and existing Runs are not backfilled.

---

## 123. Locked Decisions

The following are locked for MRP-10:

1. Calculation grain = Daily.
2. Run-specific As-Of Date + Horizon End.
3. Default Horizon = Jalali Period containing As-Of + next 11 Periods; default Horizon End = Gregorian end of the twelfth Period; explicit override requires `HorizonEnd >= AsOf`.
4. No hard database horizon limit.
5. Every candidate calculation is immutable; operational publication atomically replaces `CURRENT` or `CERTIFIED`, while only explicit business-decision snapshots are retained as additional full Runs.
6. Run records Engine Version.
7. Forecast Revision input trace is required.
8. BOM Revision input trace is required.
9. Starting Inventory snapshot/trace is required.
10. Firm Supply Qty/ETA trace is required.
11. HYBRID route decisions are part of Run trace.
12. Scrap formula = Requirement × (1 + Scrap%).
13. BOM Scrap Override wins when not NULL.
14. NULL Scrap Override uses Item default.
15. 0 Scrap Override means explicit zero.
16. No intermediate business rounding for purchase quantities.
17. Future monthly Forecast distributes evenly over calendar days.
18. Current month distributes Remaining Forecast only from As-Of + 1 through the same Jalali Period End.
19. As-Of allocation is zero; positive remainder with zero eligible days is surfaced explicitly and never spills periods.
20. Daily allocation uses deterministic 12-decimal quantities, assigns exact residual to the last eligible day and reconciles exactly to Period total.
21. BOM Revision selected by each Demand Date.
22. Shared material Gross Demand aggregates across parent paths.
23. Multi-Level Netting happens before lower-level BOM explosion.
24. Part/Component Safety Stock is Planning Floor using separate FirmProjected and PlanningBalance calculations.
25. Firm Projected On Hand = Actual + Firm Incoming - Gross Demand.
26. Firm projection may be negative.
27. Hypothetical recommendations are not Firm Supply.
28. MRP v1 uses total company inventory across Warehouses.
29. Inactive Warehouse stock may count if physically present but creates warning.
30. Stock Coverage and Firm Supply Coverage are separate.
31. Coverage uses sequential demand.
32. Coverage states are exactly DATE / BEYOND_HORIZON / NO_FUTURE_DEMAND / INCOMPLETE_DATA; DATE means last fully covered demand date.
33. Missing ETA Firm Supply is excluded from dated projection.
34. Overdue unreceived Firm Supply is excluded until revised/received.
35. BUY = external net requirement, no lower explosion.
36. MAKE = net then BOM explosion.
37. OUTSOURCE with valid BOM = net then explosion.
38. OUTSOURCE without BOM = external requirement.
39. HYBRID decision grain is Item + Run; both-ready requires an explicit Run route decision.
40. HYBRID with only one ready route may use it automatically only when that route remains valid for every affected Demand Date; no silent date split.
41. Internal Process Lead Time baseline = 0 Calendar Days; child demand date equals parent Net Requirement date and the assumption is stored on the Run.
42. Missing critical input creates Exception.
43. No silent zero/guessing.
44. Forecast means Production Plan in v1; Product finished-goods stock and Product Safety Stock do not adjust root Product production demand.
45. Package 10 does not create procurement recommendation or PO.
46. Persisted calculation quantities use `numeric(28,12)` and internal calculation uses decimal without premature business rounding.
47. Run Number is server-generated immutable `MRP-RUN-{sequence:D6}`.
48. Stock Coverage uses Starting Actual On Hand only; Firm Coverage adds only eligible dated Firm Supply; neither uses PlanningBalance.
49. Missing/Overdue Firm Supply is excluded from timed projection and surfaced without normally making demand unknown.
50. `NO_PLANNING_READY_EXTERNAL_SOURCE` does not change Net Requirement quantity.
51. Execution v1 is bounded synchronous with a short `REPEATABLE READ` snapshot transaction, calculation from immutable snapshots, and atomic official finalization.
52. Golden Dataset v1 FINAL fixed expected outputs are mandatory and must not be generated by the production calculator.
53. NEW Runs snapshot Item Category identity/names and Critical/Safe/Excess Day thresholds for MRP-11 historical presentation.
54. NEW Runs snapshot Firm Supply Supplier identity/number/names for historical Supply Trace only.
55. Existing Runs receive no live Master Data backfill; these snapshot extensions do not alter calculation formulas or Golden Dataset outputs.
56. NEW Runs persist the actual resolved planning route at Run+Item+DemandDate with canonical route state and origin/reason; this is immutable MRP-10 planning truth for MRP-12 and changes no formula or Golden output.

---

## 124. Open Question Resolution

The previous open question:

`Exact Scrap Formula`

is now CLOSED.

Approved:

```text
Planned Requirement
=
Base Requirement × (1 + Scrap % / 100)
```

The previous open question:

`Planning Horizon`

is now CLOSED.

Approved:

```text
Run-specific As-Of Date + Horizon End
Default = Jalali Period containing As-Of + next 11 Periods
Default Horizon End = Gregorian end date of the twelfth Period
Explicit override allowed when HorizonEnd >= AsOf
No hard database limit
```

The previous open question:

`Component-to-Part / Internal Process Lead Time`

is now CLOSED for v1 baseline.

Approved:

```text
0 Calendar Days
```

until Production/Routing exists.

This simplification must be visible in Run assumptions.

---

## 125. Remaining Relevant Deferred Scope

Still intentionally deferred:

- future Production Calendar / working days
- future nonzero internal process lead time
- Warehouse allocation policy
- Package 12 Decision Horizon
- Package 12 Priority classification
- final Over-Receipt in Package 13

These items do not reopen the locked As-Of exclusion decision.

---

# B. Prompt 10A — MRP Calculation Engine Analysis

```text
# MRP-10A — MRP Calculation Engine Analysis

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
- docs/modules/mrp/features/MRP-09-PURCHASE-SUPPLY-FOUNDATION-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-10-MRP-CALCULATION-ENGINE-PACKAGE-FINAL.md
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior Packages were implemented merely because they were planned.

TASK:
Analyze MRP-10 MRP Calculation Engine.

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

1. Understand the end-to-end deterministic MRP flow.
2. Inspect actual Repository and data models implemented by MRP-01..09.
3. Identify any contradiction between planned specs and actual implementation.
4. Propose MRP Calculation Run data model:
   - identity
   - As-Of
   - Horizon
   - Engine Version
   - status
   - input trace
   - results
   - exceptions
5. Propose the smallest persistence strategy that guarantees historical Run results do not drift.
6. Analyze Forecast Revision trace strategy.
7. Analyze BOM Revision-by-demand-date trace.
8. Analyze Starting Inventory snapshot/trace.
9. Analyze Firm Supply schedule snapshot/trace.
10. Analyze HYBRID route-decision snapshot.
11. Confirm Forecast semantic:
    Forecast is planned production quantity in current project baseline.
    Do NOT subtract Product finished-goods inventory from production plan unless project docs explicitly contradict this.
12. Daily Forecast distribution:
    - Jalali month boundaries
    - Gregorian canonical dates
    - current month Remaining Forecast
    - exact residual reconciliation
13. Verify the locked current-period boundary: As-Of excluded, allocation starts at As-Of + 1, `Demand Date > As-Of`, and eligibility ends at the same Jalali Period End.
    Analyze normal mid-period, exact division, decimal residual, Actual >= Forecast, Jalali leap/month boundary, and last-day-with-positive-remainder scenarios. Confirm no same-day Actual + Remaining Forecast double count, no cross-period spill, and an explicit unallocatable state when eligible-day count is zero.
14. BOM by Demand Date.
15. Base Output Qty normalization.
16. Scrap resolution:
    Override → Default
    NULL vs explicit 0.
17. Scrap formula:
    Requirement × (1 + Scrap% / 100).
18. Numeric precision and no premature rounding.
19. Shared-material aggregation across products.
20. Design the time-phased Multi-Level Netting algorithm.
21. Confirm stock/firm supply are netted at each Item before exploding its Net Make Requirement.
22. Define Safety Stock sequential behavior without repeated/double counting.
23. Define Firm Projected On Hand.
24. Define Expected Stockout and Safety Stock Breach.
25. Define Stock Coverage algorithm using sequential demand.
26. Define Firm Supply Coverage algorithm using dated supply.
27. Define explicit coverage states:
    - Beyond Horizon
    - No Future Demand
    - Incomplete Data
28. Firm Supply:
    - statuses
    - Remaining Qty
    - split schedules
    - partial receipt
    - cancellation
29. Missing ETA behavior.
30. Overdue Firm Supply behavior.
31. Warehouse Pool:
    - total company actual stock
    - inactive Warehouse stock warning
    - no warehouse allocation
32. Route behavior:
    - BUY
    - MAKE
    - OUTSOURCE with BOM
    - OUTSOURCE without BOM
    - HYBRID
33. Propose minimal HYBRID Route Decision grain.
    Baseline Item+Run unless evidence requires more.
34. Internal Process Lead Time = 0 calendar days and future replacement path.
35. Planning Exception model:
    - error/warning/info if useful
    - completeness state
    - no silent zero
36. Demand trace / explainability design.
37. Balance performance vs trace-detail storage.
38. Recursive calculation safety.
39. Engine pipeline and calculation order.
40. Efficient bulk data loading strategy.
41. Decide synchronous vs asynchronous execution based on actual repository/expected load.
    Do not add Hangfire unless justified.
42. API/application use cases.
43. Minimal Run administration UI boundary.
44. Permissions/Audit.
45. DB constraints/indexes.
46. Performance.
47. Concurrency / multiple runs.
48. Testing plan with manually verifiable fixtures.
49. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..10 and actual Repository.
50. Smallest implementation sequence.
51. Explicit non-scope.

Testing proposal must cover at minimum:

FORECAST
- future monthly distribution exact total
- current month remaining distribution
- Jalali month boundary/leap year
- decimal residual
- no past-current double counting

BOM
- effective BOM by date
- Base Output Qty
- multi-level recursion
- shared Item demand aggregation

SCRAP
- default
- override
- explicit zero
- formula ×(1+%)

NETTING
- stock reduces need
- firm supply date matters
- parent stock reduces lower-level explosion
- shared stock not double-counted
- Safety Stock behavior

PROJECTED
- negative projected
- stockout date
- safety breach date

FIRM SUPPLY
- split schedules
- partial received remaining only
- cancelled excluded
- missing ETA excluded
- overdue ETA excluded

COVERAGE
- stock coverage
- firm coverage
- no future demand
- beyond horizon
- incomplete data

ROUTES
- BUY
- MAKE
- OUTSOURCE with/without BOM
- HYBRID one route
- HYBRID both routes without decision => exception

EXCEPTIONS
- missing Forecast
- missing BOM
- ambiguous BOM
- missing external source readiness where relevant
- inactive warehouse stock warning

REPRODUCIBILITY
- same input/engine => same output
- later source-data changes do not mutate historical Run

Output:
1. Understanding
2. Repository Findings
3. Run / Persistence Model
4. Input Trace Strategy
5. Daily Demand Model
6. BOM / Scrap Model
7. Multi-Level Netting Algorithm
8. Safety Stock / Projected Balance
9. Firm Supply / ETA
10. Coverage
11. Route Logic
12. Exception / Completeness Model
13. Explainability / Trace
14. API / UI Boundary
15. Permissions / Audit
16. Performance / Concurrency
17. Tests
    Map approved Golden fixtures GD-01, GD-02, GD-03, MRP portions of GD-08 and GD-09 to the actual repository/domain model. Verify the design can reproduce every approved numeric result without changing expected values or generating them through the production calculator.
18. Risks / Contradictions
19. Implementation Plan
20. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 10B — MRP Calculation Engine Implementation

```text
# MRP-10B — Implement MRP Calculation Engine

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
- docs/modules/mrp/features/MRP-09-PURCHASE-SUPPLY-FOUNDATION-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-10-MRP-CALCULATION-ENGINE-PACKAGE-FINAL.md
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-10A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-10 Calculation Engine.

If MRP-10A contains unresolved material contradictions, STOP.

RUN
Implement approved MRP Run model:
- UUID technical ID
- server-generated immutable Run Number `MRP-RUN-{sequence:D6}`
- As-Of Date
- Horizon End
- Engine Version
- Run Status
- timestamps
- actor/audit boundary
- known assumptions/simplifications
- input trace/snapshots
- results
- exceptions

Do not overwrite previous official Runs.

Default horizon is the Jalali Period containing As-Of plus the next 11 Periods; default Horizon End is the canonical Gregorian end date of the twelfth Period. Permit explicit override only when `HorizonEnd >= AsOf`; do not impose a hard database horizon limit.

EXECUTION
Use bounded synchronous execution in v1:
- create RUNNING Run
- use a short `REPEATABLE READ` transaction for bulk source capture and immutable Run-input persistence
- commit that snapshot transaction before calculation
- calculate only from persisted Run input snapshots without re-reading mutable sources
- atomically persist all official results and final status
- FAILED Run exposes no partial official results

Do not hold a long `REPEATABLE READ` transaction while calculating.

INPUT TRACE
Preserve approved:
- Forecast Revision references/snapshots
- BOM Revision references
- Starting Inventory used
- Firm Supply Schedule Qty/ETA used
- HYBRID route decisions
- for NEW Runs, immutable Run+Item+DemandDate ResolvedPlanningRoute (`EXTERNAL` / `INTERNAL_BOM` / `INCOMPLETE`) and traceable origin/reason (`BUY` / `MAKE` / `OUTSOURCE_EXTERNAL` / `OUTSOURCE_BOM` / `HYBRID_EXTERNAL` / `HYBRID_INTERNAL`)
- for NEW Runs, immutable Item Category identity/names and Critical/Safe/Excess Day thresholds
- for NEW Runs, immutable Firm Supply Supplier identity/number/names for historical Supply Trace only

Do not backfill an existing historical Run from live Catalog, Supplier or PO data. These additional fields are trace/presentation context only and must not change MRP calculation formulas or Golden expected outputs.

Resolved route is planning truth, not presentation-only context. Persist it without changing NetRequirement or route calculation behavior. Existing historical Runs remain unchanged and must never receive a live route fallback.

Historical Run result must not drift when source data changes later.

FORECAST
Implement approved daily distribution:
- future month forecast across calendar days
- current month Remaining Forecast only where `Demand Date > As-Of Date` and `Demand Date <= Jalali Planning Period End`
- As-Of allocation = 0; allocation begins at As-Of + 1 calendar day
- no cross-period spill; positive remainder with zero eligible days returns `CURRENT_PERIOD_REMAINDER_UNALLOCATABLE`
- deterministic 12-decimal allocation
- exact residual assigned to the LAST eligible day
- exact Period-quantity reconciliation
- no double counting of past actual demand

Treat Forecast as Production Plan under approved baseline.
Do not net Product Finished Goods inventory against planned production.
Do not apply Product Safety Stock as a production adjustment in MRP-10 v1.
Explode root forecasted Product demand directly through its effective Product BOM.

BOM
Resolve effective BOM by each demand date.

Use:
Normalized Usage = Usage Qty / Base Output Qty

SCRAP
Resolve:
- BOM override when non-null
- otherwise Item default
- 0 explicit zero

Formula:
Required = Base Requirement × (1 + Scrap%/100)

Do not use yield formula.

NETTING
Implement approved shared-material, time-phased multi-level netting:

- aggregate gross demand for Item/date across all sources
- start from actual pooled company stock
- add usable dated Firm Incoming
- subtract gross demand
- calculate projected balance / net need
- apply Safety Stock planning floor
- for MAKE/Outsource-BOM route, explode only Net Requirement
- do NOT explode gross requirement already covered by parent stock/firm supply

For Part/Component use exactly:

```text
FirmProjected = previous FirmProjected + eligible Firm Incoming - Gross Demand
PlanningBeforeNeed = previous PlanningBalance + eligible Firm Incoming - Gross Demand
NetRequirement = MAX(SafetyStock - PlanningBeforeNeed, 0)
PlanningBalance = PlanningBeforeNeed + NetRequirement
```

`NetRequirement` is hypothetical planning need, never Firm Supply. Stockout and Coverage use factual supply views, not `PlanningBalance`.

Use internal process lead time = 0 days.
Child demand date equals Parent Net Requirement date. Persist this assumption in the Run.

PRECISION
Persist MRP calculation quantities as `numeric(28,12)`. Use internal .NET decimal calculation without premature business rounding.

PROJECTED ON HAND
Use actual/firm facts only.

Do not include recommendations/hypothetical supply.

Allow negative projected values.

FIRM SUPPLY
Consume only approved eligible PO/Schedule remaining quantities.

Schedule ETA is authoritative for existing PO.

Do not fallback silently to current Sourcing lead time.

Exclude from timed projection:
- ETA Missing
- Overdue unreceived supply

Surface both as exceptions/summary quantities.
Missing/Overdue Firm Supply normally does not make demand itself unknown.

WAREHOUSE POOL
Use total actual company inventory across Warehouses.

Count real stock in inactive Warehouse according to approved baseline but generate warning.

Do not implement warehouse allocation.

COVERAGE
Implement approved sequential:
- Stock Coverage
- Firm Supply Coverage

Use explicit states:
- DATE, meaning the last fully covered demand date
- BEYOND_HORIZON
- NO_FUTURE_DEMAND
- INCOMPLETE_DATA

Do not use fake 9999/infinity values.
Stock Coverage uses Starting Actual On Hand only. Firm Supply Coverage adds eligible dated Firm Supply. Safety Stock breach is separate. Do not generate Finished-Goods coverage for a root forecasted Product.

ROUTES
Implement approved logic:

BUY:
- net external requirement
- no BOM explosion

MAKE:
- net
- BOM explosion
- missing required BOM => exception

OUTSOURCE:
- with valid BOM => net then explode
- without BOM => external requirement

HYBRID:
- if only one route ready, use it and trace selection
- if both ready, require approved Run route decision
- if both ready and no decision, exception/incomplete
- do not automatically prefer price/speed/make/buy
- decision grain is Item + Run
- selected route must remain valid for every affected Demand Date in the Run
- if no single route covers all affected dates, return explicit exception/incomplete
- never silently split Make/Buy by date

For every affected Item/date, persist the actual canonical resolved route and origin/reason. Do not defer route inference to MRP-12.

EXCEPTIONS
Do not silently replace missing critical data with zero.

Implement approved derived completeness:
- COMPLETE
- COMPLETE_WITH_WARNINGS
- INCOMPLETE
or exact approved labels from 10A.

`NO_PLANNING_READY_EXTERNAL_SOURCE` affects downstream actionability/completeness but must not alter computed Net Requirement quantity.

RESULTS
Implement approved persistent/read model for:
- daily Item result
- Item summary
- exceptions
- demand/BOM trace sufficient for explainability

At minimum summary supports:
- Starting Actual On Hand
- Firm Incoming
- ETA Missing Firm Qty
- Overdue Firm Qty
- Expected Stockout Date
- Safety Stock Breach Date
- Stock Coverage
- Firm Supply Coverage
- Net Requirement
- Planning Completeness

Do not implement final procurement recommendation.

DETERMINISM
Same Engine Version + same inputs + same route decisions must produce same result.

No AI/probabilistic calculation.

READ-ONLY SOURCE DATA
MRP execution must not modify:
- Forecast
- BOM
- Inventory
- Sourcing
- Purchase Supply

REFERENCE DATA / ITEM IDENTITY
Reuse MRP-01 typed Reference Data and Unit ownership. Do not create shadow Unit, Category, Business Unit, Currency or other reference masters. Historical inactive references remain resolvable; newly assigned references follow their owning module's active-reference rules. Official Item Code is `Item.BusinessNumber`; do not reintroduce manual or parallel Item Code fields. Preserve `fa-IR => NameFa` and `en => non-empty NameEn, otherwise NameFa` without translating stored business values.

BACKEND
Implement only approved:
- calculation domain/services
- persistence/read models
- migrations if required
- API
- validation
- errors
- efficient data loading
- transaction handling

Do not add microservices/Kafka/Redis.

Do not add Hangfire unless MRP-10A specifically approved it based on actual need.

FRONTEND
Implement only approved minimal Run UI:
- Run list
- create/run parameters
- route-decision input where required
- Run status
- exception summary
- high-level result inspection
- trace access where approved

Use the existing Hadron ERP shared UI/UX and i18n foundation with FA/EN, RTL/LTR, server paging/filtering, URL context preservation and locale-aware reference display.

Full Material Planning UI belongs to MRP-11.

PERMISSIONS/AUDIT
Reuse existing platform.
Do not build parallel Identity/Audit infrastructure.

TESTS
Implement meaningful deterministic tests for all approved MRP-10A scenarios, including:
- canonical Golden Regression for GD-01, GD-02, GD-03, MRP portions of GD-08 and GD-09
- expected values encoded independently from engine outputs; never invoke the production calculator to generate expectations and never edit Golden values to make tests pass
- Forecast distribution
- exact residual
- persisted `numeric(28,12)` quantities and no premature business rounding
- default 12-Jalali-Period horizon and explicit override validation
- immutable generated `MRP-RUN-{sequence:D6}` numbering
- short snapshot transaction / calculation outside transaction / atomic finalization
- effective BOM by date
- Base Output Qty
- Scrap formula/default/override/zero
- multi-level netting
- parent stock reducing child demand
- shared Item aggregation
- Safety Stock
- separate FirmProjected and PlanningBalance formulas
- Product Finished Goods and Product Safety Stock excluded from root production adjustment
- negative projected balance
- stockout
- Firm schedule date handling
- split schedules
- partial receipt remaining qty
- missing ETA
- overdue ETA
- coverage states
- DATE as last fully covered demand date and factual Stock/Firm views
- BUY/MAKE/OUTSOURCE/HYBRID
- HYBRID Item+Run validity across every affected Demand Date and no date split
- persisted resolved-route state/origin at Run+Item+DemandDate for BUY/MAKE/OUTSOURCE/HYBRID and INCOMPLETE cases
- no formula/Golden-output change from the route snapshot extension
- `NO_PLANNING_READY_EXTERNAL_SOURCE` leaves Net Requirement unchanged
- missing BOM
- missing forecast
- historical Run immutability/reproducibility and FAILED Run with no partial official results
- pooled warehouse stock warning
- ReferenceType count = 8 and no shadow reference masters
- Official Item Code = Item.BusinessNumber
- FA/EN locale-aware reference display without modifying stored values
- no source-data mutation

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend install if required
- frontend build/lint
- startup/health checks
- representative deterministic MRP fixture

Do not claim unrun checks.

SCOPE GUARD
Do NOT implement:
- Supplier recommendation
- MOQ rounding
- order quantity recommendation
- order date recommendation
- expert decision
- Purchase Order creation
- Recommendation→PO
- warehouse allocation
- Production Calendar
- capacity planning
- nonzero internal process routing
- inventory posting
- Cash Flow
- Lead-Time Analytics
- AI calculation

Final report:
- Changed
- Database / Run Persistence
- Input Trace
- Daily Demand
- BOM / Scrap
- Multi-Level Netting
- Safety Stock / Projected
- Firm Supply
- Coverage
- Route Logic
- Exceptions
- Results / Trace
- API
- UI
- Performance
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-11.
```

---

# D. Prompt 10C — MRP Calculation Engine Independent Audit

```text
# MRP-10C — MRP Calculation Engine Independent Audit

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
- docs/modules/mrp/features/MRP-09-PURCHASE-SUPPLY-FOUNDATION-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-10-MRP-CALCULATION-ENGINE-PACKAGE-FINAL.md
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-10A Analysis
- MRP-10B implementation report

Inspect all MRP-10 implementation files.

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
   - Supplier recommendation
   - MOQ/order rounding
   - Order Planning
   - PO creation
   - Inventory posting
   - Warehouse allocation
   - capacity planning
   - Cash Flow
   - Lead-Time Analytics
   - AI calculation

2. Run Integrity
   - immutable official Runs
   - UUID technical identity
   - server-generated immutable `MRP-RUN-{sequence:D6}` Run Number
   - As-Of/Horizon
   - default Horizon is As-Of Jalali Period plus next 11 Periods, ending at the Gregorian end of the twelfth Period
   - explicit override requires HorizonEnd >= AsOf and no hard DB limit exists
   - Engine Version
   - status
   - failure/incomplete handling
   - bounded synchronous pipeline uses a short REPEATABLE READ source-snapshot transaction, calculates outside it from persisted immutable inputs, and atomically finalizes official results
   - FAILED Run exposes no partial official results

3. Historical Reproducibility
   - Forecast Revision trace
   - BOM Revision trace
   - Starting Inventory used
   - Firm Schedule Qty/ETA used
   - NEW Run Item snapshots preserve Category identity/names and Critical/Safe/Excess Day thresholds
   - NEW Run Firm Supply snapshots preserve Supplier identity/number/names for historical trace
   - existing Runs are never completed from live Catalog/Supplier/PO data
   - HYBRID route decisions
   - NEW Run+Item+DemandDate results persist canonical ResolvedPlanningRoute and traceable origin/reason
   - legacy Runs are not backfilled and downstream consumers cannot infer historical route from live Master Data
   - later source changes do not mutate historical result

4. Forecast Semantics
   - Forecast treated as production plan
   - no Product finished-goods inventory net against production plan
   - no Product Safety Stock production adjustment in MRP-10 v1
   - root forecasted Product demand explodes directly through effective Product BOM
   - current month uses Remaining Forecast
   - no past-actual double counting

5. Daily Distribution
   - calendar-day baseline
   - Jalali month boundaries
   - Gregorian canonical dates
   - As-Of excluded and next-day start
   - no allocation beyond current Jalali Period End
   - zero eligible days handled as explicit unallocatable state
   - exact residual reconciliation
   - deterministic 12-decimal allocation with residual on the last eligible day
   - persisted quantities use numeric(28,12) and internal decimal calculation has no premature business rounding
   - no damaging early rounding
   - future tests cover normal mid-period, exact division, decimal residual, final day, Actual >= Forecast and Jalali leap/month boundary

6. BOM Resolution
   - effective by demand date
   - Base Output Qty normalization
   - recursive multi-level
   - no fixed-depth limitation

7. Scrap
   - override/default/zero semantics
   - formula ×(1+%)
   - no yield formula
   - no accidental multiple scrap application

8. Shared Demand / Netting
   - shared Item demand aggregated
   - shared stock not double-counted
   - parent Item supply netted before child explosion
   - only Net Make requirement explodes
   - Part/Component FirmProjected and PlanningBalance are separate
   - NetRequirement = MAX(SafetyStock - PlanningBeforeNeed, 0)
   - hypothetical NetRequirement never becomes Firm Supply

9. Safety Stock
   - Planning Floor
   - not Gross Demand
   - not Firm Supply
   - no repeated daily double-counting
   - Product Safety Stock is not applied to root forecasted production in v1

10. Projected On Hand
    - Actual + dated Firm Incoming - Gross Demand
    - may be negative
    - recommendations not counted

11. Stockout / Safety Breach
    - correctly distinct

12. Firm Supply
    - approved PO statuses
    - remaining schedule qty only
    - split schedules by date
    - partial receipts do not double count
    - cancelled excluded

13. Missing / Overdue ETA
    - no invented date
    - missing ETA excluded from timed balance
    - overdue unreceived supply not assumed available
   - surfaced clearly
   - normally does not make demand itself unknown

14. Warehouse Pool
    - total actual company stock baseline
    - no allocation algorithm
    - inactive warehouse stock warning

15. Coverage
    - sequential demand
    - Stock Coverage
    - Firm Supply Coverage
    - no average-demand shortcut
    - exact DATE / BEYOND_HORIZON / NO_FUTURE_DEMAND / INCOMPLETE_DATA states
    - DATE means last fully covered demand date
    - Stock Coverage uses Starting Actual On Hand only
    - Firm Coverage adds only eligible dated Firm Supply
    - factual coverage does not use PlanningBalance
    - root forecasted Product has no misleading Finished-Goods coverage

16. Route Logic
    - BUY
    - MAKE
    - OUTSOURCE with/without BOM
    - HYBRID no guessing
    - one-route automatic resolution trace
    - HYBRID decision grain = Item + Run and route remains valid for every affected Demand Date
    - no silent date-specific Make/Buy split; no single valid route creates exception/incomplete
    - persisted resolved route is exactly EXTERNAL / INTERNAL_BOM / INCOMPLETE with canonical BUY/MAKE/OUTSOURCE/HYBRID origin
    - route snapshot addition does not alter NetRequirement, formulas or Golden outputs
    - internal process lead = 0 day assumption visible
    - child demand date equals parent Net Requirement date

17. Exceptions
    - no silent zero
    - incomplete affected results
    - unaffected items may still calculate where safe
    - NO_PLANNING_READY_EXTERNAL_SOURCE does not alter Net Requirement quantity

18. Explainability
    - demand/BOM/source trace sufficient
    - not excessively broken by missing references
    - Engine Version visible

19. Database Integrity
    - result/run relations
    - numeric(28,12) persisted calculation precision
    - indexes
    - migration safety
    - no destructive cascades

20. Performance
    - no per-day/per-item DB query loops
    - batched input loading
    - recursive processing efficient
    - no unnecessary microservice/cache infrastructure

21. Determinism
    - same inputs produce same output
    - no AI/random behavior

22. Source Data Safety
    - MRP Run does not mutate Forecast/BOM/Inventory/Sourcing/PO

23. Reference Data / Item Identity
    - MRP-01 remains the typed Reference Data and Unit source of truth
    - no shadow reference master exists
    - historical inactive references remain resolvable
    - ReferenceType canonical seed count remains 8
    - Official Item Code is Item.BusinessNumber with no manual/parallel Item Code
    - fa-IR uses NameFa and en uses non-empty NameEn with NameFa fallback
    - stored business/reference values are never auto-translated

24. UI Boundary
    - minimal Run UI only
    - Material Planning UI not prematurely duplicated
    - FA/EN and RTL/LTR use the existing Hadron ERP UX/i18n foundation

25. Permissions / Auditability

26. Tests
    Verify meaningful numerical assertions, especially:
    - Golden inputs and expected outputs exactly match canonical v1.0
    - GD-01, GD-02, GD-03, MRP portions of GD-08 and GD-09 are asserted
    - tests are not tautological and the production calculator is not used to generate expected results
    - parent stock reduces child explosion
    - shared material aggregation
    - Scrap
    - missing/overdue ETA
    - current-month distribution
    - exact last-day residual at 12-decimal precision
    - default/override horizon semantics
    - separate FirmProjected/PlanningBalance equations
    - root Product inventory/Safety Stock exclusion
    - HYBRID Item+Run all-date validity
    - external-source incompleteness without Net Requirement mutation
    - synchronous snapshot/finalization transaction boundaries and failed-run atomicity
    - historical Run reproducibility
    - ReferenceType count 8, no shadow masters, Item.BusinessNumber identity and FA/EN reference fallback

27. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..10
    - approved MRP-10A
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
- Calculation Correctness
- Historical Reproducibility
- Specification Compliance
- Scope Compliance
- Recommendation

Do not modify anything.
STOP.
```

---

# E. Final Pre-Codex Audit — MRP-10

## Result

`PASS`

## Audited Approved Decisions

تمام ۲۴ تصمیم تأییدشده Package 10 پوشش داده شده‌اند:

1. Daily Calculation Grain.
2. Run-specific Planning Horizon.
3. Immutable/versioned MRP Runs.
4. Forecast Revision trace.
5. BOM Revision trace by date.
6. Starting Inventory trace/snapshot.
7. Firm Supply Qty/ETA trace.
8. Scrap formula = ×(1+Scrap%).
9. Scrap override/default resolution.
10. High-precision intermediate math.
11. Calendar-day Forecast distribution.
12. Exact daily residual reconciliation.
13. BOM selection by Demand Date.
14. Multi-Level Netting before lower-level explosion.
15. Safety Stock as Planning Floor.
16. Firm Projected On Hand separated from hypothetical recommendations.
17. Total-company inventory pool baseline.
18. Stock Coverage + Firm Supply Coverage.
19. Overdue Firm Supply excluded from availability.
20. Missing ETA Firm Supply excluded from timed projection.
21. BUY / MAKE / OUTSOURCE logic.
22. HYBRID route decision behavior.
23. Internal Process Lead Time baseline = 0 days.
24. Missing critical data creates Exceptions rather than guessed zeros.

## Open Question Resolution

### Exact Scrap Formula

CLOSED:

```text
Requirement × (1 + Scrap% / 100)
```

### Planning Horizon

CLOSED:

```text
Run-specific As-Of + Horizon End
Default = Jalali Period containing As-Of + next 11 Periods
Default Horizon End = Gregorian end of the twelfth Period
Explicit override requires HorizonEnd >= AsOf
No hard database limit
```

### Internal Component→Part Process Lead Time

CLOSED for v1:

```text
0 Calendar Days
```

until Production/Routing exists.

## Additional Cross-Package Semantic Audit

A critical semantic distinction was made explicit:

### Product Forecast

In the current MRP project baseline, Product Forecast is treated as:

```text
Planned Production Quantity
```

not Sales Demand.

Therefore Finished Goods inventory must not automatically reduce planned Product production quantity.

Part/Component inventory is netted normally.

MRP-10A verified this meaning against the canonical project documents. It is locked for MRP-10B.

## Cross-Package Consistency

- MRP-07 supplies Forecast Revision and Remaining Forecast.
- MRP-06 supplies effective-dated BOM.
- MRP-08 supplies Actual Inventory only.
- MRP-09 supplies dated Firm Incoming only.
- MRP-04 supplies Material Safety Stock and Supply Method.
- MRP-05 supplies external sourcing readiness but not actual existing-PO ETA.
- MRP-10 does not create supplier/order decisions.
- Package 11 consumes MRP results for Material Planning UI.
- Package 12 transforms need into procurement recommendation.
- Package 13 creates/operates POs and receipts.
- Package 14 audits analytics/integration.

No material ownership conflict remains.

## Architecture Corrections Applied

- Multi-level explosion is explicitly preceded by Item-level netting.
- Shared material inventory cannot be consumed independently by each Product path.
- Existing PO ETA is never reconstructed from mutable Sourcing configuration.
- Missing/overdue Firm Supply is prevented from overstating future availability.
- Safety Stock is separated from Gross Demand.
- Projected On Hand intentionally allows negative values.
- Recommendation quantities are excluded from Firm projection.
- Forecast daily distribution preserves exact monthly total.
- Effective BOM is selected per demand date, not per Run date.
- Internal process timing simplification is explicit and replaceable.
- Historical official Run result must persist and not drift.
- Calculation engine remains deterministic and AI-free.

## Remaining Intentional Deferred Scope

For later Packages:

- future Production Calendar / working days
- future nonzero internal process lead time
- Warehouse allocation policy
- Decision Horizon for Package 12
- Priority classification for Package 12
- Over-Receipt for Package 13

## Prompt Audit

### MRP-10A
PASS

Analysis-only, Repository-aware, and specifically requires mathematical, temporal, semantic and persistence validation before implementation.

### MRP-10B
PASS

Implementation is strictly constrained to deterministic MRP calculation and explicitly blocks Supplier/MOQ/PO/Inventory mutation scope.

### MRP-10C
PASS

Independent read-only audit checks calculation correctness, reproducibility, netting order, ETA handling, coverage, route logic, exceptions, determinism and specification drift.

## Final Decision

`MRP-10 MRP Calculation Engine` is approved for the project Prompt Baseline.

MRP-10A has been finalized and synchronized. MRP-10B is ready only when explicitly authorized by the Owner.
