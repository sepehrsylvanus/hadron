# Hadron ERP — Cash Flow V1

## Status and authority

Status: `OWNER-APPROVED V1 BASELINE — EXTENDED BY USD REPORTING AND MATERIAL COST BOOK`

This document is the canonical functional baseline for Cash Flow v1. `CASH-FLOW-USD-REPORTING.md` is the owner-approved extension for governed USD reporting-rate sets. `MATERIAL-COST-BOOK.md` is the owner-approved extension for a Cash Flow-owned material price book and a separate Purchase Price History report. Existing MRP decisions remain authoritative for Purchase Order, Delivery Schedule, milestone, Receipt, BOM, currency and historical-source facts. Cash Flow consumes those facts; it does not take ownership of them.

## Purpose

Cash Flow v1 provides a restricted workspace for:

- expected procurement-payment exposure on a dated timeline;
- currency-separated dashboard totals and daily buckets;
- drill-down to individual payment obligations; and
- a traceable material-cost roll-up for current approved BOMs;
- a governed, versioned Material Cost Book outside Engineering/BOM; and
- a read-only Purchase Price History at one-row-per-PO-line grain.

The obligation and purchase-history surfaces are forecast/read models. Material Cost Book is a bounded Cash Flow-owned valuation master. The module is not Accounts Payable, a bank ledger, payment execution, inventory valuation or a legal statement of the remaining supplier balance.

## Module boundary

Cash Flow owns:

- the obligation projection contract;
- the Cash Flow dashboard/list presentation contract;
- cost-source selection for its v1 BOM-costing view; and
- the provenance exposed with those read results.

The USD reporting extension additionally owns versioned Reporting FX Rate Sets. The Material Cost Book extension owns versioned direct-material price assumptions with exact Item-specific UOM conversion snapshots. Those values are Cash Flow planning assumptions, not enterprise accounting, trading, inventory-valuation or landed-cost rates.

Source modules remain authoritative:

- Purchase Supply owns PO commercial snapshots, status, Delivery Schedules, plan revisions, actual milestones, cancellations and Receipts.
- Supplier Master owns supplier identity and the default profile from which a PO snapshot is created.
- Engineering/BOM owns BOM revisions, lines, Base Output Quantity and Scrap Override.
- Catalog owns Item Base Unit and Item Default Planning Scrap.

Cash Flow has no command that updates those source modules. Its projection, purchase history and costing use query-time backend read models and server-side aggregation/paging in accordance with `DEC-060`. The approved extensions introduce only Cash Flow-owned Reporting FX and Material Cost Book persistence. Frontend recomputation of authoritative financial amounts, FX conversion, normalized price or BOM cost is forbidden; the client may show a clearly labelled preview that is confirmed by the backend response.

## Projection and costing read-only API

| Contract | Permission |
|---|---|
| `GET /api/v1/cash-flow/dashboard` | `cash_flow.dashboard.view` |
| `GET /api/v1/cash-flow/dashboard/resolve-jalali-date?jalaliDate=YYYYMMDD` | `cash_flow.dashboard.view` |
| `GET /api/v1/cash-flow/obligations` | `cash_flow.obligation.view` |
| `GET /api/v1/cash-flow/obligations/resolve-jalali-date?jalaliDate=YYYYMMDD` | `cash_flow.obligation.view` |
| `GET /api/v1/cash-flow/costed-boms` | `cash_flow.costing.view` |
| `GET /api/v1/cash-flow/costed-boms/resolve-jalali-date?jalaliDate=YYYYMMDD` | `cash_flow.costing.view` |
| `GET /api/v1/cash-flow/costed-boms/{parentItemId}` | `cash_flow.costing.view` |

The three permissions are independent. Access to one surface does not imply access to either of the others. Each Jalali resolver is a read-only helper protected by the same permission as its owning surface; it does not create or update business data. Projection/costing mutations and export remain deferred. The only Cash Flow mutations are the explicitly governed Reporting FX Rate Set lifecycle defined by the USD reporting extension.

## Reporting-rate lifecycle API

| Contract | Purpose | Permission |
|---|---|---|
| `GET /api/v1/cash-flow/reporting-rate-currencies` | List currency choices; USD is identified as the implicit base currency | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets?page={n}&pageSize={n}&status={DRAFT\|PUBLISHED}` | Bounded, filtered rate-set worklist | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets/current?asOfDate=YYYY-MM-DD` | Resolve the applicable Published set for an explicit Gregorian reporting date | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets/resolve-jalali-date?jalaliDate=YYYYMMDD` | Convert normalized Jalali input through the centralized calendar | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets/{id}` | Read one Draft or Published set with exact rate lines and provenance | `cash_flow.reporting_rate.view` |
| `POST /api/v1/cash-flow/reporting-rate-sets` | Create an idempotent Draft for a new Effective Date | `cash_flow.reporting_rate.manage` |
| `PUT /api/v1/cash-flow/reporting-rate-sets/{id}` | Idempotently replace a Draft atomically using `ClientRequestId` and optimistic concurrency | `cash_flow.reporting_rate.manage` |
| `POST /api/v1/cash-flow/reporting-rate-sets/{id}/publish` | Idempotently validate completeness and atomically publish an immutable set | `cash_flow.reporting_rate.manage` |
| `POST /api/v1/cash-flow/reporting-rate-sets/{id}/corrections` | Create an idempotent Draft revision that supersedes one Published set | `cash_flow.reporting_rate.manage` |

There is deliberately no delete endpoint. A Published set is immutable at both API and database levels; correction creates a linked revision. Create, Update Draft, Publish and Create Correction all require `ClientRequestId`; Update, Publish and Correction also require `ExpectedVersion`. Rates are exact invariant decimal strings named `UnitsPerUsd`; JSON number input is rejected, USD is implicit at exactly one and must not appear as a manually entered line. Publish requires every defined non-USD currency, including inactive currencies retained by historical/open obligations; activity metadata remains visible to the client.

All business dates are canonical Gregorian `date` values. Jalali is input/display only. Requests require an explicit `asOfDate`; the dashboard also requires an explicit bounded horizon. `GeneratedAtUtc` identifies when the live read model was generated. V1 does not claim system-time historical replay.

## Exposure classification

The default scope is committed exposure only.

```text
APPROVED PO = PLANNED
ORDERED and later active operational PO states = COMMITTED
DRAFT = excluded
```

Planned exposure is opt-in and remains separately labelled `PLANNED`. A PO can contribute through only one classification at a time. Transition to `ORDERED` replaces its planned exposure with committed exposure; it must never add a second copy.

`CANCELLED` is not an active v1 projection source. Whether a cancellation leaves a legal payable, paid advance, credit note or settlement obligation requires future Finance/AP facts and is explicitly deferred.

## Obligation components

Payment percentages come from the immutable PO commercial snapshot and must satisfy:

```text
Advance % + Before Shipment % + Credit % = 100%
```

Each non-zero component is an independent obligation. Failure to resolve one component date must not suppress or alter the other components.

### Advance

```text
Amount = active projected PO value x Advance %
Anchor = ORDERED
```

Advance is PO-level because `OrderedDate` is common to the PO. An approved-but-not-ordered planned PO retains the obligation with an unresolved date.

### Before Shipment

```text
Amount = active projected Schedule value x Before Shipment %
Anchor = SUPPLIER_READY
```

Before Shipment is Schedule-level. In Cash Flow v1, `SUPPLIER_READY` is authoritative for this payment stage; `SHIPPED` is not a substitute.

### Credit

```text
Amount = active projected Schedule value x Credit %
Due Date = resolved configured Credit Anchor + Credit Days
```

Supported Credit Anchor codes remain:

```text
ORDERED
SHIPPED
WAREHOUSE_RECEIPT
```

Credit Days are calendar days. For `WAREHOUSE_RECEIPT`, only valid Posted, non-reversed Receipt completion may provide an Actual date. First Receipt Date and Cancellation Date are not Credit anchors.

### Date resolution

For `SUPPLIER_READY`, `SHIPPED` and `WAREHOUSE_RECEIPT`, each required Schedule resolves independently:

```text
Actual -> Latest Revised Plan -> Original Plan -> Unresolved
```

An unresolved obligation remains visible with a null expected-payment date and `UNRESOLVED` state. The system must not invent a date from Requested Receipt Date, current Sourcing, the current day or a manually maintained financial receipt date.

## Currency and USD reporting contract

Every obligation and cost basis preserves its original currency. Dashboard summaries, chart buckets and BOM material totals are grouped by currency.

```text
100 USD + 500 CNY = two currency totals
```

No implicit exchange rate, currencyless total or cheapest-price comparison is allowed. The USD reporting extension permits only an explicit, traceable USD total derived from the selected Published Reporting FX Rate Set while preserving all original-currency values.

## Costed BOM contract

BOM stores engineering quantities and units only. It does not store, copy or own price.

Costed BOM uses the current `APPROVED` BOM graph and normalizes every edge by its revision Base Output Quantity:

```text
Required Child Quantity
= Parent Quantity x Usage Quantity / Base Output Quantity
  x (1 + Effective Scrap % / 100)
```

Effective Scrap is the BOM-line override when present; `NULL` uses Item Default Planning Scrap and explicit `0` means zero. Scrap is applied exactly once per traversed edge. Usage and cost basis must agree with the immutable Child Base Unit snapshot; no implicit UOM conversion is allowed.

Only leaf material cost contributes to totals. A non-leaf line is recursively expanded. Cycle, multiple-current-BOM and base-unit integrity failures block a misleading roll-up.

`asOfDate` limits Cost Source eligibility only. The structure itself is always the current Approved BOM; v1 does not reconstruct a historical BOM structure as of the valuation date.

### Cost-source precedence

For each leaf Item and explicit `asOfDate`:

1. Select the latest valid Posted, non-reversed Purchase Receipt on or before `asOfDate` and use the immutable Unit Price and currency from its linked PO Line.
2. If no eligible Receipt exists, return a deterministic `PURCHASE_ORDER_ESTIMATE` from the latest eligible approved/committed PO Line on or before `asOfDate`.
3. If neither source exists, return `MISSING`; Unit Cost and Extended Cost remain null.

An eligible Cost Basis requires `UnitPrice > 0`. Because the current Purchase Supply model does not distinguish an unknown zero from an approved free-of-charge line, zero and negative prices are ineligible and produce a missing-cost exception. Free-of-charge procurement treatment is deferred rather than inferred.

Receipt precedence is deterministic by Receipt Effective Date, then posting timestamp, then stable receipt-line identity. Estimate precedence is deterministic by PO business date, then update timestamp, then stable PO-line identity. Every response exposes source kind, source document/date, PO, supplier, currency and unit provenance.

A Receipt-linked cost is still the linked PO-line price; v1 does not claim that it is an invoiced or final landed acquisition cost.

Missing leaf cost is never converted to zero. Mixed-currency BOMs retain one material total per currency and set an explicit mixed-currency indicator.

Obligation rows expose PO Line and Item identity for every Schedule-level component so identical Schedule Numbers on different PO Lines remain distinguishable. The PO-level Advance row deliberately leaves those fields null because it is allocated once across the active PO, not duplicated by line. Item and BOM provenance exposes both Persian and English names; clients apply the active-language fallback without changing stored source data.

## Explicit v1 exclusions

The following are not part of v1 and must not be inferred from the current read model:

- payment execution, approval, bank instruction or bank balance;
- supplier invoice, Accounts Payable, paid/open balance or settlement state;
- accounting, trading, settlement or ungoverned FX conversion beyond the approved USD reporting extension;
- freight, insurance, customs, tax or other landed-cost allocation;
- weighted-average, FIFO, standard or inventory-valuation cost;
- manual financial-date overrides;
- Cash Flow export;
- persisted scenarios, forecast publication or background materialization; and
- system-time historical replay.

Policies for rounding residuals, cancellation liabilities, paid advances, credit notes, partial settlement, fallback-estimate eligibility beyond the current v1 candidate set, and future organizational Data Scope remain deferred until explicitly approved.
