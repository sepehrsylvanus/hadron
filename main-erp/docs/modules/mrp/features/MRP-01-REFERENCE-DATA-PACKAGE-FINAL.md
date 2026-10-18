# Hadron ERP — MRP Package 01 Final

**Package:** MRP-01 Reference Data Foundation  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 01-REFERENCE-DATA

## 1. Purpose

این Feature زیرساخت داده‌های مرجع و Controlled Values موردنیاز MRP و در موارد مناسب کل ERP را ایجاد می‌کند.

هدف:

- جلوگیری از نام‌گذاری‌های متفاوت برای یک مفهوم
- جلوگیری از Duplicate Data
- حذف Free-text غیرقابل کنترل
- حذف Dropdownهای Hardcoded
- جلوگیری از تکرار Listها در Moduleهای مختلف
- جلوگیری از وابستگی Business Logic به Display Text

Google Sheet قدیمی یک Sheet با نام `List` داشت. در ERP قرار نیست یک جدول بزرگ مشابه Sheet ایجاد شود. Reference Data باید استاندارد، قابل توسعه و دارای مرز مالکیت روشن باشد.

---

## 2. Classification

### Shared ERP Reference Data

- Country
- Currency
- Unit
- Business Unit

### Configurable MRP / Procurement Reference Data

- Product Category
- Part Category
- Component Category
- Payment Term
- Shipping Mode
- Incoterm

### System-Controlled Values

مواردی مثل:

- Purchase Order Status
- Active Status
- Expert Decision
- Workflow States

باید Technical Code ثابت داشته باشند و از Reference Data قابل تغییر توسط کاربر جدا باشند.

Display Label می‌تواند فارسی/انگلیسی باشد، اما Technical Meaning نباید قابل حذف یا تغییر آزاد باشد.

### Approved v1 Data Model

MRP-01 uses a hybrid model:

- `ReferenceType`
- `ReferenceValue`
- dedicated `Currency`
- dedicated `Unit`

`ReferenceType` definitions are system-defined and allowlisted in v1. Users cannot create, delete or redefine arbitrary ReferenceTypes. The generic ReferenceValue structure contains only approved common fields and must not become EAV or a dynamic schema mechanism.

---

## 3. Values Removed From Legacy List

موارد زیر Reference List نیستند:

- Product Name
- Part Name
- Component Name
- Supplier Name
- Product Filter
- Supplier Filter
- Owner Filter
- Component Count
- Contact Person
- Hadron Owner

دلایل:

- Product / Part / Component / Supplier مستقیماً از Master Data خوانده می‌شوند.
- Filterها Query هستند، نه Stored List.
- Component Count از تعداد واقعی BOM Lines محاسبه می‌شود.
- Contact Person و Hadron Owner Entity هستند، نه String List.

---

## 4. Reference Value Structure

هر Configurable Reference Value حداقل:

- Stable Technical ID
- Code
- Name FA
- Name EN
- Active Status
- Sort Order
- Version (`bigint`)
- Created At
- Updated At

داشته باشد.

Technical Code:

- Unique
- Trimmed
- Case-normalized
- Stable
- assigned at creation and immutable through ordinary Edit

باشد.

Business Logic نباید به Name FA یا Name EN وابسته باشد.

Ordinary API/UI operations must not expose Code rename. Any future exceptional Code migration requires a separately approved administrative mechanism.

---

## 5. Localization

هر Value حداقل:

- Name FA
- Name EN

دارد.

Persian UI → Name FA  
English UI → Name EN  
Business Logic → Technical Code

---

## 6. Lifecycle

کاربر مجاز بتواند:

- Add
- Edit
- Deactivate
- Reactivate

کند.

Hard Delete برای Value استفاده‌شده ممنوع است.

Inactive Value:

- در انتخاب جدید به‌صورت پیش‌فرض نمایش داده نمی‌شود.
- در Historical Data قابل Resolve باقی می‌ماند.

Baseline این Feature بر Deactivate تکیه دارد، نه Delete.

---

## 7. Duplicate Prevention

Duplicate Code must be blocked by server-side validation and a database uniqueness constraint.

`Name FA` and `Name EN` are display/search values, not identity. They may be normalized for search and may produce a non-blocking duplicate warning, but matching localized names must not be blocked solely for matching and must not have database uniqueness constraints.

### Approved Optimistic Concurrency

Every mutable Reference Data entity uses explicit `Version : bigint` stale-write protection:

- reads return Version;
- update, deactivate and reactivate submit expected Version;
- successful mutation atomically advances Version;
- stale Version returns structured HTTP `409 Conflict`;
- silent lost updates are prohibited.

---

## 8. Country

Shared ERP Reference.

Baseline:

- Code
- Name FA
- Name EN
- Active

در صورت امکان از Code استاندارد کشور استفاده شود.

---

## 9. Currency

Shared ERP Reference.

Baseline:

- ISO Code
- Name FA
- Name EN
- Symbol
- Decimal Precision
- Active
- Version (`bigint`)
- timestamps

Baseline expected:

- IRR
- USD
- CNY
- AED
- EUR

Currency جدید قابل اضافه‌شدن باشد.

Exchange Rate در Scope این Feature نیست.

---

## 10. Unit

Shared ERP Reference.

Baseline:

- Code
- Name FA
- Name EN
- nullable Symbol where actually required
- Active
- Sort Order
- Version (`bigint`)
- timestamps

Examples:

- PCS
- KG
- G
- M
- MM
- BOX
- CARTON

Unit Conversion در Scope نیست.

Unit `DecimalPrecision` is not part of MRP-01 v1 unless a future approved requirement explicitly adds it. Do not invent Unit metadata.

---

## 11. Business Unit

Shared ERP Reference.

فعلاً ساختار ساده Reference Data کافی است.

Organization Hierarchy کامل ساخته نشود.

---

## 12. Categories

- Product Category
- Part Category
- Component Category

در نسخه اول Flat باشند مگر Requirement واقعی برای Hierarchy ایجاد شود.

---

## 13. Payment Term

Payment Term فقط نوع/نام شرایط پرداخت را نگه دارد.

Examples:

- Advance
- Split Payment
- Credit

درصدهای واقعی:

- Advance %
- Before Shipment %
- Credit %
- Credit Days

بعداً در Supplier / Sourcing / PO نگهداری می‌شوند.

---

## 14. Shipping Mode

Examples:

- Sea
- Air
- Road
- Internal

Shipping Mode و Incoterm دو مفهوم مستقل هستند.

---

## 15. Incoterm

Examples:

- EXW
- FOB
- CFR
- CIF

در یک List یا Field با Shipping Mode ترکیب نشود.

---

## 16. System-Controlled Statuses

System Statusها Technical Code ثابت دارند.

Future PO examples:

- Draft
- Approved
- Ordered
- Shipped
- Customs
- Partially Received
- Received
- Cancelled

این Feature PO Workflow را نمی‌سازد.

فقط باید System-controlled state از صفحه مدیریت Reference Data قابل حذف/تعریف مجدد نباشد.

---

## 17. Contact Person

Approved direction: **Option B**

Contact Person یک Lightweight Person / Contact Entity در زمان نیاز خواهد بود و بعداً به Party / Contact Module متصل می‌شود.

Contact Person خارج از implementation MRP-01 است. Text List، temporary string field یا premature entity برای آن ساخته نشود.

---

## 18. Hadron Owner

Approved direction: **Option B**

Hadron Owner یک Lightweight Internal Person / Owner Entity در زمان نیاز خواهد بود و بعداً به ERP User / Employee / Organization متصل می‌شود.

Hadron Owner خارج از implementation MRP-01 است. Text List، temporary string field یا premature entity برای آن ساخته نشود.

---

## 19. Management UI

یک UI مدیریتی یکپارچه برای Reference Data.

قابلیت‌ها:

- Search
- Filter Active / Inactive
- Add
- Edit
- Deactivate
- Reactivate
- Sort/order where applicable

UI باید از:

`docs/brand/UI-BRAND-RULES.md`

پیروی کند.

برای هر Reference Type صفحه تکراری مستقل ساخته نشود مگر نیاز واقعی.

Baseline UI یک صفحه minimal، Persian-first و RTL است. AG Grid برای این management page کوچک الزامی نیست.

---

## 20. Permissions

بین:

- View Reference Data
- Manage Reference Data

تفکیک وجود داشته باشد.

اگر Identity/Permission هنوز کامل نیست، Codex نباید Authentication System مستقل فقط برای این Feature بسازد.

---

## 21. API

Frontend مقادیر فعال را از Backend دریافت کند.

Use Cases لازم:

- Read
- Add
- Edit
- Deactivate
- Reactivate

Generic unrestricted CRUD معماری اصلی نباشد.

---

## 22. Performance

Reference Data حجم کم ولی Read frequency بالا دارد.

نیازها:

- Query ساده
- Index مناسب
- عدم N+1
- Payload کوچک

Caching فقط در صورت نیاز واقعی.

Redis ممنوع مگر بعداً با دلیل اندازه‌گیری‌شده تصویب شود.

---

## 23. Audit

تغییرات مهم قابل Audit باشند:

- Create
- Rename
- Deactivate
- Reactivate

Ordinary Code rename در v1 وجود ندارد. Exceptional future Code migration فقط با mechanism اداری جداگانه و approval صریح مجاز خواهد بود.

اگر Audit Platform هنوز وجود ندارد، Integration Point حفظ شود و زیرساخت موازی ساخته نشود.

---

## 24. Seed Data

Baseline seed فقط شامل deterministic و approved `ReferenceType` definitions موردنیاز MRP-01 است.

تا زمانی که business values کامل تصویب نشده‌اند، Currency، Unit، Country، Business Unit، Category، Payment Term، Shipping Mode یا Incoterm seed نشوند. Codex نباید code، translation، symbol، decimal precision یا metadata مفقود را اختراع کند. Future PO/workflow records نیز صرفاً به‌دلیل وجود system-controlled codes زودهنگام seed نشوند.

Seed باید:

- Idempotent
- Version-controlled
- Testable

باشد.

---

## 25. Explicit Non-Scope

این Feature نسازد:

- Product Master
- Supplier Master
- Part Master
- Component Master
- BOM
- Forecast
- Inventory
- Purchase Orders
- Exchange Rates
- Unit Conversion
- Full Contact Management
- HR
- Full Identity
- Workflow Engine

---

## 26. Acceptance Criteria

1. Reference Data استاندارد در Database ذخیره شود.
2. Technical Code از Display Label جدا باشد.
3. FA / EN پشتیبانی شود.
4. Add / Edit / Deactivate / Reactivate کار کند.
5. Hard Delete رکورد استفاده‌شده وجود نداشته باشد.
6. Duplicate Code جلوگیری شود.
7. Currency ساختار مناسب داشته باشد.
8. Unit ساختار مناسب داشته باشد.
9. Dynamic Master Data به List تکراری تبدیل نشود.
10. System Statusها با Configurable Lists مخلوط نشوند.
11. UI مطابق Brand Rules باشد.
12. Backend Validation وجود داشته باشد.
13. Lifecycle/validation tests پاس شوند.
14. Feature بعدی Premature ساخته نشود.
15. Contact Person / Hadron Owner به Text List تقلیل داده نشوند.
16. Shipping Mode و Incoterm مستقل بمانند.
17. Hybrid model شامل allowlisted ReferenceType/ReferenceValue و dedicated Currency/Unit باشد.
18. Code در ordinary Edit immutable باشد.
19. Localized duplicate names با database uniqueness یا hard block ممنوع نشوند.
20. Version bigint از stale write و lost update جلوگیری کند.
21. Seed فقط approved ReferenceType definitions باشد و business rows اختراع نشوند.

---

## 27. Locked Decisions

- Reference Data مدیریت‌پذیر است.
- Add / Edit / Deactivate / Reactivate تأیید شده.
- Hard Delete برای Value استفاده‌شده ممنوع.
- System Statusها User-editable List نیستند.
- Reference Value دارای Code + FA + EN است.
- Currency Shared ERP Reference است.
- Unit Shared ERP Reference است.
- Component Count حذف می‌شود.
- Filter Lists حذف می‌شوند.
- Product / Part / Component / Supplier Names از Master Data خوانده می‌شوند.
- Contact Person Entity است، نه List.
- Hadron Owner Entity است، نه List.
- Contact Person / Hadron Owner از Option B پیروی می‌کنند.
- Shipping Mode و Incoterm مستقل هستند.
- Business Logic به Display Text وابسته نمی‌شود.
- v1 model hybrid است: allowlisted ReferenceType/ReferenceValue + dedicated Currency/Unit.
- ReferenceType user-created نیست و dynamic schema/EAV ایجاد نمی‌کند.
- Unit v1 فاقد DecimalPrecision و Unit Conversion است.
- Code برای هشت `ReferenceValue` allowlisted هنگام Save در backend، با شمارنده مستقل هر `ReferenceType` و فرمت عددی حداقل سه‌رقمی (`001`, `002`, ...) تولید می‌شود؛ در Create input از کاربر دریافت نمی‌شود و در ordinary Edit immutable است. sequence/counter باید concurrency-safe باشد، با بیشترین کد عددی موجود به‌صورت forward-only همگام شود و هیچ کد موجودی را تغییر ندهد. `Currency.ISO Code` و `Unit.Code` به‌دلیل معنای استاندارد/عملیاتی خود همچنان ورودی صریح و immutable هستند.
- Localized duplicate names identity نیستند و database uniqueness ندارند.
- mutable Reference Data از Version bigint و structured 409 برای optimistic concurrency استفاده می‌کند.
- seed baseline فقط approved deterministic ReferenceType definitions است.
- MRP-01 برای Contact Person/Hadron Owner entity یا temporary string field نمی‌سازد.

---

## 28. Open Questions

Business-critical Open Question برای MRP-01 وجود ندارد.

---

# B. Prompt 01A — Analysis

```text
# MRP-01A — Reference Data Foundation Analysis

Read and follow:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md

Review the official Brand PDF only if clarification of a visual rule is required.

TASK:
Analyze the approved MRP Reference Data Foundation.

This is ANALYSIS ONLY.

Do not modify:
- source code
- database
- migrations
- configuration
- documentation
- repository structure

Do not install packages.
Do not generate implementation code.

Required analysis:

1. Understanding
2. Existing Repository/Foundation
3. Proposed Domain/Data Model
   - configurable reference types/values
   - Currency
   - Unit
   - Business Unit
   - system-controlled status codes
   Compare generic vs dedicated vs hybrid.
   Do not use EAV.
4. Shared vs MRP ownership
5. Localization: Code / Name FA / Name EN
6. Lifecycle: Add/Edit/Deactivate/Reactivate/duplicate prevention/stable codes/no destructive delete
7. System-Controlled Values and localized labels
8. Minimal API design; avoid unrestricted generic CRUD
9. One simple Reference Data Management UI
10. Person / Owner boundary
    Approved direction = Option B
11. Seed strategy
12. Database constraints/indexes
13. Performance and whether caching is actually needed
14. Audit / Permission integration
15. Testing plan
16. Risks / contradictions across AGENTS, Master Spec, Decisions, Feature Spec and Repository
17. Smallest implementation plan
18. Explicit non-scope

Output:
1. Understanding
2. Repository Findings
3. Proposed Data Model
4. Ownership Boundaries
5. API Design
6. UI Design
7. Validation / Constraints
8. Seed Strategy
9. Permissions / Audit
10. Performance
11. Tests
12. Risks / Contradictions
13. Implementation Plan
14. Approval Required

Then STOP.
Do not implement anything until explicit approval.
```

---

# C. Prompt 01B — Implementation

```text
# MRP-01B — Implement Reference Data Foundation

Read and follow:

- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-01A analysis if saved in Repository

TASK:
Implement only the approved Reference Data Foundation.

If MRP-01A contains unresolved material contradictions, STOP.

Implement approved architecture for:

Data model:
- hybrid `ReferenceType` / `ReferenceValue` + dedicated `Currency` / `Unit`
- ReferenceType definitions are system-defined/allowlisted; no user create/delete/redefine API
- no EAV or dynamic schema behavior

Shared ERP:
- Country
- Currency
- Unit
- Business Unit

Configurable MRP/Procurement:
- Product Category
- Part Category
- Component Category
- Payment Term
- Shipping Mode
- Incoterm

System-Controlled:
- stable machine codes + localized labels
- do not implement Purchase Order Workflow

Reference values where applicable:
- stable Technical ID
- Code
- Name FA
- Name EN
- Active Status
- Sort Order
- Version bigint
- timestamps

Lifecycle:
- Create
- Edit allowed fields
- Deactivate
- Reactivate
- no destructive delete for used values
- allowlisted generic ReferenceValue Code is generated server-side on Save from an independent per-ReferenceType concurrency-safe sequence, formatted with at least three digits, absent from the Create contract, displayed as disabled/automatic in the UI, and immutable afterward; existing Codes are preserved
- Currency ISO Code and Unit Code remain explicit standard/operational inputs at Create and immutable in ordinary Edit; no normal Code rename API/UI
- mutation requires expected Version, atomically increments Version, and stale Version returns structured HTTP 409 Conflict

Currency:
- ISO Code
- Name FA
- Name EN
- Symbol
- Decimal Precision
- Active
- Version bigint
- no Exchange Rates

Unit:
- Code
- Name FA
- Name EN
- nullable Symbol where actually required
- Active
- Sort Order
- Version bigint
- no Unit Conversion
- no Unit DecimalPrecision in v1

Do NOT create lists for:
- Product Name
- Part Name
- Component Name
- Supplier Name
- Product Filter
- Supplier Filter
- Owner Filter
- Component Count

Person/Owner:
- do not create free-text lists, temporary string fields or premature entities
- approved direction is Option B
- Contact Person and Hadron Owner remain outside MRP-01 implementation
- no HR / full Identity / full Contact Management

Backend:
- models/entities
- EF configuration
- migrations
- application use cases/services
- API
- validation
- error handling
- reuse existing HadronDbContext and integration-test project

Do not introduce speculative:
- Generic Repository framework
- unnecessary Unit of Work abstraction
- CQRS framework
- event bus
- Redis
- unnecessary new projects
- unrelated infrastructure

Frontend:
- minimal Reference Data Management UI
- RTL-ready
- Persian-first where appropriate
- Name FA / Name EN
- Search
- Reference Type selection where appropriate
- Active/Inactive filter
- Add/Edit/Deactivate/Reactivate
- follow UI-BRAND-RULES.md
- one minimal Persian-first RTL screen; AG Grid is not required
- no Dashboard/unrelated modules

Seed:
- only approved deterministic ReferenceType definitions required by MRP-01
- idempotent
- version-controlled
- testable
- do not seed Currency, Unit, Country, Business Unit, Category, Payment Term, Shipping Mode or Incoterm rows without complete approved values
- do not invent codes, translations, symbols, decimal precision or other business metadata
- do not prematurely seed future PO/workflow business records

Tests at minimum:
- duplicate codes rejected
- Code immutable through ordinary Edit
- duplicate Name FA / Name EN is not blocked by database uniqueness or solely because names match
- required labels validated
- Version returned by reads and required by mutations
- successful mutation increments Version
- stale update/deactivate/reactivate returns structured 409 Conflict with no lost update
- active/inactive behavior
- inactive values excluded from normal new-selection queries
- historical lookup resolves inactive values
- invalid currency precision rejected
- duplicate Currency ISO Code rejected
- duplicate Unit Code rejected
- activate/reactivate works
- no hard delete
- seed contains only approved ReferenceType definitions and no speculative business rows
- authorization behavior only if Authorization already exists

Verification:
- backend restore/build/tests
- development migrations
- frontend install if needed
- frontend build/lint
- startup/health checks

Scope guard:
Do NOT implement Product, Supplier, Part, Component, Sourcing, BOM, Forecast, Inventory, Purchase Orders, Exchange Rates, Unit Conversion, HR, full Contact Management, full Identity, Workflow.

Final report:
- Changed
- Database
- API
- UI
- Seeded
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-02.
```

---

# D. Prompt 01C — Independent Audit

```text
# MRP-01C — Reference Data Foundation Independent Audit

READ-ONLY audit.

Read:
- AGENTS.md
- docs/ARCHITECTURE.md
- docs/modules/mrp/MRP-BUSINESS-SCENARIO.md
- docs/modules/mrp/MRP-SYSTEM-SPEC.md
- docs/modules/mrp/DECISIONS.md
- docs/modules/mrp/OPEN-QUESTIONS.md
- docs/modules/mrp/features/MRP-01-REFERENCE-DATA-PACKAGE-FINAL.md
- docs/brand/UI-BRAND-RULES.md
- approved MRP-01A analysis
- MRP-01B implementation report

Inspect all MRP-01 implementation files.

Do NOT modify code, migrations, database, docs, tests or configuration.
Do not auto-fix.

Audit:

1. Scope: no premature Product/Supplier/Part/Component/BOM/Forecast/Inventory/PO modules
2. Data Model:
   - approved hybrid ReferenceType/ReferenceValue + dedicated Currency/Unit
   - ReferenceType fixed/allowlisted with no user create/delete/redefine path
   - normalization
   - no EAV misuse
   - stable IDs
   - Code separated from FA/EN labels
   - Currency/Unit structure
   - Shared vs MRP ownership
   - no duplicate persisted dynamic lists
3. Database Integrity:
   - Code uniqueness constraints
   - no database uniqueness constraint on Name FA / Name EN
   - indexes
   - required fields
   - foreign keys
   - active/inactive behavior
   - migration safety
   - Version bigint optimistic concurrency on every mutable Reference Data entity
4. Business Rules:
   - Add/Edit/Deactivate/Reactivate
   - no destructive removal of used values
   - Technical Code protection
   - generic ReferenceValue Code generated server-side on Save, independently per allowlisted ReferenceType, absent from client Create input and immutable through ordinary Edit/API/UI
   - sequence initialization preserves every existing Code and advances beyond the maximum existing numeric Code without `MAX()+1` runtime generation
   - Currency ISO Code and Unit Code remain explicit Create inputs and immutable afterward
   - system-controlled statuses protected
   - Shipping Mode != Incoterm
   - no Component Count list
   - no persisted Filter Lists
   - Contact Person/Hadron Owner not plain text lists, temporary string fields or premature entities
5. Localization:
   - Name FA
   - Name EN
   - stable Code
   - no Business Logic dependency on translated labels
6. Currency:
   - ISO uniqueness
   - precision validation
   - active state
   - no Exchange Rate
7. Unit:
   - correct structure
   - no DecimalPrecision in v1
   - no Unit Conversion
8. API:
   - use-case boundaries
   - validation
   - error behavior
   - reads return Version; mutations require expected Version and advance it
   - stale update/deactivate/reactivate returns structured HTTP 409 with no lost update
   - no dangerous unrestricted CRUD
   - inactive selection behavior
9. Frontend:
   - RTL
   - brand consistency
   - management usability
   - active/inactive handling
   - FA/EN editing
   - no ordinary Code rename
   - one minimal Persian-first RTL screen; no unnecessary AG Grid dependency
   - no unrelated UI
10. Security/Authorization:
   - consistent with current platform maturity
   - do not call missing full Identity a defect if intentionally out of scope
11. Auditability
    - no parallel Audit platform
12. Performance:
   - N+1
   - unnecessary queries
   - over-fetching
   - unnecessary caching
   - premature infrastructure
13. Tests:
    - verify assertions actually cover Acceptance Criteria
    - duplicate Code blocked
    - duplicate localized names allowed and not constrained as identity
    - Code immutability
    - Version concurrency and stale-write 409
    - deactivate/reactivate and inactive historical resolution
    - no hard delete
    - seed only approved ReferenceType definitions; no speculative business values
    - existing HadronDbContext and integration-test project reused
14. Regression:
   - run appropriate tests/builds if possible
15. Specification Drift:
   - compare Master Spec / Decisions / Feature Spec / implementation

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
STOP after audit.
```

---

# E. Final Pre-Codex Audit

## Result

`PASS`

## Audited Decisions

تمام ۸ تصمیم Package 01 پوشش داده شده‌اند:

1. Reference Data management → تأیید
2. System Status separation → تأیید
3. FA / EN + Stable Code → تأیید
4. Contact Person / Hadron Owner → Option B
5. Currency → Shared ERP Reference
6. Unit → Shared ERP Reference
7. Component Count → حذف
8. Filter Lists → حذف

## Corrections Applied

در Final Audit این اصلاحات اعمال شد:

- Deactivate به‌عنوان Baseline lifecycle صریح‌تر شد.
- Option B برای Contact Person / Hadron Owner در Spec و Promptها صریح شد.
- وابستگی Business Logic به Display Text ممنوع شد.
- Shared ERP و MRP/Procurement ownership روشن‌تر شد.
- Audit Prompt موظف شد Person/Owner را از نظر تبدیل اشتباه به Text List بررسی کند.
- Implementation Prompt موظف شد اگر Analysis دارای Conflict حل‌نشده بود STOP کند.
- Exchange Rate، Unit Conversion، HR، full Identity، Workflow و Featureهای بعدی صریحاً Out of Scope باقی ماندند.
- Generic vs Dedicated vs Hybrid Reference Data فقط در Analysis قابل مقایسه است؛ EAV مجاز نیست.

## Dependency Audit

هیچ Dependency Conflict برای شروع MRP-01 دیده نشد.

MRP-01 می‌تواند قبل از:

- Product Master
- Supplier Master
- Part / Component Master
- Sourcing

طراحی شود.

## Prompt Audit

### MRP-01A
PASS

تحلیل جدا از Implementation است و Stop Point دارد.

### MRP-01B
PASS

Scope محدود، Testها مشخص و Scope Guard صریح است.

### MRP-01C
PASS

Audit مستقل و Read-only است و Specification Drift را بررسی می‌کند.

## Final Decision

`MRP-01 Reference Data Foundation` برای قرار گرفتن در Baseline نهایی Promptهای MRP آماده است.

طبق تصمیم پروژه، فعلاً به Codex داده نشود تا Packageهای باقی‌مانده نیز طراحی و Final Audit کل مجموعه انجام شود.
