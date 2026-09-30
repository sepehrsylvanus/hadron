# Hadron ERP — Material Cost Book and Purchase Price History

## Status and authority

Status: `OWNER APPROVED — IMPLEMENTATION BASELINE`

This document defines the Cash Flow-owned material-price basis used by Product Structure Costing and the separate read-only Purchase Price History. It extends `CASH-FLOW-V1.md` without moving price into Engineering/BOM, Sourcing, Catalog, Inventory or Supplier Master.

## Business boundary

Engineering/BOM owns only the product structure and exact consumption quantities. The Cash Flow workspace owns the controlled financial valuation of that structure.

Two price sources are deliberately separate:

1. `PURCHASE_HISTORY` reads actual procurement facts from immutable Purchase Order lines and valid posted receipts.
2. `MATERIAL_COST_BOOK` reads one explicitly selected Published Material Cost Book revision.

The two sources are never merged and Material Cost Book costing never silently falls back to Purchase History. A missing price produces an incomplete/null result, not zero and not a partial total presented as complete.

Neither source claims invoice, payment, accounting, weighted-average inventory valuation or final landed cost. Freight, clearance, insurance, tax and allocation remain separate future capabilities.

## Material Cost Book lifecycle

The lifecycle is:

```text
Draft -> Published (immutable) -> Correction Draft -> Published correction
```

- A book has a server-generated immutable business number.
- Each revision has a Persian name, optional English name, effective business date and status.
- Drafts do not affect product costing.
- A future-dated Published revision is visible but cannot be selected for a valuation date before its Effective Date.
- A Published revision cannot be edited or deleted. A correction creates a linked new revision.
- Partial coverage may be published because not every active Item necessarily needs a price. Products using an uncovered purchased leaf remain explicitly incomplete.
- Create, edit, line mutation, publish and correction use idempotency, optimistic concurrency and Audit.
- A user may publish only with the separate Publish permission. Holding Manage alone is insufficient.

## Price-line semantics

There is at most one price line per Item in a revision. Every line preserves the original commercial expression and the conversion facts used when it was entered:

- Item identity and display snapshots;
- original amount and original currency;
- Pricing Quantity;
- Price Unit;
- Item Base Unit snapshot;
- `FactorToBase` snapshot from the active Inventory Item Unit Conversion;
- optional Supplier provenance;
- optional source reference and note; and
- creation/update provenance.

`PriceAmount` means the price of `PricingQuantity` units of `PriceUnit`.

```text
BaseUnitPrice = PriceAmount / (PricingQuantity x FactorToBase)
MaterialCostOriginal = RequiredBaseQuantity x BaseUnitPrice
MaterialCostUsd = MaterialCostOriginal / UnitsPerUsd
```

All amounts, quantities, factors and rates use exact decimal values. The original amount and currency are always retained. USD conversion uses one explicit Published Reporting FX Rate Set selected for the report valuation date.

Example:

```text
PriceAmount = 5,500,000 IRR
PricingQuantity = 1 bag
FactorToBase = 25 kg/bag
BaseUnitPrice = 220,000 IRR/kg
RequiredBaseQuantity = 0.014 kg
MaterialCostOriginal = 3,080 IRR
```

Another valid expression is `100 USD per 1,000 pcs`, which normalizes to exactly `0.1 USD/pcs`.

Only an active, explicit conversion for that exact Item is accepted. Net Weight, a similarly named unit or a conversion belonging to another Item is never inferred. MOQ and order multiple do not change unit-price normalization.

Zero/free-of-charge price semantics are deferred. Until an explicit free-of-charge business state exists, Price Amount, Pricing Quantity and Factor To Base must all be greater than zero.

## Product Structure Costing selection

Product Structure Costing defaults to `PURCHASE_HISTORY` to preserve the existing contract. Selecting `MATERIAL_COST_BOOK` requires an explicit Published revision identifier. The selection is preserved in the URL/deep link and returned in the backend response with full provenance.

The current Approved BOM is recursively expanded according to the locked structural Costed BOM contract in CF-008. Each structural leaf must have a price line in the selected revision. This report is not MRP route-aware: it has neither Demand Date nor the explicit HYBRID route decision required to reproduce an MRP run. Route-aware valuation is deferred until an owner-approved contract supplies an explicit route selection or immutable MRP-run snapshot. The Material Cost Book changes valuation only and never changes the BOM or supply method.

## Purchase Price History

Purchase Price History is a read-only Cash Flow report sourced from Purchase Supply. It is not the Material Cost Book and it does not create or edit prices.

The row grain is exactly one Purchase Order line. This prevents a PO line with multiple receipts from being duplicated. Each row exposes:

- PO number, status, ordered date and line number;
- Item and Supplier;
- original unit price, currency and Item Base Unit;
- ordered quantity and original order amount;
- aggregate count and quantity of valid Posted, non-reversed receipts;
- first and last valid receipt dates; and
- optional USD comparison under one explicitly selected reporting-rate context.

Draft/unposted/reversed receipts do not contribute to the receipt summary. Filters, sorting and pagination are server-side and bounded. Supported filters include Item, Supplier, order-date range, currency and text search.

USD values are reporting valuations, not historical transaction FX. One explicit rate-set revision or one valuation As-Of Date applies to the entire result. If a required rate is missing, the original price remains visible and the USD fields remain null with an explicit incomplete context.

## Permissions

The capability uses independent sensitive permissions:

```text
cash_flow.material_cost_book.view
cash_flow.material_cost_book.manage
cash_flow.material_cost_book.publish
cash_flow.purchase_price_history.view
```

Manage authorizes Draft editing only. Publish is a separate action. Purchase Price History is read-only. None of these permissions grants Procurement, BOM, Inventory, Supplier or Reporting FX management authority.

## UI contract

Cash Flow groups these pages under **Item and Product Costing**:

- Product Structure Costing;
- Material Cost Book; and
- Purchase Price History.

Material Cost Book follows List Report -> Object Page. Editable financial inputs use the approved soft-yellow treatment; conversion and calculated values are read-only neutral fields. Every validation message is attached to its field. Published data is visibly read-only. The publish action confirms that partial coverage is allowed but affected product totals will remain incomplete.

Purchase Price History is a clean, server-paged table. Original price/currency is primary; optional USD is secondary and always labelled with the selected valuation date/rate revision. Persian business dates are shown first with the Gregorian date in smaller text, using the centralized calendar conversion.

## Required tests

At minimum, deterministic tests must cover:

- exact normalization for bag-to-kilogram and per-1,000-piece examples;
- exact original-currency and USD calculations;
- Item-specific conversion validation and inactive-conversion rejection;
- one line per Item per revision;
- partial publication and strict incomplete product totals;
- future-effective selection rules;
- immutable Published revisions and correction lineage;
- idempotent retries, optimistic concurrency, rollback and Audit;
- separate View, Manage and Publish authorization boundaries;
- one Purchase History row for a PO line with multiple Posted receipts;
- exclusion of Draft, unposted and reversed receipts;
- server-side Item, Supplier, date, currency and text filters;
- one explicit FX valuation context across all history rows;
- missing-FX null behavior; and
- Persian/English, RTL/LTR, keyboard, focus, loading, empty, filtered-empty, error and unsaved-change UI states.
