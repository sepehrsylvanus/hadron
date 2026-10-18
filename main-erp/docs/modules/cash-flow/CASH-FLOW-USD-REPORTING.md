# Hadron ERP — Cash Flow USD Reporting Extension

## Status and authority

Status: `OWNER-APPROVED — IMPLEMENTED AND VERIFIED`

This document extends the Cash Flow v1 contract with one deliberately bounded write capability and one reporting conversion. All Cash Flow v1 rules remain authoritative except where this document explicitly supersedes the original prohibition on FX conversion and Cash Flow-owned persistence.

The extension is a planning/reporting facility. It is not an accounting FX book, currency-trading rate, settlement rate, realized/unrealized gain calculation, bank rate or statutory ledger conversion.

The reporting-rate lifecycle, USD reporting projections and frontend workflow have completed their focused implementation verification. Production deployment and business UAT remain separate release activities.

## Approved business meaning

The reporting currency is USD. Every entered rate means:

```text
1 USD = UnitsPerUsd units of the row currency
USD Amount = Original Currency Amount / UnitsPerUsd
```

Examples:

```text
1 USD = 2,200,000 IRR
1 USD = 6.8 CNY
1 USD = 0.86 EUR
```

USD is implicit, immutable and equal to `1`. IRR always means rial; no implicit toman conversion is allowed.

## Rate-set lifecycle

- A rate set has a canonical Gregorian Effective Date; Jalali is input/display only.
- A Draft may be edited by an authorized user and never affects reports.
- Publishing is explicit and atomic: a report must never observe a partially updated set.
- A Published set is immutable and cannot be deleted.
- A correction creates a new revision linked to the superseded revision.
- A new effective date may copy the previous set for efficient data entry without mutating history.
- Every create, edit, publish and correction records actor, UTC timestamp, correlation ID and relevant changes in Audit in the same database transaction as the business mutation.
- Every mutating request requires a `ClientRequestId`, is idempotent for a lost-response retry and is protected against stale concurrent edits. Reusing a key for changed input is rejected; replaying an exact result after a later mutation returns `IDEMPOTENT_RESULT_SUPERSEDED` instead of misrepresenting the newer entity as the old result.

## Rate selection

For a Cash Flow request with an explicit `asOfDate`, the backend selects the latest Published rate set where:

```text
EffectiveDate <= asOfDate
```

The highest published revision wins for the same Effective Date. Future-dated sets are ignored. The selected set is applied as one flat planning-rate assumption across the entire requested future horizon. Future rate curves and period-specific FX scenarios are deferred.

There is no silent fallback to `1`, no client-side conversion and no implicit cross-rate calculation. USD itself passes through at `1`.

## Precision and aggregation

- Rates use exact decimal storage with explicit precision.
- Original monetary amounts and currencies remain unchanged and visible.
- The backend converts without row-level presentation rounding.
- The backend aggregates full-precision USD values and the UI displays USD with two decimal places.
- JSON transports financial decimals as invariant strings. Reporting-rate input rejects JSON number tokens so a JavaScript client cannot round a value before the backend receives it.
- A mathematically valid source amount/rate combination that exceeds the .NET decimal range returns the explicit `FX_CONVERSION_OUT_OF_RANGE` conflict; it is neither a server `500` nor reported as a missing rate.

## Completeness

Each response exposes a conversion context containing:

- reporting currency code `USD`;
- selected Rate Set ID, Effective Date and revision when available;
- completeness state: `COMPLETE`, `INCOMPLETE` or `MISSING_SET`; and
- distinct missing currency codes.

If any monetary row participating in a requested total lacks a rate:

- its original amount remains visible;
- its USD amount is `null`;
- the full USD total is `null`, not a partial number; and
- the UI identifies the missing currency codes and provides a path to rate maintenance when authorized.

For Costed BOM, missing purchase cost and missing FX rate are independent conditions. `MaterialCostUsd` is null when either condition makes the roll-up incomplete.

Publish requires one rate for every defined non-USD currency, including currencies that are now inactive. Inactivity prevents inappropriate new operational selection but does not invalidate historical/open monetary obligations. The currency option and rate response retain `IsActive` metadata so the UI can label an inactive historical currency without hiding it or dropping its rate. Correction copies every prior line, including inactive currencies.

## Reporting presentation

- Cash Flow Dashboard defaults to the management USD view.
- Original-currency view remains available for provenance and operational reconciliation.
- Dashboard USD KPIs cover overdue, 7-day, 30-day, 90-day and horizon exposure.
- Dashboard USD buckets use the same selected rate set as the KPIs.
- Obligation rows expose original amount/currency, Units Per USD and converted USD amount.
- The obligation-page USD total represents all filtered rows, not only the current page.
- Costed BOM preserves original cost-source currency and adds a complete USD material total when possible.
- Every USD surface shows the applied Rate Set Effective Date, revision, freshness and completeness.

## Ownership boundary

Cash Flow owns these values as `Reporting FX Rates`. They must not be consumed as the canonical accounting or trading rate by another module. If Hadron later requires enterprise-wide accounting, treasury or settlement rates, that capability must move behind an explicitly approved Finance Integration contract rather than silently reusing this planning assumption.

## Permissions

```text
cash_flow.reporting_rate.view
cash_flow.reporting_rate.manage
```

Dashboard, obligation and costing permissions allow their existing report surfaces to expose the rate context used by those calculations. They do not grant access to the rate-maintenance worklist or mutations. Manage includes Draft editing and Publish in this bounded phase; a separate Maker-Checker approval permission is deferred.

## Reporting-rate API

| Method and route | Contract | Permission |
|---|---|---|
| `GET /api/v1/cash-flow/reporting-rate-currencies` | Currency choices and implicit-USD marker | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets?page={n}&pageSize={n}&status={DRAFT\|PUBLISHED}` | Paged lifecycle worklist | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets/current?asOfDate=YYYY-MM-DD` | Current Published revision for the explicit date | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets/resolve-jalali-date?jalaliDate=YYYYMMDD` | Jalali input conversion only | `cash_flow.reporting_rate.view` |
| `GET /api/v1/cash-flow/reporting-rate-sets/{id}` | Exact detail and provenance | `cash_flow.reporting_rate.view` |
| `POST /api/v1/cash-flow/reporting-rate-sets` | Idempotent Draft create | `cash_flow.reporting_rate.manage` |
| `PUT /api/v1/cash-flow/reporting-rate-sets/{id}` | Idempotent atomic Draft replacement using `ClientRequestId` and `ExpectedVersion` | `cash_flow.reporting_rate.manage` |
| `POST /api/v1/cash-flow/reporting-rate-sets/{id}/publish` | Idempotent completeness validation and immutable Publish using `ClientRequestId` and `ExpectedVersion` | `cash_flow.reporting_rate.manage` |
| `POST /api/v1/cash-flow/reporting-rate-sets/{id}/corrections` | Idempotent linked correction Draft | `cash_flow.reporting_rate.manage` |

The create request contains `EffectiveDate`, `ClientRequestId`, optional `ChangeReason` and `Rates`. Each rate contains `CurrencyId` and exact invariant decimal-string `UnitsPerUsd`; a JSON number is invalid. Update requires `ClientRequestId`, `EffectiveDate`, `ExpectedVersion`, optional `ChangeReason` and the complete replacement `Rates`. Publish requires `ClientRequestId`, `ExpectedVersion` and `Reason`. Correction requires `ClientRequestId`, `ExpectedVersion` and `Reason`. Validation uses RFC 7807 field errors. Lifecycle conflicts return HTTP `409` with stable `code` and optional `details`. No delete route exists.

## Explicit exclusions

- accounting, tax or statutory conversion;
- currency purchase, allocation or settlement;
- bank balances and payment execution;
- realized or unrealized FX gain/loss;
- future FX curves, parity or no-arbitrage forecasts;
- rate-provider integration or automatic market-rate import;
- transaction-currency overwrite;
- landed-cost allocation; and
- Accounts Payable remaining-balance assertions.
