# Cash Flow V1 Test Contract

## Gate

Cash Flow v1 is complete only when its arithmetic, lifecycle integration, authorization boundaries and missing-data behavior are covered by deterministic tests. Tests use exact decimal values, explicit Gregorian business dates, isolated PostgreSQL test databases and the existing `FoundationApiFactory` safety guard.

Recommended test files in `tests/backend/Hadron.Erp.IntegrationTests`:

- `CashFlowCalculatorTests.cs`
- `CashFlowTests.cs`
- `CashFlowAuthorizationTests.cs`
- `CostedBomTests.cs`

Frontend permission/routing and query construction remain covered in `CashFlowPage.test.ts` and `workspaceNavigation.test.ts`.

## CF-GD-01 — Independent installments

Input:

```text
PO value = 80 x 7.00 = 560 CNY
Advance = 20%
Before Shipment = 50%
Credit = 30%
Credit Days = 60 calendar days
Credit Anchor = WAREHOUSE_RECEIPT
Schedules = 30 and 50
```

Required literal amounts:

```text
Advance PO row = 112 CNY
Before Shipment Schedule 1 = 105 CNY
Before Shipment Schedule 2 = 175 CNY
Credit Schedule 1 = 63 CNY
Credit Schedule 2 = 105 CNY
Component totals = 112 / 280 / 168 CNY
All components = 560 CNY
```

Assertions:

- Advance uses Ordered Date.
- Each Before Shipment row uses its own `SUPPLIER_READY` resolution.
- Each Credit row uses its configured anchor plus 60 calendar days.
- A zero-percent component produces no row.
- Stable obligation keys and deterministic ordering prevent duplicate rows.

## CF-GD-02 — Unresolved anchor

One required Schedule has no Actual, Revised or Original `SUPPLIER_READY` date.

Assertions:

- its Before Shipment obligation remains present;
- expected-payment date is null, Date Source and State are `UNRESOLVED`;
- no current date, Requested Receipt Date or Sourcing date is substituted; and
- resolved Advance/Credit components for the same PO remain unchanged.

## CF-GD-03 — Planned versus committed

Assertions:

- Approved PO is absent when `includePlanned=false`;
- the same PO appears once as `PLANNED` when `includePlanned=true`;
- after `MarkOrdered`, it appears once as `COMMITTED` in both scopes; and
- dashboard, obligation list and pagination never contain planned and committed copies of the same source obligation.

## CF-GD-04 — Receipt completion and reversal

Reuse the canonical MRP GD-05 lifecycle:

- partial Receipt does not use First Receipt as the WAREHOUSE_RECEIPT Actual anchor;
- plan fallback remains independent from Actual completion;
- valid completion uses the final Posted, non-reversed Receipt date;
- completion by earlier Receipt plus later cancellation keeps the Receipt date, not Cancellation Date;
- reversal that removes completion removes the stale Actual anchor; and
- a later completing Receipt establishes the new Actual date.

Financial liability after cancellation or payment is not asserted until its deferred policy is approved.

## CF-GD-05 — Currency isolation

Input:

```text
100 USD
500 CNY
```

Assertions:

- dashboard, daily buckets and obligation list preserve two currencies;
- no value `600` or currencyless grand total is returned; and
- filtering one currency cannot change the other currency's source rows.

## COST-GD-01 — Receipt-linked price precedence

For one leaf Item, create two eligible Posted Receipts linked to immutable PO-line prices `7 CNY` and `8 CNY` on successive dates.

Assertions:

- the later Receipt selects `8 CNY`, kind `RECEIPT`, with full Receipt/PO/Supplier provenance;
- reversing that Receipt makes the earlier valid Receipt the source;
- an as-of date before the later Receipt also selects the earlier Receipt; and
- a newer Draft Receipt or non-positive line cannot become the source.

## COST-GD-02 — Fallback and missing cost

Assertions:

- without an eligible Receipt, the deterministic latest eligible PO Line is returned as `PURCHASE_ORDER_ESTIMATE`;
- a later ineligible Draft or Cancelled PO cannot win;
- identical business dates resolve stably by documented tie-breakers; and
- with no eligible source, Unit Price and Extended Cost remain null with `NO_VALID_PURCHASE_COST`; zero is forbidden.

## BOM-GD-01 — Multilevel quantity, UOM and scrap

Input:

```text
FG Base Output = 1 PCE
FG -> SUB = 2 PCE, Scrap = 10%
SUB Base Output = 2 PCE
SUB -> RAW = 3 KG, Scrap = 5%
RAW cost = 10 CNY/KG
```

Expected:

```text
SUB quantity = 2.2 PCE
RAW per SUB = (3 / 2) x 1.05 = 1.575 KG
RAW quantity = 2.2 x 1.575 = 3.465 KG
Material cost = 34.65 CNY
```

Assertions:

- recursion has no business fixed depth; the technical safety limit only guards corrupted graphs;
- Base Output normalization occurs on every level;
- each edge applies scrap once;
- null Scrap Override uses Item default and explicit zero remains zero;
- changing inventory balance does not change structural BOM cost;
- Cost Basis unit must equal the BOM Child Base Unit snapshot; mismatch is missing/incomplete, never implicitly converted; and
- cycle or multiple current Approved BOMs returns a stable conflict rather than a partial total.

## BOM-GD-02 — Incomplete and mixed-currency roll-up

Assertions:

- one missing leaf is listed with path/item/reason and does not contribute zero;
- other valid leaves remain traceable;
- one total is returned per original currency; and
- `HasMixedCurrencies=true` when more than one currency is present, without a combined total.

## Authorization matrix

For every Cash Flow endpoint:

- unauthenticated request returns `401`;
- authenticated request without the exact permission returns `403`;
- `cash_flow.dashboard.view` cannot open obligation or costing endpoints;
- `cash_flow.obligation.view` cannot open dashboard or costing endpoints;
- `cash_flow.costing.view` cannot open dashboard or obligation endpoints;
- direct Deny wins over ordinary Role/direct Grant;
- every endpoint has registered permission metadata and an unknown/missing permission fails closed; and
- each dashboard, obligation and costing permission independently protects its own read-only
  `GET .../resolve-jalali-date?jalaliDate=YYYYMMDD` helper; and
- projection/costing POST/PUT/PATCH/DELETE and export surfaces do not exist in v1. The only
  exemption is the bounded Reporting FX lifecycle: its documented POST/PUT routes require
  `cash_flow.reporting_rate.manage`, while its GET routes require
  `cash_flow.reporting_rate.view`; each route must explicitly prove unauthenticated `401` and
  authenticated-but-forbidden `403` behavior.

Frontend tests must prove route/navigation isolation, but UI hiding is never accepted as authorization evidence.

## Query and regression contract

- `asOfDate` and dashboard horizon are required and invalid ranges fail validation.
- Page sizes are bounded and ordering is stable across calls.
- Obligation source projection, filters, `COUNT`, ordering and `Skip/Take` execute in PostgreSQL;
  application memory may contain only the requested page, never an arbitrarily capped source set.
- List count and page are read in one `REPEATABLE READ` transaction so concurrent commits cannot make
  `TotalCount` disagree with the returned page snapshot.
- Dashboard currency totals, date buckets and unresolved count are grouped and counted in PostgreSQL from
  the same obligation projection and one `REPEATABLE READ` snapshot; client-side full-row aggregation is forbidden.
- A deterministic integration regression must exceed the former 25,000-schedule ceiling and prove that row
  25,001 remains countable/pageable and contributes to dashboard obligation and unresolved counts.
- A command-shape guard must observe server `COUNT`, `LIMIT/OFFSET` and `GROUP BY/SUM`, and reject reintroduction
  of the former 25k/100k/150k/250k literal source limits.
- Schedule-level obligations expose PO Line and bilingual Item provenance; PO-level Advance remains singular and has null Line/Item provenance.
- percentage arithmetic divides by `100m` before multiplying large schema-valid projected values, avoiding temporary decimal overflow.
- Costed-BOM list summaries use bounded memoized traversal, while detail expansion returns stable conflict code
  `COSTED_BOM_EXPANSION_LIMIT_EXCEEDED` before an excessive number of path rows can be allocated.
- Dashboard and obligation list use the same backend obligation rules.
- `GeneratedAtUtc` comes from the injected `TimeProvider` in deterministic tests.
- Jalali helpers accept exactly one normalized `YYYYMMDD` value, use the centralized planning calendar for conversion,
  and reject invalid dates without requiring an MRP-execution permission.
- legacy `/api/v1/analytics/commercial-commitments` is not mapped, and the generic Analytics export route rejects
  `commercial-commitments`; the retained `purchase-order-receipt` export is operational-only and must not expose
  amount, currency, payment-anchor or expected-credit-date columns. Legacy Analytics permissions cannot bypass
  Cash Flow isolation.
- Later source changes affect a new live query but never mutate PO, Receipt or BOM history.
- Existing MRP GD-02, GD-05, GD-09 and GD-10 suites must continue to pass.
- No test may assert accounting/trading FX, landed cost, weighted average, AP settlement, cancellation liability, export or payment execution until the corresponding deferred decision is approved. The bounded USD reporting-rate extension is governed by the tests below.

## FX-GD-01 — Owner-approved USD examples

Given one Published Reporting FX Rate Set:

```text
1 USD = 2,200,000 IRR
1 USD = 6.8 CNY
1 USD = 0.86 EUR
```

and three obligations whose original amounts equal those respective rates, the backend result is exactly `3 USD` before presentation formatting. Original amounts and currency codes remain unchanged.

## FX-GD-02 — Effective-date and revision selection

- select the latest Published Rate Set with `EffectiveDate <= asOfDate`;
- ignore Draft and future-dated sets;
- use the highest Published correction revision for the same Effective Date; and
- use that one set for every date in the requested horizon.

## FX-GD-03 — Missing-rate safety

For any monetary row with no USD rate:

- preserve original amount/currency;
- return row USD as null;
- return distinct missing currency codes;
- mark conversion incomplete; and
- return the affected aggregate USD total as null rather than a partial value.

Costed BOM tests must independently distinguish missing purchase cost from missing FX rate.

## FX-GD-04 — Rate-set lifecycle and security

Test at minimum:

- Draft creation/update and atomic Publish;
- immutable Published sets and correction Revision creation;
- USD implicit pass-through at one;
- positive exact-decimal rate constraint;
- duplicate-currency rejection;
- idempotent mutation retry;
- independent lost-response replay for Create, Update Draft, Publish and Correction, including
  changed-payload key reuse and superseded-result conflicts;
- optimistic-concurrency failure without partial update;
- transaction rollback when one row is invalid;
- business mutation and durable Audit event committed in one database transaction;
- publish completeness across every defined non-USD currency, including inactive currencies
  referenced by historical/open obligations;
- strict decimal-string input (JSON number rejected) with twelve-place exactness;
- bounded `FX_CONVERSION_OUT_OF_RANGE` behavior for source amount/rate combinations whose USD
  result exceeds the decimal range, without misclassifying the rate as missing;
- authorization for view versus manage;
- backend fail-closed permission metadata; and
- Audit actor, timestamp, correlation, old/new values and source.

## FX-GD-05 — UI and human workflow

Verify Persian/English and RTL/LTR behavior, Jalali input with smaller Gregorian display, keyboard navigation, stable focus while typing every digit, loading/empty/no-result/error states, Draft unsaved-change protection, Publish confirmation, default USD dashboard view, original-currency toggle, independent section retry and no misleading total when FX is incomplete.

## MCB-GD-01 — Exact price-unit normalization

- `5,500,000 IRR per 1 bag`, with the active Item-specific conversion `1 bag = 25 kg`, must normalize to exactly `220,000 IRR/kg` and value `0.014 kg` at exactly `3,080 IRR`.
- `100 USD per 1,000 pcs` must normalize to exactly `0.1 USD/pcs`.
- JSON numbers, zero/negative inputs, precision beyond `numeric(28,12)`, inactive units and a conversion belonging to another Item must be rejected without a partial write.

## MCB-GD-02 — Strict basis and completeness

- Purchase History remains the default Costed BOM basis.
- Material Cost Book requires one explicit Published revision eligible for the valuation date.
- Material Cost Book never falls back to Purchase History.
- Any purchased leaf missing from the selected revision remains null and makes the affected product USD total null; known lines may remain visible but are never presented as a complete total.
- The response exposes the selected basis and full line provenance.

## MCB-GD-03 — Lifecycle, security and audit

Test Draft create/update/line add-edit-remove, partial Publish, Published immutability, future-effective selection, linked correction, idempotent retry, changed-payload key reuse, optimistic concurrency, rollback and durable Audit. Independently prove View, Manage and Publish authorization, direct-API denial and fail-closed permission metadata.

## PPH-GD-01 — Purchase Price History row grain

One Purchase Order Line with multiple valid Posted receipts must return exactly one history row. Its receipt summary must aggregate only Posted, non-reversed receipts and expose their count, base quantity, first date and last date. Draft, unposted and reversed receipt facts must not contribute.

## PPH-GD-02 — Filters and USD valuation

Prove bounded server paging and independent Item, Supplier, order-date, currency, PO-status and literal text filters. One explicit Published FX Rate Set or one valuation As-Of Date must apply to every row. Original price/currency always remains visible; missing FX produces null USD fields and an explicit incomplete context. The UI must label USD as reporting valuation rather than transaction FX.
