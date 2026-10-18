# Hadron ERP — MRP Package 09 Final

**Package:** MRP-09 Purchase Supply Foundation  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

## Owner Correction — 2026-08-31 (LOCKED)

- PO Line دیگر یک Sourcing Price فعال انتخاب نمی‌کند؛ `SourcingPriceId` برای رکوردهای جدید تهی است.
- UnitPrice و Currency ورودی صریح کاربر مجاز در تصمیم تجاری/PO هستند و در PO Snapshot تغییرناپذیر می‌شوند.
- Sourcing فقط منبع Item–Supplier، MOQ، Route و Lead Time است؛ هیچ fallback یا refresh قیمت از Sourcing مجاز نیست.
- داده قدیمی Sourcing Price حذف نمی‌شود، اما فقط آرشیو تاریخی است.

این اصلاح بر عبارت‌های متعارض «selected Sourcing Price» در baseline قدیمی این سند مقدم است.

---

# A. Final Feature Specification — 09-PURCHASE-SUPPLY-FOUNDATION

## 1. Purpose

این Feature زیرساخت Purchase Supply را برای اتصال Sourcing، MRP Planning و آینده Purchase Order Operations ایجاد می‌کند.

MRP-09 باید بتواند:

- Purchase Order identity
- PO Header / Lines
- PO Status semantics
- Commercial snapshots
- Sourcing snapshots
- Delivery Schedules / Split Delivery
- Planned and Actual supply milestones
- Firm Supply rules
- Remaining Open Qty
- Partial Receipt foundation
- Cancellation foundation
- historical traceability

را مدل کند.

این Feature نباید هنوز عملیات کامل Procurement، Receipt posting into Inventory، Recommendation→PO orchestration یا Over-Receipt behavior را نهایی کند.

---

## 2. Purchase Order Identity

### LOCKED DECISION

Each Purchase Order has:

### Internal Technical ID

- system-generated
- immutable
- not user-entered

### PO Number

Example:

```text
PO-000001
PO-000002
```

Rules:

- server-generated
- unique
- immutable
- concurrency-safe
- not user-overridable
- gapless numbering is NOT required

---

## 3. One Supplier per PO

### LOCKED DECISION

Each PO has exactly one Supplier.

A single PO cannot contain Lines from different Suppliers.

Reason:

- commercial clarity
- payment consistency
- logistics traceability
- future cash-flow integrity

---

## 4. Multi-Line Purchase Order

### LOCKED DECISION

A PO may contain multiple Lines.

Each Line represents one ordered Item.

Baseline Item types relevant to external procurement:

- Part
- Component

Product purchasing is not introduced here unless explicitly approved later.

---

## 5. One Currency per PO

### LOCKED DECISION

A PO has one Currency.

All Lines in the PO use the same PO Currency.

If the same Supplier sells different Items under different currencies:

```text
Create separate POs
```

Do not mix currencies inside one PO.

---

## 6. PO Line Sourcing Reference

Each PO Line should retain a reference to the selected Sourcing Option when the order originated from Sourcing.

However, historical commercial meaning must not depend only on mutable Sourcing Master.

Therefore PO must also preserve snapshots of important data.

---

## 7. Commercial Snapshot Principle

### LOCKED DECISION

PO data captures its commercial assumptions in `DRAFT`. An explicit snapshot refresh is allowed only while the PO remains `DRAFT`.

For a Draft later created by MRP-13 from an immutable Expert Decision, Package 13 applies the stricter conversion contract: Decision-derived Item, Supplier/Source, Currency, Unit Price, Ordered Qty, Payment Snapshot, Shipping Mode and Incoterm are not editable or refreshable. A different commercial basis requires a new MRP-12 Expert Decision; only Schedule planning may deviate under the locked Reason/audit rules.

At `APPROVED`, the commercial snapshot is locked. `APPROVED` remains internal only and is not Firm Supply. Returning an approved PO to `DRAFT` requires an explicit command and Reason.

At `ORDERED`, the order becomes an external commitment; core commercial, quantity and source data are permanently locked, Firm Supply begins and the Original Plan is finalized.

Changing Supplier or Sourcing Master later must not rewrite historical PO values.

Snapshot data includes at minimum:

### Header / Supplier Commercial Snapshot

- Supplier identity reference
- Currency
- Payment Term
- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Credit Anchor Event (`ORDERED` / `SHIPPED` / `WAREHOUSE_RECEIPT`)

### Line Commercial Snapshot

- Item identity reference
- Sourcing Option reference where applicable
- selected Sourcing Price row
- immutable Item Base Unit snapshot
- Ordered Qty
- Unit Price
- MOQ snapshot
- four configured lead-time stage snapshots

All Lines in one PO must have the same Supplier, Currency, Payment Snapshot, Shipping Mode and Incoterm grouping context. A mismatched Line source requires a separate PO.

---

## 8. Payment Snapshot Source

Supplier Default Payment Profile originates from MRP-03.

At Draft PO creation, relevant payment values are copied into the PO snapshot. Draft refresh is explicit; approval locks the snapshot and ordering commits it permanently.

Future Supplier Default changes must not alter old POs.

Future Package 13 may support a controlled PO-level override before/at ordering.

MRP-09 establishes the snapshot structure but does not build a complex approval flow for override.

If Credit% > 0, Credit Days > 0 and Credit Anchor Event are required. If Credit%=0, Credit Anchor Event may be null. `WAREHOUSE_RECEIPT` uses the operational Final Receipt Date derived by MRP-13; MRP-09 snapshots the event code but does not own Receipt facts.

---

## 9. PO Quantity

Ordered Qty:

- decimal/numeric
- > 0
- expressed in Item Base Unit

No supplier packaging/UOM conversion is implemented in this Package.

---

## 10. PO Line Total

### LOCKED DECISION

Line Total is Derived:

```text
Line Total
=
Ordered Qty × Unit Price Snapshot
```

Do not maintain an independently editable Line Total.

---

## 11. PO Total

### LOCKED DECISION

PO Total is Derived:

```text
PO Total
=
SUM(Line Totals)
```

Do not maintain an independently editable total that can diverge from Lines.

Tax/freight/customs/landed cost are outside MRP-09.

---

## 12. PO Statuses

### LOCKED DECISION

Baseline machine statuses:

```text
DRAFT
APPROVED
ORDERED
SHIPPED
CUSTOMS
PARTIALLY_RECEIVED
RECEIVED
CANCELLED
```

Display labels may be localized.

These are system-controlled business states, not user-configurable Reference Data.

---

## 13. PO Status Meaning

### DRAFT

Working PO, not internally approved.

### APPROVED

Internally approved by Hadron, but not yet considered supplier-committed.

### ORDERED

Supplier order has been placed / externally committed.

### SHIPPED

Shipment departed according to actual milestone data.

### CUSTOMS

Shipment is in customs-stage handling.

### PARTIALLY_RECEIVED

Some quantity has been physically received while remaining open quantity still exists.

### RECEIVED

No remaining open supply quantity exists because all non-cancelled ordered quantity has been received.

### CANCELLED

Remaining or entire order obligation has been cancelled according to allowed rules.

---

## 14. Approved Is Not Firm Supply

### LOCKED DECISION

`APPROVED` means internal approval only.

It is NOT Firm Supply.

Firm Supply starts only once order commitment reaches:

```text
ORDERED
```

or a later relevant open-supply status.

---

## 15. Firm Supply Status Rule

### LOCKED DECISION

Firm Incoming status behavior:

```text
DRAFT               → 0 Firm Supply
APPROVED            → 0 Firm Supply
ORDERED             → Remaining Open Qty is Firm
SHIPPED             → Remaining Open Qty is Firm
CUSTOMS             → Remaining Open Qty is Firm
PARTIALLY_RECEIVED  → Remaining Open Qty is Firm
RECEIVED            → 0 Firm Supply
CANCELLED           → 0 Firm Supply
```

The quantity counted as Firm Supply must be only the still-open quantity.

---

## 16. Open Quantity

### LOCKED DECISION

Conceptually:

```text
Open Qty
=
Ordered Qty
-
Received Qty
-
Cancelled Qty
```

Rules:

- Derived
- never manually typed
- must not become negative
- receipt/cancellation details must reconcile to it

Package 09 establishes this model; Package 13 will implement full operational receipt/cancel behavior.

---

## 17. Received Quantity

Received Qty is derived from actual receipt events/records.

MRP-09 may define the supply-side quantity model required for future integration.

Actual physical stock changes only through Inventory Receipt in MRP-08 when Package 13 connects the two.

MRP-09 must not independently create a second inventory balance.

---

## 18. Cancelled Quantity

Cancelled Qty represents quantity explicitly removed from the remaining supplier commitment.

It must not alter quantities already actually received.

Cancellation must remain historically traceable.

---

## 19. Partial Receipt

### LOCKED DECISION

Partial Receipt is supported.

Example:

```text
Ordered Qty: 100,000
Received:     40,000
Remaining:    60,000
```

Inventory future behavior:

```text
+40,000 actual Inventory
```

Firm Incoming future behavior:

```text
60,000 remaining Firm Supply
```

MRP-09 establishes this semantic model.

Full posting into Inventory belongs to Package 13.

---

## 20. Cancel After Partial Receipt

### LOCKED DECISION

Cancellation after partial receipt is allowed.

Example:

```text
Ordered:    100,000
Received:    40,000
Cancelled:   10,000

Open Qty:
100,000 - 40,000 - 10,000
= 50,000
```

Previous receipts remain intact.

Only remaining obligation is affected.

---

## 21. Full Cancellation Before Receipt

A PO or Line may eventually support cancellation before any receipt.

If entire remaining quantity is cancelled:

```text
Open Qty = 0
Firm Supply = 0
```

Exact status transition rules are finalized in Package 13 operations.

MRP-09 must preserve the data model.

---

## 22. Over-Receipt

### LOCKED CROSS-PACKAGE BOUNDARY

MRP-09 implements no Receipt or Over-Receipt behavior.

The project-wide MRP-13 v1 rule is already locked:

```text
Tolerance = 0
Receipt Qty > Schedule Open Qty → BLOCK
```

MRP-13 owns enforcement together with operational Receipt and Inventory posting. MRP-09 must neither implement a competing policy nor describe this as an open project question.

---

## 23. Delivery Schedule

### LOCKED DECISION

A PO Line may have one or more Delivery Schedules.

Every Line starts with:

```text
one default Schedule covering the full Ordered Qty
```

But Split Delivery is supported.

Example:

```text
PO Line Qty = 100,000

Schedule A = 40,000
Schedule B = 60,000
```

This is necessary for accurate date-based Firm Supply planning.

---

## 24. Delivery Schedule Quantity

Each Schedule has a planned quantity.

Rules:

- Qty > 0
- Item Base Unit
- before `ORDERED`, current Schedule allocations must sum exactly to Line Ordered Qty
- no ambiguous double-counting
- Planning consumes Schedule grain only

---

## 25. Split Delivery

### LOCKED DECISION

Same PO Line may arrive in multiple planned deliveries.

Example:

```text
40,000 → 1405/08/15
60,000 → 1405/09/05
```

Future Planning must therefore see:

```text
1405/08/15 +40,000
1405/09/05 +60,000
```

not a single false combined ETA.

---

## 26. Firm Supply ETA Source

### LOCKED DECISION

Firm Incoming ETA comes from:

```text
PO Delivery Schedule
```

not from the current Sourcing Master lead-time values.

Reason:

Once an order exists, the PO/Schedule has its own committed/planned timeline.

Sourcing remains the configuration source for creating plans, not the live ETA source of an existing order.

---

## 27. Delivery Schedule Milestones

Baseline milestone machine codes:

```text
SUPPLIER_READY
HANDOVER_TO_FORWARDER
SHIPPED
ARRIVED_DESTINATION
CUSTOMS_CLEARED
WAREHOUSE_RECEIPT
```

These represent major supply-chain stages.

Analytics-compatible stage boundaries are:

```text
ORDERED → SUPPLIER_READY
SUPPLIER_READY → SHIPPED
SHIPPED → ARRIVED_DESTINATION
ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
```

`HANDOVER_TO_FORWARDER` and `CUSTOMS_CLEARED` are diagnostic milestones inside these stages, not additional additive stages.

Exact display labels may be localized.

---

## 28. Ordered Date

PO Order/Commitment Date is a PO-level event/date.

It is separate from Delivery Schedule milestones.

Do not create duplicate semantically identical date fields.

---

## 29. Planned Milestone Date

Each relevant Schedule milestone may contain:

```text
Original Planned Date
Current/Revised Planned Date
```

Canonical date storage is Gregorian.

Jalali presentation may use shared date infrastructure.

---

## 30. Actual Milestone Date

Each milestone may later contain:

```text
Actual Date
```

Actual date records observed real execution.

Examples:

- actual shipped date
- actual customs cleared date
- actual warehouse receipt date

MRP-09 establishes the model.

Package 13 will implement operational lifecycle updates.

Actual milestone structure may exist in MRP-09, but Actual `WAREHOUSE_RECEIPT` must not be a free manual MRP-09 operation. It is derived from valid operational Receipt facts owned by MRP-13.

MRP-13 operational Actuals follow this Package's canonical milestone sequence, reject future dates, enforce chronology and preserve immutable originals. Corrections are append-only supersessions with mandatory Reason/audit context; MRP-09 does not authorize direct edit/delete of an Actual milestone.

---

## 31. Original vs Revised Plan

### LOCKED DECISION

Original Planned Date must be preserved.

It must never be overwritten by later ETA revision.

Conceptually:

```text
Original Planned
Current/Revised Planned
Actual
```

This supports later performance analytics.

---

## 32. ETA Revision

Current/Revised Planned Date may change when new logistics information becomes available.

Change requires:

- Reason
- actor/audit context where available
- timestamp

Do not silently overwrite the original plan.

---

## 33. Planned ETA for Firm Supply

For each Delivery Schedule, Firm Supply uses planned `WAREHOUSE_RECEIPT` dates only.

Canonical precedence:

```text
Latest Revised Planned WAREHOUSE_RECEIPT
→ Original Planned WAREHOUSE_RECEIPT
→ Missing
```

Actual milestone dates are not a Firm ETA fallback. `RequestedReceiptDate`, if retained, is a target only and is not an ETA fallback. Current Sourcing Master lead time is never an ETA fallback for an existing PO.

If required receipt ETA is missing:

```text
Firm Supply quantity may exist,
but date-based Planning readiness must surface incomplete ETA.
```

The MRP engine must not invent a date.

---

## 34. Actual Receipt and Schedule

When actual quantity is received in the future:

- received quantity must reconcile to a specific PO Line / Schedule context where possible
- actual Warehouse Receipt milestone/date is recorded
- remaining Schedule/Line quantity stays open if applicable

Package 13 finalizes exact operational receipt allocation.

---

## 35. Configured Lead-Time Snapshot

### LOCKED DECISION

At order commitment, configured Sourcing lead-time assumptions must be preserved on the order/schedule planning snapshot.

At minimum snapshot relevant configured stages:

- Supplier Lead Time: ORDERED → SUPPLIER_READY
- Forwarder / Pre-Shipment Days: SUPPLIER_READY → SHIPPED
- Shipping Period Days: SHIPPED → ARRIVED_DESTINATION
- Destination / Customs Days: ARRIVED_DESTINATION → WAREHOUSE_RECEIPT

These represent what Planning believed when the order was placed.

Original Planned Milestones must be derivable consistently from Ordered Date plus the four snapshotted non-overlapping stages. Diagnostic milestones must not increase Total Configured Lead Time.

The four configured stages derive core Original dates for:

- `SUPPLIER_READY`
- `SHIPPED`
- `ARRIVED_DESTINATION`
- `WAREHOUSE_RECEIPT`

`HANDOVER_TO_FORWARDER` and `CUSTOMS_CLEARED` remain diagnostic/non-additive. Their dates must not be invented or interpolated when no explicit operational fact or approved plan exists.

---

## 36. Sourcing Change Does Not Rewrite PO

If Sourcing later changes from:

```text
20 / 5 / 28 / 8
```

to:

```text
20 / 7 / 35 / 10
```

old PO keeps:

```text
20 / 5 / 28 / 8
```

Historical PO planning assumptions remain intact.

---

## 37. Configured vs PO Planned vs Actual

MRP-09 preserves three distinct concepts:

### Configured

Current Sourcing Master assumptions.

### PO Planned

Snapshot/original/revised dates and assumptions for this actual order.

### Actual

Observed milestone execution.

Do not merge these into one mutable lead-time field.

---

## 38. Future Lead-Time Analytics

Future Package 14 may compare:

```text
Configured
Original Plan
Revised Plan
Actual
```

and calculate:

- average delay
- median
- P80/P90
- on-time %
- sample count
- recommended lead time

MRP-09 does not calculate those analytics.

---

## 39. Payment Snapshot

At PO commitment, payment values are historical transaction facts.

Snapshot:

- Payment Term
- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Credit Anchor Event

Future Cash Flow will use these plus milestone dates/events.

Supplier Master changes must not rewrite them.

Credit Anchor validation follows MRP-03. `WAREHOUSE_RECEIPT` means operational Final Receipt Date; First Receipt and Cancellation Date are not anchors.

---

## 40. Payment Milestone Boundary

MRP-09 does not yet calculate payment cash-flow dates.

It preserves enough commercial and supply milestone data so future Cash Flow can derive obligations such as:

- advance at order
- before shipment
- credit due after agreed event

Exact Cash Flow schedule logic belongs to future integration.

---

## 41. Currency Snapshot

Currency is a PO-level snapshot/reference.

Changing Currency activation/reference labels later must not change the PO's historical currency identity.

Exchange rates are not part of this Package.

---

## 42. Unit Price Snapshot

Unit Price is snapshotted on PO Line.

Changing current Sourcing Price later must not change old PO Line Unit Price.

Future price comparison may use the Sourcing reference plus snapshot.

---

## 43. Sourcing Option Snapshot Boundary

PO keeps both:

- reference to original Sourcing Option where applicable
- transaction snapshot fields

Reason:

Reference gives traceability.

Snapshot gives historical independence.

The Line snapshot preserves:

- Item
- Sourcing Option
- selected Sourcing Price row
- Item Base Unit
- Unit Price
- MOQ
- Supplier Lead Time
- Forwarder / Pre-Shipment Days
- Shipping Period Days
- Destination / Customs Days

The PO must remain understandable even if Sourcing is later deactivated or edited.

---

## 44. Supplier Deactivation

Historical PO remains resolvable if Supplier is later inactive.

New PO creation from inactive Supplier should be blocked by later operations rules.

MRP-09 does not erase historical PO.

---

## 45. Item Deactivation

Historical PO Line remains resolvable if Item becomes inactive later.

New ordering should normally use active Items.

Exact operational block belongs to Package 13.

---

## 46. PO Header Conceptual Fields

Conceptually:

- Internal ID
- PO Number
- Supplier
- Currency
- Status
- Ordered Date where applicable
- Payment Term Snapshot
- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Notes
- Created At
- Updated At
- Approved At / Ordered At where status events require
- audit/user references where available

Exact field placement is finalized in Analysis.

---

## 47. PO Line Conceptual Fields

Conceptually:

- PO Line ID
- PO
- Item
- Sourcing Option reference
- Ordered Qty
- Unit Price Snapshot
- Derived Line Total
- Received Qty derived from future operational Receipt facts
- Cancelled Qty derived from future operational Cancellation facts
- Open Qty derived
- Notes where needed

Do not add duplicate Item name/code snapshots unless future immutable document rendering requires them and Analysis explicitly justifies it.

Stable Item reference plus transaction snapshot of commercial terms is baseline.

---

## 48. Delivery Schedule Conceptual Fields

Conceptually:

- Schedule ID
- PO Line
- Planned Qty
- Cancelled Qty derived from future operational Cancellation facts
- Received Qty derived from future operational Receipt allocation facts
- Open Qty derived
- original/revised milestone dates
- actual milestone dates
- status/readiness derived where useful

Exact schedule lifecycle is analyzed in MRP-09A.

Received, Cancelled and Open quantities must not be independently editable counters.

---

## 49. Line vs Schedule Reconciliation

### LOCKED PRINCIPLE

Schedules must not cause quantity double-counting.

For a PO Line, system must be able to reconcile:

```text
Ordered Qty
Received Qty
Cancelled Qty
Open Qty
Schedule Open Qty
```

The constraint model is locked: every Line starts fully scheduled, and pre-ORDERED allocation mutations must preserve exact equality with Line Ordered Qty in one atomic operation.

If schedules are partial or created later, Planning must never count both unscheduled line quantity and scheduled quantity twice.

---

## 50. Unscheduled Open Quantity

### LOCKED DECISION

There is no implicit unscheduled remainder in MRP-09. At Line creation:

```text
Create one default Schedule for full Line Qty at order creation.
```

Before `ORDERED`, Schedule allocations may be split/edited only in one reconciled operation such that:

```text
SUM(Current Schedule Allocations) = Line Ordered Qty
```

Planning consumes Schedule grain only and must never count Line Qty plus Schedule Qty.

---

## 51. Firm Supply Grain

Future Planning should consume Firm Supply at:

```text
Item + Expected Receipt Date + Remaining Qty
```

preferably from Delivery Schedule.

Warehouse allocation is not yet solved here.

MRP-09 must not force a destination Warehouse planning allocation unless the business process explicitly requires it later.

---

## 52. Warehouse Receipt Milestone vs Inventory Warehouse

`WAREHOUSE_RECEIPT` milestone represents expected/actual receipt timing.

It does not by itself determine which Inventory Warehouse receives stock.

Actual Inventory Receipt warehouse is selected/recorded in Package 13 when posting to MRP-08.

This keeps logistics ETA separate from warehouse allocation.

---

## 53. PO Status vs Schedule Milestones

PO Status is a summarized business lifecycle state.

Schedule milestone dates are more granular facts.

Do not infer every actual milestone solely from PO Status.

Example:

```text
PO Status = SHIPPED
```

does not mean each split schedule necessarily shipped at the same instant unless actual schedule events support it.

MRP-09A should propose status aggregation rules cautiously.

---

## 54. Status Source of Truth

PO Status belongs to Purchase Supply/Operations.

It must not be manually duplicated inside:

- Mapping
- Inventory
- Sourcing

Future planning/read models consume it.

---

## 55. Status Transition Boundary

MRP-09 establishes valid machine statuses and semantics.

MRP-09 commandable lifecycle is limited to:

```text
DRAFT
APPROVE
RETURN_TO_DRAFT
MARK_ORDERED
```

`APPROVED → DRAFT` requires an explicit Return-to-Draft command and Reason.

`SHIPPED`, `CUSTOMS`, `PARTIALLY_RECEIVED`, `RECEIVED` and `CANCELLED` remain modeled semantics for future operational facts. MRP-09 must not expose a generic `SetStatus` API.

Package 13 owns the full operational transition commands, derived-status aggregation and side effects.

MRP-09 must not invent broad workflow automation prematurely.

---

## 56. Status and Firm Supply Independence

Firm Supply logic must use both:

- PO/Schedule status eligibility
- Remaining Open Qty
- usable ETA

Status alone is insufficient.

Example:

```text
ORDERED
Open Qty = 0
```

must not create Firm Incoming.

---

## 57. Received Status

`RECEIVED` means no remaining non-cancelled open quantity.

It is not simply a manually chosen label.

Package 13 should derive/enforce this transition from receipt/cancellation facts.

MRP-09 establishes the semantic expectation.

---

## 58. Partially Received Status

`PARTIALLY_RECEIVED` means:

- Received Qty > 0
- Open Qty > 0

unless remaining open quantity has all been cancelled, in which case final status semantics are resolved in Package 13.

MRP-09 must preserve this distinction.

---

## 59. Cancelled Status with Prior Receipt

Because cancel-after-partial is allowed, a future PO/Line may have:

- actual received history
- zero remaining open qty
- cancelled remainder

Package 13 must define final header/line status aggregation cleanly.

MRP-09 must not delete receipt history.

---

## 60. Purchase Supply Read Models

MRP-09 should expose enough read-model capability for future Planning:

### Open PO Supply

- Item
- Supplier
- PO Number
- Status
- Remaining Qty
- Expected Receipt Date
- Sourcing Option
- Schedule

### Firm Incoming

- only eligible statuses
- only remaining open qty
- schedule ETA

### PO Commercial Snapshot

- Supplier
- Currency
- Unit Price
- Payment terms

MRP-09 does not calculate projected stock.

---

## 61. Purchase Supply UI

Baseline UI may include:

### PO List

- PO Number
- Supplier
- Currency
- Status
- PO Total
- Ordered Date
- Open Qty summary
- next ETA

Filters:

- Supplier
- Status
- Currency
- date range

### PO Detail

- Header
- Payment Snapshot
- Lines
- Delivery Schedules
- milestone dates
- snapshot lead-time assumptions

### Schedule Timeline

Show:

- Original Planned
- Revised Planned
- Actual

Do not turn Package 09 into final operational receiving UI.

---

## 62. Original / Revised / Actual Timeline UX

For every visible milestone:

```text
Milestone
Original Plan
Current/Revised Plan
Actual
Variance
```

Variance may be displayed as simple derived informational value if approved.

Full analytics is out of scope.

---

## 63. Permissions

Architecture should distinguish at minimum:

- View Purchase Supply
- Manage Draft/Approved Purchase Supply foundation
- Manage Schedule Plans
- Update Actual Milestones

Exact operational permissions/status-transition permissions are finalized in Package 13.

Do not build parallel Identity infrastructure.

---

## 64. Auditability

Important events must be compatible with Audit:

- PO create
- PO header/line edits while editable
- approval/order commitment
- status change
- schedule create/split/change
- original plan creation
- revised ETA change + Reason
- actual milestone entry
- cancellation foundation
- receipt foundation

Snapshots and milestone history must remain structurally traceable even if generic audit logs are unavailable.

---

## 65. Revised ETA History

### LOCKED PRINCIPLE

Original Planned Date is immutable historical baseline.

If Revised Planned Date changes multiple times, generic audit may be enough for v1 if it preserves:

- previous value
- new value
- reason
- actor
- timestamp

Because the actual Repository currently registers a no-op Audit sink, MRP-09 must preserve Revised ETA history durably in its domain persistence rather than relying on an unavailable generic Audit store. The smallest relational history model is sufficient; no generic workflow/audit platform is introduced.

---

## 66. Date Rules

Canonical storage:

- Gregorian dates/timestamps
- Asia/Tehran business context where applicable

Jalali display/input may use shared date infrastructure.

Do not store milestone logic only as formatted Persian date strings.

---

## 67. Numeric Precision

Use decimal/numeric for:

- Ordered Qty
- Schedule Qty
- Received Qty
- Cancelled Qty
- Unit Price
- Line Total
- PO Total

Do not use binary floating point.

---

## 68. Database Integrity

Expected protections where practical:

- PO Number uniqueness
- one Supplier per PO
- one Currency per PO
- positive Ordered Qty
- positive Schedule Qty
- non-negative received/cancelled/open logic
- valid line references
- valid schedule reconciliation
- immutable snapshots after commitment according to approved state
- historical FK safety
- no destructive cascades

Exact database strategy is proposed in MRP-09A.

---

## 69. Concurrency

The PO aggregate has exactly one optimistic concurrency token:

```text
PurchaseOrder.Version : bigint
```

PO Lines and Delivery Schedules do not have independent mutable Version tokens. Every aggregate mutation submits `ExpectedVersion`, successful mutation advances `PurchaseOrder.Version`, and stale mutation returns structured `409 / STALE_VERSION` without partial change.

Concurrency-sensitive operations include:

- PO numbering
- order commitment
- schedule splitting
- schedule quantity reconciliation
- receipt/cancel quantity foundations
- ETA updates

Use the smallest robust PostgreSQL/EF Core transaction and row-locking approach required to keep aggregate and Schedule reconciliation atomic.

Full receipt concurrency with Inventory is finalized in Package 13.

---

## 70. Snapshot Immutability Boundary

`DRAFT` values are editable and may use an explicit snapshot refresh.

At `APPROVED`, commercial snapshot fields are locked. Return to `DRAFT` requires an explicit command and Reason.

At `ORDERED`, core commercial, quantity and source data become permanently locked; Firm Supply begins and Original Planned Milestones are finalized.

Any future exceptional committed correction requires a separately approved controlled operation and is not normal MRP-09 editing.

The business principle is locked:

> historical committed order assumptions must not drift with Master Data.

---

## 71. Firm Supply Calculation Boundary

MRP-09 should provide deterministic logic/read model conceptually:

```text
GetFirmIncoming(item, date range)
```

using:

- eligible status
- remaining schedule/line open qty
- schedule expected receipt date

It must not calculate:

- Projected On Hand
- Coverage
- Order Recommendation

Those belong to later Planning packages.

---

## 72. Missing ETA

A Firm-eligible order with open qty but no usable schedule ETA is not silently placed on an invented date.

It should surface an explicit incomplete supply-planning condition.

Future Planning may show:

```text
Firm Qty exists
ETA missing
```

rather than guessing from current Sourcing Lead Time.

---

## 73. No Master Lead-Time Fallback for Existing PO ETA

### LOCKED DECISION

Once an existing PO/Schedule is Firm Supply, its expected receipt date must come from PO planning data.

Do not silently recalculate its ETA from the latest Sourcing Master configuration.

If PO ETA is missing, surface missing data.

---

## 74. Seed / Migration

Codex must not invent real Purchase Orders.

If legacy PO data is not explicitly supplied:

- implement capability
- do not fabricate historical orders

Legacy PO migration will be a separate controlled task.

---

## 75. Explicit Non-Scope

MRP-09 must NOT implement:

- Recommendation→PO conversion
- final Purchase Order operational workflow
- supplier communication
- document PDF/email sending
- PO approval engine beyond status foundation
- Inventory posting
- final Receipt orchestration
- Over-Receipt policy
- Warehouse destination allocation
- projected inventory
- Coverage
- MRP Calculation Engine
- Order Planning recommendation
- Cash Flow calculation
- payment execution
- exchange rates
- landed cost
- freight/customs costing
- Lead-Time Analytics
- automatic ETA prediction
- full logistics module
- Workflow Engine beyond minimal Purchase Supply status foundation

---

## 76. Acceptance Criteria

MRP-09 is acceptable when:

1. PO has Internal ID + server-generated immutable PO Number.
2. One Supplier per PO.
3. Multi-Line PO supported.
4. One Currency per PO.
5. PO Line may reference Sourcing Option.
6. Commercial snapshots are preserved.
7. Supplier Master/Sourcing changes do not rewrite historical PO.
8. Ordered Qty is decimal and > 0.
9. Status set is exactly the approved baseline.
10. APPROVED is not Firm Supply.
11. Firm Supply begins from ORDERED.
12. Firm Supply uses Remaining Open Qty only.
13. Open Qty is Derived.
14. Partial Receipt semantics are supported.
15. Cancel after Partial Receipt is supported.
16. Prior receipts remain untouched by cancellation.
17. Split Delivery / multiple Schedules per Line supported.
18. Firm ETA comes from Delivery Schedule.
19. No current Sourcing Lead-Time fallback silently rewrites existing PO ETA.
20. Configured lead-time assumptions are snapshotted.
21. Planned and Actual milestones are separated.
22. Original Planned Date is preserved.
23. Revised ETA change requires traceability.
24. Actual milestone structure exists; Actual WAREHOUSE_RECEIPT is not a free manual MRP-09 operation.
25. Line/PO totals are Derived.
26. Schedule quantities cannot double-count ordered supply.
27. Missing ETA remains explicit.
28. Historical Supplier/Item/Sourcing remain resolvable.
29. No Inventory posting occurs in this Package.
30. MRP-09 implements no Receipt/Over-Receipt behavior; MRP-13 enforces the locked v1 zero-tolerance rule.
31. No Projected Inventory/Coverage logic leaks into MRP-09.
32. `PurchaseOrder.Version` is the only aggregate concurrency token.
33. APPROVED locks the commercial snapshot and Return-to-Draft requires Reason.
34. Firm ETA precedence is Latest Revised Planned WAREHOUSE_RECEIPT → Original Planned WAREHOUSE_RECEIPT → Missing.
35. RequestedReceiptDate and Actual dates are not Firm ETA fallbacks.
36. Every Line starts with a default Schedule covering full Ordered Qty.
37. Planning consumes Schedule grain only.
38. Received/Cancelled/Open quantities are derived from operational facts and are not editable counters.

---

## 77. Locked Decisions

The following are locked for MRP-09:

1. Internal PO ID + auto PO Number.
2. PO Number server-generated, immutable, unique, concurrency-safe.
3. One Supplier per PO.
4. Multi-Line PO.
5. One Currency per PO.
6. Each Line may reference selected Sourcing Option.
7. Commercial values snapshot onto PO/Lines.
8. Supplier/Currency/Unit Price/Payment terms are historically preserved.
9. Ordered Qty > 0 in Item Base Unit.
10. Statuses = Draft / Approved / Ordered / Shipped / Customs / Partially Received / Received / Cancelled.
11. Approved = internal approval only.
12. Approved is not Firm Supply.
13. Firm statuses = Ordered / Shipped / Customs / Partially Received.
14. Firm Supply = Remaining Open Qty only.
15. Open Qty = Ordered - Received - Cancelled.
16. Partial Receipt supported.
17. Cancel after Partial Receipt supported.
18. Cancellation affects remaining obligation only.
19. PO Line may have multiple Delivery Schedules.
20. Every Line starts with one default Schedule covering the full Ordered Qty; pre-ORDERED allocations reconcile exactly.
21. Firm ETA source = Latest Revised Planned WAREHOUSE_RECEIPT, then Original Planned WAREHOUSE_RECEIPT, else Missing.
22. Configured Sourcing lead times snapshot at order commitment.
23. PO timeline separates Configured / Original Plan / Revised Plan / Actual.
24. Original Planned Date never overwritten.
25. Revised ETA requires Reason/Audit.
26. Baseline milestones = Supplier Ready / Handover / Shipped / Arrived / Customs Cleared / Warehouse Receipt.
27. PO Line Total and PO Total are Derived.
28. Existing PO ETA must not silently follow later Sourcing Master changes.
29. Package 09 builds Purchase Supply foundation only.
30. Full PO Operations and Inventory receipt integration belong to Package 13.
31. MRP-09 implements no Receipt/Over-Receipt behavior; MRP-13 v1 tolerance is locked at zero.
32. `PurchaseOrder.Version` is the only aggregate concurrency token; Lines/Schedules have no independent mutable Version.
33. DRAFT is editable with explicit snapshot refresh; APPROVED locks the commercial snapshot; Return-to-Draft requires Reason; ORDERED permanently locks core commercial/quantity/source data and finalizes Original Plan.
34. MRP-09 commandable lifecycle is limited to Draft/Approve/Return-to-Draft/Mark-Ordered; no generic SetStatus API exists.
35. Actual dates and RequestedReceiptDate are not Firm ETA fallbacks.
36. Diagnostic Handover/Customs dates are not invented or interpolated.
37. Multi-Line grouping requires identical Supplier/Currency/Payment Snapshot/Shipping Mode/Incoterm.
38. Received/Cancelled/Open quantities remain derived operational facts, never editable counters.

## 77A. MRP-14 Multi-Schedule Financial Anchor Contract

### LOCKED CROSS-PACKAGE DECISION

PO payment percentages create PO-level commitment components. When an event anchor is represented by multiple Delivery Schedules:

- each commercially required Schedule resolves its event date independently using `Actual → Latest Revised → Original → Unresolved`;
- fully cancelled Schedules with no remaining commercial obligation are excluded from the required set;
- the PO-level event date is the maximum resolved date across all required Schedules;
- if any required Schedule is unresolved, the PO-level anchor is unresolved;
- `ORDERED` continues to use the PO Ordered Date;
- actual `WAREHOUSE_RECEIPT` continues to use MRP-13 operational Final Receipt Date when PO completion exists;
- no current Sourcing date, RequestedReceiptDate or manually editable financial date is a fallback.

---

## 78. Open Question Resolution

The previous open question:

`Firm Supply ETA Source`

is now CLOSED.

Approved answer:

```text
Firm Supply ETA comes from the PO Delivery Schedule using:
Latest Revised Planned WAREHOUSE_RECEIPT
→ Original Planned WAREHOUSE_RECEIPT
→ Missing.
Actual dates and RequestedReceiptDate are not Firm ETA fallbacks.
Existing PO ETA must not be silently recalculated from current Sourcing Lead Time.
```

The previous open question:

`Cancel after Partial Receipt`

is now CLOSED.

Approved answer:

```text
Allowed.
Only the remaining open obligation is cancelled.
Previously received quantity remains historical and unchanged.
```

---

## 79. Remaining Relevant Deferred Operations

Deferred to Package 13, but not open business blockers for MRP-09:

- enforcement of the locked Over-Receipt tolerance-zero rule during Receipt posting
- exact final status aggregation after mixed receipt + cancellation
- operational receipt allocation across split schedules
- destination Inventory Warehouse during receipt
- final status transition commands / permissions
- Recommendation→Draft PO orchestration

None blocks the MRP-09 Purchase Supply foundation.

---

# B. Prompt 09A — Purchase Supply Foundation Analysis

```text
# MRP-09A — Purchase Supply Foundation Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior Packages were implemented merely because they were planned.

TASK:
Analyze MRP-09 Purchase Supply Foundation.

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

1. Understand Purchase Supply role between Sourcing, Planning and future PO Operations.
2. Inspect actual Repository and reuse existing Item/Supplier/Sourcing/Inventory patterns.
3. Propose clean relational model for:
   - PO Header
   - PO Line
   - Delivery Schedule
   - Milestone planned/actual data
   Avoid EAV.
4. PO identity/numbering.
5. One Supplier per PO.
6. Multi-Line PO.
7. One Currency per PO.
8. Sourcing Option reference + historical snapshot design.
9. Commercial snapshot:
   - Supplier
   - Currency
   - Unit Price
   - Payment Term
   - Advance %
   - Before Shipment %
   - Credit %
   - Credit Days
   - Credit Anchor Event codes and conditional validation; confirm WAREHOUSE_RECEIPT uses operational Final Receipt Date without moving Receipt ownership from MRP-13
10. Verify the locked snapshot/lifecycle boundary:
    - DRAFT editable with explicit snapshot refresh
    - APPROVED commercial snapshot locked; Return-to-Draft requires Reason; not Firm
    - ORDERED permanent core commercial/quantity/source lock; Firm begins; Original Plan finalized
11. Ordered Qty / derived Line Total / PO Total.
12. Status model and machine semantics:
    DRAFT / APPROVED / ORDERED / SHIPPED / CUSTOMS /
    PARTIALLY_RECEIVED / RECEIVED / CANCELLED
13. Confirm APPROVED is internal only and not Firm Supply.
14. Firm Supply formula based on status + Remaining Open Qty.
15. Open Qty = Ordered - Received - Cancelled.
16. Partial Receipt semantics.
17. Cancel-after-partial semantics.
18. Delivery Schedule model:
    - default schedule
    - split deliveries
    - quantity reconciliation
19. Propose deterministic method to prevent double-counting between Line Qty and Schedule Qty.
20. Verify every Line receives one default Schedule covering full Ordered Qty and pre-ORDERED allocations reconcile exactly.
21. Firm ETA source = Latest Revised Planned WAREHOUSE_RECEIPT, then Original Planned WAREHOUSE_RECEIPT, else Missing. Actual and RequestedReceiptDate are not fallbacks.
22. Missing ETA behavior.
23. Configured Sourcing Lead-Time snapshot.
24. Planned vs Actual milestone model.
    Confirm four continuous stage boundaries and that HANDOVER_TO_FORWARDER/CUSTOMS_CLEARED are diagnostic only.
    - ORDERED → SUPPLIER_READY
    - SUPPLIER_READY → SHIPPED
    - SHIPPED → ARRIVED_DESTINATION
    - ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
25. Baseline milestones:
    SUPPLIER_READY
    HANDOVER_TO_FORWARDER
    SHIPPED
    ARRIVED_DESTINATION
    CUSTOMS_CLEARED
    WAREHOUSE_RECEIPT
    Confirm Original Plan can be generated from Ordered Date + four snapshotted stage durations without gap, overlap or diagnostic double counting.
26. Original Planned vs Revised Planned vs Actual.
27. Revised ETA audit model.
28. Do NOT silently recalculate existing PO ETA from current Sourcing Master.
29. Analyze status aggregation vs split Schedule milestones.
30. Preserve foundation for future receipt allocation without implementing Inventory posting.
31. Confirm MRP-09 implements no Receipt/Over-Receipt behavior and does not conflict with the locked MRP-13 zero-tolerance rule.
32. Historical inactive Item/Supplier/Sourcing behavior.
33. Read models:
    - open PO supply
    - Firm Incoming
    - next ETA
    - commercial snapshot
34. API/application use cases.
35. UI:
    - PO list
    - PO detail
    - lines
    - delivery schedules
    - timeline
36. Permissions/Audit.
37. DB constraints/indexes.
38. Numeric precision/date handling.
39. Performance.
40. Concurrency: verify `PurchaseOrder.Version` is the only aggregate token and Line/Schedule mutations use the PO ExpectedVersion.
41. Testing plan.
42. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..09 and actual Repository.
43. Smallest implementation sequence.
44. Explicit non-scope.

Testing proposal must cover at minimum:
- PO Number uniqueness/concurrency
- one Supplier per PO
- multi-Line PO
- one Currency per PO
- snapshot does not change when Supplier/Sourcing changes
- Ordered Qty > 0
- derived Line Total / PO Total
- status integrity
- Approved not Firm
- Ordered/Shipped/Customs/PartiallyReceived Firm
- Received/Cancelled not Firm
- Open Qty derivation
- partial receipt quantity model
- cancel after partial
- multiple Schedules
- split schedule quantity reconciliation
- no schedule double counting
- missing ETA is explicit
- Delivery Schedule ETA used instead of Sourcing lead time
- configured lead-time snapshot preserved
- Original Planned immutable
- Revised ETA traceable
- Actual milestone separate
- inactive historical references resolve
- no Inventory posting
- no Receipt/Over-Receipt behavior in MRP-09; locked MRP-13 zero-tolerance policy is not contradicted
- no Projected Inventory/Coverage

Output:
1. Understanding
2. Repository Findings
3. PO / Line Data Model
4. Snapshot Model
5. Status / Firm Supply Model
6. Delivery Schedule Model
7. Milestone / ETA Model
8. Quantity Reconciliation
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

# C. Prompt 09B — Purchase Supply Foundation Implementation

```text
# MRP-09B — Implement Purchase Supply Foundation

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-09A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-09 Purchase Supply Foundation.

If MRP-09A contains unresolved material contradictions, STOP.

PO IDENTITY
Implement:
- Internal ID
- server-generated immutable PO Number
- concurrency-safe numbering
- no client override

PO HEADER
Implement approved:
- one Supplier
- one Currency
- status
- payment snapshot
- ordered/approval dates as approved
- notes/timestamps
- exactly one aggregate concurrency token: `PurchaseOrder.Version : bigint`

Every Header/Line/Schedule mutation must submit PO `ExpectedVersion`. Lines and Schedules must not have independent mutable Version tokens. Success advances PurchaseOrder.Version; stale mutation returns structured `409 / STALE_VERSION` with no partial change.

PO LINE
Implement:
- Item
- Sourcing Option reference where applicable
- selected Sourcing Price row
- immutable Item Base Unit snapshot
- Ordered Qty
- Unit Price Snapshot
- MOQ snapshot
- four configured lead-time stage snapshots
- derived Line Total
- received/cancel/open quantities derived from future operational facts according to the approved model

Do not add UOM conversion.

Do not create independently editable Received/Cancelled/Open counters.

MULTI-LINE GROUPING
Every Line in one PO must have identical Supplier, Currency, Payment Snapshot, Shipping Mode and Incoterm grouping context. A mismatched Line source requires a separate PO.

SNAPSHOTS
Preserve approved transaction snapshots so later:
- Supplier change
- Sourcing change
- Price change
- Payment default change
do NOT rewrite committed historical PO values.

Lifecycle boundary:
- DRAFT = editable; explicit snapshot refresh allowed
- APPROVED = commercial snapshot locked; internal only; Return-to-Draft requires Reason
- ORDERED = external commitment; core commercial/quantity/source data permanently locked; Firm begins; Original Plan finalized

Payment Snapshot includes Credit Anchor Event. Enforce Credit%>0 → CreditDays>0 + Anchor required; Credit%=0 → Anchor may be null. For WAREHOUSE_RECEIPT, consume MRP-13 operational Final Receipt Date and never First Receipt or Cancellation Date.

STATUS
Stable machine states:
- DRAFT
- APPROVED
- ORDERED
- SHIPPED
- CUSTOMS
- PARTIALLY_RECEIVED
- RECEIVED
- CANCELLED

Do not make statuses user-configurable.

MRP-09 commandable lifecycle is limited to Draft, Approve, Return-to-Draft and Mark-Ordered. Do not expose a generic SetStatus API. SHIPPED/CUSTOMS/PARTIALLY_RECEIVED/RECEIVED/CANCELLED are modeled for future operational facts; Package 13 owns full transition side effects.

FIRM SUPPLY
Implement approved deterministic read/domain logic:
- DRAFT = no Firm
- APPROVED = no Firm
- ORDERED = remaining open qty Firm
- SHIPPED = remaining open qty Firm
- CUSTOMS = remaining open qty Firm
- PARTIALLY_RECEIVED = remaining open qty Firm
- RECEIVED = no Firm
- CANCELLED = no Firm

OPEN QTY
Derive:
Ordered - Received - Cancelled

Do not store independently editable Open Qty.

PARTIAL / CANCELLATION FOUNDATION
Support approved data semantics for:
- partial receipt
- cancellation of remaining qty after partial receipt

Do NOT post Inventory yet.

Do not implement Receipt/Over-Receipt behavior. Do not contradict the locked MRP-13 v1 rule: tolerance is zero and Receipt Qty greater than Schedule Open Qty is blocked.

DELIVERY SCHEDULE
Implement approved:
- one or more schedules per PO Line
- one default Schedule covering full Ordered Qty at Line creation
- schedule planned qty
- split delivery
- exact pre-ORDERED quantity reconciliation to Line OrderedQty
- no double counting

Planning consumes Schedule grain only, never Line Qty plus Schedule Qty.

FIRM ETA
Use only:
- Latest Revised Planned WAREHOUSE_RECEIPT
- otherwise Original Planned WAREHOUSE_RECEIPT
- otherwise explicit Missing

Actual dates and RequestedReceiptDate are not Firm ETA fallbacks.

Do NOT silently use current Sourcing Master lead-time configuration as ETA for an existing PO.

MILESTONES
Implement approved baseline milestones:
- SUPPLIER_READY
- HANDOVER_TO_FORWARDER
- SHIPPED
- ARRIVED_DESTINATION
- CUSTOMS_CLEARED
- WAREHOUSE_RECEIPT

Stage boundaries:
- ORDERED → SUPPLIER_READY
- SUPPLIER_READY → SHIPPED
- SHIPPED → ARRIVED_DESTINATION
- ARRIVED_DESTINATION → WAREHOUSE_RECEIPT

HANDOVER_TO_FORWARDER and CUSTOMS_CLEARED are diagnostic and must not be added to Total Configured Lead Time.

Generate core Original dates only for SUPPLIER_READY, SHIPPED, ARRIVED_DESTINATION and WAREHOUSE_RECEIPT from the four configured stages. Do not invent or interpolate diagnostic milestone dates.

For each approved milestone structure support:
- Original Planned Date
- Current/Revised Planned Date
- Actual Date

ORIGINAL PLAN
Original Planned Date is immutable after commitment according to approved lifecycle.

REVISED ETA
Changes require approved Reason/Audit handling.

ACTUAL MILESTONES
Store/foundation only.
Do not implement full logistics automation.
Actual WAREHOUSE_RECEIPT must not be a free manual MRP-09 operation; MRP-13 Receipt facts own it.

CONFIGURED LEAD-TIME SNAPSHOT
Preserve the approved Sourcing configured stage assumptions on the order/schedule snapshot.
Ensure Original Planned Milestones derive consistently from Ordered Date plus exactly four non-overlapping stage durations.

TOTALS
- Line Total derived
- PO Total derived
- no independent editable totals

READ MODELS
Implement approved:
- PO list/detail
- open supply
- Firm Incoming
- schedule ETA
- commercial snapshot

Do NOT implement Projected On Hand or Coverage.

BACKEND
Implement only approved:
- domain/data model
- EF Core config
- migrations
- application use cases/services
- API
- validation
- errors

FRONTEND
Implement approved foundation UI:
- PO list
- filters
- PO detail
- Lines
- Delivery Schedules
- milestone timeline
- snapshot display

Do NOT build final receiving screen or Recommendation→PO flow.

Follow UI-BRAND-RULES.md.
Reuse approved patterns.

REFERENCE DATA INTEGRITY GATE
- Currency and Unit remain MRP-01 typed sources of truth.
- PAYMENT_TERM, SHIPPING_MODE and INCOTERM remain MRP-01 ReferenceValue ownership.
- Require active references for new assignment/commitment while preserving historical inactive resolution.
- Display `fa-IR` with `NameFa`; display `en` with non-empty `NameEn`, otherwise `NameFa` fallback.
- Do not create shadow reference masters or alter stored localized values.

PERMISSIONS/AUDIT
Reuse existing platform.
Do not build parallel Identity/Audit infrastructure.

TESTS
At minimum:
- PO numbering concurrency
- one Supplier
- one Currency
- multiple Lines
- snapshot immutability
- PurchaseOrder-only aggregate Version and stale Line/Schedule mutations
- Draft refresh / Approved lock / Return-to-Draft Reason / Ordered permanent lock
- grouping compatibility across Supplier/Currency/Payment Snapshot/Shipping Mode/Incoterm
- Line snapshot preserves selected Price row, BaseUnit, Unit Price, MOQ and all four configured lead stages
- Ordered Qty validation
- totals derived
- status semantics
- Firm Supply status rules
- Open Qty
- partial receipt model
- cancel after partial
- split schedules
- default Schedule equals full OrderedQty
- schedule quantity reconciliation
- no double counting
- schedule ETA source
- Revised → Original → Missing Firm ETA precedence
- Actual/RequestedReceiptDate excluded as Firm ETA fallback
- missing ETA explicit
- Sourcing lead-time change does not rewrite PO
- Original Plan immutability
- Revised ETA audit boundary
- Actual date separate
- diagnostic milestone dates are not invented/interpolated
- Actual WAREHOUSE_RECEIPT has no free manual MRP-09 command
- Received/Cancelled/Open are derived and have no independently editable counters
- historical inactive references
- no Inventory posting
- no Receipt/Over-Receipt behavior and no conflict with MRP-13 tolerance zero
- no Projected Inventory/Coverage leakage
- Currency/Unit typed ownership and PAYMENT_TERM/SHIPPING_MODE/INCOTERM ownership
- active assignment plus historical inactive reference resolution
- FA/EN reference display fallback and no shadow masters

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
- Recommendation→PO conversion
- final PO operational workflow
- Inventory Receipt posting
- Receipt/Over-Receipt implementation (MRP-13 applies the already locked zero-tolerance rule)
- warehouse destination allocation
- projected inventory
- Coverage
- MRP calculation
- Order Planning
- Cash Flow
- payment execution
- exchange rates
- landed cost
- Lead-Time Analytics
- automatic ETA prediction
- full logistics module

Final report:
- Changed
- Database
- PO Header / Lines
- Snapshots
- Status / Firm Supply
- Delivery Schedules
- Milestones / ETA
- Read Models
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-10.
```

---

# D. Prompt 09C — Purchase Supply Foundation Independent Audit

```text
# MRP-09C — Purchase Supply Foundation Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-09A Analysis
- MRP-09B implementation report

Inspect all MRP-09 implementation files.

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
   - Recommendation→PO
   - Inventory posting
   - Receipt/Over-Receipt behavior; confirm no contradiction with MRP-13 tolerance zero
   - Projected Inventory
   - Coverage
   - Cash Flow
   - Lead-Time Analytics
   - full logistics workflow

2. Identity
   - Internal ID
   - auto PO Number
   - immutable
   - concurrency-safe
   - not client supplied

3. Header
   - one Supplier
   - one Currency
   - status integrity
   - payment snapshot
   - Credit Anchor Event preserved and conditionally valid
   - multi-Line grouping compatibility: same Supplier/Currency/Payment Snapshot/Shipping Mode/Incoterm

4. Lines
   - multi-Line
   - Item
   - Sourcing Option trace
   - Ordered Qty
   - Unit Price snapshot
   - selected Price row, BaseUnit, MOQ and four configured lead-stage snapshots
   - totals derived

5. Snapshot Integrity
   Verify historical PO values do not drift when:
   - Supplier defaults change
   - Sourcing Price changes
   - Sourcing Lead Time changes
   - reference display labels change
   - Credit Anchor remains historical and does not drift

   Verify WAREHOUSE_RECEIPT resolves to MRP-13 operational Final Receipt Date and no duplicate financial receipt date exists.

   Verify configured/planned stage boundaries match MRP-05/MRP-14 and diagnostic milestones are never double-counted.
   - ORDERED → SUPPLIER_READY
   - SUPPLIER_READY → SHIPPED
   - SHIPPED → ARRIVED_DESTINATION
   - ARRIVED_DESTINATION → WAREHOUSE_RECEIPT

6. Statuses
   Verify exactly:
   - DRAFT
   - APPROVED
   - ORDERED
   - SHIPPED
   - CUSTOMS
   - PARTIALLY_RECEIVED
   - RECEIVED
   - CANCELLED

   Verify MRP-09 commandable lifecycle is only Draft/Approve/Return-to-Draft/Mark-Ordered, Return-to-Draft requires Reason, and no generic SetStatus API exists. Operational later statuses are modeled but their full side effects remain MRP-13-owned.

7. Firm Supply
   - Approved not Firm
   - Ordered/Shipped/Customs/PartiallyReceived remaining qty Firm
   - Received/Cancelled not Firm
   - Open Qty required
   - zero-open qty never counted

8. Open Qty
   - Derived
   - Ordered - Received - Cancelled
   - non-negative

9. Partial Receipt / Cancellation
   - partial semantics preserved
   - cancel-after-partial preserves received history
   - no independently editable Received/Cancelled/Open counters
   - no Receipt/Over-Receipt behavior implemented in MRP-09

10. Delivery Schedules
   - multiple schedules
   - every Line starts with one default Schedule covering full Ordered Qty
   - exact pre-ORDERED allocation reconciliation
   - split delivery
   - qty reconciliation
   - no double counting

11. Firm ETA
   - source is Latest Revised Planned WAREHOUSE_RECEIPT, then Original Planned WAREHOUSE_RECEIPT, else Missing
   - Actual dates are not fallback
   - RequestedReceiptDate is target-only and not fallback
   - no silent fallback to current Sourcing Lead Time
   - missing ETA explicit

12. Lead-Time Snapshot
   - configured stage assumptions preserved historically

13. Milestones
   - approved milestone set
   - Original Planned
   - Revised Planned
   - Actual
   - fields semantically distinct
   - core Original dates derive only from the four configured non-overlapping stages
   - diagnostic Handover/Customs dates are not invented/interpolated
   - Actual WAREHOUSE_RECEIPT is not a free manual MRP-09 operation

14. Original Plan
   - not overwritten

15. Revised ETA
   - traceable
   - Reason/audit boundary correct

16. Historical Integrity
   - inactive Item/Supplier/Sourcing remains resolvable
   - no destructive cascades

17. Database Integrity
   - constraints
   - indexes
   - schedule quantity integrity
   - numeric precision
   - migration safety
   - concurrency
   - `PurchaseOrder.Version` is the only aggregate concurrency token
   - Line/Schedule mutations require PO ExpectedVersion and do not have independent mutable Versions

18. API
   - safe use-case boundaries
   - server validation
   - no dangerous unrestricted generic CRUD

19. UI
   - Brand Rules
   - RTL
   - PO list/detail
   - Lines/Schedules
   - timeline clarity
   - snapshot clarity
   - no receiving/Planning scope leakage

20. Permissions / Auditability

21. Performance
   - Firm Incoming lookup
   - next ETA
   - PO line/schedule loading
   - no N+1
   - no unnecessary analytics/caching

22. Tests
   Verify meaningful assertions especially:
   - snapshot immutability
   - Firm Supply semantics
   - schedule double-counting prevention
   - Original vs Revised ETA
   - PO numbering concurrency
   - Draft refresh / Approved snapshot lock / Return-to-Draft Reason / Ordered permanent lock
   - grouping compatibility
   - PurchaseOrder-only Version and stale Line/Schedule conflict
   - Revised → Original → Missing Firm ETA precedence
   - Actual/RequestedReceiptDate exclusion from Firm ETA
   - selected Price/BaseUnit/UnitPrice/MOQ/four-stage Line snapshot preservation
   - default Schedule full coverage and exact pre-ORDERED reconciliation
   - Received/Cancelled/Open quantities are derived with no editable counters
   - diagnostic milestone dates are not invented/interpolated
   - Actual WAREHOUSE_RECEIPT has no free manual MRP-09 operation
   - no Receipt/Over-Receipt behavior and no contradiction with MRP-13 tolerance zero

23. Reference Data Integrity Gate
   Verify:
   - Currency and Unit remain MRP-01 typed sources of truth
   - PAYMENT_TERM, SHIPPING_MODE and INCOTERM remain MRP-01 ReferenceValue ownership
   - active references are required for new assignment/commitment
   - historical inactive references remain resolvable
   - `fa-IR` displays `NameFa`
   - `en` displays non-empty `NameEn`, otherwise falls back to `NameFa`
   - no shadow Currency/Unit/PaymentTerm/ShippingMode/Incoterm master exists

24. Specification Drift
   Compare:
   - Master Spec
   - Decisions
   - MRP-01..09
   - approved MRP-09A
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

# E. Final Pre-Codex Audit — MRP-09

## Result

`PASS`

## Audited Approved Decisions

تمام تصمیم‌های تأییدشده Package 09 پوشش داده شده‌اند:

1. Internal PO ID + auto PO Number.
2. One Supplier per PO.
3. Multi-Line PO.
4. One Currency per PO.
5. Sourcing Option reference per Line.
6. Commercial snapshot preservation.
7. Ordered Qty in Item Base Unit and > 0.
8. Approved status set.
9. Approved = internal approval only.
10. Firm Supply starts at Ordered.
11. Open Qty derived.
12. Partial Receipt supported.
13. Cancel after Partial Receipt supported.
14. Split Delivery / Delivery Schedule supported.
15. Firm ETA comes from PO Delivery Schedule.
16. Configured Sourcing lead times snapshot.
17. Planned and Actual milestones separated.
18. Original Planned vs Revised Planned preserved.
19. Line Total / PO Total derived.
20. Package 09 remains foundation; Package 13 owns full operations/inventory integration.
21. PurchaseOrder.Version is the only aggregate concurrency token.
22. Draft refresh, Approved lock, Reasoned Return-to-Draft and permanent Ordered lock are explicit.
23. MRP-09 commandable lifecycle is limited to Draft/Approve/Return-to-Draft/Mark-Ordered.
24. Firm ETA precedence is Revised Planned → Original Planned → Missing; Actual/RequestedReceiptDate are excluded.
25. Every Line starts with a full-quantity default Schedule and Planning consumes Schedule grain only.
26. Multi-Line grouping requires identical Supplier/Currency/Payment Snapshot/Shipping Mode/Incoterm.
27. Line snapshot includes selected Price/BaseUnit/UnitPrice/MOQ/four lead stages.
28. Diagnostic milestone dates are not invented or interpolated.
29. Actual WAREHOUSE_RECEIPT is not manually owned by MRP-09.
30. Received/Cancelled/Open remain derived operational facts, not editable counters.

## Open Question Resolution

Closed:

### Firm Supply ETA Source

```text
PO Delivery Schedule
```

not current Sourcing Master Lead Time.

### Cancellation after Partial Receipt

```text
Allowed.
Only remaining open obligation is cancelled.
Historical receipts remain intact.
```

Closed project-wide for Package 13:

### Over-Receipt

MRP-09 implements no Receipt/Over-Receipt behavior. MRP-13 owns enforcement of the locked v1 zero-tolerance rule.

---

## Cross-Package Consistency

- MRP-03 Supplier owns current defaults; PO snapshots them historically.
- MRP-05 Sourcing owns current Price/Lead-Time configuration; PO snapshots selected commercial/planning assumptions.
- MRP-08 Inventory remains the only Actual On Hand source of truth.
- MRP-09 does not post stock.
- Future MRP-10 consumes Firm Incoming from PO Schedule data.
- Future MRP-13 connects actual receipts to Inventory.
- Future MRP-14 can compare Configured / Original Plan / Revised Plan / Actual milestones.
- Cash Flow can later use Payment Snapshot plus milestone dates without mutating Supplier defaults.

No material ownership conflict was found.

## Architecture Corrections Applied

- PO Firm ETA is separated from mutable Sourcing Master lead time.
- Split Delivery is modeled explicitly to prevent false single-date supply.
- Sourcing reference and PO transaction snapshot coexist.
- Commercial history is protected from Master Data drift.
- Open Qty is derived rather than manually edited.
- Firm Supply uses both eligible status and remaining quantity.
- Original Planned Date is immutable.
- Revised Planned Date remains auditable.
- Actual milestone is kept distinct from plan.
- Existing PO with missing ETA remains explicitly incomplete rather than guessed.
- Warehouse destination allocation is kept outside this Package.
- Firm ETA uses Revised Planned, then Original Planned, then Missing; Actual and Requested Receipt target are not fallbacks.
- Over-Receipt is not implemented in MRP-09; MRP-13 applies the locked zero-tolerance rule.

## Remaining Deferred Operations

For Package 13:

- operational enforcement of the locked Over-Receipt tolerance-zero rule.
- final mixed Receipt + Cancellation status aggregation.
- operational allocation of Receipt to split schedules.
- destination Warehouse selection.
- Recommendation→Draft PO conversion.
- final PO status-transition permissions and side effects.

None blocks the MRP-09 foundation.

## Prompt Audit

### MRP-09A
PASS

Analysis-only, Repository-aware, and specifically requires review of snapshots, schedule reconciliation, Firm Supply semantics, milestones and concurrency.

### MRP-09B
PASS

Implementation remains limited to Purchase Supply foundation and explicitly blocks Inventory posting, Order Planning and Receipt/Over-Receipt behavior.

### MRP-09C
PASS

Independent read-only audit checks historical snapshot integrity, status/Firm Supply correctness, split delivery, ETA source, milestone history and specification drift.

## Final Decision

`MRP-09 Purchase Supply Foundation` is approved for MRP-09B after Owner authorization.
