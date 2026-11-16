# Hadron ERP — MRP Package 03 Final

**Package:** MRP-03 Supplier Master  
**Status:** Final / Pre-Codex Approved  
**Audit Result:** PASS

---

# A. Final Feature Specification — 03-SUPPLIER-MASTER

## 1. Purpose

این Feature اطلاعات پایه تأمین‌کنندگان مورد استفاده در MRP و Procurement را نگهداری می‌کند.

Supplier Master باید:

- هویت Supplier
- کشور
- Contactها
- مسئول پیگیری هادرون
- Default Payment Profile
- Expert Evaluation Scores
- Active / Inactive lifecycle
- Notes

را مدیریت کند.

این Feature نباید اطلاعاتی را که به رابطه خاص `Supplier + Item` تعلق دارند داخل Supplier Master قرار دهد.

مواردی مانند:

- Item Price
- Currency
- MOQ
- Item-specific Lead Time
- Shipping Mode
- Incoterm
- Forwarder Days
- Shipping Days
- Customs Days

به Package مستقل Sourcing تعلق دارند.

---

## 2. Supplier Identity

هر Supplier دو شناسه مجزا دارد.

### 2.1 Internal Technical ID

- system-generated
- immutable
- machine-oriented
- not user-entered

### 2.2 Supplier Number

Business-facing Supplier ID مانند:

```text
SUP-0001
SUP-0002
SUP-0003
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

## 3. Supplier Name

`Supplier Name`

Rules:

- Required
- user-entered
- trimmed
- not a technical key
- not hard-unique in the database

Duplicate or very similar Supplier Names may create a warning, but Name similarity alone must not block save.

The Internal ID and Supplier Number remain the reliable identifiers.

---

## 4. Country

Every Supplier must have a Country.

Rules:

- Required
- selected from Shared ERP Country reference data
- active Country required for new assignment
- historical inactive Country remains resolvable

Country must not be stored as free text.

---

## 5. Supplier Contacts

### LOCKED DECISION

A Supplier may have multiple Contacts.

Conceptually:

```text
Supplier
├── Contact A
├── Contact B  ← Primary
└── Contact C
```

At most one active Contact should be designated as Primary at a time.

Contact Person must NOT be stored as a simple text dropdown list.

The Contact model in this Package must be lightweight and limited to Supplier needs.

MRP-03 must NOT build a full CRM or Party Management platform.

The approved minimal Contact fields are:

- `FullName` required
- `JobTitle` optional
- `Email` optional
- `Phone` optional
- `Mobile` optional

No contact method is required. A Supplier may have zero or many Contacts.

The design must remain future-compatible with a broader Contact / Party module.

---

## 6. Primary Contact

Primary Contact is the default operational contact for the Supplier.

Rules:

- Optional unless later business rules require otherwise
- must belong to the same Supplier
- only one active Primary Contact per Supplier
- changing Primary Contact must not delete historical Contacts
- inactive Contact cannot remain Primary
- deactivating a Primary Contact clears `IsPrimary`
- a partial unique database index enforces at most one active Primary Contact per Supplier
- Contacts are never hard-deleted

---

## 7. Hadron Owner

Every active Supplier must have one primary Hadron Owner.

### LOCKED DECISION

Hadron Owner is an Entity, not a String List.

Approved direction:

`Option B`

Use the lightweight `organization.internal_people` Owner master.

Rules:

- required for Active Supplier
- optional while Supplier is inactive if implementation needs to support incomplete preparation
- independently managed through a minimal API/UI because Supplier Owner requires a real source of truth
- do not build HR, Employee, Department, payroll or Identity in this Package

---

## 8. Default Payment Profile

Supplier Master stores the Supplier's current **Default Payment Profile**.

This is a default commercial condition, not an immutable rule.

Future Purchase Orders will:

```text
Supplier Default Payment Profile
        ↓
PO Snapshot
```

A future PO may use the default or an explicitly approved override.

Changing Supplier defaults later must NOT change historical PO snapshots.

MRP-03 does not implement PO Snapshotting; it only establishes the source default.

---

## 9. Payment Term

`Payment Term`

Rules:

- selected from approved Payment Term reference data
- required for an operational/active Supplier
- display label may be FA/EN
- business logic must use stable technical code / ID

This field describes the general payment model.

Examples may include:

- Advance
- Split Payment
- Credit

Exact business percentages are stored separately.

---

## 10. Payment Percentages

Fields:

- Advance %
- Before Shipment %
- Credit %

Rules:

```text
0 <= Advance % <= 100
0 <= Before Shipment % <= 100
0 <= Credit % <= 100
```

For a complete active Supplier payment profile:

```text
Advance %
+
Before Shipment %
+
Credit %
=
100%
```

Use decimal/numeric arithmetic.

Do not use binary floating point.

---

## 11. Credit Days

Rules:

If:

```text
Credit % > 0
```

then:

```text
Credit Days > 0
```

and Credit Days is required.

If:

```text
Credit % = 0
```

then Credit Days and Credit Anchor Event are canonicalized to `null`.

The system must not allow contradictory combinations.

### Credit Anchor Event — LOCKED CROSS-PACKAGE AMENDMENT

Supplier Default Payment Profile also owns `Credit Anchor Event` with stable codes:

```text
ORDERED
SHIPPED
WAREHOUSE_RECEIPT
```

If `Credit % > 0`, `Credit Days > 0` and Credit Anchor Event are both required. If `Credit % = 0`, Credit Days and Credit Anchor Event must be normalized to `null`. Future PO snapshots preserve this value; later Supplier-default changes must not rewrite history.

`WAREHOUSE_RECEIPT` is an approved anchor event and uses the operational `Final Receipt Date` owned by MRP-13. First Receipt and Cancellation Date are not financial anchors; MRP-03 owns only the default event code and does not derive Receipt facts.

---

## 12. Expert Lead Time Score

Supplier Master may contain:

`Expert Lead Time Score`

Rules:

- Optional
- integer
- range 1–10
- human/expert evaluation

This is NOT the operational Supplier Lead Time used by MRP.

Actual Item/Supplier lead time belongs to Sourcing.

Future analytics may calculate a separate System Performance Score.

System Score and Expert Score must never overwrite each other.

---

## 13. Expert Quality Score

Supplier Master may contain:

`Expert Quality Score`

Rules:

- Optional
- integer
- range 1–10
- human/expert evaluation

Future system-generated quality metrics must remain separate.

---

## 14. Future System Performance

MRP-03 does NOT calculate system-generated Supplier performance scores.

Future analytics may calculate metrics such as:

- On-Time Delivery %
- Average Delay
- Median Delay
- Lead Time Variability
- Sample Count
- Quality Failure Rate

These metrics must be based on Actual historical data.

They must remain separate from Expert Scores.

---

## 15. Supplier Without Item

A Supplier may exist even if no Product, Part or Component is currently sourced from it.

Supplier creation must NOT require:

- Item
- Sourcing record
- Price
- MOQ
- Purchase Order

This allows Supplier onboarding before commercial linkage is complete.

---

## 16. Active / Inactive Lifecycle

Supplier lifecycle:

- Active
- Inactive

Rules:

- creation always produces an Inactive Supplier
- creation requires an active Country
- baseline lifecycle uses deactivate/reactivate
- Supplier used historically must not be hard deleted
- inactive Supplier is excluded by default from new Sourcing/PO selections
- historical records continue resolving the Supplier
- Supplier may be prepared while inactive and activated when required information is complete

---

## 17. Supplier Activation Requirements

At minimum an Active Supplier must have:

- Supplier Name
- active Country
- active Internal Person Hadron Owner
- active Default Payment Term
- valid Payment Percentages totaling exactly 100.00%
- valid Credit Days rule

Contact is not mandatory for activation unless later approved.

Codex Analysis may propose validation implementation details but may not weaken these locked activation requirements.

---

## 18. Notes

Notes:

- Optional
- human-readable explanation only
- must not contain executable business rules
- must not be parsed by calculation logic

---

## 19. Approved Supplier Fields

| Field | Rule |
|---|---|
| Internal ID | System / immutable |
| Supplier Number | Auto / unique / immutable |
| Supplier Name | Required |
| Country | Required |
| Contacts | 0..many |
| Primary Contact | Optional / max one active |
| Hadron Owner | Required when Active |
| Payment Term | Required when Active |
| Advance % | 0..100 |
| Before Shipment % | 0..100 |
| Credit % | 0..100 |
| Credit Days | Required if Credit % > 0 |
| Expert Lead Time Score | Optional / 1..10 |
| Expert Quality Score | Optional / 1..10 |
| Active Status | Active / Inactive |
| Notes | Optional |
| Created At | System |
| Updated At | System |

---

## 20. Supplier-Level Data vs Sourcing Data

### LOCKED BOUNDARY

Supplier Master owns:

- identity
- Country
- Contacts
- Hadron Owner
- default payment profile
- expert evaluation
- lifecycle

Sourcing owns relationship-specific commercial/supply data.

Supplier Master must NOT own:

- Item Price
- Purchase Currency
- Item MOQ
- Item-specific Supplier Lead Time
- Shipping Mode
- Incoterm
- Forwarder to Shipment Days
- Shipping Period Days
- Customs Period Days
- Preferred Supplier relation
- price validity periods

These belong to later Sourcing / Supply Route features.

---

## 21. Supplier List UI

Baseline capabilities:

- Supplier list/grid
- Search
- Active/Inactive filter
- Country filter
- Hadron Owner filter where available
- Add Supplier
- Edit Supplier
- Deactivate
- Reactivate
- Supplier detail
- lightweight Contact management within Supplier context

Reuse approved grid/form patterns.

Follow:

`docs/brand/UI-BRAND-RULES.md`

---

## 22. Supplier Detail UX

Supplier Detail should separate concerns visually.

Recommended sections:

- Identity
- Contact / Owner
- Default Payment Profile
- Expert Evaluation
- Status / Notes

Do not place Sourcing, Item Price or PO management in the Supplier Master screen before those features exist.

---

## 23. Contact UX

Within Supplier Detail, user should be able to manage lightweight Supplier Contacts:

- Add
- Edit
- Deactivate
- Mark one active Contact as Primary

Do not create a separate enterprise CRM experience in this Package.

---

## 24. Reference Data Integration

MRP-03 consumes from MRP-01:

- Country
- Payment Term

It must reuse Shared/Reference Data and must not duplicate those lists.

For new assignment:

- active values only

For historical data:

- inactive referenced values remain resolvable

---

## 25. Person / Owner Integration

MRP-03 follows the Option B decision from MRP-01 by owning the lightweight `organization.internal_people` boundary required for Supplier Owner selection.

`InternalPerson` contains:

- `Id : uuid`
- `DisplayNameFa` required, maximum 200 characters
- `DisplayNameEn` nullable, maximum 200 characters
- `IsActive`
- `Version : bigint`
- `CreatedAtUtc` / `UpdatedAtUtc`

A minimal management API/UI is allowed. This boundary must not expand into HR, Employee, Department, payroll or Identity.

---

## 26. Supplier Number Generation

Supplier Number must be generated server-side and safely under concurrency.

Requirements:

- unique
- immutable
- not user-overridable
- gapless numbering not required
- generated from PostgreSQL sequence `procurement.supplier_number_seq`
- formatted as `SUP-{value:D4}`
- gaps are allowed; `MAX()+1` is prohibited

---

## 27. Duplicate Supplier Name Warning

Supplier Name must not have a hard uniqueness constraint.

The UI/application may warn about exact or clearly duplicated normalized names.

Do not introduce fuzzy-search infrastructure solely for this warning unless already available.

---

## 28. Permissions

Architecture should distinguish:

- View Supplier Master
- Manage Supplier Master

If contacts are managed inside Supplier, those operations fall under the approved Supplier management permission unless architecture already provides a better boundary.

Reuse existing authorization.

Do not create parallel Identity infrastructure.

---

## 29. Auditability

Important Supplier changes must be compatible with Audit:

- Create
- Supplier Name change
- Country change
- Hadron Owner change
- Payment Term change
- Payment percentage change
- Credit Days change
- Expert Score change
- Contact create/edit/deactivate
- Primary Contact change
- Deactivate
- Reactivate

If central Audit is not yet implemented, preserve the integration boundary rather than building a competing platform.

---

## 30. Historical Payment Integrity

Supplier Master stores current defaults.

Changing current Supplier Payment Profile must not be designed in a way that can rewrite historical downstream transactions.

Future PO will Snapshot:

- Payment Term
- Advance %
- Before Shipment %
- Credit %
- Credit Days

MRP-03 must preserve this architectural boundary.

---

## 31. API / Application Use Cases

Required use cases include:

- list/search Suppliers
- get Supplier detail
- create Supplier
- update allowed Supplier fields
- deactivate/reactivate Supplier
- manage Supplier Contacts
- set/change Primary Contact

Avoid unrestricted dangerous generic CRUD.

Exact endpoints are determined during Analysis.

---

## 32. Validation

Server-side validation is mandatory.

At minimum:

- Supplier Name required
- Country required
- Country must resolve
- active Country required for new assignment
- Hadron Owner required for Active Supplier
- Payment Term required for Active Supplier
- each payment percentage between 0 and 100
- active Supplier payment percentages total exactly 100
- Credit Days > 0 if Credit % > 0
- Credit Days and Credit Anchor Event normalized to null if Credit % = 0
- Expert Lead Time Score between 1 and 10 when supplied
- Expert Quality Score between 1 and 10 when supplied
- max one active Primary Contact per Supplier
- Primary Contact must belong to Supplier
- inactive Contact cannot remain Primary
- client cannot supply/override Supplier Number or Internal ID

---

## 33. Precision

Payment percentages must use `numeric(5,2)`.

Do not use binary floating point for:

- payment percentages
- future prices
- future exchange-related data

Scores may use integer values.

---

## 34. Performance

Support efficient:

- Supplier Number lookup
- Supplier Name search
- Active filtering
- Country filtering
- Owner filtering
- loading Contacts for one Supplier

Avoid:

- N+1 queries
- loading all Contacts for all Suppliers when list/grid does not need them
- loading future Sourcing/PO data
- speculative caching

---

## 35. Seed / Migration

Codex must not invent real Supplier records.

If legacy Supplier data is not supplied:

- implement capability
- do not fabricate production Suppliers

Reference values may come from MRP-01.

Legacy Supplier import will be a controlled future migration/import task.

---

## 36. Explicit Non-Scope

MRP-03 must NOT implement:

- Product Master changes beyond required integration
- Part Master
- Component Master
- Sourcing
- Price
- Currency-per-Supplier/Item
- MOQ
- operational Item Lead Time
- Shipping Mode assignment to Supplier/Item
- Incoterm assignment to Supplier/Item
- Shipping Days
- Customs Days
- Forwarder Days
- Purchase Orders
- Cash Flow
- Supplier system-performance analytics
- CRM
- full Party Management
- HR
- full Identity
- Workflow Engine

---

## 37. Acceptance Criteria

MRP-03 is acceptable when:

1. Internal Supplier ID is system-controlled.
2. Supplier Number is auto-generated, unique, immutable and concurrency-safe.
3. Supplier Name is required but not hard-unique.
4. Country is required and uses MRP-01 Reference Data.
5. Supplier supports multiple lightweight Contacts.
6. Maximum one active Primary Contact exists.
7. Contact Person is not a text-list field.
8. Active Supplier requires Hadron Owner.
9. Hadron Owner follows Option B entity approach.
10. Active Supplier has valid Default Payment Profile.
11. Payment percentages are each 0..100.
12. Active Supplier payment percentages total 100%.
13. Credit Days rule is enforced.
14. Expert Lead Time Score is optional 1..10.
15. Expert Quality Score is optional 1..10.
16. Expert Scores remain separate from future system metrics.
17. Supplier can exist without Items.
18. Active/Inactive lifecycle works.
19. Historical Supplier data remains resolvable.
20. No historical hard delete.
21. Price/Currency/MOQ/operational lead time/shipping/customs data do not leak into Supplier Master.
22. Reference Data is reused, not duplicated.
23. Server-side validation exists.
24. Tests cover concurrency, payment validation, contacts and lifecycle.
25. No Sourcing/PO/CRM feature is prematurely implemented.

---

## 38. Locked Decisions

The following are locked for MRP-03:

1. Internal Supplier ID + auto Supplier Number.
2. Supplier Number is server-generated, unique, immutable and concurrency-safe.
3. Supplier Name required but not hard-unique.
4. Country required.
5. Supplier may have multiple Contacts.
6. One active Contact may be Primary.
7. Contact is Entity, not Text List.
8. Active Supplier requires a Hadron Owner.
9. Hadron Owner follows Option B.
10. Supplier has a Default Payment Profile.
11. Payment percentages must each be 0..100.
12. Complete active payment profile totals 100%.
13. Credit Days > 0 if Credit % > 0.
14. Expert Lead Time Score is separate from future System Performance.
15. Expert Quality Score is separate from future System Performance.
16. Supplier can exist without Item linkage.
17. Active/Inactive lifecycle.
18. Notes are informational only.
19. Supplier Master does not own operational Item Lead Time.
20. Supplier Master does not own Item Price/Currency/MOQ/Shipping/Incoterm/Customs data.
21. Future PO snapshots Supplier default payment terms.
22. Changes to Supplier defaults must not rewrite historical downstream records.

---

## 39. Open Questions

No Business-critical or implementation-blocking question remains for MRP-03. The Owner-approved MRP-03A baseline closes the Contact fields, Internal Person ownership, Supplier Number sequence, aggregate concurrency and duplicate-name warning decisions.

## 40. Final Owner-Approved MRP-03A Baseline

1. `organization.internal_people` is the lightweight Owner source of truth. It has its own `Version : bigint` concurrency token and the exact fields defined in §25.
2. Supplier creation always produces an Inactive Supplier. Country is required at creation and must be an active `COUNTRY`. Owner and Payment Term may be null, and the payment total may be incomplete, while Inactive.
3. Activation and Reactivation require active Country, active Internal Person Owner, active `PAYMENT_TERM`, percentages totaling exactly `100.00`, and valid credit rules.
4. Advance, Before Shipment and Credit percentages use `numeric(5,2)` and each must always be between `0.00` and `100.00`.
5. `Credit > 0` requires `CreditDays > 0` and an anchor from `ORDERED`, `SHIPPED`, or `WAREHOUSE_RECEIPT`. `Credit = 0` canonicalizes both `CreditDays` and the anchor to `null`. MRP-03 stores no Receipt dates.
6. Supplier Number is server-generated from `procurement.supplier_number_seq`, formatted `SUP-{value:D4}`, immutable and unique. Gaps are allowed and `MAX()+1` is prohibited.
7. Duplicate Supplier Name is warning-only and has no database uniqueness constraint.
8. A Supplier may have zero or many Contacts. `FullName` is required; `JobTitle`, `Email`, `Phone`, and `Mobile` are optional. No contact method is mandatory.
9. At most one active Primary Contact is enforced by a partial unique database index. An inactive Contact cannot be Primary; deactivation clears `IsPrimary`; Contacts have no hard delete.
10. Supplier, Default Payment Profile and Contact lifecycle mutations all require `ExpectedVersion` and use the single `Supplier.Version : bigint` aggregate concurrency token. Successful mutations advance it; stale mutations return structured HTTP `409 Conflict` without a lost update.
11. Reuse `IAuditEventSink` with the existing post-commit best-effort audit policy. Do not create Party, HR, Identity, Sourcing, PO or parallel Audit scope.

---

# B. Prompt 03A — Supplier Master Analysis

```text
# MRP-03A — Supplier Master Analysis

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
- docs/brand/UI-BRAND-RULES.md

Inspect actual Repository state.
Do not assume MRP-01 or MRP-02 implementation succeeded simply because it was planned.

TASK:
Analyze MRP-03 Supplier Master.

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

1. Understanding of Supplier Master role and boundary.
2. Actual Repository findings and reusable foundation.
3. Supplier identity:
   - Internal ID
   - Supplier Number
   - Supplier Name
4. Concurrency-safe Supplier Number generation.
   Reuse MRP-02 numbering pattern if it actually exists and is appropriate.
5. Supplier Name normalization and warning-level duplicate handling.
6. Country integration from MRP-01.
7. Lightweight Supplier Contact model:
   - use the finalized minimum fields
   - multiple Contacts
   - one active Primary Contact
   - no full CRM
8. Hadron Owner:
   - Option B is locked
   - use `organization.internal_people` as the approved lightweight boundary
   - no HR/full Identity
9. Default Payment Profile model:
   - Payment Term
   - Advance %
   - Before Shipment %
   - Credit %
   - Credit Days
   - Credit Anchor Event: ORDERED / SHIPPED / WAREHOUSE_RECEIPT
10. Payment validation and activation rules.
    Confirm Credit Anchor is required with Credit% > 0, Credit Days and Anchor are canonicalized to null with Credit%=0, and WAREHOUSE_RECEIPT means operational Final Receipt Date without moving Receipt ownership into MRP-03.
11. Expert Lead Time Score and Expert Quality Score:
   - optional 1..10
   - explicitly separate from future system metrics
12. Supplier-level vs Sourcing-level ownership.
    Confirm Price, Currency, MOQ, operational Lead Time, Shipping, Incoterm, Forwarder, Customs are NOT Supplier Master data.
13. Supplier creation without Item linkage.
14. Active/Inactive lifecycle.
15. API/application use cases.
16. UI:
   - Supplier grid
   - Supplier detail
   - Contact management
   - payment section
   - expert scores
17. Permissions/Audit integration.
18. DB constraints/indexes.
19. Numeric precision.
20. Performance/query strategy.
21. Testing plan.
22. Risks/contradictions across AGENTS, Master Spec, Decisions, MRP-01, MRP-02, MRP-03 and actual Repository.
23. Smallest implementation sequence.
24. Explicit non-scope.

Testing proposal must cover at minimum:
- Supplier Number uniqueness
- concurrent Supplier creation
- Supplier Name not hard-unique
- Country required
- active Supplier requires Owner
- active Supplier requires valid payment profile
- payment percentages total exactly 100
- invalid percentage rejected
- Credit Days required if Credit > 0
- Credit Days null if Credit = 0
- Credit Anchor required if Credit > 0
- Credit Anchor null if Credit = 0
- WAREHOUSE_RECEIPT uses operational Final Receipt Date; First Receipt and Cancellation Date are not anchors
- score range 1..10
- multiple Contacts
- max one active Primary Contact
- Primary Contact belongs to Supplier
- inactive Contact cannot remain Primary
- Supplier may exist without Item
- Active/Inactive lifecycle
- no Sourcing fields leaked into Supplier Master

Output:
1. Understanding
2. Repository Findings
3. Supplier Identity / Numbering
4. Contact Model
5. Owner Model
6. Payment Profile
7. Expert Evaluation
8. Ownership Boundaries
9. Data Model
10. Validation / Constraints
11. API Design
12. UI Design
13. Permissions / Audit
14. Performance
15. Tests
16. Risks / Contradictions
17. Implementation Plan
18. Approval Required

Then STOP.
Do not implement anything.
```

---

# C. Prompt 03B — Supplier Master Implementation

```text
# MRP-03B — Implement Supplier Master

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-03A Supplier Master Analysis

Inspect actual implementation state and reuse approved existing patterns.

TASK:
Implement only the approved MRP-03 Supplier Master.

If MRP-03A contains unresolved material contradictions, STOP.

SUPPLIER IDENTITY
Implement:
- Internal system ID
- server-generated Supplier Number
- Supplier Name

Supplier Number:
- unique
- immutable
- concurrency-safe
- server-generated
- not user-overridable
- gapless not required
- use PostgreSQL sequence `procurement.supplier_number_seq`
- format `SUP-{value:D4}`
- gaps allowed; never use `MAX()+1`

SUPPLIER NAME
- required
- no hard DB uniqueness
- implement only approved lightweight duplicate warning

COUNTRY
- required at Create
- reuse MRP-01 Country
- Country must be active at Create
- historical inactive Country remains resolvable

CONTACTS
Implement the approved lightweight Supplier Contact model:
- multiple Contacts per Supplier
- zero Contacts allowed
- FullName required
- JobTitle/Email/Phone/Mobile optional
- no contact method is mandatory
- at most one active Primary Contact
- enforce max one active Primary through a partial unique database index
- Contact entity, not text list
- Add/Edit/Deactivate; no hard delete
- set/change Primary
- inactive Contact cannot be Primary
- deactivating Primary clears IsPrimary
- every Contact mutation requires ExpectedVersion and advances Supplier.Version

Do not build CRM/full Party Management.

HADRON OWNER
- implement `organization.internal_people` as the lightweight Owner master
- fields: Id uuid; DisplayNameFa required max 200; DisplayNameEn nullable max 200; IsActive; Version bigint; CreatedAtUtc/UpdatedAtUtc
- provide only the minimal management API/UI needed to manage this source of truth
- active Supplier requires Hadron Owner
- Owner may be null while Supplier is Inactive
- Owner must reference an active InternalPerson for Activation/Reactivation
- InternalPerson mutations use its own ExpectedVersion/Version concurrency
- no HR, Employee, Department, payroll or Identity

DEFAULT PAYMENT PROFILE
Implement:
- Payment Term
- Advance %
- Before Shipment %
- Credit %
- Credit Days
- Credit Anchor Event: ORDERED / SHIPPED / WAREHOUSE_RECEIPT

Always:
- store percentages as numeric(5,2)
- each percentage 0.00..100.00
- Credit Days and Credit Anchor Event canonicalized to null when Credit % = 0

While Supplier is Inactive:
- Payment Term may be null
- payment total may be incomplete

For Activation/Reactivation:
- Payment Term required and active
- Country required and active
- InternalPerson Owner required and active
- total = exactly 100.00
- Credit Days > 0 if Credit % > 0
- Credit Anchor Event required if Credit % > 0
- WAREHOUSE_RECEIPT uses operational Final Receipt Date from MRP-13; do not create a Supplier-owned or manually editable receipt date

MRP-03 owns no Receipt date.

EXPERT SCORES
Implement:
- Expert Lead Time Score optional 1..10
- Expert Quality Score optional 1..10

Do NOT create system-generated Supplier Performance analytics.

SUPPLIER WITHOUT ITEM
Supplier must be creatable without:
- Item
- Sourcing
- Price
- MOQ
- PO

LIFECYCLE
Support:
- create always as Inactive
- edit
- deactivate
- reactivate
- no destructive historical delete

SOURCING BOUNDARY
Do NOT put these in Supplier Master:
- Item Price
- purchase Currency
- MOQ
- operational Item/Supplier Lead Time
- Shipping Mode assignment
- Incoterm assignment
- Forwarder Days
- Shipping Days
- Customs Days
- Preferred Supplier
- price validity

BACKEND
Implement only approved:
- entities/models
- EF Core mapping
- migrations
- application use cases/services
- API
- validation
- errors

Reuse existing patterns.
Do not add speculative frameworks.

FRONTEND
Implement approved:
- Supplier list/grid
- search
- Active/Inactive filter
- Country filter
- Hadron Owner filter where available
- Add/Edit
- Deactivate/Reactivate
- Supplier Detail
- lightweight Contact management
- Default Payment Profile section
- Expert Evaluation section

Follow UI-BRAND-RULES.md.
Reuse existing grid/form patterns.

PERMISSIONS/AUDIT
Reuse existing platform capabilities.
Do not create parallel Identity or Audit systems.
Reuse `IAuditEventSink` and its existing post-commit best-effort audit policy.

CONCURRENCY
- Supplier, Default Payment Profile and Contact lifecycle mutations use one Supplier.Version bigint aggregate token
- every such mutation requires ExpectedVersion
- successful mutation advances Supplier.Version
- stale mutation returns structured HTTP 409 Conflict and must not silently overwrite data
- InternalPerson uses its own Version bigint token

TESTS
At minimum:
- Supplier Number unique
- concurrent creation safe
- exact `SUP-{sequence:D4}` formatting and sequence-based generation
- gaps accepted and no `MAX()+1`
- client cannot override Supplier Number
- Supplier Name required
- Supplier Name not hard-unique
- duplicate Supplier Name produces warning only
- Country required
- Create always Inactive and rejects inactive/missing Country
- Inactive Supplier permits null Owner, null Payment Term and incomplete payment total
- active Supplier requires Owner
- Activation/Reactivation rejects inactive Owner, Country or Payment Term
- active Supplier requires valid Payment Term
- percentages each 0..100
- percentages persisted as numeric(5,2)
- Activation/Reactivation payment total = exactly 100.00
- Credit > 0 requires CreditDays > 0 and valid anchor
- Credit = 0 canonicalizes CreditDays and anchor to null
- only ORDERED/SHIPPED/WAREHOUSE_RECEIPT anchors accepted
- MRP-03 stores no Receipt dates
- Expert scores 1..10
- zero Contacts supported
- multiple Contacts supported
- FullName required and optional contact methods accepted
- max one active Primary Contact
- database partial unique index prevents concurrent active Primary duplicates
- Primary Contact belongs to Supplier
- inactive Contact cannot remain Primary
- deactivating Primary clears IsPrimary
- no Contact hard delete
- Supplier/Profile/Contact mutations require ExpectedVersion, advance Supplier.Version and return structured 409 when stale
- InternalPerson lifecycle and its own Version concurrency
- Supplier without Item
- Active/Inactive lifecycle
- inactive historical references resolve
- no Sourcing fields in Supplier Master

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
- Part/Component
- Sourcing
- Item Price
- Item Currency
- MOQ
- operational Lead Time
- Shipping/Incoterm route assignment
- Forwarder/Shipping/Customs timeline
- Purchase Orders
- Cash Flow
- Supplier system-performance analytics
- CRM
- full Party Management
- HR
- full Identity
- Workflow Engine

Final report:
- Changed
- Database
- Supplier Identity
- Contacts
- Owner
- Payment Profile
- Expert Scores
- API
- UI
- Validation
- Tested
- Deviations
- Pending

Then STOP.
Do not start MRP-04.
```

---

# D. Prompt 03C — Supplier Master Independent Audit

```text
# MRP-03C — Supplier Master Independent Audit

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
- docs/brand/UI-BRAND-RULES.md
- approved MRP-03A Analysis
- MRP-03B implementation report

Inspect all MRP-03 implementation files.

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
   Confirm no premature Sourcing, PO, Cash Flow, CRM, HR, Employee, Department, payroll, Identity, full Party, system-performance analytics or parallel Audit system.

2. Identity
   - Internal ID system-controlled
   - Supplier Number distinct from Name
   - auto-generated
   - immutable
   - concurrency-safe
   - not client-overridable
   - generated only from `procurement.supplier_number_seq`
   - formatted `SUP-{value:D4}` with allowed gaps and no `MAX()+1`

3. Supplier Name
   - required
   - not incorrectly hard-unique
   - duplicate name is warning-only

4. Country
   - MRP-01 reused
   - required and active at Create
   - active/inactive reference behavior correct

5. Contacts
   - zero or many contacts
   - multiple contacts
   - FullName required
   - JobTitle/Email/Phone/Mobile optional and no contact method required
   - Contact is Entity, not Text List
   - max one active Primary
   - partial unique database index enforces max one active Primary
   - Primary belongs to Supplier
   - inactive Contact not Primary
   - deactivating Primary clears IsPrimary
   - no hard delete
   - Contact mutations use ExpectedVersion and Supplier.Version
   - no unnecessary CRM architecture

6. Hadron Owner
   - `organization.internal_people` is the real lightweight Owner source
   - exact Id/DisplayNameFa/DisplayNameEn/IsActive/Version/timestamp model
   - DisplayNameFa max 200 required; DisplayNameEn max 200 nullable
   - minimal management API/UI only
   - Owner optional while Inactive and active Owner required for Activation/Reactivation
   - InternalPerson uses its own Version bigint stale-write protection
   - no parallel HR/Employee/Department/payroll/Identity platform

7. Payment Profile
   Verify:
   - Payment Term
   - Advance %
   - Before Shipment %
   - Credit %
   - Credit Days
   - Credit Anchor Event codes
   - percentages use numeric(5,2) and each is always 0.00..100.00
   - Payment Term may be null and payment total incomplete while Inactive
   - Activation/Reactivation require active Payment Term and exact total 100.00
   - Credit > 0 requires CreditDays > 0 and a valid anchor
   - Credit = 0 canonicalizes CreditDays and anchor to null
   - anchors are limited to ORDERED / SHIPPED / WAREHOUSE_RECEIPT
   - MRP-03 stores no Receipt dates
   - WAREHOUSE_RECEIPT resolves only from operational Final Receipt Date; First Receipt is never the financial anchor
   - decimal/numeric precision

8. Expert Scores
   - optional
   - integer 1..10
   - separate from any system metrics

9. Supplier Without Item
   - no Item/Sourcing required for Supplier creation

10. Lifecycle
   - Create always produces Inactive
   - Activation/Reactivation require active Country, active InternalPerson Owner, active PAYMENT_TERM, exact payment total and valid credit rules
   - no destructive historical deletion
   - historical resolution

11. Sourcing Boundary
   Verify Supplier Master does NOT contain:
   - Item Price
   - Item Currency
   - MOQ
   - operational Item Lead Time
   - Shipping Mode relation
   - Incoterm relation
   - Forwarder Days
   - Shipping Days
   - Customs Days
   - Preferred Supplier
   - price validity

12. Database Integrity
   - constraints
   - indexes
   - foreign keys
   - exact numeric(5,2) payment columns
   - partial unique active-Primary Contact index
   - Supplier/Profile/Contact share one Supplier.Version bigint aggregate concurrency token
   - every aggregate mutation requires ExpectedVersion, advances Version on success and returns structured 409 when stale
   - migration safety

13. API
   - use-case boundaries
   - validation
   - errors
   - no dangerous unrestricted CRUD

14. UI
   - Brand Rules
   - RTL
   - Supplier grid
   - Supplier Detail
   - Contact UX
   - Payment Profile UX
   - Expert Evaluation
   - no unrelated expansion

15. Permissions / Auditability
   - existing `IAuditEventSink` reused
   - audit remains post-commit best-effort
   - no parallel audit platform

16. Performance
   - N+1
   - over-fetching Contacts
   - unnecessary future data joins
   - caching/infrastructure overengineering

17. Tests
   Verify meaningful assertions for all required cases, especially:
   - concurrent Supplier Number generation
   - Create always Inactive with required active Country
   - inactive incomplete Owner/PaymentTerm/payment-total rules
   - Activation/Reactivation completeness and active-reference rules
   - numeric(5,2), exact payment total and Credit canonicalization
   - zero/multiple Contacts and optional contact methods
   - Primary Contact partial-index uniqueness and deactivation behavior
   - Supplier aggregate and InternalPerson concurrency conflicts

18. Specification Drift
   Compare:
   - Master Spec
   - Decisions
   - MRP-01
   - MRP-02
   - MRP-03
   - approved MRP-03A
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

# E. Final Pre-Codex Audit — MRP-03

## Result

`PASS`

## Audited Approved Decisions

تمام ۱۴ تصمیم تأییدشده Supplier Master پوشش داده شده‌اند:

1. Internal Supplier ID + auto Supplier Number.
2. Supplier Name required; duplicate warning instead of hard uniqueness.
3. Country required.
4. Multiple Contacts + one Primary.
5. Hadron Owner required for Active Supplier.
6. Supplier Default Payment Profile.
7. Advance + Before Shipment + Credit = 100%.
8. Credit Days conditional rule.
9. Expert Lead Time / Quality Scores remain human scores.
10. Supplier may exist without Item.
11. Active / Inactive lifecycle.
12. Notes are informational.
13. Operational Supplier/Item Lead Time excluded from Supplier Master.
14. Price / Currency / MOQ / Shipping / Incoterm excluded and delegated to Sourcing.

## Cross-Package Consistency

- Country and Payment Term reuse MRP-01.
- Contact Person / Hadron Owner follow MRP-01 Option B.
- Supplier Master remains independent from Product Master.
- Sourcing-specific data does not leak into MRP-03.
- Default Payment Profile is positioned correctly as a future PO source, not historical PO data.
- Supplier changes cannot conceptually rewrite future historical PO snapshots.
- Expert Scores remain separate from future Lead-Time Learning/System Performance data.

## Architecture Corrections Applied

- Supplier Number made server-side and concurrency-safe.
- Gapless numbering explicitly not required.
- Supplier Name deliberately not used as technical identity.
- Multiple Supplier Contacts modeled without introducing CRM.
- Maximum one active Primary Contact is explicit.
- Active Supplier completeness rules are explicit.
- Payment values use decimal/numeric.
- Credit Days contradictions are blocked.
- Supplier-level `Lead Time Score` is explicitly an Expert Score, not MRP operational lead time.
- All Item-specific supply parameters remain reserved for Sourcing.

## Prompt Audit

### MRP-03A
PASS

Analysis-only, repository-aware, and explicitly reviews Person/Owner and Contact boundaries.

### MRP-03B
PASS

Implementation is bounded to Supplier Master and contains strict Sourcing/CRM/PO scope guards.

### MRP-03C
PASS

Independent read-only audit checks payment integrity, contacts, ownership boundaries, concurrency and specification drift.

## Final Decision

`MRP-03 Supplier Master` is approved for the project Prompt Baseline.

It should not yet be sent to Codex until all MRP Packages are designed and the complete prompt set receives the final cross-package audit.
