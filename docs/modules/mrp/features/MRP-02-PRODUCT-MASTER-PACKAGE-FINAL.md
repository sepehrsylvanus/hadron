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

هر Product دو شناسه مجزا دارد.

### Internal Technical ID

- system-generated
- immutable
- machine-oriented
- not user-entered

### Product Number

Business-facing Product ID مثل:

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

---

## 4. Product Code

`Product Code` یک Business Code دستی است.

Rules:

- Required
- Unique
- User-entered
- Trimmed
- Normalized
- تغییر آن بعد از استفاده در BOM/Transactions باید در آینده Controlled باشد

Example:

```text
C1011-W
```

Product Code نباید Technical Primary Key باشد.

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
| Product Number | Auto / unique / immutable |
| Product Code | Required / unique |
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

- Product Number

User-entered:

- Product Code
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

- duplicate Product Code

Warning only:

- duplicate Product Name FA
- duplicate Product Name EN

No hard DB uniqueness on product names.

---

## 19. Product Number Generation

Requirements:

- server-side
- concurrency-safe
- unique
- immutable
- no client override
- gapless numbering not required

Codex Analysis must propose the simplest robust PostgreSQL / EF Core-compatible mechanism.

---

## 20. Product Code Change

Product Code is not technical identity.

Baseline:

- may be editable before downstream usage
- after downstream usage, change must be Controlled and auditable
- MRP-02 must not create speculative BOM/transaction tables just to enforce future usage detection

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
- Product Code change
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

Client cannot supply Internal ID or Product Number.

---

## 24. Validation

Server-side validation at minimum:

- Product Code required
- Product Code unique
- Product Name FA required
- Product Category required
- Base Unit required
- Safety Stock >= 0
- Critical Days >= 0
- Safe Start Days >= Critical Days
- Excess Start Days >= Safe Start Days
- referenced Category/Unit/Business Unit exists
- active reference value required for new assignment
- Product Number cannot be supplied/overridden

---

## 25. Precision

Quantity and planning numeric values use decimal/numeric.

Binary floating point must not be used for Business-critical values.

---

## 26. Performance

Support:

- indexed Product Code
- indexed Product Number
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
3. Product Number is auto-generated, unique and immutable.
4. Product Code is required and unique.
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
20. Product Number concurrency/uniqueness tested.
21. Product Code uniqueness tested.
22. No future MRP features prematurely implemented.

---

## 30. Locked Decisions

1. Unified Item starts here only to the extent Product needs it.
2. Internal Technical ID separate from Product Number.
3. Product Number auto-generated, unique, immutable, non-user-entered.
4. Product Code user-entered, required and unique.
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

Codex Analysis may raise technical questions about:

- Product Number sequence mechanism
- Product Code normalization
- duplicate-name warning UX
- optimistic concurrency

but cannot change locked Business decisions.

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
4. Internal ID / Product Number / Product Code separation
5. Concurrency-safe Product Number generation
6. Product Code normalization, uniqueness and future controlled rename boundary
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
- Product Number uniqueness
- concurrent Product creation
- Product Code uniqueness
- Product Code normalization
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

Required:

UNIFIED ITEM
- implement only minimal approved Unified Item foundation needed for Product
- do not implement Part/Component features

IDENTITY
- internal system ID
- server-generated Product Number
- user-entered Product Code

Product Number:
- unique
- immutable
- server-generated
- concurrency-safe
- not user-overridable
- gapless not required

FIELDS
- Product Code
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
- Product Code required/unique
- FA name required
- EN optional
- active Category required for new assignment
- active Unit required
- Business Unit optional
- Safety Stock >= 0
- Critical >= 0
- Safe >= Critical
- Excess >= Safe
- client cannot set Product Number or Internal ID

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
- implement only approved lightweight warning from MRP-02A

REFERENCE DATA
Reuse from MRP-01:
- Product Category
- Unit
- Business Unit

Do not duplicate reference data.

BACKEND
Implement only approved:
- domain/data model
- EF Core mapping
- migrations
- application use cases/services
- API
- validation
- error handling

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

Follow UI-BRAND-RULES.md.
Reuse existing grid/form patterns.

PERMISSIONS/AUDIT
Reuse existing platform capabilities.
Do not build parallel Identity/Audit systems.

TESTS
At minimum:
- unique Product Number
- concurrent creation does not duplicate Product Number
- client cannot set/change Product Number
- Product Code uniqueness/normalization
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

VERIFICATION
Run applicable:
- backend restore/build/tests
- migrations
- frontend install if required
- frontend build
- frontend lint
- startup/health checks

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
   - minimal
   - no overbuilt future Part/Component behavior

3. Identity
   - Internal ID system-controlled
   - Product Number distinct from Product Code
   - Product Number auto-generated/immutable/concurrency-safe
   - Product Code unique/normalized
   - Product Code not technical primary identity

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
   - no premature BOM module

6. Reference Data
   - reuse Product Category / Unit / Business Unit from MRP-01
   - no duplicate reference tables
   - correct active/inactive assignment behavior

7. Lifecycle
   - Active/Inactive
   - no destructive historical deletion
   - historical resolution preserved

8. Duplicate Names
   - Product Names not hard-unique
   - warning behavior matches approved design

9. Database Integrity
   - constraints
   - indexes
   - foreign keys
   - numeric precision
   - migration safety
   - concurrency behavior

10. API
   - safe use-case boundaries
   - server-side validation
   - no client override of Product Number/Internal ID
   - correct errors
   - no dangerous generic CRUD

11. UI
   - Brand Rules
   - RTL
   - grid usability
   - filters
   - add/edit/deactivate/reactivate
   - validation presentation
   - no unrelated UI

12. Permissions/Auditability

13. Performance
   - N+1
   - indexes
   - over-fetching
   - unnecessary BOM joins
   - unnecessary caching
   - premature infrastructure

14. Tests
   - verify assertions are meaningful
   - pay special attention to concurrent Product Number generation

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

1. Internal ID + auto Product Number
2. Product Code required + unique
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

- Product Number از Product Code به‌طور صریح جدا شد.
- Product Number generation باید concurrency-safe باشد.
- Gapless sequence requirement حذف شد.
- Product Code technical PK نیست تا Controlled Rename آینده ممکن بماند.
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
