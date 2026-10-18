# Hadron ERP — MRP Golden Dataset v1.0 — FINAL

**Document Status:** FINAL / OWNER-APPROVED BASELINE  
**Purpose:** Deterministic numerical regression baseline for MRP-01 through MRP-14  
**Q-023 Status after propagation:** CLOSED  
**Data Type:** Synthetic test data only — never production data  
**Default Lead-Time Recommendation:** P80  
**Conservative Lead-Time Reference:** P90  
**Percentile Method:** continuous percentile equivalent to PostgreSQL `percentile_cont`

---

# 0. Golden Dataset Governance

This document is the approved numerical source of truth for MRP regression testing.

Rules:

1. All fixture inputs are fixed and synthetic.
2. All expected outputs are fixed.
3. Test code must not generate new expected values from the implementation under test.
4. A failing Golden test must not be "fixed" by changing the expected value unless the underlying business rule has formally changed.
5. Every intentional change to this file must be reviewed as a business-rule change.
6. No random values are permitted.
7. No live supplier, customer, product, inventory, PO or financial data may be used.
8. Calculations must use decimal arithmetic appropriate to the approved domain model.
9. Display rounding must never change authoritative quantities.
10. Gregorian is canonical storage; Jalali dates below represent business/UI dates and must be converted using the application's canonical calendar service.
11. Timezone is `Asia/Tehran`.
12. Current-period Remaining Forecast allocation begins strictly after the MRP Run As-Of date.
13. `WAREHOUSE_RECEIPT` Credit Anchor uses Final Receipt Date.
14. P80 is the default planning Lead-Time recommendation.
15. P90 is a conservative/risk reference only and does not automatically replace P80.

---

# 1. Golden Dataset Index

| Fixture | Purpose |
|---|---|
| GD-01 | Current-period Forecast, As-Of boundary, residual and period-end exception |
| GD-02 | Multi-level BOM, net-before-explode, shared material and Scrap |
| GD-03 | Firm Supply, overdue supply and ETA-missing supply |
| GD-04 | Source selection, Order By, Priority, Decision Horizon, MOQ and Recommendation Qty |
| GD-04B | Safety Stock sizing as planning floor |
| GD-05 | PO, split schedules, partial Receipt, cancellation, over-receipt, reversal and Credit Anchor |
| GD-06 | Lead-Time Analytics: Mean, Median/P50, P80, P90 and On-Time |
| GD-07 | Forecast Accuracy: Error, APE, WAPE and Bias |
| GD-08 | Full End-to-End regression from Forecast to repeated MRP Runs after PO/Receipt |
| GD-09 | Historical snapshot / drift protection |
| GD-10 | Currency isolation guard |

---

# GD-01 — Current-Period Forecast / As-Of Boundary

## 1.1 Normal Current-Period Case

Planning Period:

```text
1405/05
```

Period End:

```text
1405/05/31
```

MRP Run As-Of:

```text
1405/05/28
```

Official Current Forecast:

```text
100
```

Actual MTD through As-Of:

```text
40
```

Formula:

```text
Remaining Forecast
= MAX(Official Current Forecast - Actual MTD, 0)
= MAX(100 - 40, 0)
= 60
```

Eligible dates:

```text
date > 1405/05/28
AND
date <= 1405/05/31
```

Therefore eligible dates are:

```text
1405/05/29
1405/05/30
1405/05/31
```

Expected allocation:

| Date | Allocation |
|---|---:|
| 1405/05/28 | 0 |
| 1405/05/29 | 20 |
| 1405/05/30 | 20 |
| 1405/05/31 | 20 |

Exact assertion:

```text
SUM(Daily Remaining Forecast Allocation) = 60
```

Forbidden:

```text
Allocation on As-Of date
Spill into 1405/06
Silent discard
```

---

## 1.2 Decimal Residual Micro-Case

Input:

```text
Remaining Forecast = 10
Eligible Days = 3
```

The authoritative daily values may use high-precision decimals.

Required invariant:

```text
SUM(all 3 daily allocations) = exactly 10
```

The implementation must use one deterministic residual strategy.

The exact storage-scale strategy is to be implemented consistently by MRP-10A/B, but no unit may disappear due to display rounding.

---

## 1.3 Last Day of Jalali Period

Input:

```text
Planning Period = 1405/05
As-Of = 1405/05/31
Official Forecast = 100
Actual MTD = 90
Remaining Forecast = 10
```

Eligible future days inside the period:

```text
0
```

Expected:

```text
As-Of allocation = 0
Next-period spill = 0
Discarded quantity = 0
Unallocatable current-period remainder = 10
```

Expected explicit planning exception/state:

```text
CURRENT_PERIOD_REMAINDER_UNALLOCATABLE
```

If MRP-10A selects another stable machine code, the behavior above remains mandatory.

---

## 1.4 Actual Exceeds Forecast

Input:

```text
Official Forecast = 100
Actual MTD = 120
```

Expected:

```text
Remaining Forecast = 0
Future Remaining Forecast allocations = 0
```

---

# GD-02 — Multi-Level Netting, Shared Material and Scrap

All demand occurs on the same planning date.

## 2.1 Product Demand

```text
P1 = 10
P2 = 5
```

---

## 2.2 Structure

### Product P1

```text
P1
→ Part A = 1
```

Part A:

```text
Supply Method = MAKE
Starting Actual On Hand = 3
Safety Stock = 0
```

BOM:

```text
Part A
→ Component X = 2
   BOM Scrap Override = 10%

Part A
→ Component Y = 1
   Scrap = 0%
```

### Product P2

```text
P2
→ Part B = 1
```

Part B:

```text
Supply Method = HYBRID
MRP Run Route Decision = MAKE
Starting Actual On Hand = 0
Safety Stock = 0
```

BOM:

```text
Part B
→ Component X = 1
   BOM Scrap Override = NULL

Part B
→ Component Z = 2
   Scrap = 0%
```

### Component X

```text
Supply Method = BUY
Default Planning Scrap = 5%
Starting Actual On Hand = 8
Safety Stock = 0
Firm Supply = 0
```

### Component Y

```text
Supply Method = BUY
Starting Actual On Hand = 2
Safety Stock = 0
Firm Supply = 0
```

### Component Z

```text
Supply Method = BUY
Starting Actual On Hand = 12
Safety Stock = 0
Firm Supply = 0
```

---

## 2.3 Expected Parent Netting

Part A:

```text
Gross Demand = 10
Starting Stock = 3
Net MAKE Requirement = 7
```

Only `7`, not `10`, explodes to Part A children.

Part B:

```text
Gross Demand = 5
Starting Stock = 0
Net MAKE Requirement = 5
```

Only `5` explodes to Part B children.

---

## 2.4 Expected Component X Demand

Part A contribution:

```text
Base Requirement = 7 × 2 = 14
Scrap = 10%

Planned Requirement
= 14 × (1 + 10/100)
= 15.40
```

Part B contribution:

Because BOM line Scrap Override is `NULL`, X Item Default Scrap = 5% applies:

```text
Base Requirement = 5 × 1 = 5

Planned Requirement
= 5 × 1.05
= 5.25
```

Shared X Gross Demand:

```text
15.40 + 5.25 = 20.65
```

X Starting Stock:

```text
8
```

X Net External Requirement:

```text
20.65 - 8 = 12.65
```

Expected:

```text
X Gross Demand = 20.65
X Net Requirement = 12.65
```

Critical invariant:

```text
X inventory is netted ONCE
against aggregated X demand.
```

It must not be separately netted once for P1 and again for P2.

---

## 2.5 Expected Component Y

```text
Gross Demand = 7 × 1 = 7
Starting Stock = 2
Net External Requirement = 5
```

---

## 2.6 Expected Component Z

```text
Gross Demand = 5 × 2 = 10
Starting Stock = 12
Net External Requirement = 0
Projected Balance = 2
```

---

# GD-03 — Firm Supply, Overdue Supply and ETA Missing

Item:

```text
Q
Supply Method = BUY
Safety Stock = 0
```

MRP As-Of:

```text
1405/05/10
```

Starting Actual On Hand:

```text
50
```

Gross Demand:

| Date | Demand |
|---|---:|
| 1405/05/11 | 20 |
| 1405/05/12 | 20 |
| 1405/05/13 | 20 |
| 1405/05/14 | 20 |
| 1405/05/15 | 20 |
| 1405/05/16 | 20 |

Eligible timed Firm Supply:

```text
Qty = 15
ETA = 1405/05/12
Eligible Firm status
```

Overdue open Firm Supply:

```text
Qty = 10
ETA = 1405/05/09
```

ETA-missing Firm Supply:

```text
Qty = 8
ETA = NULL
```

Expected timed projection:

| Date | Firm Incoming | Demand | Projected On Hand |
|---|---:|---:|---:|
| 1405/05/11 | 0 | 20 | 30 |
| 1405/05/12 | 15 | 20 | 25 |
| 1405/05/13 | 0 | 20 | 5 |
| 1405/05/14 | 0 | 20 | -15 |
| 1405/05/15 | 0 | 20 | -35 |
| 1405/05/16 | 0 | 20 | -55 |

Expected:

```text
Expected Stockout Date = 1405/05/14
Overdue Firm Qty = 10
Firm Qty — ETA Missing = 8
```

Forbidden:

```text
Overdue 10 inserted into today's projected availability
ETA-missing 8 inserted into timed projected availability
```

---

# GD-04 — Procurement Recommendation / Source / MOQ

This fixture consumes the approved projected shortage from GD-03.

## 4.1 Planning Inputs

```text
MRP As-Of = 1405/05/10
Need By = 1405/05/14
Safety Stock = 0
Safe Start Days = 2
Critical Days = 2
```

Projected balance without hypothetical recommendation:

| Date | Projected On Hand |
|---|---:|
| 1405/05/14 | -15 |
| 1405/05/15 | -35 |
| 1405/05/16 | -55 |

---

## 4.2 Candidate Source S1

```text
Preferred = YES
Planning Ready = YES
Total Configured Lead Time = 5 calendar days
MOQ = 50
Unit Price = 1.00 USD
```

Earliest feasible receipt from As-Of:

```text
1405/05/15
```

Therefore:

```text
S1 cannot meet Need By = 1405/05/14
```

---

## 4.3 Candidate Source S2

```text
Preferred = NO
Planning Ready = YES
Total Configured Lead Time = 3 calendar days
MOQ = 80
Unit Price = 7.00 CNY
```

Earliest feasible receipt from As-Of:

```text
1405/05/13
```

Therefore S2 can meet Need By.

Expected selected Source:

```text
S2
```

Reason:

```text
Preferred source cannot meet Need By.
Select fastest valid on-time source.
```

Forbidden:

```text
Comparing 1.00 USD to 7.00 CNY as if the currencies were directly comparable.
```

---

## 4.4 Timing

Because selected S2 can meet Need By:

```text
Target Receipt = Need By = 1405/05/14
```

Order By:

```text
1405/05/14 - 3 calendar days
= 1405/05/11
```

Days Until Order By from As-Of:

```text
1
```

Decision Horizon:

```text
Target Receipt + Safe Start Days
= 1405/05/14 + 2
= 1405/05/16
```

Priority:

Rule:

```text
0 < Days Until Order By <= Critical Days
```

Therefore:

```text
0 < 1 <= 2
Priority = P2
Next Action = ORDER_BY_DATE
```

---

## 4.5 Recommendation Quantity

Minimum projected balance through Decision Horizon:

```text
-55
```

Planning Safety floor:

```text
0
```

Base Recommended Qty:

```text
0 - (-55) = 55
```

MOQ:

```text
80
```

System Recommended Qty:

```text
MAX(55, 80) = 80
```

Estimated Value:

```text
80 × 7.00 CNY
= 560 CNY
```

Expected:

```text
Selected Source = S2
Order By = 1405/05/11
Decision Horizon = 1405/05/16
Base Recommended Qty = 55
System Recommended Qty = 80
Estimated Value = 560 CNY
Priority = P2
Next Action = ORDER_BY_DATE
```

---

# GD-04B — Safety Stock Sizing Micro-Case

Use the GD-04 projected timeline but set:

```text
Safety Stock = 10
```

Minimum projected balance:

```text
-55
```

Base Recommended Qty:

```text
10 - (-55)
= 65
```

MOQ:

```text
80
```

System Recommended Qty:

```text
80
```

Critical invariant:

```text
Safety Stock is a planning floor.
It is NOT added repeatedly as daily Gross Demand.
```

---

# GD-05 — PO / Split Schedule / Receipt / Cancellation / Reversal

Accepted Recommendation:

```text
Expert Decision = ACCEPT
Final Source = S2
Final Qty = 80
Currency = CNY
Unit Price = 7.00
Target Receipt = 1405/05/14
Credit % = 30%
Credit Days = 60
Credit Anchor Event = WAREHOUSE_RECEIPT
```

Expected PO Line Total:

```text
80 × 7.00 = 560 CNY
```

---

## 5.1 Draft and Approved

Draft:

```text
Ordered Qty = 80
Firm Supply = 0
```

Approved:

```text
Firm Supply = 0
```

`APPROVED` is internal approval only.

---

## 5.2 Ordered

Ordered Date:

```text
1405/05/11
```

Expected:

```text
Firm Supply = 80
```

Default schedule begins as:

```text
80
```

Split into:

```text
Schedule A = 30
Current ETA = 1405/05/14

Schedule B = 50
Current ETA = 1405/05/16
```

Invariant:

```text
30 + 50 = 80
```

Firm Supply after split:

```text
80
```

No duplication.

---

## 5.3 Over-Receipt Guard

Attempt:

```text
Schedule A Open Qty = 30
Receipt Qty = 31
```

Expected:

```text
BLOCK
```

Tolerance:

```text
0
```

---

## 5.4 Partial Receipt

Post:

```text
Receipt R1
Schedule A
Qty = 20
Effective Receipt Date = 1405/05/14
Warehouse = WH-RAW
```

Expected:

```text
Inventory +20
Total Received = 20
Schedule A Open = 10
Schedule B Open = 50
Total Open = 60
Firm Supply Remaining = 60
PO Status = PARTIALLY_RECEIVED
Final Receipt Date = unresolved
WAREHOUSE_RECEIPT Credit Anchor = unresolved
```

First Receipt Date may exist for operational/logistics analytics, but it is not the financial Credit Anchor.

---

## 5.5 Cancellation

Cancel Schedule A remaining quantity:

```text
Cancelled Qty = 10
Reason = required
```

Expected:

```text
Received = 20
Cancelled = 10
Open = 50
Firm Supply Remaining = 50
PO Status = PARTIALLY_RECEIVED
```

Cancellation Date must not become a Warehouse Receipt Credit Anchor.

---

## 5.6 Final Receipt

Post:

```text
Receipt R2
Schedule B
Qty = 50
Effective Receipt Date = 1405/05/16
```

Expected:

```text
Total Received = 70
Total Cancelled = 10
Open = 0
Inventory = 70
Firm Supply Remaining = 0
PO Status = RECEIVED
Completion Outcome = RECEIVED_WITH_CANCELLATION
Final Receipt Date = 1405/05/16
WAREHOUSE_RECEIPT Credit Anchor = 1405/05/16
```

Credit Expected Date:

```text
1405/05/16 + 60 calendar days
= 1405/07/14
```

The date `1405/05/14` from R1 is not the financial anchor.

---

## 5.7 Receipt Reversal

Reverse R2.

Expected:

```text
Inventory = 20
Received = 20
Cancelled = 10
Open = 50
Firm Supply Remaining = 50
PO Status = PARTIALLY_RECEIVED
Final Receipt Date = unresolved
WAREHOUSE_RECEIPT Credit Anchor = unresolved
```

A stale Final Receipt/Credit Anchor must not remain.

---

## 5.8 Re-Completion

Post replacement:

```text
Receipt R3
Schedule B
Qty = 50
Effective Receipt Date = 1405/05/18
```

Expected:

```text
Inventory = 70
Received = 70
Cancelled = 10
Open = 0
Firm Supply Remaining = 0
PO Status = RECEIVED
Completion Outcome = RECEIVED_WITH_CANCELLATION
Final Receipt Date = 1405/05/18
WAREHOUSE_RECEIPT Credit Anchor = 1405/05/18
```

Credit Expected Date:

```text
1405/05/18 + 60 calendar days
= 1405/07/16
```

---

## 5.9 Fully Cancelled Micro-Case

Input:

```text
Ordered = 50
Received = 0
Cancelled = 50
Open = 0
```

Expected:

```text
Completion Outcome = FULLY_CANCELLED
Final Receipt Date = unresolved / none
WAREHOUSE_RECEIPT Credit Anchor = unresolved / none
```

Cancellation Date is not Receipt Date.

---

## 5.10 Completion by Cancellation after Prior Receipt Micro-Case

Input sequence:

```text
Ordered = 100
Posted Receipt = 70 on 1405/05/12
Remaining 30 cancelled on 1405/05/15
```

Expected:

```text
Received = 70
Cancelled = 30
Open = 0
Completion Outcome = RECEIVED_WITH_CANCELLATION
Final Receipt Date = 1405/05/12
WAREHOUSE_RECEIPT Credit Anchor = 1405/05/12
```

The later Cancellation Date `1405/05/15` does not replace the last valid Receipt date.

---

## 5.11 Negative-Stock Reversal Guard

Starting after a valid inventory balance:

```text
Inventory = 70
```

Post Issue:

```text
ISSUE = 60
```

Current Inventory:

```text
10
```

Attempt to reverse a 50-unit Receipt:

```text
10 - 50 = -40
```

Expected:

```text
BLOCK REVERSAL
```

Normal operation must not create negative Inventory.

---

# GD-06 — Lead-Time Analytics / P80 / P90

One exact Sourcing Option.

Five valid, fully completed, non-cancelled Delivery Schedule observations:

```text
10
12
14
16
21
calendar days
```

One additional cancelled Schedule exists and must be excluded from clean Lead-Time recommendation statistics.

Percentile definition:

```text
continuous percentile
equivalent to PostgreSQL percentile_cont
```

Sorted observations:

```text
10, 12, 14, 16, 21
```

---

## 6.1 Mean

```text
(10 + 12 + 14 + 16 + 21) / 5
= 73 / 5
= 14.6
```

Expected:

```text
Mean = 14.6 days
```

---

## 6.2 Median / P50

Middle value:

```text
14
```

Expected:

```text
Median = P50 = 14 days
```

---

## 6.3 P80

With continuous percentile:

```text
position = (n - 1) × 0.80
= 4 × 0.80
= 3.2
```

This lies 20% between the fourth and fifth values:

```text
16 + 0.2 × (21 - 16)
= 16 + 1
= 17
```

Expected:

```text
P80 = 17 days
```

---

## 6.4 P90

```text
position = (n - 1) × 0.90
= 4 × 0.90
= 3.6
```

This lies 60% between 16 and 21:

```text
16 + 0.6 × 5
= 19
```

Expected:

```text
P90 = 19 days
```

---

## 6.5 Lead-Time Policy

Approved semantic:

```text
P50 / Median
= typical historical performance

P80
= Default System Recommended Lead Time

P90
= Conservative / Risk Lead-Time Reference
```

P90 must be displayed/available in analytics but must not automatically replace P80.

---

## 6.6 Original Plan On-Time

Assume Original Planned Total Lead Time:

```text
14 days
```

Valid actual observations:

```text
10 → on time
12 → on time
14 → on time
16 → late
21 → late
```

Expected:

```text
On-Time Count = 3
Valid Sample Count = 5
Original Plan On-Time % = 3/5 = 60%
```

Expected statistical summary:

```text
Sample Count = 5
Mean = 14.6
Median/P50 = 14
P80 = 17
P90 = 19
Min = 10
Max = 21
Original Plan On-Time % = 60%
System Recommended Lead Time = 17
Conservative Reference = 19
```

Cancelled observation:

```text
excluded from Mean
excluded from Median
excluded from P80
excluded from P90
reported separately in Cancellation KPI
```

Applying the P80 recommendation requires explicit authorized audited action.

---

# GD-07 — Forecast Accuracy

Planning Period status:

```text
CLOSED
```

---

## 7.1 Product F1

```text
Baseline Forecast = 100
Final Forecast = 110
Actual = 105
```

Baseline:

```text
Error = 100 - 105 = -5
Absolute Error = 5
APE = 5 / 105
≈ 4.7619047619%
```

Final:

```text
Error = 110 - 105 = 5
Absolute Error = 5
APE = 5 / 105
≈ 4.7619047619%
```

---

## 7.2 Product F2

```text
Baseline Forecast = 200
Final Forecast = 180
Actual = 150
```

Baseline:

```text
Error = 50
Absolute Error = 50
APE = 50 / 150
≈ 33.3333333333%
```

Final:

```text
Error = 30
Absolute Error = 30
APE = 30 / 150
= 20%
```

---

## 7.3 Aggregate Baseline Metrics

Total Actual:

```text
105 + 150 = 255
```

Baseline Absolute Error:

```text
5 + 50 = 55
```

Baseline WAPE:

```text
55 / 255
= 0.2156862745098039
≈ 21.5686274510%
```

Baseline total signed Error:

```text
-5 + 50 = 45
```

Baseline Bias:

```text
45 / 255
= 0.1764705882352941
≈ 17.6470588235%
```

---

## 7.4 Aggregate Final Metrics

Final Absolute Error:

```text
5 + 30 = 35
```

Final WAPE:

```text
35 / 255
= 0.1372549019607843
≈ 13.7254901961%
```

Final signed Error:

```text
5 + 30 = 35
```

Final Bias:

```text
35 / 255
= 0.1372549019607843
≈ 13.7254901961%
```

---

## 7.5 Actual = 0

Input:

```text
Forecast = 10
Actual = 0
```

Expected:

```text
Error = 10
Absolute Error = 10
APE = N/A
```

No divide-by-zero and no fabricated percentage accuracy.

---

# GD-08 — Full End-to-End Regression

This fixture is mandatory.

Path:

```text
Forecast
→ Effective BOM
→ MRP Run R1
→ Shortage
→ Recommendation
→ Expert Decision
→ Draft PO
→ Approved
→ Ordered
→ Firm Supply
→ Actual Production / Inventory Issue
→ MRP Run R2
→ Partial Receipt
→ Remaining Firm Supply
→ MRP Run R3
→ Final Receipt
→ Inventory
→ MRP Run R4
→ Period Completion
```

---

## 8.1 Forecast

Planning Period:

```text
1405/05
```

Product:

```text
E-P1
```

Official Forecast:

```text
40
```

Run R1 As-Of:

```text
1405/05/27
```

Actual MTD:

```text
0
```

Remaining Forecast:

```text
40
```

Eligible days:

```text
1405/05/28
1405/05/29
1405/05/30
1405/05/31
```

Expected planned production:

```text
10/day
```

---

## 8.2 BOM

```text
E-P1
→ E-Part-A = 1
```

E-Part-A:

```text
Supply Method = MAKE
Starting On Hand = 0
Internal Process Lead Time = 0
```

BOM:

```text
E-Part-A
→ E-X = 2
```

E-X:

```text
Supply Method = BUY
Scrap = 0
Safety Stock = 0
Starting Actual Inventory = 30
Firm Supply = 0
```

Expected E-X Gross Demand:

```text
10 Product/day × 1 Part/Product × 2 X/Part
= 20 X/day
```

---

## 8.3 MRP Run R1

Starting E-X Inventory:

```text
30
```

Projection:

| Date | Demand | Firm Incoming | Projected E-X |
|---|---:|---:|---:|
| 1405/05/28 | 20 | 0 | 10 |
| 1405/05/29 | 20 | 0 | -10 |
| 1405/05/30 | 20 | 0 | -30 |
| 1405/05/31 | 20 | 0 | -50 |

Expected:

```text
Expected Stockout Date = 1405/05/29
Need By = 1405/05/29
```

---

## 8.4 Recommendation

Selected Sourcing Option:

```text
Planning Ready = YES
Total Lead Time = 1 calendar day
MOQ = 50
Critical Days = 1
Safe Start Days = 1
```

Target Receipt:

```text
1405/05/29
```

Order By:

```text
1405/05/28
```

Decision Horizon:

```text
1405/05/30
```

Projected balance without recommendation through Decision Horizon:

```text
05/29 = -10
05/30 = -30
```

Base Recommended Qty:

```text
0 - (-30) = 30
```

MOQ:

```text
50
```

System Recommended Qty:

```text
50
```

Days Until Order By from R1 As-Of:

```text
1
```

Expected:

```text
Priority = P2
Next Action = ORDER_BY_DATE
```

Expert Decision:

```text
ACCEPT
Final Qty = 50
```

---

## 8.5 PO Commitment

Draft PO:

```text
Qty = 50
Firm Supply = 0
```

Approved:

```text
Firm Supply = 0
```

Ordered:

```text
Ordered Date = 1405/05/28
Qty = 50
ETA = 1405/05/29
Firm Supply = 50
```

---

## 8.6 Actual Operation on 1405/05/28

Actual Production:

```text
10 E-P1
```

Material Inventory Issue:

```text
20 E-X
```

E-X Actual Inventory:

```text
30 - 20 = 10
```

---

## 8.7 MRP Run R2

As-Of:

```text
1405/05/28
```

Actual MTD:

```text
10
```

Remaining Forecast:

```text
40 - 10 = 30
```

As-Of day is excluded.

Future planned production:

```text
10 on 1405/05/29
10 on 1405/05/30
10 on 1405/05/31
```

E-X demand:

```text
20/day
```

Starting Actual Inventory:

```text
10
```

Firm Incoming:

```text
50 on 1405/05/29
```

Expected:

| Date | Demand | Firm Incoming | Projected E-X |
|---|---:|---:|---:|
| 1405/05/29 | 20 | 50 | 40 |
| 1405/05/30 | 20 | 0 | 20 |
| 1405/05/31 | 20 | 0 | 0 |

Expected:

```text
No stockout within current period
```

---

## 8.8 Operations on 1405/05/29

To avoid any transient negative-stock ambiguity, the Golden operational sequence is explicit:

### First — Post Partial Receipt

```text
Receipt Qty = 20
```

Inventory:

```text
10 + 20 = 30
```

PO:

```text
Received = 20
Open = 30
```

### Second — Post Actual Production Material Issue

Actual Production:

```text
10
```

Inventory Issue:

```text
20 E-X
```

End Actual Inventory:

```text
30 - 20 = 10
```

Remaining PO Firm Supply:

```text
30
```

Revised ETA for remaining schedule:

```text
1405/05/30
```

---

## 8.9 MRP Run R3

As-Of:

```text
1405/05/29
```

Actual MTD:

```text
20
```

Remaining Forecast:

```text
20
```

Eligible future production:

```text
10 on 1405/05/30
10 on 1405/05/31
```

Starting Actual Inventory:

```text
10
```

Firm Incoming:

```text
30 on 1405/05/30
```

Expected:

| Date | Demand | Firm Incoming | Projected E-X |
|---|---:|---:|---:|
| 1405/05/30 | 20 | 30 | 20 |
| 1405/05/31 | 20 | 0 | 0 |

---

## 8.10 Operations on 1405/05/30

Again the Golden operational sequence is explicit.

### First — Post Final Receipt

```text
Receipt Qty = 30
```

Inventory:

```text
10 + 30 = 40
```

PO:

```text
Total Received = 50
Open = 0
Firm Supply Remaining = 0
Status = RECEIVED
Final Receipt Date = 1405/05/30
```

### Second — Post Production Issue

Actual Production:

```text
10
```

Inventory Issue:

```text
20
```

End Actual Inventory:

```text
40 - 20 = 20
```

---

## 8.11 MRP Run R4

As-Of:

```text
1405/05/30
```

Actual MTD:

```text
30
```

Remaining Forecast:

```text
10
```

As-Of excluded.

Future production:

```text
10 on 1405/05/31
```

Starting Actual Inventory:

```text
20
```

Firm Supply:

```text
0
```

Expected:

| Date | Demand | Firm Incoming | Projected E-X |
|---|---:|---:|---:|
| 1405/05/31 | 20 | 0 | 0 |

Critical invariant:

```text
The fully received PO does NOT remain in Firm Supply.
```

---

## 8.12 Period Completion

On:

```text
1405/05/31
```

Actual Production:

```text
10
```

Inventory Issue:

```text
20
```

Expected end E-X Inventory:

```text
0
```

Final Product Actual for period:

```text
40
```

Official Forecast:

```text
40
```

Expected:

```text
Period Forecast = 40
Period Actual = 40
End material stock = 0
PO Firm Supply = 0
```

---

# GD-09 — Snapshot / Historical Drift Guard

At historical MRP Run time:

```text
Forecast Revision = F1
BOM Revision = B1
Starting Inventory Snapshot = I1
Firm Supply ETA Snapshot = D1
Engine Version = V1
```

After Run completion, current source data changes:

```text
Forecast Revision = F2
BOM Revision = B2
Current Inventory = I2
Current PO ETA = D2
```

Expected:

```text
Historical MRP Run input trace remains F1/B1/I1/D1/V1
Historical MRP Run output remains unchanged
```

---

## 9.1 Recommendation Snapshot

At Recommendation creation:

```text
Selected Source = S2
Unit Price Snapshot = 7.00 CNY
Recommended Qty = 80
Estimated Value = 560 CNY
```

Later current Sourcing price:

```text
8.00 CNY
```

Expected:

```text
Historical Recommendation Unit Price = 7.00 CNY
Historical Estimated Value = 560 CNY
```

No drift.

---

## 9.2 PO Snapshot

PO committed at:

```text
Unit Price = 7.00 CNY
Credit Anchor Event = WAREHOUSE_RECEIPT
```

Later Supplier Default Payment Profile or Sourcing price changes.

Expected:

```text
Committed PO commercial snapshot remains unchanged
```

---

# GD-10 — Currency Isolation Guard

Recommendation/PO A:

```text
100 USD
```

Recommendation/PO B:

```text
500 CNY
```

Expected report grouping:

```text
USD Total = 100 USD
CNY Total = 500 CNY
```

Forbidden:

```text
Grand Total = 600
```

No cross-currency arithmetic is allowed without a future approved FX layer.

---

# 2. Mandatory Golden Assertions

The automated Golden Regression Suite must prove all of the following:

## Forecast / Date

```text
As-Of day receives zero Remaining Forecast
Current-period Remaining Forecast starts next day
No cross-period spill
No quantity discarded due to residual rounding
Last-day positive remainder becomes explicit exception
```

## BOM / MRP

```text
Recursive BOM has no fixed depth
Parent stock is netted before child explosion
Shared material demand is aggregated before stock netting
Scrap is applied exactly once
NULL Scrap Override uses Item default
0 Scrap Override means explicit zero
```

## Inventory / Firm Supply

```text
Actual Inventory is not an editable balance field
Overdue open supply does not enter timed projection
ETA-missing supply does not enter timed projection
Approved-only PO is not Firm Supply
Ordered open schedule qty is Firm Supply
Received Qty leaves Firm Supply and enters Inventory
Cancelled Qty leaves Firm Supply
Split schedules never duplicate quantity
```

## Recommendation

```text
Preferred source is not blindly selected if late
On-time source selection follows approved policy
Cross-currency cheapest comparison is prohibited
Order By is deterministic
Decision Horizon is deterministic
Safety Stock is a planning floor
MOQ applies after Base Recommended Qty
Risk is not Procurement Priority
```

## PO / Receipt

```text
Over-Receipt tolerance is zero
Receipt posts PO + Inventory atomically
Partial Receipt supported
Final Receipt derived from valid Posted non-reversed Receipts
WAREHOUSE_RECEIPT Credit Anchor = Final Receipt
Cancellation Date is not Receipt Date
Receipt reversal removes stale final anchor when completion is lost
Negative-stock reversal is blocked
```

## Analytics

```text
Mean/Median/P80/P90 deterministic
P80 is default recommended Lead Time
P90 is conservative reference
Cancelled schedules excluded from clean percentile sample
Original-plan On-Time remains distinct from revised-plan performance
```

## Forecast Accuracy

```text
Only Closed Periods are final accuracy samples
APE is N/A when Actual = 0
WAPE uses aggregate actual denominator
Bias preserves signed error
```

## History

```text
Historical MRP Run does not drift
Historical Recommendation does not drift
Committed PO snapshot does not drift
```

## Currency

```text
Original currency preserved
No cross-currency grand total
```

---

# 3. Recheck / Mathematical Verification

The following values were independently rechecked before this document was marked FINAL:

| Check | Expected | Rechecked |
|---|---:|---:|
| GD-02 Part A Net MAKE | 7 | PASS |
| GD-02 X from Part A | 15.40 | PASS |
| GD-02 X from Part B | 5.25 | PASS |
| GD-02 Shared X Gross | 20.65 | PASS |
| GD-02 X Net Requirement | 12.65 | PASS |
| GD-03 projection final | -55 | PASS |
| GD-04 Base Recommendation | 55 | PASS |
| GD-04 MOQ Recommendation | 80 | PASS |
| GD-04 Estimated Value | 560 CNY | PASS |
| GD-04B Safety-stock Base Qty | 65 | PASS |
| GD-06 Mean | 14.6 | PASS |
| GD-06 Median/P50 | 14 | PASS |
| GD-06 P80 | 17 | PASS |
| GD-06 P90 | 19 | PASS |
| GD-06 Original On-Time | 60% | PASS |
| GD-07 Baseline WAPE | 21.5686274510% | PASS |
| GD-07 Baseline Bias | 17.6470588235% | PASS |
| GD-07 Final WAPE | 13.7254901961% | PASS |
| GD-07 Final Bias | 13.7254901961% | PASS |
| GD-08 R1 Base Recommendation | 30 | PASS |
| GD-08 R1 MOQ Recommendation | 50 | PASS |
| GD-08 R2 end projection | 0 | PASS |
| GD-08 R3 end projection | 0 | PASS |
| GD-08 R4 end projection | 0 | PASS |
| GD-08 final physical Inventory | 0 | PASS |

---

# 4. Final Approved Lead-Time Statistical Policy

The baseline Lead-Time Analytics policy is:

```text
Sample Count
Mean
Median / P50
P80
P90
Min
Max
Original Plan On-Time %
Revised Plan Performance
Cancellation KPI
```

Interpretation:

```text
P50 / Median = Typical historical performance

P80 = Default System Recommended Lead Time

P90 = Conservative / Risk Lead-Time Reference
```

Rules:

```text
P90 does not automatically replace P80.
Analytics never silently modifies Sourcing.
Applying a Lead-Time recommendation requires an explicit authorized audited action.
```

Sample eligibility/window rules remain:

```text
Primary window = last 12 months
If valid samples < 5 → expand to last 24 months
If still < 5 → INSUFFICIENT_SAMPLE
```

---

# 5. Final Approval Statement

This Golden Dataset v1.0 is the approved baseline for implementing MRP regression tests.

After this document is propagated into the canonical MRP documentation:

```text
Q-023 — Golden Dataset numeric approval
```

must be marked:

```text
CLOSED
```

Codex must not invent replacement numbers for these fixtures.

Any future change to an expected output requires:

```text
Business Rule Review
→ Decision Update
→ Golden Dataset Version Change
→ Regression Update
```

The next version, if ever needed, must be explicitly versioned rather than silently overwriting the meaning of v1.0.
