# Hadron ERP — MRP Package 02 Final

**Package:** MRP-02 Product Master  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 02-PRODUCT-MASTER

## 1. Purpose

این Feature اولین Master Data واقعی MRP را برای محصولات نهایی هادرون ایجاد می‌کند.

Product Master باید اطلاعات هویتی و Planning-level محصول نهایی را نگهداری کند، بدون اینکه Business Logic مربوط به BOM، Forecast، Inventory، Production یا Part/Component را زودتر از موعد پیاده‌سازی کند.

این Feature همچنین اولین استفاده حداقلی از Unified Item Model است.

---

## 2. Unified Item Boundary

### LOCKED DECISION

معماری کل MRP از Unified Item Model استفاده می‌کند:

```text
Item
├── Product
├── Part        ← future package
└── Component   ← future package
```

در MRP-02 فقط زیرساختی که Product واقعاً نیاز دارد ساخته می‌شود.

Codex نباید در این Package موارد زیر را بسازد:

- Part UI
- Component UI
- Part business rules
- Component business rules
- Part/Component sourcing
- BOM explosion

---

## 3. Product Identity

هر Product یک شناسه فنی و یک Official Item Code دارد.

### Internal Technical ID

- system-generated
- immutable
- machine-oriented
- not user-entered

### Official Item Code

`Item.BusinessNumber` تنها کد رسمی و user-facing محصول است، مثل:

```text
PRD-0001
PRD-0002
```

Rules:

- auto-generated
- server-side
- unique
- immutable
- not user-entered
- concurrency-safe
- gapless numbering is NOT required
- shown read-only after creation
- used for search, sort and display

---

## 4. Deprecated Item Code Fields

`Item.Code` و `Item.NormalizedCode` هیچ معنای تجاری مستقلی ندارند و برای حذف deprecate شده‌اند.

Rules:

- هیچ Code دومی در Create دریافت نمی‌شود.
- هیچ Code دومی در ordinary Edit قابل تغییر نیست.
- `Item.BusinessNumber` تنها Official Item Code است.
- هر مقدار non-null قدیمی پیش از حذف ستون‌ها در `catalog.item_legacy_codes` آرشیو می‌شود.
- archive فقط migration/history است و در UI، business logic، search یا current identity استفاده نمی‌شود.

---

## 5. Product Names

Fields:

- Product Name FA
- Product Name EN

Rules:

- Product Name FA: Required
- Product Name EN: Optional
- Names are not Business Keys
- Nameها Hard-Unique نمی‌شوند
- Duplicate obvious name may warn but must not hard-block save

---

## 6. Product Category

هر Product فعلاً فقط یک Product Category اصلی دارد.

Rules:

- Required
- from active Product Category reference data
- no multi-category
- no tags
- no hierarchy in this Feature

Historical inactive Category must remain resolvable.

---

## 7. Base Unit

هر Product Base Unit اجباری دارد.

Rules:

- Required
- from Shared ERP Unit
- active Unit for new assignment
- historical inactive Unit remains resolvable
- once any MRP-08 Inventory transaction exists for the Item, ordinary Base Unit change is blocked with `ITEM_BASE_UNIT_LOCKED_BY_INVENTORY`; no UOM conversion or history rewrite

Example:

```text
PCS
```

Unit Conversion در Scope نیست.

---

## 8. Business Unit

Business Unit:

- Optional
- from Shared ERP Business Unit
- for future filtering/reporting
- must not block Product creation

Organization Hierarchy در Scope نیست.

---

## 9. Safety Stock Qty

Safety Stock Qty در Product Master باقی می‌ماند.

Purpose:

Future Finished Product planning.

Rules:

- decimal/numeric
- >= 0
- must not become Part/Component safety stock automatically

---

## 10. Product Coverage Thresholds

Fields:

- Critical Days
- Safe Start Days
- Excess Start Days

Purpose:

Finished Product planning only.

These thresholds must NOT control Part/Component planning.

Validation:

```text
0 <= Critical Days
Critical Days <= Safe Start Days
Safe Start Days <= Excess Start Days
```

---

## 11. Active Status

Product lifecycle:

- Active
- Inactive

Rules:

- system-controlled lifecycle state
- not an arbitrary editable reference list
- used Product must not be hard deleted
- baseline lifecycle = deactivate/reactivate
- inactive Product excluded by default from new planning selections
- historical data remains resolvable

---

## 12. Product Without BOM

Product may be created before BOM exists.

Future planning eligibility:

```text
Planning Eligible Product
=
Active Product
+
Valid BOM
```

MRP-02 must NOT:

- require BOM during Product creation
- build BOM
- create manually maintained `HasBOM` flag

BOM validity must later be derived from BOM data.

---

## 13. Notes

Notes:

- optional free text
- human explanation only
- must not contain executable Business Rules
- calculation logic must never parse Notes

---

## 14. Approved Product Fields

| Field | Rule |
|---|---|
| Internal ID | System / immutable |
| Official Item Code (`Item.BusinessNumber`) | Auto / globally unique / immutable |
| Product Name FA | Required |
| Product Name EN | Optional |
| Product Category | Required |
| Base Unit | Required |
| Business Unit | Optional |
| Safety Stock Qty | >= 0 |
| Critical Days | >= 0 |
| Safe Start Days | >= Critical Days |
| Excess Start Days | >= Safe Start Days |
| Active Status | Active / Inactive |
| Notes | Optional |
| Created At | System |
| Updated At | System |

---

## 15. Product Master UI

Required:

- list/grid
- search
- Active/Inactive filter
- Product Category filter
- Business Unit filter where available
- Add
- Edit
- Deactivate
- Reactivate
- Product details

Reuse existing approved grid patterns.

Follow:

`docs/brand/UI-BRAND-RULES.md`

Do not introduce another grid library only for this Feature.

---

## 16. Create / Edit UX

System-generated:

- Official Item Code (`PRD-{sequence:D4}`)

User-entered:

- Product Name FA
- Product Name EN
- Category
- Base Unit
- Business Unit
- Safety Stock
- Critical/Safe/Excess Days
- Notes

Validation must exist server-side, not only frontend.

---

## 17. Reference Data Integration

MRP-02 consumes from MRP-01:

- Product Category
- Unit
- Business Unit

Rules:

- active values selectable for new assignments
- historical inactive values remain displayable
- no duplicate Reference Data tables

---

## 18. Duplicate Handling

Hard Block:

- duplicate generated Official Item Code

Warning only:

- duplicate Product Name FA
- duplicate Product Name EN

No hard DB uniqueness on product names.

---

## 19. Official Item Code Generation

Requirements:

- server-side
- concurrency-safe
- unique
- immutable
- no client override
- gapless numbering not required

Codex Analysis must propose the simplest robust PostgreSQL / EF Core-compatible mechanism.

---

## 20. Official Item Code Immutability

`Item.BusinessNumber` is not the technical identity; `Item.Id` remains the UUID identity.

Baseline:

- Official Item Code is never user-entered.
- It is never editable through ordinary API/UI operations.
- Application and database protections must reject attempted changes.
- No second active Item Code is permitted.

---

## 21. Permissions

Architecture should distinguish:

- View Product Master
- Manage Product Master

Reuse existing authorization foundation if available.

Do not create a parallel Identity system.

---

## 22. Auditability

Important Product changes should be compatible with audit:

- Create
- attempted Official Item Code change
- Name change
- Category change
- Base Unit change
- Planning threshold changes
- Deactivate
- Reactivate

If central Audit does not yet exist, preserve integration boundary and do not build a competing platform.

---

## 23. API / Application Boundary

Use cases:

- list/search Products
- get Product detail
- create Product
- update allowed fields
- deactivate
- reactivate

Do not expose dangerous unrestricted generic CRUD.

Client cannot supply Internal ID or Official Item Code.

---

## 24. Validation

Server-side validation at minimum:

- Official Item Code is generated server-side
- Official Item Code is globally unique and immutable
- Product Name FA required
- Product Category required
- Base Unit required
- Safety Stock >= 0
- Critical Days >= 0
- Safe Start Days >= Critical Days
- Excess Start Days >= Safe Start Days
- referenced Category/Unit/Business Unit exists
- active reference value required for new assignment
- Official Item Code cannot be supplied/overridden

---

## 25. Precision

Quantity and planning numeric values use decimal/numeric.

Binary floating point must not be used for Business-critical values.

---

## 26. Performance

Support:

- indexed Official Item Code
- efficient Active filtering
- efficient Category filtering
- efficient Business Unit filtering where useful
- server-side paging/filtering where existing grid architecture needs it

Avoid:

- N+1
- unrelated BOM loading
- speculative caching

---

## 27. Seed / Migration

Codex must not invent Product records.

If legacy Product data is not supplied:

- build capability
- do not fabricate production records

Legacy import will later use a controlled migration/import process.

---

## 28. Explicit Non-Scope

MRP-02 must NOT implement:

- Part Master
- Component Master
- Supplier Master
- Sourcing
- BOM
- BOM validation engine
- Forecast
- Actual Production
- Inventory
- Mapping
- Order Planning
- Purchase Orders
- Product costing
- Sales pricing
- Finished Goods planning engine
- Unit Conversion
- Category hierarchy
- Tags
- HR / Contacts
- full Identity
- Workflow Engine

---

## 29. Acceptance Criteria

1. Product uses minimal approved Unified Item architecture.
2. Internal ID is system-controlled.
3. Official Item Code is auto-generated, globally unique and immutable.
4. No manual or second active Item Code exists.
5. Product Name FA is required.
6. Product Name EN is optional.
7. One Product Category.
8. Required Base Unit.
9. Optional Business Unit.
10. Safety Stock >= 0.
11. Critical/Safe/Excess ordering enforced.
12. Product can exist without BOM.
13. No manual HasBOM flag.
14. Active/Inactive lifecycle works.
15. Historical inactive Product remains resolvable.
16. No historical hard delete.
17. Product Name is not hard-unique.
18. MRP-01 Reference Data reused.
19. Server-side validation exists.
20. Official Item Code concurrency/uniqueness tested.
21. API/DB immutability and absence of a second active Item Code tested.
22. No future MRP features prematurely implemented.

---

## 30. Locked Decisions

1. Unified Item starts here only to the extent Product needs it.
2. Internal Technical ID separate from Official Item Code.
3. Official Item Code is `Item.BusinessNumber`, auto-generated, globally unique, immutable and non-user-entered.
4. `Item.Code` and `Item.NormalizedCode` are deprecated for archival and removal; no second active Item Code is allowed.
5. Product Name FA required.
6. Product Name EN optional.
7. One primary Product Category.
8. Base Unit required.
9. Business Unit optional.
10. Safety Stock remains at Product level.
11. Product Critical/Safe/Excess thresholds remain at Product level.
12. Product thresholds do not control Part/Component planning.
13. Threshold order = Critical <= Safe <= Excess.
14. Active/Inactive lifecycle.
15. Product can exist without BOM.
16. Planning eligibility later = Active + Valid BOM.
17. Duplicate Product Name = warning, not hard uniqueness.
18. Notes are informational only.
19. No manually maintained HasBOM field.
20. No downstream MRP module in this Package.

---

## 31. Open Questions

No Business-critical open question blocks MRP-02.

The former technical questions about Official Item Code generation, duplicate-name warning behavior and optimistic concurrency were resolved by the Owner-approved baseline in §32. No implementation-blocking technical question remains.

---

## 32. Final Owner-Approved MRP-02A Implementation Baseline

This section is the authoritative implementation baseline for Prompt 02B and the audit baseline for Prompt 02C. If earlier wording is less specific, this section controls without reopening any locked business decision.

### 32.1 Unified Item Storage

MRP-02 uses schema `catalog` and only this inheritance boundary:

```text
catalog.items
└── catalog.products (1:1)
```

`Item` owns `Id`, `ItemType`, `BusinessNumber`, `NameFa`, nullable `NameEn`, `BaseUnitId`, `IsActive`, nullable `Notes`, `Version`, `CreatedAtUtc` and `UpdatedAtUtc`. `BusinessNumber` is the single Official Item Code. The deprecated `Code` and `NormalizedCode` fields are not part of the final active model.

`Product` owns `ItemId`, `ProductCategoryId`, nullable `BusinessUnitId`, `SafetyStockQty`, `CriticalDays`, `SafeStartDays` and `ExcessStartDays`.

MRP-02 supports only Product. It must not implement Part, Component, EAV or generic metadata.

### 32.2 Identity and Official Item Code

`Item.BusinessNumber` is the Product Official Item Code. It is generated from PostgreSQL sequence `catalog.product_number_seq` and formatted server-side as `PRD-{sequenceValue:D4}`, including `PRD-0001` and `PRD-10000`. It is server-generated, immutable, globally unique, concurrency-safe, absent from create/update contracts and shown read-only after creation. Gaps and non-rollback of consumed sequence values are accepted. `MAX()+1` is prohibited. Search, sort and display use this value.

`Item.Code` and `Item.NormalizedCode` have no independent business meaning and are deprecated for removal. Before they are dropped, every non-null historical value must be copied to `catalog.item_legacy_codes` with `ItemId`, `LegacyCode`, `LegacyNormalizedCode` and `ArchivedAtUtc`. This archive is migration/history-only: no normal UI, business logic, search identity or duplicate current code concept may consume it.

### 32.3 Canonical Storage Types and Limits

| Field | Canonical storage |
|---|---|
| Id / BaseUnitId / ProductCategoryId / BusinessUnitId / ItemId | `uuid` |
| ItemType | `varchar(20)` |
| BusinessNumber — Official Item Code | `varchar(32)` |
| NameFa | `varchar(200)`, required |
| NameEn | `varchar(200)`, nullable |
| SafetyStockQty | `numeric(18,6)` |
| CriticalDays / SafeStartDays / ExcessStartDays | `integer` |
| IsActive | `boolean` |
| Notes | `varchar(2000)`, nullable |
| Version | `bigint` |
| CreatedAtUtc / UpdatedAtUtc | `timestamp with time zone`, UTC |

Unit `DecimalPrecision` is not introduced.

### 32.4 Reference Assignment and Reactivation

MRP-02 consumes MRP-01 sources of truth: `PRODUCT_CATEGORY` and `BUSINESS_UNIT` from `ReferenceValue`, and Base Unit from `Unit`. No duplicate masters are created in `catalog`.

- Create requires an active `PRODUCT_CATEGORY`, an active Unit, and either no Business Unit or an active `BUSINESS_UNIT`.
- Update requires a changed reference to have the correct type and be active. An unchanged inactive historical reference remains resolvable and does not block an unrelated edit.
- Reactivate requires every currently assigned operational reference to be valid and active: Product Category must be active and of type `PRODUCT_CATEGORY`; Base Unit must be active; a non-null Business Unit must be active and of type `BUSINESS_UNIT`.
- Failed reactivation returns structured HTTP `409` with code `PRODUCT_REACTIVATION_BLOCKED` and identifies invalid assigned references.

Historical resolution does not imply reactivation eligibility.

### 32.5 Duplicate-Name Warnings

Duplicate localized Product names are warning-only. Comparison is trimmed and case-insensitive. Empty/null Name EN is ignored. Create compares other Products; Update excludes the current Item ID. Warning codes are `DUPLICATE_NAME_FA` and `DUPLICATE_NAME_EN`. A successful save remains successful, a Product never warns against itself, and no Name FA/EN database uniqueness is allowed.

### 32.6 Planning, Concurrency and Audit

`SafetyStockQty` is `numeric(18,6)` and nonnegative. Thresholds are integers and must satisfy:

```text
0 <= CriticalDays <= SafeStartDays <= ExcessStartDays
```

Equality is valid. Product may exist without BOM. `HasBOM`, `Ready` and `PlanningReady` fields are prohibited; future planning eligibility remains derived.

The aggregate has one concurrency token: `Item.Version : bigint`. Reads return Version; Edit/Deactivate/Reactivate require `ExpectedVersion`; every successful Item or Product-profile mutation advances the same Version; stale mutation returns HTTP `409 / STALE_VERSION`. Item and Product changes persist atomically. No second Product Version is allowed.

MRP-02 reuses `IAuditEventSink` for Product `CREATE`, `EDIT`, `DEACTIVATE` and `REACTIVATE`, with `EntityType = Product` and `EntityId = Item.Id`. Applicable old/new values, UTC timestamp, actor, correlation ID and reason are included. Events are emitted only after successful persistence. Validation, attempted Official Item Code mutation, stale Version, failed reactivation and failed persistence must not emit a successful Product mutation event. Audit persistence remains out of scope.

### 32.7 Database, API, UI and Permissions

Database integrity requires globally unique and immutable BusinessNumber in the `PRD-{sequence:D4}` format, `Version >= 1`, `SafetyStockQty >= 0`, threshold-order checks, required columns, Product `ItemId` as one-to-one PK/FK, restrictive foreign keys, no name uniqueness and no hard delete. Deprecated Code values are archived before column removal. ReferenceValue subtype membership is application-enforced. Only a forward migration is allowed; earlier migrations must not be rewritten.

Explicit APIs are:

```text
GET  /api/products
GET  /api/products/{id}
POST /api/products
PUT  /api/products/{id}
POST /api/products/{id}/deactivate
POST /api/products/{id}/reactivate
```

List supports `search`, `includeInactive`, `productCategoryId`, `businessUnitId`, `page` and bounded `pageSize` with maximum 100. Create/update contracts do not allow client control of Internal ID, Official Item Code, Item Type or IsActive. Update includes `ExpectedVersion`. No DELETE, unrestricted PATCH or generic CRUD exists. Responses include the read-only Official Item Code, resolved reference summaries and activity state.

The UI is Persian-first and RTL and provides Product list, server-side paging/search, Active/Inactive, Product Category and Business Unit filters, Add/Edit, Product detail, Deactivate/Reactivate, one read-only Official Item Code shown after creation, active reference selectors, inactive historical reference display, duplicate-name warnings and structured errors. Create/Edit has no manual Item Code field. No AG Grid dependency or global shell redesign is required; a minimal navigation addition is sufficient.

Future permission concepts remain `catalog.product.view` and `catalog.product.manage`. MRP-02 must not implement parallel Identity or fake authorization and must use central authorization only when it exists.

---

# B. Prompt 02A — Product Master Analysis

```text
# MRP-02A — Product Master Analysis

Read and follow:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-02-PRODUCT-MASTER-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Inspect the actual implementation state of MRP-01 if it exists.

TASK:
Analyze MRP-02 Product Master.

ANALYSIS ONLY.

Do not modify code, database, migrations, configuration, documentation or repository structure.
Do not install packages.
Do not generate implementation code.

Required analysis:

1. Understanding
2. Actual Repository Findings
3. Smallest clean Unified Item design needed for Product now
4. Internal ID / Official Item Code separation
5. Concurrency-safe `PRD-{sequence:D4}` Official Item Code generation
6. Official Item Code API/DB immutability and removal of the deprecated Code pair
7. Product field design
8. MRP-01 Reference Data integration
9. Product creation without BOM; no manual HasBOM
10. Lightweight duplicate-name warning
11. Server-side validation and DB constraints
12. Minimal API/use cases
13. Product grid/list and create/edit UI
14. View/Manage permissions and Audit integration
15. Numeric precision
16. Performance/indexes
17. Testing plan
18. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01, MRP-02 and actual Repository
19. Smallest implementation sequence
20. Explicit non-scope

Tests proposed must cover at minimum:
- Official Item Code uniqueness and PRD format
- concurrent Product creation
- no manual Item Code input
- API and DB immutability
- no second active Item Code
- required FA name
- optional EN name
- required Category
- required Unit
- optional Business Unit
- threshold ordering
- negative quantity rejection
- Active/Inactive
- inactive Reference Data behavior
- Product without BOM
- no HasBOM field
- duplicate Name warning does not hard-block

Output:
1. Understanding
2. Repository Findings
3. Unified Item Proposal
4. Identity / Numbering
5. Data Model
6. Reference Data Integration
7. Validation / Constraints
8. API Design
9. UI Design
10. Permissions / Audit
11. Performance
12. Tests
13. Risks / Contradictions
14. Implementation Plan
15. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 02B — Product Master Implementation

```text
# MRP-02B — Implement Product Master

Read and follow:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/modules/mrp/features/MRP-02-PRODUCT-MASTER-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-02A Product Master Analysis

Inspect actual MRP-01 implementation and reuse it.

TASK:
Implement only the approved MRP-02 Product Master.

If MRP-02A contains unresolved material contradictions, STOP.

FINAL OWNER-APPROVED MRP-02A BASELINE:
- implement `catalog.items` + `catalog.products` 1:1 exactly as defined in Feature Specification §32
- Item owns UUID identity, single Official Item Code/names/Base Unit/lifecycle/Version/timestamps; Product owns Category/Business Unit/Safety Stock/thresholds
- support Product only; no Part/Component behavior, EAV or generic metadata
- generate the single Product Official Item Code in `Item.BusinessNumber` from PostgreSQL sequence `catalog.product_number_seq` as `PRD-{sequenceValue:D4}`
- Official Item Code is server-only, immutable, globally unique, concurrency-safe, gap-tolerant, absent from mutation contracts and never generated with `MAX()+1`
- deprecate and remove `Item.Code` / `Item.NormalizedCode`; never introduce a second generated or user-entered Item Code
- before removal archive every non-null legacy value in `catalog.item_legacy_codes` (`ItemId`, `LegacyCode`, `LegacyNormalizedCode`, `ArchivedAtUtc`) with no UI/business/search use
- use exact §32.3 storage types and limits, including `SafetyStockQty numeric(18,6)`, nullable Name EN, Notes max 2000 and no Unit DecimalPrecision
- use one aggregate token `Item.Version : bigint`; every Item/Product-profile mutation advances it and stale mutation returns structured `409 / STALE_VERSION`
- persist Item/Product atomically and create only a forward migration; never rewrite MRP-01 migrations

Required:

UNIFIED ITEM
- implement only minimal approved Unified Item foundation needed for Product
- do not implement Part/Component features

IDENTITY
- internal system ID
- server-generated Official Item Code
- no manual or second Item Code

Official Item Code:
- unique
- immutable
- server-generated
- concurrency-safe
- not user-overridable
- gapless not required

FIELDS
- Product Name FA
- Product Name EN
- Product Category
- Base Unit
- Business Unit
- Safety Stock Qty
- Critical Days
- Safe Start Days
- Excess Start Days
- Active Status
- Notes
- timestamps

VALIDATION
- generated Official Item Code follows `PRD-{sequence:D4}`, is globally unique and immutable
- FA name required
- EN optional
- active Category required for new assignment
- active Unit required
- Business Unit optional
- Safety Stock >= 0
- Critical >= 0
- Safe >= Critical
- Excess >= Safe
- client cannot set Official Item Code or Internal ID

LIFECYCLE
- create
- edit approved fields
- deactivate
- reactivate
- no historical hard delete

BOM BOUNDARY
- Product must be creatable without BOM
- do not create BOM
- do not create editable/stored HasBOM
- do not block Product creation because BOM does not exist

DUPLICATE NAMES
- no DB uniqueness on FA/EN names
- compare trimmed names case-insensitively and ignore null/empty Name EN
- Create compares other Products; Update excludes the current Item ID
- return non-blocking `DUPLICATE_NAME_FA` / `DUPLICATE_NAME_EN` warnings with successful save
- a Product must never warn against itself

REFERENCE DATA
Reuse from MRP-01:
- Product Category
- Unit
- Business Unit

Do not duplicate reference data.

Reference assignment/reactivation:
- Create requires active correct `PRODUCT_CATEGORY`, active Unit and null or active correct `BUSINESS_UNIT`
- Update requires a changed reference to be correct and active; unchanged inactive historical references remain resolvable for unrelated edits
- Reactivate requires all currently assigned operational references to be valid and active
- inactive Category, Unit or assigned Business Unit blocks Reactivate with structured `409 / PRODUCT_REACTIVATION_BLOCKED` identifying invalid references
- historical resolution does not imply reactivation eligibility

BACKEND
Implement only approved:
- domain/data model
- EF Core mapping
- migrations
- application use cases/services
- API
- validation
- error handling

Database integrity must include globally unique and DB-immutable BusinessNumber in PRD format, Version >= 1, Safety Stock >= 0, threshold ordering, Product ItemId 1:1 PK/FK, restrictive FKs, required fields, no Name uniqueness and no hard delete. Archive the deprecated Code pair before dropping it. ReferenceValue subtype membership remains application-enforced.

Do not introduce speculative frameworks.

FRONTEND
Implement approved:
- Product grid/list
- search
- Active/Inactive filter
- Product Category filter
- Business Unit filter where available
- add/edit/deactivate/reactivate
- approved Product detail UX
- server-side paging with maximum pageSize 100
- one read-only Official Item Code shown after creation; no Code input in Create/Edit
- active assignment selectors, inactive historical reference display, duplicate-name warnings and structured errors

Follow UI-BRAND-RULES.md.
Reuse existing grid/form patterns.
No AG Grid dependency or global shell redesign is required; use only a minimal navigation addition.

PERMISSIONS/AUDIT
Reuse existing platform capabilities.
Do not build parallel Identity/Audit systems.
Retain future permission concepts `catalog.product.view` and `catalog.product.manage`; do not implement fake authorization while central authorization is absent.
Reuse `IAuditEventSink` with EntityType Product and EntityId Item.Id for CREATE/EDIT/DEACTIVATE/REACTIVATE. Emit only after successful persistence. Validation, attempted Official Item Code mutation, stale Version, failed reactivation and persistence failure must not emit a successful Product event.

TESTS
At minimum:
- unique Official Item Code
- concurrent creation does not duplicate Official Item Code
- client cannot set/change Official Item Code
- no manual Code field and no second active Item Code
- FA required
- EN optional
- Category required
- Base Unit required
- Business Unit optional
- negative Safety Stock rejected
- threshold ordering
- inactive lifecycle
- inactive Reference Data invalid for new assignment
- historical inactive Reference Data resolves
- Product without BOM
- no manual HasBOM
- Product Names not hard-unique
- UUID server-generated; Official Item Code follows PRD format, permits gaps, cannot be overridden and never uses MAX()+1
- direct API and direct DB attempts to change Official Item Code are blocked
- failed Official Item Code mutation emits no successful audit
- migration archives non-null legacy Code values and removes Code/NormalizedCode from the active model
- exact decimal Safety Stock, all max lengths and equal thresholds
- wrong Category/Business Unit ReferenceType rejected
- inactive unchanged historical references remain resolvable
- Reactivate with active references succeeds; inactive Category, Unit or assigned Business Unit each blocks it
- failed Reactivate neither increments Version nor emits successful audit
- duplicate Name FA/EN saves with warning; empty Name EN is ignored; Update excludes self; another matching Item warns
- no `HasBOM`, `Ready` or `PlanningReady`
- zero-to-latest migration, unchanged MRP-01 migrations/tests, ReferenceType count 8, Development DB preservation and no orphan test DBs

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend install if required
- frontend build
- frontend lint
- startup/health checks
- successful and failed Product audit semantics with the recording sink

Do not claim unrun checks.

SCOPE GUARD
Do NOT implement:
- Part Master
- Component Master
- Supplier Master
- Sourcing
- BOM
- Forecast
- Production Actual
- Inventory
- Mapping
- Order Planning
- Purchase Orders
- Product costing
- Sales pricing
- Finished Goods planning engine
- Unit Conversion
- Category hierarchy
- Tags
- HR/Contacts
- full Identity
- Workflow Engine

Final report:
- Changed
- Database
- Unified Item
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-03.
```

---

# D. Prompt 02C — Product Master Independent Audit

```text
# MRP-02C — Product Master Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-02A Analysis
- MRP-02B implementation report

Inspect all MRP-02 implementation files.

Do NOT modify code, database, migrations, tests, docs or configuration.
Do not auto-fix.

Audit:

1. Scope
   - no premature Part/Component/Supplier/Sourcing/BOM/Forecast/Inventory/Mapping/Order Planning/PO functionality

2. Unified Item
   - exact `catalog.items` + `catalog.products` 1:1 boundary from §32
   - Item owns common fields and the only aggregate Version; Product owns only Product-specific fields
   - exact canonical types/limits, including Official Item Code 32, names 200, Notes 2000 and Safety Stock `numeric(18,6)`
   - no overbuilt future Part/Component behavior
   - no EAV/generic metadata and no second Product Version

3. Identity
   - Internal ID system-controlled
   - Internal UUID distinct from the single Official Item Code
   - Official Item Code is `Item.BusinessNumber`, generated from `catalog.product_number_seq` as `PRD-{sequenceValue:D4}` and is immutable/concurrency-safe/gap-tolerant
   - Official Item Code absent from mutation contracts; no `MAX()+1`; uniqueness survives concurrent creation
   - no manual Product Code, no active `Item.Code`/`Item.NormalizedCode` and no second active Item Code
   - API and DB prevent Official Item Code mutation
   - non-null legacy Code values are preserved only in `catalog.item_legacy_codes`

4. Fields / Rules
   - FA required
   - EN optional
   - Category required
   - Unit required
   - Business Unit optional
   - Safety Stock >= 0
   - Critical <= Safe <= Excess
   - Notes not machine rules

5. BOM Boundary
   - Product can exist without BOM
   - no stored editable HasBOM
   - no `Ready` or `PlanningReady`
   - no premature BOM module

6. Reference Data
   - reuse Product Category / Unit / Business Unit from MRP-01
   - no duplicate reference tables
   - Create and changed Update assignments require correct type and active state
   - unchanged inactive historical references remain resolvable for unrelated edits
   - Reactivate requires active correct Category, active Unit and null or active correct Business Unit
   - failed Reactivate returns `409 / PRODUCT_REACTIVATION_BLOCKED`, identifies invalid references, changes no Version and emits no successful audit
   - historical resolution is not treated as reactivation eligibility

7. Lifecycle
   - Active/Inactive
   - no destructive historical deletion
   - historical resolution preserved

8. Duplicate Names
   - Product Names not hard-unique
   - trimmed/case-insensitive warning comparison; null/empty Name EN ignored
   - Create compares other Products and Update excludes current Item ID
   - successful save returns `DUPLICATE_NAME_FA` / `DUPLICATE_NAME_EN` where applicable
   - Product never warns against itself; no database name uniqueness

9. Database Integrity
   - globally unique, correctly formatted and DB-immutable BusinessNumber
   - legacy archive exists as migration/history only and deprecated Code columns are removed
   - Version >= 1, Safety Stock >= 0 and threshold-order constraints
   - Product ItemId 1:1 PK/FK, restrictive foreign keys and required columns
   - exact numeric precision and field lengths; no Unit DecimalPrecision
   - no Name FA/EN uniqueness and no hard delete
   - ReferenceValue subtype membership application-enforced
   - forward-only migration; original MRP-01 migrations unchanged; zero-to-latest succeeds
   - single Item Version protects all aggregate mutations atomically

10. API
   - safe use-case boundaries
   - server-side validation
   - no client override of Official Item Code/Internal ID
   - only explicit GET list/detail, POST create, PUT update and POST deactivate/reactivate endpoints
   - bounded server-side paging with maximum pageSize 100 and approved filters
   - no client control of ItemType/IsActive; no DELETE, unrestricted PATCH or generic CRUD
   - resolved reference summaries/activity state returned
   - correct errors

11. UI
   - Brand Rules
   - RTL
   - grid usability
   - filters
   - add/edit/deactivate/reactivate
   - one read-only Official Item Code; no manual Code field in Create/Edit
   - active reference selectors and inactive historical reference display
   - duplicate warning and structured error display
   - no AG Grid dependency, global shell redesign or more than minimal navigation addition
   - validation presentation
   - no unrelated UI

12. Permissions/Auditability
   - future concepts remain `catalog.product.view` / `catalog.product.manage`; no fake/parallel Identity
   - reuse `IAuditEventSink`, EntityType Product and EntityId Item.Id
   - CREATE/EDIT/DEACTIVATE/REACTIVATE emitted only after successful persistence
   - validation, attempted Official Item Code mutation, stale Version, failed Reactivate and failed persistence emit no successful mutation event
   - no Audit persistence platform

13. Performance
   - N+1
   - indexes
   - over-fetching
   - unnecessary BOM joins
   - unnecessary caching
   - premature infrastructure

14. Tests
   - verify assertions are meaningful
   - execute every identity, Official Item Code, field, reference, reactivation, duplicate-name, lifecycle, scope and regression case required by §32 and Prompt 02B
   - pay special attention to concurrent PRD Code generation, API/DB immutability, no MAX()+1 and client non-override
   - verify active-reference Reactivate success and separate inactive Category/Unit/Business Unit failures
   - verify failed Reactivate preserves Version and failed mutations emit no successful audit
   - verify duplicate-name self-exclusion and warning-only success semantics
   - verify zero-to-latest migration, all MRP-01 tests, ReferenceType count 8, isolated DB safety, Development preservation and no orphan test DBs

15. Specification Drift
   - compare Master Spec / Decisions / MRP-01 / MRP-02 / approved MRP-02A / implementation

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

# E. Final Pre-Codex Audit — MRP-02

## Result

`PASS`

## Audited Decisions

تمام ۱۲ تصمیم تأییدشده Product Master پوشش داده شده‌اند:

1. Internal ID + auto Official Item Code
2. No manual or second active Item Code
3. FA required / EN optional
4. One Product Category
5. Base Unit required
6. Safety Stock retained at Product level
7. Critical/Safe/Excess retained at Product level
8. Active/Inactive lifecycle
9. Product may exist before BOM
10. Business Unit optional
11. Duplicate Name warning, not hard uniqueness
12. Notes informational only

## Cross-Package Consistency

- MRP-02 از MRP-01 برای Product Category / Unit / Business Unit استفاده می‌کند.
- Reference Data تکراری ایجاد نمی‌شود.
- Unified Item فقط به اندازه Product شروع می‌شود.
- Part/Component عمداً به Package بعدی موکول شده‌اند.
- Product threshold به Planning Policy قطعات سرایت نمی‌کند.
- Product بدون BOM قابل ثبت است.
- Future Planning Eligibility به شکل Derived باقی می‌ماند: `Active + Valid BOM`.
- `HasBOM` دستی ایجاد نمی‌شود.
- Forecast / Inventory / PO logic وارد Product Master نشده است.

## Architecture Corrections Applied

- UUID فنی از Official Item Code به‌طور صریح جداست.
- Official Item Code generation باید concurrency-safe باشد.
- Gapless sequence requirement حذف شد.
- `Item.BusinessNumber` تنها Official Item Code و در API/DB immutable است.
- `Item.Code`/`Item.NormalizedCode` فقط پس از archival تاریخی حذف می‌شوند.
- Product Name hard uniqueness ممنوع شد.
- Product Master اجازه ایجاد رکورد بدون BOM را دارد.
- No-BOM state با boolean دستی ذخیره نمی‌شود.
- Numeric quantity precision با قواعد Decimal/Numeric MRP همسو شد.

## Prompt Audit

### MRP-02A
PASS

Analysis-only، Repository-aware، با Stop Point صریح.

### MRP-02B
PASS

Scope محدود و تست‌های Business/Concurrency مشخص.

### MRP-02C
PASS

Audit مستقل، Read-only و شامل Architecture / DB / API / UI / Tests / Scope Drift.

## Final Decision

`MRP-02 Product Master` برای Prompt Baseline پروژه تأیید شد.

طبق Workflow پروژه هنوز به Codex داده نشود تا همه Packageهای MRP طراحی شوند و Final Cross-Package Audit کل مجموعه انجام شود.
