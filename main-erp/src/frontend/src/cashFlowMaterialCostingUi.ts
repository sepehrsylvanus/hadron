import type { ExactDecimal } from './cashFlowUi'

export type MaterialCostBookStatus = 'DRAFT' | 'PUBLISHED'
export type CostBasisMode = 'PURCHASE_HISTORY' | 'MATERIAL_COST_BOOK'

export type MaterialCostBookSummary = {
  bookId: string
  bookNumber: string
  revisionId: string
  revision: number
  nameFa: string
  nameEn?: string | null
  effectiveDate: string
  status: MaterialCostBookStatus
  supersedesRevisionId?: string | null
  lineCount: number
  createdAtUtc: string
  updatedAtUtc: string
  publishedAtUtc?: string | null
  version: number
}

export type MaterialCostBookPage = {
  items: MaterialCostBookSummary[]
  page: number
  pageSize: number
  totalCount: number
}

export type MaterialCostBookRevision = MaterialCostBookSummary & {
  clientRequestId: string
  changeReason?: string | null
  createdByIdentifier?: string | null
  updatedByIdentifier?: string | null
  publishedByIdentifier?: string | null
  publishReason?: string | null
}

export type MaterialCostBookPriceUnit = {
  unitId: string
  unitCode: string
  unitNameFa: string
  unitNameEn?: string | null
  unitSymbol?: string | null
  factorToBase: ExactDecimal
  isBaseUnit: boolean
}

export type MaterialCostBookItemOption = {
  itemId: string
  itemCode: string
  itemNameFa: string
  itemNameEn?: string | null
  baseUnitId: string
  baseUnitCode: string
  priceUnits: MaterialCostBookPriceUnit[]
}

export type MaterialCostBookItemOptionPage = {
  items: MaterialCostBookItemOption[]
  page: number
  pageSize: number
  totalCount: number
}

export type MaterialCostBookCurrencyOption = {
  currencyId: string
  currencyCode: string
  currencyNameFa: string
  currencyNameEn?: string | null
  symbol?: string | null
  decimalPrecision: number
}

export type MaterialCostBookSupplierOption = {
  supplierId: string
  supplierNumber: string
  supplierName: string
  isActive: boolean
}

export type MaterialCostBookSupplierOptionPage = {
  items: MaterialCostBookSupplierOption[]
  page: number
  pageSize: number
  totalCount: number
  selected?: MaterialCostBookSupplierOption | null
}

export type MaterialCostBookLine = {
  id: string
  itemId: string
  itemCode: string
  itemNameFa: string
  itemNameEn?: string | null
  currencyId: string
  currencyCode: string
  currencyNameFa: string
  currencyNameEn?: string | null
  currencyDecimalPrecision: number
  originalPriceAmount: ExactDecimal
  priceBasisQuantity: ExactDecimal
  priceUnitId: string
  priceUnitCode: string
  priceUnitNameFa: string
  priceUnitNameEn?: string | null
  priceUnitFactorToBase: ExactDecimal
  itemBaseUnitId: string
  itemBaseUnitCode: string
  itemBaseUnitNameFa: string
  itemBaseUnitNameEn?: string | null
  normalizedUnitPrice: ExactDecimal
  supplierId?: string | null
  supplierNumber?: string | null
  supplierName?: string | null
  sourceReference?: string | null
  notes?: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export type MaterialCostBookLinePage = {
  items: MaterialCostBookLine[]
  page: number
  pageSize: number
  totalCount: number
  revisionVersion: number
}

export type MaterialCostBookSelection = {
  bookId: string
  bookNumber: string
  revisionId: string
  revision: number
  nameFa: string
  nameEn?: string | null
  effectiveDate: string
  publishedAtUtc: string
  lineCount: number
}

export type PurchasePriceHistoryReceiptSummary = {
  postedReceiptCount: number
  postedQuantity: ExactDecimal
  quantityUnitId: string
  quantityUnitCode: string
  firstPostedReceiptDate?: string | null
  lastPostedReceiptDate?: string | null
}

export type PurchasePriceHistoryRow = {
  purchaseOrderLineId: string
  purchaseOrderId: string
  purchaseOrderNumber: string
  purchaseOrderStatus: string
  orderedDate: string
  lineNumber: number
  itemId: string
  itemCode: string
  itemNameFa: string
  itemNameEn?: string | null
  supplierId: string
  supplierNumber: string
  supplierName: string
  originalCurrencyId: string
  originalCurrencyCode: string
  originalCurrencyDecimalPrecision: number
  priceBaseUnitId: string
  priceBaseUnitCode: string
  priceBaseUnitNameFa: string
  priceBaseUnitNameEn?: string | null
  priceBaseUnitSymbol?: string | null
  orderedQuantity: ExactDecimal
  originalUnitPrice: ExactDecimal
  originalOrderAmount: ExactDecimal
  receipts: PurchasePriceHistoryReceiptSummary
  valuationUnitsPerUsd?: ExactDecimal | null
  valuationUnitPriceUsd?: ExactDecimal | null
  valuationOrderAmountUsd?: ExactDecimal | null
  valuationPostedReceiptAmountUsd?: ExactDecimal | null
}

export type PurchasePriceHistoryUsdContext = {
  requested: boolean
  purpose: string
  selectionMode: 'NONE' | 'EXACT_RATE_SET' | 'AS_OF_LATEST_PUBLISHED'
  valuationAsOfDate?: string | null
  rateSetId?: string | null
  rateSetEffectiveDate?: string | null
  rateSetRevision?: number | null
  status: 'NOT_REQUESTED' | 'COMPLETE' | 'INCOMPLETE' | 'MISSING_SET'
  missingCurrencyCodes: string[]
}

export type PurchasePriceHistoryPage = {
  items: PurchasePriceHistoryRow[]
  page: number
  pageSize: number
  totalCount: number
  generatedAtUtc: string
  dataSemantics: string
  usdValuationContext: PurchasePriceHistoryUsdContext
}

export type PurchasePriceHistoryItemOption = Pick<MaterialCostBookItemOption, 'itemId' | 'itemCode' | 'itemNameFa' | 'itemNameEn'>
export type PurchasePriceHistoryItemOptionPage = { items: PurchasePriceHistoryItemOption[]; page: number; pageSize: number; totalCount: number }
export type PurchasePriceHistorySupplierOption = Pick<MaterialCostBookSupplierOption, 'supplierId' | 'supplierNumber' | 'supplierName'>
export type PurchasePriceHistorySupplierOptionPage = { items: PurchasePriceHistorySupplierOption[]; page: number; pageSize: number; totalCount: number }
export type PurchasePriceHistoryFilterValues = { currencyCodes: string[]; purchaseOrderStatuses: string[] }

export function buildMaterialCostBookQuery(input: {
  page: number
  pageSize: number
  search?: string
  status?: MaterialCostBookStatus | ''
}) {
  const query = new URLSearchParams({ page: String(input.page), pageSize: String(input.pageSize) })
  if (input.search?.trim()) query.set('search', input.search.trim())
  if (input.status) query.set('status', input.status)
  return query
}

export function buildMaterialCostBookOptionsQuery(input: { page: number; pageSize: number; search?: string }) {
  const query = new URLSearchParams({ page: String(input.page), pageSize: String(input.pageSize) })
  if (input.search?.trim()) query.set('search', input.search.trim())
  return query
}

export function buildEffectiveMaterialCostBookSelectionsQuery(input: { asOfDate: string; page: number; pageSize: number; search?: string }) {
  const query = new URLSearchParams({ asOfDate: input.asOfDate, page: String(input.page), pageSize: String(input.pageSize) })
  if (input.search?.trim()) query.set('search', input.search.trim())
  return query
}

export function materialCostBookRevisionPath(revisionId: string, mode: 'view' | 'edit' = 'view') {
  const base = `/cash-flow/material-cost-books/revisions/${encodeURIComponent(revisionId)}`
  return mode === 'edit' ? `${base}/edit` : base
}

export function materialCostBookItemsPath(revisionId: string) {
  return `${materialCostBookRevisionPath(revisionId, 'edit')}#material-cost-book-items`
}

export function buildPurchasePriceHistoryQuery(input: {
  page: number
  pageSize: number
  search?: string
  itemId?: string
  supplierId?: string
  currencyCode?: string
  purchaseOrderStatus?: string
  fromOrderDate?: string
  toOrderDate?: string
  valuationFxRateSetId?: string
  valuationAsOfDate?: string
}) {
  const query = new URLSearchParams({ page: String(input.page), pageSize: String(input.pageSize) })
  for (const [key, raw] of Object.entries(input)) {
    if (key === 'page' || key === 'pageSize') continue
    const value = typeof raw === 'string' ? raw.trim() : ''
    if (value) query.set(key, value)
  }
  return query
}

/** Keep an active server-side filter visible and clearable while browsing another option page. */
export function preservePagedSelection<T extends { id: string }>(selected: string, options: T[], outsidePageOption: T): T[] {
  if (!selected || options.some(option => option.id === selected)) return options
  return [outsidePageOption, ...options]
}

export function normalizeCostBookDecimalInput(value: string) {
  const normalized = value
    .replace(/[۰-۹]/g, digit => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit)))
    .replace(/[٠-٩]/g, digit => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)))
    .replace(/٫/g, '.')
    .replace(/[٬,\u00a0\u202f\s]/g, '')
  return /^\.\d*$/.test(normalized) ? `0${normalized}` : normalized
}

export function isPositiveCostBookDecimal(value: string) {
  return /^(?:0|[1-9]\d*)(?:\.\d{1,12})?$/.test(value) && /[1-9]/.test(value) && integerDigits(value) <= 16
}

/** Never reinterpret a stored price through a different unit when its original unit is unavailable. */
export function findExactPriceUnit<T extends { unitId: string }>(priceUnits: T[], unitId: string) {
  return priceUnits.find(unit => unit.unitId === unitId)
}

/** Exact, display-only preview. Backend remains the authority for persisted normalization. */
export function previewNormalizedUnitPrice(amount: string, quantity: string, factor: string, fractionDigits = 8) {
  if (![amount, quantity, factor].every(isPositiveCostBookDecimal)) return null
  const a = decimalFraction(amount)
  const q = decimalFraction(quantity)
  const f = decimalFraction(factor)
  const numerator = a.numerator * q.denominator * f.denominator
  const denominator = a.denominator * q.numerator * f.numerator
  if (denominator === 0n) return null
  const scale = 10n ** BigInt(Math.max(0, Math.min(12, fractionDigits)))
  const rounded = (numerator * scale + denominator / 2n) / denominator
  const whole = rounded / scale
  const fraction = (rounded % scale).toString().padStart(Number(BigInt(Math.max(0, Math.min(12, fractionDigits)))), '0').replace(/0+$/, '')
  return fraction ? `${whole}.${fraction}` : whole.toString()
}

function integerDigits(value: string) {
  return (value.split('.')[0].replace(/^0+/, '') || '0').length
}

function decimalFraction(value: string) {
  const [whole, fraction = ''] = value.split('.')
  const denominator = 10n ** BigInt(fraction.length)
  return { numerator: BigInt(`${whole}${fraction}`), denominator }
}
