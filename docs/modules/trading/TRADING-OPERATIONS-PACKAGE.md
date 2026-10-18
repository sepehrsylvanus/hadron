# Hadron ERP Trading Operations

Status: `IMPLEMENTED — OWNER MANUAL TESTING`

## Purpose

Trading is an independent Hadron ERP workspace for the commercial/import file lifecycle. It replaces the legacy Google Sheet and the standalone `hadron-trade-web` prototype without copying their infrastructure or spreadsheet-shaped persistence.

The initial operational flow is:

```text
Trade Project
→ Order Registration
→ FX Allocation / Receipt
→ Shipment
→ Shipment Legs
→ Project–Shipment Allocation
→ Customs Clearance
→ Trade Costs
→ Operational Report / Closure
```

## Locked boundaries

1. Trading owns its domain tables in PostgreSQL schema `trading`.
2. Trading does not read or mutate MRP facts, formulas, snapshots or plan state.
3. Future integrations with MRP, Procurement, Inventory, Finance and Logistics use explicit application/API contracts. Direct cross-module table mutation is forbidden.
4. The module uses the central Hadron ERP Identity, permissions, authenticated personnel profile, date service, shell, localization and shared UI primitives.
5. Google Sheets and the standalone SQLite database remain read-only migration sources. No data is imported until a dry-run reconciliation identifies the authoritative record for every conflict.
6. Historical source identifiers and display snapshots are preserved during a future approved migration. New records use immutable server-generated business numbers and UUID technical identity.
7. Canonical business dates are Gregorian `date`; timestamps are UTC. Jalali is input/display only and uses the shared Tehran calendar.
8. Monetary values use exact decimal precision and an explicit original Currency. Cross-currency totals are forbidden without an approved FX snapshot.
9. Important operational changes are append-only audited. Commercial history is never hard deleted.
10. Archive is a query-time reporting read model. Only an explicit management note is persisted.

## Source classification

| Legacy source | ERP treatment |
|---|---|
| `Projects` | `TradeProject` aggregate root |
| `Order_Registration` | child of one Trade Project |
| `FX_Allocations` | staged FX facts linked to project and optionally order registration |
| `Shipments` | trade-owned import shipment until a future approved Logistics integration |
| `Shipment_Legs` | ordered operational legs of one shipment |
| `Project_Shipment_Link` | explicit many-to-many allocation fact |
| `Customs_Clearance` | customs case belonging to one shipment |
| `Cost_Transactions` | original-currency cost fact scoped to project/shipment/leg/customs |
| `Archive` | calculated read model; editable note only is persisted |
| `Data_Log` | immutable legacy audit import |
| `Users_Roles` | not imported as authorization; actors resolve to central Identity where possible |
| `Lists` | process codes become domain constants; managed counterparties become Trading references; ERP masters are not duplicated |

## Lifecycle codes preserved from the source

- Project: `DRAFT`, `ACTIVE`, `WAITING_ORDER`, `FX_PROCESS`, `READY_TO_SHIP`, `IN_TRANSIT`, `IN_CUSTOMS`, `CLEARED`, `DELIVERED`, `CLOSED`, `CANCELLED`.
- Order Registration: `DRAFT`, `SUBMITTED`, `ACTIVE`, `SUSPENDED`, `CANCELLED`.
- FX Process: `REQUEST`, `ALLOCATION`, `DEAL`, `RECEIPT`, `CORRECTION`.
- FX Deal: `NOT_STARTED`, `IN_QUEUE`, `PRIORITY_CONFIRMED`, `ALLOCATED`, `DEAL_DONE`, `CANCELLED`.
- FX Receipt: `NOT_RECEIVED`, `PARTIAL`, `COMPLETE`, `CANCELLED`.
- Shipment: `DRAFT`, `WAITING_LINK`, `READY`, `IN_TRANSIT`, `ARRIVED`, `IN_CUSTOMS`, `PARTIAL_CLEARED`, `CLEARED`, `DELIVERED`, `CANCELLED`.
- Shipment Leg: `PLANNED`, `DEPARTED`, `IN_TRANSIT`, `ARRIVED`, `DELAYED`, `CANCELLED`.
- Customs: `NOT_STARTED`, `DOCUMENTS_PENDING`, `IN_PROGRESS`, `PARTIAL`, `COMPLETE`, `BLOCKED`, `CANCELLED`.
- Payment: `UNPAID`, `PARTIAL`, `PAID`, `CANCELLED`.

Creating an Order Registration does not silently change the Project status. Lifecycle changes are explicit commands and audited.

## Locked operational transitions

- Project advances only `DRAFT → ACTIVE → WAITING_ORDER → FX_PROCESS → READY_TO_SHIP → IN_TRANSIT → IN_CUSTOMS → CLEARED → DELIVERED → CLOSED`. Cancellation is allowed only before `DELIVERED`. `CLOSED → DELIVERED` is a dedicated reopen command requiring `trading.project.reopen` and a reason.
- Order Registration advances `DRAFT → SUBMITTED → ACTIVE`; suspension/resumption is explicit and cancellation is recorded as a final state.
- Shipment advances `DRAFT → WAITING_LINK → READY → IN_TRANSIT → ARRIVED → IN_CUSTOMS → PARTIAL_CLEARED/CLEARED → DELIVERED`. Shipment Legs advance from `PLANNED` through departure/transit/delay to `ARRIVED`.
- Customs advances `NOT_STARTED → DOCUMENTS_PENDING → IN_PROGRESS → PARTIAL/COMPLETE`; blocked/resumed and cancelled states are explicit.
- A backward transition is not ordinary editing. It requires the correction flag, a mandatory reason and an append-only audit event. Original history is never overwritten or deleted.
- `CLOSED` and `CANCELLED` projects reject new Order, FX, Shipment link, Customs and Cost facts at both application and database boundaries.
- A shipment may be linked to multiple projects through a versioned allocation fact. Allocation edits require optimistic concurrency and a reason; the shipment itself is never duplicated.

## UX contract

The workspace is named `Trading` / `بازرگانی`. Its primary navigation is:

1. Operations overview
2. Trade projects
3. Shipments
4. Customs and clearance
5. Trade costs
6. Comprehensive report

Order Registration, FX stages, shipment legs, project–shipment links and close/reopen actions live in the relevant Object Page instead of separate top-level menus. List context, filters and deep links are preserved.

## Permissions

Every route and command is default-deny and registers an immutable permission code:

- `trading.project.view`, `trading.project.manage`, `trading.project.close`, `trading.project.reopen`
- `trading.order_registration.view`, `trading.order_registration.manage`
- `trading.fx.view`, `trading.fx.manage`
- `trading.shipment.view`, `trading.shipment.manage`
- `trading.customs.view`, `trading.customs.manage`
- `trading.cost.view`, `trading.cost.manage`
- `trading.report.view`, `trading.report.manage`, `trading.audit.view`

System Administrator automatically receives current and future registered permissions but never bypasses business validation, lifecycle or database integrity.

## Migration safety

The live Google Sheet and standalone SQLite source currently diverge. Migration is therefore a later explicit operation:

1. immutable extraction from each source;
2. manifest and hashes;
3. staging tables or bounded in-memory validation;
4. conflict and relationship report;
5. Owner approval of authoritative records;
6. Development backup;
7. one transactional import preserving approved source IDs/snapshots;
8. reconciliation and readback.

No merge, reset, seed or guessed replacement data is allowed.

## Release gates

- module schema and API remain independent of MRP;
- all APIs have permission metadata and authorization tests;
- exact decimal and currency isolation;
- Jalali input/Gregorian persistence and Tehran timestamps;
- duplicate business-number and source-identity protection;
- project/shipment allocation integrity;
- chronology validation for FX, legs and customs;
- append-only audit and no hard-delete endpoint;
- database checks/triggers reject invalid status/date values, cross-project cost scopes and operational writes against final projects;
- server-side paging/filter/sort and records beyond 100;
- FA/EN and RTL/LTR;
- forward migration, zero-to-latest and EF pending changes zero;
- MRP regression remains green.


## Scoped first-step form (2026-10-18)

The six-step home layout from reference commit `620c750` is retained; only its first panel is implemented in this slice. `/trading` and `/trading/projects/new` share the same creation form: generated internal number, business start/referral date, subject, optional customer, optional business card, required active follow-up user, read-only canonical DRAFT status, and notes. Project priority is not editable in this form; legacy storage remains NORMAL. No later-stage or eight-digit-code inputs are added.

The additive `20261018120000_AddTradingProjectSetup` migration preserves historical nulls. Existing `trading.reference_values` with CUSTOMER/BUSINESS_CARD roles remain the deployed lookup source; this slice does not silently migrate the full proposed 33-table schema. The Identity-owned `IActiveUserDirectory` exposes active IDs and display names only.

`POST /api/trading/project-setup`, `GET /api/trading/project-setup/lookups` and `POST /api/trading/project-setup/resolve-jalali-date` reuse `trading.project.manage`. The date endpoint reuses the shared calendar implementation without requiring MRP permissions. The old create command is retained for existing consumers. Creation persists the project and append-only CREATE audit atomically, never an automatic order or lifecycle transition. Setup facts and reference-name snapshots are returned on the project object page.

The production view is exported through React SSR as clean and annotated offline HTML under `docs/ui/trading-stage-1/`; annotations distinguish deployed storage from the proposed future Prisma model. See that folder's README for the exact field map, tests, known limitations and migration prerequisites. Backend tests and EF model validation require the .NET-enabled test environment; they were not executable in the coding sandbox.


## Scoped second-step order management (2026-10-18)

The second home panel now mounts the same order workspace used by the project object page. It selects an existing project and manages multiple registered-order documents: external registration number, business registration date, original currency, exact registered amount, canonical lifecycle status and per-document notes. Server-paged search/status filters and independently paged active-currency lookups replace the legacy unpaged order table and parallel create form. No priority or later-stage inputs were added to stage one.

This is the deployed document-header slice, not an implicit deployment of the full proposed schema. Goods lines, permits, incoterms/payment dictionaries and generic stage assignment are not introduced here. Bank/FX allocation/receipt, shipping and customs remain separate. No tables or migration are added for this slice. The preceding first-step migration is still a prerequisite.

New bounded order list/read/update/currency/date endpoints reuse existing order view/manage permissions. Draft edits require a reason and expected version and cannot move an order to another project or set its status. Closed/cancelled projects reject writes. Existing FX-linked drafts reject data edits; Draft editing and FX linking acquire the same order-row lock in their transactions to avoid a check/link race. Record+audit writes remain atomic and there is no hard delete or automatic lifecycle progression. The legacy create/status APIs remain compatible, with additional required-date, numeric(24,6), notes-length and reason-length validation.

Managed-order reads return amounts as invariant decimal strings and write requests explicitly accept decimal strings; browser formatting/validation never converts an amount to JavaScript Number. Existing response contracts are retained for older consumers. Currency snapshots survive master-data rename/deactivation unless a permitted explicit currency change is made.

Matching clean/annotated HTML is exported through the actual React form, picker and list views under `docs/ui/trading-stage-2/`; see its README for exact field lineage and exclusions. Frontend build, 323 frontend tests, targeted lint and mocked-API real-built-app browser checks passed. Six backend integration tests were added but backend compilation, PostgreSQL execution and lock behavior still require verification in the .NET-enabled environment; the sandbox has no dotnet SDK.
