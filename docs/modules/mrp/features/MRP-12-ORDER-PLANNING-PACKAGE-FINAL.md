# Hadron ERP — MRP Package 12 Final

**Package:** MRP-12 Order Planning Recommendation + Expert UI  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 12-ORDER-PLANNING

## 1. Purpose

این Feature خروجی Material Requirement از MRP-10 را به یک Recommendation قابل بررسی برای Procurement تبدیل می‌کند.

MRP-12 باید بتواند برای هر Item دارای External Requirement:

- Need By Date
- Source candidates
- Selected Sourcing Option
- Order By Date
- Earliest Feasible Receipt
- Decision Horizon
- Base Recommended Qty
- MOQ-adjusted Recommended Qty
- Priority
- Next Action
- Recommendation completeness/state
- Machine Recommendation snapshot
- Expert Decision history
- Final Expert Qty / Source / Receipt date

را تولید و مدیریت کند.

MRP-12 هنوز Purchase Order ایجاد نمی‌کند.

---

## 2. Recommendation Grain

### LOCKED DECISION

For v1:

```text
One Next Procurement Recommendation
per Item + MRP Run
```

The system does NOT generate a full multi-order procurement calendar for the same Item inside one Run.

The goal is to identify the next actionable procurement decision.

---

## 3. Operational Run

### LOCKED DECISION

Operational Order Planning is based on:

```text
Latest completed official MRP Run
```

Historical Runs may be reviewed, but a Recommendation from an older Run is not considered current for PO creation.

---

## 4. Applicable Items

### LOCKED DECISION

Purchase Recommendation applies only to external-supply requirements.

Baseline:

### BUY
External recommendation applicable.

### HYBRID
Applicable only when the selected Run route is external.

### OUTSOURCE without Planning BOM
External recommendation applicable.

### MAKE
No purchase recommendation for the MAKE parent itself.

Child BUY/External materials may independently receive recommendations.

### OUTSOURCE with Planning BOM
The outsourced parent is not automatically converted into a material purchase recommendation in this Package; lower-level externally sourced inputs may be.

Service procurement remains outside current scope.

---

## 5. Recommendation Input

Recommendation consumes immutable selected MRP Run results and trace.

It must not silently recompute requirement from live Forecast/BOM/Inventory/PO data.

Inputs include at minimum:

- MRP Run identity
- Item
- Net external requirement
- Need By Date
- Starting/Firm supply context used by Run
- planning thresholds
- available Sourcing Options
- Source readiness
- configured lead times
- MOQ
- current effective source Price/Currency at recommendation-generation time

Machine Recommendation becomes its own immutable snapshot.

---

## 6. Need By Date

### LOCKED DECISION

`Need By Date` is the first date on which the Item requires additional external supply according to the selected MRP Run.

It is derived from MRP-10 output.

MRP-12 must not invent a different shortage date.

---

## 7. Candidate Sourcing Options

### LOCKED DECISION

Only Sourcing Options that are:

```text
Active
+
Planning Ready
```

are eligible as machine candidates.

Planning Ready follows MRP-05 rules.

Inactive/incomplete Sourcing Options may remain visible for historical context but are not selectable by the machine as valid candidates.

---

## 8. Candidate Source Eligibility

A candidate Source must have enough data to calculate procurement timing and quantity context.

At minimum where applicable:

- Supplier active
- Item active
- compatible Supply Method
- current valid Price/Currency
- MOQ
- configured lead-time stages
- Shipping Mode / required route data

If critical source data is missing:

```text
Recommendation = BLOCKED
```

rather than guessed.

---

## 9. Source Selection Policy

### LOCKED DECISION

Machine source selection baseline:

1. If Preferred active Planning-Ready Source can meet Need By Date, select Preferred.
2. Otherwise select the fastest Planning-Ready Source that can meet Need By Date.
3. If no Source can meet Need By Date, select the fastest Planning-Ready Source as the emergency/late candidate.
4. If no Planning-Ready Source exists, Recommendation is BLOCKED.

---

## 10. Preferred Source Meaning

Preferred Source is a business preference, not an unconditional winner.

It must not be selected if it cannot meet the required timing while another valid source can.

---

## 11. No Cheapest-Supplier Guess

### LOCKED DECISION

Machine Recommendation must NOT rank sources across different currencies by "cheapest" price without a trusted exchange-rate layer.

Example:

```text
0.42 USD
3.10 CNY
```

must not be automatically compared financially.

Price/Currency are displayed and snapshotted.

FX comparison belongs to a future financial layer.

---

## 12. Same-Currency Price

Even where candidate Sources share the same Currency, v1 machine selection remains timing/preference driven.

Price may be shown for expert review.

Do not silently introduce a weighted cost optimization formula in v1.

---

## 13. Configured Lead Time

For a selected Sourcing Option:

```text
Total Configured Lead Time
=
Supplier Lead Time
+ Forwarder-to-Shipment Days
+ Shipping Days
+ Customs Days
```

using the MRP-05 configured values.

Calendar days are used.

Missing required duration means source readiness is incomplete.

---

## 14. Lead-Time Snapshot

### LOCKED DECISION

Selected lead-time assumptions are copied into the Recommendation snapshot.

Later Sourcing Master changes do not rewrite the historical Recommendation.

Snapshot includes relevant stage values and Total Configured Lead Time.

---

## 15. Order By Date

### LOCKED DECISION

```text
Order By Date
=
Need By Date
-
Total Configured Lead Time
```

using calendar days.

This is the latest baseline ordering date under the configured source assumptions.

---

## 16. Earliest Feasible Receipt

### LOCKED DECISION

If order is not yet late:

```text
Earliest Feasible Receipt
=
Need By Date
```

for sizing purposes when source can meet that date.

If:

```text
Order By Date < MRP Run As-Of Date
```

then:

```text
Earliest Feasible Receipt
=
As-Of Date + Total Configured Lead Time
```

This exposes the realistically achievable receipt timing.

---

## 17. Late Days

If Earliest Feasible Receipt is later than Need By Date:

```text
Late Days
=
Earliest Feasible Receipt - Need By Date
```

This is Derived.

Late Days must not be hidden.

---

## 18. Decision Horizon

### LOCKED DECISION

The previously open `Decision Horizon` is now CLOSED.

For an on-time recommendation:

```text
Decision Horizon
=
Target Receipt Date + Safe Start Days
```

where baseline Target Receipt Date is Need By Date.

For a late recommendation:

```text
Decision Horizon
=
Earliest Feasible Receipt + Safe Start Days
```

Purpose:

The proposed order should not merely rescue the first shortage day; it should restore material position into the Safe planning range.

---

## 19. Target Receipt Date

Machine baseline:

### On-Time Case

```text
Target Receipt Date = Need By Date
```

### Late Case

```text
Target Receipt Date = Earliest Feasible Receipt
```

Expert may later Modify Target Receipt Date.

---

## 20. MRP Horizon Sufficiency

### LOCKED DECISION

If:

```text
Decision Horizon > MRP Run Horizon End
```

the engine does not know enough future demand to size the order confidently.

Recommendation state:

```text
HORIZON_INSUFFICIENT
```

The system must not invent demand beyond the MRP Run.

---

## 21. Recommendation Qty under Insufficient Horizon

### LOCKED DECISION

When Horizon is insufficient, the system must NOT publish a normal valid final:

```text
System Recommended Qty
```

as though complete.

It may show known minimum requirement for diagnostic purposes if clearly labeled:

```text
Minimum Known Need
```

but operational Recommendation remains BLOCKED until a Run with sufficient horizon is available.

---

## 22. Base Recommended Qty

### LOCKED DECISION

For the selected Target Receipt Date and Decision Horizon:

`Base Recommended Qty` is the minimum additional supply quantity arriving on the Target Receipt Date required to keep projected balance at or above Safety Stock throughout the Decision Horizon.

Conceptually:

```text
Base Recommended Qty
=
max(
  Safety Stock - Minimum Projected Balance over Target Receipt..Decision Horizon,
  0
)
```

using the selected MRP Run's demand/firm-supply timeline and adding the hypothetical new supply only for sizing simulation.

Exact algorithm must be specified in MRP-12A and tested numerically.

---

## 23. Quantity Simulation Boundary

The hypothetical supply used to size Recommendation:

- exists only inside Recommendation calculation
- does NOT modify MRP Run
- does NOT become Firm Incoming
- does NOT alter Inventory
- does NOT alter PO data

It is a sizing simulation only.

---

## 24. Existing Firm Supply

Base Recommended Qty must respect all Firm Supply already included in the selected MRP Run.

Do not count a new proposed order as if it already existed.

---

## 25. Safety Stock in Sizing

Safety Stock is the target floor through Decision Horizon.

It is not added repeatedly as daily demand.

The sizing algorithm must avoid double-counting Safety Stock.

---

## 26. MOQ

### LOCKED DECISION

Selected Source MOQ comes from MRP-05 snapshot.

Rule:

```text
System Recommended Qty
=
MAX(Base Recommended Qty, MOQ)
```

If:

```text
MOQ = 0
```

then no minimum is applied.

---

## 27. No Pack / Carton Rounding

### LOCKED DECISION

MRP-12 does not perform:

- order multiple
- carton rounding
- pallet rounding
- supplier pack-size rounding

because v1 Sourcing only defines MOQ in Item Base Unit.

Any future order-multiple capability must be explicit.

---

## 28. MOQ Excess Warning

### LOCKED DECISION

If applying MOQ causes post-receipt projected coverage to exceed:

```text
Excess Start Days
```

generate:

```text
MOQ_CAUSES_EXCESS
```

warning.

The Recommendation may remain valid because MOQ is a real commercial constraint.

Do not silently reduce below MOQ.

---

## 29. Recommended Qty Trace

Recommendation detail must preserve at least:

- Base Recommended Qty
- MOQ
- System Recommended Qty
- Safety Stock
- Target Receipt Date
- Decision Horizon
- sizing rationale / minimum projected balance where useful

This makes the recommendation explainable.

---

## 30. Commercial Snapshot

### LOCKED DECISION

Machine Recommendation snapshots:

- selected Sourcing Option
- Supplier
- Supply Route
- Shipping Mode
- Incoterm
- Unit Price
- Currency
- MOQ
- configured lead-time stages
- Total Configured Lead Time

Where Supplier payment/commercial context is carried for future PO conversion, preserve the Supplier Default Payment Profile including:

- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Credit Anchor Event (`ORDERED` / `SHIPPED` / `WAREHOUSE_RECEIPT`)

MRP-12 does not own payment rules. It preserves the source context without drift. Credit%>0 requires CreditDays>0 and Credit Anchor; Credit%=0 allows a null Anchor. `WAREHOUSE_RECEIPT` means the operational Final Receipt Date later derived by MRP-13; MRP-12 does not derive it.

Later Master changes do not rewrite historical Recommendation.

---

## 31. Estimated Order Value

Derived:

```text
Estimated Order Value
=
System Recommended Qty × Unit Price Snapshot
```

expressed only in the selected Source Currency.

No automatic FX conversion is performed.

---

## 32. Recommendation Algorithm Version

### LOCKED DECISION

Each Machine Recommendation records:

```text
Recommendation Algorithm Version
```

Example:

```text
ORDER-PLANNING-1.0
```

This is separate from MRP Engine Version.

Historical recommendations must remain explainable after recommendation logic changes.

---

## 33. Machine Recommendation Immutability

### LOCKED DECISION

Once generated, Machine Recommendation snapshot is immutable.

Expert actions do not overwrite:

- selected machine Source
- machine Qty
- machine dates
- machine priority
- machine reasoning

Expert Decision is stored separately.

---

## 34. Recommendation State

Suggested stable states:

```text
READY
BLOCKED
OUTDATED_RUN
CONVERTED_TO_PO
```

Additional technical states may be proposed in MRP-12A if necessary.

`HORIZON_INSUFFICIENT` is a blocking reason/exception, not a fake Ready recommendation.

---

## 35. Next Action

### LOCKED DECISION

Derived machine codes:

```text
RESOLVE_DATA
EMERGENCY_REVIEW
ORDER_NOW
ORDER_BY_DATE
NO_ACTION
```

These are not editable text lists.

Human-friendly messages may include a date.

---

## 36. Next Action Rules

Baseline:

### RESOLVE_DATA
Recommendation is blocked/incomplete.

### EMERGENCY_REVIEW
Earliest feasible receipt is after Need By Date.

### ORDER_NOW
Order By Date <= As-Of Date and source can still materially improve the position.

### ORDER_BY_DATE
Order By Date is in the future.

### NO_ACTION
No external procurement requirement currently needs action.

MRP-12A must align `NO_ACTION` behavior with whether rows with zero requirement are even materialized.

---

## 37. Procurement Priority

### LOCKED DECISION

The previously open Priority classification is now CLOSED.

Machine Priority:

```text
P0_EMERGENCY
P1_DUE_NOW
P2_UPCOMING
P3_PLANNED
```

A blocked/incomplete recommendation does NOT receive a fake numeric priority.

Instead:

```text
BLOCKED
```

is its state.

---

## 38. P0 Emergency

```text
Earliest Feasible Receipt > Need By Date
```

means current valid sources cannot meet need on time.

Priority:

```text
P0_EMERGENCY
```

Next Action:

```text
EMERGENCY_REVIEW
```

---

## 39. P1 Due Now

When:

```text
Order By Date <= As-Of Date
```

and recommendation is otherwise valid and not P0:

```text
Priority = P1_DUE_NOW
```

---

## 40. P2 Upcoming

When:

```text
0 < Days Until Order By <= Item Critical Days
```

then:

```text
Priority = P2_UPCOMING
```

This uses Item planning policy rather than arbitrary fixed windows.

---

## 41. P3 Planned

When:

```text
Days Until Order By > Item Critical Days
```

then:

```text
Priority = P3_PLANNED
```

---

## 42. Missing/Invalid Priority Policy

If Item planning thresholds required for classification are invalid/missing:

- do not guess
- Recommendation becomes blocked/incomplete according to approved readiness semantics

Do not invent a default Critical Days value.

---

## 43. Priority Is Not Risk Band

MRP-11 Risk Band describes material condition.

MRP-12 Priority describes procurement action timing.

Example:

A Material may currently look SAFE because a PO is inbound, while its next purchasing action is still P3_PLANNED.

These concepts must remain separate.

---

## 44. Expert Decision

### LOCKED DECISION

Available expert decisions:

```text
ACCEPT
MODIFY
REJECT
DEFER
```

Machine Recommendation remains immutable.

---

## 45. Accept

`ACCEPT` means Expert accepts machine recommendation as proposed.

Effective expert values equal machine snapshot:

- selected Source
- Qty
- Target Receipt Date

No separate Reason is required unless MRP-12A proposes an optional note.

---

## 46. Modify

### LOCKED DECISION

Expert may Modify:

- Selected Source
- Final Qty
- Target Receipt Date

Reason is required.

The modified Source must be a valid usable Sourcing Option unless a future exception workflow is explicitly approved.

---

## 47. Expert Final Qty

Rules:

- > 0 for MODIFY/ACCEPT procurement decision
- Item Base Unit
- decimal/numeric
- not constrained to System Recommended Qty
- if below selected Source MOQ, system should block or explicitly require a policy exception

Baseline recommendation:

```text
Do not allow Final Qty < MOQ
```

without a future approved commercial exception rule.

MRP-12A must verify this does not contradict project requirements.

---

## 48. Modified Target Receipt

If Expert changes Target Receipt Date, system must clearly show:

- machine Target Receipt
- expert Target Receipt
- timing impact where derivable
- recalculated Expert Order By Date using final selected Source lead time

Do not rewrite machine dates.

---

## 49. Expert Order By Date

For final Expert Source:

```text
Expert Order By Date
=
Expert Target Receipt Date
-
Final Source Total Configured Lead Time
```

This is Derived.

If Expert target is already infeasible:

- show warning/error
- do not silently change date

---

## 50. Modified Source Snapshot

When Expert selects another Source, final decision snapshot must preserve:

- final Supplier
- final Sourcing Option
- final Price/Currency
- final MOQ
- final Lead-Time assumptions

This preserves what the expert actually approved.

---

## 51. Reject

### LOCKED DECISION

`REJECT` requires Reason.

Examples may include:

- product discontinuation
- commercial hold
- deliberate stock reduction

Reject does not alter MRP Run.

It only records the human decision against the Recommendation.

---

## 52. Defer

### LOCKED DECISION

`DEFER` requires:

- Reason
- Review Date

The Recommendation remains unresolved for procurement.

Review Date is a human follow-up date, not a new MRP Need By date.

---

## 53. Expert Decision History

### LOCKED DECISION

Expert Decisions are historical records.

Do not overwrite the previous decision.

Example:

```text
Decision 1 = DEFER
Decision 2 = ACCEPT
```

Latest valid decision determines current expert state.

History remains visible.

---

## 54. Expert Decision Metadata

Each decision conceptually preserves:

- Decision ID
- Recommendation
- Decision Type
- Reason
- actor
- timestamp
- Final Source snapshot where applicable
- Final Qty
- Final Target Receipt
- Review Date for DEFER
- derived Expert Order By Date where applicable

Exact model is proposed in MRP-12A.

---

## 55. Decision State vs Machine State

Machine state and Expert decision are distinct.

Example:

```text
Machine Recommendation = READY / P1
Expert Decision = DEFER
```

Do not mutate machine Recommendation to pretend it never existed.

---

## 56. Outdated Run

### LOCKED DECISION

If a newer completed official MRP Run exists than the Run behind a Recommendation:

```text
Recommendation State = OUTDATED_RUN
```

Historical Recommendation remains readable.

---

## 57. Outdated Recommendation and PO Conversion

An `OUTDATED_RUN` Recommendation must not be directly converted into a new PO without Refresh/Reconfirmation against the latest valid Run.

Exact reconfirm flow belongs to MRP-13 integration.

MRP-12 establishes the block.

---

## 58. Refresh Behavior

Refresh means generating a new Recommendation from the newer MRP Run.

It does not overwrite the old Recommendation.

Historical sequence remains:

```text
Recommendation from Run 127
Recommendation from Run 128
```

---

## 59. Converted Recommendation

Future Package 13 may mark a Recommendation/Expert Decision:

```text
CONVERTED_TO_PO
```

and retain a reference to the Draft/PO created.

MRP-12 only prepares this integration boundary.

---

## 60. Order Planning Grid

Baseline fields:

- Official Item Code (`Item.BusinessNumber`)
- Item Name
- Supplier / Selected Source
- Recommendation State
- Priority
- Next Action
- Need By Date
- Target Receipt Date
- Order By Date
- Earliest Feasible Receipt
- Late Days
- Starting Actual On Hand
- Firm Incoming
- Open PO Qty where available from Run context
- Demand through Decision Horizon
- Total Configured Lead Time
- MOQ
- Base Recommended Qty
- System Recommended Qty
- Unit Price
- Currency
- Estimated Order Value
- Expert Decision
- Expert Final Qty
- Expert Final Source
- Expert Target Receipt
- Review Date where deferred

Exact columns may be organized into Summary/Detail.

---

## 61. Demand Through Decision Horizon

Recommendation detail should show the amount of relevant future material demand considered through Decision Horizon.

This is informational trace supporting quantity explanation.

It must be derived from selected MRP Run result/simulation.

---

## 62. Last Order Date

Where Purchase Supply data exists, Recommendation detail may show:

```text
Last Order Date
```

as informational context.

It must not drive machine sizing unless explicitly specified.

---

## 63. Remaining Open Orders

Recommendation detail may show:

- Firm Incoming
- open PO qty
- overdue Firm qty
- ETA Missing qty

but only using the selected MRP Run / snapshot context for machine recommendation explanation.

Do not mix current live PO data into historical Recommendation.

---

## 64. Sourcing Candidate Comparison

Detail may show candidate Sources with:

- Preferred flag
- Supplier
- Shipping Mode
- Incoterm
- Total Lead Time
- Can Meet Need By? yes/no
- MOQ
- Price
- Currency

Do not display a fabricated cross-currency "cheapest" rank.

---

## 65. Source Selection Explanation

Machine Recommendation should record a stable reason code such as:

```text
PREFERRED_MEETS_DATE
FASTEST_MEETS_DATE
FASTEST_AVAILABLE_BUT_LATE
NO_READY_SOURCE
```

Human-readable explanation may be localized.

---

## 66. Recommendation Exceptions

Examples:

```text
NO_READY_SOURCE
HORIZON_INSUFFICIENT
MISSING_PRICE
MISSING_LEAD_TIME
MISSING_MOQ
INVALID_PLANNING_POLICY
MOQ_CAUSES_EXCESS
OUTDATED_RUN
```

Exact codes are proposed/normalized in MRP-12A.

Blocking vs warning severity must be explicit.

---

## 67. No Silent Guessing

MRP-12 must not guess:

- FX rate
- missing Lead Time
- missing MOQ
- missing Price
- missing Source
- demand beyond MRP horizon

Critical unknowns must become explicit exceptions.

---

## 68. Source Price Date

The machine Recommendation uses the valid current Sourcing Price at recommendation generation date according to MRP-05 effective dating.

That price is then snapshotted.

If no current valid price exists:

```text
Recommendation = BLOCKED
```

under MRP-05 Planning Readiness rules.

---

## 69. Recommendation Time Context

Recommendation generation time must preserve:

- underlying MRP Run As-Of Date
- Recommendation Created At
- Source data snapshot effective at Recommendation generation

This distinction is important because Sourcing Master may change after the MRP Run but before Recommendation is generated.

MRP-12A must decide whether operational Recommendation generation must occur immediately with the MRP Run or explicitly capture this time difference.

No silent ambiguity.

---

## 70. Recommendation Reproducibility

Historical Recommendation must remain explainable even after:

- new MRP Run
- Sourcing changes
- Price changes
- MOQ changes
- Lead-Time changes
- Preferred Source changes

Therefore both MRP Run reference and Recommendation commercial snapshot are required.

---

## 71. Algorithm Determinism

Given:

- same MRP Run
- same eligible Sourcing snapshots
- same Algorithm Version

machine Recommendation must be deterministic.

No AI/probabilistic source selection is allowed.

---

## 72. AI Boundary

Future AI may explain:

- why a source was chosen
- why a recommendation is P0
- why MOQ created excess

It must not silently override deterministic machine recommendation.

No AI dependency in MRP-12.

---

## 73. No Automatic Expert Approval

A machine Recommendation does not become an Expert ACCEPT automatically.

Human decision remains explicit.

This separation must remain auditable.

---

## 74. Bulk Expert Actions

Bulk Accept/Defer may be considered later.

It is not required for v1 unless MRP-12A finds a strong usability need.

Do not add bulk financial/procurement commitment actions casually.

---

## 75. Permissions

Architecture should distinguish at minimum:

- View Order Planning
- Generate/Refresh Recommendations
- Make Expert Decision
- Modify Recommendation Final Decision

Future PO creation permission belongs to Package 13.

Do not create parallel Identity infrastructure.

---

## 76. Auditability

Important events:

- Recommendation generated
- Machine Algorithm Version
- Source selection reason
- input Run
- commercial snapshot
- recommendation state change
- Expert ACCEPT/MODIFY/REJECT/DEFER
- Decision Reason
- Review Date
- outdated status
- future conversion reference

Machine snapshot and Expert history are primary records.

---

## 77. API / Application Use Cases

Required conceptual use cases:

### Recommendation
- generate recommendations from selected/latest MRP Run
- list/filter recommendations
- get detail
- get source candidates
- refresh recommendation from newer Run
- calculate/view recommendation reasoning

### Expert Decision
- Accept
- Modify
- Reject
- Defer
- view decision history

No PO creation endpoint in MRP-12.

---

## 78. Recommendation Generation Scope

MRP-12A should propose whether recommendation generation is:

- automatically triggered after a completed MRP Run
or
- explicitly invoked by authorized user

Baseline preference:

```text
Explicit generation/refresh action
```

for v1, to keep system behavior controlled and auditable.

Do not add background automation without need.

---

## 79. UI Structure

Recommended navigation:

```text
MRP
├── Runs
├── Material Planning
├── Exceptions
├── Order Planning
│   ├── Recommendations
│   └── Decision History
└── Purchase Supply
```

Exact route structure follows app conventions.

---

## 80. Recommendation Detail

Recommended sections:

### Requirement
- Need By
- Target Receipt
- Decision Horizon
- demand context
- stock / Firm Supply context

### System Recommendation
- Source
- Lead Time
- Order By
- Base Qty
- MOQ
- Recommended Qty
- Price/Currency
- estimated value
- Priority / Next Action
- Reason / warnings

### Candidate Sources
comparison table

### Expert Decision
Accept / Modify / Reject / Defer

### History
machine snapshot + expert decisions

---

## 81. Read-Only Machine Fields

Machine fields are read-only in UI.

Expert does not edit machine Source/Qty/date cells directly.

Modify creates separate Expert final values.

---

## 82. Expert Modify UX

When MODIFY selected:

Fields become available for Expert final decision:

- Source
- Final Qty
- Target Receipt
- Reason

System shows derived:

- Expert Order By
- MOQ validation
- timing feasibility warning
- estimated final order value in final Source Currency

---

## 83. Defer UX

DEFER requires:

- Reason
- Review Date

Order Planning can filter:

```text
Review Due
Review Upcoming
Deferred
```

This is operational follow-up, not an automation/reminder engine.

---

## 84. Priority Sorting

Default Recommendation sorting:

1. BLOCKED requiring data resolution
2. P0 Emergency
3. P1 Due Now
4. P2 Upcoming
5. P3 Planned

Within priority:

- earliest Need By
- earliest Order By

Exact UX may separate BLOCKED in its own section.

---

## 85. Filters

Baseline filters:

- Item
- Category
- Supplier / Selected Source
- Recommendation State
- Priority
- Next Action
- Expert Decision
- Need By date range
- Order By date range
- Review Date
- warning/exception presence
- OUTDATED_RUN
- P0 only

---

## 86. Jalali / Date Handling

Business dates display Jalali.

Canonical dates remain Gregorian.

Date arithmetic uses canonical date values.

Do not subtract formatted Jalali strings.

---

## 87. Numeric Precision

Use decimal/numeric for:

- Base Qty
- Recommended Qty
- Final Qty
- MOQ
- Price
- Estimated Order Value

No binary floating point.

---

## 88. Currency

Estimated values remain in original Recommendation Source Currency.

No cross-currency aggregation into one monetary total without an approved FX layer.

UI may group totals by Currency if useful.

---

## 89. No Global Procurement Value Total Across Currencies

Do not show:

```text
Total Recommended Purchases = X
```

across USD/CNY/EUR/AED as one number without FX.

Instead:

```text
USD total
CNY total
EUR total
...
```

may be displayed separately.

---

## 90. Recommendation and Cash Flow

Payment Snapshot/Cash Flow timing is not calculated in MRP-12.

Package 13/14 may later use accepted PO/commercial snapshots.

If payment context is carried, Credit Anchor Event must be preserved for that handoff; MRP-12 must not derive payment or Receipt dates. WAREHOUSE_RECEIPT is resolved later from MRP-13 operational Final Receipt Date.

MRP-12 estimated order value is not a cash-flow schedule.

---

## 91. Database Integrity

Expected protections where practical:

- Recommendation linked to MRP Run + Item
- one machine Recommendation identity per generated iteration
- immutable machine snapshot
- stable Algorithm Version
- positive quantities
- valid Expert Decision type
- Reason required for Modify/Reject/Defer
- Review Date required for Defer
- final Source/Qty required for Accept/Modify as appropriate
- historical decisions preserved
- no destructive cascade

Exact model is proposed in MRP-12A.

---

## 92. Recommendation Uniqueness

Because Refresh creates a new historical Recommendation, do not enforce a simplistic permanent unique constraint on:

```text
Item + MRP Run
```

if regeneration/versioning is needed.

MRP-12A must choose a clean iteration/version model.

Business requirement:

- machine snapshots are immutable
- history is retained
- one current recommendation can be identified

---

## 93. Concurrency

Concurrency-sensitive operations include:

- generating recommendation versions
- two experts attempting decisions simultaneously
- refreshing while a decision is being made
- marking OUTDATED_RUN

MRP-12A must propose smallest robust PostgreSQL/EF Core strategy.

---

## 94. Stale Decision Protection

If user opens a Recommendation and a newer Expert Decision is saved before their submission:

- stale write should not silently overwrite newer decision

Use existing concurrency patterns.

---

## 95. Outdated Run Detection

System should determine outdated status from official Run chronology, not manually entered flags alone.

A persisted state may exist for operational convenience but must remain reconcilable.

---

## 96. Source Deactivation after Recommendation

Historical Recommendation remains readable if selected Source is later inactive.

For a new Expert MODIFY decision, inactive Source should normally not be selectable.

---

## 97. Price Change after Recommendation

Historical machine Estimated Value remains based on snapshot price.

If Expert modifies Source later, final Expert decision uses the current valid snapshot for the newly selected Source at decision time unless MRP-12A defines a stricter rule.

This distinction must be visible/auditable.

---

## 98. Recommendation State and Expert Decision Display

Example:

```text
Machine State: READY
Priority: P1_DUE_NOW
Expert: DEFER
Review Date: 1405/05/25
```

Do not collapse all of these into one vague Status column.

---

## 99. Future Package 13 Contract

Package 13 may convert only an eligible current Expert decision into Draft PO.

Baseline eligible:

```text
ACCEPT
MODIFY
```

and Recommendation must:

- not be OUTDATED_RUN
- not be BLOCKED
- not already converted
- have valid final Source
- valid Final Qty
- valid commercial snapshot

Package 13 finalizes transactional rules.

---

## 100. No PO Creation

MRP-12 must NOT:

- allocate PO Number
- create PO Header/Lines
- post to Purchase Supply
- send Supplier order
- create receipt
- affect Inventory

It only produces an approved procurement decision record.

---

## 101. Performance

Order Planning Summary should be server-filterable/sortable.

Do not run quantity sizing simulation for every grid repaint.

Machine Recommendation is persisted snapshot.

Candidate-source details load on demand.

Avoid N+1 Sourcing queries.

---

## 102. Recommendation Calculation Architecture

MRP-12A should propose a deterministic application/domain service pipeline:

1. load selected MRP Run external requirement
2. validate Item policy
3. load eligible Sourcing Options in batch
4. classify source timing feasibility
5. select machine Source
6. derive Need By / Target Receipt / Order By / lateness
7. validate MRP horizon
8. simulate Base Qty through Decision Horizon
9. apply MOQ
10. derive excess warning
11. derive Priority / Next Action
12. snapshot result

No source-of-truth mutation.

---

## 103. Tests

Required meaningful test scenarios:

Canonical numerical fixtures are defined read-only in `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md`. Recommendation regression uses GD-04, GD-04B, the GD-08 Recommendation section and GD-09 Recommendation snapshot without changing their expected values.

### Source Selection
- Preferred can meet date
- Preferred cannot, faster backup can
- multiple can meet, Preferred wins
- none can meet, fastest chosen as late
- no ready source → blocked
- no cross-currency cheapest ranking

### Timing
- Order By date
- on-time receipt
- late receipt
- Late Days
- Decision Horizon normal
- Decision Horizon late case

### Horizon
- sufficient horizon
- insufficient horizon blocks valid qty

### Quantity
- Base Qty simulation
- Safety Stock floor
- existing Firm Supply included
- MOQ raises Qty
- MOQ=0
- MOQ causes Excess warning
- no pack rounding

### Priority
- P0
- P1
- P2 using Critical Days
- P3
- blocked has no fake P priority

### Snapshot
- Sourcing changes do not mutate machine Recommendation
- Algorithm Version preserved

### Expert
- Accept
- Modify Source/Qty/Date + Reason
- Reject + Reason
- Defer + Reason + Review Date
- history preserved
- stale expert write rejected

### Outdated
- newer MRP Run marks prior recommendation outdated
- historical recommendation remains readable
- outdated cannot proceed to PO contract

---

## 104. Explicit Non-Scope

MRP-12 must NOT implement:

- PO creation
- PO Numbering
- Purchase Order status workflow
- Supplier confirmation
- Receipt
- Inventory posting
- Over-Receipt
- Payment scheduling
- Cash Flow
- FX conversion
- landed cost
- order multiple / carton rounding
- split recommendation into multiple future POs
- automatic mixed Make/Buy allocation
- AI purchasing decision
- automatic expert approval
- supplier communication

---

## 105. Acceptance Criteria

MRP-12 is acceptable when:

1. Recommendation grain is one next external procurement decision per Item + Run iteration.
2. Operational recommendation uses latest completed official Run.
3. Historical recommendations remain reviewable.
4. BUY/external-route requirements are included.
5. MAKE parent is not turned into purchase recommendation.
6. Need By comes from MRP-10.
7. Candidate Sources are Active + Planning Ready.
8. Preferred source wins only if it can meet date.
9. Fastest on-time backup is selected when Preferred cannot.
10. Fastest source is selected with Emergency state when none can meet date.
11. No cross-currency cheapest-source guessing.
12. Selected lead time is snapshotted.
13. Order By = Need By - Lead Time.
14. Earliest Feasible Receipt is derived for late orders.
15. Decision Horizon = Target Receipt + Safe Start Days.
16. Late case uses Earliest Feasible Receipt + Safe Start Days.
17. Insufficient MRP horizon blocks normal valid quantity recommendation.
18. Base Recommended Qty restores Safety Stock through Decision Horizon.
19. MOQ is applied as minimum.
20. No pack/carton rounding exists.
21. MOQ-caused excess is warned.
22. Commercial Source data is snapshotted.
23. Next Action is derived.
24. Priority = P0/P1/P2/P3 and is derived.
25. Blocked recommendation has no fake P priority.
26. Algorithm Version is preserved.
27. Machine Recommendation is immutable.
28. Expert decisions = Accept/Modify/Reject/Defer.
29. Modify requires Reason.
30. Reject requires Reason.
31. Defer requires Reason + Review Date.
32. Expert Decision history is preserved.
33. New MRP Run makes old recommendation OUTDATED_RUN.
34. Historical old recommendation is not deleted.
35. OUTDATED_RUN is blocked from direct PO conversion.
36. No PO is created in MRP-12.
37. No FX conversion is performed.
38. No AI/probabilistic decision is used.
39. Tests verify source selection, sizing, MOQ, priority and expert history.

---

## 106. Locked Decisions

The following are locked for MRP-12:

1. One next Recommendation per Item + MRP Run iteration in v1.
2. Operational source Run = latest completed official MRP Run.
3. Recommendation applies to external supply requirement only.
4. Need By Date comes from MRP-10.
5. Machine candidates = Active + Planning Ready Sourcing Options.
6. Preferred Source selected if it can meet Need By.
7. Otherwise fastest Source that can meet Need By.
8. If none can meet, fastest valid Source is emergency/late candidate.
9. No cross-currency cheapest ranking without FX.
10. Selected Source lead time is snapshotted.
11. Order By = Need By - Configured Lead Time.
12. Earliest Feasible Receipt = As-Of + Lead Time when late.
13. Decision Horizon = Target Receipt + Safe Start Days.
14. Late Decision Horizon = Earliest Feasible Receipt + Safe Start Days.
15. Horizon beyond MRP Run = HORIZON_INSUFFICIENT / blocked.
16. Base Recommended Qty restores Safety Stock through Decision Horizon.
17. System Recommended Qty = MAX(Base Qty, MOQ).
18. MOQ=0 means no minimum.
19. No pack/carton/order-multiple rounding.
20. MOQ causing Excess creates warning.
21. Recommendation snapshots Source/Supplier/Price/Currency/MOQ/Lead Time/Route.
22. Next Action codes = Resolve Data / Emergency Review / Order Now / Order By Date / No Action.
23. Priority = P0 Emergency / P1 Due Now / P2 Upcoming / P3 Planned.
24. P2 window uses Item Critical Days.
25. Machine Recommendation snapshot is immutable.
26. Recommendation has Algorithm Version.
27. Expert Decision = ACCEPT / MODIFY / REJECT / DEFER.
28. Modify may change Source / Final Qty / Target Receipt and requires Reason.
29. Reject requires Reason.
30. Defer requires Reason + Review Date.
31. Expert decisions are historical, not overwritten.
32. Newer MRP Run makes older Recommendation OUTDATED_RUN.
33. OUTDATED_RUN requires refresh/reconfirmation before PO conversion.
34. MRP-12 creates no PO.

---

## 107. Open Question Resolution

The previous open question:

`Decision Horizon`

is now CLOSED.

Approved:

```text
On-time:
Decision Horizon = Target Receipt Date + Safe Start Days

Late:
Decision Horizon = Earliest Feasible Receipt + Safe Start Days
```

The previous open question:

`Procurement Priority`

is now CLOSED.

Approved:

```text
P0_EMERGENCY
P1_DUE_NOW
P2_UPCOMING
P3_PLANNED
```

with:

```text
P2 window = Item Critical Days
```

and blocked/incomplete Recommendations do not receive a fake P-priority.

---

## 108. Remaining Relevant Open Questions

Intentionally deferred to MRP-13:

- Over-Receipt rule / tolerance
- final Recommendation→Draft PO transaction
- final PO status transition rules
- destination Inventory Warehouse at Receipt
- split-schedule receipt allocation
- mixed receipt + cancellation final status behavior

None blocks MRP-12.

---

# B. Prompt 12A — Order Planning Analysis

```text
# MRP-12A — Order Planning Recommendation + Expert Decision Analysis

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
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior Packages were implemented merely because they were planned.

TASK:
Analyze MRP-12 Order Planning Recommendation + Expert UI.

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

1. Understand MRP-12 as deterministic procurement recommendation layer after MRP-10.
2. Inspect actual MRP Run/read-model/Sourcing implementation.
3. Confirm applicable external-requirement Item/route rules.
4. Propose Recommendation identity/version/current-history model.
5. Preserve immutable machine snapshot + Algorithm Version.
6. Need By derivation from MRP Run.
7. Candidate Source readiness.
8. Source-selection algorithm:
   - Preferred if on-time
   - fastest on-time backup
   - fastest late source
   - no ready source
9. Confirm no cross-currency cheapest comparison.
10. Lead-time snapshot.
11. Order By formula.
12. Earliest Feasible Receipt.
13. Late Days.
14. Decision Horizon:
    Target Receipt + Safe Start Days.
15. Horizon-insufficient rule.
16. Design exact Base Recommended Qty simulation.
17. Prove Safety Stock is not double-counted.
18. Confirm existing Firm Supply from MRP Run remains part of simulation.
19. MOQ application.
20. MOQ=0.
21. MOQ excess warning.
22. No pack/order-multiple rounding.
23. Commercial snapshot.
    If payment context is included, verify preservation of the MRP-03 Credit Anchor codes (`ORDERED` / `SHIPPED` / `WAREHOUSE_RECEIPT`) and conditional validation; confirm WAREHOUSE_RECEIPT means operational Final Receipt Date and remains owned by MRP-13.
24. Recommendation price timing and distinction between MRP Run time vs Recommendation creation time.
25. Estimated value in original currency only.
26. Priority:
    - P0
    - P1
    - P2 using Critical Days
    - P3
    - blocked no P priority
27. Next Action derivation.
28. Machine recommendation reason codes.
29. Expert Decision model:
    ACCEPT / MODIFY / REJECT / DEFER
30. Modify fields:
    - Source
    - Final Qty
    - Target Receipt
    - Reason
31. Validate whether Final Qty below MOQ must be blocked.
    Baseline requirement: block unless explicit future exception policy.
32. Expert Source snapshot and Expert Order By derivation.
33. Reject Reason.
34. Defer Reason + Review Date.
35. Expert Decision history.
36. Concurrency/stale-write protection.
37. OUTDATED_RUN detection.
38. Refresh creates new historical Recommendation, does not overwrite.
39. Future Package-13 conversion eligibility contract.
40. API/use cases.
41. UI:
    - grid
    - detail
    - candidate sources
    - expert decision
    - history
42. Filters/sorting.
43. Permissions/Audit.
44. Numeric precision/Jalali/date rules.
45. Performance / batched Sourcing loading.
46. Testing plan.
47. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..12 and actual Repository.
48. Smallest implementation sequence.
49. Explicit non-scope.

Testing proposal must cover at minimum:

SOURCE
- Preferred on-time
- Preferred late / backup on-time
- multiple on-time candidates
- all candidates late
- no ready source
- no FX-based cheapest guess

TIMING
- Need By
- Order By
- Earliest Feasible Receipt
- Late Days
- normal Decision Horizon
- late Decision Horizon

QUANTITY
- Base Qty simulation
- Safety Stock floor
- existing Firm Supply
- MOQ
- MOQ=0
- MOQ excess warning
- no pack rounding

HORIZON
- sufficient
- insufficient blocks final valid qty

PRIORITY
- P0
- P1
- P2
- P3
- blocked no priority

EXPERT
- Accept
- Modify
- Reject
- Defer
- mandatory Reasons
- Review Date
- decision history
- stale concurrency

SNAPSHOT
- Sourcing/Price/MOQ/LeadTime changes do not mutate history
- Algorithm Version

OUTDATED
- newer MRP Run marks old recommendation outdated
- refresh keeps old history
- outdated blocked from conversion contract

Output:
1. Understanding
2. Repository Findings
3. Recommendation Model
4. Source Selection
5. Timing / Decision Horizon
6. Quantity / MOQ Algorithm
7. Priority / Next Action
8. Snapshot / Reproducibility
9. Expert Decision Model
10. Outdated / Refresh Model
11. API Design
12. UI Design
13. Permissions / Audit
14. Performance / Concurrency
15. Tests
    Map GD-04, GD-04B, GD-08 Recommendation and GD-09 Recommendation snapshot to the actual domain model. Verify Source selection, Order By, Decision Horizon, Priority, Base Qty, MOQ adjustment, currency isolation and snapshot stability without changing approved expected values.
16. Risks / Contradictions
17. Implementation Plan
18. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 12B — Order Planning Implementation

```text
# MRP-12B — Implement Order Planning Recommendation + Expert UI

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
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-12A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only approved MRP-12 Order Planning Recommendation + Expert Decision.

If MRP-12A contains unresolved material contradictions, STOP.

RECOMMENDATION
Implement immutable machine Recommendation snapshots linked to:
- MRP Run
- Item
- Algorithm Version

Do not overwrite historical Recommendation versions.

APPLICABILITY
Generate only for approved external requirement Items/routes.

NEED BY
Use MRP-10 result.

SOURCES
Load Active + Planning Ready Sourcing Options.

Machine selection:
1. Preferred if can meet date
2. otherwise fastest on-time
3. otherwise fastest valid late source
4. no ready source => BLOCKED

Do not rank cross-currency prices.

LEAD TIME
Use configured Sourcing stage durations and snapshot them.

ORDER BY
Need By - Total Lead Time.

LATE
When Order By is before As-Of:
- Earliest Feasible Receipt = As-Of + Lead Time
- derive Late Days
- P0 emergency when after Need By

DECISION HORIZON
On-time:
Target Receipt + Safe Start Days

Late:
Earliest Feasible Receipt + Safe Start Days

HORIZON SUFFICIENCY
If Decision Horizon exceeds MRP Run horizon:
- HORIZON_INSUFFICIENT
- BLOCKED
- do not publish a normal valid System Recommended Qty

QUANTITY
Implement approved deterministic sizing simulation:
- hypothetical supply at Target Receipt
- preserve existing Firm Supply
- minimum qty keeping projected balance >= Safety Stock through Decision Horizon
- no mutation of MRP Run

MOQ
System Recommended Qty = MAX(Base Qty, MOQ)

No:
- pack rounding
- carton rounding
- pallet rounding

Warn:
- MOQ_CAUSES_EXCESS

COMMERCIAL SNAPSHOT
Preserve:
- Sourcing Option
- Supplier
- route
- Shipping Mode
- Incoterm
- Price
- Currency
- MOQ
- stage Lead Times
- Total Lead Time
- payment profile context including Credit Anchor Event (`ORDERED` / `SHIPPED` / `WAREHOUSE_RECEIPT`) where carried for PO conversion

Do not make MRP-12 the payment source of truth. Preserve conditional validation. WAREHOUSE_RECEIPT uses MRP-13 operational Final Receipt Date; do not create or derive a competing date in MRP-12.

ESTIMATED VALUE
Recommended Qty × Unit Price
in original Currency only.

PRIORITY
Implement:
- P0_EMERGENCY
- P1_DUE_NOW
- P2_UPCOMING
- P3_PLANNED

P2:
Days Until Order By <= Item Critical Days

Blocked Recommendation gets no fake P priority.

NEXT ACTION
Stable codes:
- RESOLVE_DATA
- EMERGENCY_REVIEW
- ORDER_NOW
- ORDER_BY_DATE
- NO_ACTION

MACHINE IMMUTABILITY
Expert must not edit machine fields.

EXPERT DECISION
Implement historical:
- ACCEPT
- MODIFY
- REJECT
- DEFER

ACCEPT:
use machine Source/Qty/Target Receipt.

MODIFY:
allow approved:
- Source
- Final Qty
- Target Receipt
- Reason required

Validate final Source.
Validate Qty.
Apply approved MOQ rule from MRP-12A.
Derive Expert Order By from final Source Lead Time.

REJECT:
Reason required.

DEFER:
Reason + Review Date required.

HISTORY
Do not overwrite earlier Expert Decisions.

OUTDATED RUN
When newer completed official MRP Run exists:
- old Recommendation remains historical
- mark/derive OUTDATED_RUN
- block future PO conversion contract

REFRESH
Create new Recommendation history from latest Run.
Do not mutate old snapshot.

BACKEND
Implement only approved:
- domain/data model
- EF Core/migrations
- calculation/application services
- query/read models
- APIs
- validation
- concurrency

FRONTEND
Implement approved:
- Order Planning grid
- filters/sorting
- Recommendation detail
- candidate Source comparison
- System Recommendation section
- Expert Decision section
- Decision History
- OUTDATED/BLOCKED warnings

Follow UI-BRAND-RULES.md.
Support RTL/Jalali/mixed technical codes.

PERMISSIONS/AUDIT
Reuse existing platform.

TESTS
At minimum:
- Golden regression from GD-04, GD-04B, GD-08 Recommendation and GD-09 Recommendation snapshot, with expected values encoded independently from production logic
- source selection scenarios
- timing formulas
- Decision Horizon
- Horizon insufficiency
- Base Qty numeric fixtures
- Safety Stock
- Firm Supply
- MOQ / MOQ=0 / excess warning
- Priority P0..P3
- blocked no priority
- immutable machine snapshot
- commercial snapshot history
- Accept/Modify/Reject/Defer
- Reason/Review validation
- Expert history
- concurrency
- OUTDATED_RUN
- refresh history
- no PO creation
- no FX conversion

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend build/lint
- startup/health
- deterministic recommendation fixture

Do not claim checks not run.

SCOPE GUARD
Do NOT implement:
- PO creation
- PO Number
- PO operations
- supplier confirmation
- receipt
- inventory posting
- over-receipt
- cash flow
- FX
- landed cost
- pack rounding
- multi-order procurement schedule
- AI purchasing decisions

Final report:
- Changed
- Recommendation Model
- Source Selection
- Timing
- Quantity / MOQ
- Priority / Next Action
- Snapshots
- Expert Decisions
- Outdated / Refresh
- API
- UI
- Concurrency
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-13.
```

---

# D. Prompt 12C — Order Planning Independent Audit

```text
# MRP-12C — Order Planning Recommendation + Expert UI Independent Audit

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
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-12A Analysis
- MRP-12B implementation report

Inspect all MRP-12 implementation files.

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
   - PO creation
   - PO numbering
   - receipt/inventory posting
   - FX conversion
   - cash flow
   - over-receipt
   - AI decision

2. Recommendation Source
   - linked to explicit MRP Run
   - external requirement only
   - latest Run operational context
   - historical review preserved

3. Machine Snapshot
   - immutable
   - Algorithm Version
   - MRP Run reference
   - commercial snapshot
   - no live Master drift
   - Credit Anchor Event codes (`ORDERED` / `SHIPPED` / `WAREHOUSE_RECEIPT`) and conditional validation preserved when payment context is carried
   - MRP-12 does not own payment rules or payment-date resolution
   - WAREHOUSE_RECEIPT is resolved only from MRP-13 operational Final Receipt Date; First Receipt is not the financial anchor

4. Source Selection
   - Preferred on-time wins
   - faster backup when Preferred late
   - fastest late when none can meet
   - no ready source blocked
   - no cheapest cross-currency guess

5. Timing
   - Need By
   - Lead Time
   - Order By
   - Earliest Feasible Receipt
   - Late Days

6. Decision Horizon
   - normal = Target Receipt + Safe Days
   - late = Earliest Feasible + Safe Days
   - no arbitrary fixed decision window

7. Horizon Sufficiency
   - outside MRP horizon => blocked/incomplete
   - no fabricated future demand
   - no normal valid qty issued

8. Quantity
   - deterministic simulation
   - existing Firm Supply respected
   - Safety Stock floor
   - no double-counting
   - Base Qty traceable

9. MOQ
   - MAX(Base, MOQ)
   - MOQ=0
   - no pack rounding
   - excess warning

10. Priority
    - P0
    - P1
    - P2 uses Critical Days
    - P3
    - blocked has no fake priority
    - Priority distinct from MRP-11 Risk

11. Next Action
    - stable machine codes
    - derived only

12. Expert Decision
    - ACCEPT
    - MODIFY
    - REJECT
    - DEFER
    - machine fields preserved

13. Modify
    - Source/Qty/Target Receipt
    - Reason
    - final Source snapshot
    - Expert Order By
    - MOQ validation

14. Reject / Defer
    - Reason
    - Review Date for Defer

15. Decision History
    - old decisions preserved
    - latest decision derivable
    - stale write protection

16. Outdated Run
    - older Recommendation preserved
    - newer MRP Run makes old recommendation outdated
    - old recommendation blocked from PO-conversion contract
    - refresh creates new history

17. UI
    - System vs Expert values visually distinct
    - Candidate Sources
    - warnings
    - historical context
    - RTL/Jalali
    - no direct edit of machine snapshot

18. API / Permissions
    - no hidden PO creation
    - safe use cases
    - server validation

19. Performance
    - no recommendation recalculation on grid repaint
    - no N+1 sourcing
    - candidate details on demand

20. Tests
    Verify meaningful numerical/source-selection assertions against GD-04, GD-04B, GD-08 Recommendation and GD-09 Recommendation snapshot. Confirm inputs/outputs match canonical v1.0, expectations are not generated by production logic, currency remains isolated and historical snapshots do not drift.

21. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..12
    - approved MRP-12A
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
- Recommendation Correctness
- Snapshot / Historical Integrity
- Expert Decision Integrity
- Specification Compliance
- Scope Compliance
- Recommendation

Do not modify anything.
STOP.
```

---

# E. Final Pre-Codex Audit — MRP-12

## Result

`PASS`

## Audited Approved Decisions

تمام ۲۵ تصمیم تأییدشده Package 12 پوشش داده شده‌اند:

1. One next Recommendation per Item+Run iteration.
2. Latest completed MRP Run drives operational planning.
3. External requirement Item/route only.
4. Need By from MRP-10.
5. Candidate Sources = Active + Planning Ready.
6. Preferred/on-time then fastest/on-time then fastest/late source-selection rule.
7. No cross-currency cheapest guess.
8. Lead-Time snapshot.
9. Order By formula.
10. Earliest Feasible Receipt.
11. Decision Horizon = Target Receipt + Safe Days.
12. Late Decision Horizon = Earliest Feasible + Safe Days.
13. Horizon-insufficient blocking.
14. Base Recommended Qty restores Safety Stock through horizon.
15. MOQ applied as minimum.
16. No pack rounding.
17. MOQ Excess warning.
18. Commercial snapshot.
19. Derived Next Action.
20. Derived P0/P1/P2/P3 Priority.
21. Immutable machine snapshot + Algorithm Version.
22. Expert Accept/Modify/Reject/Defer.
23. Modify Source/Qty/Receipt + Reason.
24. Reject/Defer controlled with reasons and Review Date.
25. Old Recommendation becomes OUTDATED_RUN after newer MRP Run.

## Open Question Resolution

### Decision Horizon

CLOSED:

```text
Target Receipt + Safe Start Days
```

Late case:

```text
Earliest Feasible Receipt + Safe Start Days
```

### Procurement Priority

CLOSED:

```text
P0_EMERGENCY
P1_DUE_NOW
P2_UPCOMING
P3_PLANNED
```

with P2 using Item Critical Days rather than an arbitrary global time window.

## Cross-Package Consistency

- MRP-10 remains owner of Net Requirement and Need By.
- MRP-05 remains owner of current Sourcing configuration.
- MRP-12 snapshots selected commercial/timing assumptions.
- MRP-11 Risk Band remains separate from Procurement Priority.
- MRP-09 Purchase Supply remains actual committed Firm Supply.
- Recommendation does NOT become Firm Incoming.
- MRP-13 remains the only Package that may convert approved recommendation into PO.
- No Inventory is mutated.
- No FX layer is invented.

No material ownership conflict was found.

## Architecture Corrections Applied

- Source selection is deterministic and timing-aware.
- Preferred supplier is not blindly selected when late.
- Cross-currency price comparison is explicitly blocked.
- Recommendation sizing simulates a hypothetical receipt without mutating MRP.
- Decision Horizon is policy-driven through Safe Start Days.
- Insufficient MRP horizon blocks false precision.
- MOQ effect is transparent.
- Machine Recommendation and Expert Decision are permanently separated.
- Expert history is not overwritten.
- New MRP Run invalidates old operational recommendation without deleting history.
- Algorithm Version supports future auditability.
- PO conversion contract is prepared but not implemented.

## Remaining Intentional Open Questions

Deferred to Package 13:

- Over-Receipt rule/tolerance.
- Recommendation→Draft PO transaction.
- final PO lifecycle transition permissions.
- schedule-level receipt allocation.
- destination Warehouse selection.
- final status after mixed partial receipt + cancellation.

No unresolved MRP-12 question blocks the baseline.

## Prompt Audit

### MRP-12A
PASS

Analysis-only and explicitly validates source selection, timing, Decision Horizon, quantity simulation, MOQ, expert history and stale/outdated behavior.

### MRP-12B
PASS

Implementation is strictly limited to Recommendation + Expert Decision and explicitly prohibits PO/Inventory/FX/Cash Flow scope leakage.

### MRP-12C
PASS

Independent read-only audit checks deterministic recommendation correctness, snapshot immutability, expert separation, outdated-run handling and specification drift.

## Final Decision

`MRP-12 Order Planning Recommendation + Expert UI` is approved for the project Prompt Baseline.

It should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives the final cross-package audit.
