# Hadron ERP — MRP Package 04 Final

**Package:** MRP-04 Part & Component Master + Unified Item Completion  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 04-PART-COMPONENT-MASTER

## 1. Purpose

این Feature زیرساخت Master Data مربوط به `Part` و `Component` را ایجاد می‌کند و Unified Item Model را تا سطح موردنیاز MRP تکمیل می‌کند.

هدف:

- تعریف استاندارد Part و Component
- تفکیک مشخصات ذاتی Item از اطلاعات Sourcing
- ایجاد Planning Policy مستقل برای مواد
- آماده‌سازی معماری برای Multi-Level BOM
- جلوگیری از تکرار Data Model بین Product / Part / Component
- جلوگیری از وابستگی Master Data به Supplier-specific commercial data

این Feature نباید BOM، Sourcing، Inventory یا MRP Calculation Engine را زودتر از موعد پیاده‌سازی کند.

---

## 2. Unified Item Model

### LOCKED DECISION

Unified Item Model شامل حداقل Typeهای زیر است:

```text
Item
├── Product
├── Part
└── Component
```

MRP-02 Product را شروع کرده است.

MRP-04 باید Unified Item architecture را تا حدی تکمیل کند که:

- Product
- Part
- Component

همه Technical Identity مشترک و سازگار داشته باشند.

این Feature نباید Unified Item را تبدیل به EAV یا Generic Entity Framework پیچیده کند.

### OWNER-APPROVED MRP-04A IMPLEMENTATION BASELINE

- existing `catalog.items` remains the shared Product / Part / Component root
- Part and Component share `catalog.material_item_profiles`; separate duplicate profile tables are not created
- shared Item ownership may move from the Product namespace to a Catalog namespace without changing Product schema or behavior
- `Item.Version : bigint` is the single aggregate concurrency token
- Part and Component numbering uses separate PostgreSQL sequences
- no BOM, Sourcing, Inventory, File Storage or manually maintained Readiness behavior is implemented

---

## 3. Item Type Definitions

### Product

محصول نهایی هادرون.

### Part

Itemی که مستقیماً در Final Assembly یک Product مصرف می‌شود.

### Component

Itemی که برای ساخت Part، Subassembly یا Component دیگر مصرف می‌شود.

### LOCKED PRINCIPLE

Component الزاماً فقط یک Level پایین‌تر از Part نیست.

Multi-level chain مجاز است:

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

Relationship بین این Levelها در BOM تعریف می‌شود، نه در Master.

---

## 4. Part Identity

هر Part دارای:

### Internal Technical ID

- system-generated
- immutable
- machine-oriented
- not user-entered

### Part Official Item Code

Example:

```text
PRT-0001
PRT-0002
```

Rules:

- auto-generated
- server-side
- unique
- immutable
- concurrency-safe
- not user-entered
- gapless numbering is NOT required
- shown read-only after creation
- used for search, sort and display

---

## 5. Component Identity

هر Component دارای:

### Internal Technical ID

- system-generated
- immutable
- machine-oriented
- not user-entered

### Component Official Item Code

Example:

```text
CMP-0001
CMP-0002
```

Rules:

- auto-generated
- server-side
- unique
- immutable
- concurrency-safe
- not user-entered
- gapless numbering is NOT required
- shown read-only after creation
- used for search, sort and display

---

## 6. Part / Component Official Item Code

`Item.BusinessNumber` تنها Official Item Code برای Product، Part و Component است.

- Part: `PRT-{sequence:D4}`
- Component: `CMP-{sequence:D4}`
- server-generated
- globally unique
- immutable
- never user-entered
- never editable

`Item.Code` و `Item.NormalizedCode` هیچ معنای تجاری مستقلی ندارند و برای archival و removal deprecate شده‌اند. هیچ Code فعال دومی مجاز نیست.

---

## 7. Names

Fields:

- Name FA
- Name EN

Rules for Part and Component:

- Name FA: Required
- Name EN: Optional
- Name is not Business Key
- Name is not hard-unique
- duplicate obvious names may warn but do not block save
- duplicate-name warning comparison is scoped by `ItemType`: Part only against Part, Component only against Component
- update warning queries exclude the current Item
- null/empty `Name EN` is ignored by duplicate warning logic
- no database uniqueness constraint exists for `Name FA` or `Name EN`

---

## 8. Category

### Part

Uses:

`Part Category`

### Component

Uses:

`Component Category`

Rules:

- one primary Category
- required
- active Category required for new assignment
- historical inactive Category remains resolvable
- no Tags in this Feature
- no category hierarchy in this Feature

---

## 9. Base Unit

Every Part and Component must have Base Unit.

Rules:

- Required
- from Shared ERP Unit
- active Unit required for new assignment
- historical inactive Unit remains resolvable

Examples:

```text
PCS
KG
G
M
MM
```

Unit Conversion is out of scope.

---

## 10. Supply Method

### LOCKED DECISION

Part and Component have a controlled `Supply Method`.

Allowed baseline values:

```text
Buy
Make
Outsource
Hybrid
```

Definitions:

### Buy

Item آماده از Supplier خریداری می‌شود.

### Make

Item داخل هادرون از BOM / internal process ساخته می‌شود.

### Outsource

ساخت یا مونتاژ Item توسط شخص ثالث انجام می‌شود.

### Hybrid

بیش از یک روش تأمین معتبر برای Item وجود دارد.

Examples:

- Buy + Make
- Buy + Outsource
- Make + Outsource

### Important

Supply Method فقط Classification است.

این Field به‌تنهایی:

- BOM ایجاد نمی‌کند
- Supplier ایجاد نمی‌کند
- Sourcing Route ایجاد نمی‌کند
- Procurement Logic کامل ایجاد نمی‌کند

BOM و Sourcing در Packageهای بعدی Route واقعی تأمین را مشخص می‌کنند.

---

## 11. Supply Method vs Planning Readiness

Supply Method نباید با Planning Readiness اشتباه شود.

Item ممکن است:

```text
Supply Method = Buy
```

داشته باشد ولی هنوز:

- Supplier
- Price
- MOQ
- Lead Time

نداشته باشد.

بنابراین `Ready` نباید Manual Flag باشد.

Future MRP Readiness باید از Data completeness به‌صورت Derived محاسبه شود.

Examples:

```text
Ready
Incomplete Data
Blocked
Review Required
```

MRP-04 این Engine را نمی‌سازد.

---

## 12. Net Weight

`Net Weight`

یک مشخصه ذاتی Item است و در Master باقی می‌ماند.

Rules:

- Optional unless business requires later
- numeric/decimal
- >= 0
- canonical unit = kg unless a future general weight-unit model is explicitly approved

For current baseline:

```text
Net Weight (kg)
```

---

## 13. Gross Weight

### LOCKED DECISION

`Gross Weight` از Part/Component Master حذف می‌شود.

Reason:

Gross Weight معمولاً به:

- packaging
- supplier packaging
- carton configuration
- shipment configuration

وابسته است.

بنابراین Gross Weight باید بعداً در Packaging / Sourcing / Logistics context قرار گیرد.

MRP-04 نباید Gross Weight را به‌عنوان intrinsic Item Master field نگهداری کند.

---

## 14. Image

Image remains a future optional capability. MRP-04 does not persist an Image/File field.

### LOCKED DIRECTION

Preferred architecture:

`Image / File Reference`

نه وابستگی دائمی به external URL.

If file storage is not yet available:

- Image feature must not block Part/Component creation
- Codex must not invent a new storage platform solely for MRP-04
- architecture should preserve future file-reference compatibility
- no external URL, dangling File ID, binary content or persisted File field is introduced by MRP-04

No external URL compatibility mechanism is approved for MRP-04.

---

## 15. Default Planning Scrap %

Part and Component may have:

`Default Planning Scrap %`

Rules:

```text
0 <= Scrap % <= 100
```

Use decimal/numeric.

### Purpose

Default planning assumption for the Item.

### Future Override

BOM Line may later allow an approved process-specific Scrap Override.

This is important because one Item may have different scrap behavior in different Parent/BOM contexts.

MRP-04 does not implement BOM Line override.

---

## 16. Planning Policy

### LOCKED DECISION

Part / Component planning policy is independent from Product thresholds.

Each Part or Component may contain:

- Safety Stock Qty
- Critical Days
- Safe Start Days
- Excess Start Days

Validation:

```text
Safety Stock Qty >= 0

0 <= Critical Days
Critical Days <= Safe Start Days
Safe Start Days <= Excess Start Days
```

These values are Material Planning policy.

They do not inherit Product thresholds.

---

## 17. Supplier / Commercial Data Boundary

### LOCKED DECISION

The following DO NOT belong in Part/Component Master:

- Supplier
- Purchase Price
- Purchase Currency
- MOQ
- Supplier Lead Time
- Shipping Mode
- Incoterm
- Forwarder to Shipment Days
- Shipping Period Days
- Customs Period Days
- Preferred Supplier
- price validity
- supplier-specific commercial terms

These belong to:

`MRP-05 Sourcing & Commercial Terms`

---

## 18. Initial Inventory

### LOCKED DECISION

`Initial Inventory Qty` must NOT be stored as an ongoing Part/Component Master field.

Opening Inventory belongs to Inventory Foundation.

Future model:

```text
Item
Warehouse
Quantity
Effective Date
```

MRP-04 must not create Inventory transactions.

---

## 19. Component → Part Output Relationship

### LOCKED DECISION

MRP-04 does NOT store a direct `Output Part` field on Component.

Example:

```text
PCB + LED + IC
      ↓
PCBA
```

is a BOM / production structure relationship.

It belongs to:

`MRP-06 Multi-Level BOM`

not Part/Component Master.

This avoids hard-wiring one Component to one Output Part.

---

## 20. Multi-Level Component Support

Component can itself be parent output of another BOM in future.

Example:

```text
Component X
   ↓
Component Y + Component Z
```

Therefore Master Data must not assume:

- Component is always purchased
- Component is always leaf-level
- Component cannot have its own BOM

Supply Method and BOM later determine behavior.

---

## 21. Active / Inactive Lifecycle

Part and Component support:

- Active
- Inactive

Rules:

- baseline lifecycle = deactivate/reactivate
- used historical Item must not be hard deleted
- inactive Item excluded by default from new BOM/Sourcing selections
- historical data remains resolvable

---

## 22. Planning Readiness

Part/Component may be created before all downstream data exists.

Examples:

- no Sourcing yet
- no BOM yet
- no Price yet
- no Supplier yet

Creation must not be blocked by future dependency absence unless a specific Master-level required field is missing.

### LOCKED DECISION

Do not create a manually editable:

`Ready`

field.

Future readiness is Derived.

---

## 23. Notes

Notes:

- Optional
- human explanation only
- not machine logic
- must not be parsed by calculations

---

## 24. Approved Part Fields

| Field | Rule |
|---|---|
| Internal ID | System / immutable |
| Official Item Code (`Item.BusinessNumber`) | PRT / auto / globally unique / immutable |
| Name FA | Required |
| Name EN | Optional |
| Part Category | Required |
| Base Unit | Required |
| Supply Method | Required |
| Net Weight kg | Optional / >= 0 |
| Image/File Reference | Deferred; no MRP-04 File field/platform |
| Default Planning Scrap % | 0..100 |
| Safety Stock Qty | >= 0 |
| Critical Days | >= 0 |
| Safe Start Days | >= Critical |
| Excess Start Days | >= Safe |
| Active Status | Active / Inactive |
| Notes | Optional |
| Created At | System |
| Updated At | System |

---

## 25. Approved Component Fields

| Field | Rule |
|---|---|
| Internal ID | System / immutable |
| Official Item Code (`Item.BusinessNumber`) | CMP / auto / globally unique / immutable |
| Name FA | Required |
| Name EN | Optional |
| Component Category | Required |
| Base Unit | Required |
| Supply Method | Required |
| Net Weight kg | Optional / >= 0 |
| Image/File Reference | Deferred; no MRP-04 File field/platform |
| Default Planning Scrap % | 0..100 |
| Safety Stock Qty | >= 0 |
| Critical Days | >= 0 |
| Safe Start Days | >= Critical |
| Excess Start Days | >= Safe |
| Active Status | Active / Inactive |
| Notes | Optional |
| Created At | System |
| Updated At | System |

---

## 26. Official Item Code Integrity and Legacy Archive

`Item.BusinessNumber` is globally unique across:

- Product
- Part
- Component

Do not silently lose existing deprecated Code values.

### OWNER-APPROVED DATABASE INTEGRITY

The final active model must always satisfy:

```text
BusinessNumber IS NOT NULL
BusinessNumber is globally unique
BusinessNumber matches the ItemType prefix and sequence format
BusinessNumber is immutable
```

Before deprecated fields are dropped, every non-null historical value must be copied to:

```text
catalog.item_legacy_codes
  ItemId
  LegacyCode
  LegacyNormalizedCode
  ArchivedAtUtc
```

The archive is migration/history-only. It has no normal UI, business logic, search identity or duplicate current code meaning. After archival, `Item.Code` and `Item.NormalizedCode` are removed from the active model. A second automatic Code must not be created.

---

## 27. Official Item Code Generation

Official Item Code prefixes:

```text
Product:   PRD-
Part:      PRT-
Component: CMP-
```

Each Official Item Code generation strategy must be:

- server-side
- concurrency-safe
- immutable
- unique
- non-user-overridable

Exact sequence implementation should reuse approved MRP-02 pattern where appropriate.

Approved sequence names:

```text
catalog.part_number_seq
catalog.component_number_seq
```

Gapless sequence allocation is not required.

---

## 28. Supply Method Storage

Supply Method is System-controlled.

Users may choose from approved values but cannot redefine machine meaning.

Business Logic must use stable technical codes.

Display labels may be localized.

Baseline technical codes:

```text
BUY
MAKE
OUTSOURCE
HYBRID
```

---

## 29. Management UI

Recommended navigation:

```text
Items
├── Products
├── Parts
└── Components
```

MRP-04 only adds Parts and Components.

Required capabilities:

- Part list/grid
- Component list/grid
- Search
- Active/Inactive filter
- Category filter
- Supply Method filter
- Add
- Edit
- Deactivate
- Reactivate
- Detail view

Reuse existing Product Master/grid/form patterns where suitable.

Follow:

`docs/brand/UI-BRAND-RULES.md`

---

## 30. Create / Edit UX

System-generated:

- Part / Component Official Item Code

User-entered:

- Name FA
- Name EN
- Category
- Base Unit
- Supply Method
- Net Weight
- Image/File Reference where supported
- Scrap %
- Planning Policy
- Notes

Do not show Supplier/Price/MOQ/Lead Time/Inventory fields in these forms.

---

## 31. Reference Data Integration

MRP-04 consumes:

From MRP-01:

- Part Category
- Component Category
- Unit

System-controlled:

- Supply Method

Rules:

- active reference values for new assignment
- historical inactive values remain resolvable
- no duplicate Reference Data tables

---

## 32. Permissions

Architecture should distinguish:

- View Part/Component Master
- Manage Part/Component Master

Exact permission granularity may reuse shared Item permissions if existing architecture supports it cleanly.

Do not build parallel authorization.

---

## 33. Auditability

Important changes should be compatible with audit:

- Create
- Code change
- Name change
- Category change
- Base Unit change
- Supply Method change
- Scrap change
- Planning Policy change
- Deactivate
- Reactivate

No parallel Audit platform.

---

## 34. API / Application Use Cases

Required use cases include:

### Parts

- list/search
- get detail
- create
- update allowed fields
- deactivate
- reactivate

### Components

- list/search
- get detail
- create
- update allowed fields
- deactivate
- reactivate

Do not expose dangerous unrestricted generic CRUD.

---

## 35. Validation

Server-side validation is mandatory.

At minimum:

- correct Item Type
- Number cannot be client supplied
- Code normalized
- Code globally unique if present
- Name FA required
- Category required
- Base Unit required
- Supply Method required and valid
- Net Weight >= 0
- Scrap % between 0 and 100
- Safety Stock >= 0
- Critical >= 0
- Safe >= Critical
- Excess >= Safe
- active Category/Unit required for new assignment
- client cannot alter Item Type after creation without an explicitly approved migration flow

---

## 36. Item Type Immutability

### LOCKED DIRECTION

A created Item should not casually switch between:

- Product
- Part
- Component

because Type changes may affect BOM, Sourcing and historical meaning.

Baseline:

Item Type is immutable after creation.

If a real business correction is needed later, it should use a controlled migration/correction process.

MRP-04 must not offer simple UI Type switching.

---

## 37. Precision

Approved storage precision:

- Net Weight: `numeric(18,6)`, nullable, value `>= 0`
- Safety Stock Qty: `numeric(18,6)`, value `>= 0`
- Default Planning Scrap %: `numeric(5,2)`, value between `0` and `100`

Do not use binary floating point.

Days fields may use appropriate integer type.

---

## 38. Performance

Required support:

- indexed Official Item Code (`Item.BusinessNumber`)
- active filtering
- Category filtering
- Supply Method filtering

Avoid:

- N+1
- loading BOM/Sourcing data that does not exist yet
- speculative caching
- generic polymorphic queries that are unnecessarily complex

---

## 39. Image/File Handling Boundary

If existing file storage exists:

- reuse it

If not:

- do not implement MinIO/S3 solely for this Feature unless already approved in foundation
- defer Image/File persistence
- preserve a future File Reference integration point
- do not persist an external URL, dangling UUID, file bytes or a placeholder File field in MRP-04

Image handling must not block core Master Data.

---

## 40. Migration from Legacy Component Terminology

Legacy system used `Component` for many items that are actually direct Final Assembly parts.

### LOCKED BUSINESS TERMINOLOGY

During migration:

- direct Final Assembly items → Part
- lower-level inputs used to make Parts/Subassemblies → Component

Codex must not automatically migrate legacy data in MRP-04 unless an explicit migration dataset/task is provided.

---

## 41. Explicit Non-Scope

MRP-04 must NOT implement:

- Supplier linkage
- Sourcing
- Purchase Price
- Purchase Currency
- MOQ
- operational Supplier Lead Time
- Shipping Mode relation
- Incoterm relation
- Forwarder Days
- Shipping Days
- Customs Days
- Preferred Supplier
- BOM
- BOM lines
- Component-to-Part direct mapping field
- Inventory
- Opening Balance
- Forecast
- Production Actual
- Mapping
- Order Planning
- Purchase Orders
- Gross shipping weight
- Packaging specification
- full File Storage platform
- Readiness Engine
- Workflow Engine

---

## 42. Acceptance Criteria

MRP-04 is acceptable when:

1. Unified Item cleanly supports Product/Part/Component.
2. Part and Component have separate generated Official Item Code sequences.
3. Official Item Codes are server-generated, globally unique, immutable and concurrency-safe.
4. No manual or second active Item Code exists.
5. Name FA required / EN optional.
6. Correct Category source used per Item Type.
7. Base Unit required.
8. Supply Method = Buy/Make/Outsource/Hybrid.
9. Supply Method machine meaning is system-controlled.
10. Net Weight remains intrinsic Master Data.
11. Gross Weight is not stored in Part/Component Master.
12. Image/File persistence is deferred and no storage infrastructure or placeholder File field is created.
13. Default Scrap % validates 0..100.
14. Part/Component have independent Planning Policy.
15. Product thresholds are not reused.
16. Supplier/Price/Currency/MOQ/Lead Time/Shipping/Customs do not exist in these Masters.
17. Initial Inventory does not exist in these Masters.
18. No direct Component→Output Part field exists.
19. Multi-Level Component future support is not blocked.
20. Active/Inactive lifecycle works.
21. Item can exist before Sourcing/BOM.
22. No manual Ready flag.
23. Item Type cannot casually change.
24. Server-side validation exists.
25. Required concurrency/uniqueness tests pass.
26. No BOM/Sourcing/Inventory feature is prematurely implemented.

---

## 43. Locked Decisions

1. Part and Component use Unified Item.
2. Part Official Item Code = `PRT-{sequence:D4}`.
3. Component Official Item Code = `CMP-{sequence:D4}`.
4. `Item.BusinessNumber` is the single Official Item Code.
5. Official Item Code is globally unique across Product/Part/Component; no second active Item Code is allowed.
6. Name FA required.
7. Name EN optional.
8. One Part Category / Component Category.
9. Base Unit required.
10. Supply Method required.
11. Supply Methods = Buy / Make / Outsource / Hybrid.
12. Part = direct Final Assembly item.
13. Component = lower-level input for Part/Subassembly/Component.
14. Multi-Level Components are allowed.
15. Net Weight remains in Master.
16. Gross Weight removed from Master.
17. Image/File persistence deferred; MRP-04 creates no File field/platform and preserves future File Reference direction.
18. Default Planning Scrap % in Master.
19. BOM Line may later override Scrap where approved.
20. Part/Component Planning Policy independent from Product.
21. Supplier/Price/Currency/MOQ/Lead Time/Shipping/Customs removed from Master.
22. Initial Inventory removed from Master.
23. Component→Part output relation belongs to BOM, not Master.
24. Active/Inactive lifecycle.
25. Part/Component may exist with incomplete downstream data.
26. Planning Readiness is Derived, not manually entered.
27. Item Type is immutable in normal UI.
28. No future Package business logic is implemented here.

---

## 44. Open Questions

No Business-critical open question blocks MRP-04.

The prior `Supply Method` open question is now CLOSED with:

```text
Buy / Make / Outsource / Hybrid
```

MRP-04A raised and resolved the following technical questions:

- exact Unified Item table strategy → existing `catalog.items` + `catalog.material_item_profiles`
- safe Item identity migration → archive non-null deprecated Code values in `catalog.item_legacy_codes`, then remove Code/NormalizedCode from the active model
- numbering sequence reuse → separate `catalog.part_number_seq` and `catalog.component_number_seq`
- file reference integration → deferred; no File field/platform in MRP-04
- concurrency constraints → one `Item.Version : bigint`

These resolutions may not change locked Business decisions.

MRP-04A technical questions listed above are CLOSED by the Owner-approved baseline in this Package. No Business-critical open question blocks MRP-04B.

---

# B. Prompt 04A — Part & Component Master Analysis

```text
# MRP-04A — Part & Component Master Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.

Do not assume MRP-01/02/03 implementation succeeded merely because it was planned.

TASK:
Analyze MRP-04 Part & Component Master + Unified Item completion.

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

1. Understanding of Product / Part / Component terminology and boundary.
2. Actual Repository findings.
3. Proposed smallest clean Unified Item design compatible with existing Product implementation.
4. Part and Component Official Item Code generation and immutability.
5. Global uniqueness of the single `Item.BusinessNumber` identity across Product/Part/Component.
6. Legacy `Item.Code`/`Item.NormalizedCode` archival and removal impact.
7. Name FA/EN.
8. Category and Unit integration from MRP-01.
9. Supply Method:
   - BUY
   - MAKE
   - OUTSOURCE
   - HYBRID
   Explain stable machine-code implementation.
10. Confirm Part/Component role semantics without hard-coding BOM relationships.
11. Net Weight vs Gross Weight boundary.
12. Optional Image/File Reference boundary without introducing new storage infrastructure.
13. Default Planning Scrap % and future BOM override boundary.
14. Independent Part/Component Planning Policy.
15. Explicit Sourcing boundary:
   Supplier/Price/Currency/MOQ/Lead Time/Shipping/Incoterm/Forwarder/Customs are NOT Master fields.
16. Initial Inventory boundary.
17. Confirm no direct Component→Output Part field.
18. Confirm multi-level Component future support.
19. Active/Inactive lifecycle.
20. Derived Planning Readiness; no manual Ready flag.
21. Item Type immutability.
22. API/application use cases.
23. UI:
   - Parts list
   - Components list
   - filters
   - forms/details
   - reuse Product Master patterns
24. Permissions/Audit integration.
25. DB constraints/indexes.
26. Numeric precision.
27. Performance.
28. Testing plan.
29. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01/02/03/04 and actual Repository.
30. Smallest implementation sequence.
31. Explicit non-scope.

Testing proposal must cover at minimum:
- Part Official Item Code uniqueness/concurrency
- Component Official Item Code uniqueness/concurrency
- client cannot supply or override generated Official Item Codes
- API and DB immutability
- no manual or second active Item Code
- legacy Code archival before active-column removal
- required FA name
- optional EN
- correct Category per Type
- Base Unit required
- Supply Method validation
- Net Weight >= 0
- Scrap 0..100
- Safety Stock >= 0
- Critical <= Safe <= Excess
- Active/Inactive
- inactive Reference Data behavior
- Item creation without Sourcing/BOM
- no Initial Inventory field
- no Gross Weight field
- no direct Component→Part field
- no manual Ready field
- Item Type immutability

Output:
1. Understanding
2. Repository Findings
3. Unified Item Proposal
4. Identity / Official Item Code
5. Legacy Code Archive and Removal Strategy
6. Data Model
7. Supply Method
8. Planning Policy
9. Ownership Boundaries
10. File/Image Boundary
11. Validation / Constraints
12. API Design
13. UI Design
14. Permissions / Audit
15. Performance
16. Tests
17. Risks / Contradictions
18. Implementation Plan
19. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 04B — Part & Component Master Implementation

```text
# MRP-04B — Implement Part & Component Master

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-04A Analysis

Inspect actual prior implementation and reuse approved patterns.

TASK:
Implement only the approved MRP-04 design.

If MRP-04A contains unresolved material contradictions, STOP.

UNIFIED ITEM
- retain existing `catalog.items` as the shared root and extend only the approved Unified Item foundation
- support Product / Part / Component
- use one `catalog.material_item_profiles` table for both PART and COMPONENT profiles
- shared Item ownership may move to a Catalog namespace only without unintended Product schema, API or behavior changes
- do not create EAV architecture
- do not implement BOM/Sourcing behavior

IDENTITY
Part:
- Internal ID
- server-generated Part Official Item Code (`PRT-{sequence:D4}`)

Component:
- Internal ID
- server-generated Component Official Item Code (`CMP-{sequence:D4}`)

Official Item Codes:
- unique
- immutable
- concurrency-safe
- server-generated
- not user-overridable
- gapless not required

ITEM CODE
- `Item.BusinessNumber` is the single Official Item Code across PRODUCT/PART/COMPONENT
- generated server-side from PRD/PRT/CMP sequences, globally unique and immutable
- absent from Create/Update input contracts and shown read-only after creation
- search, sort and display use Official Item Code
- `Item.Code` and `Item.NormalizedCode` are deprecated; no second active Item Code is permitted
- preserve every non-null deprecated value in `catalog.item_legacy_codes` with ItemId, LegacyCode, LegacyNormalizedCode and ArchivedAtUtc before dropping active columns
- legacy archive is migration/history-only and must not participate in UI, business logic or search

DUPLICATE NAMES
- warning only; no Name FA/EN database uniqueness
- PART compares only against PART and COMPONENT only against COMPONENT
- exclude the current Item on update
- ignore null/empty Name EN

FIELDS
Implement approved Part/Component fields:
- Name FA
- Name EN
- Category
- Base Unit
- Supply Method
- Net Weight kg
- no persisted Image/File field in MRP-04; preserve only future File Reference compatibility
- Default Planning Scrap %
- Safety Stock Qty
- Critical Days
- Safe Start Days
- Excess Start Days
- Active Status
- Notes
- timestamps

SUPPLY METHOD
Allowed stable codes:
- BUY
- MAKE
- OUTSOURCE
- HYBRID

Do not let users redefine machine meaning.

PLANNING POLICY
Validate:
- Safety Stock >= 0
- Critical >= 0
- Safe >= Critical
- Excess >= Safe

Do not reuse Product thresholds.

PRECISION
- Net Weight kg and Safety Stock Qty use `numeric(18,6)`
- Default Planning Scrap % uses `numeric(5,2)`

SCRAP
- default Scrap 0..100
- do not implement BOM override yet

WEIGHT
- Net Weight only
- do not add Gross Weight to Master

IMAGE
- optional
- reuse existing approved file mechanism if available
- do not build new S3/MinIO/file platform solely for MRP-04

REFERENCE DATA
Reuse:
- Part Category
- Component Category
- Unit

Do not duplicate Reference Data.

LIFECYCLE
- create
- edit approved fields
- deactivate
- reactivate
- no destructive historical deletion
- no normal UI Item Type switching

CONCURRENCY
- `Item.Version : bigint` is the single Part/Component aggregate token
- Edit/Deactivate/Reactivate require `ExpectedVersion`
- success advances Version; stale mutation returns structured `409 / STALE_VERSION`
- failed mutation changes no state/version and emits no successful audit

READINESS
- do not create manual Ready field
- Item may exist without Sourcing/BOM

STRICT OWNERSHIP BOUNDARY
Do NOT add:
- Supplier
- Price
- Currency
- MOQ
- operational Supplier Lead Time
- Shipping Mode relation
- Incoterm relation
- Forwarder Days
- Shipping Days
- Customs Days
- Preferred Supplier
- Initial Inventory
- Gross Weight
- direct Component→Output Part field

BACKEND
Implement only approved:
- domain/data model
- EF Core mappings
- migrations
- use cases/services
- API
- validation
- errors

FRONTEND
Implement approved:
- Parts grid/list
- Components grid/list
- search
- Active/Inactive filter
- Category filter
- Supply Method filter
- add/edit
- deactivate/reactivate
- detail view

Reuse Product Master patterns.
Follow UI-BRAND-RULES.md.

PERMISSIONS/AUDIT
Reuse existing platform capability.
Do not create parallel infrastructure.

TESTS
At minimum:
- Part Official Item Code uniqueness/concurrency
- Component Official Item Code uniqueness/concurrency
- generated Official Item Codes cannot be supplied or overridden by clients
- global Official Item Code uniqueness and correct PRD/PRT/CMP format
- API and direct-DB immutability
- no manual Code input and no second active Item Code
- migration archives non-null legacy Code values before removing Code/NormalizedCode
- FA required / EN optional
- Part-only and Component-only duplicate-name warnings, self-exclusion and empty Name EN handling
- correct Category per Item Type
- Unit required
- Supply Method valid
- Net Weight negative rejected
- Scrap outside 0..100 rejected
- Safety Stock negative rejected
- threshold order
- lifecycle
- inactive Reference Data behavior
- Item can exist without Sourcing/BOM
- no Gross Weight
- no Initial Inventory
- no Component→Part direct field
- no manual Ready flag
- Item Type immutable
- exact `numeric(18,6)` quantity/weight and `numeric(5,2)` Scrap persistence
- one Item.Version stale-write behavior

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
- Sourcing
- Supplier linkage
- Price/Currency/MOQ
- operational Lead Time
- Shipping/Incoterm
- Forwarder/Shipping/Customs timeline
- BOM
- Inventory
- Forecast
- Mapping
- Order Planning
- Purchase Orders
- Gross shipping weight
- Packaging
- full File Storage platform
- Readiness Engine
- Workflow Engine

Final report:
- Changed
- Database
- Unified Item
- Part
- Component
- Official Item Code and legacy archive
- Supply Method
- Planning Policy
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-05.
```

---

# D. Prompt 04C — Part & Component Master Independent Audit

```text
# MRP-04C — Part & Component Master Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-04A Analysis
- MRP-04B implementation report

Inspect all MRP-04 implementation files.

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
   - Sourcing
   - BOM
   - Inventory
   - Forecast
   - Mapping
   - Order Planning
   - PO

2. Unified Item
   - clean Product/Part/Component support
   - existing `catalog.items` remains the shared root
   - one `catalog.material_item_profiles` table supports PART/COMPONENT without duplicate profiles
   - any Item ownership move to Catalog preserves Product schema, API and behavior
   - no EAV
   - no unnecessary generic architecture
   - Product compatibility preserved

3. Identity
   - Part Official Item Code auto/immutable/concurrency-safe in PRT format
   - Component Official Item Code auto/immutable/concurrency-safe in CMP format
   - client cannot override
   - gapless not assumed

4. Item Code
   - `Item.BusinessNumber` is the single Official Item Code across Product/Part/Component
   - globally unique, correctly formatted and immutable in API and DB
   - absent from Create/Update input and shown read-only after creation
   - no active `Item.Code`/`Item.NormalizedCode` and no second active Item Code
   - migration archives non-null legacy values in `catalog.item_legacy_codes` before column removal
   - archive has no normal UI, business logic or search identity
   - UUID remains the technical identity

5. Duplicate Names
   - warning only with no Name FA/EN database uniqueness
   - PART compares only with PART and COMPONENT only with COMPONENT
   - update excludes the current Item
   - null/empty Name EN creates no warning

6. Type Semantics
   - Part = direct Final Assembly item
   - Component = lower-level input
   - multi-level Components not blocked
   - no direct BOM relationships in Master

7. Fields
   - FA required
   - EN optional
   - Category correct per Type
   - Unit required
   - Supply Method required
   - Net Weight only
   - no Gross Weight
   - Scrap 0..100
   - Planning Policy correct
   - Notes not machine logic

8. Supply Method
   - BUY / MAKE / OUTSOURCE / HYBRID
   - stable system meaning
   - no user-redefinable machine semantics
   - no fake routing logic added

9. Planning Policy
   - independent from Product
   - Safety Stock >= 0
   - Critical <= Safe <= Excess

10. Sourcing Boundary
   Confirm Master does NOT contain:
   - Supplier
   - Price
   - Currency
   - MOQ
   - operational Supplier Lead Time
   - Shipping Mode relation
   - Incoterm relation
   - Forwarder Days
   - Shipping Days
   - Customs Days
   - Preferred Supplier

11. Inventory Boundary
    - no Initial Inventory field
    - no inventory transactions

12. BOM Boundary
    - no direct Component→Output Part field
    - no BOM lines
    - no BOM engine

13. Readiness
    - no manual Ready flag
    - incomplete Item creation allowed

14. Item Type
    - immutable in normal workflow
    - no casual Product↔Part↔Component conversion

15. Image/File
    - no new storage platform, external URL, dangling UUID, file bytes or placeholder File field
    - File persistence is deferred while future File Reference compatibility is preserved

16. Reference Data
    - MRP-01 reused
    - no duplicates
    - inactive assignment/history rules correct

17. Database Integrity
    - constraints
    - indexes
    - global Code uniqueness
    - migration safety
    - Net Weight/Safety Stock `numeric(18,6)` and Scrap `numeric(5,2)`
    - one `Item.Version : bigint` aggregate concurrency token

18. API
    - safe use-case boundaries
    - server validation
    - no unrestricted dangerous CRUD

19. UI
    - Brand Rules
    - RTL
    - Part/Component lists
    - filters
    - create/edit/deactivate/reactivate
    - no Sourcing/BOM/Inventory leakage

20. Permissions/Auditability

21. Performance
    - N+1
    - over-fetching
    - unnecessary polymorphic complexity
    - premature caching/infrastructure

22. Tests
    Verify meaningful assertions, especially:
    - concurrent numbering
    - global Official Item Code uniqueness and PRD/PRT/CMP generation
    - API/direct-DB immutability and no manual Item Code input
    - legacy archive preservation and absence of a second active Item Code
    - ItemType-scoped duplicate warnings, self-exclusion and empty Name EN handling
    - Version advancement and stale Edit/lifecycle conflict
    - Item Type immutability
    - no Master leakage from future packages

23. Specification Drift
    Compare:
    - Master Spec
    - Decisions
    - MRP-01/02/03/04
    - approved MRP-04A
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

# E. Final Pre-Codex Audit — MRP-04

## Result

`PASS`

## Audited Approved Decisions

تمام ۱۶ تصمیم تأییدشده Package 04 پوشش داده شده‌اند:

1. Part/Component Internal ID + auto Official Item Code.
2. No manual or second active Item Code.
3. `Item.BusinessNumber` is globally unique across Product/Part/Component.
4. FA required / EN optional.
5. Separate Part Category / Component Category.
6. Base Unit required.
7. Supply Method = Buy / Make / Outsource / Hybrid.
8. Part vs Component business role locked.
9. Net Weight retained.
10. Gross Weight removed from Master.
11. Image/File persistence deferred with future File Reference direction; no File field/platform in MRP-04.
12. Default Scrap % retained.
13. Independent Material Planning Policy.
14. Sourcing-specific data removed from Master.
15. Initial Inventory removed from Master.
16. Component→Part relationship deferred to BOM.
17. Active/Inactive lifecycle.
18. Planning Readiness derived, not manual.

## Open Question Resolution

Previous Open Question:

`Supply Method`

is now CLOSED.

Approved values:

```text
BUY
MAKE
OUTSOURCE
HYBRID
```

This must be reflected in `DECISIONS.md` and removed/closed in `OPEN-QUESTIONS.md` during the later documentation consolidation pass.

## Cross-Package Consistency

- MRP-02 Product and MRP-04 Part/Component converge on Unified Item.
- MRP-01 provides Category and Unit references.
- MRP-03 Supplier remains independent.
- MRP-05 will own Item↔Supplier commercial/supply relationships.
- MRP-06 will own Part/Component BOM relationships.
- Inventory remains reserved for later Inventory Foundation.
- No Product threshold inheritance is introduced.
- Historical legacy `Component` terminology is explicitly separated from new `Part` terminology.

## Architecture Corrections Applied

- `Gross Weight` moved out of Master because it is packaging/logistics contextual.
- `Initial Inventory Qty` removed from Master.
- Supplier/Price/Currency/MOQ/Lead-Time data fully moved to Sourcing boundary.
- Component does not store `Output Part`.
- `Item.BusinessNumber` made the single globally unique Official Item Code across the Unified Item domain.
- Deprecated Item Code values are preserved only in `catalog.item_legacy_codes` before active-column removal.
- Item Type switching disabled in normal UI.
- Supply Method made a stable system-controlled classification.
- Planning Readiness remains Derived.
- Image/File persistence is deferred and cannot force premature storage infrastructure or a placeholder field.
- Multi-level Components remain supported by future BOM architecture.

## Prompt Audit

### MRP-04A
PASS

Analysis-only, includes Official Item Code generation/immutability and legacy Code archival/removal impact based on actual Repository inspection.

### MRP-04B
PASS

Implementation scope is bounded and explicitly protects Sourcing/BOM/Inventory boundaries.

### MRP-04C
PASS

Independent read-only audit includes Unified Item integrity, global Code uniqueness, type semantics, future-module leakage, database migration safety and tests.

## Final Decision

`MRP-04 Part & Component Master + Unified Item Completion` is approved for the project Prompt Baseline.

According to the agreed workflow, it should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives a final cross-package audit.
