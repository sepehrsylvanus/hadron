export type CashFlowScope = 'COMMITTED' | 'ALL'
export type CashFlowDateSurface = 'dashboard' | 'obligations' | 'costed-boms' | 'reporting-rates' | 'material-cost-books' | 'purchase-price-history'
export type CashObligationStage = 'ADVANCE' | 'BEFORE_SHIPMENT' | 'CREDIT'
export type CashObligationConfidence = 'COMMITTED' | 'PLANNED'
export type CashObligationDateSource = 'ACTUAL' | 'REVISED' | 'ORIGINAL' | 'ORDERED' | 'UNRESOLVED'
export type CashObligationState = 'OVERDUE' | 'DUE_SOON' | 'SCHEDULED' | 'UNRESOLVED'
export type CostBasisKind = 'RECEIPT' | 'PURCHASE_ORDER_ESTIMATE' | 'MATERIAL_COST_BOOK' | 'MISSING'
export type CostBasisMode = 'PURCHASE_HISTORY' | 'MATERIAL_COST_BOOK'
export type ExactDecimal = string
export type CashFlowCurrencyView = 'USD' | 'ORIGINAL'
export type FxRateSetStatus = 'DRAFT' | 'PUBLISHED'
export type FxConversionCompleteness = 'COMPLETE' | 'INCOMPLETE' | 'MISSING_SET'

export type FxConversionContext = {
  reportingCurrencyCode: 'USD'
  rateSetId: string | null
  effectiveDate: string | null
  revision: number | null
  completeness: FxConversionCompleteness
  missingCurrencyCodes: string[]
}

export type CashUsdSummary = {
  overdueAmount: ExactDecimal | null
  due7DaysAmount: ExactDecimal | null
  due30DaysAmount: ExactDecimal | null
  due90DaysAmount: ExactDecimal | null
  horizonAmount: ExactDecimal | null
}

export type CashUsdBucket = {
  date: string
  amount: ExactDecimal | null
  count: number
}

export type CashFlowRequestError = {
  status: number | null
  kind: 'validation' | 'authentication' | 'not-found' | 'forbidden' | 'conflict' | 'server'
  code: string
  fieldErrors: Record<string, string[]>
}

export class CashFlowApiError extends Error {
  readonly requestError: CashFlowRequestError

  constructor(requestError: CashFlowRequestError) {
    super(requestError.code)
    this.name = 'CashFlowApiError'
    this.requestError = requestError
  }
}

export type CashCurrencySummary = {
  currencyId: string
  currencyCode: string
  decimalPrecision: number
  overdueAmount: ExactDecimal
  due7DaysAmount: ExactDecimal
  due30DaysAmount: ExactDecimal
  due90DaysAmount: ExactDecimal
  horizonAmount: ExactDecimal
  obligationCount: number
}

export type CashFlowBucket = {
  date: string
  currencyId: string
  currencyCode: string
  amount: ExactDecimal
  count: number
}

export type CashFlowDashboard = {
  asOfDate: string
  horizonEnd: string
  includePlanned: boolean
  generatedAtUtc: string
  currencies: CashCurrencySummary[]
  buckets: CashFlowBucket[]
  unresolvedCount: number
  fxConversionContext: FxConversionContext
  usdSummary: CashUsdSummary
  usdBuckets: CashUsdBucket[]
}

export type CashObligation = {
  obligationKey: string
  expectedPaymentDate: string | null
  purchaseOrderId: string
  purchaseOrderNumber: string
  purchaseOrderStatus: string
  purchaseOrderLineId: string | null
  purchaseOrderLineNumber: number | null
  itemId: string | null
  itemCode: string | null
  itemNameFa: string | null
  itemNameEn: string | null
  supplierId: string
  supplierNumber: string
  supplierName: string
  currencyId: string
  currencyCode: string
  currencyDecimalPrecision: number
  projectedAmount: ExactDecimal
  unitsPerUsd: ExactDecimal | null
  projectedAmountUsd: ExactDecimal | null
  stage: CashObligationStage
  confidence: CashObligationConfidence
  dateSource: CashObligationDateSource
  state: CashObligationState
  anchorCode: string
  deliveryScheduleId: string | null
  scheduleNumber: number | null
}

export type CashObligationPage = {
  items: CashObligation[]
  page: number
  pageSize: number
  totalCount: number
  asOfDate: string
  generatedAtUtc: string
  fxConversionContext: FxConversionContext
  totalUsdAmount: ExactDecimal | null
}

export type CostCurrencyTotal = {
  currencyId: string
  currencyCode: string
  decimalPrecision: number
  materialCost: ExactDecimal
  unitsPerUsd: ExactDecimal | null
  materialCostUsd: ExactDecimal | null
}

export type CostedBomSummary = {
  parentItemId: string
  parentItemCode: string
  parentItemNameFa: string
  parentItemNameEn?: string | null
  bomRevisionId: string
  bomRevisionCode: string
  costedOutputQuantity: ExactDecimal
  outputUnitId: string
  outputUnitCode: string
  asOfDate: string
  costBasisSelection: CostBasisSelectionContext
  currencyTotals: CostCurrencyTotal[]
  materialCostUsd: ExactDecimal | null
  missingCostCount: number
  hasMixedCurrencies: boolean
}

export type CostedBomPage = {
  items: CostedBomSummary[]
  page: number
  pageSize: number
  totalCount: number
  asOfDate: string
  generatedAtUtc: string
  valuationSemantics: string
  costBasisSelection: CostBasisSelectionContext
  fxConversionContext: FxConversionContext
}

export type CostBasisSelectionContext = {
  mode: CostBasisMode
  traversalSemantics: string
  priceBookId?: string | null
  priceBookNumber?: string | null
  priceBookRevisionId?: string | null
  priceBookRevision?: number | null
  priceBookNameFa?: string | null
  priceBookNameEn?: string | null
  priceBookEffectiveDate?: string | null
  priceBookPublishedAtUtc?: string | null
}

export type CostBasis = {
  kind: CostBasisKind
  unitPrice?: ExactDecimal | null
  currencyId?: string | null
  currencyCode?: string | null
  currencyDecimalPrecision?: number | null
  unitId?: string | null
  unitCode?: string | null
  sourceDocumentId?: string | null
  sourceDocumentNumber?: string | null
  sourceDate?: string | null
  purchaseOrderId?: string | null
  purchaseOrderNumber?: string | null
  supplierId?: string | null
  supplierNumber?: string | null
  supplierName?: string | null
  missingReason?: string | null
  originalPriceAmount?: ExactDecimal | null
  priceBasisQuantity?: ExactDecimal | null
  priceUnitId?: string | null
  priceUnitCode?: string | null
  priceUnitFactorToBase?: ExactDecimal | null
  priceBookRevisionId?: string | null
}

export type CostedBomLine = {
  path: string
  level: number
  isLeaf: boolean
  itemId: string
  itemCode: string
  itemNameFa: string
  itemNameEn?: string | null
  quantity: ExactDecimal
  unitId: string
  unitCode: string
  scrapPercent: ExactDecimal
  costBasis?: CostBasis | null
  extendedCost?: ExactDecimal | null
  extendedCostUsd: ExactDecimal | null
}

export type MissingCost = {
  itemId: string
  itemCode: string
  itemNameFa: string
  itemNameEn?: string | null
  path?: string | null
  reason: string
}

export type CostedBomDetail = {
  parentItemId: string
  parentItemCode: string
  parentItemNameFa: string
  parentItemNameEn?: string | null
  bomRevisionId: string
  bomRevisionCode: string
  costedOutputQuantity: ExactDecimal
  outputUnitId: string
  outputUnitCode: string
  asOfDate: string
  generatedAtUtc: string
  valuationSemantics: string
  costBasisSelection: CostBasisSelectionContext
  fxConversionContext: FxConversionContext
  materialCostUsd: ExactDecimal | null
  lines: CostedBomLine[]
  currencyTotals: CostCurrencyTotal[]
  missingCosts: MissingCost[]
  hasMixedCurrencies: boolean
}

export type ReportingRateCurrency = {
  id: string
  code: string
  nameFa: string
  nameEn?: string | null
  isActive: boolean
  isBaseCurrency: boolean
}

export type ReportingFxRate = {
  currencyId: string
  currencyCode: string
  currencyNameFa: string
  currencyNameEn?: string | null
  currencyIsActive: boolean
  unitsPerUsd: ExactDecimal
}

export type ReportingRateSet = {
  id: string
  baseCurrencyId: string
  baseCurrencyCode: 'USD'
  effectiveDate: string
  revision: number
  status: FxRateSetStatus
  supersedesRateSetId?: string | null
  clientRequestId: string
  changeReason?: string | null
  createdByIdentifier?: string | null
  createdAtUtc: string
  updatedAtUtc: string
  publishedByIdentifier?: string | null
  publishedAtUtc?: string | null
  publishReason?: string | null
  version: number
  rates: ReportingFxRate[]
}

export type ReportingRateSetSummary = {
  id: string
  baseCurrencyId: string
  baseCurrencyCode: string
  effectiveDate: string
  revision: number
  status: FxRateSetStatus
  supersedesRateSetId?: string | null
  createdAtUtc: string
  updatedAtUtc: string
  publishedAtUtc?: string | null
  rateCount: number
  version: number
}

export type ReportingRateSetPage = {
  items: ReportingRateSetSummary[]
  page: number
  pageSize: number
  totalCount: number
}

export type StableClientRequestAttempt = {
  clientRequestId: string
  payloadSignature: string
}

export type IndependentSectionResult<T> = { data: T; error: null } | { data: null; error: CashFlowRequestError }

export function cashFlowJalaliResolverPath(surface: CashFlowDateSurface) {
  if (surface === 'reporting-rates') return '/v1/cash-flow/reporting-rate-sets/resolve-jalali-date'
  return `/v1/cash-flow/${surface}/resolve-jalali-date`
}

export function buildReportingRateSetQuery(input: { page: number; pageSize: number; status?: FxRateSetStatus | '' }) {
  const query = new URLSearchParams({ page: String(input.page), pageSize: String(input.pageSize) })
  if (input.status) query.set('status', input.status)
  return query
}

export function reportingRateSetPath(id: string, mode: 'view' | 'edit' = 'view') {
  const base = `/cash-flow/reporting-rates/${encodeURIComponent(id)}`
  return mode === 'edit' ? `${base}/edit` : base
}

export function reportingRateDisplayCount(rateCount: number) {
  return Math.max(0, Math.trunc(rateCount)) + 1
}

export function reportingRateEffectiveDateIsLocked(supersedesRateSetId: string | null | undefined) {
  return Boolean(supersedesRateSetId)
}

export function isExactDecimalInput(value: string) {
  const match = value.match(/^(\d+)(?:\.(\d+))?$/)
  if (!match || !/[1-9]/.test(value)) return false
  const integerDigits = match[1].replace(/^0+/, '') || '0'
  return integerDigits.length <= 16 && (match[2]?.length ?? 0) <= 12
}

export function normalizeReportingRateInput(value: string) {
  const normalized = value
    .replace(/[۰-۹]/g, digit => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit)))
    .replace(/[٠-٩]/g, digit => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)))
    .replace(/٫/g, '.')
    .replace(/[٬,\u00a0\u202f\s]/g, '')
  return /^\.\d*$/.test(normalized) ? `0${normalized}` : normalized
}

export function stableClientRequestAttempt(
  previous: StableClientRequestAttempt | null,
  payloadSignature: string,
  createClientRequestId: () => string,
) {
  if (previous?.payloadSignature === payloadSignature) return previous
  return { clientRequestId: createClientRequestId(), payloadSignature }
}

export function retainClientRequestAttempt(error: CashFlowRequestError) {
  return error.status == null || error.status >= 500
}

export function mergeCopiedReportingRateValues(
  currentValues: Record<string, string>,
  editableCurrencyIds: string[],
  copiedRates: Array<Pick<ReportingFxRate, 'currencyId' | 'unitsPerUsd'>>,
) {
  const next = { ...currentValues }
  const copied = new Map(copiedRates.map(rate => [rate.currencyId, rate.unitsPerUsd]))
  for (const currencyId of editableCurrencyIds) {
    const copiedValue = copied.get(currencyId)
    if (copiedValue != null) next[currencyId] = copiedValue
  }
  return next
}

export function reportingRateCurrenciesForEditor(currencies: ReportingRateCurrency[]) {
  return [...currencies]
    .sort((left, right) => Number(right.isBaseCurrency) - Number(left.isBaseCurrency) || left.code.localeCompare(right.code))
}

export function reportingRateMissingCurrencies(
  currencies: ReportingRateCurrency[],
  rates: Array<Pick<ReportingFxRate, 'currencyId'>>,
) {
  const ratedCurrencyIds = new Set(rates.map(rate => rate.currencyId))
  return reportingRateCurrenciesForEditor(currencies)
    .filter(currency => !currency.isBaseCurrency && !ratedCurrencyIds.has(currency.id))
}

export function reportingRateServerInvalidIds(
  error: CashFlowRequestError | null,
  submittedCurrencyIds: string[],
  editableCurrencyIds: string[],
  values: Record<string, string>,
) {
  const ids = new Set<string>()
  if (!error) return ids
  const fieldKeys = Object.keys(error.fieldErrors)
  if (fieldKeys.some(key => key.toLowerCase() === 'rates' || key.toLowerCase().startsWith('rates.'))) {
    const blankCurrencyIds = editableCurrencyIds.filter(currencyId => !values[currencyId]?.trim())
    for (const currencyId of blankCurrencyIds.length ? blankCurrencyIds : submittedCurrencyIds) ids.add(currencyId)
  }
  for (const key of fieldKeys) {
    const match = key.match(/^rates\[(\d+)]\.unitsPerUsd$/i)
    if (match) {
      const id = submittedCurrencyIds[Number(match[1])]
      if (id) ids.add(id)
    }
  }
  return ids
}

export function canLoadDashboardObligations(hasPermission: (permission: string) => boolean) {
  return hasPermission('cash_flow.obligation.view')
}

export function cashFlowDashboardRequestPaths(hasPermission: (permission: string) => boolean) {
  return ['/dashboard', ...(canLoadDashboardObligations(hasPermission) ? ['/obligations'] : [])]
}

export async function loadIndependentCashFlowSection<T>(request: Promise<T>): Promise<IndependentSectionResult<T>> {
  try { return { data: await request, error: null } }
  catch (error) { return { data: null, error: cashFlowRequestError(error) } }
}

export async function readCashFlowApiError(response: Response) {
  const raw = await response.text().catch(() => '')
  let body: Record<string, unknown> = {}
  if (raw.trim()) {
    try {
      const parsed: unknown = JSON.parse(raw)
      if (isRecord(parsed)) body = parsed
    } catch {
      // A non-JSON gateway response remains a service error; do not surface its raw body to the user.
    }
  }

  const fieldErrors = normalizeProblemErrors(body.errors)
  const isValidation = response.status === 400 || response.status === 422 || Object.keys(fieldErrors).length > 0
  const kind: CashFlowRequestError['kind'] = isValidation ? 'validation'
    : response.status === 401 ? 'authentication'
      : response.status === 404 ? 'not-found'
        : response.status === 403 ? 'forbidden'
          : response.status === 409 ? 'conflict'
            : 'server'
  const suppliedCode = typeof body.code === 'string' ? body.code.trim() : ''
  const code = suppliedCode || (kind === 'validation' ? 'CASH_FLOW_VALIDATION_FAILED'
    : kind === 'authentication' ? 'CASH_FLOW_AUTHENTICATION_REQUIRED'
      : kind === 'not-found' ? 'CASH_FLOW_NOT_FOUND'
        : kind === 'forbidden' ? 'CASH_FLOW_FORBIDDEN'
          : kind === 'conflict' ? 'CASH_FLOW_CONFLICT'
            : 'CASH_FLOW_LOAD_FAILED')

  return new CashFlowApiError({ status: response.status, kind, code, fieldErrors })
}

export function cashFlowRequestError(error: unknown): CashFlowRequestError {
  if (error instanceof CashFlowApiError) return error.requestError
  if (error instanceof Error && error.name === 'AbortError') {
    return { status: null, kind: 'server', code: 'ABORTED', fieldErrors: {} }
  }
  return { status: null, kind: 'server', code: 'CASH_FLOW_LOAD_FAILED', fieldErrors: {} }
}

export function cashFlowFieldHasError(error: CashFlowRequestError | null, field: string) {
  const normalized = field.toLowerCase()
  return Boolean(error && Object.keys(error.fieldErrors).some(key => key.toLowerCase() === normalized))
}

export function cashFlowDateResolutionFailure(error: unknown): 'validation' | 'authentication' | 'forbidden' | 'unavailable' {
  const requestError = cashFlowRequestError(error)
  if (requestError.kind === 'validation') return 'validation'
  if (requestError.kind === 'authentication') return 'authentication'
  if (requestError.kind === 'forbidden') return 'forbidden'
  return 'unavailable'
}

export function cashFlowFilterPath(pathname: string, values: Record<string, string | number | null | undefined>) {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(values)) {
    if (value != null && String(value).trim()) query.set(key, String(value))
  }
  const search = query.toString()
  return search ? `${pathname}?${search}` : pathname
}

export function costedBomDetailPath(parentItemId: string, context: { asOfDate: string; search?: string; page: number; costBasisMode?: CostBasisMode; priceBookRevisionId?: string }) {
  return `/cash-flow/costed-boms/${encodeURIComponent(parentItemId)}?${costedBomContextQuery(context)}`
}

export function costedBomListPath(context: { asOfDate: string; search?: string; page: number; costBasisMode?: CostBasisMode; priceBookRevisionId?: string }) {
  return `/cash-flow/costed-boms?${costedBomContextQuery(context)}`
}

function costedBomContextQuery(context: { asOfDate: string; search?: string; page: number; costBasisMode?: CostBasisMode; priceBookRevisionId?: string }) {
  const query = new URLSearchParams({ asOfDate: context.asOfDate, page: String(positiveCashFlowPage(String(context.page))) })
  if (context.search?.trim()) query.set('search', context.search.trim())
  if (context.costBasisMode) query.set('costBasisMode', context.costBasisMode)
  if (context.priceBookRevisionId) query.set('priceBookRevisionId', context.priceBookRevisionId)
  return query
}

function normalizeProblemErrors(value: unknown) {
  if (!isRecord(value)) return {}
  const errors: Record<string, string[]> = {}
  for (const [field, rawMessages] of Object.entries(value)) {
    const messages = (Array.isArray(rawMessages) ? rawMessages : [rawMessages])
      .filter((message): message is string => typeof message === 'string')
      .map(message => message.trim())
      .filter(Boolean)
    if (messages.length) errors[field] = messages
  }
  return errors
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function buildCashFlowDashboardQuery(input: {
  asOfDate: string
  horizonEnd: string
  scope: CashFlowScope
}) {
  return new URLSearchParams({
    asOfDate: input.asOfDate,
    horizonEnd: input.horizonEnd,
    includePlanned: String(input.scope === 'ALL'),
  })
}

export function buildCashObligationsQuery(input: {
  asOfDate: string
  fromDate?: string
  toDate?: string
  scope: CashFlowScope
  search?: string
  supplierId?: string
  currencyCode?: string
  stage?: string
  state?: string
  page: number
  pageSize: number
}) {
  const query = new URLSearchParams({
    asOfDate: input.asOfDate,
    includePlanned: String(input.scope === 'ALL'),
    page: String(input.page),
    pageSize: String(input.pageSize),
  })
  const optional: Array<[string, string | undefined]> = [
    ['fromDate', input.fromDate],
    ['toDate', input.toDate],
    ['search', input.search?.trim()],
    ['supplierId', input.supplierId],
    ['currencyCode', input.currencyCode?.trim().toUpperCase()],
    ['stage', input.stage],
    ['state', input.state],
  ]
  for (const [key, value] of optional) if (value) query.set(key, value)
  return query
}

export function buildCostedBomQuery(input: {
  asOfDate: string
  search?: string
  page: number
  pageSize: number
  costBasisMode?: CostBasisMode
  priceBookRevisionId?: string
}) {
  const query = new URLSearchParams({
    asOfDate: input.asOfDate,
    page: String(input.page),
    pageSize: String(input.pageSize),
  })
  if (input.search?.trim()) query.set('search', input.search.trim())
  if (input.costBasisMode) query.set('costBasisMode', input.costBasisMode)
  if (input.priceBookRevisionId) query.set('priceBookRevisionId', input.priceBookRevisionId)
  return query
}

export function groupCashBucketsByCurrency(buckets: CashFlowBucket[]) {
  const groups = new Map<string, CashFlowBucket[]>()
  for (const bucket of buckets) {
    const rows = groups.get(bucket.currencyCode) ?? []
    rows.push(bucket)
    groups.set(bucket.currencyCode, rows)
  }
  return [...groups.entries()].map(([currencyCode, rows]) => ({
    currencyCode,
    buckets: rows.sort((left, right) => left.date.localeCompare(right.date)),
  })).sort((left, right) => left.currencyCode.localeCompare(right.currencyCode))
}

export function formatCashAmount(value: ExactDecimal | null | undefined, precision: number, language: string) {
  return formatExactDecimal(value, precision, language, true, 9)
}

/**
 * Unit prices can legitimately be smaller than the currency's normal cash precision
 * (for example, IRR per gram or USD per piece after a per-1,000 quotation). Keep the
 * normal currency precision as a floor, then reveal the stored fractional precision
 * up to the database contract's 12 decimal places so a non-zero price never appears
 * as zero merely because it is being displayed.
 */
export function formatCashUnitPrice(value: ExactDecimal | null | undefined, currencyPrecision: number, language: string) {
  if (value == null || !/^-?\d+(?:\.\d+)?$/.test(value)) return '—'
  const storedFraction = value.split('.')[1]?.replace(/0+$/, '').length ?? 0
  const precision = Math.max(Math.max(0, Math.trunc(currencyPrecision)), Math.min(12, storedFraction))
  return formatExactDecimal(value, precision, language, false, 12)
}

export function formatCashQuantity(value: ExactDecimal | null | undefined, language: string) {
  return formatExactDecimal(value, 6, language, false, 6)
}

export function formatReportingRate(value: ExactDecimal | null | undefined, language: string) {
  return formatExactDecimal(value, 12, language, false, 12)
}

export function cashFlowCurrencyView(value: string | null): CashFlowCurrencyView {
  return value === 'ORIGINAL' ? 'ORIGINAL' : 'USD'
}

export function canRenderReportingRateEditor(
  mode: 'create' | 'edit',
  status: FxRateSetStatus | undefined,
  canManage: boolean,
) {
  return canManage && (mode === 'create' || status === 'DRAFT')
}

export function isPositiveExactDecimal(value: ExactDecimal) {
  return /^\+?\d+(?:\.\d+)?$/.test(value) && /[1-9]/.test(value)
}

/** Number conversion is intentionally confined to relative chart geometry, never financial display or totals. */
export function cashFlowVisualNumber(value: ExactDecimal) {
  const parsed = Number(value)
  if (Number.isFinite(parsed)) return Math.max(0, parsed)
  return isPositiveExactDecimal(value) ? Number.MAX_VALUE : 0
}

function formatExactDecimal(
  value: ExactDecimal | null | undefined,
  precision: number,
  language: string,
  fixed: boolean,
  maximumPrecision: number,
) {
  if (value == null || !/^-?\d+(?:\.\d+)?$/.test(value)) return '—'
  const fractionDigits = Math.max(0, Math.min(maximumPrecision, Math.trunc(precision)))
  const negative = value.startsWith('-')
  const [integerPart, fractionPart = ''] = (negative ? value.slice(1) : value).split('.')
  const paddedFraction = fractionPart.padEnd(fractionDigits + 1, '0')
  const retainedFraction = paddedFraction.slice(0, fractionDigits)
  let scaled = BigInt(`${integerPart}${retainedFraction}` || '0')
  if (Number(paddedFraction[fractionDigits] ?? '0') >= 5) scaled += 1n
  const scale = 10n ** BigInt(fractionDigits)
  const whole = fractionDigits ? scaled / scale : scaled
  let fraction = fractionDigits ? String(scaled % scale).padStart(fractionDigits, '0') : ''
  if (!fixed) fraction = fraction.replace(/0+$/, '')
  const persian = language.toLowerCase().startsWith('fa')
  const grouped = new Intl.NumberFormat(persian ? 'fa-IR' : 'en-US', { maximumFractionDigits: 0, useGrouping: true }).format(whole)
  const localizedFraction = persian ? fraction.replace(/\d/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]) : fraction
  const sign = negative && scaled !== 0n ? '−' : ''
  return `${sign}${grouped}${localizedFraction ? `${persian ? '٫' : '.'}${localizedFraction}` : ''}`
}

export function addCalendarDays(isoDate: string, days: number) {
  const date = new Date(`${isoDate}T12:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

export function positiveCashFlowPage(value: string | null) {
  if (value == null || !/^\d+$/.test(value)) return 1
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed >= 1 ? parsed : 1
}

export function tehranTodayIso(now = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(now)
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(value => value.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}`
}
