# Hadron ERP — MRP Package 06 Final

**Package:** MRP-06 Multi-Level BOM  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

## Owner Correction — Current BOM Authority (2026-08-30)

این تصمیم، تمام wording قدیمی Effectivity در ادامه این سند را supersede می‌کند:

- BOM هیچ `EffectiveFrom` / `EffectiveTo` یا تاریخ کسب‌وکاری ندارد؛ تاریخ فقط Audit/تاریخچه است.
- هر Parent حداکثر یک Revision جاری `APPROVED` دارد.
- تأیید Revision جدید، Revision جاری قبلی را در همان تراکنش `OBSOLETE` و Revision جدید را `APPROVED` می‌کند.
- Draft قابل ویرایش است؛ Approved/Obsolete و Lines آنها immutable هستند.
- current BOM با Parent حل می‌شود، نه با business date.
- MRP Run، BOM جاری زمان اجرای خود را snapshot می‌کند؛ Run تاریخی از BOM زنده بازسازی نمی‌شود.
- Create/Edit/Copy/Approve/Obsolete/API/UI هیچ فیلد یا کنترل تاریخ BOM ندارد.
- cycle validation روی گراف BOMهای جاری Released و candidate انجام می‌شود.

هر بخش، Prompt یا Acceptance Criteria پایین‌تر که BOM را effective-dated، future-effective یا date-resolved معرفی می‌کند، legacy و غیرقابل اجرا است.

---

# A. Final Feature Specification — 06-MULTI-LEVEL-BOM

## 1. Purpose

این Feature ساختار مهندسی و مصرف مواد MRP را ایجاد می‌کند.

BOM باید بتواند مشخص کند یک Parent Item برای تولید مقدار مشخصی Output به چه Child Itemهایی و با چه Usage Quantity نیاز دارد.

این Feature باید:

- Multi-Level BOM
- Revision / Versioning
- Current Approved Revision per Parent
- Approval lifecycle
- Base Output Quantity
- Usage Quantity
- Circular Dependency protection
- Scrap Override boundary
- Historical traceability

را ایجاد کند.

این Feature نباید هنوز:

- Demand Explosion Engine
- Scrap calculation formula
- Forecast
- Inventory
- Order Planning
- Purchase Order
- Hybrid route selection

را پیاده‌سازی کند.

---

## 2. BOM Parent Types

### LOCKED DECISION

BOM می‌تواند برای Parent Typeهای زیر وجود داشته باشد:

- Product
- Part
- Component

این قابلیت پایه Multi-Level BOM است.

---

## 3. Allowed Parent → Child Relationships

### LOCKED DECISION

Baseline allowed relationships:

```text
Product   → Part
Part      → Component
Component → Component
```

این Rule باید Definitionهای Business را حفظ کند:

- Product از Partهای Final Assembly تشکیل می‌شود.
- Part از Componentها ساخته می‌شود.
- Component می‌تواند از Componentهای پایین‌تر ساخته شود.

Baseline MRP-06 نباید بدون تصمیم جدید روابط دیگری مانند:

```text
Product → Component
Part → Part
Component → Part
```

را مجاز کند.

---

## 4. Recursive Multi-Level BOM

### LOCKED DECISION

BOM Engine architecture نباید به تعداد ثابت Level محدود شود.

Example:

```text
Product
  ↓
Part
  ↓
Component A
  ↓
Component B
  ↓
Component C
```

MRP-06 فقط ساختار معتبر Recursive را ذخیره و Validate می‌کند.

Actual multi-level demand explosion در Planning Engine آینده انجام می‌شود.

---

## 5. BOM Header

Conceptual BOM Revision Header شامل حداقل:

- BOM Technical ID
- Parent Item
- Revision Identifier
- Status
- Base Output Qty
- Effective From
- Effective To
- Notes / Revision Notes where appropriate
- Created At
- Updated At
- Approved At where available
- Approved By where existing audit/user infrastructure supports it

Exact physical table naming is a technical design decision for MRP-06A.

---

## 6. BOM Revision

### LOCKED DECISION

هر Parent Item می‌تواند چند BOM Revision داشته باشد.

Examples:

```text
REV-1
REV-2
REV-3
```

Revision identity must be stable.

Exact revision-number generation strategy is proposed in Codex Analysis.

Revision history must not be overwritten.

---

## 7. BOM Status

### LOCKED DECISION

Baseline statuses:

```text
Draft
Approved
Obsolete
```

Technical machine codes must be stable.

Suggested machine codes:

```text
DRAFT
APPROVED
OBSOLETE
```

Display labels may be localized.

These are System-controlled workflow states, not user-configurable Reference Data.

---

## 8. Status Meaning

### Draft

Editable working revision.

### Approved

Released revision that may become effective according to its date range.

### Obsolete

Revision intentionally retired from future use.

Historical records remain resolvable.

---

## 9. Approved BOM Immutability

### LOCKED DECISION

An Approved BOM Revision must not be directly edited.

If a released BOM needs change:

```text
Approved Revision
      ↓
Create New Draft Revision
      ↓
Edit New Revision
      ↓
Approve New Revision
```

Historical Approved content must remain intact.

This applies to:

- Header planning fields
- BOM Lines
- Usage Qty
- Scrap Override
- Effective logic

Any allowed administrative correction to historical data would require a separate controlled correction process and is not part of normal MRP-06 UI.

The only approved operational exception is the explicit atomic effectivity closure defined in §51. It may change only `Effective To` on exactly one preceding open-ended Approved Revision while approving its confirmed successor. It does not permit editing released BOM content, quantities, units, Lines or other effectivity fields.

---

## 10. Obsolete Revision

Obsoleting a Revision must not delete it.

Operational effective resolution uses only `APPROVED` Revisions.

Historical resolution may resolve `APPROVED` or `OBSOLETE` Revisions by business date. Obsoleting a current or future Revision therefore requires an explicit `Retire From Date`. Where the Revision would otherwise remain effective on or after that date, the controlled Obsolete transaction closes its `Effective To` to:

```text
Retire From Date - 1 calendar day
```

The closure, status transition and audit event must be atomic. Released historical ranges must remain non-overlapping. Obsolete must not erase or rewrite the period during which the Revision was historically effective.

Historical:

- MRP references
- reports
- audits
- future transactional traces

must remain able to identify the exact Revision.

MRP-06 does not implement downstream transactional references yet, but must preserve this capability.

---

## 11. Effective From / Effective To

### LOCKED DECISION

Every Approved Revision must have an Effective period.

Fields:

- Effective From
- Effective To

Rules:

- Effective From required for Approved Revision.
- Effective To optional/open-ended.
- Effective To cannot be earlier than Effective From.
- For a given Parent Item and business date, at most one Approved BOM Revision may be effective.

Approved effective ranges for the same Parent must not overlap.

Released historical ranges across `APPROVED` and `OBSOLETE` Revisions must also remain non-overlapping.

---

## 12. Future Effective Revision

### LOCKED DECISION

A Revision may be Approved today but become effective in the future.

Example:

```text
REV-1
Effective through 2026-09-30

REV-2
Approved today
Effective From 2026-10-01
```

Future Planning must later select BOM based on Demand Date, not simply "latest revision".

---

## 13. Effective BOM Resolution

Future Planning rule:

```text
Effective BOM
=
Approved Revision
whose Effective Date Range contains the Demand Date
```

MRP-06 must provide a deterministic way to resolve this Revision by date.

It must NOT create a manually maintained `Current BOM` flag that can conflict with Effective Dates.

---

## 14. Date Storage

Canonical date storage:

`Gregorian`

Business timezone:

`Asia/Tehran`

Jalali may be displayed/input through shared date infrastructure.

BOM validity logic must not depend on formatted Persian date strings.

---

## 15. Base Output Qty

### LOCKED DECISION

Every BOM Revision has:

`Base Output Qty`

Default:

```text
1
```

Rule:

```text
Base Output Qty > 0
```

Use decimal/numeric representation.

Purpose:

Usage Qty on BOM Lines is expressed relative to this Output Quantity.

---

## 16. Base Output Qty Example

Instead of approximating:

```text
Master Carton Usage = 0.083333
```

for one carton per 12 products, BOM may be represented as:

```text
Base Output Qty = 12

Housing        = 12
PCBA           = 12
Retail Box     = 12
Master Carton  = 1
```

This preserves exact ratios where practical.

Base Output Qty = 1 remains valid and common.

---

## 17. BOM Line

Conceptual BOM Line fields:

- BOM Revision
- Child Item
- Usage Qty
- Scrap Override %
- optional display/sort order if approved by implementation design
- Created/Updated metadata where platform conventions require it

Do not duplicate Child Item Name/Code as stored snapshot fields in Master BOM unless a later historical transaction requires snapshotting.

Historical Item identity remains resolvable by stable Item ID.

---

## 18. Usage Qty

### LOCKED DECISION

Every BOM Line must have:

`Usage Qty`

Rule:

```text
Usage Qty > 0
```

Use decimal/numeric.

Usage is expressed in Child Item Base Unit for the BOM Base Output Qty.

Conceptual normalized usage before scrap:

```text
Normalized Usage Per Parent Unit
=
Usage Qty
/
Base Output Qty
```

MRP-06 does not yet calculate demand explosion.

---

## 19. Child Unit

BOM Line must not independently redefine the Child Item Base Unit.

Usage Qty uses the Child Item Base Unit defined in Item Master.

MRP-06 stores server-captured immutable unit snapshots:

- `ParentBaseUnitId` on the BOM Revision
- `ChildBaseUnitId` on each BOM Line

Clients cannot assign or edit these snapshots. They preserve the unit meaning of `Base Output Qty` and `Usage Qty` without creating an alternate Unit source of truth.

If the current Item Base Unit no longer matches its captured snapshot:

```text
BASE_UNIT_CHANGED
```

must block operational BOM use. The system must not convert quantities, rewrite history or silently refresh snapshots.

Unit Conversion is not part of MRP-06.

If future engineering requires alternate BOM units, that must be introduced through an explicit Unit Conversion capability, not hidden inside BOM.

---

## 20. Duplicate Child Prevention

### LOCKED DECISION

The same Child Item may appear only once in a given BOM Revision.

Duplicate Child Lines are prohibited.

If total usage must be higher, Usage Qty should be represented on the single line.

Database/Application validation must enforce this.

---

## 21. Circular BOM

### LOCKED DECISION

Circular dependency is invalid.

Example:

```text
A → B
B → C
C → A
```

must not be approvable.

---

## 22. Circular Validation Timing

A Draft may temporarily contain a cycle while user is editing.

The UI/system should surface a warning.

However:

```text
Approval MUST be blocked
```

until the BOM graph is acyclic.

Cycle detection must consider the full relevant multi-level BOM graph, not only direct self-reference.

Cycle validation is effective-date-aware. Approval evaluates the candidate Revision against the released graph for every effective-date segment that may apply during the candidate range. Revision boundaries partition the relevant range; a representative business date from each stable segment is checked with full recursive traversal.

Approval and Obsolete operations participate in one shared transaction-level BOM graph lock. This serializes released-graph changes so concurrent operations cannot introduce a reciprocal or multi-level cycle between separate Parent Items.

---

## 23. Direct Self Reference

A direct line:

```text
A → A
```

is also invalid.

It should be blocked at the earliest practical validation point.

---

## 24. Scrap Override

### LOCKED DECISION

BOM Line may have:

`Scrap Override %`

Rules:

```text
NULL
=
use Item Default Planning Scrap %

0
=
explicit zero scrap

> 0
=
explicit BOM-specific scrap
```

Valid range when supplied:

```text
0 <= Scrap Override % <= 100
```

Use decimal/numeric.

---

## 25. Scrap Formula Boundary

### LOCKED DECISION

The higher canonical MRP Decisions lock the v1 material-requirement formula as:

```text
Planned Requirement
=
Base Requirement × (1 + Scrap % / 100)
```

MRP-06 must NOT calculate or apply this formula. It only stores and resolves:

- Item Default Planning Scrap %
- optional BOM Line Override %

and preserves NULL vs explicit 0 semantics for the future Planning Engine.

---

## 26. Scrap Effective Value

Conceptually, future Planning will resolve:

```text
Effective Scrap %
=
BOM Line Scrap Override
if not NULL
else
Item Default Planning Scrap %
```

MRP-06 may expose this as informational derived data, but must not apply the material-requirement formula.

---

## 27. Supply Method Compatibility

MRP-04 defines:

```text
BUY
MAKE
OUTSOURCE
HYBRID
```

### LOCKED DECISION

BOM compatibility baseline:

### Product

May have BOM.

Future Product planning requires a valid Approved effective BOM.

### MAKE

May/should have a BOM when MRP must plan internal inputs.

### OUTSOURCE

May have a BOM when Hadron must plan/provide/track input Components for outsourced assembly or processing.

### HYBRID

May have both:

- BOM route
- external Sourcing Options

### BUY

Does not require a planning BOM for normal external procurement.

A BUY Item may still have a BOM as engineering or historical data. MRP-06 must not block its creation, revisioning, approval or historical resolution solely because its current Supply Method is BUY.

Future normal BUY planning stops at the BUY Item and does not explode that BOM. MRP-06 does not implement that Planning behavior.

MRP-06 does not choose which route HYBRID will use.

---

## 28. HYBRID Route Boundary

### LOCKED PRINCIPLE

For:

```text
Supply Method = HYBRID
```

BOM and external Sourcing may coexist.

Example:

```text
PCBA

Route A:
Make via BOM

Route B:
Buy via Supplier
```

BOM Master only describes the BOM route.

Future Planning/Order Planning determines which route to use.

MRP-06 must not auto-select Make vs Buy.

---

## 29. OUTSOURCE BOM Meaning

An OUTSOURCE Item may have BOM if Hadron needs visibility/planning for lower-level inputs.

Example:

```text
PCB
LED
IC
CAP
   ↓
External Assembly
   ↓
PCBA
```

MRP-06 stores engineering/material structure only.

It does not implement outsourced service purchasing, process routing or logistics ownership.

---

## 30. Active Item Rules

### LOCKED DECISION

New BOM revisions may select only Active Child Items.

Historical BOMs remain valid/resolvable even if an Item is later deactivated.

Inactive historical Child Items must not disappear from old Approved Revisions.

---

## 31. Parent/Child Type Validation

MRP-06 must enforce the locked allowed relationships:

```text
Product → Part
Part → Component
Component → Component
```

Selection UI should filter valid active Child types to reduce user error.

Server-side validation remains mandatory.

---

## 32. Component Count

### LOCKED DECISION

There is no manually entered:

`Component Count`

or:

`Part Count`

field.

Line count is Derived:

```text
BOM Line Count
=
number of BOM Lines
```

The UI allows Add/Remove Lines.

---

## 33. Draft Editing

Draft Revision may support:

- add line
- remove line
- change Usage Qty
- change Scrap Override
- change Base Output Qty
- change planned Effective dates before approval
- reorder lines if UI supports display order

Draft editing must not mutate another Approved Revision.

---

## 34. Revision Creation

A new Revision may be created:

- blank
or
- by copying an existing Revision

Copying an Approved Revision into a new Draft is allowed and useful.

The copy must receive a new Revision identity and must not share mutable line records with the original.

---

## 35. Revision Notes / Change Reason

BOM Revision should support human-readable Revision Notes / Change Reason.

This supports traceability.

Whether Change Reason is mandatory at Approval is not currently a locked Business Rule and may be proposed in MRP-06A.

Codex must not invent approval bureaucracy beyond approved scope.

---

## 36. Approval Validation

Before status changes to Approved, at minimum validate:

- Parent/Child type rules
- Base Output Qty > 0
- at least one BOM Line where required by the chosen revision
- Usage Qty > 0
- no duplicate Child
- valid Scrap Override
- valid Effective dates
- no overlapping Approved effective Revision for same Parent
- no circular dependency
- Child Items were valid selections under active-item rules when revision was prepared/approved
- current Parent/Child Base Units match their immutable snapshots
- any confirmed predecessor closure satisfies §51 exactly

Approval uses the shared transaction-level BOM graph lock. Validation, optional confirmed predecessor closure, candidate Approval and audit persistence boundary must execute as one atomic transaction.

---

## 37. Zero-Line Draft

A Draft Revision may exist temporarily without Lines.

An Approved planning BOM should not be empty.

Approval of an empty BOM must be rejected.

---

## 38. Product Planning Eligibility Boundary

MRP-02 locked future rule:

```text
Planning Eligible Product
=
Active Product
+
Valid BOM
```

MRP-06 provides the BOM side of that future rule.

A "valid BOM" for a planning date means:

- Approved
- effective for the date
- structurally valid

MRP-06 does not implement Forecast eligibility UI.

---

## 39. BOM Resolution Service Boundary

MRP-06 should provide or make possible a deterministic application/domain use case conceptually like:

```text
ResolveEffectiveBom(parentItemId, businessDate)
```

Behavior:

- returns one Approved effective Revision
- returns no result if none exists
- must never return two ambiguous overlapping revisions

This is operational resolution and therefore considers `APPROVED` only.

Historical resolution is a separate explicit use case. It may return `APPROVED` or `OBSOLETE` for a business date, while preserving the status and exact released effective range in the response.

Exact API/domain design is proposed during Analysis.

---

## 40. Recursive Graph Query Boundary

MRP-06 must support validation/traversal needed for:

- cycle detection
- future multi-level explosion

But it must not prematurely implement large-scale MRP demand calculation.

Architecture should keep BOM graph access efficient and testable.

---

## 41. Historical Traceability

Approved/Obsolete BOM revisions must preserve enough identity to later answer:

```text
Which BOM Revision was effective on this date?
```

and future transactional systems must be able to reference the exact Revision.

Do not design history around mutable "current BOM" rows.

---

## 42. BOM Management UI

Required capabilities:

- BOM list/search
- filter by Parent Item
- filter by Item Type
- filter by Status
- filter by Effective Date
- create Revision
- copy Revision
- edit Draft
- approve Draft
- obsolete Revision
- view Approved/Obsolete Revision read-only
- view effective/current-by-date result
- manage Lines

Follow:

`docs/brand/UI-BRAND-RULES.md`

Reuse existing approved grid/form patterns.

---

## 43. BOM Editor UX

Recommended structure:

### Header

- Parent Item
- Revision
- Status
- Base Output Qty
- Effective From
- Effective To
- Revision Notes

### Lines

- Child Item
- Child Code/Name display
- Base Unit display
- Usage Qty
- Scrap Override %
- Effective Scrap % display if useful
- optional order

Child selection must respect Parent/Child type rules.

---

## 44. Approved Revision UX

Approved BOM should display as read-only.

Primary action for change:

```text
Create New Revision
```

not:

```text
Edit Approved
```

This must be clear in UI.

---

## 45. Effective Date UX

UI should make overlapping revisions difficult to create.

Future approved Revision should be clearly distinguishable from currently effective and historical/obsolete Revisions.

Example UI states may include informational labels:

- Future Approved
- Effective Now
- Historical

These are derived labels, not new machine workflow statuses.

---

## 46. Permissions

Architecture should distinguish at minimum:

- View BOM
- Manage Draft BOM
- Approve BOM

Approval is materially different from ordinary editing and should have a separate permission boundary if authorization infrastructure supports it.

Do not create a parallel Identity system.

---

## 47. Auditability

Important events must be compatible with Audit:

- Draft creation
- Revision copy
- line add/remove/change
- Base Output Qty change
- Effective date change
- Approval
- Obsolete action

Approved Revision content must be historically recoverable without relying only on generic audit logs.

The Revision itself is the primary historical record.

---

## 48. API / Application Use Cases

Required use cases include:

- list/search BOM Revisions
- get Revision detail
- create Draft Revision
- copy Revision to new Draft
- update Draft header
- manage Draft Lines
- validate Draft
- approve Draft
- approve Draft, optionally with the explicit confirmed predecessor closure contract from §51
- obsolete Revision with explicit Retire From Date
- resolve operational effective Approved Revision by date
- resolve historical released Approved/Obsolete Revision by date
- check cycle/structural validation

Avoid dangerous unrestricted generic CRUD.

---

## 49. Validation

Server-side validation is mandatory.

At minimum:

### Header
- valid Parent Item
- valid Parent Type
- Base Output Qty > 0
- valid status transition
- Effective From required on approval
- Effective To >= Effective From
- no overlapping Approved date ranges

### Lines
- Child Item required
- allowed Parent→Child type
- active Child for new selection
- Usage Qty > 0
- Scrap Override NULL or 0..100
- no duplicate Child in same Revision
- no direct self-reference

### Approval
- at least one Line
- no circular dependency
- no ambiguity in effectivity
- Base Unit snapshots still match current Parent/Child Base Units
- explicit predecessor identity/version/confirmation when closing an open-ended predecessor

---

## 50. Status Transitions

Baseline normal workflow:

```text
Draft
  ↓
Approved
  ↓
Obsolete
```

Approved does not return to Draft through normal UI.

To change an Approved BOM:

```text
New Draft Revision
```

Obsolete does not mean deleted.

Obsolete requires an explicit Retire From Date. Operational resolution excludes Obsolete; historical resolution preserves it for dates inside its released historical range. The controlled retirement transaction closes a current/future open range where necessary and preserves non-overlapping released history.

---

## 51. Effective Range Replacement

When a new future Revision is Approved, the system must not silently create overlapping ranges.

Example desired state:

```text
REV-1: 2026-01-01 → 2026-09-30
REV-2: 2026-10-01 → open
```

### LOCKED DECISION

Successor Approval may close exactly one preceding open-ended `APPROVED` Revision to:

```text
New Effective From - 1 calendar day
```

only when the Approval command supplies all of:

- explicit user confirmation
- `PreviousRevisionId`
- `ExpectedPreviousVersion`

The preceding Revision remains `APPROVED`. Its Lines and all other released content remain immutable.

The system must verify that the supplied Revision is the same Parent's single immediately preceding open-ended Approved Revision and that the proposed closure is not earlier than its Effective From. Previous closure and successor Approval occur in one transaction under the shared BOM graph lock. Both Version values advance according to the approved aggregate concurrency policy, and stale state returns structured conflict without partial mutation.

No predecessor is discovered and rewritten silently. No more than one predecessor may be closed. Every other overlap is blocked by application validation and the database released-range exclusion constraint.

---

## 52. Concurrency

Concurrency-sensitive operations include:

- Revision numbering
- BOM Approval
- effective-range overlap protection
- cycle validation against concurrently approved revisions
- explicit predecessor closure during successor Approval
- Obsolete retirement and released-range closure

Approval and Obsolete use the same transaction-level BOM graph lock. Revision rows and referenced Item rows are locked deterministically inside the transaction. Effective-range and cycle validation must be repeated after acquiring the lock. Database exclusion remains the final protection against released-range overlap.

---

## 53. Database Integrity

Expected database-level protections where practical:

- stable foreign keys to Parent/Child Item IDs
- unique Revision identity per Parent
- unique Child per Revision
- numeric constraints
- status integrity
- indexes for Parent + Status + effective dates
- immutable server-captured Parent/Child Base Unit foreign keys
- non-overlapping released historical ranges for Approved/Obsolete Revisions

Application/domain validation still handles graph-level rules such as recursive cycle detection.

---

## 54. Precision

Use decimal/numeric for:

- Base Output Qty
- Usage Qty
- Scrap Override %

Do not use binary floating point.

---

## 55. Performance

Support efficient:

- list revisions for one Parent
- resolve effective Revision by date
- load Lines for one Revision
- graph traversal for cycle checks
- future recursive explosion

Avoid:

- loading every BOM in the ERP for one edit
- N+1 line/item resolution
- premature materialized-path architecture unless justified
- speculative caching/Redis
- recursive client-side calculation of large planning datasets

---

## 56. Seed / Migration

Codex must not invent BOM data.

If legacy BOM records are not explicitly supplied:

- build BOM capability
- do not fabricate production BOMs

Legacy wide-sheet BOM migration will be a separate controlled import/migration task.

Legacy `Component Count` must not be migrated as a persistent business field.

---

## 57. Explicit Non-Scope

MRP-06 must NOT implement:

- Forecast
- Actual Production
- Daily demand distribution
- Material requirement explosion
- Scrap requirement formula
- Inventory
- Coverage
- Mapping
- Order Planning
- Purchase Orders
- Make-vs-Buy route selection
- Outsource service PO
- production routing
- work centers
- process capacity
- internal process lead-time engine
- Unit Conversion
- Packaging
- Cash Flow
- Lead-Time Analytics
- Workflow engine beyond BOM's minimal status lifecycle

---

## 58. Acceptance Criteria

MRP-06 is acceptable when:

1. Product/Part/Component may be BOM Parents as approved.
2. Allowed Parent→Child types are enforced.
3. Recursive multi-level structure is not depth-limited.
4. Multiple Revisions per Parent are supported.
5. Statuses are Draft/Approved/Obsolete.
6. Approved Revision is immutable.
7. New changes use a new Draft Revision.
8. Effective From/To are supported.
9. Approved effective ranges do not overlap for same Parent.
10. Future Approved Revision is supported.
11. Effective BOM resolves by business date.
12. No manual Current BOM flag.
13. Base Output Qty > 0.
14. Usage Qty > 0.
15. Usage uses the server-captured immutable Child Base Unit snapshot; Parent and Child snapshot drift blocks operational use with `BASE_UNIT_CHANGED`.
16. Duplicate Child lines are prevented.
17. Direct self-reference is prevented.
18. Recursive cycles block Approval.
19. Draft cycle may be saved with warning.
20. Scrap Override preserves NULL vs 0 semantics.
21. Scrap formula is NOT implemented.
22. Product/MAKE/OUTSOURCE/HYBRID BOM compatibility is preserved.
23. BUY does not require BOM, but BUY BOM is allowed as engineering/history data and MRP-06 does not block it.
24. HYBRID route selection is deferred.
25. New BOM selection uses active Items.
26. Historical inactive Items remain visible.
27. Component Count is derived, not stored.
28. Empty Draft allowed; empty Approved BOM rejected.
29. Approved/Obsolete history remains resolvable.
30. Server-side validation exists.
31. Concurrency-sensitive Approval/effectivity rules are tested.
32. No Planning/Inventory/PO logic leaks into MRP-06.
33. Confirmed successor Approval may atomically close exactly one preceding open-ended Approved range using predecessor identity and Expected Version; other overlaps are blocked.
34. Operational resolution uses Approved only; historical resolution may use Approved/Obsolete without overlapping released history.
35. Obsolete retirement requires explicit Retire From Date and preserves historical effectivity.
36. Effective-date-aware cycle validation and shared Approval/Obsolete locking prevent concurrent released-graph cycles.

---

## 59. Locked Decisions

The following are locked for MRP-06:

1. BOM Parent may be Product, Part or Component.
2. Allowed relationships are Product→Part, Part→Component, Component→Component.
3. BOM is recursive and not fixed-depth.
4. Multiple BOM Revisions per Parent.
5. Status = Draft / Approved / Obsolete.
6. Approved Revision is immutable.
7. Changes to Approved BOM require new Revision.
8. Every Approved Revision uses Effective From/To.
9. At most one Approved Revision effective per Parent/date.
10. Future Approved Revisions are supported.
11. Planning will select BOM by Demand Date.
12. Base Output Qty exists and defaults to 1.
13. Base Output Qty > 0.
14. Usage Qty > 0.
15. Usage Qty uses `ParentBaseUnitId` and `ChildBaseUnitId` server-captured immutable snapshots; drift produces `BASE_UNIT_CHANGED` without conversion or historical rewrite.
16. Duplicate Child per Revision prohibited.
17. Circular BOM prohibited.
18. Draft may warn on cycle; Approval must block.
19. Scrap Override optional.
20. NULL Scrap Override means Item default.
21. 0 Scrap Override means explicit zero.
22. The canonical v1 formula is additive, but MRP-06 does not calculate it and only owns NULL/default/explicit-zero Scrap resolution.
23. Product may have BOM.
24. MAKE may use BOM.
25. OUTSOURCE may use BOM where lower-level inputs must be planned.
26. HYBRID may have BOM and external Sourcing.
27. BUY does not require planning BOM, but its BOM is allowed as engineering/history data; future normal BUY planning does not explode it.
28. BOM does not choose HYBRID route.
29. New revision child selections use Active Items.
30. Historical inactive Items remain resolvable.
31. Component Count is Derived.
32. No manual Current BOM flag.
33. No downstream Planning logic is implemented here.
34. Successor Approval may close exactly one confirmed preceding open-ended Approved Revision to New Effective From minus one day, using Previous Revision ID and Expected Previous Version in one transaction; the predecessor remains Approved and other overlaps are blocked.
35. Operational resolution uses Approved only; historical resolution may resolve Approved/Obsolete by business date while released ranges remain non-overlapping.
36. Obsolete retirement requires explicit Retire From Date and controlled atomic range closure where needed.
37. Approval and Obsolete share transaction-level released-graph locking, and cycle validation is effective-date-aware.

---

## 60. Open Questions

The previous questions concerning BOM Revision/effectivity, successor closure, Obsolete resolution, Base Unit drift, BUY BOM compatibility, cycle locking and Scrap formula are CLOSED.

Canonical v1 Scrap formula:

```text
Base Requirement × (1 + Scrap % / 100)
```

MRP-06 does not calculate it. MRP-06 stores and resolves only the default/override boundary, including NULL versus explicit zero.

There are no remaining MRP-06 business blockers. Minor technical choices such as revision-code formatting and optional Revision Notes presentation may be completed in 06B without changing these locked rules.

---

# B. Prompt 06A — Multi-Level BOM Analysis

```text
# MRP-06A — Multi-Level BOM Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume prior packages were implemented merely because they were planned.

TASK:
Analyze MRP-06 Multi-Level BOM.

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

1. Understanding of Product/Part/Component BOM semantics.
2. Actual Repository findings.
3. Proposed relational model for:
   - BOM Revision Header
   - BOM Lines
   Avoid EAV.
4. Parent/Child type enforcement:
   - Product→Part
   - Part→Component
   - Component→Component
5. Multi-level recursive design without fixed depth.
6. Revision identity/numbering strategy.
7. Status lifecycle:
   - Draft
   - Approved
   - Obsolete
8. Approved Revision immutability.
9. New Revision creation/copy strategy.
10. Effective From/To model.
11. Safest PostgreSQL/EF Core approach to preventing overlapping Approved effective ranges per Parent.
12. Future Approved revisions.
13. Effective BOM resolution by business date.
   - operational = APPROVED only
   - historical = APPROVED / OBSOLETE
14. Confirm no manual Current BOM flag.
15. Base Output Qty.
16. Usage Qty and server-captured immutable Parent/Child Base Unit snapshots.
17. Duplicate Child prevention.
18. Circular dependency detection:
   - direct self-reference
   - recursive cycles
   - Draft warning
   - Approval block
19. Explain cycle detection strategy and expected complexity.
20. Scrap Override:
   - NULL = Item default
   - 0 = explicit zero
   - canonical additive formula is already locked, but MRP-06 must not calculate/apply it
21. Supply Method compatibility:
   - Product
   - MAKE
   - OUTSOURCE
   - HYBRID
   - BUY
22. HYBRID route-selection boundary.
23. Active/inactive Item behavior.
24. Component Count derived.
25. Empty Draft vs Approved BOM rules.
26. Revision Notes / Change Reason recommendation.
27. Verify the locked explicit successor closure contract and Obsolete retirement semantics: confirmation + PreviousRevisionId + ExpectedPreviousVersion, exactly one predecessor, explicit RetireFromDate, operational versus historical resolution, one atomic transaction, non-overlapping released history and no silent rewrite.
28. API/application use cases.
29. UI:
   - Revision list
   - BOM editor
   - approved read-only
   - future/current/historical effective display
30. Permissions:
   - View
   - Manage Draft
   - Approve
31. Auditability.
32. Database constraints/indexes.
33. Numeric precision/date handling.
34. Performance.
35. Concurrency.
36. Testing plan.
37. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..06 and actual Repository.
38. Smallest implementation sequence.
39. Explicit non-scope.

Testing proposal must cover at minimum:
- allowed Parent/Child type combinations
- invalid type combinations
- multiple revisions
- approved immutability
- future effective revision
- overlap prevention
- effective revision lookup by date
- Base Output Qty > 0
- Usage Qty > 0
- decimal Usage
- duplicate Child rejection
- direct self-reference
- multi-level cycle detection
- Draft cycle warning
- Approval cycle block
- Scrap NULL vs 0
- Scrap range
- active Item selection
- historical inactive Item resolution
- Base Unit snapshot drift and `BASE_UNIT_CHANGED`
- empty Draft allowed
- empty Approved rejected
- Component Count not stored
- no Current BOM flag
- no Scrap calculation formula
- no Planning/Inventory leakage
- explicit predecessor closure and stale Previous Version
- Obsolete RetireFromDate and historical Approved/Obsolete resolution
- effective-date-aware cycle checks under concurrent Approval/Obsolete locking

Output:
1. Understanding
2. Repository Findings
3. Data Model
4. Revision / Status Model
5. Effective Dating Strategy
6. Parent/Child Rules
7. Base Qty / Usage Model
8. Cycle Detection
9. Scrap Boundary
10. Supply Method Compatibility
11. Validation / Constraints
12. API Design
13. UI Design
14. Permissions / Audit
15. Performance / Concurrency
16. Tests
17. Risks / Contradictions
18. Implementation Plan
19. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 06B — Multi-Level BOM Implementation

```text
# MRP-06B — Implement Multi-Level BOM

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-06A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-06 Multi-Level BOM design.

If MRP-06A contains unresolved material contradictions, STOP.

BOM REVISION
Implement approved:
- Parent Item
- server-captured immutable ParentBaseUnitId snapshot
- Revision identity
- Status
- Base Output Qty
- Effective From
- Effective To
- Revision Notes if approved
- timestamps/audit integration boundary

STATUSES
Stable machine states:
- DRAFT
- APPROVED
- OBSOLETE

Do not make them user-configurable reference values.

PARENT/CHILD RULES
Enforce:
- Product→Part
- Part→Component
- Component→Component

Do not enable other relationships.

LINES
Implement:
- Child Item
- server-captured immutable ChildBaseUnitId snapshot
- Usage Qty
- Scrap Override %
- approved display order if needed

Rules:
- Usage Qty > 0
- Child Base Unit is authoritative
- clients cannot assign or edit unit snapshots
- current Base Unit mismatch returns/produces `BASE_UNIT_CHANGED` and blocks operational use
- no conversion, silent snapshot refresh or historical rewrite
- duplicate Child in same Revision prohibited
- direct self-reference prohibited

BASE OUTPUT
- default 1
- > 0
- decimal/numeric

REVISIONING
- multiple Revisions per Parent
- Approved Revision immutable
- create new Draft for change
- approved/obsolete historical revisions remain resolvable
- copying Revision to new Draft if approved in 06A

EFFECTIVE DATES
- Effective From required for Approval
- Effective To optional
- To >= From
- no overlapping released Approved/Obsolete historical ranges for same Parent
- future Approved revisions supported
- operational effective Revision resolves APPROVED only by business date
- historical resolution may resolve APPROVED/OBSOLETE by business date
- no manual Current BOM field
- successor Approval may close exactly one preceding open-ended APPROVED Revision to NewEffectiveFrom - 1 day only with explicit confirmation, PreviousRevisionId and ExpectedPreviousVersion
- predecessor closure and successor Approval are one atomic transaction; predecessor remains APPROVED and other overlaps are blocked
- Obsolete current/future Revision requires explicit RetireFromDate and controlled atomic EffectiveTo closure to RetireFromDate - 1 where necessary

CYCLE VALIDATION
- Draft may be saved with warning if approved design allows it
- Approval MUST fail for any recursive cycle
- implement approved effective-date-aware full-graph cycle detection across every stable segment of the candidate effective range
- do not limit to direct self-reference
- Approval and Obsolete use the same transaction-level released-graph lock

SCRAP
Store:
- nullable Scrap Override %

Semantics:
- NULL = Item default
- 0 = explicit zero

The canonical v1 formula is `Base Requirement × (1 + Scrap % / 100)`, but MRP-06 must NOT calculate or apply it.

SUPPLY METHOD BOUNDARY
Respect:
- Product BOM supported
- MAKE BOM supported
- OUTSOURCE BOM supported where lower-level inputs need planning
- HYBRID may coexist with Sourcing
- BUY BOM is allowed as engineering/history data and must not be blocked by MRP-06
- future normal BUY planning does not explode the BOM

Do NOT implement Make-vs-Buy route selection.

ACTIVE ITEMS
- new Child selections use active Items
- historical inactive Items remain resolvable

COMPONENT COUNT
- derived from Lines
- do not persist manual count

APPROVAL
Approval validation must include approved:
- type rules
- Base Output Qty
- at least one Line
- positive Usage
- unique Child
- Scrap validation
- effective-date validity
- no overlapping Approved Revision
- no cycle
- Parent/Child Base Unit snapshots still match current Item Base Units
- confirmed predecessor identity/version is validated before any optional closure
- all validation, optional predecessor closure and Approval are atomic

OBSOLETE
- require explicit RetireFromDate
- operational resolution excludes Obsolete
- historical resolution preserves Approved/Obsolete effectivity
- controlled range closure and status transition are atomic under the shared graph lock
- released historical ranges remain non-overlapping

APPROVED UI
Approved revision read-only.
Normal change path = new Draft Revision.

BACKEND
Implement only approved:
- domain/data model
- EF Core config
- migrations
- use cases/services
- API
- validations
- errors

Do not add speculative frameworks.

FRONTEND
Implement approved:
- BOM Revision list
- filters
- create/copy Draft
- Draft editor
- line editor
- validation feedback
- approve
- obsolete
- approved read-only detail
- effective-date display/resolution

Follow UI-BRAND-RULES.md.
Reuse approved patterns.

PERMISSIONS/AUDIT
Reuse existing platform.
Respect separate approval boundary where existing authorization can support it.
Do not create parallel Identity/Audit infrastructure.

TESTS
At minimum:
- valid/invalid Parent→Child types
- revision history
- approved immutability
- future Approved revision
- effective-range overlap prevention
- effective BOM resolution by date
- confirmed predecessor closure, stale ExpectedPreviousVersion, exactly-one-predecessor rule and rollback
- operational Approved-only resolution and historical Approved/Obsolete resolution
- Obsolete RetireFromDate and historical effectivity preservation
- Base Output Qty validation
- decimal Usage
- Usage > 0
- immutable Parent/Child Base Unit snapshots and Base Unit drift
- duplicate Child rejection
- direct self-reference rejection
- recursive cycle detection
- effective-date segmented cycle detection
- concurrent reciprocal Approval and Approval/Obsolete locking
- Draft cycle warning/save according to approved analysis
- Approval blocked on cycle
- Scrap NULL vs 0
- Scrap range
- active Child selection
- historical inactive Child resolution
- empty Draft
- empty Approval rejection
- no manual Component Count
- no Current BOM field
- no Scrap formula
- BUY BOM allowed without implementing BUY explosion
- no Planning/Inventory/PO leakage
- concurrency around Approval/effectivity

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
- Forecast
- Actual Production
- demand explosion
- Scrap material formula
- Inventory
- Coverage
- Mapping
- Order Planning
- Purchase Orders
- Make-vs-Buy selection
- outsource service PO
- production routing
- work centers
- process capacity
- process lead-time engine
- Unit Conversion
- Cash Flow
- Lead-Time Analytics

Final report:
- Changed
- Database
- Revision Model
- Effective Dating
- BOM Lines
- Cycle Validation
- Scrap Boundary
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-07.
```

---

# D. Prompt 06C — Multi-Level BOM Independent Audit

```text
# MRP-06C — Multi-Level BOM Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-06A Analysis
- MRP-06B implementation report

Inspect all MRP-06 implementation files.

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
   - Forecast
   - demand explosion
   - Scrap calculation formula
   - Inventory
   - Mapping
   - Order Planning
   - PO
   - route selection

2. Parent/Child Rules
   Verify only:
   - Product→Part
   - Part→Component
   - Component→Component

3. Multi-Level
   - no fixed depth
   - architecture supports recursive chains

4. Revisioning
   - multiple Revisions
   - stable identity
   - Approved immutable
   - new Draft required for changes
   - Obsolete not deleted

5. Status
   - DRAFT / APPROVED / OBSOLETE
   - system-controlled meaning
   - no normal Approved→Draft mutation

6. Effective Dating
   - From/To rules
   - no overlap across released Approved/Obsolete historical ranges per Parent
   - future Approved revisions
   - operational lookup uses APPROVED only
   - historical lookup may resolve APPROVED/OBSOLETE by business date
   - explicit successor confirmation, PreviousRevisionId and ExpectedPreviousVersion
   - exactly one preceding open-ended Approved Revision closes to NewEffectiveFrom - 1 in the same transaction and remains Approved
   - every other overlap is blocked; no silent predecessor rewrite
   - Obsolete current/future retirement requires explicit RetireFromDate and controlled atomic closure
   - no manual Current BOM flag
   - date storage rules

7. Base Output Qty
   - > 0
   - decimal/numeric

8. BOM Lines
   - Usage > 0
   - Child Base Unit
   - ParentBaseUnitId and ChildBaseUnitId are server-captured immutable snapshots
   - drift produces BASE_UNIT_CHANGED without conversion or history rewrite
   - duplicate Child prohibited
   - no hidden Component Count

9. Circular BOM
   - direct self-reference
   - recursive cycles
   - Draft warning behavior
   - Approval block
   - effective-date-aware full graph considered for every stable segment
   - Approval/Obsolete share transaction-level graph locking
   - concurrent reciprocal approvals cannot introduce a cycle

10. Scrap
    - NULL vs 0 semantics
    - valid range
    - Item default fallback boundary
    - canonical additive formula is not calculated or applied by MRP-06

11. Supply Method
    - Product / MAKE / OUTSOURCE / HYBRID compatibility
    - BUY BOM is allowed as engineering/history data
    - no BUY planning explosion is implemented
    - HYBRID route selection not implemented

12. Active/Inactive Items
    - active new selections
    - historical inactive resolution

13. Empty BOM
    - Draft may be empty
    - Approved may not

14. Historical Integrity
   - Approved/Obsolete records preserved
   - historical released effectivity remains non-overlapping and resolvable
    - no destructive cascades

15. Database Integrity
    - constraints
    - indexes
    - effective-range concurrency
    - unique Child per Revision
    - migration safety
    - numeric precision
    - Base Unit snapshot foreign keys/immutability

16. API
    - safe use-case boundaries
    - approval validation
    - no unrestricted dangerous CRUD

17. UI
    - Brand Rules
    - RTL
    - revision workflow
    - Draft editor
    - Approved read-only
    - effective-date clarity
    - no downstream feature leakage

18. Permissions / Auditability
    - View / Manage Draft / Approve boundary where supported

19. Performance
    - effective lookup
    - line loading
    - cycle traversal
    - N+1
    - no unnecessary whole-ERP graph loading
    - no premature caching/Redis

20. Tests
    Verify meaningful assertions especially:
    - effective-date overlap/concurrency
    - successor predecessor-closure confirmation/version/rollback
    - Obsolete RetireFromDate and operational/historical resolution split
    - immutable Approved revisions
    - effective-date-aware recursive cycle detection and concurrent graph locking
    - Base Unit drift
    - Scrap NULL vs 0
    - BUY BOM permitted without planning leakage

21. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..06
    - approved MRP-06A
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

# E. Final Pre-Codex Audit — MRP-06

## Result

`PASS`

## Audited Approved Decisions

تمام تصمیم‌های تأییدشده Package 06، از جمله Owner-approved MRP-06A closure، پوشش داده شده‌اند:

1. Product/Part/Component may be BOM Parents.
2. Allowed Child relationships are explicitly constrained.
3. Recursive Multi-Level BOM.
4. Multiple BOM Revisions.
5. Draft / Approved / Obsolete status model.
6. Approved BOM is immutable.
7. Effective From / Effective To.
8. Future Approved revisions.
9. Base Output Qty.
10. Decimal positive Usage Qty.
11. Duplicate Child prohibited.
12. Circular BOM blocked at Approval.
13. Scrap Override with NULL vs 0 meaning.
14. Canonical additive Scrap formula is acknowledged but not calculated in MRP-06.
15. Supply Method compatibility.
16. Active Item rule with historical inactive resolution.
17. Component Count derived.
18. Successor Approval may explicitly and atomically close exactly one preceding open-ended Approved Revision.
19. Operational resolution uses Approved only; historical resolution may use Approved/Obsolete.
20. Obsolete retirement uses explicit Retire From Date and preserves non-overlapping historical effectivity.
21. Parent/Child Base Unit snapshots are server-owned and immutable; drift blocks operational use.
22. BUY BOM is allowed as engineering/history data without BUY planning explosion.
23. Effective-date-aware cycle validation and shared Approval/Obsolete locking are required.

## Open Question Resolution

All MRP-06 business questions are CLOSED.

The canonical v1 Scrap formula is:

```text
Base Requirement × (1 + Scrap % / 100)
```

MRP-06 does not calculate it; it stores and resolves only the Scrap default/override semantics.

## Cross-Package Consistency

- MRP-02 Product remains Final Product parent.
- MRP-04 Part/Component terminology is preserved.
- MRP-04 Supply Method drives BOM compatibility without making BOM choose route.
- MRP-05 external Sourcing can coexist with BOM for HYBRID.
- MRP-06 does not duplicate Sourcing.
- Forecast and future Planning will resolve BOM by Demand Date.
- Product planning eligibility remains compatible with `Active + Valid BOM`.
- Legacy `Component Count` is not carried into ERP.
- Legacy wide BOM layout is replaced by relational Header + Lines.

## Architecture Corrections Applied

- Added Base Output Qty to avoid precision loss from repeated fractional usage where practical.
- Approved Revision made immutable.
- Current BOM is derived by Effective Date rather than manually flagged.
- Overlapping released Approved/Obsolete historical ranges prohibited.
- Explicit successor closure and Obsolete retirement preserve effectivity without silent rewriting.
- Cycle detection is effective-date-aware and spans the recursive graph under shared released-graph locking.
- Draft cycle and Approved cycle behavior are separated.
- Scrap storage is separated from the already-approved Planning formula.
- Child Unit stays owned by Item Master while immutable Parent/Child Base Unit snapshots preserve historical quantity meaning.
- BUY BOM is allowed without implementing normal BUY planning explosion.
- HYBRID Make-vs-Buy selection explicitly deferred.
- Approved BOM history is structural history, not dependent only on audit logs.

## Prompt Audit

### MRP-06A
PASS

Analysis-only and explicitly requires Repository inspection, effective-range design, graph cycle strategy and concurrency review.

### MRP-06B
PASS

Implementation is bounded to Engineering/BOM structure and explicitly blocks Planning, Inventory and route-selection leakage.

### MRP-06C
PASS

Independent read-only audit covers Revision integrity, effectivity, cycle detection, Scrap boundary, performance and specification drift.

## Final Decision

`MRP-06 Multi-Level BOM` is approved for the project Prompt Baseline.

MRP-06A has no remaining business blocker. MRP-06B is ready only after explicit Owner authorization.
