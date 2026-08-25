# Hadron ERP — MRP Package 14 Final

**Package:** MRP-14 Analytics, Reporting & Final Integration  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 14-ANALYTICS-REPORTING-INTEGRATION

## 1. Purpose

این Feature آخرین Package طراحی MRP است.

MRP-14 باید:

- Lead-Time Performance Analytics
- Forecast Accuracy
- Procurement Financial Commitment Contract
- Reporting / Export
- Golden Regression Suite
- End-to-End Integration Regression
- final integration safeguards

را ایجاد کند.

MRP-14 نباید مالک عملیات Procurement، Inventory، Accounting یا Payment Execution شود.

هدف این Package:

```text
Operational Data
→ Measurable Performance
→ Controlled Recommendation
→ Auditable Improvement
```

و در سطح معماری:

```text
MRP-01..14
→ one coherent deterministic system
```

---

# 2. Analytics Source of Truth

### LOCKED DECISION

Analytics فقط از داده‌های واقعی تاریخی و auditable سیستم استفاده می‌کند.

منابع مجاز:

- Sourcing configuration snapshots
- PO / PO Line
- Delivery Schedule
- Original Planned milestones
- Revised Planned milestones
- Actual milestones
- Receipt facts
- Forecast revisions
- Actual Production
- MRP Run references

Analytics نباید Performance value دستی و مستقل ایجاد کند.

---

# 3. Configured / Original / Revised / Actual Separation

### LOCKED DECISION

چهار مفهوم همیشه مستقل می‌مانند:

```text
Configured
Original Plan
Revised Plan
Actual
```

Do not overwrite one with another.

Example:

```text
Configured Lead Time = 45d
Original Planned Receipt = 1405/07/10
Revised Planned Receipt  = 1405/07/20
Actual Receipt           = 1405/07/18
```

All four remain historically visible.

---

# 4. No Automatic Master Data Overwrite

### LOCKED DECISION

Analytics may recommend a new Sourcing Lead Time.

It must NOT automatically modify:

- Supplier Lead Time
- Forwarder-to-Shipment Days
- Shipping Days
- Customs/Destination Days
- Total Configured Lead Time

Applying a recommendation requires explicit authorized user action.

---

# 5. Lead-Time Analytics Grain

### LOCKED DECISION

Lead-Time performance is primarily calculated at:

```text
Sourcing Option
```

Meaning the effective Supplier + Item + Route combination represented by MRP-05.

This is preferred over Supplier-only aggregation because logistics route materially changes lead time.

Higher-level Supplier summaries may aggregate for reporting.

---

# 6. Observation Unit

### LOCKED DECISION

One valid:

```text
Delivery Schedule
```

is one Lead-Time performance observation.

Do not use entire PO as one observation when schedules have different dates/routes.

---

# 7. Valid Lead-Time Observation

A Schedule can enter Lead-Time recommendation statistics only when required actual dates are sufficiently complete and reliable.

At minimum Total Lead Time observation needs:

```text
Actual Ordered Date
Actual warehouse receipt occurrence
```

according to the stage-mapping rules below.

Incomplete observations may remain visible diagnostically but do not enter recommendation statistics.

---

# 8. Cancelled Schedule Sample Policy

### LOCKED DECISION

A Schedule with cancelled committed quantity is excluded from Lead-Time recommendation sample by default.

Reason:

It is not a clean full-delivery timing sample.

However cancellations remain visible through separate reliability KPIs such as:

```text
Cancellation Rate
```

Do not hide Supplier failure by deleting the operational record.

Reliability metrics are locked as follows for the selected observation window:

The reliability cohort uses Schedules whose PO `OrderedDate` falls inside the selected window. This cohort is distinct from the clean completed Lead-Time sample, whose inclusion date is Final Receipt Completion Date.

```text
Schedule Cancellation Rate
=
Count(committed Schedules with CancelledQty > 0)
/
Count(all committed Schedules)
```

```text
Quantity Cancellation Rate
=
SUM(CancelledQty)
/
SUM(AllocatedQty)
```

Quantity Fulfillment is reported separately as valid Posted, non-reversed Received Qty divided by Allocated Qty. Open overdue Schedules are an operational reliability state; they do not enter the completed-sample late-delivery denominator. Completed late/on-time performance uses only clean completed Schedules with no cancelled committed quantity.

---

# 9. Sample Window

### LOCKED DECISION

Primary statistical window:

```text
Last 12 months
```

If:

```text
Valid Sample Count < 5
```

expand to:

```text
Last 24 months
```

If still:

```text
Valid Sample Count < 5
```

then:

```text
INSUFFICIENT_SAMPLE
```

and no statistical Lead-Time recommendation is issued.

---

# 10. Sample Date Basis

### LOCKED OWNER DECISION

```text
Observation Inclusion Date
=
Delivery Schedule Final Receipt Completion Date
```

A clean completed Schedule requires Ordered Date, zero CancelledQty and valid Posted/non-reversed Receipt history that completes its AllocatedQty. Partial Receipt is diagnostic until completion. Reversal removes completion until a later valid Receipt re-establishes it.

The primary trailing window is:

```text
(evaluationDate.AddMonths(-12), evaluationDate]
```

using canonical Gregorian business dates. If expansion is required, use the same boundary rule for 24 months. The upper bound is inclusive and the lower bound is exclusive. `evaluationDate` is explicit and returned in the response.

Do not mix Order Date for some observations and Receipt Date for others.

---

# 11. Lead-Time Statistics

### LOCKED DECISION

At minimum calculate:

- Sample Count
- Mean
- Median / P50
- P80
- P90
- Minimum
- Maximum
- On-Time %
- cancellation-related KPI separately
- configured vs actual variance

P50/Median represents typical historical performance. P80 remains the Default System Recommended Lead Time. P90 is the approved Conservative / Risk Lead-Time Reference and must be displayed/available, but never automatically replaces P80 or updates Sourcing.

---

# 12. P80 Method

### LOCKED DECISION

System Recommended Lead Time:

```text
P80
```

of valid historical observations.

Do not use Mean as the primary system planning recommendation.

P50, P80 and P90 use one authoritative continuous percentile implementation equivalent to PostgreSQL `percentile_cont`. Application-code implementations must reproduce identical semantics; frontend and backend must not diverge.

---

# 13. Why P80 Is Planning Recommendation

P80 provides a more conservative planning assumption than average while not targeting extreme worst-case behavior.

This is a business planning policy, not a claim that 80% is universally optimal.

The value remains an auditable recommendation only.

---

# 14. Apply Lead-Time Recommendation

### LOCKED DECISION

Authorized user may explicitly:

```text
Apply Recommendation
```

to update Sourcing configuration.

The action must preserve:

- Sourcing Option
- previous configured value
- recommended value
- applied value
- statistical method = P80
- Sample Count
- observation window
- actor
- timestamp
- Reason / note where required

No silent auto-update.

### LOCKED OWNER DECISION — PER-STAGE APPLY

- Total Lead-Time P80 remains an analytical recommendation and is not stored as a separate Sourcing field.
- Calculate P80 independently for each canonical configured stage.
- Convert each stage P80 to whole Calendar Days using `Ceiling`.
- Apply only explicitly selected stage values through one Sourcing-owned, versioned command.
- A stage with insufficient valid samples is not populated or applied automatically.
- Preserve previous, recommended and applied values per stage in durable apply history.
- P90 remains display-only conservative/risk context and is never applied automatically.

---

# 15. Stage Mapping — Cross-Package Amendment

### LOCKED CROSS-PACKAGE DECISION

To make configured and actual lead-time stages comparable without gaps/overlap:

## Supplier Lead

```text
ORDERED
→
SUPPLIER_READY
```

## Forwarder / Pre-Shipment

```text
SUPPLIER_READY
→
SHIPPED
```

`HANDOVER_TO_FORWARDER` remains a diagnostic milestone inside this stage.

## Shipping

```text
SHIPPED
→
ARRIVED_DESTINATION
```

## Destination / Customs

```text
ARRIVED_DESTINATION
→
WAREHOUSE_RECEIPT
```

`CUSTOMS_CLEARED` remains a diagnostic milestone inside this stage.

---

# 16. Existing "Customs Days" Semantic Clarification

### CROSS-PACKAGE AMENDMENT

The existing configured MRP-05 field historically called:

```text
Customs Days
```

is interpreted in v1 planning/analytics as:

```text
Destination Arrival
→
Warehouse Receipt
```

rather than only the literal time physically inside customs.

UI/documentation should use a clearer user-facing label where appropriate, for example:

```text
Destination / Customs Days
```

while preserving stable machine/schema compatibility unless Analysis justifies a migration.

---

# 17. Total Actual Lead Time

With approved stage mapping:

```text
Total Actual Lead Time
=
Actual Warehouse Receipt
-
Actual Ordered Date
```

and also equals the sum of valid stage durations when all stage boundaries exist.

No overlapping stage arithmetic.

---

# 18. Stage Statistics

Where actual milestone coverage allows, analytics should calculate P50/Median, P80, P90 and Mean per stage:

- Supplier Stage
- Forwarder / Pre-Shipment
- Shipping
- Destination / Customs

This helps identify where delays occur.

If stage milestone data is incomplete, Total Lead-Time sample may still be usable when the total boundaries are valid.

---

# 19. Original Plan On-Time Baseline

### LOCKED DECISION

Primary `On-Time %` is measured against:

```text
Original Planned Warehouse Receipt
```

not the repeatedly revised ETA.

Baseline:

```text
Actual Receipt <= Original Planned Receipt
→ On Time
```

Tolerance:

```text
0 days
```

in v1 unless a future policy explicitly introduces tolerance.

---

# 20. Revised Plan Performance

MRP-14 also reports:

```text
Actual vs Revised Planned Receipt
```

as a separate operational KPI.

This answers a different question:

> Did the latest ETA communicate reality accurately?

Do not replace Original Plan performance with Revised Plan performance.

---

# 21. Lead-Time Variance

At minimum support:

```text
Original Plan Variance Days
=
Actual Receipt - Original Planned Receipt
```

and:

```text
Revised Plan Variance Days
=
Actual Receipt - Current/Revised Planned Receipt
```

Positive = late.
Negative = early.

---

# 22. Supplier-Level Reporting

Supplier summary may aggregate multiple Sourcing Options and show:

- valid samples
- On-Time %
- cancellation rate
- average lateness
- Median/P80 where meaningful

But Sourcing Option remains primary grain for system Lead-Time recommendation.

---

# 23. Forecast Accuracy Scope

### LOCKED DECISION

Forecast Accuracy is calculated only for:

```text
CLOSED Planning Periods
```

Open/current periods are not final accuracy samples.

---

# 24. Forecast Baseline Definition

### LOCKED DECISION

For each Product + Closed Period:

```text
Baseline Forecast
=
last Official Forecast available at or before Period Start
```

If none existed:

```text
BASELINE_NOT_AVAILABLE
```

Do not treat a mid-period forecast as the original baseline.

The cutoff instant is the start of the Period's canonical `GregorianStartDate` in `Asia/Tehran`, converted to UTC. A Forecast Revision created exactly at that instant is included.

---

# 25. Final Forecast Definition

### LOCKED DECISION

```text
Final Forecast
=
last Official Forecast Revision before Period Close
```

This measures the quality of the final planning view.

The Close cutoff is the lifecycle timestamp of the latest effective `OPEN → CLOSED` event that leaves the Period currently Closed. If a Period is Reopened and then Closed again, the later Close event controls Final Forecast selection. Close/Reopen history must be consumed directly; current timestamps or live mutable context are not valid substitutes.

---

# 26. Actual Definition

Forecast Accuracy uses:

```text
Actual Production for the Closed Period
```

from MRP-07 / future Production authoritative source.

No manual replacement in Analytics.

### LOCKED OWNER DECISION — CLOSED PERIOD COMPLETENESS

A canonical `CLOSED` Period certifies that Actual Production is complete. For an in-scope Product with no Daily Actual rows in that Closed Period:

```text
Actual Production = 0
```

Absence of rows must not be reported as `ACTUAL_NOT_AVAILABLE`. That state is reserved for explicitly identified legacy/import incompleteness.

---

# 27. Forecast Row Metrics

For each valid Product + Period:

```text
Error
=
Forecast - Actual
```

```text
Absolute Error
=
ABS(Forecast - Actual)
```

If:

```text
Actual > 0
```

then:

```text
APE
=
Absolute Error / Actual
```

If:

```text
Actual = 0
```

then:

```text
APE = N/A
```

Do not divide by zero or invent a percentage.

---

# 28. Aggregate Forecast Metrics

### LOCKED DECISION

Primary aggregate accuracy metric:

```text
WAPE
=
SUM(Absolute Error)
/
SUM(Actual)
```

where aggregate Actual denominator is >0.

Bias:

```text
Bias
=
SUM(Forecast - Actual)
/
SUM(Actual)
```

where denominator is valid.

Do not use simple average MAPE as the primary aggregate KPI.

---

# 29. Baseline vs Final Forecast Analytics

Reports should show both:

- Baseline Forecast Accuracy
- Final Forecast Accuracy

This allows management to distinguish:

- quality of original planning
- effect of revisions during the period

---

# 30. Forecast Accuracy Grouping

Useful grouping may include:

- Product
- Product Category
- Business Unit where unambiguous
- Period
- rolling 3/6/12 month view

Do not create ambiguous allocations from shared materials.

Forecast accuracy is Product-level.

---

# 31. Forecast Missing Cases

Explicit states:

```text
BASELINE_NOT_AVAILABLE
FINAL_FORECAST_NOT_AVAILABLE
ACTUAL_NOT_AVAILABLE
```

where applicable.

`ACTUAL_NOT_AVAILABLE` applies only to explicitly marked legacy/import incompleteness. It does not apply to an in-scope Product in a canonical Closed Period merely because no Daily Actual row exists; that case is actual zero.

Do not silently convert missing forecast to zero.

---

# 32. Procurement Financial Commitment Contract

### LOCKED DECISION

MRP-14 does not build a full Cash Flow module.

It defines a standardized read contract from committed PO facts for future Treasury/Cash Flow use.

The contract must remain read-only relative to PO source data.

---

# 33. Credit Anchor Event — Cross-Package Amendment

### LOCKED CROSS-PACKAGE DECISION

Add to Supplier Default Payment Profile and future PO payment snapshots:

```text
Credit Anchor Event
```

Baseline stable codes:

```text
ORDERED
SHIPPED
WAREHOUSE_RECEIPT
```

---

# 34. Credit Anchor Validation

If:

```text
Credit % > 0
```

then:

```text
Credit Days > 0
Credit Anchor Event is required
```

If:

```text
Credit % = 0
```

Credit Anchor may be null.

This amends MRP-03 Supplier Payment Profile and PO snapshot requirements in MRP-09/13.

---

# 35. Payment Percent Integrity

Existing rule remains:

```text
Advance %
+
Before Shipment %
+
Credit %
=
100%
```

for active usable payment profiles.

---

# 36. Financial Commitment Components

For a committed PO:

## Advance

```text
Amount
=
PO Total × Advance %
```

Anchor:

```text
ORDERED
```

## Before Shipment

```text
Amount
=
PO Total × Before Shipment %
```

Anchor:

```text
SHIPPED
```

## Credit

```text
Amount
=
PO Total × Credit %
```

Anchor:

```text
Credit Anchor Event
+
Credit Days
```

---

# 37. Planned Financial Date Resolution

### LOCKED DECISION

For an event-anchored expected payment date, resolve event date in this order:

```text
Actual
↓
Current/Revised Plan
↓
Original Plan
↓
Unresolved
```

If no valid date:

```text
PAYMENT_DATE_UNRESOLVED
```

Do not invent a date.

### LOCKED OWNER DECISION — MULTI-SCHEDULE PO

For `SHIPPED` and planned `WAREHOUSE_RECEIPT` event resolution on a PO with multiple Delivery Schedules:

1. Determine the commercially required Schedule set; a fully cancelled Schedule with no remaining obligation is excluded.
2. Resolve each required Schedule independently using `Actual → Latest Revised → Original → Unresolved`.
3. If any required Schedule is unresolved, the PO-level event date is unresolved.
4. Otherwise the PO-level event date is the maximum resolved date across required Schedules.

Do not mix Schedule quantities into separate payment components; payment percentages remain PO-level.

---

# 38. ORDERED Anchor Date

For `ORDERED`, actual committed Ordered Date is authoritative.

There is no need to use Revised/Original milestone fallback for this event once PO is ORDERED.

---

# 39. SHIPPED Anchor Date

For expected/planned commitment:

```text
Actual Shipped
else Revised Shipped
else Original Shipped
else unresolved
```

---

# 40. WAREHOUSE_RECEIPT Anchor Date

For expected/planned commitment:

```text
Actual Final Receipt Date
else Revised Warehouse Receipt
else Original Warehouse Receipt
else unresolved
```

### LOCKED OWNER DECISION

For `WAREHOUSE_RECEIPT`, the financial Credit Anchor uses operational `Final Receipt Date` from MRP-13, never First Receipt Date. MRP-14 consumes this fact and must not create a parallel or manually editable Financial Receipt Date.

Final Receipt Date is resolved only from valid Posted, non-reversed Receipt history when commercially required receipt completion exists. Partial Receipt leaves the actual anchor unresolved. For `RECEIVED_WITH_CANCELLATION`, the last valid Posted Receipt required before completion is used; Cancellation Date is not an anchor. `FULLY_CANCELLED` with no received quantity has no WAREHOUSE_RECEIPT anchor. If reversal removes completion, the actual anchor becomes unresolved until a later valid Receipt re-establishes completion; that later completing Receipt becomes the new Final Receipt Date.

For a multi-Schedule PO, operational Final Receipt Date remains the actual PO-level `WAREHOUSE_RECEIPT` anchor when completion exists. Before actual completion, planned fallback follows the required-Schedule maximum/unresolved rule in §37.

---

# 41. Financial Commitment Currency

### LOCKED DECISION

Every commitment remains in:

```text
PO Currency
```

Do not perform FX conversion in MRP-14.

---

# 42. No Cross-Currency Total

Reports must not display a combined numeric total across currencies.

Allowed:

```text
USD total
CNY total
EUR total
AED total
```

Not allowed:

```text
Grand Total = USD + CNY + EUR
```

without FX.

---

# 43. Financial Contract Is Not Payment Execution

The contract represents:

```text
planned/expected procurement financial commitments
```

It does not mean:

- invoice posted
- payment executed
- AP settled
- bank transaction occurred

Future finance modules own those facts.

---

# 44. Analytics Center Navigation

### LOCKED DECISION

Recommended navigation:

```text
MRP
├── Runs
├── Material Planning
├── Exceptions
├── Order Planning
├── Purchase Supply
└── Analytics
    ├── Lead-Time Performance
    ├── Forecast Accuracy
    ├── Inventory Position & Movement
    └── Reports
```

Exact route names follow repository conventions.

---

# 45. Lead-Time Performance UI

Baseline Summary fields:

- Supplier
- Item
- Sourcing Option / Route
- Sample Count
- Configured Total Lead Time
- Mean
- Median / P50
- P80
- P90
- Min
- Max
- Original Plan On-Time %
- Revised Plan On-Time %
- Cancellation Rate
- System Recommended Lead Time
- Recommendation Status

---

# 46. Lead-Time Detail

Detail should support:

- stage-level statistics
- list of sample Schedules
- Ordered Date
- Original Plan
- Revised Plan
- Actual
- variance
- excluded-sample reason where applicable
- applied recommendation history

---

# 47. Recommendation Status

Suggested machine states:

```text
AVAILABLE
INSUFFICIENT_SAMPLE
NO_VALID_OBSERVATIONS
```

Applying the recommendation is a separate explicit action.

---

# 48. Forecast Accuracy UI

Baseline fields:

- Product
- Period
- Baseline Forecast
- Final Forecast
- Actual
- Baseline Error
- Baseline APE where valid
- Final Error
- Final APE where valid

Summary cards/read models may show:

- WAPE
- Bias
- sample Product count
- missing baseline count

---

# 49. Reporting Baseline

### LOCKED DECISION

Provide baseline reports:

1. MRP Run Summary
2. Material Risk Report
3. Order Planning Report
4. Purchase Order / Receipt Report
5. Lead-Time Performance Report
6. Forecast Accuracy Report
7. Inventory Position / Movement Report

---

# 50. MRP Run Summary Report

Should include:

- MRP Run
- As-Of
- Horizon
- Engine Version
- Critical / Stockout Material counts
- Incomplete Material count
- Exception counts
- Net Requirement quantities by relevant grouping

Do not create invalid cross-unit quantity totals across unrelated Items unless clearly grouped.

---

# 51. Material Risk Report

Uses MRP-11 selected Run semantics.

Must preserve:

- Run identity
- snapshot context
- Risk Band
- Coverage
- Stockout
- Completeness

Do not mix live data.

---

# 52. Order Planning Report

Uses persisted MRP-12 Recommendation / Expert Decision data.

Include:

- MRP Run
- Item
- Priority
- Need By
- Order By
- Machine Source
- Machine Qty
- Expert Decision
- Expert Final Qty/Source
- warnings
- Recommendation State

---

# 53. PO / Receipt Report

Include:

- PO
- Supplier
- Currency
- Ordered Qty
- Received Qty
- Cancelled Qty
- Open Qty
- Original ETA
- Current ETA
- First Receipt
- Final Receipt
- status
- Completion Outcome

---

# 54. Lead-Time Report

Include configured vs historical performance and recommendation.

Never automatically update Master Data by exporting/reporting.

---

# 55. Forecast Accuracy Report

Available only for Closed Periods under approved accuracy rules.

Must identify whether metrics refer to:

```text
Baseline Forecast
or
Final Forecast
```

## 55A. Inventory Analytics v1

### LOCKED OWNER DECISION

Inventory Analytics v1 is limited to facts supported by the MRP-08 ledger and persisted MRP Run results:

- Actual OnHand by Item + Warehouse and company-wide as-of position
- inbound/outbound movement by canonical transaction type and business date
- inactive-Warehouse stock warning
- Receipt/Reversal inventory effects
- MRP Run material Risk/Coverage from persisted MRP-10/11 results

The following are excluded until separate thresholds, consumption policy or valuation authority is approved:

- Slow-moving Inventory
- Inventory Turnover
- Inventory Valuation
- generic Excess Inventory KPI
- invented Days-on-Hand based on noncanonical consumption

Do not recalculate MRP Risk/Coverage from the ledger inside MRP-14.

---

# 56. Export

### LOCKED DECISION

Use server-side Excel export where appropriate.

CSV may be offered where already supported.

Export implementation should preserve semantic values and not rely solely on browser-rendered text.

UI and every export format must consume the same authoritative backend read contracts. React must not recalculate percentile, Forecast Accuracy, financial anchors, MRP Risk/Coverage or Inventory business metrics.

---

# 57. PDF Reporting

For formal report outputs where needed:

```text
HTML/CSS → PDF
```

following existing architecture.

PDF export is a reporting output, not a requirement to recreate every interactive grid.

---

# 58. Export Provenance

### LOCKED DECISION

Every exported report must include where applicable:

- Hadron Tavan ERP
- Report Name
- MRP Run
- As-Of Date
- Period
- Engine / Algorithm Version if relevant
- Generated At
- applied filters

This prevents orphaned spreadsheets/PDFs with unknown time context.

---

# 59. Analytics Read-Only Principle

### LOCKED DECISION

Analytics and Reports do not edit operational Source Data.

The only approved write action in MRP-14 baseline:

```text
Apply Lead-Time Recommendation
```

which is a controlled Sourcing update with audit.

---

# 60. Apply Recommendation Boundary

The Apply action must call an explicit Sourcing business command.

Do not let Analytics directly update database fields bypassing MRP-05 domain rules.

---

# 61. Historical Analytics Stability

Historical analytics should remain explainable.

If configured lead time changes today, old Schedule Actual data remains unchanged.

Reports may recompute statistics over selected historical windows, but must distinguish:

- historical observations
- current configured value
- recommendation as-of report generation time

---

# 62. No AI Statistics

All baseline statistics are deterministic.

No AI is required to calculate:

- P80
- P90
- Median / P50
- WAPE
- Bias
- On-Time %

Future AI may read and explain reports only. AI must not mutate Forecast, MRP, Recommendation, Sourcing, PO, Receipt or Inventory truth and must not execute Apply Lead-Time Recommendation.

---

# 63. Golden Regression Suite

### LOCKED DECISION

The approved canonical fixture is `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md` (v1.0). Create automated regression tests from this read-only baseline; do not generate new expected values or modify approved values to make tests pass.

It must contain known expected numeric outputs.

Fixture should cover at minimum:

- Product P1
- Product P2 sharing a material
- multi-level MAKE Part
- BUY Components
- one HYBRID Item
- Forecast current + future periods
- effective BOM revision change
- Scrap
- Safety Stock
- Inventory
- Firm Supply
- split schedule
- overdue supply
- partial receipt
- cancellation or reversal where useful
- Sourcing MOQ / Lead Time
- Order Recommendation
- Expert Decision

---

# 64. Golden Fixture Independence

Golden fixture must not depend on production/customer data.

It is deterministic synthetic test data.

No random quantities.

Expected results are independently documented in the canonical Golden file and must not be generated by the production calculators under test.

---

# 65. Golden Expected Results

At minimum explicit expected values for selected Items:

- daily Gross Demand
- Net Requirement
- child explosion quantity
- Projected On Hand
- Stockout Date
- Coverage state
- Recommendation Source
- Order By
- Base Qty
- MOQ-adjusted Qty
- PO Open Qty after receipt
- Inventory after receipt
- Firm Supply remaining

Tests must assert numbers, not merely "completed successfully".

---

# 66. End-to-End Regression

### LOCKED DECISION

At least one integration test must cover:

```text
Forecast
→ Effective BOM
→ MRP Run
→ Material Shortage
→ Order Recommendation
→ Expert ACCEPT/MODIFY
→ Draft PO
→ APPROVED
→ ORDERED
→ Firm Incoming
→ New MRP Run
→ Partial Receipt
→ Inventory
→ Remaining Firm Supply
→ New MRP Run
```

Expected quantities must be asserted at each major stage.

---

# 67. Double-Count Regression

Golden suite must explicitly prove absence of:

- shared Item inventory double counting
- past Actual demand double counting
- Receipt + full PO Firm Supply double counting
- cancelled Qty as Firm Supply
- received Qty remaining in Firm Supply
- Product finished-goods stock incorrectly reducing Production Plan
- schedule split duplication

---

# 68. Snapshot Regression

Tests must prove historical:

- MRP Run
- Recommendation
- PO commercial commitment

do not drift when current:

- Forecast
- BOM
- Sourcing
- Price
- Lead Time
- PO ETA
- Inventory

change later.

---

# 69. Calendar Regression

Include:

- Jalali month boundary
- leap behavior
- Gregorian canonical dates
- Asia/Tehran
- daily forecast residual

No string-date arithmetic.

---

# 70. Currency Regression

Tests must prove:

- original Currency preserved
- no cross-currency summation
- estimated commitment values remain currency-specific

---

# 71. Final Integration Audit Scope

### LOCKED DECISION

Before any Codex implementation begins, perform one independent Final Cross-Package Audit across:

```text
MRP-01
...
MRP-14
```

including every:

```text
Prompt A
Prompt B
Prompt C
```

---

# 72. Final Cross-Package Audit Objectives

Audit must search specifically for:

- duplicate Source of Truth
- contradictory field ownership
- status semantic conflicts
- quantity double counting
- Firm Supply double counting
- date/calendar inconsistency
- snapshot drift
- BOM semantic conflict
- Forecast semantic conflict
- warehouse ownership conflict
- source/route conflict
- currency aggregation errors
- transaction-boundary gaps
- missing concurrency protection
- prompt dependency errors
- premature scope leakage
- unclosed Open Questions
- incompatible naming/codes
- audit/history loss

---

# 73. Cross-Package Amendment: Credit Anchor Propagation

Final Cross-Package Audit must ensure `Credit Anchor Event` is propagated consistently to:

### MRP-03
Supplier Default Payment Profile

### MRP-09
PO Payment Snapshot foundation

### MRP-12
Recommendation commercial context if payment profile is displayed/snapshotted as part of future PO commitment

### MRP-13
Committed PO Payment Snapshot

### MRP-14
Financial Commitment Contract

No Package may use Credit Days without knowing the anchor when Credit% > 0.

---

# 74. Cross-Package Amendment: Lead-Time Stage Semantics

Final Cross-Package Audit must propagate/verify stage mapping consistently across:

### MRP-05
Configured Sourcing lead-time stage definitions

### MRP-09
PO lead-time snapshot / planned milestones

### MRP-13
Original/Revised/Actual milestone operations

### MRP-14
Actual statistics and Lead-Time Recommendation

No gap/overlap in stage semantics.

---

# 75. Final Audit Gate

Codex implementation may begin only when:

```text
FINAL CROSS-PACKAGE AUDIT = PASS
```

If:

```text
PASS WITH FINDINGS
```

material findings must be resolved before implementation.

If:

```text
FAIL
```

implementation must not begin.

---

# 76. Package 14 API / Read Models

Required conceptual use cases:

### Lead Time
- query Sourcing Option performance
- query Supplier aggregate performance
- get sample detail
- get P80 recommendation
- apply recommendation via controlled Sourcing command
- get apply history

### Forecast Accuracy
- query Closed Period accuracy
- filter Product/Category/Period
- get Baseline vs Final metrics

### Financial Commitment
- query PO commitment contract
- group by Currency
- expose unresolved dates

### Reports
- query/report datasets
- export Excel
- produce approved PDF reports where implemented

---

# 77. Permissions

At minimum architecture should distinguish:

- View Analytics
- View Forecast Accuracy
- View Lead-Time Performance
- Apply Lead-Time Recommendation
- Export Reports
- View Procurement Financial Commitments

Do not build parallel Identity.

---

# 78. Auditability

Apply Lead-Time Recommendation must be audited.

Report generation need not create heavyweight audit records unless existing platform convention requires it.

Core provenance must live in report/export metadata.

---

# 79. Performance

Analytics queries must use server-side aggregation/read models appropriate to the selected window.

Avoid:

- loading all Receipt history into browser
- one query per Schedule
- percentile calculation in React
- N+1 supplier/item queries

Use PostgreSQL/application aggregation as justified in Analysis.

---

# 80. Percentile Calculation Location

P50, P80 and P90 calculation must have one authoritative continuous-percentile implementation equivalent to PostgreSQL `percentile_cont`.

Preferred:

```text
Backend / database-supported deterministic analytics service
```

Do not implement a different percentile formula separately in frontend.

---

# 81. Forecast Metric Calculation Location

WAPE/Bias have one authoritative backend/read-model calculation.

Frontend displays results.

The same backend read contracts serve UI, Excel/PDF export and future integration consumers. Frontend KPI/business recomputation is forbidden.

---

# 82. Read Model Rebuildability

If materialized analytics/read-model tables are introduced, they must be rebuildable from operational facts.

They are not independent sources of truth.

---

# 83. Refresh Strategy

### LOCKED OWNER DECISION

MRP-14 v1 uses query-time backend analytics/read models with server-side aggregation, bounded filters and paging.

Do not add materialized analytics tables, scheduled background refresh or a broad BI warehouse unless measured query plans/data scale later prove the need and a separate decision approves it.

Do not introduce scheduled background jobs without need.

---

# 84. Reporting Currency Grouping

Any monetary report must group totals by Currency unless an explicit approved FX layer is later provided.

This rule applies to:

- Recommendations
- POs
- Financial Commitments
- procurement value reports

---

# 85. Report Snapshot vs Live Analytics

Reports generated from historical MRP Runs remain Run-specific.

Lead-Time/Forecast Analytics may be "as of now" over selected historical data windows.

UI/report headers must clearly distinguish:

```text
Run Snapshot Report
```

from:

```text
Current Historical Analytics
```

Current Historical Analytics must carry explicit `evaluationDate`, `GeneratedAtUtc`/data cutoff and algorithm version. Append-only facts, corrections and supersessions are resolved only when their creation timestamp is at or before that cutoff, so the same historical cutoff remains reproducible. Historical MRP Run reports continue to use immutable Run snapshots only and never fall back to live data.

---

# 86. Explicit Non-Scope

MRP-14 must NOT implement:

- Payment execution
- Bank integration
- AP accounting
- General ledger
- FX conversion
- exchange-rate sourcing
- Inventory valuation
- Landed Cost
- Supplier invoice matching
- Tax accounting
- AI auto-optimization
- auto-changing Sourcing Lead Time
- auto-changing Forecast
- auto-changing MOQ
- Production capacity analytics
- quality analytics
- broad enterprise BI warehouse

---

# 87. Acceptance Criteria

MRP-14 is acceptable when:

1. Analytics uses actual operational history only.
2. Configured / Original / Revised / Actual stay separate.
3. Lead-Time recommendation never auto-updates Master Data.
4. Sourcing Option is primary Lead-Time recommendation grain.
5. Delivery Schedule is observation unit.
6. Cancelled schedules are excluded from clean Lead-Time recommendation samples and reported separately.
7. Sample window = 12 months, expands to 24 if <5.
8. <5 valid samples after expansion => no recommendation.
9. Sample Count/Mean/Median-P50/P80/P90/Min/Max/Original On-Time/Revised Performance/Cancellation KPI are available.
10. P80 is system recommended planning lead time.
11. P90 is a conservative/risk reference and never replaces or auto-applies over P80.
12. Applying P80 is explicit/audited.
13. Stage mapping is continuous and cross-package consistent.
14. Original Plan is primary On-Time baseline.
15. Revised Plan performance is separate.
16. Forecast Accuracy uses Closed Periods only.
17. Baseline Forecast is last Official available at/before Period Start.
18. Final Forecast is last Official before Close.
19. Error/Abs Error/APE rules are correct.
20. WAPE + Bias are aggregate metrics.
21. Actual=0 does not create fake APE.
22. Credit Anchor Event exists for Credit% >0.
23. WAREHOUSE_RECEIPT Credit Anchor uses MRP-13 Final Receipt Date; First Receipt and Cancellation Date are never used.
24. Partial receipt, full cancellation, reversal and re-completion resolve/unresolve the anchor from operational facts without a duplicate financial date.
25. Financial Commitment dates use Actual→Revised→Original fallback.
26. Missing financial date remains unresolved, not guessed.
27. Financial commitments remain in PO Currency.
28. No cross-currency total exists.
29. Baseline Reports are available.
30. Exports include provenance metadata.
31. Analytics is read-only except controlled Apply Lead-Time Recommendation.
32. Golden deterministic regression fixture exists.
33. Golden expected numeric outputs are asserted.
34. End-to-End regression covers Forecast→MRP→Recommendation→PO→Receipt→Inventory→new MRP.
35. Snapshot and double-count regressions exist.
36. Final Cross-Package Audit is mandatory before Codex execution.
37. Lead-Time sample inclusion uses clean Schedule Final Receipt Completion Date and the approved trailing Gregorian boundary.
38. Cancellation count, cancellation quantity and fulfillment are separate metrics; cancelled Schedules do not enter clean completed On-Time/percentile samples.
39. Per-stage P80 uses Ceiling and is applied only through an explicit Sourcing-owned command; Total P80 is not stored as a fifth stage.
40. Closed Period absence of Daily Actual rows means Actual zero for an in-scope Product.
41. Reopen/reclose uses the latest effective Close event for Final Forecast cutoff.
42. Multi-Schedule PO anchors use the maximum resolved required-Schedule date and remain unresolved when any required Schedule is unresolved.
43. Inventory Analytics v1 excludes slow-moving, turnover, valuation and generic excess KPIs.
44. v1 analytics are query-time, server-aggregated and server-paged; no materialized/background refresh exists without measured approval.
45. UI/export/integration consume the same backend read contracts and do not recompute business KPIs.
46. Historical analytics preserve explicit cutoff/provenance and are reproducible without live fallback.
47. AI is read/explain only and cannot mutate operational truth or apply recommendations.

---

# 88. Locked Decisions

The following are locked for MRP-14:

1. Analytics from real auditable history.
2. Configured/Original/Revised/Actual separated.
3. No automatic Sourcing Lead-Time overwrite.
4. Lead-Time recommendation grain = Sourcing Option.
5. Observation unit = Delivery Schedule.
6. Cancelled schedule excluded from clean Lead-Time sample.
7. Sample window = 12 months, expand to 24 if sample <5.
8. Still <5 => INSUFFICIENT_SAMPLE.
9. Statistics include Count/Mean/Median-P50/P80/P90/Min/Max/Original On-Time%/Revised Performance/Cancellation KPI.
10. System Recommended Lead Time = P80.
11. P90 = Conservative/Risk Reference and is never auto-applied.
12. Apply Recommendation is explicit/audited.
13. Stage mapping:
    - ORDERED→SUPPLIER_READY
    - SUPPLIER_READY→SHIPPED
    - SHIPPED→ARRIVED_DESTINATION
    - ARRIVED_DESTINATION→WAREHOUSE_RECEIPT
14. HANDOVER_TO_FORWARDER and CUSTOMS_CLEARED remain diagnostic milestones.
15. Existing Customs Days semantics clarified as Destination/Customs stage.
16. Primary On-Time baseline = Original Planned Warehouse Receipt.
17. Revised Plan Performance remains separate.
18. Forecast Accuracy only on Closed Periods.
19. Baseline Forecast = last Official available at/before Period Start.
20. Final Forecast = last Official before Period Close.
21. Forecast metrics = Error / Absolute Error / APE where valid.
22. Aggregate metrics = WAPE + Bias.
23. Cash Flow itself is not built.
24. Procurement Financial Commitment Contract is built/read-modeled.
25. Credit Anchor Event codes = ORDERED / SHIPPED / WAREHOUSE_RECEIPT.
26. Credit%>0 requires CreditDays>0 + Credit Anchor.
27. WAREHOUSE_RECEIPT Credit Anchor = operational Final Receipt Date; First Receipt and Cancellation Date are excluded.
28. Reversal that removes completion clears the actual anchor; later re-completion establishes a new Final Receipt Date.
29. Planned financial date fallback = Actual → Revised → Original → Unresolved.
30. Currency stays original.
31. No cross-currency total.
32. Reports = MRP Run / Material Risk / Order Planning / PO-Receipt / Lead-Time / Forecast Accuracy.
33. Export provenance required.
34. Golden deterministic Regression Suite required.
35. End-to-End regression required.
36. Final Cross-Package Audit required before Codex.
37. Observation Inclusion Date = clean Delivery Schedule Final Receipt Completion Date.
38. Trailing windows use canonical Gregorian dates with `(evaluationDate.AddMonths(-N), evaluationDate]` boundaries.
39. Cancellation count rate, quantity cancellation rate and quantity fulfillment are distinct; clean completed performance excludes cancelled Schedules.
40. Stage recommendations use per-stage continuous P80, `Ceiling` to whole Calendar Days and explicit Sourcing-owned apply.
41. Total Lead-Time P80 is analytical only; it is not a fifth stored Sourcing duration.
42. Closed Period certifies Actual completeness; missing Daily Actual rows for an in-scope Product mean zero.
43. Final Forecast cutoff after Reopen/Reclose is the latest effective Close event that leaves the Period Closed.
44. Multi-Schedule PO anchor = maximum resolved required-Schedule date; any unresolved required Schedule makes the PO anchor unresolved.
45. Inventory Analytics v1 is physical position/movement plus persisted MRP Risk only; slow-moving/turnover/valuation/generic excess are excluded.
46. v1 uses query-time backend read models; no materialization or background refresh without measured approval.
47. UI and exports share backend read contracts; frontend KPI/business recomputation is forbidden.
48. Historical analytics use explicit evaluation/cutoff/version provenance; Run reports remain snapshot-only.
49. AI may read/explain only; Apply Lead-Time Recommendation remains an explicit Sourcing-owned human action.

---

# 89. Open Question Resolution

The previous open question:

`Forecast Accuracy details`

is now CLOSED for baseline.

Approved:

```text
Closed Periods only
Baseline Forecast = last Official at/before Period Start
Final Forecast = last Official before Close
Metrics = Error / Absolute Error / APE where valid
Aggregate = WAPE + Bias
```

The previous open question:

`Lead-Time statistical recommendation policy`

is now CLOSED.

Approved:

```text
Primary grain = Sourcing Option
Observation = Delivery Schedule
Window = 12m, expand to 24m if <5
Minimum valid samples = 5
Recommended Lead Time = P80
No automatic application
```

The ambiguity:

`Credit Days anchor`

is now CLOSED.

Approved:

```text
Credit Anchor Event required when Credit% >0

Codes:
ORDERED
SHIPPED
WAREHOUSE_RECEIPT
```

The lead-time stage semantic ambiguity is now CLOSED through approved cross-package Stage Mapping.

The MRP-14A Owner decisions are also CLOSED:

```text
Lead-Time inclusion = clean Schedule Final Receipt Completion Date.
Window boundary = (evaluationDate.AddMonths(-N), evaluationDate].
Cancellation/fulfillment metrics remain separate.

P80 is calculated per stage, rounded upward with Ceiling to whole Calendar Days,
and applied only through an explicit Sourcing-owned versioned command.

CLOSED Period certifies Actual completeness.
No Daily Actual rows for an in-scope Product = Actual zero.
Latest effective Close after Reopen/Reclose controls Final Forecast cutoff.

Multi-Schedule PO anchor = MAX(resolved required Schedule dates).
Any unresolved required Schedule = unresolved PO anchor.

Inventory Analytics v1 excludes slow-moving, turnover, valuation and generic excess.
Query-time backend read models are the v1 strategy.
```

---

# 90. Remaining Intentional Future Questions

Outside current MRP baseline:

- true Treasury/Cash Flow Module
- FX and exchange-rate policy
- AP/accounting
- inventory valuation / landed cost
- supplier quality analytics
- controlled over-receipt tolerance if business later requires it
- Production Calendar
- nonzero internal production lead time/routing
- capacity planning
- Quality/Quarantine stock
- enterprise BI / data warehouse

None blocks completion of MRP design.

---

# B. Prompt 14A — Analytics & Final Integration Analysis

```text
# MRP-14A — Analytics, Reporting & Final Integration Analysis

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
- docs/modules/mrp/features/MRP-11-MATERIAL-PLANNING-UI-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-12-ORDER-PLANNING-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-13-PURCHASE-ORDER-OPERATIONS-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-14-ANALYTICS-REPORTING-INTEGRATION-PACKAGE-FINAL.md
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior Packages were implemented merely because they were planned.

TASK:
Analyze MRP-14 Analytics, Reporting & Final Integration.

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

1. Understand MRP-14 as analytics/reporting/integration layer, not operational source of truth.
2. Inspect actual Repository implementation of MRP-03/05/07/09/10/12/13.
3. Verify the cross-package amendment for Lead-Time Stage Mapping.
4. Verify/plan propagation of Credit Anchor Event across Supplier/payment/PO snapshots.
5. Identify any conflict before implementation.
6. Design Lead-Time Analytics grain:
   - Sourcing Option primary
   - Delivery Schedule observation
7. Define valid observation criteria.
8. Define cancelled/excluded sample policy.
9. Define 12m→24m sample window.
10. Define minimum sample count = 5.
11. Specify one authoritative continuous percentile method equivalent to PostgreSQL `percentile_cont` for P50, P80 and P90. Verify exact GD-06 results, no frontend/backend disagreement, P80 recommendation role and P90 conservative-reference role without automatic application.
12. Define stage-level and total Lead-Time calculations.
13. Define Original Plan On-Time baseline.
14. Define separate Revised Plan performance.
15. Define cancellation/reliability KPIs.
16. Design controlled Apply Lead-Time Recommendation command through Sourcing domain.
17. Preserve apply-history/audit.
18. Define Forecast Accuracy for Closed Periods.
19. Define Baseline Forecast at/before Period Start.
20. Define Final Forecast before Close.
21. Define Actual source.
22. Define Error/Absolute Error/APE/WAPE/Bias formulas.
23. Handle Actual=0 and missing Forecast correctly.
24. Design Procurement Financial Commitment Contract.
25. Validate Credit Anchor Event:
    - ORDERED
    - SHIPPED
    - WAREHOUSE_RECEIPT
26. Analyze WAREHOUSE_RECEIPT using the locked MRP-13 operational Final Receipt Date. Cover partial receipts, received-with-cancellation, full cancellation, reversal of completion, re-completion after reversal, expected-date resolution and absence of a manually editable Financial Receipt Date. First Receipt and Cancellation Date must never become the financial anchor.
27. Define expected-date fallback:
    Actual → Revised → Original → Unresolved.
28. Keep Currency original; prohibit cross-currency total.
29. Design Analytics read models/APIs.
30. Design Reports:
    - MRP Run Summary
    - Material Risk
    - Order Planning
    - PO/Receipt
    - Lead-Time
    - Forecast Accuracy
31. Define server-side Excel export.
32. Define PDF approach only where useful and consistent with architecture.
33. Ensure export provenance.
34. Design Analytics UI/navigation.
35. Define permissions/audit.
36. Define performance/bulk aggregation.
37. Decide whether materialized read models are justified.
38. Map canonical Golden Dataset v1.0 into automated regression without changing or production-generating expected values.
39. Design mandatory GD-08 End-to-End Regression with all major quantities asserted.
40. Use GD-09 for Run/Recommendation/PO snapshot drift and GD-10 for currency isolation.
41. Identify all cross-package amendments required in MRP-01..13 docs/prompts before Codex.
42. Testing plan.
43. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..14 and actual Repository.
44. Smallest implementation sequence.
45. Explicit non-scope.

Testing proposal must cover at minimum:

LEAD TIME
- valid schedule observation
- cancelled schedule excluded
- 12m sample >=5
- 12m sample <5 expands to 24m
- still <5 => no recommendation
- Mean/Median-P50/P80/P90
- continuous percentile equivalent to PostgreSQL percentile_cont
- GD-06 exact Mean=14.6, P50=14, P80=17 and P90=19
- P80 default recommendation; P90 conservative reference only
- Original Plan On-Time
- Revised Plan performance
- stage mapping
- total lead time
- Apply Recommendation audit
- current Master update through Sourcing command only

FORECAST
- Closed Period only
- baseline available
- baseline missing
- final forecast
- Actual=0
- WAPE
- Bias
- Baseline vs Final comparison

FINANCIAL CONTRACT
- Advance anchored to Ordered
- Before Shipment Actual/Revised/Original fallback
- Credit Anchor Ordered
- Credit Anchor Shipped
- Credit Anchor Warehouse Receipt
- Credit Days offset
- unresolved anchor date
- currency preserved
- no cross-currency sum

REPORTING
- provenance metadata
- Run-specific snapshot report isolation
- currency grouping

REGRESSION
- golden numeric fixture
- shared material
- multi-level netting
- split schedule
- partial receipt
- cancellation
- new MRP after receipt
- recommendation→PO trace
- no double counts
- historical snapshot stability

Output:
1. Understanding
2. Repository Findings
3. Cross-Package Amendments
4. Lead-Time Analytics Model
5. Statistical Policy
6. Forecast Accuracy Model
7. Financial Commitment Contract
8. Analytics / Reporting Read Models
9. Export
10. UI
11. Permissions / Audit
12. Performance
13. Golden Regression Suite
14. End-to-End Regression
15. Tests
16. Risks / Contradictions
17. Implementation Plan
18. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 14B — Analytics & Final Integration Implementation

```text
# MRP-14B — Implement Analytics, Reporting & Final Integration

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
- docs/modules/mrp/features/MRP-11-MATERIAL-PLANNING-UI-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-12-ORDER-PLANNING-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-13-PURCHASE-ORDER-OPERATIONS-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-14-ANALYTICS-REPORTING-INTEGRATION-PACKAGE-FINAL.md
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-14A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only approved MRP-14 Analytics, Reporting & Final Integration.

If MRP-14A contains unresolved material contradictions, STOP.

CROSS-PACKAGE AMENDMENTS
Implement only the approved required amendments identified by 14A, including:
- Credit Anchor Event propagation
- Lead-Time stage semantic consistency

Do not opportunistically refactor unrelated Packages.

LEAD-TIME ANALYTICS
Implement primary grain:
- Sourcing Option

Observation:
- Delivery Schedule

Apply approved:
- valid-observation criteria
- inclusion date = clean Schedule Final Receipt Completion Date
- trailing Gregorian boundary = `(evaluationDate.AddMonths(-N), evaluationDate]`
- cancelled sample exclusion
- Schedule cancellation rate and quantity cancellation rate separately
- quantity fulfillment separately
- reliability cohort based on PO OrderedDate within the selected window
- open overdue excluded from completed late/on-time denominator
- 12-month window
- expand to 24 months when <5
- minimum sample count 5
- Mean
- Median / P50
- P80
- P90
- Min
- Max
- Original Plan On-Time %
- Revised Plan performance
- cancellation KPI

Use one deterministic continuous-percentile method equivalent to PostgreSQL `percentile_cont` for P50/P80/P90. GD-06 must assert Mean=14.6, P50=14, P80=17 and P90=19.

SYSTEM RECOMMENDATION
Recommended Lead Time = P80.
P90 = Conservative / Risk Lead-Time Reference only; expose it but never auto-apply it or substitute it for P80.

Do NOT auto-update Sourcing.

APPLY LEAD TIME
Implement explicit authorized action through Sourcing domain command.

- calculate P80 independently per canonical stage
- convert stage P80 to whole Calendar Days using Ceiling
- do not store Total P80 as a fifth configured duration
- do not populate/apply a stage with insufficient valid samples
- apply only explicitly selected stage values with ExpectedVersion

Audit:
- previous value
- recommendation
- applied value
- method
- sample count
- window
- actor
- timestamp
- reason/note

STAGE MAPPING
Use approved:
- ORDERED→SUPPLIER_READY
- SUPPLIER_READY→SHIPPED
- SHIPPED→ARRIVED_DESTINATION
- ARRIVED_DESTINATION→WAREHOUSE_RECEIPT

Keep:
- HANDOVER_TO_FORWARDER diagnostic
- CUSTOMS_CLEARED diagnostic

FORECAST ACCURACY
Implement Closed Period analytics.

Baseline:
last Official Forecast available at/before Period Start.

Final:
last Official before the latest effective Close event that leaves the Period Closed after any Reopen/Reclose.

Actual:
authoritative Production Actual.

For an in-scope Product in a canonical Closed Period, absence of Daily Actual rows means Actual zero. `ACTUAL_NOT_AVAILABLE` requires explicit legacy/import incompleteness and must not be inferred from no rows.

Metrics:
- Error
- Absolute Error
- APE where Actual >0
- WAPE
- Bias

Do not fabricate APE when Actual=0.

FINANCIAL COMMITMENT CONTRACT
Implement approved read model only.

Payment components:
- Advance anchored to ORDERED
- Before Shipment anchored to SHIPPED
- Credit anchored to configured Credit Anchor + Credit Days

Date fallback:
Actual → Revised → Original → Unresolved

For a multi-Schedule PO, resolve each commercially required Schedule independently. Exclude fully cancelled no-obligation Schedules; PO anchor is the maximum resolved required-Schedule date, and any unresolved required Schedule makes the PO anchor unresolved.

Keep original PO Currency.

No FX.
No payment execution.

CREDIT ANCHOR
Implement stable codes:
- ORDERED
- SHIPPED
- WAREHOUSE_RECEIPT

If Credit%>0:
- CreditDays>0
- CreditAnchor required

For WAREHOUSE_RECEIPT:
- actual anchor = MRP-13 operational Final Receipt Date
- partial Receipt leaves anchor unresolved
- received-with-cancellation uses the last required valid Posted Receipt, never Cancellation Date
- fully cancelled with zero received quantity has no anchor
- reversal that removes completion clears the anchor
- later valid re-completion establishes the new anchor
- no manually editable Financial Receipt Date

Future tests must cover one full Receipt, multiple partial Receipts, Final Receipt + Credit Days, received-with-cancellation, fully cancelled, final-Receipt reversal and re-completion after reversal.

REPORTS
Implement approved baseline reports/read models:
- MRP Run Summary
- Material Risk
- Order Planning
- PO/Receipt
- Lead-Time Performance
- Forecast Accuracy
- Inventory Position / Movement

Inventory Analytics v1 is limited to ledger-backed physical position/movement, inactive-Warehouse warnings and persisted MRP Risk/Coverage. Do not implement slow-moving, turnover, valuation, invented Days-on-Hand or generic excess KPIs.

EXPORT
Implement server-side Excel where approved.

Implement formal PDF only if approved by 14A and existing architecture supports it cleanly.

Every export includes provenance:
- report name
- Run/period where applicable
- As-Of
- Engine/Algorithm version where relevant
- Generated At
- filters
- explicit evaluation/data cutoff and locale/timezone where applicable

UI, export and integration must consume the same authoritative backend read contracts. Frontend KPI/business recomputation is forbidden; do not reproduce those formulas in React.

UI
Implement Analytics navigation:
- Lead-Time Performance
- Forecast Accuracy
- Inventory Position / Movement
- Reports

Follow UI-BRAND-RULES.md.
Support RTL/Jalali.

ANALYTICS READ ONLY
No operational source editing except controlled Apply Lead-Time Recommendation.

Use query-time server aggregation/read models with bounded filtering/paging in v1. Do not add materialized analytics tables, scheduled background refresh or a BI warehouse. AI is read/explain only and cannot apply recommendations or mutate operational truth.

Historical analytics must resolve append-only corrections/supersessions at the requested `GeneratedAtUtc`/data cutoff. Run reports remain immutable-snapshot-only and must never use live fallback.

GOLDEN REGRESSION
Implement automated tests from `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md`. Encode approved expected values independently; never calculate them using production services under test and never edit Golden values to pass.

Release gates explicitly include:
- GD-06 exact percentile/sample outputs
- GD-07 exact Forecast Accuracy outputs, including Actual zero semantics
- GD-09 historical snapshot/cutoff non-drift
- GD-10 original-Currency isolation and no cross-currency total

END-TO-END REGRESSION
Implement mandatory GD-08 with all major quantities asserted:
Forecast
→ BOM
→ MRP
→ Recommendation
→ Expert Decision
→ Draft PO
→ Approved
→ Ordered
→ Firm Supply
→ Actual Production / Inventory Issue
→ New MRP
→ Partial Receipt
→ Inventory
→ Remaining Firm Supply
→ New MRP
→ Final Receipt
→ Inventory
→ New MRP

Assert exact major quantities/states.

REGRESSION GUARDS
Test:
- shared inventory no double count
- received PO qty no longer firm
- cancelled qty not firm
- schedule split no duplication
- historical Run snapshot immutability
- Recommendation snapshot immutability
- PO snapshot immutability
- currency isolation
- Jalali/Gregorian boundaries
- GD-09 proves MRP Run, Recommendation and committed PO snapshot do not drift
- GD-10 proves `100 USD + 500 CNY` remains separate and never becomes `600`

BACKEND
Implement only approved:
- analytics queries/services
- read models
- controlled Sourcing apply command integration
- reports/export
- migrations if required by Credit Anchor/amendments
- APIs
- validation

Do not add broad BI infrastructure.

PERMISSIONS/AUDIT
Reuse existing platform.

TESTS / VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend build/lint
- startup/health
- golden fixture
- end-to-end regression
- report/export checks
- server-side export correctness, bounded processing, provenance and currency separation
- server-side aggregation/paging and no-N+1 checks
- records beyond page 1 / 100 are reachable
- Forecast Reopen/Reclose cutoff checks
- multi-Schedule financial anchor checks
- historical cutoff reproducibility checks

Do not claim unrun checks.

SCOPE GUARD
Do NOT implement:
- Payment execution
- banking
- AP accounting
- GL
- FX conversion
- landed cost
- inventory valuation
- slow-moving Inventory
- Inventory Turnover
- generic Excess Inventory KPI
- AI auto-optimization
- auto-update of Sourcing recommendations
- supplier quality analytics
- production capacity planning
- enterprise data warehouse

Final report:
- Changed
- Cross-Package Amendments
- Lead-Time Analytics
- Forecast Accuracy
- Financial Commitment Contract
- Reports / Export
- UI
- Golden Regression
- End-to-End Regression
- Tested
- Deviations
- Pending

Then STOP.
Do not begin any new ERP module.
```

---

# D. Prompt 14C — Analytics & Final Integration Independent Audit

```text
# MRP-14C — Analytics, Reporting & Final Integration Independent Audit

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
- docs/modules/mrp/features/MRP-11-MATERIAL-PLANNING-UI-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-12-ORDER-PLANNING-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-13-PURCHASE-ORDER-OPERATIONS-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-14-ANALYTICS-REPORTING-INTEGRATION-PACKAGE-FINAL.md
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-14A Analysis
- MRP-14B implementation report

Inspect all MRP-14 and cross-package amendment implementation files.

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
   Confirm no:
   - payment execution
   - FX
   - accounting
   - landed cost
   - valuation
   - AI auto-optimization
   - automatic Sourcing overwrite
   - broad BI/data warehouse

2. Analytics Source
   - real operational facts only
   - no manual parallel performance source
   - Configured/Original/Revised/Actual separated

3. Lead-Time Grain
   - Sourcing Option primary
   - Schedule observation
   - no PO-wide distortion

4. Sample Policy
   - inclusion date is clean Schedule Final Receipt Completion Date
   - trailing window uses `(evaluationDate.AddMonths(-N), evaluationDate]`
   - cancelled schedule excluded from recommendation sample
   - cancellations still reported
   - Schedule cancellation rate, quantity cancellation rate and quantity fulfillment are separate
   - reliability cohort uses PO OrderedDate within the selected window
   - open overdue does not contaminate completed late/on-time denominator
   - 12m
   - 24m expansion
   - min 5

5. Statistics
   - Count
   - Mean
   - Median / P50
   - P80
   - P90
   - Min/Max
   - continuous percentile method equivalent to PostgreSQL percentile_cont
   - no frontend/backend mismatch
   - GD-06 exact Mean=14.6, P50=14, P80=17 and P90=19 from approved expected values

6. Recommendation
   - P80
   - P90 remains conservative/risk reference and does not replace P80
   - no auto application
   - no silent Sourcing update from either percentile
   - insufficient sample correctly blocked
   - Apply action audited through Sourcing domain
   - P80 calculated independently per configured stage
   - Ceiling to whole Calendar Days
   - explicitly selected stage values only
   - Total P80 not stored as a fifth Sourcing duration
   - insufficient stage sample never auto-populated

7. Stage Mapping
   Verify:
   - ORDERED→SUPPLIER_READY
   - SUPPLIER_READY→SHIPPED
   - SHIPPED→ARRIVED_DESTINATION
   - ARRIVED_DESTINATION→WAREHOUSE_RECEIPT
   - diagnostic milestone treatment
   - no gap/overlap

8. On-Time
   - Original Plan primary
   - Revised performance separate
   - tolerance baseline correct

9. Forecast Accuracy
   - Closed Period only
   - baseline at/before Period Start
   - final before Close
   - Actual authoritative
   - Error/Abs Error
   - APE only when valid
   - WAPE
   - Bias
   - missing states
   - Closed Period absence of Daily Actual rows for an in-scope Product equals Actual zero
   - `ACTUAL_NOT_AVAILABLE` requires explicit legacy/import incompleteness
   - Reopen/Reclose uses the latest effective Close event that leaves the Period Closed

10. Credit Anchor Amendment
    - Supplier Payment Profile
    - PO snapshot
    - Credit%>0 validation
    - stable anchor codes
    - historical integrity
    - WAREHOUSE_RECEIPT uses operational Final Receipt Date, never First Receipt or Cancellation Date
    - partial/full-cancel/reversal/re-completion behavior
    - no duplicate manually editable Financial Receipt Date

11. Financial Commitment Contract
    - Advance
    - Before Shipment
    - Credit
    - event date fallback
    - unresolved date not guessed
    - original Currency
    - no execution semantics
    - every required Schedule resolves independently
    - fully cancelled no-obligation Schedule excluded
    - PO anchor is maximum resolved required-Schedule date
    - any unresolved required Schedule makes PO anchor unresolved
    - multi-Schedule anchors follow the same maximum/unresolved contract

12. Currency
    - no cross-currency total
    - reports grouped correctly

13. Reports
    - approved report set
    - correct source
    - historical Run snapshot isolation where applicable
    - Inventory Position/Movement is ledger-backed
    - no slow-moving/turnover/valuation/generic-excess invention

14. Export
    - provenance metadata
    - no unknown-context spreadsheet/PDF
    - same backend contract as UI
    - historical cutoff reproducibility
    - server-side export correctness, bounded processing and currency separation

15. Analytics Write Boundary
    - read-only except Apply Lead-Time Recommendation
    - no direct DB bypass
    - Apply Lead-Time Recommendation remains Sourcing-owned
    - AI read/explain only

15A. Read-Model / Performance Boundary
    - query-time server aggregation in v1
    - bounded server paging/filtering
    - records beyond page 1 / 100 reachable
    - no frontend KPI recomputation
    - no-N+1 and no frontend full-dataset analytics
    - no materialized/background refresh without measured approval

16. Golden Regression
    - canonical v1.0 inputs and expected values match exactly, including GD-06 P80=17 and P90=19
    - expected values are independent; no production calculator generates them and tests are not tautological
    - no production data dependency
    - GD-07 exact WAPE/Bias/Actual-zero expectations
    - GD-09 historical snapshot and cutoff non-drift
    - GD-10 original-Currency separation with no cross-currency total

17. End-to-End Regression
    Verify mandatory GD-08 complete path and assert all major quantities:
    Forecast→BOM→MRP→Recommendation→Decision→PO→Firm→Receipt→Inventory→New MRP
    Verify GD-09 snapshot non-drift and GD-10 currency isolation.

18. Double-Count Regression
    - shared Item stock
    - received vs firm
    - cancelled supply
    - schedule split
    - current-month Actual
    - Product production forecast semantics

19. Snapshot Regression
    - MRP Run
    - Recommendation
    - PO
    remain historically stable

20. Calendar Regression
    - Jalali/Gregorian
    - timezone
    - residual

21. Performance
    - no N+1
    - percentile server-side/authoritative
    - no unnecessary BI/cache/job infrastructure

22. Permissions / Audit

23. Tests
    Verify meaningful numeric/statistical assertions.

24. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..14
    - approved MRP-14A
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
- Statistical Correctness
- Financial Contract Correctness
- Regression Coverage
- Cross-Package Amendment Integrity
- Specification Compliance
- Scope Compliance
- Recommendation

Do not modify anything.
STOP.
```

---

# E. Final Pre-Codex Audit — MRP-14

## Result

`PASS`

## Audited Approved Decisions

تمام ۲۶ تصمیم Package 14 پوشش داده شده‌اند:

1. Analytics from actual history.
2. Configured/Original/Revised/Actual separation.
3. No automatic Sourcing overwrite.
4. Sourcing Option Lead-Time grain.
5. Delivery Schedule observation.
6. Cancelled sample exclusion with separate cancellation KPI.
7. 12m→24m sample window.
8. minimum sample count 5.
9. approved statistical set.
10. P80 recommendation.
11. controlled Apply action.
12. continuous Lead-Time Stage Mapping.
13. Original Plan primary On-Time baseline.
14. Revised Plan performance separate.
15. Closed-period Forecast Accuracy.
16. Baseline + Final Forecast definitions.
17. Error/Absolute Error/APE/WAPE/Bias.
18. Financial Commitment Contract only.
19. Credit Anchor Event.
20. Actual→Revised→Original date fallback.
21. Currency preservation.
22. baseline report set.
23. export provenance.
24. Analytics read-only boundary.
25. Golden Regression Suite.
26. mandatory Final Cross-Package Audit.

## Cross-Package Amendment Audit

### Lead-Time Stage Mapping

Approved amendment:

```text
ORDERED → SUPPLIER_READY
SUPPLIER_READY → SHIPPED
SHIPPED → ARRIVED_DESTINATION
ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
```

Diagnostic milestones remain:

```text
HANDOVER_TO_FORWARDER
CUSTOMS_CLEARED
```

This must be propagated consistently into MRP-05/09/13/14.

### Credit Anchor Event

Approved amendment:

```text
ORDERED
SHIPPED
WAREHOUSE_RECEIPT
```

Rule:

```text
Credit% > 0
→ CreditDays > 0
→ CreditAnchor required
```

This must be propagated into MRP-03/09/13/14 and any relevant commercial snapshot contract.

For `WAREHOUSE_RECEIPT`, audit that Final Receipt Date comes only from MRP-13 valid Posted/non-reversed Receipt history. First Receipt and Cancellation Date must never be used for the financial anchor. Verify partial Receipt remains unresolved, fully cancelled has no anchor, reversal removes a stale anchor, later re-completion establishes a new anchor, and no duplicate Financial Receipt Date exists.

## Open Question Resolution

### Forecast Accuracy

CLOSED.

### Lead-Time Statistical Policy

CLOSED.

### Credit Days Anchor

CLOSED.

WAREHOUSE_RECEIPT uses operational Final Receipt Date.

### Lead-Time Stage Semantics

CLOSED.

## Architecture Corrections Applied

- Performance analytics cannot silently become a second Master Data source.
- P80 is explicit planning policy rather than average-based hidden logic.
- Small samples do not generate false precision.
- Original and revised supplier performance cannot be conflated.
- Forecast accuracy distinguishes original planning quality from final revised planning.
- Actual=0 no longer produces invalid percentage math.
- Credit Days now has a defined business-event anchor.
- Financial commitment schedule is separated from payment execution.
- Cross-currency aggregation is explicitly prohibited.
- Exported reports preserve provenance.
- Golden tests verify numerical integration rather than module-level build success only.
- Final Cross-Package Audit is an explicit implementation gate.

## Remaining Future Scope

The following are intentionally outside the current MRP baseline:

- Treasury/Cash Flow module
- FX
- AP/accounting
- Inventory valuation
- Landed cost
- Quality analytics
- Capacity planning
- Production Routing
- Production Calendar
- controlled supplier over-receipt tolerance
- enterprise BI/data warehouse

None blocks MRP baseline completion.

## Prompt Audit

### MRP-14A
PASS

Analysis-only and explicitly checks cross-package amendments, statistical definitions, financial-date semantics, reporting and regression design before implementation.

### MRP-14B
PASS

Implementation is constrained to Analytics/Reporting/Integration plus explicitly approved amendments and blocks finance/accounting/AI scope leakage.

### MRP-14C
PASS

Independent read-only audit checks statistical correctness, financial commitment semantics, stage/credit amendments, exports, regression integrity and specification drift.

## Final Decision

`MRP-14 Analytics, Reporting & Final Integration` is approved for the project Prompt Baseline.

All planned MRP Packages 01 through 14 are now designed.

**Codex execution must NOT begin yet.**

The next mandatory step is:

```text
FINAL CROSS-PACKAGE AUDIT
MRP-01 → MRP-14
including every Prompt A/B/C
```

Only a final `PASS` authorizes execution beginning with `MRP-01A`.
