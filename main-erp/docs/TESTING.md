# Hadron ERP System — End-to-End Test Record

## Shayan Test scenario — 2026-09-05 / 1405-06-14

### Scope

This scenario was executed through the Persian production UI with a full-access human user. It covers Product Master, Component Master, multi-level BOM, domestic and import sourcing, manufacturing routing, Forecast and Actual Production, automatic MRP refresh, Order Planning, Purchase Order, Warehouse Receipt, Inventory Transfer, Production Order, backflush consumption and final inventory.

All scenario records are visibly named `Shayan Test` where they are newly owned by this test. Existing fictional suppliers were reused as alternate sourcing parties. No legacy A10 master or history was deleted.

### Test master data

| Area | Record | Purpose |
|---|---|---|
| Product | `PRD-0005` — Shayan Test - محصول نهایی | MAKE finished product, PCE |
| Part | `PRT-0022` — Shayan Test - بدنه پلیمری ساخت داخل | MAKE polymer part, PCE |
| Part | `PRT-0023` — Shayan Test - برد الکترونیکی خریدنی | BUY electronic part, PCE |
| Component | `CMP-0016` — Shayan Test - پلیمر ABS | BUY material, KG |
| Component | `CMP-0017` — Shayan Test - ماژول الکترونیکی | BUY component, PCE |
| Work center | `WC-0001` — Shayan Test - تزریق و مونتاژ | Saturday–Thursday production calendar |
| Staging warehouse | `WH-0006` — Shayan Test - انبار موقت تولید | Production-staging warehouse excluded from MRP usable stock |

### Approved engineering structure

- `BOM-000015`: `PRD-0005` → `PRT-0022` × 1 PCE + `PRT-0023` × 1 PCE.
- `BOM-000016`: `PRT-0022` → `CMP-0016` × 0.1 KG.
- `BOM-000017`: historical/engineering structure under BUY part `PRT-0023`; MRP correctly does not explode it while the parent remains BUY.
- `RTE-0002`: approved route for `PRD-0005`, 3 working days.
- `RTE-0001`: approved route for `PRT-0022`, 4 working days.

### Sourcing coverage

| Source | Item | Scope | Readiness | Key data |
|---|---|---|---|---|
| `SRC-0023` | `PRT-0023` | Domestic | READY, preferred | MOQ 2,000 PCE; 5 + 1 = 6 days |
| `SRC-0026` | `PRT-0023` | Import | READY | MOQ 200 PCE; 5 + 3 + 20 + 4 = 32 days |
| `SRC-0024` | `CMP-0016` | Domestic | READY, preferred | MOQ 100 KG; 2 + 1 = 3 days |
| `SRC-0025` | `CMP-0017` | Import | READY, preferred | MOQ 500 PCE; 7 + 2 + 10 + 3 = 22 days |

Legacy incomplete test source `SRC-0022` was deactivated instead of destroying history. Sourcing contains no price; price entered only with the expert procurement decision.

### Planning and execution evidence

1. Forecast plan `برنامه تست شایانن` contains 13 Jalali months from `140506` through `140606`. The current month is revision 2 at 350 PCE, equal to actual production to date; future months remain 1,000 PCE.
2. The existing actual on `1405/06/14` was deliberately corrected to 250 PCE with a required reason.
3. Automatic MRP refresh produced clean current run `MRP-RUN-000208` after the inactive-product planning fix and current-month forecast revision.
4. The Shayan Test product has 11,000 PCE forecast demand in the active horizon and three complete material rows.
5. `PRT-0022` detail is complete with zero calculation exceptions; its first shortage is `1405/07/23`.
6. `CMP-0016` first shortage is `1405/09/12`.
7. `PRT-0023` first shortage is `1405/10/22` after the warehouse receipt.
8. Order generation recommended 2,000 PCE for `PRT-0023` because the preferred domestic source MOQ is 2,000.
9. The accepted decision created `PO-000003`, 2,000 × 125,000 IRR. It was approved, ordered, fully received through `RCV-000003`, and posted into `WH-0001`.
10. `MO-000001` was approved, released with `BOM-000015`, staged through `ITR-000001` and `ITR-000002`, and completed with 100 good units and exact standard consumption of 100 PCE from each direct child.
11. The future production-date validation was deliberately triggered and correctly rejected. The report then succeeded on the current Tehran business date.

### Final inventory evidence

| Item | Warehouse | Quantity |
|---|---|---:|
| `PRD-0005` | `WH-0002` | 1,100 PCE |
| `PRT-0022` | `WH-0001` | 900 PCE |
| `PRT-0022` | `WH-0006` | 0 PCE |
| `PRT-0023` | `WH-0001` | 3,900 PCE |
| `PRT-0023` | `WH-0006` | 0 PCE |
| `CMP-0016` | `WH-0001` | 200 KG |
| `CMP-0017` | `WH-0001` | 2,000 PCE |

The monthly actual for `PRD-0005` is now 350 PCE: prior corrected actual 250 + production order report 100. Both staging transfers are fully received, in-transit inventory is zero, and backflush consumed the staging balances.

### Defects found and corrected

- Automatic MRP CURRENT-slot replacement now prunes manufacturing-operation input snapshots atomically.
- Domestic source conversion no longer crashes when import-only lead stages are intentionally null.
- Product and material master editors now refresh their local version and header after save.
- Purchase-order dates use Jalali input in Persian and Gregorian input in English.
- Purchase-order receipt, ETA, planned milestone and actual milestone dates now follow the active UI calendar.
- A failed production-report attempt is cleared before retry/open, so a successful report does not leave a stale critical message.
- The correct `PRODUCTION_STAGING` warehouse type is enforced for production staging; the legacy storage warehouse named “انبار موقت تولید” was not silently repurposed.
- Inactive products are now excluded from operational Forecast capture and MRP demand while their Forecast and master-data history remain intact.

### Automated verification

| Verification | Result |
|---|---:|
| Relevant backend integration suite | 163 passed, 0 failed |
| Full backend solution suite | 318 passed, 0 failed |
| Focused current `MrpRunTests` after inactive-product fix | 8 passed, 0 failed |
| Broad current MRP integration regression | 30 passed, 0 failed |
| Frontend tests | 100 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |
| `git diff --check` | Passed; only existing line-ending warnings |

### Current operational result

Legacy A10 products `PRD-0001` through `PRD-0004` were deactivated through the application UI rather than deleted. Their master data, BOM and Forecast history remain recoverable and auditable, but they are no longer part of operational planning. Current run `MRP-RUN-000208` is `COMPLETED`, `COMPLETE`, contains 27 persisted item rows, one forecasted product, three material rows and exactly zero calculation exceptions or warnings.
