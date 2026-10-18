# Hadron ERP — MRP Package 13 Final

**Package:** MRP-13 Purchase Order Operations & Receipt  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

## Owner Correction — 2026-08-31 (LOCKED)

- تبدیل Expert Decision به PO از UnitPrice/Currency همان Decision snapshot استفاده می‌کند؛ هیچ Sourcing Price فعال، refresh یا repricing مجاز نیست.
- ایجاد دستی PO نیز UnitPrice و Currency را صریحاً از کاربر مجاز دریافت می‌کند.
- PO commercial snapshot پس از commitment طبق قواعد موجود immutable است و منبع حقیقت تجاری برای Cash Flow آینده خواهد بود.
- داده‌های Sourcing Price قبلی فقط برای حفظ تاریخچه باقی می‌مانند و وارد عملیات جدید نمی‌شوند.

---

# A. Final Feature Specification — 13-PURCHASE-ORDER-OPERATIONS

## 1. Purpose

این Feature زنجیره اجرایی Procurement را از تصمیم تأییدشده Expert تا Purchase Order، Firm Supply، Receipt و Actual Inventory کامل می‌کند.

MRP-13 باید بتواند:

- Recommendation / Expert Decision → Draft PO
- controlled PO lifecycle
- commercial commitment
- Delivery Schedule operations
- Actual logistics milestones
- Receipt Documents
- Receipt allocation to exact PO Line + Schedule
- Partial Receipt
- Cancellation
- Receipt Reversal
- atomic Inventory integration
- derived operational status
- historical traceability

را مدیریت کند.

این Feature نباید:

- Payment Execution
- Cash Flow forecasting
- FX conversion
- Accounting
- Inventory valuation
- Landed Cost
- AI purchasing decisions

را پیاده‌سازی کند.

---

## 2. PO Conversion Eligibility

### LOCKED DECISION

Only a valid current Expert Decision may convert to Draft PO.

Eligible Expert Decision:

```text
ACCEPT
or
MODIFY
```

and Recommendation must be:

- not `OUTDATED_RUN`
- not `BLOCKED`
- not already converted
- valid final Source
- valid final Qty
- valid commercial snapshot
- valid current conversion eligibility

Conversion uses the immutable Expert Decision snapshot as the approved commercial basis, but it must revalidate current operational validity immediately before creating the PO group. Material drift in Source operational state/readiness, selected Price, MOQ, configured Lead Time or Base Unit blocks conversion with:

```text
DECISION_RECONFIRMATION_REQUIRED
```

and one stable detail reason:

```text
SOURCE_NOT_OPERATIONAL
READINESS_CHANGED
PRICE_CHANGED
MOQ_CHANGED
LEAD_TIME_CHANGED
BASE_UNIT_CHANGED
```

The conversion command must not silently refresh, reprice or recalculate the Decision. The Planner returns to MRP-12 and creates a new valid Expert Decision.

`REJECT` and `DEFER` cannot convert.

---

## 3. Conversion Source of Truth

The PO is created from the final approved Expert Decision snapshot.

### ACCEPT

Use machine Recommendation values as final approved values.

### MODIFY

Use Expert final values:

- final Supplier / Sourcing Option
- Final Qty
- Final Target Receipt
- final commercial snapshot
- final configured lead-time snapshot

Do not re-run source selection silently during PO creation.

---

## 4. Recommendation → PO Trace

### LOCKED DECISION

Trace must be preserved:

```text
MRP Run
→ Machine Recommendation
→ Expert Decision
→ PO Line
→ Delivery Schedule
→ Receipt
→ Inventory Transaction
```

Each resulting PO Line must retain the exact Expert Decision / Recommendation lineage.

Historical trace must remain resolvable.

---

## 5. Idempotent Conversion

### LOCKED DECISION

The same eligible Expert Decision must not be converted twice.

Conversion must be idempotent / concurrency-safe.

If duplicate conversion is attempted:

```text
BLOCK
```

or return the already-created result according to the approved application pattern.

Do not create duplicate commercial commitments.

---

## 6. Cancelled PO and Reuse of Old Decision

### LOCKED DECISION

If a PO created from an Expert Decision is later cancelled, the old decision is not automatically reusable to create another PO.

A new valid Reconfirmation / current Decision based on current planning context is required.

This prevents accidental duplicate commitment from stale planning data.

---

## 7. Batch Conversion

### LOCKED DECISION

Multiple eligible Expert Decisions may be combined into one Draft PO only when all approved grouping keys are identical:

- Supplier
- Currency
- Payment Snapshot
- Incoterm
- Shipping Mode

If any differ:

```text
Create separate PO
```

Grouping equality uses persisted identity/snapshot values, never display text: exact Supplier ID, Currency ID, Payment Snapshot fields, Shipping Mode ID/null and Incoterm ID/null. Each resulting group is an independent atomic unit of work.

---

## 8. One Decision → One PO Line

### LOCKED DECISION

Each Expert Decision converts to exactly one PO Line in v1.

Do not merge multiple different Expert Decisions into one aggregated PO Line because that would destroy planning lineage.

If two decisions concern the same Item under the same PO, they remain separate Lines unless a future explicit consolidation policy is approved.

---

## 9. PO Identity

Use MRP-09 PO foundation:

- Internal Technical ID
- server-generated immutable PO Number
- one Supplier
- one Currency
- multi-Line

MRP-13 operationalizes this model.

---

## 10. Draft PO Creation

When PO is created from Expert Decision:

Header snapshots:

- Supplier
- Currency
- Payment Term
- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Credit Anchor Event (`ORDERED` / `SHIPPED` / `WAREHOUSE_RECEIPT`)
- Shipping Mode
- Incoterm where grouped/header-owned according to approved model

Line snapshots:

- Item
- Sourcing Option
- Ordered Qty
- Unit Price
- source / route references
- Decision lineage

Exact ownership follows MRP-09A/B implementation.

The committed PO snapshot must enforce Credit%>0 → CreditDays>0 and Credit Anchor required; Credit%=0 → Anchor may be null. Supplier-default changes must never alter this snapshot. For `WAREHOUSE_RECEIPT`, the financial anchor is the operational Final Receipt Date, never First Receipt or Cancellation Date.

---

## 11. Default Delivery Schedule

### LOCKED DECISION

At Draft PO creation, each PO Line receives one default Delivery Schedule covering its full ordered quantity.

Baseline:

```text
Schedule Qty = PO Line Ordered Qty
Target / Current Planned Warehouse Receipt = Expert Target Receipt
```

This prevents unscheduled Firm Supply ambiguity later.

---

## 12. Draft Status

### LOCKED DECISION

`DRAFT` is editable.

For a Draft created from an Expert Decision, the following Decision-derived core fields are read-only:

- Item
- Supplier / selected Source
- Currency
- Unit Price
- Ordered Qty
- Payment Snapshot
- Shipping Mode
- Incoterm

Changing any of these values requires a new MRP-12 Expert Decision; MRP-13 never silently detaches a Draft from its approved commercial basis.

Schedule split/reallocation and planned-date revision may be performed while total Line Ordered Qty remains unchanged. Every such deviation from the Decision target requires Reason, durable audit lineage and preservation of the original Expert Target Receipt.

The original Expert Decision lineage must never be lost.

---

## 13. Approved Status

### LOCKED DECISION

`APPROVED` means internal Hadron approval.

It is still:

```text
NOT Firm Supply
```

Commercial/core PO fields become locked against casual edit.

If material commercial fields need correction:

```text
Return to Draft
+ Reason
↓
Edit
↓
Approve again
```

---

## 14. Return to Draft

### LOCKED DECISION

Return from APPROVED to DRAFT is allowed before ORDERED.

Requirements:

- explicit command
- Reason
- permission
- audit trail

This is not a generic status dropdown.

---

## 15. Ordered Status

### LOCKED DECISION

`ORDERED` means the order has actually been committed/placed with Supplier.

At this moment:

- remaining quantity becomes Firm Supply
- commercial snapshot becomes committed
- Original Planned Milestones are locked
- Ordered Date is recorded
- later ordinary edits to commercial/core fields are prohibited

---

## 16. Firm Supply Start

### LOCKED DECISION

Only the actual transition:

```text
APPROVED → ORDERED
```

starts Firm Supply.

`APPROVED` alone never creates Firm Incoming.

This preserves MRP-09/10 semantics.

---

## 17. Ordered Commercial Lock

After ORDERED, normal edits are prohibited for:

- Supplier
- Currency
- Item
- Ordered Qty
- Unit Price
- Payment Snapshot
- Sourcing reference that defines commercial commitment

Any future exceptional correction requires a separately approved controlled process and is not part of normal MRP-13 UI.

---

## 18. Original Planned Milestones

### LOCKED DECISION

Original Planned Milestones are generated/finalized when PO becomes ORDERED.

Reason:

Actual Ordered Date is now known.

Baseline:

```text
Ordered Date
+
snapshotted configured lead-time stages
→ Original Planned Milestone Dates
```

Do not freeze Original Plan at Draft creation.

Generation uses exactly four non-overlapping stage boundaries:

```text
ORDERED → SUPPLIER_READY
SUPPLIER_READY → SHIPPED
SHIPPED → ARRIVED_DESTINATION
ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
```

`HANDOVER_TO_FORWARDER` and `CUSTOMS_CLEARED` remain diagnostic milestones and do not add duration.

---

## 19. Milestone Baseline

Use MRP-09 baseline milestone codes:

```text
SUPPLIER_READY
HANDOVER_TO_FORWARDER
SHIPPED
ARRIVED_DESTINATION
CUSTOMS_CLEARED
WAREHOUSE_RECEIPT
```

Stable machine codes.

Display labels may be localized.

---

## 20. Planned / Revised / Actual

For each applicable milestone, preserve:

- Original Planned Date
- Current/Revised Planned Date
- Actual Date

Original remains immutable after ORDERED.

Revised plan may change under controlled rules.

Actual records observed execution.

---

## 21. Revised Plan after ORDERED

Changes to Revised Planned Dates after ORDERED require:

- Reason
- actor
- timestamp
- audit

Do not overwrite Original Planned Date.

---

## 22. Split Delivery

### LOCKED DECISION

Delivery Schedule splitting is supported both:

- before ORDERED
- after ORDERED

provided quantity reconciliation remains valid.

After ORDERED:

- split / reallocation requires Reason + Audit
- already received quantity cannot be moved between schedules
- historical actual receipt allocation remains immutable

---

## 23. Schedule Quantity Integrity

For each PO Line:

```text
SUM(active schedule ordered/planned allocation)
=
PO Line committed quantity
```

subject to approved cancellation/receipt rules.

The schedule model must never double-count or lose line quantity. `AllocatedQty` is the immutable commercial quantity basis of a Schedule after commitment; Received and Cancelled are facts, not rewrites of that basis.

At all times:

```text
SUM(active Schedule AllocatedQty) = PO Line OrderedQty
Schedule OpenQty = AllocatedQty - Valid Posted ReceivedQty - CancelledQty
Schedule OpenQty >= 0
```

Split/reallocation replaces only unreceived and uncancelled allocation and preserves the Line total atomically. Partial Receipt reduces OpenQty only by its valid Posted quantity. Cancellation reduces OpenQty only by the cancelled fact. Final closure requires every Schedule OpenQty to equal zero; no quantity may be inferred, dropped or counted at both Line and Schedule grain.

---

## 24. Status Control Principle

### LOCKED DECISION

PO lifecycle is not a free-edit status dropdown.

### Command-driven states

```text
DRAFT
APPROVED
ORDERED
```

are entered through explicit commands.

### Fact-derived / operational summary states

Where practical:

```text
SHIPPED
CUSTOMS
PARTIALLY_RECEIVED
RECEIVED
CANCELLED
```

derive from:

- actual milestones
- receipt facts
- cancellation facts
- open quantity

After `ORDERED`, deterministic Header summary precedence is:

```text
OpenQty = 0 and ReceivedQty > 0 → RECEIVED
OpenQty = 0 and ReceivedQty = 0 → CANCELLED
ReceivedQty > 0 and OpenQty > 0 → PARTIALLY_RECEIVED
otherwise any open Schedule at/after customs facts → CUSTOMS
otherwise any open Schedule with SHIPPED Actual → SHIPPED
otherwise → ORDERED
```

`DRAFT` and `APPROVED` remain their command-controlled states before commitment. Completion Outcome is derived separately and never changes this quantity precedence.

---

## 25. Header Status Is Summary

### LOCKED DECISION

In split-shipment PO, Schedule detail is the precise operational truth.

PO Header Status is a summarized lifecycle state.

Example:

```text
Schedule A → CUSTOMS
Schedule B → SHIPPED
Schedule C → ORDERED
```

Header summarizes progress, but user must be able to inspect Schedule-level reality.

---

## 26. Header Status Precedence

Baseline derived precedence after ORDERED:

1. If `Open Qty = 0` and `Received Qty > 0` → `RECEIVED`
2. If `Open Qty = 0` and `Received Qty = 0` → `CANCELLED`
3. If `Received Qty > 0` and `Open Qty > 0` → `PARTIALLY_RECEIVED`
4. Else if any open Schedule is in Customs stage → `CUSTOMS`
5. Else if any open Schedule has actually Shipped → `SHIPPED`
6. Else → `ORDERED`

Exact milestone-to-header aggregation is finalized in MRP-13A.

---

## 27. Completion Outcome

### LOCKED DECISION

Final status remains compact.

A separate derived Completion Outcome explains how the order closed.

Baseline:

```text
FULLY_RECEIVED
RECEIVED_WITH_CANCELLATION
FULLY_CANCELLED
```

Examples:

### Fully Received

```text
Ordered   100
Received  100
Cancelled   0
Open        0

Status  = RECEIVED
Outcome = FULLY_RECEIVED
```

### Received with Cancellation

```text
Ordered   100
Received   40
Cancelled  60
Open        0

Status  = RECEIVED
Outcome = RECEIVED_WITH_CANCELLATION
```

### Fully Cancelled

```text
Ordered   100
Received    0
Cancelled 100
Open        0

Status  = CANCELLED
Outcome = FULLY_CANCELLED
```

---

## 28. Receipt Document

### LOCKED DECISION

Receipt is an independent Entity / business document.

Baseline lifecycle:

```text
DRAFT
POSTED
REVERSED
```

Only POSTED Receipt affects:

- PO Received Qty
- Delivery Schedule Remaining Qty
- Inventory

---

## 29. Receipt Identity

Receipt should have:

- Internal Technical ID
- Receipt Number
- Status
- Destination Warehouse
- Effective Receipt Date
- Created At
- Posted At
- Reversal info where applicable
- actor/audit metadata

Receipt Number is server-generated, immutable and unique:

```text
RCV-{sequence:D6}
```

Use a PostgreSQL sequence; gaps are allowed and `MAX()+1` is forbidden.

---

## 30. One PO and One Warehouse per Receipt

### LOCKED DECISION

Each Receipt Document belongs to exactly one Purchase Order and exactly one Destination Warehouse.

All Receipt Lines must reference Lines/Schedules of that same PO. Mixing more than one PO in a Receipt is prohibited.

If physical goods are split across two Warehouses:

```text
Create two Receipt Documents
```

This keeps Inventory posting unambiguous.

---

## 31. Receipt Line Allocation

### LOCKED DECISION

Each Receipt Line must reference exactly:

```text
PO Line
+
Delivery Schedule
```

Receipt allocation only at PO Header/Line without Schedule is insufficient in split-delivery model.

---

## 32. Receipt Quantity

Rules:

- decimal/numeric
- > 0
- Item Base Unit
- must not exceed Schedule Open Qty in v1
- must match PO Line Item
- Destination Warehouse belongs to Receipt Header

---

## 33. Receipt Draft

DRAFT Receipt:

- editable
- has no Inventory impact
- has no PO Received Qty impact
- does not change Firm Supply

User may review before posting.

---

## 34. Receipt Posting

### LOCKED DECISION

Posting Receipt is one atomic transaction.

It must simultaneously:

1. validate Receipt
2. update receipt state
3. record received quantity against exact PO Line + Schedule
4. reduce open Firm Supply
5. create MRP-08 Inventory `RECEIPT`
6. commit both Procurement and Inventory effects together

Half-commit is prohibited.

---

## 35. Inventory Transaction Link

Each posted Receipt Line should preserve a traceable link to the MRP-08 Inventory transaction/effect it created.

The Inventory Ledger remains Actual Stock source of truth.

PO must not maintain a second independent stock balance.

---

## 36. Partial Receipt

### LOCKED DECISION

Partial Receipt is allowed.

Example:

```text
Ordered = 100,000
Received = 40,000
Open = 60,000
```

Effects:

```text
Actual Inventory += 40,000
Firm Incoming remaining = 60,000
```

Only remaining Schedule quantity stays Firm Supply.

---

## 37. Repeated Partial Receipts

A Schedule may be received through multiple Receipt Documents until open quantity reaches zero.

Each receipt remains separately auditable.

Do not collapse history into one mutable received number only.

---

## 38. Over-Receipt

### LOCKED DECISION

The previously open Over-Receipt policy is now CLOSED for v1.

Tolerance:

```text
0
```

Rule:

```text
Receipt Qty > Schedule Open Qty
→ BLOCK
```

No percentage tolerance.

No hidden exception.

---

## 39. Physical Excess Delivery

### LOCKED DECISION

If Supplier physically sends extra quantity and Hadron decides to accept it:

Do not silently over-receive the original PO.

First create a controlled valid additional commercial commitment, such as:

```text
Supplemental PO
```

or another future explicitly approved commercial mechanism.

Only then can extra quantity be received against valid committed quantity.

---

## 40. Cancellation

### LOCKED DECISION

Cancellation applies to remaining Schedule quantity.

Cancellation record/operation must preserve:

- Schedule
- Cancel Qty
- Reason
- actor
- timestamp

Received history is never changed by cancellation.

---

## 41. Cancellation Quantity

Rules:

- `Cancel Qty > 0`
- `Cancel Qty <= Schedule Open Qty`
- cancellation reduces Firm Incoming
- cancellation does not change Actual Inventory
- cancellation cannot cancel already received quantity

---

## 42. Cancel after Partial Receipt

### LOCKED DECISION

Allowed.

Example:

```text
Ordered   100,000
Received   40,000
Open       60,000
Cancel     15,000
Remaining  45,000
```

Firm Supply becomes:

```text
45,000
```

---

## 43. Full Cancellation

If no Receipt exists and all remaining quantity is cancelled:

```text
Open Qty = 0
Received Qty = 0
Status = CANCELLED
Outcome = FULLY_CANCELLED
```

---

## 44. Cancelled Quantity Trace

Cancelled Qty must remain historically visible.

Do not mutate Ordered Qty downward to hide cancellation.

Maintain:

```text
Ordered
Received
Cancelled
Open
```

as distinct concepts.

---

## 45. Open Qty

Use MRP-09 formula:

```text
Open Qty
=
Ordered Qty
-
Received Qty
-
Cancelled Qty
```

Derived, never manually edited.

Schedule and Line totals must reconcile.

---

## 46. Receipt Reversal

### LOCKED DECISION

Posted Receipt cannot be directly edited or deleted.

Correction uses:

```text
Reverse Receipt
```

Reversal must preserve original Receipt.

---

## 47. Receipt Reversal Atomicity

Reversal is one atomic business operation.

It must simultaneously:

1. mark/create reversal relationship
2. reverse PO received quantity effect
3. restore relevant open supply if still commercially valid
4. create opposite Inventory effect
5. preserve audit/history

Half reversal is prohibited.

---

## 48. Reversal Inventory Guard

### LOCKED DECISION

If reversing the Receipt would make Destination Warehouse Actual On Hand negative:

```text
BLOCK
```

The user must first correct downstream Inventory transactions.

This preserves MRP-08 normal no-negative-stock rule.

---

## 49. Reversed Receipt History

Original Receipt remains visible:

```text
POSTED → REVERSED
```

or equivalent approved linked-state model.

Do not erase it.

The reversing Inventory movement must remain traceable.

---

## 50. Reversal and Firm Supply

When a Receipt is reversed, quantity may become open supply again only if:

- PO/Schedule remains commercially active
- quantity has not been cancelled
- status/lifecycle still allows remaining supplier commitment

Restoration is derived, never manually assigned:

```text
Restored OpenQty
= AllocatedQty
 - remaining valid Posted, non-reversed ReceivedQty
 - existing CancelledQty
```

Only the non-cancelled commercial obligation may reopen. Fully cancelled allocation stays cancelled; a positive derived OpenQty becomes Firm Supply only when the PO/Schedule remains in an eligible committed lifecycle state. Do not blindly restore Firm Supply to a cancelled/closed schedule.

---

## 51. Actual Milestones

### LOCKED DECISION

Actual Schedule milestones may be recorded as real operational facts.

They must remain separate from:

- Original Plan
- Revised Plan

Examples:

- actual Supplier Ready
- actual Handover
- actual Shipped
- actual Arrival
- actual Customs Cleared

---

## 52. Milestone Ordering Validation

Actual milestone dates must be temporally coherent where the business process requires sequence.

Example baseline:

```text
SUPPLIER_READY
<= HANDOVER_TO_FORWARDER
<= SHIPPED
<= ARRIVED_DESTINATION
<= CUSTOMS_CLEARED
<= WAREHOUSE_RECEIPT
```

For every pair of recorded applicable Actual milestones, the canonical sequence is mandatory and nondecreasing. Missing intermediate diagnostic milestones are allowed, but every recorded later milestone must still be on or after the latest recorded earlier milestone. Actual dates later than the server-owned current `Asia/Tehran` business date are prohibited.

---

## 53. Warehouse Receipt Milestone

### LOCKED DECISION

Actual:

```text
WAREHOUSE_RECEIPT
```

must derive from Posted Receipt facts.

User must not manually type a competing warehouse-receipt actual date.

---

## 54. First Receipt / Final Receipt Dates

For each Schedule, derive:

```text
First Receipt Date
Final Receipt Date
```

from Posted, non-reversed Receipt history.

Baseline future analytics:

- `Actual WAREHOUSE_RECEIPT` may use First Receipt Date for initial-arrival KPI
- Final Receipt Date remains separately available for completion KPI

Package 14 determines KPI presentation, but not the financial anchor rule.

For Credit Anchor Event, `WAREHOUSE_RECEIPT` uses Final Receipt Date. Final Receipt Date is derived only from valid `POSTED`, non-reversed Receipt history when the commercially required received quantity is complete:

- a partial Receipt leaves Final Receipt Date and the anchor unresolved;
- for `RECEIVED_WITH_CANCELLATION`, use the last valid Posted Receipt required before `Open = 0`; Cancellation Date is never a Receipt or anchor date;
- `FULLY_CANCELLED` with no received quantity has no WAREHOUSE_RECEIPT anchor;
- if reversal of the completing Receipt makes the Schedule/PO open again, Final Receipt Date and the anchor become unresolved;
- after a later valid Receipt re-establishes completion, that new completing Receipt effective date becomes Final Receipt Date.

There is no separately editable Financial Receipt Date.

Regardless of that future choice, the four analytics-compatible actual stages remain reconstructable from ORDERED, SUPPLIER_READY, SHIPPED, ARRIVED_DESTINATION and receipt-derived WAREHOUSE_RECEIPT. Diagnostic Handover/Customs Cleared dates do not create additive stages.

---

## 55. SHIPPED Operational State

A Schedule is considered actually Shipped only when its `SHIPPED` Actual milestone exists under approved logic.

Do not let user manually set Header `SHIPPED` without supporting facts.

---

## 56. CUSTOMS Operational State

A Schedule may be considered Customs-stage when actual Arrival / Customs process facts support it according to MRP-13A model.

Header `CUSTOMS` is derived summary.

---

## 57. Milestone Editability

Actual milestone records are immutable historical facts. Correction is append-only supersession with mandatory Reason, actor/context and timestamp; the original record is never edited or deleted. Reuse the existing audit boundary and preserve durable domain history without a parallel audit platform or heavyweight event-sourcing system.

---

## 58. Schedule Changes after Receipt

After a Schedule has Received Qty:

- received allocation is immutable
- cannot reduce schedule committed quantity below `Received + Cancelled` facts
- cannot move already received qty to another schedule
- remaining open qty may still be split/replanned where valid

---

## 59. Schedule Split after ORDERED

After ORDERED, split operation must preserve:

```text
Total committed open quantity
```

unless accompanied by explicit cancellation.

Example:

```text
Old open Schedule = 60,000
↓ split
Schedule B = 20,000
Schedule C = 40,000
```

No quantity is created/destroyed.

---

## 60. Schedule ETA Revision after ORDERED

Allowed with:

- Reason
- audit
- Original Plan preserved

Current/Revised planned Warehouse Receipt continues to feed future Firm Supply ETA.

A new MRP Run sees revised ETA.

Historical old MRP Runs remain unchanged.

---

## 61. Recommendation Target vs PO Plan

Expert Target Receipt is the planning input at PO creation.

At ORDERED, Original Planned Warehouse Receipt may be recalculated from actual Ordered Date + snapshotted lead-time chain.

Both planning lineage values remain visible with distinct labels:

- `Expert Target Receipt`
- `Original Planned Warehouse Receipt`
- `Current/Revised Planned Warehouse Receipt`

Do not overwrite the Expert Decision target.

---

## 62. PO Current ETA

After ORDERED:

```text
Current PO Schedule ETA
=
Current/Revised Planned WAREHOUSE_RECEIPT
else Original Planned WAREHOUSE_RECEIPT
```

This becomes Firm Supply ETA for future MRP Runs.

---

## 63. Missing ETA

If an ORDERED Schedule lacks a valid current/original Warehouse Receipt ETA:

- quantity can remain commercially open
- future MRP sees Firm Qty — ETA Missing
- MRP-13 should surface operational warning

Do not invent ETA.

---

## 64. Recommendation Conversion and Schedule Target

At Draft creation from Expert Decision:

```text
Default Schedule Target Receipt
=
Expert Final Target Receipt
```

This is planning intent.

At ORDERED, actual Order Date and lead-time snapshot create Original Plan.

Any difference should remain visible rather than silently reconciled.

---

## 65. PO Approval Permissions

Architecture should distinguish at minimum:

- Create/Edit Draft PO
- Approve PO
- Return Approved to Draft
- Place Order / Mark Ordered
- Manage Delivery Schedules
- Update logistics Actual milestones
- Create Receipt
- Post Receipt
- Reverse Receipt
- Cancel remaining quantity

Exact roles map to existing RBAC.

Do not build parallel Identity.

---

## 66. Approval Audit

Important events:

- Draft created from Decision
- Draft edited
- Approved
- Returned to Draft + Reason
- Ordered
- Schedule split
- ETA revised + Reason
- Actual milestone updated
- Receipt created/posted/reversed
- Cancellation
- final outcome

must be auditable.

---

## 67. PO Edit History

Committed historical values must remain explainable. Durable conversion, lifecycle, schedule revision, cancellation, Receipt and milestone-supersession records preserve Reason, actor/context, timestamp and lineage. Do not rely solely on mutable current header values and do not build a parallel generic Audit platform.

---

## 68. Recommendation Link after Draft Edit

Decision-derived core values cannot differ in the MRP-13 Draft. The UI shows them read-only from the immutable Decision snapshot. Schedule target/split deviations show the original Expert Target beside the revised Schedule plan and require Reason/audit lineage.

---

## 69. Approval Validation

Before APPROVED, validate at minimum:

- Supplier
- Currency
- payment snapshot valid
- Lines exist
- Line Qty > 0
- Price valid
- compatible Item/Source
- Schedule quantities reconcile
- no duplicate conversion violation
- commercial grouping rules valid

Exact validation is finalized in MRP-13A.

---

## 70. Order Commitment Validation

Before ORDERED, validate at minimum:

- PO is APPROVED
- Ordered Date supplied/derived
- Supplier active or explicit approved historical exception policy
- committed commercial snapshot complete
- schedule allocation complete
- required lead-time snapshot available
- Original Planned milestone generation possible or explicit ETA-missing handling approved

No casual bypass.

---

## 71. Receipt Validation

Before POST:

- Receipt is DRAFT
- Destination Warehouse active
- PO/Schedule eligible
- Qty > 0
- Qty <= Schedule Open Qty
- Item matches
- Effective Date valid
- no reversed/closed invalid source
- posting will not violate inventory/data integrity

Receipt scope and date validation also require:

```text
one Purchase Order
one Destination Warehouse
OrderedDate <= EffectiveDate <= TehranToday
```

`TehranToday` is server-owned. A future Receipt is rejected; the client cannot override the business date.

---

## 72. Receipt Posting Date

Separate:

- Receipt Effective Date
- Receipt Created At
- Receipt Posted At

Inventory movement uses approved Effective Date semantics from MRP-08.

Do not confuse system posting timestamp with business receipt date.

---

## 73. Backdated Receipt

MRP-13 consumes the locked MRP-08 business-date rule. A backdated Receipt or its Inventory reversal must preserve every end-of-day closing balance from its EffectiveDate through the latest ledger date for each affected Item+Warehouse. MRP-13 must reuse MRP-08 deterministic locks and DB protection and must not invent intra-day ordering or a second inventory policy.

---

## 74. Receipt and Inactive Warehouse

New Receipt normally requires Active Warehouse.

Historical posted Receipts remain resolvable if Warehouse later becomes inactive.

---

## 75. Receipt and Inactive Item

Historical receipt remains resolvable.

For an already `ORDERED` PO, Receipt, remaining-quantity Cancellation and Receipt Reversal remain allowed when the Item is inactive, with an explicit `INACTIVE_ITEM_EXISTING_COMMITMENT` warning. This permits settlement/correction of an existing commitment.

An inactive Item cannot enter a new procurement commitment and its committed Ordered Qty cannot be expanded. No new Decision conversion or Mark-Ordered operation may bypass that rule.

---

## 76. Receipt and Cancelled Schedule

Cannot post Receipt against quantity already cancelled.

If physical goods arrive after cancellation, commercial commitment must be reopened/re-established through controlled process; do not hide conflict.

---

## 77. Cancellation after Shipment

Cancellation of remaining quantity may still occur operationally if Supplier will not deliver the rest.

MRP-13 baseline allows cancellation of remaining open qty regardless of logistics stage, provided:

- Qty remains open
- Reason required
- user authorized

Do not alter already received qty.

---

## 78. Cancellation after Full Receipt

If Open Qty = 0 due to full receipt:

```text
Cancel Qty = 0
```

No cancellation is allowed.

---

## 79. Receipt Reversal after PO Closed

A Receipt may be reversed even after PO reached RECEIVED, if integrity rules permit.

After reversal:

- Header/Line/Schedule derived status must recalculate
- PO may return to PARTIALLY_RECEIVED / ORDERED / appropriate state
- Inventory reverses atomically

Do not freeze derived status incorrectly.

---

## 80. Derived Status Recalculation

After:

- Receipt post
- Receipt reversal
- Cancellation
- milestone update
- schedule split/replan

derived PO/Schedule status must recalculate deterministically.

No stale manually maintained summary status.

---

## 81. No Independent Firm Supply Field

Firm Supply remains derived from:

- status/commitment
- Schedule Open Qty
- ETA

There is no manually editable:

```text
Firm = Yes
```

flag.

---

## 82. MRP Run Interaction

Operational updates do not mutate old MRP Runs.

After:

- PO ORDERED
- ETA revision
- Receipt
- Cancellation

the current source data changes.

To see planning impact:

```text
Run MRP again
```

Historical previous Runs stay unchanged.

---

## 83. Recommendation State after Conversion

Once a valid Decision is converted:

```text
Recommendation / Decision
→ CONVERTED_TO_PO
```

with PO reference.

This state is historical and not reversible merely because PO later changes.

If PO is cancelled, new planning/reconfirmation is required.

---

## 84. PO Conversion Atomicity

Conversion of one or multiple Expert Decisions into Draft PO(s) must be transactional enough to prevent:

- PO created but Decision not marked converted
- Decision marked converted but PO not created
- duplicate conversion under concurrency

For each PO group, lock selected Expert Decisions in deterministic ID order, revalidate them, insert the PO/Lines/Schedules and persist the immutable conversion lineage in one database transaction. A database unique constraint on Expert Decision conversion is the final duplicate-conversion guard.

---

## 85. Batch Conversion Failure

Each generated PO group is atomic. A failed Decision invalidates its whole group, but does not roll back an independent valid group. The batch result returns explicit success/failure for every group and every selected Decision. Retrying the request must return/reuse already-successful conversion results and must never duplicate them. Do not silently drop invalid Lines.

---

## 86. Receipt Atomicity with Inventory

Because ERP is a Modular Monolith with one transactional database baseline, Receipt posting should use one database transaction across Procurement + Inventory module boundaries where architecture permits.

Do not use eventual consistency/outbox for this core same-database integrity unless actual architecture requires otherwise.

---

## 87. Reversal Atomicity with Inventory

Same principle:

PO quantity restoration and Inventory reversal must commit together.

---

## 88. Source Document Link

MRP-08 Inventory `RECEIPT` created from PO Receipt must retain a typed source reference back to Receipt Document / Line according to approved module integration.

Do not use ambiguous free-text-only linkage.

---

## 89. Purchase Supply Read Models

MRP-13 should expose operational read models:

### PO List
- PO Number
- Supplier
- Currency
- Status
- Completion Outcome
- Ordered Date
- Total
- Received Qty summary
- Open Qty summary
- Next ETA
- overdue state

### PO Detail
- Header snapshot
- Lines
- Decision lineage
- Schedules
- milestone timeline
- receipts
- cancellations
- status/outcome

### Receipt List
- Receipt Number
- Warehouse
- PO
- Effective Date
- Status
- Qty

---

## 90. PO Operations UI

Baseline actions by state:

### DRAFT
- edit
- add/remove approved Lines if allowed
- manage schedules
- approve

### APPROVED
- return to Draft
- mark ORDERED

### ORDERED / in progress
- revise ETA
- update actual milestones
- split remaining schedules
- create Receipt
- cancel remaining qty

### RECEIVED / CANCELLED
- read-only operational history
- Receipt reversal where authorized and valid

Exact action availability derives from facts/permissions.

---

## 91. Receipt UI

Receipt entry should show:

- PO
- Supplier
- Destination Warehouse
- Effective Date
- Lines
- PO Line
- Schedule
- Item
- Schedule Open Qty
- Receipt Qty
- resulting Schedule Open Qty

Before POST:

- validate
- show inventory destination
- show over-receipt block if applicable

---

## 92. Receipt Posting Confirmation

Because posting changes Actual Inventory and Firm Supply, UI should require explicit Post action.

Do not auto-post upon saving Draft.

---

## 93. Reversal UI

Reversal requires:

- Reason
- clear display of original Receipt
- quantity
- Warehouse
- Inventory impact
- PO impact

If reversal blocked by negative-stock guard, show the exact reason and current available stock context.

---

## 94. Cancellation UI

Cancellation requires:

- PO Line / Schedule
- current Open Qty
- Cancel Qty
- Reason
- resulting Open Qty

No direct edit of Ordered Qty.

---

## 95. Schedule Timeline UI

For each Schedule display:

- committed/planned Qty
- Received Qty
- Cancelled Qty
- Open Qty
- Original Planned milestone dates
- Current/Revised dates
- Actual dates
- ETA
- operational warnings

---

## 96. Historical Trace UI

User should be able to navigate:

```text
PO Line
→ Expert Decision
→ Machine Recommendation
→ MRP Run
```

and:

```text
Receipt
→ Inventory Transaction
```

where permissions allow.

---

## 97. Commercial Values after ORDERED

Display original committed values as authoritative.

Do not replace them with current Sourcing Master Price, MOQ or Payment terms.

Current Master may be shown separately only if clearly labeled.

---

## 98. Quantity Precision

Use decimal/numeric for:

- Ordered Qty
- Schedule Qty
- Received Qty
- Cancelled Qty
- Open Qty
- Unit Price
- Totals

Do not use binary floating point.

---

## 99. Currency

PO keeps original Currency snapshot.

No FX conversion in MRP-13.

Do not aggregate PO values across currencies into one total without FX.

---

## 100. Dates

Canonical storage:

- Gregorian

Business display:

- Jalali

Business timezone:

```text
Asia/Tehran
```

Use canonical dates for arithmetic and status logic.

---

## 101. Database Integrity

Expected protections where practical:

- unique PO Number
- unique Receipt Number if implemented
- Expert Decision conversion uniqueness/idempotency
- valid PO grouping
- Qty > 0
- Received <= Open Schedule Qty
- Cancelled <= Open Schedule Qty
- Open Qty never negative
- schedule quantity reconciliation
- exact Receipt Line → PO Line + Schedule FK
- one Warehouse per Receipt
- one PO per Receipt
- Receipt Effective Date inside inclusive OrderedDate..server TehranToday
- posted receipt immutability
- reversal relationship integrity
- append-only Actual milestone correction/supersession
- immutable Decision-derived Draft core values
- no destructive cascade of historical PO/Receipt records

MRP-13B must implement these protections with forward migrations and prove direct-SQL bypass attempts fail without rewriting earlier migrations.

---

## 102. Concurrency

Concurrency-sensitive operations include:

- Decision→PO conversion
- batch grouping/conversion
- PO approval/order placement
- schedule split/reallocation
- two Receipts against same Schedule
- Receipt vs Cancellation race
- Receipt reversal
- ETA edits
- milestone updates

Use the existing `PurchaseOrder.Version` as the single PO aggregate optimistic token. Conversion additionally uses deterministic Expert Decision row locks plus conversion uniqueness. Receipt posting/cancellation/split/reversal lock affected PO/Schedule rows in deterministic ID order and participate in the MRP-08 Item/Warehouse advisory-lock protocol for inventory effects. Receipt Draft/Post/Reverse uses an explicit Receipt concurrency token. Direct-DB constraints/triggers remain the final protection for immutable history, quantity reconciliation, over-receipt, one-PO/one-Warehouse scope and milestone supersession integrity.

---

## 103. Receipt Concurrency

Two concurrent Receipt posts must not both consume the same Schedule Open Qty.

Example:

```text
Open Qty = 10,000

Receipt A = 8,000
Receipt B = 8,000
```

Both must not succeed.

At most a valid non-over-received result may commit.

---

## 104. Receipt vs Cancellation Concurrency

A Receipt and Cancellation against same open Schedule quantity must serialize safely.

Do not let both consume the same remaining quantity.

---

## 105. Schedule Split Concurrency

Schedule split/reallocation must not race with Receipt posting and create invalid allocation.

Use locking/concurrency strategy approved in Analysis.

---

## 106. Operational Exceptions

Useful exceptions/warnings include:

```text
OUTDATED_DECISION
ALREADY_CONVERTED
INVALID_PO_GROUP
PO_NOT_APPROVABLE
PO_NOT_ORDERABLE
SCHEDULE_QTY_MISMATCH
RECEIPT_OVER_OPEN_QTY
RECEIPT_TO_CANCELLED_QTY
RECEIPT_REVERSAL_NEGATIVE_STOCK
ETA_MISSING
MILESTONE_SEQUENCE_INVALID
```

Exact codes normalized in MRP-13A.

---

## 107. No Silent Auto-Fix

MRP-13 must not silently:

- change Qty to fit open amount
- change Warehouse
- move receipt to another Schedule
- reopen cancelled qty
- change Price
- recalculate commercial snapshots from Sourcing
- invent ETA

Invalid operations must fail clearly.

---

## 108. Package 14 Integration Contract

MRP-13 must preserve data needed by MRP-14:

### Lead-Time Analytics
- configured snapshot
- original plan
- revised plan
- actual milestones
- first/final receipt dates

### Cash Flow Contract
- PO Currency
- Unit Price / PO Total
- Payment snapshot
- Ordered Date
- shipment milestones
- receipt milestones
- credit days

MRP-13 stores source facts.

It does not calculate analytics or cash-flow schedules.

---

## 109. Explicit Non-Scope

MRP-13 must NOT implement:

- payment execution
- bank transactions
- cash-flow forecast
- FX rates/conversion
- accounting journal entries
- AP ledger
- inventory valuation
- FIFO/LIFO/average cost
- landed cost
- freight invoice matching
- customs costing
- supplier email automation
- EDI
- AI procurement decision
- automatic over-receipt tolerance
- quality inspection / quarantine
- barcode/WMS
- lot/serial/batch

---

## 110. Acceptance Criteria

MRP-13 is acceptable when:

1. Only valid ACCEPT/MODIFY current decisions convert to PO.
2. OUTDATED/BLOCKED/converted decisions cannot convert.
3. Conversion is idempotent and concurrency-safe.
4. Multiple decisions group only when Supplier/Currency/Payment/Incoterm/Shipping Mode match.
5. Each Expert Decision maps to one PO Line.
6. Default Schedule is created for full Line Qty.
7. Draft schedule planning is editable, but Decision-derived core fields remain read-only.
8. Approved is internal and non-Firm.
9. Return-to-Draft requires Reason/Audit.
10. ORDERED creates commercial commitment and Firm Supply.
11. Commercial/core fields lock after ORDERED.
12. Original Planned milestones freeze at ORDERED.
13. Split Delivery works before/after ORDERED.
14. Post-ORDERED schedule changes require Reason/Audit.
15. Header Status is deterministic summary.
16. Receipt Document has Draft/Posted/Reversed lifecycle.
17. One Purchase Order and one Destination Warehouse per Receipt.
18. Every Receipt Line targets exact PO Line + Schedule.
19. Posting Receipt atomically updates PO receipt facts and Inventory Ledger.
20. Partial Receipt works.
21. Remaining open quantity remains Firm Incoming.
22. Over-Receipt tolerance is zero.
23. Physical excess requires additional commercial commitment.
24. Cancellation operates on remaining Schedule Qty.
25. Cancel after Partial is supported.
26. Received history is preserved after cancellation.
27. Mixed receipt+cancellation closes as RECEIVED + RECEIVED_WITH_CANCELLATION.
28. Full cancellation closes as CANCELLED + FULLY_CANCELLED.
29. Posted Receipt cannot be directly edited/deleted.
30. Receipt Reversal is atomic across Procurement + Inventory.
31. Reversal is blocked if it would create negative Inventory.
32. Warehouse Receipt milestone derives from Posted Receipt.
33. Actual milestone chronology is validated.
34. Old MRP Runs remain unchanged after operational updates.
35. Recommendation lineage remains traceable.
36. No payment/FX/accounting logic leaks into MRP-13.
37. Concurrency tests cover Receipt/Cancel/Schedule races.
38. Conversion revalidates operational drift and never silently refreshes the Expert Decision snapshot.
39. Each PO group commits/fails atomically and batch results report every group explicitly.
40. Receipt Effective Date is inside the inclusive OrderedDate..server TehranToday range.
41. Existing ORDERED commitments remain settleable for an inactive Item with an explicit warning, but cannot expand.
42. Actual milestone corrections are append-only supersessions with Reason and canonical chronology.

---

## 111. Locked Decisions

The following are locked for MRP-13:

1. PO conversion eligibility = valid current ACCEPT/MODIFY only.
2. OUTDATED/BLOCKED/already-converted cannot convert.
3. Conversion is idempotent.
4. Cancelled PO does not make old Decision reusable.
5. Batch grouping requires identical Supplier/Currency/Payment/Incoterm/Shipping Mode.
6. One Expert Decision → one PO Line.
7. Decision-derived DRAFT core fields are immutable; only approved schedule planning changes are allowed with Reason/Audit.
8. APPROVED commercial/core locked; Return to Draft + Reason before ORDERED.
9. ORDERED locks committed commercial/core values.
10. Firm Supply starts at ORDERED.
11. One default Schedule per Line at Draft creation.
12. Default Schedule initially uses Expert Target Receipt.
13. Original Planned Milestones lock at ORDERED using actual Ordered Date + lead-time snapshot.
14. Split Schedule supported before/after ORDERED.
15. Post-ORDERED schedule change requires Reason/Audit.
16. DRAFT/APPROVED/ORDERED command-controlled.
17. SHIPPED/CUSTOMS/PARTIALLY_RECEIVED/RECEIVED/CANCELLED derived where practical from facts.
18. Receipt is independent Draft/Posted/Reversed document.
19. One Purchase Order and one Warehouse per Receipt.
20. Receipt Line maps exact PO Line + Schedule.
21. Receipt posting atomically updates PO + Inventory.
22. Partial Receipt supported.
23. Over-Receipt tolerance = 0.
24. Extra physical qty requires supplemental/controlled commercial commitment.
25. Cancellation applies only to remaining Schedule Qty.
26. Cancel after Partial supported.
27. Open Qty = Ordered - Received - Cancelled.
28. Open=0 + Received>0 → RECEIVED.
29. Completion Outcome distinguishes FULLY_RECEIVED vs RECEIVED_WITH_CANCELLATION.
30. Open=0 + Received=0 → CANCELLED / FULLY_CANCELLED.
31. Posted Receipt correction uses Reversal.
32. Receipt Reversal atomically reverses PO + Inventory.
33. Reversal blocked if Inventory would become negative.
34. Actual WAREHOUSE_RECEIPT derives from Posted Receipt.
35. Header Status is summary; Schedule detail is operational truth.
36. Package 13 does not execute payments, FX, accounting or cash flow.
37. Conversion revalidation blocks Source/Readiness/Price/MOQ/LeadTime/BaseUnit drift with `DECISION_RECONFIRMATION_REQUIRED`; it never refreshes the Decision silently.
38. Each batch PO group is an independent atomic transaction with explicit per-group result and idempotent retry.
39. Receipt Effective Date obeys `OrderedDate <= EffectiveDate <= TehranToday` using server-owned Tehran date.
40. Inactive Item settlement/correction is allowed only for an existing ORDERED commitment with warning; no new commitment or quantity expansion is allowed.
41. Actual milestones follow the canonical MRP-09 sequence, reject future dates and use immutable append-only supersession for correction.
42. Receipt Number is immutable server-generated `RCV-{sequence:D6}`.

---

## 112. Open Question Resolution

The previous open question:

`Over-Receipt`

is now CLOSED for v1.

Approved:

```text
Tolerance = 0
Receipt Qty > Schedule Open Qty → BLOCK
```

The previous open question:

`Recommendation → Draft PO`

is now CLOSED.

Approved:

```text
Current valid ACCEPT/MODIFY
→ controlled idempotent conversion
→ one Decision = one PO Line
```

The previous open question:

`Receipt allocation across split schedules`

is now CLOSED.

Approved:

```text
Each Receipt Line must reference exact PO Line + Delivery Schedule.
```

The previous open question:

`Destination Warehouse`

is now CLOSED.

Approved:

```text
One Destination Warehouse per Receipt Document.
Multiple Warehouses require multiple Receipts.
```

Receipt scope is also CLOSED:

```text
One Receipt = one Purchase Order + one Destination Warehouse.
All Receipt Lines belong to that PO.
```

The MRP-14 PO-level financial anchor aggregation is also CLOSED:

```text
Resolve every commercially required Schedule independently:
Actual → Latest Revised → Original → Unresolved.

Exclude a fully cancelled Schedule with no remaining obligation.
PO-level event date = MAX(resolved required Schedule dates).
Any unresolved required Schedule = unresolved PO-level anchor.
```

For actual `WAREHOUSE_RECEIPT`, the existing operational Final Receipt Date remains authoritative when completion exists. MRP-14 consumes these operational facts and does not create or edit a parallel financial milestone.

The previous open question:

`Mixed Partial Receipt + Cancellation final status`

is now CLOSED.

Approved:

```text
Open = 0 and Received > 0
→ Status = RECEIVED

Completion Outcome:
FULLY_RECEIVED
or
RECEIVED_WITH_CANCELLATION
```

---

## 113. Remaining Relevant Open Questions

Intentionally deferred beyond MRP-13:

- Quality inspection / quarantine before available stock
- supplier tolerance policy if business later wants controlled over-receipt
- Production-specific returns
- landed cost/accounting treatment
- payment execution
- cash-flow calculation
- lead-time analytics/statistics

None blocks MRP-13 baseline.

Canonical PO/Receipt regression fixtures are read-only in `docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md`: GD-05, the GD-08 PO/Receipt sequence, GD-09 PO snapshot and GD-10 currency isolation. Their approved inputs and expected outputs must not be silently changed.

---

# B. Prompt 13A — Purchase Order Operations Analysis

```text
# MRP-13A — Purchase Order Operations & Receipt Analysis

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
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume previous Packages were implemented merely because they were planned.

TASK:
Analyze MRP-13 Purchase Order Operations & Receipt.

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

1. Understand the end-to-end contract:
   MRP Run → Recommendation → Expert Decision → PO → Receipt → Inventory.
2. Inspect actual MRP-08, MRP-09 and MRP-12 implementations.
3. Identify any mismatch between planned foundation and repository reality.
4. Design Recommendation→Draft PO idempotent conversion.
5. Propose conversion uniqueness/concurrency mechanism.
6. Design batch grouping by:
   - Supplier
   - Currency
   - Payment Snapshot
   - Incoterm
   - Shipping Mode
   Confirm Payment Snapshot includes Credit Anchor Event and does not drift; WAREHOUSE_RECEIPT uses operational Final Receipt Date.
7. Confirm one Expert Decision → one PO Line.
8. Design lineage references.
9. Analyze Draft editability and deviation-from-decision audit.
10. Design Approval and Return-to-Draft command.
11. Design ORDERED commitment command.
12. Define exact field mutability by DRAFT / APPROVED / ORDERED.
13. Confirm Firm Supply begins only at ORDERED.
14. Design default Delivery Schedule creation from Expert Target Receipt.
15. Design Original Planned milestone generation at ORDERED using Ordered Date + lead-time snapshot.
    Use exactly four approved stage boundaries; diagnostic milestones must not create gaps, overlaps or double counting.
    - ORDERED → SUPPLIER_READY
    - SUPPLIER_READY → SHIPPED
    - SHIPPED → ARRIVED_DESTINATION
    - ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
16. Design split/reallocation before and after ORDERED.
17. Define schedule quantity reconciliation under:
    - split
    - receipt
    - cancellation
18. Design deterministic Header Status aggregation.
19. Define Schedule operational state if needed without creating redundant manual status.
20. Design Completion Outcome.
21. Design Receipt entity:
    - identity/number
    - Draft/Posted/Reversed
    - one Warehouse
22. Design exact Receipt Line → PO Line + Schedule relation.
23. Design Receipt posting atomic transaction across Procurement + Inventory.
24. Reuse MRP-08 Inventory RECEIPT model; do not create a second stock model.
25. Define Partial Receipt behavior.
26. Implement Over-Receipt tolerance=0.
27. Analyze physical excess/supplemental commitment boundary.
28. Design Schedule cancellation command and audit.
29. Design cancel-after-partial.
30. Design Receipt Reversal across PO + Inventory.
31. Define Reversal Firm Supply restoration conditions.
32. Validate negative-stock guard on reversal.
33. Design Actual Milestone update/correction.
34. Define milestone chronology validation.
35. Derive WAREHOUSE_RECEIPT from Posted Receipt.
36. Define First Receipt / Final Receipt derivations.
    Analyze partial receipts, receipt plus cancellation, full cancellation, reversal of the completing Receipt, re-completion after reversal, and financial-anchor resolution. Final Receipt is derived from valid Posted/non-reversed history; First Receipt and Cancellation Date are never the WAREHOUSE_RECEIPT credit anchor. Do not add a manually editable Financial Receipt Date.
37. Verify Receipt backdating uses the locked MRP-08 end-of-day negative-stock rule, deterministic Item+Warehouse locking and DB protection without a parallel policy.
    If materially unresolved, STOP before implementation.
38. Analyze inactive Item/Warehouse handling.
39. Analyze Receipt vs Cancellation concurrency.
40. Analyze two concurrent Receipts against same Schedule.
41. Analyze schedule split vs Receipt concurrency.
42. Design source typed link from Inventory transaction to Receipt.
43. Define APIs/commands.
44. Define PO/Receipt UI.
45. Define permissions/audit.
46. Define database constraints/indexes.
47. Numeric/date handling.
48. Performance.
49. Testing plan.
50. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..13 and actual Repository.
51. Smallest implementation sequence.
52. Explicit non-scope.

Testing proposal must cover at minimum:

CONVERSION
- Accept → Draft PO
- Modify → Draft PO
- Reject/Defer blocked
- outdated blocked
- blocked recommendation blocked
- duplicate conversion prevented
- batch grouping
- group mismatch splits/blocks correctly
- one Decision → one Line

PO LIFECYCLE
- Draft edit
- Approve
- Return to Draft + Reason
- Ordered
- Firm Supply starts only at Ordered
- committed fields immutable after Ordered

SCHEDULE
- default schedule
- split before Ordered
- split after Ordered
- quantity reconciliation
- received qty cannot move
- revised ETA trace

STATUS
- Shipped
- Customs
- Partial
- Received
- Cancelled
- mixed receipt+cancellation outcome

RECEIPT
- Draft has no effect
- Post creates PO received effect + Inventory Receipt atomically
- partial receipt
- multiple partial receipts
- exact schedule allocation
- one Warehouse per Receipt
- over-receipt blocked

CANCELLATION
- partial cancel
- cancel after receipt
- fully cancelled
- cannot cancel received qty
- receipt vs cancellation concurrency

REVERSAL
- posted Receipt immutable
- reversal restores PO quantity appropriately
- Inventory reversal
- negative-stock block
- closed PO status recalculates after reversal

MILESTONES
- Original plan at Ordered
- revised plan trace
- actual milestone chronology
- Warehouse Receipt derived from actual Receipt

HISTORY
- old MRP Runs unchanged
- Decision/Recommendation lineage
- Receipt→Inventory trace

Output:
1. Understanding
2. Repository Findings
3. Conversion / Lineage Model
4. PO Lifecycle / Mutability
5. Schedule Model
6. Status / Completion Outcome
7. Receipt Model
8. Inventory Integration
9. Cancellation
10. Reversal
11. Milestones
12. Concurrency / Atomicity
13. Validation / Constraints
14. API Design
15. UI Design
16. Permissions / Audit
17. Performance
18. Tests
    Map GD-05, GD-08 PO/Receipt, GD-09 PO snapshot and GD-10 currency isolation to the actual model. Verify Draft/Approved are not Firm, Ordered is Firm, split reconciliation, partial Receipt, cancellation, over-receipt block, Final Receipt/Credit Anchor, reversal, negative-stock guard and Receipt→Inventory atomicity without changing expected values.
19. Risks / Contradictions
20. Implementation Plan
21. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 13B — Purchase Order Operations Implementation

```text
# MRP-13B — Implement Purchase Order Operations & Receipt

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
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-13A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only approved MRP-13 Purchase Order Operations & Receipt.

If the actual MRP-08 contract cannot be reused without weakening its locked Receipt/backdated Inventory integrity, STOP and report the material contradiction.

CONVERSION
Implement controlled idempotent conversion from eligible current:
- ACCEPT
- MODIFY

Block:
- REJECT
- DEFER
- OUTDATED_RUN
- BLOCKED
- already converted

Use the immutable Expert Decision snapshot as the commercial basis. Immediately before conversion, revalidate Source operational state/readiness, selected Price, MOQ, configured Lead Time and Base Unit. Any material drift must block the group with `DECISION_RECONFIRMATION_REQUIRED` plus its stable detail reason. Never refresh, reprice or recalculate silently; require a new MRP-12 Expert Decision.

Preserve:
MRP Run → Recommendation → Expert Decision → PO Line trace.

BATCH GROUPING
Combine Decisions only when approved grouping keys match:
- Supplier
- Currency
- Payment Snapshot
- Incoterm
- Shipping Mode

One Expert Decision = one PO Line.

Treat each resulting PO group as an independent atomic transaction. A failed group must not roll back an unrelated valid group. Return explicit per-group/per-Decision success or failure, and make retry idempotent for groups already committed.

Payment Snapshot must preserve Credit Anchor Event. Enforce Credit%>0 → CreditDays>0 + Anchor required and Credit%=0 → Anchor may be null. Implement WAREHOUSE_RECEIPT using operational Final Receipt Date only.

DRAFT PO
Create:
- Header snapshot
- Line snapshot
- one default Schedule per Line
- default Schedule Qty = Line Qty
- initial Target Receipt = Expert Final Target Receipt

Decision-derived Item, Supplier/Source, Currency, Unit Price, Ordered Qty, Payment Snapshot, Shipping Mode and Incoterm are read-only even in Draft. Changing any of them requires a new MRP-12 Expert Decision. Schedule split/date revision may change only planning distribution while total Ordered Qty remains fixed and requires Reason + durable audit lineage.

PO LIFECYCLE
Implement explicit commands:
- Approve
- Return to Draft with Reason
- Mark Ordered

DRAFT:
schedule planning editable only under the approved constrained rules; Decision-derived core fields immutable.

APPROVED:
not Firm Supply.
commercial/core fields locked.
return-to-draft required for material edit.

ORDERED:
Firm Supply begins.
Ordered Date recorded.
committed commercial/core fields immutable.

ORIGINAL PLAN
At ORDERED:
- generate/finalize Original Planned milestones from actual Ordered Date + snapshotted lead times
- preserve Expert Target separately
- Original Plan immutable
- use ORDERED→SUPPLIER_READY→SHIPPED→ARRIVED_DESTINATION→WAREHOUSE_RECEIPT stage boundaries
- keep HANDOVER_TO_FORWARDER and CUSTOMS_CLEARED diagnostic and non-additive
- preserve ORDERED → SUPPLIER_READY
- preserve SUPPLIER_READY → SHIPPED
- preserve SHIPPED → ARRIVED_DESTINATION
- preserve ARRIVED_DESTINATION → WAREHOUSE_RECEIPT

SCHEDULES
Implement:
- split/reallocation
- exact quantity reconciliation
- post-ORDERED changes require Reason/Audit
- received qty cannot move
- revised ETA allowed with Reason
- Original Plan preserved

STATUS
Implement deterministic header summary from:
- Open Qty
- Received Qty
- Cancelled Qty
- actual shipment/customs milestones

Do not expose free status dropdown for operational states.

COMPLETION OUTCOME
Implement:
- FULLY_RECEIVED
- RECEIVED_WITH_CANCELLATION
- FULLY_CANCELLED

RECEIPT
Implement Receipt entity:
- ID and immutable server-generated `RCV-{sequence:D6}` number
- DRAFT
- POSTED
- REVERSED
- exactly one Purchase Order
- one Destination Warehouse
- Effective Date
- Lines

All Lines must belong to that same PO. Validate the inclusive server-owned date rule:

`OrderedDate <= EffectiveDate <= TehranToday`

Future receipt posting is forbidden.

RECEIPT LINE
Must reference:
- PO Line
- exact Delivery Schedule
- Item
- Qty

POSTING
Post Receipt atomically:
1. validate Schedule Open Qty
2. mark Receipt Posted
3. increase received qty facts
4. reduce open Firm Supply
5. create MRP-08 Inventory RECEIPT
6. preserve typed source link
7. commit together

No half-commit.

PARTIAL
Support repeated partial Receipts.

OVER-RECEIPT
Tolerance = 0.

If Receipt Qty > Schedule Open Qty:
BLOCK.

Do not auto-create extra commercial qty.

CANCELLATION
Implement Schedule remaining-qty cancellation:
- Qty
- Reason
- actor/time
- cannot exceed Open Qty
- cannot cancel received qty

Support cancel after partial receipt.

OPEN QTY
Derived:
Ordered - Received - Cancelled

Never manually edit.

REVERSAL
Posted Receipt cannot be edited/deleted.

Implement controlled reversal:
- Reason
- preserve original
- reverse PO received effect
- reverse Inventory atomically
- recalculate Open Qty/status
- restore Firm Supply only where commercially valid
- block if Inventory would become negative

MILESTONES
Implement immutable Actual milestone history using the canonical MRP-09 sequence. Dates cannot be future and must be chronologically nondecreasing. Correction is append-only supersession with mandatory Reason/audit context; never edit or delete the original Actual record.

WAREHOUSE_RECEIPT actual is derived from Posted Receipt.
Do not allow competing manual warehouse-receipt actual date.

Derive First Receipt / Final Receipt dates.

Final Receipt derivation must:
- remain unresolved after partial Receipt
- use the last valid Posted Receipt required for completion, including RECEIVED_WITH_CANCELLATION; never use Cancellation Date
- return no anchor for FULLY_CANCELLED with zero received quantity
- become unresolved when reversal removes completion
- use a later completing valid Receipt after re-completion
- expose no separately editable Financial Receipt Date

Add future tests (do not substitute First Receipt): one full Receipt, multiple partial Receipts, Final Receipt + Credit Days, received-with-cancellation, fully cancelled, final-Receipt reversal, and new completion after reversal.

INACTIVE ENTITIES
For an already ORDERED PO, allow Receipt, Cancellation and Receipt Reversal for an inactive Item with explicit `INACTIVE_ITEM_EXISTING_COMMITMENT` warning. Block new procurement commitment and Ordered Qty expansion for inactive Item. Preserve existing inactive Warehouse/Supplier policies without breaking historical resolution.

BACKEND
Implement only approved:
- domain/data model
- EF Core configuration
- migrations
- commands/application services
- transactional integration with Inventory
- APIs
- validation
- concurrency
- errors

FRONTEND
Implement approved:
- PO list/detail
- Decision lineage
- Draft edit
- Approve
- Return to Draft
- Ordered
- Schedules
- milestone timeline
- ETA revision
- Receipt list/detail
- Receipt Draft/Post
- Cancellation
- Receipt reversal
- Completion Outcome
- warnings/errors

Follow UI-BRAND-RULES.md.
Support RTL/Jalali.

PERMISSIONS/AUDIT
Reuse existing RBAC/Audit.
Do not build parallel infrastructure.

TESTS
Implement approved numerical/transactional scenarios, especially:
- canonical GD-05, GD-08 PO/Receipt, GD-09 PO snapshot and GD-10 currency-isolation regressions with independently encoded expected values
- conversion revalidation for Source/readiness/Price/MOQ/LeadTime/BaseUnit drift, stable reasons and no silent refresh
- duplicate conversion concurrency
- batch grouping, independent group atomicity, partial batch success and idempotent retry
- Decision-derived Draft core-field immutability and schedule-only Reasoned deviation
- Firm starts at Ordered
- committed immutability
- schedule split/reconcile
- two concurrent Receipts
- Receipt vs Cancel concurrency
- over-receipt block
- partial Receipt
- Receipt Inventory atomicity
- one-PO/one-Warehouse Receipt scope at API and direct DB levels
- inclusive OrderedDate/TehranToday Receipt boundaries and future-date rejection
- cancellation
- mixed completion outcome
- reversal
- reversal negative-stock block
- derived Warehouse Receipt milestone
- inactive Item existing-commitment settlement warning and new-commitment/qty-expansion block
- Actual milestone chronology, future-date block, immutable original and append-only correction
- old MRP Run immutability
- lineage integrity

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend build/lint
- startup/health
- end-to-end deterministic fixture:
  Expert Accept
  → Draft PO
  → Approve
  → Ordered
  → partial Receipt
  → Inventory
  → cancel remaining or final Receipt
  → status/outcome

Do not claim unrun checks.

SCOPE GUARD
Do NOT implement:
- payment execution
- cash flow
- FX
- accounting
- valuation
- landed cost
- over-receipt tolerance >0
- quality quarantine
- WMS/barcode
- AI procurement

Final report:
- Changed
- Conversion / Lineage
- PO Lifecycle
- Schedules
- Status / Completion
- Receipt
- Inventory Integration
- Cancellation
- Reversal
- Milestones
- API
- UI
- Concurrency
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-14.
```

---

# D. Prompt 13C — Purchase Order Operations Independent Audit

```text
# MRP-13C — Purchase Order Operations & Receipt Independent Audit

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
- docs/modules/mrp/golden/MRP-GOLDEN-DATASET-V1-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-13A Analysis
- MRP-13B implementation report

Inspect all MRP-13 implementation files.

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
   - cash flow
   - FX
   - accounting
   - inventory valuation
   - landed cost
   - AI purchasing
   - over-receipt tolerance >0

2. Conversion
   - only ACCEPT/MODIFY
   - current/non-blocked/non-outdated
   - idempotent
   - duplicate conversion prevented
   - cancelled PO does not automatically reuse old decision
   - immutable Expert Decision snapshot remains the commercial basis
   - Source/readiness/Price/MOQ/LeadTime/BaseUnit drift blocks with stable reconfirmation reason
   - no silent refresh/reprice/recalculation

3. Batch Grouping
   - exact approved keys
   - no mixed Supplier/Currency/Payment/Incoterm/Mode
   - one Decision → one Line
   - Payment Snapshot includes historical Credit Anchor Event
   - Credit Anchor conditional validation is enforced
   - WAREHOUSE_RECEIPT uses operational Final Receipt Date; First Receipt and Cancellation Date are never anchors
   - every resulting PO group is independently atomic
   - valid independent groups may commit when another group fails
   - explicit per-group results and idempotent retry prevent duplication

4. Lineage
   - PO Line → Decision → Recommendation → MRP Run
   - historical trace preserved

5. PO Lifecycle
   - Decision-derived Draft core fields read-only
   - schedule split/date revision only with unchanged OrderedQty, Reason and lineage
   - Approved internal only
   - Approved not Firm
   - Return to Draft requires Reason
   - Ordered begins Firm
   - committed fields immutable

6. Original Plan
   - frozen at Ordered
   - based on actual Ordered Date + snapshot lead times
   - Expert Target preserved separately
   - four approved stage boundaries reconstruct without gap/overlap
   - diagnostic Handover/Customs milestones are not double-counted
   - ORDERED → SUPPLIER_READY
   - SUPPLIER_READY → SHIPPED
   - SHIPPED → ARRIVED_DESTINATION
   - ARRIVED_DESTINATION → WAREHOUSE_RECEIPT

7. Schedules
   - default schedule
   - split support
   - quantity reconciliation
   - post-Ordered audit
   - received qty immutable
   - revised ETA trace
   - no quantity creation/loss

8. Header Status
   - deterministic summary
   - not manual dropdown
   - split schedule truth preserved

9. Completion Outcome
   - fully received
   - received with cancellation
   - fully cancelled

10. Receipt Entity
    - Draft/Posted/Reversed
    - immutable `RCV-{sequence:D6}` identity
    - one Purchase Order and one Warehouse
    - all Lines belong to the Receipt PO
    - OrderedDate <= EffectiveDate <= server TehranToday
    - exact Line+Schedule allocation

11. Receipt Posting
    - atomic PO + Inventory
    - Draft has no effect
    - Partial receipt correct
    - Firm open qty reduced
    - typed Inventory source link

12. Over-Receipt
    - tolerance zero
    - > Open Qty blocked
    - no hidden auto-expansion

13. Cancellation
    - only remaining qty
    - Reason
    - cancel after partial
    - received qty preserved
    - status recalculated

14. Reversal
    - original preserved
    - PO + Inventory atomic reverse
    - negative-stock guard
    - valid Firm supply restoration only
    - status recalculated

15. Milestone Analytics Compatibility
    - actual stages reconstruct as ORDERED→SUPPLIER_READY, SUPPLIER_READY→SHIPPED, SHIPPED→ARRIVED_DESTINATION, ARRIVED_DESTINATION→WAREHOUSE_RECEIPT
    - WAREHOUSE_RECEIPT remains derived from Posted Receipt
    - WAREHOUSE_RECEIPT Credit Anchor uses operational Final Receipt Date
    - partial Receipt leaves anchor unresolved; reversal removes a stale final anchor; later re-completion establishes a new anchor
    - received-with-cancellation uses the last required valid Receipt date, fully cancelled has no anchor, and no manual Financial Receipt Date exists

16. Milestones
    - Original/Revised/Actual distinct
    - canonical chronology and future-date validation
    - immutable originals and append-only correction/supersession with Reason
    - Warehouse Receipt derived from Posted Receipt
    - First/Final Receipt derivable

17. Historical Integrity
    - old MRP Runs unchanged
    - old Recommendation/Decision unchanged
    - current Master changes do not rewrite PO commitment

18. Database Integrity
    - conversion uniqueness
    - quantity constraints
    - schedule reconciliation
    - receipt allocation FKs
    - one-PO/one-Warehouse Receipt scope
    - Receipt date boundary protection
    - Decision-derived Draft immutability
    - Actual milestone append-only supersession
    - reversal links
    - no destructive cascades

19. Concurrency
    - two Receipts cannot overconsume open qty
    - Receipt vs Cancel safe
    - split vs Receipt safe
    - conversion race safe
    - independent batch-group atomicity and retry idempotency

20. Inactive Item Existing Commitment
    - Receipt/Cancellation/Reversal allowed after ORDERED with explicit warning
    - no new commitment or Ordered Qty expansion

21. API
    - command boundaries
    - no unrestricted status/qty mutation
    - server-side validation

22. UI
    - correct actions by lifecycle
    - read-only committed values
    - Decision lineage
    - Schedule detail
    - Receipt Post explicit
    - Reversal/Cancellation Reasons
    - RTL/Jalali/brand rules

23. Permissions / Auditability

24. Performance
    - no N+1 Schedule/Receipt loading
    - correct transactional locking without unnecessary distributed infra

25. Tests
    Verify meaningful transactional assertions especially:
    - Golden inputs/outputs exactly match GD-05, GD-08 PO/Receipt, GD-09 PO snapshot and GD-10
    - expected results are independent and not generated by production logic
    - conversion idempotency
    - all conversion drift/reconfirmation reasons and no silent refresh
    - batch partial success, per-group rollback and retry idempotency
    - Decision-derived Draft immutability
    - Firm at Ordered
    - receipt/inventory atomicity
    - one-PO/one-Warehouse and Receipt date direct-DB guards
    - over-receipt
    - cancellation
    - reversal
    - inactive Item settlement vs new commitment/expansion
    - milestone chronology/future-date/supersession immutability
    - concurrency

26. Golden and End-to-End Integrity
    - GD-05 fixed PO/split/partial/cancel/final/reversal/re-completion/Credit Anchor outputs
    - GD-08 newer ORDERED Schedule and Receipts feed later MRP Runs without duplicate procurement
    - expected values are encoded independently and never changed to fit implementation

27. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..13
    - approved MRP-13A
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
- Procurement / Inventory Integrity
- Historical Trace Integrity
- Concurrency Integrity
- Specification Compliance
- Scope Compliance
- Recommendation

Do not modify anything.
STOP.
```

---

# E. Final Pre-Codex Audit — MRP-13

## Result

`PASS`

## Audited Approved Decisions

تمام ۳۳ تصمیم تأییدشده Package 13 پوشش داده شده‌اند:

1. Valid ACCEPT/MODIFY conversion only.
2. Controlled batch conversion.
3. One Decision → one PO Line lineage.
4. Idempotent conversion.
5. Decision-derived Draft core fields immutable; schedule planning changes controlled / Approved return-to-draft controlled.
6. Ordered commercial lock.
7. Firm Supply starts at Ordered.
8. Default Schedule creation.
9. Original Plan locked at Ordered.
10. Split Schedule before/after Ordered.
11. Command-controlled early lifecycle + fact-derived operational summary.
12. Receipt Document lifecycle.
13. One Purchase Order and one Destination Warehouse per Receipt.
14. Exact Receipt Line→PO Line+Schedule allocation.
15. Atomic PO+Inventory Receipt posting.
16. Partial Receipt.
17. Over-Receipt tolerance = 0.
18. Physical excess requires valid supplemental commitment.
19. Cancellation on remaining Schedule Qty.
20. Cancel after partial supported.
21. RECEIVED + Completion Outcome for mixed completion.
22. Receipt correction via Reversal.
23. Reversal negative-stock guard.
24. Actual milestones + derived Warehouse Receipt.
25. Header Status is Summary; Schedule detail is operational truth.
26. No payment/FX/accounting/cash-flow execution.
27. Conversion revalidation blocks material operational drift without silent refresh.
28. Independent atomic PO groups with explicit partial-batch results and idempotent retry.
29. Receipt Effective Date obeys inclusive OrderedDate..server TehranToday.
30. Existing ORDERED commitments remain settleable for inactive Items with warning; no expansion/new commitment.
31. Actual milestone chronology/future-date rules are mandatory.
32. Actual milestone correction is append-only supersession with Reason/audit.
33. GD-05 and GD-08 are fixed implementation/audit gates.

## Open Question Resolution

### Over-Receipt

CLOSED:

```text
Tolerance = 0
Receipt > Schedule Open Qty → BLOCK
```

### Recommendation → Draft PO

CLOSED:

```text
current valid ACCEPT/MODIFY
→ idempotent controlled conversion
```

### Receipt Allocation

CLOSED:

```text
Receipt Line
→ exact PO Line + Delivery Schedule
```

### Destination Warehouse

CLOSED:

```text
one Purchase Order + one Destination Warehouse per Receipt Document
```

### Mixed Partial Receipt + Cancellation

CLOSED:

```text
Open=0 and Received>0
→ Status RECEIVED

Outcome:
FULLY_RECEIVED
or
RECEIVED_WITH_CANCELLATION
```

## Cross-Package Consistency

- MRP-12 remains owner of approved procurement decision.
- MRP-13 creates the actual commercial commitment.
- MRP-09 Firm Supply semantics remain unchanged.
- MRP-08 remains sole Actual Inventory source of truth.
- Receipt posting connects Procurement and Inventory atomically.
- MRP-10 consumes updated Firm Supply only on a future new Run.
- Historical MRP Runs remain immutable.
- PO snapshots remain independent of future Sourcing changes.
- Package 14 can consume actual milestone/payment snapshot facts without changing operational ownership.

No material ownership conflict was found.

## Architecture Corrections Applied

- PO conversion is idempotent and lineage-preserving.
- Batch grouping cannot mix incompatible commercial/logistics terms.
- Firm Supply begins only with actual supplier commitment at ORDERED.
- Original Plan is tied to actual Ordered Date, not hypothetical Draft timing.
- Receipt is a real transactional document, not a direct Qty edit.
- Schedule-level receipt allocation prevents split-delivery ambiguity.
- PO Received Qty and Inventory are updated atomically.
- Over-receipt cannot silently distort commercial commitment.
- Cancellation preserves Ordered/Received history.
- Completion outcome solves mixed receipt+cancellation without status explosion.
- Reversal preserves ledger history and Inventory integrity.
- WAREHOUSE_RECEIPT actual derives from physical posted receipt.
- WAREHOUSE_RECEIPT financial Credit Anchor derives from Final Receipt Date, never First Receipt or Cancellation Date.
- Reversal invalidates a stale final anchor when completion is lost; re-completion establishes the new final date.
- Header status remains a summary over Schedule facts.

## Remaining Intentional Open Questions

Deferred beyond Package 13:

- future quality/quarantine workflow
- possible future controlled supplier over-receipt tolerance
- landed cost/accounting
- payment execution
- cash-flow scheduling
- future lead-time analytics application/integration beyond the locked MRP-14 policy
- Production-return integration

No unresolved MRP-13 baseline issue remains.

## Prompt Audit

### MRP-13A
PASS

Analysis-only, Repository-aware, and completed with Owner-approved decisions for conversion revalidation, Draft immutability, batch atomicity, Receipt scope/date, inactive-Item settlement and immutable Actual milestone correction.

### MRP-13B
PASS

Implementation is strictly bounded to PO Operations + Receipt + Inventory integration, explicitly tests GD-05/GD-08 and the Owner decisions, and blocks financial/accounting scope.

### MRP-13C
PASS

Independent read-only audit checks conversion revalidation/idempotency, independent batch-group atomicity, Decision-derived Draft immutability, lifecycle semantics, Firm Supply, Receipt scope/date, split schedules, Receipt/Inventory atomicity, inactive-Item settlement, append-only milestones, cancellation, reversal, concurrency, direct-DB integrity, GD-05/GD-08 and historical trace.

## Final Decision

`MRP-13 Purchase Order Operations & Receipt` is approved for the project Prompt Baseline.

MRP-13A is Owner-approved and synchronized. `MRP-13B Readiness = READY`; MRP-13 implementation and MRP-14 implementation have not started.
