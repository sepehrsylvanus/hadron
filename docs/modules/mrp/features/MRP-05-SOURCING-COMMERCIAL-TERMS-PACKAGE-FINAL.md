# Hadron ERP — MRP Package 05 Final

**Package:** MRP-05 Sourcing & Commercial Terms  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 05-SOURCING-COMMERCIAL-TERMS

## 1. Purpose

این Feature رابطه عملیاتی و تجاری بین `Item` و `Supplier` را ایجاد می‌کند.

MRP-05 باید بتواند پاسخ دهد:

> این Part یا Component را از کدام Supplier، از طریق کدام Sourcing Option، با چه Price/Currency/MOQ و با چه Configured Supply Timeline می‌توان تأمین کرد؟

این Feature پایه مستقیم Featureهای آینده زیر است:

- Multi-Level BOM Planning
- MRP Calculation Engine
- Order Planning
- Purchase Orders
- Lead-Time Analytics
- Cash Flow

MRP-05 نباید Purchase Order، Actual Logistics Events، Cash Flow یا Lead-Time Learning Engine را زودتر از موعد پیاده‌سازی کند.

---

## 2. Core Sourcing Concept

### LOCKED DECISION

Sourcing باید یک Entity مستقل باشد.

Supplier نباید داخل Part/Component Master ذخیره شود.

Conceptually:

```text
Item
  ↓
Sourcing Option
  ↓
Supplier
+ Commercial Terms
+ Supply Route
+ Configured Lead Times
```

یک Item می‌تواند چند Sourcing Option داشته باشد.

---

## 3. Eligible Item Types

Baseline MRP-05 applies to:

- Part
- Component

Product sourcing is not part of this Package unless explicitly approved in a future requirement.

MRP-05 must not modify Product Master merely to support hypothetical finished-goods purchasing.

---

## 4. Multiple Suppliers per Item

### LOCKED DECISION

A Part or Component may have multiple Sourcing Options.

Example:

```text
PCBA-25W
├── Supplier A
├── Supplier B
└── Supplier C
```

Each option may have different:

- Price
- Currency
- MOQ
- Supplier Lead Time
- Shipping Mode
- Incoterm
- Route Lead Times

---

## 5. Multiple Sourcing Options for Same Supplier

### LOCKED DECISION

One Supplier may have more than one Sourcing Option for the same Item.

Example:

```text
PCBA-25W
├── Supplier A + Sea + FOB
└── Supplier A + Air + EXW
```

These are separate Sourcing Options because their:

- Price
- Lead Time
- Shipping Mode
- Incoterm

may differ.

The Data Model must not impose uniqueness only on:

`Item + Supplier`

because that would incorrectly block multiple valid routes.

---

## 6. Sourcing Option Identity

Every Sourcing Option must have a stable internal technical identity.

A human-readable Sourcing Number such as:

```text
SRC-0001
```

may be used if it fits the approved numbering architecture.

Whether to expose a Sourcing Number is a technical/UI decision for MRP-05A, not a new Business Rule.

If implemented, it must follow the same principles as other business-facing numbers:

- server-generated
- immutable
- unique
- concurrency-safe
- gapless numbering not required

---

## 7. Supplier Relationship

Each Sourcing Option references exactly one Supplier.

Rules:

- Supplier must exist.
- Supplier must remain historically resolvable if later deactivated.
- New Planning-ready sourcing must not use an inactive Supplier.
- Supplier details must not be duplicated into the Sourcing record except for intentional historical snapshots in future transactional modules.
- `ItemId` and `SupplierId` are assigned at Sourcing Option creation and are immutable afterward.
- Changing the Item/Supplier relationship requires creating a new Sourcing Option; historical Price/route meaning must not be rewritten by relinking an existing option.

MRP-05 references Supplier Master from MRP-03.

---

## 8. Preferred Sourcing Option

### LOCKED DECISION

For external sourcing options, at most one active Preferred Sourcing Option may exist per Item.

Conceptually:

```text
Item
├── SRC-A  ← Preferred
├── SRC-B  ← Backup
└── SRC-C  ← Backup
```

Preferred means:

> default external source for future planning/recommendation when no higher-level approved policy selects another route.

It does NOT mean future MRP must ignore alternatives.

Future Order Planning may compare alternatives.

For `HYBRID` Items, Preferred external sourcing does not automatically override an internal `MAKE` route. Final route-selection policy belongs to later Planning features.

---

## 9. Preferred Integrity

Rules:

- maximum one active Preferred external Sourcing Option per Item
- inactive Sourcing Option cannot remain Preferred
- `IsPreferred` is not accepted as a client-editable field in ordinary Create/Update; Create always defaults it to `false`
- Preferred changes occur only through the dedicated Preferred command
- the target option must be Active but does not need to be Planning Ready
- switching uses Item-row transactional locking plus a partial unique database index for active Preferred uniqueness
- stale expected Preferred state returns structured `409 / PREFERRED_STATE_CHANGED`
- Sourcing Option with inactive Supplier cannot be Planning Ready
- incomplete Preferred option must not silently be treated as usable by MRP
- future MRP should surface a warning if Preferred exists but is not Planning Ready

MRP-05 stores/validates the sourcing state; it does not implement the later MRP warning UI.

---

## 10. Item Supply Method Compatibility

MRP-04 defines:

```text
BUY
MAKE
OUTSOURCE
HYBRID
```

### Baseline compatibility

External Sourcing Options are valid for:

```text
BUY
OUTSOURCE
HYBRID
```

A pure:

```text
MAKE
```

Item does not require an external Sourcing Option.

MRP-05 must not create fake external sourcing for a pure MAKE Item.

Historical records must remain resolvable if Supply Method is later changed through an approved controlled change.

---

## 11. Commercial Terms Ownership

Sourcing Option owns relationship/route-specific commercial data.

Baseline includes:

- Item
- Supplier
- Preferred flag
- Active status
- MOQ
- Configured Supply Route
- Configured Lead-Time stages

Price is versioned separately as Sourcing Price History.

Supplier default Payment Profile remains owned by Supplier Master.

---

## 12. Price

### LOCKED DECISION

Purchase Price belongs to Sourcing, not Item Master and not Supplier Master.

Every valid Price record must have:

- Sourcing Option
- Unit Price
- Currency
- Effective From
- Effective To where applicable

Price without Currency is invalid.

---

## 13. Price Unit Baseline

For the current MRP baseline:

- Unit Price is expressed per Item Base Unit.
- MOQ is expressed in Item Base Unit.
- `ItemBaseUnitId` is captured by the server from the Item at Sourcing Option creation and is immutable.
- `ItemBaseUnitId` is not client-selectable and is not a supplier order-UOM.
- If the Item Base Unit later differs from the captured value, the Sourcing Option is `BLOCKED` with reason `BASE_UNIT_CHANGED`.
- No conversion or historical rewrite is performed; a new Sourcing Option is required for the new Base Unit context.

Example:

```text
Item Base Unit: PCS
Unit Price: 12.80 CNY / PCS
MOQ: 10,000 PCS
```

Supplier packaging/order-unit conversion is a future Packaging/Procurement capability and is not part of MRP-05.

This avoids hidden conversion factors inside Sourcing.

---

## 14. Currency

Currency must reference Shared ERP Currency from MRP-01.

Examples:

- CNY
- USD
- AED
- EUR
- IRR

Rules:

- required for every Price
- historical inactive Currency remains resolvable
- active Currency required for a new Price record
- Exchange Rate is NOT part of MRP-05

Cash Flow must later preserve original transaction Currency.

---

## 15. Price History

### LOCKED DECISION

Price changes must preserve history.

Example:

```text
2026-01-01 → 2026-03-31
12.50 CNY

2026-04-01 → 2026-06-30
12.80 CNY

2026-07-01 →
13.10 CNY
```

The system must not overwrite the previous historical Price when a new Price becomes effective.

---

## 16. Price Effective Period

Each Price record contains:

- Effective From
- Effective To

Rules:

- Effective From is required.
- Effective To may be open-ended.
- Effective To must not be before Effective From.
- Effective From and Effective To are inclusive business dates.
- overlapping effective periods for the same Sourcing Option must be prevented by a PostgreSQL `btree_gist` exclusion constraint.
- if `btree_gist` cannot be created in the target environment, implementation must STOP and must not silently weaken database integrity.
- at a specific planning date there should be at most one applicable Price per Sourcing Option.

---

## 17. Current Price

`Current Price` is Derived.

The system must not maintain a separate manually synchronized Current Price field if it can be derived from Price History by effective date.

Conceptually:

```text
Current Price
=
Price History record valid on evaluation date
```

The calculation requires an explicit `evaluationDate`. The frontend may default it to the current Tehran business date, but it must send the date explicitly. The API must return the exact `evaluationDate` used.

---

## 18. Future PO Price Snapshot

MRP-05 does not implement Purchase Orders.

However, architecture must preserve the following future rule:

```text
Sourcing Price
      ↓
PO Snapshot
```

When a PO is created, the PO will snapshot:

- Currency
- Unit Price
- Quantity
- Total Amount
- relevant commercial assumptions

Changing Sourcing Price later must never alter historical PO values.

---

## 19. MOQ

### LOCKED DECISION

MOQ belongs to Sourcing Option because it is Supplier/route-specific.

Rules:

- expressed in Item Base Unit
- stored as `numeric(18,6)`
- >= 0
- 0 means no effective minimum quantity if business chooses to use zero
- historical PO quantity is not changed by later MOQ changes

MRP-05 does not yet apply MOQ to Purchase Recommendations. That belongs to Order Planning.

---

## 20. Supply Route

Each Sourcing Option may define its default Supply Route.

Baseline route fields:

- Shipping Mode
- Incoterm
- Forwarder to Shipment Days
- Shipping Period Days
- Customs Period Days

Shipping Mode and Incoterm are independent concepts.

---

## 21. Shipping Mode

References MRP-01 Shipping Mode.

Examples:

- Sea
- Air
- Road
- Internal

For new assignment, active Reference Data must be used.

Historical inactive value remains resolvable.

---

## 22. Incoterm

References MRP-01 Incoterm.

Examples:

- EXW
- FOB
- CFR
- CIF

Incoterm is independent from Shipping Mode.

Incoterm may be optional where genuinely not applicable to the sourcing route; MRP-05 must not invent a fake commercial term solely to satisfy a database field.

---

## 23. Supplier Lead Time

### LOCKED DECISION

Operational Supplier Lead Time belongs to Sourcing Option.

It is specific to:

`Supplier + Item + Sourcing Option`

and must not be confused with Supplier Master Expert Lead Time Score.

Rules:

- Calendar Days
- integer
- >= 0

This is a configured planning assumption.

Stage boundary:

```text
ORDERED → SUPPLIER_READY
```

---

## 24. Forwarder to Shipment Days

Configured route stage.

Approved semantic label and boundary:

```text
Forwarder / Pre-Shipment Days
SUPPLIER_READY → SHIPPED
```

`HANDOVER_TO_FORWARDER` is a diagnostic milestone inside this stage and is not an additional additive stage.

Rules:

- Calendar Days
- integer
- >= 0
- use 0 where stage is genuinely not applicable

---

## 25. Shipping Period Days

Configured route stage.

Stage boundary:

```text
SHIPPED → ARRIVED_DESTINATION
```

Rules:

- Calendar Days
- integer
- >= 0
- use 0 where stage is genuinely not applicable

---

## 26. Customs Period Days / Destination–Customs Days

Configured route stage.

Approved v1 semantic meaning:

```text
ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
```

The clearer documentation/UI label is `Destination / Customs Days`. Existing technical/schema naming remains controlled. `CUSTOMS_CLEARED` is diagnostic inside this stage and is not an additional additive stage.

Rules:

- Calendar Days
- integer
- >= 0
- use 0 where stage is genuinely not applicable

---

## 27. Total Configured Lead Time

### LOCKED DECISION

Total Lead Time must be Derived, not separately manually stored.

Baseline:

```text
Total Configured Lead Time
=
Supplier Lead Time
+
Forwarder to Shipment Days
+
Shipping Period Days
+
Customs Period Days
```

All stages use Calendar Days.

The four stages are non-overlapping and continuous. `HANDOVER_TO_FORWARDER` and `CUSTOMS_CLEARED` must never be added again or double-counted in Total Configured Lead Time.

If future BOM/process planning introduces Internal Process Time, that time is handled by the appropriate later planning feature and must not be silently inserted into this external sourcing formula.

---

## 28. Configured vs Planned vs Actual Time

The system must distinguish:

### Configured Time

Current planning assumption in Sourcing Option.

Example:

```text
Configured Shipping = 28 days
```

### Planned Time

Snapshot/expected timing on a future PO or supply transaction.

### Actual Time

Observed real milestone duration from actual logistics events.

MRP-05 only manages current Configured Sourcing assumptions.

PO and Logistics packages will later store Planned and Actual dates/durations.

---

## 29. Lead-Time Historical Learning Boundary

### LOCKED DECISION

Historical Actual data must never silently overwrite Configured Sourcing lead-time values.

Future analytics may produce:

- Historical Average
- Median
- Recent Average
- P80/P90
- On-Time %
- Delay Frequency
- Sample Count
- Recommended Lead Time

but:

```text
System Recommendation
      ≠
Automatic Master Overwrite
```

Expert approval is required before changing Configured planning assumptions.

MRP-05 does not implement this learning engine.

---

## 30. Configured Lead-Time Auditability

Changes to configured lead-time stages should be auditable.

At minimum future audit should preserve:

- previous value
- new value
- actor
- timestamp

Historical PO snapshots later preserve what Planning believed at the time of each real order.

A separate full lead-time versioning engine is not required in MRP-05.

---

## 31. Payment Terms

### LOCKED DECISION

MRP-05 does NOT duplicate Supplier Default Payment Profile.

Current v1 rule:

```text
Sourcing Option
      ↓
uses Supplier Default Payment Profile
```

Future Purchase Order:

- snapshots Supplier default terms
- may allow controlled explicit override

Sourcing-level Payment Override is NOT part of MRP-05.

This avoids duplicating the same payment information in Supplier and Sourcing.

---

## 32. Active / Inactive Lifecycle

Sourcing Option supports:

- Active
- Inactive

Rules:

- baseline lifecycle = deactivate/reactivate
- historical Sourcing Option must not be hard deleted if referenced
- inactive option excluded from future source selection
- inactive option cannot remain Preferred
- Price History remains historically preserved
- reactivation requires only fundamental compatibility: active Item, active Supplier, compatible Supply Method, matching captured Base Unit and valid assigned references
- missing current Price, MOQ or route-completeness data does not block reactivation; the derived result is `INCOMPLETE_DATA`

---

## 33. Incomplete Sourcing

### LOCKED DECISION

A Sourcing Option may be entered before all information is complete.

Do not require users to fabricate data just to save a record.

Examples of incomplete data:

- missing current Price
- missing Currency through missing Price
- missing MOQ
- missing route stage
- missing Shipping Mode

---

## 34. Planning Readiness

### LOCKED DECISION

`Planning Ready` is Derived.

It must NOT be a manually editable checkbox.

Conceptually:

```text
Planning Ready
=
Active Sourcing Option
+
Active Supplier
+
Compatible Item Supply Method
+
Current Valid Price
+
Currency
+
MOQ
+
Required Configured Lead-Time Data
+
Required Route Data
```

Exact validation expression may account for legitimate non-applicable route fields, but must not silently treat missing required planning data as ready.

Approved derived states and precedence:

```text
INACTIVE
→ BLOCKED
→ INCOMPLETE_DATA
→ READY
```

The evaluation stops at the first applicable state in that precedence. Stable machine-readable reason codes are required. At minimum, Base Unit drift uses `BASE_UNIT_CHANGED`; other missing/blocked causes must also use stable codes rather than display text.

Readiness requires an explicit `evaluationDate`. The frontend may default to Tehran today but must send it explicitly, and every API response containing readiness/current Price must return the date used.

---

## 35. Route Completeness Baseline

For a Planning-ready external Sourcing Option:

- Supplier Lead Time must be provided.
- Forwarder to Shipment Days must be provided.
- Shipping Period Days must be provided.
- Customs Period Days must be provided.
- Shipping Mode must be provided.

A legitimate non-applicable duration is represented by:

```text
0
```

rather than missing data.

Incoterm may remain optional where not commercially applicable.

This allows Planning Readiness to distinguish:

`0 days intentionally`

from:

`unknown`

---

## 36. Current Valid Price Requirement

A Planning-ready Sourcing Option must have exactly one Price applicable at the planning/evaluation date.

If:

- no Price exists
or
- price periods overlap ambiguously

then:

```text
Planning Ready = false
```

The MRP engine must not guess a Price.

---

## 37. Supplier Active Requirement

New Planning-ready sourcing requires Active Supplier.

If Supplier is deactivated:

- historical Sourcing and Price History remain resolvable
- Sourcing Option cannot be considered Planning Ready for new procurement
- future planning should not create new order recommendation against it without explicit approved reactivation/override policy

MRP-05 does not implement override workflow.

---

## 38. Item Active Requirement

A Planning-ready Sourcing Option requires an Active Part/Component for future planning.

Historical inactive Items remain resolvable.

---

## 39. Alternative Source Future Use

MRP-05 must preserve enough clean data so future Order Planning can compare:

- Preferred Source
- Backup Source
- faster route
- cheaper route
- alternative Supplier

Example future analysis:

```text
Preferred Sea Source:
61 days

Backup Air Source:
18 days

Risk:
Preferred cannot arrive before stockout.
```

MRP-05 does not implement this recommendation logic.

---

## 40. Source Price vs Lead-Time Tradeoff

MRP-05 must not itself choose between cheaper and faster Sourcing Options.

It stores the facts.

Future Planning Engine may evaluate:

- urgency
- price
- lead time
- availability
- risk
- MOQ

Selection policy belongs to Order Planning.

---

## 41. Supplier Defaults and Sourcing Overrides

MRP-05 may reference Supplier-level current defaults where applicable but must not copy values that are owned by Supplier Master without a real need.

Payment Profile specifically remains Supplier-owned.

Item/route-specific values remain Sourcing-owned.

---

## 42. Data Model Concept

Conceptually:

```text
Item
  │
  └── Sourcing Option
          │
          ├── Supplier
          ├── Item Base Unit Snapshot
          ├── Preferred
          ├── MOQ
          ├── Shipping Mode
          ├── Incoterm
          ├── Supplier Lead Time
          ├── Forwarder Days
          ├── Shipping Days
          ├── Customs Days
          ├── Active
          │
          └── Price History
                  ├── Unit Price
                  ├── Currency
                  ├── Effective From
                  └── Effective To
```

This is conceptual, not a required table naming scheme.

Codex must propose the smallest clean relational implementation.

---

## 43. Sourcing Management UI

Required capabilities:

- Sourcing Options list/grid
- search by Item
- filter by Supplier
- filter by Item Type
- filter by Active/Inactive
- filter by Preferred
- filter by Planning Readiness
- create
- edit
- deactivate
- reactivate
- set Preferred
- view Sourcing detail
- manage Price History

Follow:

`docs/brand/UI-BRAND-RULES.md`

Reuse existing approved grid/form patterns.

---

## 44. Item-Centric UX

From a Part or Component detail page, future navigation should be able to show:

```text
Sourcing Options
```

for that Item.

MRP-05 may implement an approved integration link/tab if it fits existing UI architecture.

It must not redesign Part/Component Master.

---

## 45. Supplier-Centric UX

From a Supplier detail page, future navigation should be able to show Sourcing Options for that Supplier.

MRP-05 may add an approved integration surface if it reuses Supplier architecture cleanly.

It must not turn Supplier Master into a combined PO/Logistics screen.

---

## 46. Price History UX

User should be able to see historical Price records chronologically.

At minimum display:

- Unit Price
- Currency
- Effective From
- Effective To
- Current/Historical state

UI must prevent or clearly surface overlapping effective periods.

---

## 47. Preferred Source UX

Setting a Sourcing Option as Preferred must enforce:

- only one active Preferred external option per Item

If another Preferred exists, the operation must be atomic and unambiguous.

The approved approach is a dedicated command using Item-row transactional locking, expected Preferred state and a partial unique active-Preferred index. A stale expected state returns `409 / PREFERRED_STATE_CHANGED`.

---

## 48. Permissions

Architecture should distinguish at minimum:

- View Sourcing
- Manage Sourcing
- Manage Sourcing Prices

Exact permission granularity may reuse broader Procurement permissions if already approved.

Do not create parallel Identity infrastructure.

---

## 49. Auditability

Important changes must be compatible with Audit:

- create Sourcing Option
- attempted or created Item/Supplier relationship; ordinary relinking is prohibited
- Preferred change
- MOQ change
- route change
- lead-time stage change
- activate/deactivate
- Price record add/change/end-date
- Currency change through new Price record

Do not build a competing Audit platform.

---

## 50. API / Application Use Cases

Required use cases include:

- list/search Sourcing Options
- get detail
- create Sourcing Option
- update approved fields
- deactivate/reactivate
- set/change Preferred
- list Price History
- add new Price
- close/end a Price period
- derive current Price
- derive Total Configured Lead Time
- derive Planning Readiness

Avoid unrestricted dangerous generic CRUD.

---

## 51. Validation

Server-side validation is mandatory.

At minimum:

### Sourcing Option
- Item exists
- Item type is Part or Component
- Supplier exists
- Supply Method compatible
- MOQ >= 0 when provided
- lead-time stages >= 0 when provided
- Shipping Mode valid when supplied
- Incoterm valid when supplied
- no inactive option remains Preferred
- max one active Preferred external option per Item

### Price
- Unit Price stored as `numeric(18,6)` and >= 0; zero remains valid
- Currency required
- Effective From required
- Effective To >= Effective From when provided
- no overlapping effective periods for same Sourcing Option
- active Currency required for new Price record

### Planning Readiness
- must be derived
- cannot be client-forced to Ready
- missing required planning data must produce non-ready state
- precedence is `INACTIVE → BLOCKED → INCOMPLETE_DATA → READY`
- stable machine-readable reason codes are returned
- Item Base Unit mismatch produces `BLOCKED / BASE_UNIT_CHANGED`
- readiness/current Price uses explicit `evaluationDate` and returns it in the API response

---

## 52. Precision

Use decimal/numeric for:

- Unit Price: `numeric(18,6)`
- MOQ: `numeric(18,6)`

Do not use binary floating point.

Zero is valid for both Price and MOQ under the canonical `>= 0` rules.

Lead-time days use integer Calendar Days.

---

## 53. Date Rules

Price Effective dates use canonical Gregorian storage.

Effective period endpoints are inclusive.

Readiness and current-Price calculation require an explicit Gregorian `evaluationDate`. The frontend may initialize it to Tehran today, but it must transmit the value and the API must echo the value used.

Jalali display/input may be supported by shared UI/date infrastructure.

Business timezone:

`Asia/Tehran`

Price date comparisons must not depend on formatted Jalali strings.

---

## 54. Performance

Support efficient:

- Item lookup
- Supplier lookup
- active sourcing lookup
- Preferred source lookup
- current Price lookup by date
- Planning-ready filtering
- Price History retrieval

Indexes should support likely predicates.

Avoid:

- N+1
- scanning all historical prices to find one current price
- loading all Sourcing Options when only preferred/active is needed
- speculative caching
- Redis
- premature search infrastructure

---

## 55. Concurrency

Concurrency-sensitive operations include:

- setting Preferred source
- adding/replacing Price effective periods
- preventing overlapping Prices

These rules must remain correct under concurrent requests.

Preferred switching uses Item-row transactional locking and a partial unique index over active Preferred options. The command verifies the expected current Preferred state and returns `409 / PREFERRED_STATE_CHANGED` when stale. Price-period overlap is protected by the approved PostgreSQL `btree_gist` exclusion constraint.

---

## 56. Historical Integrity

Historical data must remain resolvable after:

- Supplier deactivation
- Item deactivation
- Currency deactivation
- Shipping Mode deactivation
- Incoterm deactivation
- Sourcing deactivation
- Price changes

Do not cascade-delete historical business records.

---

## 57. Seed / Migration

Codex must not invent real Sourcing records or commercial prices.

If no explicit legacy sourcing dataset is supplied:

- implement capability
- do not fabricate Supplier/Item commercial relationships

Legacy import will be a separate controlled migration task.

---

## 58. Explicit Non-Scope

MRP-05 must NOT implement:

- Product purchasing
- BOM
- BOM explosion
- Internal MAKE process routing
- Inventory
- Opening Balance
- Forecast
- Actual Production
- Mapping
- MRP Calculation Engine
- Order Planning
- Purchase Orders
- PO Price Snapshot
- PO Payment Snapshot
- Actual Shipment events
- Actual Customs events
- Actual Receipt
- Lead-Time Analytics
- automatic lead-time learning
- Cash Flow
- Exchange Rates
- Packaging/UOM Conversion
- Freight Cost
- Customs Cost
- Landed Cost
- Supplier payment override at Sourcing level
- Workflow Engine

---

## 59. Acceptance Criteria

MRP-05 is acceptable when:

1. One Item can have multiple Sourcing Options.
2. One Supplier can have multiple routes/options for the same Item.
3. Sourcing Option references Item and Supplier cleanly.
4. At most one active Preferred external option exists per Item.
5. Inactive option cannot remain Preferred.
6. Price belongs to Sourcing.
7. Every Price has Currency.
8. Price History is preserved.
9. Price effective periods do not overlap for one option.
10. Current Price is derived.
11. MOQ belongs to Sourcing and is >= 0.
12. Price/MOQ use Item Base Unit in v1.
13. Supplier Lead Time belongs to Sourcing.
14. Lead-time stages are Calendar Days.
15. Total Configured Lead Time is derived.
16. Shipping Mode and Incoterm remain independent.
17. Supplier Payment Profile is reused, not duplicated.
18. Sourcing-level Payment Override does not exist.
19. Configured lead times are separated from future Planned/Actual times.
20. Historical analytics cannot auto-overwrite Configured values.
21. Sourcing can be incomplete.
22. Planning Readiness is derived.
23. Missing required planning data cannot be falsely marked Ready.
24. External Sourcing only applies to BUY/OUTSOURCE/HYBRID baseline.
25. Active/Inactive lifecycle works.
26. Historical Sourcing/Price data remain resolvable.
27. No PO/Inventory/MRP/Analytics logic is prematurely implemented.
28. Server-side validation exists.
29. Concurrency-sensitive Preferred/Price rules are tested.
30. No real Sourcing/Price data is fabricated.
31. `ItemBaseUnitId` is server-captured/immutable and Base Unit drift produces `BLOCKED / BASE_UNIT_CHANGED` without conversion or rewrite.
32. `ItemId` and `SupplierId` are immutable after Create.
33. Create/Update cannot set `IsPreferred`; the dedicated command is atomic and stale Preferred state returns `409 / PREFERRED_STATE_CHANGED`.
34. Reactivation permits incomplete commercial data while enforcing fundamental compatibility.
35. Readiness precedence and machine-readable reason codes are stable.
36. Readiness/current Price uses and returns explicit `evaluationDate`.
37. Inclusive Price periods are protected by the approved `btree_gist` exclusion constraint.
38. Price and MOQ are `numeric(18,6)` and zero remains valid.

---

## 60. Locked Decisions

The following are locked for MRP-05:

1. Multiple Suppliers/Sourcing Options per Item.
2. Multiple Sourcing Options for same Supplier + Item are allowed.
3. At most one active Preferred external Sourcing Option per Item.
4. Supplier linkage exists in Sourcing, not Item Master.
5. Price belongs to Sourcing.
6. Price History must be preserved.
7. Every Price has Currency.
8. Price has Effective From and optional Effective To.
9. Current Price is derived.
10. MOQ belongs to Sourcing.
11. MOQ >= 0.
12. v1 Price and MOQ use Item Base Unit.
13. Operational Supplier Lead Time belongs to Sourcing.
14. Lead-time stages use Calendar Days.
15. Sourcing Route includes Shipping Mode, Incoterm and configured route durations.
16. Total Configured Lead Time is derived, not manually stored.
17. Configured vs future Planned vs Actual times remain separate.
18. Historical performance cannot silently overwrite configured lead times.
19. Supplier Default Payment Profile is reused.
20. No Sourcing-level Payment Override in v1.
21. External Sourcing applies to BUY / OUTSOURCE / HYBRID.
22. Pure MAKE does not require external Sourcing.
23. Sourcing supports Active / Inactive.
24. Sourcing may be incomplete.
25. Planning Readiness is Derived, not a checkbox.
26. Preferred does not force future MRP to ignore alternatives.
27. Future PO snapshots Price/Currency/Payment assumptions.
28. Historical downstream data cannot be rewritten by later master changes.
29. `ItemBaseUnitId` is server-captured and immutable; later mismatch is `BLOCKED / BASE_UNIT_CHANGED` with no conversion or historical rewrite.
30. Sourcing `ItemId` and `SupplierId` are immutable after Create; relationship change requires a new Sourcing Option.
31. `IsPreferred` defaults false and is changed only by a dedicated command; the target must be Active but need not be Planning Ready.
32. Preferred switching uses Item-row transactional locking plus a partial unique active-Preferred index; stale state is `409 / PREFERRED_STATE_CHANGED`.
33. Reactivation checks fundamental compatibility but permits missing Price/MOQ/route completeness as `INCOMPLETE_DATA`.
34. Readiness precedence is `INACTIVE → BLOCKED → INCOMPLETE_DATA → READY` with stable machine-readable reason codes.
35. Readiness/current Price requires explicit `evaluationDate`; the frontend sends it and the API returns it.
36. Inclusive Price periods use PostgreSQL `btree_gist` exclusion integrity; implementation stops if that integrity cannot be installed.
37. Price and MOQ use `numeric(18,6)` and accept zero.

## 60A. MRP-14 Lead-Time Recommendation Apply Contract

### LOCKED CROSS-PACKAGE DECISION

MRP-14 owns Lead-Time analytics and recommendations, but applying a recommendation remains a Sourcing-owned mutation.

- Total Lead-Time P80 is analytical only and is not stored as a fifth configurable duration.
- P80 is calculated separately for each of the four canonical configured stages.
- Each stage recommendation is converted to whole Calendar Days with `Ceiling`.
- An authorized user explicitly selects the stage values to apply through one versioned Sourcing command.
- A stage with insufficient valid samples is not populated or applied automatically.
- The command preserves previous, recommended and applied values per stage, method `P80`, sample count/window, actor, timestamp and Reason/note.
- No analytics query, UI action or background process may bypass Sourcing validation/concurrency or silently overwrite configured lead times.

---

## 61. Open Questions

No Business-critical open question blocks MRP-05.

The following implementation details may be resolved in Codex Analysis without changing Business Rules:

- exact relational table layout
- optional human-readable Sourcing Number
- UI integration into Item/Supplier detail pages

The Price-range mechanism, Preferred switching strategy, Readiness state precedence and Base Unit snapshot semantics are CLOSED by the Owner-approved MRP-05A baseline above.

---

# B. Prompt 05A — Sourcing & Commercial Terms Analysis

```text
# MRP-05A — Sourcing & Commercial Terms Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect the actual Repository state.

Do not assume prior packages were implemented merely because they were planned.

TASK:
Analyze MRP-05 Sourcing & Commercial Terms.

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

1. Understanding of Sourcing role and ownership boundary.
2. Actual Repository findings.
3. Proposed relational Data Model for:
   - Sourcing Option
   - Price History
   - Item/Supplier linkage
   Avoid EAV.
4. Confirm multiple Sourcing Options:
   - multiple Suppliers per Item
   - same Supplier + Item with multiple routes
5. Preferred Sourcing:
   - max one active Preferred external option per Item
   - atomic/concurrency-safe switching
6. Item Supply Method compatibility:
   BUY / OUTSOURCE / HYBRID
   pure MAKE behavior
7. Price model:
   - Unit Price
   - Currency
   - Effective From/To
   - no overlap
   - current Price derived
8. Explain safest PostgreSQL/EF Core approach for preventing overlapping Price periods.
9. Confirm Price and MOQ are expressed in Item Base Unit in v1.
10. MOQ.
11. Supply Route:
    - Shipping Mode
    - Incoterm
    - Supplier Lead Time
    - Forwarder to Shipment
    - Shipping Period
    - Destination / Customs Period
    - exact boundaries: ORDERED→SUPPLIER_READY→SHIPPED→ARRIVED_DESTINATION→WAREHOUSE_RECEIPT
    - HANDOVER_TO_FORWARDER and CUSTOMS_CLEARED are diagnostic, not additive stages
    Verify each boundary explicitly:
    - ORDERED → SUPPLIER_READY
    - SUPPLIER_READY → SHIPPED
    - SHIPPED → ARRIVED_DESTINATION
    - ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
12. Confirm all lead-time stages use Calendar Days.
13. Confirm Total Configured Lead Time is derived.
    Confirm it is the sum of exactly four continuous non-overlapping stages with no diagnostic-milestone double counting.
14. Configured vs future Planned vs Actual timing boundary.
15. Lead-Time Learning boundary:
    historical analytics must not overwrite configured values.
16. Supplier Default Payment Profile reuse:
    no Sourcing payment duplication/override.
17. Incomplete Sourcing and Derived Planning Readiness.
18. Propose exact Readiness evaluation without allowing missing data to become Ready.
19. Historical integrity when Item/Supplier/Reference Data is deactivated.
20. API/application use cases.
21. UI:
    - Sourcing grid
    - Item/Supplier filtering
    - Preferred
    - Planning Readiness
    - Price History
22. Permissions/Audit.
23. Database constraints/indexes.
24. Numeric precision/date handling.
25. Performance.
26. Concurrency.
27. Testing plan.
28. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01..05 and actual Repository.
29. Smallest implementation sequence.
30. Explicit non-scope.

Testing proposal must cover at minimum:
- multiple options per Item
- same Supplier+Item with different routes
- max one Preferred
- concurrent Preferred switching
- inactive option cannot be Preferred
- incompatible pure MAKE external source rejected/not Planning Ready
- Price requires Currency
- Price period validity
- Price overlap prevention
- current Price by evaluation date
- historical Price preserved
- MOQ >= 0
- lead-time stage >= 0
- derived Total Lead Time
- Incomplete Sourcing save allowed
- Planning Ready cannot be manually forced
- missing current Price => not Ready
- inactive Supplier/Item => not Ready
- active Reference Data required for new assignment
- historical inactive references remain resolvable
- no payment profile duplication
- no PO/Inventory/MRP engine leakage

Output:
1. Understanding
2. Repository Findings
3. Data Model
4. Preferred Source Strategy
5. Price History Strategy
6. Route / Lead-Time Model
7. Payment Boundary
8. Planning Readiness
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

# C. Prompt 05B — Sourcing & Commercial Terms Implementation

```text
# MRP-05B — Implement Sourcing & Commercial Terms

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-05A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-05 design.

If MRP-05A contains unresolved material contradictions, STOP.

SOURCING OPTION
Implement approved:
- Item reference
- Supplier reference
- server-captured immutable Item Base Unit reference
- Preferred
- Active/Inactive
- MOQ
- Shipping Mode
- Incoterm
- Supplier Lead Time
- Forwarder to Shipment Days
- Shipping Period Days
- Customs Period Days

Allow:
- multiple Suppliers per Item
- multiple options for same Supplier + Item

Identity/relationship rules:
- `ItemId` and `SupplierId` are immutable after Create; relationship change requires a new Sourcing Option
- `ItemBaseUnitId` is captured from the Item by the server, is immutable and is not accepted from the client
- later Item Base Unit mismatch produces `BLOCKED / BASE_UNIT_CHANGED`
- do not convert quantities or rewrite historical Sourcing/Price data
- `IsPreferred` is not accepted in Create/Update and Create defaults it to `false`

PREFERRED
Enforce:
- max one active Preferred external option per Item
- inactive option cannot remain Preferred
- Preferred changes only through the dedicated command
- target must be Active but need not be Planning Ready
- use Item-row transactional locking plus a partial unique index for active Preferred uniqueness
- command verifies expected current Preferred state
- stale Preferred state returns structured `409 / PREFERRED_STATE_CHANGED`

ITEM COMPATIBILITY
External Sourcing baseline:
- BUY
- OUTSOURCE
- HYBRID

Do not require external Sourcing for pure MAKE.

PRICE HISTORY
Implement:
- Sourcing Option
- Unit Price
- Currency
- Effective From
- Effective To

Rules:
- Price uses `numeric(18,6)` and is >= 0; zero is valid
- Currency required
- Effective From required
- Effective To >= Effective From
- Effective From/To are inclusive
- no overlapping effective periods for same option, enforced by PostgreSQL `btree_gist` exclusion constraint
- if `btree_gist` cannot be created, STOP; do not replace it with weaker application-only integrity
- current Price derived by explicit `evaluationDate`
- historical prices preserved

Do not add a manually synchronized Current Price field unless the approved Analysis demonstrates a compelling architectural reason.

PRICE / MOQ UNIT
Use Item Base Unit in v1.

- Price and MOQ use `numeric(18,6)`
- zero is valid for Price and MOQ
- use the server-captured immutable `ItemBaseUnitId`

Do not implement supplier packaging/order-UOM conversion.

LEAD TIMES
Configured stages:
- Supplier Lead Time: ORDERED → SUPPLIER_READY
- Forwarder / Pre-Shipment Days: SUPPLIER_READY → SHIPPED
- Shipping Period Days: SHIPPED → ARRIVED_DESTINATION
- Destination / Customs Days: ARRIVED_DESTINATION → WAREHOUSE_RECEIPT

All:
- Calendar Days
- integer
- >= 0

Total Configured Lead Time:
- derived
- do not store a separately editable total
- sum exactly the four non-overlapping stages
- do not add/double-count HANDOVER_TO_FORWARDER or CUSTOMS_CLEARED; both are diagnostic

ROUTE
Reuse:
- Shipping Mode
- Incoterm

from MRP-01.

Incoterm may be null where genuinely not applicable according to approved design.

PAYMENT
Reuse Supplier Default Payment Profile.

Do NOT add Sourcing-level:
- Payment Term override
- Advance %
- Before Shipment %
- Credit %
- Credit Days

PLANNING READINESS
Implement approved Derived Readiness.

Do not create user-editable Ready checkbox/status.

Incomplete Sourcing may be saved.

Approved precedence:

```text
INACTIVE → BLOCKED → INCOMPLETE_DATA → READY
```

- return stable machine-readable reason codes
- Base Unit mismatch is `BLOCKED / BASE_UNIT_CHANGED`
- calculation requires explicit `evaluationDate`
- frontend may default to Tehran today but must send the date explicitly
- API must return the `evaluationDate` used

Planning-ready evaluation must consider approved required data including:
- Active option
- Active Supplier
- Active Item
- compatible Supply Method
- current valid Price
- Currency
- MOQ
- configured lead-time stages
- required route data

Use 0 for intentionally non-applicable duration, null/missing for unknown.

REACTIVATION
- require active Item and active Supplier
- require compatible Supply Method
- require captured Item Base Unit to match current Item Base Unit
- require assigned references to be valid
- missing Price, MOQ or route-completeness data does not block reactivation and yields `INCOMPLETE_DATA`

HISTORICAL INTEGRITY
Do not cascade-delete:
- Price History
- Sourcing history
when referenced master/reference data is deactivated.

LEAD-TIME LEARNING BOUNDARY
Do NOT implement:
- Historical Average
- Median
- P90
- auto-recommended Lead Time
- automatic master updates

AUDIT
Reuse existing Audit capability/boundary for:
- sourcing create/edit
- Preferred
- MOQ
- route
- lead-time changes
- activation
- Price records

BACKEND
Implement only approved:
- entities/models
- EF Core configuration
- migrations
- application use cases/services
- API
- validation
- error handling

Do not add speculative frameworks.

FRONTEND
Implement approved:
- Sourcing list/grid
- filters by Item/Supplier/Type/Active/Preferred/Readiness
- create/edit
- deactivate/reactivate
- set Preferred
- detail
- Price History management

Reuse approved grid/form patterns.
Follow UI-BRAND-RULES.md.

PERMISSIONS
Reuse existing authorization.
Do not build parallel Identity.

TESTS
At minimum:
- multiple options per Item
- multiple same Supplier+Item routes
- max one active Preferred
- concurrent Preferred update integrity
- inactive option not Preferred
- Create/Update cannot set Preferred and Create defaults false
- Preferred target Active but not Planning Ready is allowed
- stale Preferred state returns `409 / PREFERRED_STATE_CHANGED`
- Item-row locking plus partial unique index preserves one active Preferred
- ItemId/SupplierId immutability
- server-captured immutable ItemBaseUnitId
- Base Unit mismatch => `BLOCKED / BASE_UNIT_CHANGED` with no conversion/rewrite
- Price requires Currency
- Price period validity
- inclusive Price boundary behavior
- `btree_gist` database overlap prevention, including concurrent requests
- current Price derivation from explicit `evaluationDate` and API echo
- historical Price preservation
- Price/MOQ exact `numeric(18,6)` persistence, negative rejection and zero acceptance
- lead-time stage validation
- Total Lead Time derived
- Incomplete save allowed
- Readiness derived
- Readiness precedence and stable reason codes
- client cannot force Ready
- missing Price => not Ready
- inactive Supplier/Item => not Ready
- pure MAKE external-source rule
- historical inactive reference resolution
- reactivation fundamental checks and incomplete commercial-data allowance
- no Sourcing payment override fields
- no future-module leakage

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
- BOM
- Inventory
- Forecast
- MRP Calculation Engine
- Mapping
- Order Planning
- Purchase Orders
- PO snapshots
- Logistics Actuals
- Lead-Time Analytics
- automatic learning
- Cash Flow
- Exchange Rates
- Packaging/UOM Conversion
- Freight/Customs/Landed Cost
- Workflow Engine

Final report:
- Changed
- Database
- Sourcing Options
- Preferred
- Price History
- Route / Lead Times
- Planning Readiness
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-06.
```

---

# D. Prompt 05C — Sourcing & Commercial Terms Independent Audit

```text
# MRP-05C — Sourcing & Commercial Terms Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-05A Analysis
- MRP-05B implementation report

Inspect all MRP-05 implementation files.

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
   - BOM
   - Inventory
   - MRP engine
   - PO
   - Logistics actuals
   - Cash Flow
   - Lead-Time learning

2. Sourcing Model
   - multiple Suppliers per Item
   - multiple same Supplier+Item route options
   - no Item+Supplier uniqueness bug
   - ItemId and SupplierId immutable after Create
   - relationship change requires a new Sourcing Option
   - ItemBaseUnitId captured by server, immutable and not client-supplied
   - Base Unit mismatch produces `BLOCKED / BASE_UNIT_CHANGED` without conversion/history rewrite

3. Preferred
   - max one active Preferred external option per Item
   - inactive option not Preferred
   - IsPreferred absent from Create/Update and defaults false
   - only the dedicated command changes Preferred
   - target Active but not necessarily Planning Ready
   - Item-row transactional locking correct
   - partial unique active-Preferred index exists
   - stale state returns `409 / PREFERRED_STATE_CHANGED`
   - concurrency/atomicity correct
   - Preferred not treated as mandatory exclusion of alternatives

4. Item Compatibility
   - BUY/OUTSOURCE/HYBRID supported
   - pure MAKE not forced into external sourcing

5. Price
   - belongs to Sourcing
   - Currency required
   - Price is `numeric(18,6)`, Price >= 0 and zero is valid
   - Item Base Unit baseline
   - inclusive Effective dates
   - PostgreSQL `btree_gist` exclusion constraint prevents overlap
   - implementation did not silently weaken integrity if extension creation failed
   - historical preservation
   - current Price derived from explicit `evaluationDate` and API returns that date

6. MOQ
   - Sourcing-specific
   - `numeric(18,6)`, >= 0 and zero is valid
   - Item Base Unit

7. Route
   - Shipping Mode distinct from Incoterm
   - Supplier Lead Time
   - Forwarder Days
   - Shipping Days
   - Destination / Customs Days
   - boundaries match ORDERED→SUPPLIER_READY→SHIPPED→ARRIVED_DESTINATION→WAREHOUSE_RECEIPT
   - HANDOVER_TO_FORWARDER and CUSTOMS_CLEARED remain diagnostic
   - audit ORDERED → SUPPLIER_READY
   - audit SUPPLIER_READY → SHIPPED
   - audit SHIPPED → ARRIVED_DESTINATION
   - audit ARRIVED_DESTINATION → WAREHOUSE_RECEIPT
   - Calendar-day rules
   - no negative durations

8. Total Lead Time
   - derived
   - exactly four non-overlapping configured stages
   - no diagnostic milestone double counting
   - no independently editable stored total

9. Payment Boundary
   - Supplier Default reused
   - no duplicated Sourcing Payment Profile
   - no premature override fields

10. Configured / Planned / Actual
    - only Configured belongs here
    - no PO/Actual logistics events
    - no analytics auto-overwrite

11. Planning Readiness
    - derived
    - no manual Ready checkbox
    - incomplete save allowed
    - missing required data => not Ready
    - inactive Supplier/Item => not Ready
    - precedence is `INACTIVE → BLOCKED → INCOMPLETE_DATA → READY`
    - stable machine-readable reason codes are returned
    - explicit `evaluationDate` is required and returned
    - Reactivation enforces active Item/Supplier, compatible Supply Method, matching Base Unit and valid assigned references
    - missing Price/MOQ/route completeness may Reactivate as `INCOMPLETE_DATA`

12. Historical Integrity
    - deactivation does not destroy historical Price/Sourcing resolution
    - no dangerous cascading deletes

13. Database Integrity
    - foreign keys
    - indexes
    - inclusive Price-range integrity through `btree_gist`
    - Preferred Item-row locking plus partial unique index
    - exact Price/MOQ `numeric(18,6)` precision
    - migration safety

14. API
    - safe use-case boundaries
    - server validation
    - no unrestricted dangerous CRUD

15. UI
    - Brand Rules
    - RTL
    - Sourcing grid
    - filters
    - Preferred UX
    - Readiness UX
    - Price History UX
    - no future-module leakage

16. Permissions / Auditability

17. Performance
    - efficient current Price lookup
    - preferred lookup
    - no N+1
    - no unnecessary history scanning
    - no premature caching/Redis

18. Tests
    Verify meaningful assertions especially:
    - inclusive Price overlap and concurrent database exclusion
    - concurrent Preferred changes and stale-state 409
    - Preferred not client-editable
    - Item/Supplier/Base Unit immutability
    - Base Unit mismatch reason
    - Reactivation fundamental-vs-incomplete boundary
    - Derived Readiness precedence/reason codes/evaluationDate
    - Price/MOQ zero and precision
    - historical Price preservation

19. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01..05
    - approved MRP-05A
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

# E. Final Pre-Codex Audit — MRP-05

## Result

`PASS`

## Audited Approved Decisions

تمام ۱۶ تصمیم تأییدشده Package 05 پوشش داده شده‌اند:

1. Multiple Suppliers / Sourcing Options per Item.
2. Preferred Sourcing Option.
3. Item↔Supplier relationship belongs to Sourcing.
4. Price belongs to Sourcing and preserves History.
5. Every Price has Currency.
6. Price Effective From/To.
7. MOQ belongs to Sourcing.
8. Supplier operational Lead Time belongs to Sourcing.
9. Sourcing contains default Supply Route.
10. Total Lead Time is Derived.
11. Configured timing separated from future Planned/Actual timing.
12. Historical data cannot silently overwrite configured Lead Time.
13. Supplier Default Payment Profile reused; no Sourcing-level duplicate.
14. BUY / OUTSOURCE / HYBRID external sourcing behavior.
15. Active / Inactive lifecycle.
16. Incomplete Sourcing allowed; Planning Readiness Derived.

## Additional Approved Clarifications

The following clarifications were added to eliminate implementation ambiguity while preserving the approved scenario:

- Same Supplier + Item may have multiple Sourcing Options for different routes.
- Price and MOQ use Item Base Unit in v1.
- Current Price is derived from non-overlapping Price History.
- A missing lead-time stage is different from an intentional `0 days`.
- Incoterm may be null where genuinely not applicable.
- At most one active Preferred external option exists per Item.
- Preferred external option does not automatically defeat internal MAKE route for HYBRID Items.
- Historical configuration changes rely on Audit plus future PO snapshots; no duplicate master-version engine is introduced.
- No Product sourcing is added in this Package.

## Cross-Package Consistency

- MRP-01 owns Currency, Shipping Mode and Incoterm references.
- MRP-03 owns Supplier identity and Default Payment Profile.
- MRP-04 owns Item identity, Supply Method and Base Unit.
- MRP-05 owns external Item/Supplier commercial and route configuration.
- MRP-06 will own BOM.
- Later PO package will Snapshot Price/Currency/Payment/Planned Timing.
- Later Logistics/Receipt features will own Actual milestone dates.
- Later Analytics will compare Configured/Planned/Actual lead times.
- Later Order Planning will choose/compare Preferred and alternative sources.

No ownership conflict was found.

## Architecture Corrections Applied

- Sourcing Option is not constrained to Item+Supplier uniqueness.
- Price History is separated from mutable Sourcing configuration.
- Price overlap is explicitly prohibited.
- Current Price is derived rather than synchronized manually.
- Total Configured Lead Time is derived.
- Payment Profile duplication is prohibited.
- Planning Readiness is derived rather than manually entered.
- Incomplete Sourcing can be stored without fabricating data.
- Preferred switching is marked concurrency-sensitive.
- Historical Actual performance cannot auto-edit Sourcing Master.
- Packaging/UOM conversion is explicitly deferred.

## Prompt Audit

### MRP-05A
PASS

Analysis-only, Repository-aware, and specifically requires review of Price-range integrity, Preferred concurrency and ownership boundaries.

### MRP-05B
PASS

Implementation is tightly scoped to Sourcing and explicitly prevents PO/MRP/Analytics/Cash Flow leakage.

### MRP-05C
PASS

Independent read-only audit covers price history, route integrity, planning readiness, historical integrity, concurrency and specification drift.

## Final Decision

`MRP-05 Sourcing & Commercial Terms` is approved for the project Prompt Baseline.

It should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives the final cross-package audit.
