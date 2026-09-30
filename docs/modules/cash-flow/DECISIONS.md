# Cash Flow Decisions

## Status

This file records owner-approved Cash Flow decisions. Items under **Deferred decisions** are intentionally unresolved and are not implementation authority.

## CF-001 — Separate read-only workspace

**Decision:** Cash Flow is a separate restricted workspace. V1 is read-only and owns projection/costing read contracts, not PO, Receipt, Supplier, BOM or Item source facts. It uses query-time backend read models with server aggregation/paging per MRP `DEC-060`; frontend financial recomputation and Cash Flow persistence are forbidden in v1.
**Status:** `LOCKED`

## CF-002 — Independent permissions

**Decision:** The immutable initial read permission family is `cash_flow.dashboard.view`, `cash_flow.obligation.view` and `cash_flow.costing.view`. Each permission authorizes only its named read surface. Later approved Reporting FX and Material Cost Book extensions add only their explicitly documented, independently enforced permissions; no permission is implied by workspace access.
**Status:** `LOCKED`

## CF-003 — Planned and committed exposure

**Decision:** `ORDERED` and later active operational PO states are `COMMITTED`. `APPROVED` exposure is `PLANNED`, is opt-in and remains visibly separate. Draft is excluded. A source obligation is represented once only; moving from Approved to Ordered replaces planned classification with committed classification and must never double-count.
**Status:** `LOCKED`

## CF-004 — Independent installments

**Decision:** Non-zero `ADVANCE`, `BEFORE_SHIPMENT` and `CREDIT` components are independent obligations. Advance is anchored to `ORDERED`. Before Shipment is anchored to `SUPPLIER_READY`, not `SHIPPED`. Credit uses the configured `ORDERED`, `SHIPPED` or `WAREHOUSE_RECEIPT` anchor plus Credit Days. An unresolved component remains visible and does not suppress another component.
**Status:** `LOCKED`

## CF-005 — Event-date resolution

**Decision:** Schedule event dates resolve `Actual -> Latest Revised Plan -> Original Plan -> Unresolved`. No current-day, Requested Receipt, current-Sourcing or manual financial-date fallback is allowed. `WAREHOUSE_RECEIPT` Actual uses valid Posted, non-reversed Receipt completion; First Receipt and Cancellation Date are not anchors.
**Status:** `LOCKED`
**Related:** MRP `DEC-052`, `DEC-059`

## CF-006 — Original-currency isolation

**Decision:** Obligation, dashboard bucket, Cost Basis and BOM material total retain original currency. Cross-currency addition, FX conversion and a currencyless grand total are forbidden in v1.
**Status:** `LOCKED`
**Related:** MRP `DEC-051`

## CF-007 — V1 material-cost source

**Decision:** A leaf Item first uses the latest eligible Posted, non-reversed Receipt on/before `asOfDate`, with price/currency/unit taken from the linked immutable PO Line. If absent, a deterministic latest eligible PO-line estimate is returned and explicitly labelled `PURCHASE_ORDER_ESTIMATE`. Source provenance is mandatory. A Receipt-linked result is not represented as invoice or final landed cost.
**Status:** `LOCKED`

## CF-008 — BOM never owns price

**Decision:** BOM owns structure, Base Output Quantity, Usage Quantity, unit snapshots and Scrap Override; it never stores price. Costed BOM recursively reads the current Approved BOM, applies Base Output normalization and effective scrap exactly once per edge, and requires exact Child Base Unit agreement. No implicit UOM conversion is allowed.
**Status:** `LOCKED`

## CF-009 — Missing and mixed-currency cost

**Decision:** Missing leaf cost remains null with an explicit reason and propagates as an incomplete roll-up; it is never zero. Mixed-currency material cost remains separated into currency totals and is never arithmetically combined.
**Status:** `LOCKED`

## CF-010 — No financial execution in v1

**Decision:** V1 is expected procurement exposure only. It does not assert invoice posting, remaining payable, payment approval/execution, AP settlement or bank movement.
**Status:** `LOCKED`

## CF-011 — USD reporting-rate extension

**Decision:** Cash Flow owns a deliberately bounded, manually maintained Reporting FX Rate Set solely for planning/reporting. Every rate means `1 USD = UnitsPerUsd` units of the row currency and conversion is `Original Amount / UnitsPerUsd`. USD is implicit at `1`; IRR always means rial. Original amount and currency are never overwritten. This decision supersedes CF-006 only for the explicit USD reporting surfaces defined by `CASH-FLOW-USD-REPORTING.md`; CF-006 remains authoritative for every implicit or ungoverned cross-currency addition.
**Status:** `LOCKED`

## CF-012 — Effective-date and flat-rate selection

**Decision:** Each report selects the latest Published Rate Set with `EffectiveDate <= asOfDate`, using the highest Published revision on the same date, and applies that one set across the entire report horizon. Future-dated sets are ignored. Future FX curves and period-specific rate scenarios are not inferred.
**Status:** `LOCKED`

## CF-013 — Immutable published rate sets

**Decision:** Draft Rate Sets do not affect reports. Publish is explicit and atomic. Published sets cannot be edited or deleted; corrections create a linked revision. Writes require idempotency, optimistic concurrency and Audit. The bounded permissions are `cash_flow.reporting_rate.view` and `cash_flow.reporting_rate.manage`; Maker-Checker approval remains deferred.
**Status:** `LOCKED`

## CF-014 — Incomplete conversion must not look complete

**Decision:** Missing FX never falls back to one or zero. Original amounts remain visible, row USD is null and an affected aggregate USD total is null with explicit missing currency codes. Dashboard defaults to USD but retains an original-currency view. Costed BOM missing purchase cost and missing FX are independent, and its USD total is null if either makes the roll-up incomplete.
**Status:** `LOCKED`

## CF-015 — Cash Flow owns the Material Cost Book

**Decision:** Price remains outside BOM, Sourcing, Catalog and Inventory. Cash Flow owns a versioned Material Cost Book that preserves the original amount/currency, Pricing Quantity, Item-specific Price Unit conversion snapshot and optional Supplier/source provenance. Exact normalization is `BaseUnitPrice = PriceAmount / (PricingQuantity * FactorToBase)`. Net Weight or a conversion belonging to another Item is never inferred.
**Status:** `LOCKED`

## CF-016 — Explicit and strict costing basis

**Decision:** Product Structure Costing has two explicit, non-mixed modes: `PURCHASE_HISTORY` (the backward-compatible default) and `MATERIAL_COST_BOOK` (one explicitly selected Published revision). Material Cost Book mode never falls back to purchase history. A missing leaf price makes the affected product total incomplete/null; it is never zero or a misleading partial total.
**Status:** `LOCKED`

## CF-017 — Material Cost Book revision lifecycle

**Decision:** Draft revisions do not affect costing. Published revisions are immutable and corrections create linked revisions. Future-dated Published revisions are not eligible before their Effective Date. Partial books may be Published, but their uncovered Items remain explicit. Draft writes use idempotency, optimistic concurrency and Audit. Publish requires the independent `cash_flow.material_cost_book.publish` permission, separate from Manage.
**Status:** `LOCKED`

## CF-018 — Purchase Price History is a separate actual-fact report

**Decision:** Purchase Price History is not the manual Material Cost Book. Its row grain is one Purchase Order Line, with the original price/currency/base unit and an aggregate of valid Posted, non-reversed Receipts. Multiple receipts must never duplicate a PO line. Draft/unposted/reversed receipts do not contribute. Server-side Item, Supplier, date, currency and text filtering and bounded pagination are mandatory.
**Status:** `LOCKED`

## CF-019 — Purchase-history USD is a report valuation

**Decision:** Optional USD columns in Purchase Price History use one explicit Published Reporting FX Rate Set or one explicit valuation As-Of Date for the entire result. This is labelled reporting valuation, not transaction FX. Original price/currency remains authoritative; missing FX leaves USD null and reports an incomplete conversion context.
**Status:** `LOCKED`

## CF-020 — Current price scope excludes landed and accounting cost

**Decision:** Material Cost Book and Purchase Price History currently represent direct purchase price only. They do not claim supplier invoice, payment, accounting, weighted-average inventory valuation or final landed cost. Freight, clearance, insurance, tax and allocation require a separately approved extension.
**Status:** `LOCKED`

## Deferred decisions

The following require separate owner approval before implementation or tests can establish business expectations:

- installment currency rounding and residual assignment;
- legal/financial exposure after partial or full cancellation;
- paid advances, supplier invoices, credit notes, partial settlement and remaining payable;
- automatic/market FX source, future rate curves and accounting/trading/settlement conversion;
- freight/customs/insurance/tax scope and landed-cost allocation basis;
- weighted-average and every other inventory-valuation method;
- whether an actual invoice price supersedes the Receipt-linked PO-line price;
- persisted scenarios, publication, export and background materialization;
- system-time historical replay; and
- Company/Project/Owner Data Scope once authoritative ownership facts exist.
