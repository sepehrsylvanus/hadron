# Hadron ERP — MRP Package 11 Final

**Package:** MRP-11 Mapping / Material Planning UI  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 11-MATERIAL-PLANNING-UI

## 1. Purpose

این Feature خروجی MRP Calculation Engine را به محیط عملیاتی و مدیریتی قابل‌فهم تبدیل می‌کند.

MRP-11 باید به کاربر کمک کند سریعاً بفهمد:

- چه Materialهایی در وضعیت سالم هستند
- چه Materialهایی به Safety Stock نزدیک می‌شوند
- چه Materialهایی Stockout خواهند داشت
- Firm Incoming چه اثری دارد
- چه Quantityهایی ETA ندارند یا Overdue هستند
- Demand هر Material از کجا آمده
- Firm Supply هر Material از کدام PO / Schedule آمده
- کدام Resultها به علت Missing Data ناقص هستند

این Feature نباید محاسبات MRP جدید اختراع کند.

تمام اعداد Planning باید از یک MRP Run مشخص خوانده شوند.

---

## 2. Run-Based Material Planning

### LOCKED DECISION

Material Planning UI always operates against one explicit:

```text
MRP Calculation Run
```

Default:

```text
Latest successfully completed official Run
```

The selected Run must always be visible to the user.

Example:

```text
MRP-RUN-000127
As Of:   1405/05/20
Horizon: 1406/05/20
Engine:  MRP-1.0
```

---

## 3. No Live Mixing

### LOCKED DECISION

When viewing a historical MRP Run, the UI must NOT mix its persisted results with live current values from:

- Inventory
- Purchase Supply
- Forecast
- BOM
- Sourcing

Example:

```text
Run As Of = 1405/05/20
Today's Inventory = changed later
```

The historical Run must continue showing the quantities used by that Run.

To see current state:

```text
Run MRP again
```

This rule is critical for explainability and reconciliation.

---

## 4. Snapshot Context Visibility

The UI must clearly display:

```text
Viewing MRP Run
As-Of Date
Horizon End
Engine Version
Run Status
Completion Time
```

If the user is not viewing the latest Run, that should be visually clear.

Do not silently present old results as current.

---

## 5. Material-Centric Primary View

### LOCKED DECISION

Primary Material Planning view is material-centric.

One row per planning Material:

- Part
- Component

Do not reproduce the legacy spreadsheet pattern where each Material owns repeated groups of columns across one giant sheet.

---

## 6. Product Boundary

### LOCKED DECISION

Product is not a primary row in Material Planning Summary.

Product belongs to:

- Forecast
- Demand Trace
- BOM source explanation

The Summary is designed for material availability planning.

If future Finished Goods planning is introduced, it may receive its own explicit view rather than overloading MRP-11.

---

## 7. Material Planning Summary

Required baseline fields:

- Official Item Code (`Item.BusinessNumber`)
- Item Name
- Item Type
- Category
- Supply Method
- Starting Actual On Hand
- Firm Incoming within Horizon
- Net Requirement within Horizon
- Expected Stockout Date
- Safety Stock Breach Date
- Stock Coverage
- Firm Supply Coverage
- Overdue Firm Qty
- Firm Qty — ETA Missing
- Risk Band
- Planning Completeness
- Exception count / primary exception
- Preferred Supplier/Source display where traceable and useful, without creating procurement recommendation

Exact display ordering is a UI implementation choice.

---

## 8. Starting On Hand Meaning

`Starting Actual On Hand` shown in MRP-11 must mean:

```text
Starting Inventory used by the selected MRP Run
```

not today's live Inventory unless the selected Run was created from today's current state.

Column labels/tooltips should make snapshot semantics clear.

---

## 9. Firm Incoming Meaning

`Firm Incoming` displayed in the Summary is the quantity included by the selected Run under MRP-09/10 Firm Supply rules.

It must not include:

- Draft PO
- Approved-only PO
- Cancelled supply
- Received quantity already reflected in inventory
- ETA Missing supply in date-phased projected availability
- Overdue unreceived supply in date-phased projected availability

Separate diagnostic quantities may still show ETA Missing / Overdue amounts.

---

## 10. Net Requirement Meaning

`Net Requirement` is the MRP-10 planning need.

It is NOT:

- final Recommended Order Qty
- MOQ-rounded quantity
- Purchase Order quantity

UI should avoid labeling it as "Order Qty".

Package 12 owns procurement recommendation.

---

## 11. Risk Band

### LOCKED DECISION

Material Planning Summary includes a derived:

```text
Risk Band
```

Risk Band is never manually edited.

Primary risk input:

```text
Firm Supply Coverage
```

because it includes dated Firm Incoming.

Stock Coverage remains visible as a separate KPI.

---

## 12. Risk Threshold Source

Risk classification uses the Material planning thresholds from MRP-04:

- Critical Days
- Safe Start Days
- Excess Start Days

Baseline rule:

```text
if Projected Stockout exists / immediate shortage condition
    STOCKOUT

else if Firm Supply Coverage < Critical Days
    CRITICAL

else if Critical Days <= Firm Supply Coverage < Safe Start Days
    LOW

else if Safe Start Days <= Firm Supply Coverage < Excess Start Days
    SAFE

else if Firm Supply Coverage >= Excess Start Days
    EXCESS
```

Special coverage states are handled separately.

MRP-11A must ensure exact boundary behavior is deterministic.

---

## 13. Incomplete Overrides Risk

### LOCKED DECISION

If Planning Completeness is:

```text
INCOMPLETE
```

the UI must NOT classify the Material as healthy merely because an incomplete numeric field looks favorable.

Primary displayed Risk state becomes:

```text
INCOMPLETE
```

or an equivalent approved explicit state.

Data uncertainty has precedence over a misleading healthy risk color/state.

---

## 14. Special Coverage States

MRP-10 explicit coverage states must remain visible:

```text
BEYOND_HORIZON
NO_FUTURE_DEMAND
INCOMPLETE_DATA
```

MRP-11 must not convert them into fake day counts.

Risk classification for these states should be explicit and explained, not guessed.

Examples:

```text
NO_FUTURE_DEMAND
→ No demand risk within known horizon

BEYOND_HORIZON
→ Supply covers known horizon; exact exhaustion unknown

INCOMPLETE_DATA
→ Risk cannot be reliably classified
```

---

## 15. Stockout vs Critical

`STOCKOUT` and `CRITICAL` are distinct.

`STOCKOUT` means a projected negative Firm balance occurs within the horizon.

`CRITICAL` means coverage is below the configured critical threshold but may not yet be projected negative.

Do not merge the two.

---

## 16. Safety Stock Breach

Safety Stock Breach Date should be shown separately from Stockout Date.

This lets users distinguish:

```text
Below planning safety target
```

from:

```text
Physical projected shortage
```

---

## 17. Risk Band Is Not Procurement Priority

### LOCKED DECISION

Risk Band is a planning condition classification.

It is NOT:

- Purchase Priority
- Next Action
- Procurement urgency score

Package 12 owns procurement Priority.

MRP-11 must not create final procurement action labels.

---

## 18. Material Timeline

### LOCKED DECISION

Selecting a Material opens a Timeline based on the selected MRP Run.

Baseline daily fields:

- Date
- Gross Demand
- Firm Incoming
- Projected On Hand
- Safety Stock
- Net Requirement
- Stockout / Safety breach state
- Supply Event summary
- exception indicator where relevant

The Timeline is read-only.

---

## 19. Daily Source of Truth

Timeline calculation source is always:

```text
MRP-10 Daily Result
```

Weekly/Monthly modes must aggregate Daily Result.

Do not re-run a different formula in the UI.

---

## 20. Daily / Weekly / Monthly View

### LOCKED DECISION

User may choose:

- Daily
- Weekly
- Monthly

This changes presentation grain only.

The core calculation remains daily.

---

## 21. Weekly / Monthly Aggregation Rules

### LOCKED DECISION

For an aggregated display period:

### Flow quantities

```text
Gross Demand
Firm Incoming
Net Requirement
```

use:

```text
SUM(daily values)
```

### Stock quantity

```text
Projected On Hand
```

uses:

```text
Closing balance of the final day in the period
```

Do NOT sum Projected On Hand.

---

## 22. Coverage Aggregation

Coverage must NOT be calculated as:

```text
AVERAGE(daily coverage)
```

or by averaging daily demand.

Coverage in Weekly/Monthly display should either:

- show the MRP-10 summary/coverage state
- show closing-period derived coverage where explicitly supported by MRP-10 read model

MRP-11 must not invent a different shortcut formula.

---

## 23. Timeline Event Markers

Useful timeline events may include:

- Firm PO Schedule Receipt ETA
- Stockout Date
- Safety Stock Breach
- ETA Missing warning
- Overdue Supply warning
- BOM/effectivity exception where traceable

These markers are derived from selected Run data.

Do not query live source documents to change historical Run result.

---

## 24. Timeline Chart

### LOCKED DECISION

A chart is secondary visualization.

Recommended chart series:

- Projected On Hand
- Safety Stock
- Firm Incoming markers/bars
- optionally Gross Demand

The Grid/Timeline remains the primary audit-friendly view.

Chart must not hide exact values.

---

## 25. Demand Trace

### LOCKED DECISION

Material Planning must support demand drill-down.

For selected Material, user should be able to understand:

```text
Where did this demand come from?
```

Conceptual trace:

```text
Product Forecast
→ Product BOM
→ Parent Part
→ lower Component
→ selected Material
```

---

## 26. Demand Trace Aggregation

For a shared Material used by multiple Products/Parents, trace should show contributions.

Example:

```text
IC-ABC total demand = 400,000

Product C1011 → 180,000
Product C1015 → 120,000
Product C1020 → 100,000
```

Exact trace depth/aggregation uses the data persisted by MRP-10.

MRP-11 must not independently reconstruct a different historical demand graph from current BOM data.

---

## 27. Demand Trace Snapshot Integrity

Demand Trace must reference the BOM/Forecast context used by the selected MRP Run.

If current BOM differs today, historical trace remains tied to Run inputs.

---

## 28. Supply Trace

### LOCKED DECISION

Firm Incoming must support drill-down to its Run-specific supply sources.

At minimum where available:

- PO Number
- Supplier
- PO Line
- Delivery Schedule
- Schedule Remaining Qty used
- ETA used
- relevant PO status used by Run

---

## 29. Supply Trace Historical Integrity

Supply Trace for a historical Run must show the PO/Schedule values used by that Run.

If the PO's ETA was revised later:

```text
Historical Run Supply Trace
≠ Current live PO ETA
```

The user may optionally navigate to current PO separately, but the snapshot value must remain distinguishable.

---

## 30. Source Navigation

Where application routing exists, drill-down may provide navigation to source modules:

```text
Demand Issue
→ Forecast / BOM

Supply Issue
→ Purchase Supply

Source Readiness Issue
→ Sourcing
```

Navigation does not mean direct editing inside Material Planning.

---

## 31. Exception Center

### LOCKED DECISION

MRP-11 provides a dedicated Exception Center.

It displays MRP-10 Run Exceptions in a management-friendly way.

Example categories:

- Missing Forecast
- Missing Effective BOM
- Ambiguous BOM
- Missing Sourcing readiness
- Firm Supply ETA Missing
- Overdue Firm Supply
- HYBRID Route Decision Required
- Inactive Warehouse Stock Warning
- other engine-defined exceptions

MRP-11 must not invent a second exception source.

---

## 32. Exception Center Fields

Recommended baseline fields:

- Severity
- Item
- Item Type
- Exception Code
- Human-readable message
- Effective/affected date where relevant
- Quantity impact where available
- Source module
- Planning impact
- Resolution link/action navigation

Exact columns may be refined.

---

## 33. Exception Severity

MRP-11 consumes the severity semantics defined/approved by MRP-10.

UI may group/filter:

- Error
- Warning
- Info

It must not independently downgrade an engine ERROR because the UI considers it less important.

---

## 34. Exception Resolution

### LOCKED DECISION

MRP-11 does not directly fix source data.

Flow:

```text
Exception
↓
Navigate to Source
↓
Correct source data
↓
Run MRP again
```

Existing selected Run remains immutable.

---

## 35. Read-Only Results

### LOCKED DECISION

The following are read-only:

- Projected On Hand
- Gross Demand
- Firm Incoming
- Net Requirement
- Coverage
- Stockout Date
- Safety Breach
- Risk Band
- Planning Completeness
- Run Exception

No spreadsheet-style cell editing.

---

## 36. No Result Patching

The user must not be able to manually patch an MRP result such as:

```text
Change Projected Stock from -5,000 to 0
```

or:

```text
Change Coverage from 12 days to 50
```

All corrections happen in source-of-truth modules followed by a new Run.

---

## 37. Summary Filters

### LOCKED DECISION

Baseline Material Planning filters:

- Item
- Item Type
- Category
- Supply Method
- Risk Band
- Planning Completeness
- Expected Stockout date/window
- Supplier / Preferred Source where meaningful
- Exception presence
- optional Business Unit when derivable/useful from product-demand context without ambiguity

MRP-11A should avoid filters that require expensive ambiguous joins.

---

## 38. Stockout Window Filters

Useful filters may include:

```text
Already Stocked Out
Next 7 Days
Next 30 Days
Next 60 Days
Within Horizon
No Stockout in Horizon
```

These are UI query presets over Run results.

Do not store them as business fields.

---

## 39. Sorting

Default useful sort recommendation:

1. Incomplete/Error
2. Stockout
3. Critical
4. Low
5. Safe
6. Excess
7. No Future Demand / Beyond Horizon

Within a band, earliest Stockout/Safety Breach first.

Exact default sorting is a UX choice but must prioritize risk.

---

## 40. Summary Performance

Summary should be server-queryable with:

- filtering
- sorting
- paging / virtualization as needed

Do not load every daily timeline for every Material when only Summary is shown.

AG Grid Enterprise may be used according to project architecture.

---

## 41. Timeline Loading

Timeline detail should load on demand for selected Material.

Avoid preloading:

```text
all Items × all days
```

into the browser.

---

## 42. Demand Trace Loading

Demand Trace should load on demand.

Do not load full recursive trace for every Summary row.

---

## 43. Supply Trace Loading

Supply Trace should load on demand.

Avoid N+1 PO/Schedule fetches for Summary.

Use Run-specific trace/read models.

---

## 44. Run Selector

Material Planning should allow selecting another completed Run.

Useful fields:

- Run Number
- As-Of
- Horizon
- Completed At
- Engine Version
- Completeness/Exception count

Failed Runs should not be selectable as normal valid planning result.

---

## 45. Latest Run Indicator

If selected Run is not latest completed Run:

```text
Historical Run
```

should be visibly indicated.

Do not silently auto-switch while the user is inspecting an old Run.

---

## 46. Stale Run Warning

MRP-11A may propose a derived informational warning when the latest Run is old compared with the current date.

Example:

```text
Last MRP Run: 3 days ago
```

This is informational only.

Do not define an arbitrary business SLA without approval.

---

## 47. Navigation Structure

### LOCKED DECISION

Recommended MRP navigation:

```text
MRP
├── Runs
├── Material Planning
│   ├── Summary
│   └── Material Timeline
├── Exceptions
├── Order Planning        ← MRP-12
└── Purchase Supply       ← existing/future
```

Exact application route names may follow repository conventions.

---

## 48. No Procurement Action in MRP-11

### LOCKED DECISION

MRP-11 does NOT implement:

- Recommended Order Qty
- MOQ rounding
- Select Supplier
- Purchase Priority
- Next Action
- Expert Decision
- Expert Final Qty
- Recommendation Reason
- Create Draft PO

These belong to MRP-12/13.

---

## 49. No Manual PO Status in Mapping

### LOCKED DECISION

Mapping does not store its own `Order Status`.

PO Status shown in Supply Trace comes from the Purchase Supply snapshot used by the Run.

There is no duplicate Mapping status field.

---

## 50. No Duplicate Inventory Field

MRP-11 does not own or edit Inventory.

It only displays:

```text
Starting Actual On Hand used by selected Run
Projected On Hand calculated by MRP-10
```

Do not introduce `Current Stock` as a second live inventory source.

---

## 51. Preferred Supplier Display Boundary

Summary may display Preferred Supplier / Source as informational Master Data context if this can be done without implying the engine selected that Supplier.

Label must avoid language like:

```text
Recommended Supplier
```

Package 12 owns sourcing recommendation.

---

## 52. Supply Method Display

Summary should show:

```text
BUY
MAKE
OUTSOURCE
HYBRID
```

using user-friendly localized labels where available.

For HYBRID, selected Run route should be visible in detail/trace where relevant.

---

## 53. HYBRID Route Visibility

If the Run used a HYBRID route decision, Material detail should expose:

```text
Route used by this Run
```

If Run is incomplete due to missing HYBRID decision, Exception Center should clearly show it.

---

## 54. Planning Completeness

Summary must expose the MRP-10 derived state:

```text
COMPLETE
COMPLETE_WITH_WARNINGS
INCOMPLETE
```

or the exact labels implemented by MRP-10A/B.

This is read-only.

---

## 55. Completeness Filtering

Users should be able to filter:

```text
Incomplete only
Warnings only
Complete only
```

This helps master-data cleanup.

---

## 56. Risk Color / Brand Boundary

Risk states may use semantic UI colors, but must follow:

`docs/brand/UI-BRAND-RULES.md`

Do not recolor the official Hadron logo.

Do not rely on color alone.

Every risk state must have readable text/icon/label.

---

## 57. RTL / Persian UX

MRP-11 must support Persian-first RTL use.

Technical codes such as:

```text
PO-000125
MRP-RUN-000127
PRT-0001
```

must remain readable in mixed-direction layouts.

AG Grid column alignment should be deliberately configured.

---

## 58. Jalali Dates

Business dates display in Jalali according to project conventions.

Canonical values remain Gregorian.

Timeline sorting/filtering must use canonical date values, not formatted Persian strings.

---

## 59. Numeric Formatting

Quantities should display with appropriate precision and separators.

Display rounding must not change underlying values.

Tooltip/detail may expose higher precision when needed.

No MOQ or purchasing rounding in MRP-11.

---

## 60. Export

MRP-11A may propose export of:

- Material Summary
- selected Timeline
- Exceptions

to Excel/CSV if existing project infrastructure supports it.

Export must preserve Run identity in the file/header.

Do not build a reporting subsystem solely for this Package.

---

## 61. Print / PDF

Formal PDF reporting is not required in MRP-11.

Future reporting belongs to Package 14.

If a simple browser print exists naturally, that is not a core acceptance requirement.

---

## 62. Permissions

At minimum architecture should distinguish:

- View MRP Runs
- View Material Planning
- View Exceptions

MRP-11 itself does not require edit permissions because results are read-only.

Source-module permissions control fixes.

---

## 63. Auditability

Viewing does not require heavy audit logging.

Important data provenance is already in:

- MRP Run
- Calculation Results
- Input Trace
- Exceptions

If export activity is audited by existing platform conventions, reuse them.

Do not create a parallel audit system.

---

## 64. API / Application Use Cases

Required conceptual use cases:

### Runs
- list completed Runs
- get Run header/context

### Material Summary
- query Summary by selected Run
- filter/sort/page

### Timeline
- get selected Material daily results
- aggregate Daily → Weekly/Monthly for presentation

### Demand Trace
- get demand contribution trace for selected Material

### Supply Trace
- get Firm Supply source trace used by selected Run

### Exceptions
- query Run Exceptions
- filter by severity/item/code

No write endpoint for MRP results.

---

## 65. Aggregation Location

MRP-11A should determine whether Weekly/Monthly aggregation belongs:

- server-side read model
or
- client-side for the already-loaded selected Material timeline

Baseline preference:

- core Summary server-side
- selected Material timeline aggregation may be client-side if data volume is small and exact rules remain centralized/tested

Do not duplicate formulas inconsistently.

---

## 66. Risk Calculation Location

Risk Band is derived from MRP result + Material threshold policy.

MRP-11A must choose one authoritative implementation location.

Preferred architecture:

```text
Backend/Application read model
```

so all clients/export use identical classification.

Do not calculate different Risk rules separately in React and backend.

---

## 67. Risk Threshold Validation

If threshold policy itself is invalid/missing contrary to MRP-04 rules:

- do not guess a Risk Band
- surface incomplete/configuration issue

MRP-11 should not invent default thresholds.

---

## 68. No Future Demand Risk

`NO_FUTURE_DEMAND` is not automatically:

- SAFE
- EXCESS
- CRITICAL

It should remain its own explanatory state.

---

## 69. Beyond Horizon Risk

`BEYOND_HORIZON` means supply covers all known demand through Run Horizon.

It should not be represented as exact infinite safety.

A UI label such as:

```text
Covered Beyond Horizon
```

is preferred over an arbitrary day count.

---

## 70. ETA Missing Visibility

If:

```text
Firm Qty — ETA Missing > 0
```

Summary must make this visible even if other supply makes overall risk appear acceptable.

This is an operational data-quality warning.

---

## 71. Overdue Firm Supply Visibility

If:

```text
Overdue Firm Qty > 0
```

Summary must make it visible.

It may contribute Warning status even when current projected coverage is otherwise acceptable.

Do not silently hide stale incoming supply.

---

## 72. Exception vs Risk Interaction

Suggested precedence:

```text
INCOMPLETE calculation
    overrides healthy Risk presentation

STOCKOUT
    highest valid numeric risk

CRITICAL
LOW
SAFE
EXCESS

NO_FUTURE_DEMAND / BEYOND_HORIZON
    explicit special states
```

Warnings such as Overdue Firm Supply may coexist with a Risk Band.

Exact visual combination is finalized in MRP-11A.

---

## 73. Source Data Freshness

MRP-11 does not display live source freshness inside the historical Result unless clearly separated.

Optional informational links may show:

```text
Current source has changed since this Run
```

only if a reliable diff mechanism exists.

Do not build source-diffing in v1 unless already available.

---

## 74. Comparison Between Runs

Full Run-to-Run comparison is not a locked MRP-11 requirement.

MRP-11A may propose a lightweight future-compatible path, but implementation should not expand scope unless explicitly approved.

Package 14 may later support analytics/comparison.

---

## 75. Empty States

UI must have explicit empty states:

- no completed MRP Run
- selected Run has no material results
- no exceptions
- selected Material has no demand
- no Firm Supply trace

Do not show blank grids without explanation.

---

## 76. Error Handling

If a Run result is unavailable/corrupt:

- do not fall back to live recomputation in UI
- surface an error
- preserve Run identity
- direct user to rerun or audit source as appropriate

---

## 77. Performance

Material Summary should not require loading:

- full daily results
- full demand trace
- full supply trace

for all Items.

Use purpose-specific read models/endpoints.

---

## 78. Caching Boundary

Do not add Redis or complex caching solely for MRP-11.

Run results are immutable and therefore cache-friendly later, but first implementation should remain simple unless actual performance demands otherwise.

---

## 79. AG Grid Usage

Project architecture allows AG Grid Enterprise.

Use it where appropriate for:

- Summary
- Exceptions
- Timeline grid

Do not force AG Grid into small simple forms where unnecessary.

---

## 80. Chart Library

Use an already approved/existing chart library if present.

Do not introduce a large chart dependency solely for one optional chart without analysis.

Chart is secondary.

---

## 81. Accessibility

Risk state must not rely solely on color.

Support:

- text labels
- icons where useful
- keyboard/grid accessibility according to chosen UI components

---

## 82. Responsive Scope

Primary target is desktop ERP use.

UI should remain usable on common laptop widths.

Do not over-optimize MRP planning grids for phone-sized mobile layouts in v1.

---

## 83. Data Ownership

MRP-11 owns:

```text
presentation/read models only
```

It does not own:

- Forecast
- BOM
- Inventory
- Sourcing
- PO
- MRP Calculation formulas

---

## 84. Read Model Rebuildability

If MRP-11 introduces dedicated read-model tables for performance, they must be rebuildable from immutable MRP Run results.

They must not become a second business source of truth.

MRP-11A must justify any persisted read-model projection.

---

## 85. No Business Rule in Display Text

Risk, exception and coverage logic must use stable machine values/codes.

Localized Persian/English labels are display concerns.

Do not parse text such as:

```text
"بحرانی"
```

to drive behavior.

---

## 86. Tests

MRP-11 must have meaningful tests for:

- Run-specific result isolation
- historical Run not mixed with live source data
- Summary field mapping
- Risk Band boundaries
- Incomplete overrides Risk
- Stockout vs Critical distinction
- special coverage states
- timeline daily values
- weekly/monthly aggregation
- closing Projected On Hand
- no summing projected balance
- Demand Trace from Run data
- Supply Trace from Run data
- ETA Missing / Overdue visibility
- read-only endpoints
- filters/sorting
- no procurement fields/actions
- no duplicate Mapping Order Status
- no source-data mutation

---

## 87. Explicit Non-Scope

MRP-11 must NOT implement:

- MRP calculation formulas
- live recalculation in browser
- Supplier recommendation
- Recommended Order Qty
- MOQ rounding
- Priority
- Next Action
- Expert Decision
- Expert Final Qty
- Recommendation approval
- Create Draft PO
- Receipt
- Inventory edit
- Forecast edit
- BOM edit
- PO status edit
- source-data fixing inside Mapping
- Cash Flow
- Lead-Time Analytics
- Run comparison analytics
- AI recommendation

---

## 88. Acceptance Criteria

MRP-11 is acceptable when:

1. Material Planning always uses a selected MRP Run.
2. Default is latest valid completed Run.
3. Selected Run identity is visible.
4. Historical Run results are not mixed with live source values.
5. Summary is material-centric.
6. Primary rows are Part/Component.
7. Starting On Hand means Run snapshot quantity.
8. Firm Incoming means Run-used Firm Supply.
9. Net Requirement is clearly not final Order Qty.
10. Stock Coverage and Firm Supply Coverage are both visible.
11. Risk Band is derived.
12. Firm Supply Coverage is primary risk metric.
13. MRP-04 thresholds drive Risk classification.
14. Incomplete calculation overrides healthy Risk display.
15. STOCKOUT and CRITICAL are distinct.
16. Special coverage states remain explicit.
17. Safety Stock Breach and Stockout Date are separate.
18. Material Timeline exists.
19. Timeline source is daily MRP-10 result.
20. Weekly/Monthly are aggregation only.
21. Flow quantities aggregate by Sum.
22. Projected On Hand aggregates by closing balance.
23. Coverage is not averaged.
24. Demand Trace shows Run-specific demand sources.
25. Supply Trace shows Run-specific PO/Schedule sources.
26. Exception Center exists.
27. Results are read-only.
28. Source fixes happen in source modules followed by new Run.
29. Filters support risk/completeness/item dimensions.
30. No procurement recommendation fields/actions are introduced.
31. No Mapping-specific PO status is stored.
32. Charts are secondary to grid/timeline.
33. Server-side/API read boundaries are safe.
34. Historical Run remains explainable after source changes.
35. Tests validate snapshot isolation and aggregation correctness.

---

## 89. Locked Decisions

The following are locked for MRP-11:

1. Material Planning UI is Run-based.
2. Default Run = latest completed official Run.
3. No live mixing with historical Run result.
4. Primary view is material-centric.
5. Summary primary Items = Part/Component.
6. Product is accessed through demand trace, not primary Summary rows.
7. Summary shows Starting On Hand, Firm Incoming, Net Requirement, Stockout, Safety Breach, Coverage, Overdue, ETA Missing, Risk, Completeness.
8. Risk Band is Derived.
9. Firm Supply Coverage is primary Risk metric.
10. Stock Coverage remains separate.
11. Risk thresholds use MRP-04 Critical/Safe/Excess Days.
12. Incomplete state overrides misleading healthy Risk.
13. Material Timeline uses daily Run result.
14. Daily/Weekly/Monthly views are supported.
15. Weekly/Monthly only aggregate Daily calculation.
16. Demand/Incoming/Net Requirement aggregate by Sum.
17. Projected On Hand uses Period closing balance.
18. Coverage is never simple average.
19. Demand Trace is required.
20. Supply Trace is required.
21. Exception Center is required.
22. MRP Result is read-only.
23. Mapping does not fix Source Data.
24. Mapping stores no independent PO Status.
25. Procurement recommendation fields/actions belong to Package 12.
26. Chart is secondary.
27. Navigation separates Runs / Material Planning / Exceptions / Order Planning / Purchase Supply.

---

## 90. Open Question Resolution

No previously open engine-level calculation question is newly reopened by MRP-11.

The Package intentionally does NOT resolve:

- Procurement Priority
- Decision Horizon
- Recommended Order Qty
- Supplier recommendation

These remain for Package 12.

---

# B. Prompt 11A — Material Planning UI Analysis

```text
# MRP-11A — Material Planning / Mapping UI Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume previous Packages were implemented merely because they were planned.

TASK:
Analyze MRP-11 Material Planning / Mapping UI.

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

1. Understand MRP-11 as read-only presentation of one selected MRP Run.
2. Inspect actual MRP-10 Run/result/read-model implementation.
3. Verify historical Run results can be displayed without live source mixing.
4. Propose exact Run selector/default behavior.
5. Propose Material Summary read model.
6. Confirm primary rows = Part/Component.
7. Map summary fields:
   - Starting Actual On Hand used by Run
   - Firm Incoming
   - Net Requirement
   - Stockout
   - Safety Stock Breach
   - Stock Coverage
   - Firm Supply Coverage
   - Overdue Firm Qty
   - ETA Missing Firm Qty
   - Planning Completeness
   - Exception state
8. Design Risk Band as one authoritative backend/application-derived rule.
9. Use Firm Supply Coverage as primary numeric risk metric.
10. Define exact threshold boundaries using:
    - Critical Days
    - Safe Start Days
    - Excess Start Days
11. Define precedence among:
    - INCOMPLETE
    - STOCKOUT
    - CRITICAL
    - LOW
    - SAFE
    - EXCESS
    - NO_FUTURE_DEMAND
    - BEYOND_HORIZON
12. Confirm Risk Band != Procurement Priority.
13. Design Material Timeline read model.
14. Verify Daily result is authoritative.
15. Design Weekly/Monthly aggregation:
    - flows = SUM
    - projected stock = closing balance
    - no average coverage shortcut
16. Decide server/client aggregation location without duplicating formulas.
17. Design Demand Trace from MRP-10 Run trace.
18. Ensure Demand Trace does not rebuild history from current BOM/Forecast.
19. Design Supply Trace from Run-specific PO/Schedule trace.
20. Ensure later PO ETA changes do not rewrite historical displayed trace.
21. Design Exception Center:
    - filters
    - severity
    - source navigation
    - impact
22. Confirm Mapping is read-only.
23. Confirm source corrections happen in source modules followed by new Run.
24. Define Summary filters/sorting.
25. Define stockout-window presets if useful.
26. Define incomplete/warning visibility.
27. Define ETA Missing / Overdue warning presentation.
28. Define Run header/historical/stale indicators.
29. Define empty/error states.
30. Review navigation structure.
31. Review AG Grid usage.
32. Review optional chart approach using existing dependencies if possible.
33. RTL/Jalali/mixed-code UX.
34. Permissions.
35. API/read endpoints.
36. Performance:
    - server-side summary
    - on-demand timeline
    - on-demand trace
    - avoid N+1
37. Decide whether any persisted read model is needed.
    If yes, it must remain rebuildable from immutable Run result.
38. Testing plan.
39. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..11 and actual Repository.
40. Smallest implementation sequence.
41. Explicit non-scope.

Testing proposal must cover at minimum:
- latest Run default
- selecting historical Run
- no live source mixing
- Summary mapping
- risk threshold boundaries
- incomplete overrides healthy risk
- stockout distinct from critical
- No Future Demand
- Beyond Horizon
- ETA Missing / Overdue warning
- Daily timeline
- weekly/monthly flow sums
- projected closing balance
- no projected-balance summing
- no coverage averaging
- demand trace from Run snapshot
- supply trace from Run snapshot
- later source changes do not alter historical trace
- filters/sorting
- read-only endpoints
- no procurement recommendation fields/actions
- no Mapping-specific PO status

Output:
1. Understanding
2. Repository Findings
3. Run Context Model
4. Material Summary Model
5. Risk Band Model
6. Timeline / Aggregation
7. Demand Trace
8. Supply Trace
9. Exception Center
10. Navigation / UX
11. API / Read Models
12. Permissions
13. Performance
14. Tests
15. Risks / Contradictions
16. Implementation Plan
17. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 11B — Material Planning UI Implementation

```text
# MRP-11B — Implement Material Planning / Mapping UI

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-11A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-11 Material Planning / Mapping UI.

If MRP-11A contains unresolved material contradictions, STOP.

RUN CONTEXT
Implement:
- selected MRP Run
- default latest completed official Run
- visible Run Number / As-Of / Horizon / Engine Version / status
- historical Run indicator

Do not auto-mix live source values into selected Run.

MATERIAL SUMMARY
Implement one row per approved Material Item.

Display approved fields:
- Starting Actual On Hand used by Run
- Firm Incoming
- Net Requirement
- Expected Stockout Date
- Safety Stock Breach Date
- Stock Coverage
- Firm Supply Coverage
- Overdue Firm Qty
- ETA Missing Firm Qty
- Planning Completeness
- Risk Band
- exception state/count
- approved Item/Supply context fields

Do not label Net Requirement as Order Qty.

RISK
Implement exactly the approved backend/application rule from MRP-11A.

Primary metric:
- Firm Supply Coverage

Use MRP-04 thresholds:
- Critical
- Safe Start
- Excess Start

Respect:
- INCOMPLETE precedence
- STOCKOUT vs CRITICAL distinction
- special coverage states

Do not implement Procurement Priority.

TIMELINE
Implement selected Material timeline from MRP-10 Daily results.

Support:
- Daily
- Weekly
- Monthly

Aggregation:
- Gross Demand = SUM
- Firm Incoming = SUM
- Net Requirement = SUM
- Projected On Hand = closing value of period
- no coverage averaging

Do not recalculate MRP formulas in React.

DEMAND TRACE
Implement on-demand Run-specific Demand Trace.

Do not reconstruct historical trace from current BOM/Forecast.

SUPPLY TRACE
Implement on-demand Run-specific PO/Schedule trace.

Display snapshot ETA/status/qty used by selected Run.

If navigation to current PO exists, clearly distinguish current source from historical Run snapshot.

EXCEPTIONS
Implement Exception Center using MRP-10 Run Exceptions.

Support approved:
- severity
- Item
- exception code/message
- date
- impact
- source navigation
- filters

Do not edit source data inside Exception Center.

FILTERS
Implement approved filters:
- Item
- Item Type
- Category
- Supply Method
- Risk Band
- Planning Completeness
- stockout window
- exception presence
- approved Supplier/Source filter where technically sound

READ ONLY
No editing of:
- MRP Result
- Projected On Hand
- Coverage
- Net Requirement
- Risk
- Run Exception
- PO Status

NAVIGATION
Use approved MRP structure consistent with actual app conventions.

UI
Follow:
- UI-BRAND-RULES.md
- RTL
- Jalali display
- mixed LTR technical codes
- AG Grid Enterprise where appropriate

Risk must not rely on color alone.

CHART
Implement only if approved in MRP-11A and an existing suitable chart solution exists or the dependency is justified.

Chart is secondary.

BACKEND
Implement only approved:
- read models
- query services
- API endpoints
- risk derivation
- aggregation helpers if server-side
- source-navigation references

Do not modify MRP calculation formulas.

PERMISSIONS
Reuse existing platform:
- View MRP Runs
- View Material Planning
- View Exceptions

Do not build parallel Identity.

TESTS
At minimum:
- latest Run default
- historical Run selection
- snapshot isolation
- no live Inventory/PO mixing
- Summary fields
- Risk boundaries
- Incomplete precedence
- Stockout/Critical distinction
- special coverage states
- ETA Missing/Overdue warnings
- daily timeline
- weekly/monthly aggregation
- projected closing balance
- no projected sum
- no coverage average
- Demand Trace snapshot
- Supply Trace snapshot
- source changes do not alter historical Run display
- filters/sorting
- read-only behavior
- no procurement action fields
- no independent Mapping PO Status

VERIFICATION
Run applicable:
- backend restore/build/tests
- frontend install if required
- frontend build/lint
- startup/health
- representative MRP Run UI/read-model checks

Do not claim checks not run.

SCOPE GUARD
Do NOT implement:
- MRP calculation changes
- Supplier recommendation
- MOQ
- Recommended Order Qty
- Priority
- Next Action
- Expert Decision
- Draft PO
- Receipt
- source-data editing
- Cash Flow
- Lead-Time Analytics
- AI recommendation

Final report:
- Changed
- Run Context
- Material Summary
- Risk Band
- Timeline
- Demand Trace
- Supply Trace
- Exception Center
- API / Read Models
- UI
- Performance
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-12.
```

---

# D. Prompt 11C — Material Planning UI Independent Audit

```text
# MRP-11C — Material Planning / Mapping UI Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-11A Analysis
- MRP-11B implementation report

Inspect all MRP-11 implementation files.

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
   - MRP formula changes
   - Supplier recommendation
   - MOQ
   - Priority
   - Recommended Order Qty
   - Expert Decision
   - PO creation
   - Receipt
   - Cash Flow
   - Lead-Time Analytics

2. Run Context
   - selected Run explicit
   - default latest completed Run
   - Run Number / As-Of / Horizon / Engine visible
   - historical Run identifiable

3. No Live Mixing
   - no live Inventory injected into historical result
   - no current PO ETA replacing Run snapshot
   - no current Forecast/BOM recomputation
   - no hidden live-data joins changing numerical Result

4. Material Summary
   - Part/Component rows
   - approved fields
   - snapshot semantics
   - Net Requirement not mislabeled as Order Qty

5. Risk Band
   - derived only
   - primary metric = Firm Supply Coverage
   - thresholds from MRP-04
   - exact boundaries
   - no client/backend drift
   - not Procurement Priority

6. Risk Precedence
   - INCOMPLETE overrides healthy state
   - STOCKOUT distinct from CRITICAL
   - NO_FUTURE_DEMAND explicit
   - BEYOND_HORIZON explicit
   - INCOMPLETE_DATA explicit

7. Timeline
   - sourced from Daily Run results
   - Daily/Weekly/Monthly
   - flow SUM
   - projected closing balance
   - no projected summing
   - no coverage averaging
   - no recalculation drift

8. Demand Trace
   - Run-specific
   - historical Forecast/BOM trace
   - shared Material contributions
   - no current BOM reconstruction

9. Supply Trace
   - Run-specific PO/Schedule
   - snapshot ETA/qty/status
   - later PO changes do not rewrite historical trace

10. Exception Center
    - uses MRP-10 exceptions
    - severity preserved
    - source navigation
    - no direct source editing
    - ETA Missing / Overdue visible

11. Read-Only Integrity
    - no MRP result editing
    - no Mapping stock edit
    - no Mapping PO Status
    - no result patching

12. Filters / Sorting
    - approved filter set
    - risk-first practical sorting
    - no expensive/ambiguous joins causing incorrect data

13. UI / Brand
    - UI-BRAND-RULES
    - RTL
    - Jalali
    - mixed technical codes
    - semantic risk not color-only
    - AG Grid usage appropriate

14. API
    - read-only query boundaries
    - safe Run-specific keys
    - no mutation endpoint
    - no live recomputation endpoint hidden as read

15. Performance
    - Summary does not preload timelines/traces
    - timeline on demand
    - demand trace on demand
    - supply trace on demand
    - no N+1
    - no unnecessary cache infrastructure

16. Historical Integrity
    - selected Run remains explainable after source changes

17. Permissions

18. Tests
    Verify meaningful assertions especially:
    - historical snapshot isolation
    - risk boundaries
    - incomplete precedence
    - aggregation correctness
    - trace historical integrity

19. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..11
    - approved MRP-11A
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
- Snapshot Integrity
- UI / Aggregation Correctness
- Specification Compliance
- Scope Compliance
- Recommendation

Do not modify anything.
STOP.
```

---

# E. Final Pre-Codex Audit — MRP-11

## Result

`PASS`

## Audited Approved Decisions

تمام ۲۰ تصمیم تأییدشده Package 11 پوشش داده شده‌اند:

1. Run-based UI.
2. Default latest completed Run.
3. No historical/live data mixing.
4. Material-centric Summary.
5. Product excluded from primary Summary rows.
6. Approved Summary KPIs included.
7. Derived Risk Band.
8. Firm Supply Coverage as primary Risk metric.
9. MRP-04 thresholds used for Risk.
10. Incomplete calculation overrides healthy Risk.
11. Material Timeline.
12. Daily/Weekly/Monthly presentation.
13. Correct aggregation semantics.
14. Demand Trace.
15. Supply Trace.
16. Dedicated Exception Center.
17. Read-only results.
18. Approved filters.
19. No procurement action fields.
20. No independent Mapping PO Status.

## Cross-Package Consistency

- MRP-10 remains sole owner of Calculation.
- MRP-11 does not recompute or mutate MRP results.
- MRP-04 provides threshold policy for derived Risk.
- MRP-09 supplies PO/Schedule trace only through the selected Run context.
- MRP-08 live Inventory does not overwrite historical Run Starting Inventory.
- MRP-07 current Forecast does not overwrite historical Run Forecast trace.
- MRP-06 current BOM does not overwrite historical Run BOM trace.
- Package 12 remains owner of procurement Priority and Recommendation.
- Package 13 remains owner of PO creation/receipt operations.

No ownership conflict was found.

## Architecture Corrections Applied

- Mapping is explicitly tied to one immutable Run.
- Historical Run values cannot silently drift with live Inventory/PO changes.
- Summary is normalized around Material rows rather than legacy repeated columns.
- Risk Band is separated from Procurement Priority.
- Firm Supply Coverage is used as the main risk metric while Stock Coverage remains visible.
- `INCOMPLETE` is prevented from appearing as a healthy numeric risk state.
- Flow vs balance aggregation rules are explicitly separated.
- Demand and Supply traces use persisted Run history rather than current Master Data.
- Mapping is strictly read-only.
- Exceptions direct users to source modules instead of allowing in-place patching.
- PO Status is not duplicated into Mapping.
- Optional charts remain secondary.

## Remaining Intentional Open Questions

MRP-11 intentionally leaves these to Package 12:

- Decision Horizon
- Procurement Priority
- Next Action
- Recommended Order Qty
- Supplier/Source recommendation
- Expert Decision / Final Qty

No unresolved MRP-11 issue blocks the baseline.

## Prompt Audit

### MRP-11A
PASS

Analysis-only, Repository-aware, and explicitly validates snapshot isolation, Risk derivation, aggregation rules, traces, exceptions and performance.

### MRP-11B
PASS

Implementation is limited to Run-based read models and UI and explicitly blocks procurement recommendation/source-data mutation.

### MRP-11C
PASS

Independent read-only audit checks historical integrity, no-live-mixing, Risk correctness, timeline aggregation, traces, read-only boundaries and specification drift.

## Final Decision

`MRP-11 Material Planning / Mapping UI` is approved for the project Prompt Baseline.

It should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives the final cross-package audit.
