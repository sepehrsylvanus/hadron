import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { act, create, type ReactTestRenderer } from 'react-test-renderer'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { createServer, type ViteDevServer } from 'vite'
import {
  addCalendarDays,
  buildCashFlowDashboardQuery,
  buildCashObligationsQuery,
  buildCostedBomQuery,
  buildReportingRateSetQuery,
  canLoadDashboardObligations,
  canRenderReportingRateEditor,
  cashFlowCurrencyView,
  cashFlowDateResolutionFailure,
  cashFlowDashboardRequestPaths,
  cashFlowFieldHasError,
  cashFlowFilterPath,
  cashFlowVisualNumber,
  cashFlowJalaliResolverPath,
  costedBomDetailPath,
  costedBomListPath,
  formatCashAmount,
  formatCashUnitPrice,
  formatCashQuantity,
  formatReportingRate,
  groupCashBucketsByCurrency,
  loadIndependentCashFlowSection,
  isExactDecimalInput,
  mergeCopiedReportingRateValues,
  normalizeReportingRateInput,
  positiveCashFlowPage,
  readCashFlowApiError,
  reportingRateDisplayCount,
  reportingRateCurrenciesForEditor,
  reportingRateEffectiveDateIsLocked,
  reportingRateMissingCurrencies,
  reportingRateServerInvalidIds,
  reportingRateSetPath,
  retainClientRequestAttempt,
  stableClientRequestAttempt,
  tehranTodayIso,
  type CashObligation,
  type CostedBomSummary,
} from './cashFlowUi.ts'
import { localizedName } from './i18n/localizedName.ts'

let server: ViteDevServer | undefined
let pageModule: typeof import('./CashFlowPage.tsx') | undefined
let reportingRatesModule: typeof import('./CashFlowReportingRatesPage.tsx') | undefined

after(async () => server?.close())

test('cash-flow queries default to committed exposure and preserve server paging filters', () => {
  const dashboard = buildCashFlowDashboardQuery({ asOfDate: '2026-09-07', horizonEnd: '2026-12-06', scope: 'COMMITTED' })
  assert.equal(dashboard.get('includePlanned'), 'false')
  assert.equal(dashboard.get('asOfDate'), '2026-09-07')

  const obligations = buildCashObligationsQuery({
    asOfDate: '2026-09-07', fromDate: '2026-09-07', toDate: '2026-12-06', scope: 'ALL',
    search: ' PO-000123 ', currencyCode: 'usd', stage: 'ADVANCE', state: 'DUE_SOON', page: 3, pageSize: 25,
  })
  assert.equal(obligations.get('includePlanned'), 'true')
  assert.equal(obligations.get('search'), 'PO-000123')
  assert.equal(obligations.get('currencyCode'), 'USD')
  assert.equal(obligations.get('page'), '3')

  const costs = buildCostedBomQuery({ asOfDate: '2026-09-07', search: ' A10 ', page: 2, pageSize: 25 })
  assert.equal(costs.get('search'), 'A10')
  assert.equal(costs.get('page'), '2')
  const costBook = buildCostedBomQuery({ asOfDate: '2026-09-07', page: 1, pageSize: 25, costBasisMode: 'MATERIAL_COST_BOOK', priceBookRevisionId: 'revision-1' })
  assert.equal(costBook.get('costBasisMode'), 'MATERIAL_COST_BOOK')
  assert.equal(costBook.get('priceBookRevisionId'), 'revision-1')
  assert.equal(positiveCashFlowPage('3'), 3)
  assert.equal(positiveCashFlowPage('NaN'), 1)
  assert.equal(positiveCashFlowPage('-2'), 1)
})

test('cash-flow chart groups each original currency independently and sorts its dates', () => {
  const groups = groupCashBucketsByCurrency([
    { date: '2026-09-09', currencyId: 'usd', currencyCode: 'USD', amount: '20', count: 1 },
    { date: '2026-09-08', currencyId: 'irr', currencyCode: 'IRR', amount: '1000', count: 2 },
    { date: '2026-09-07', currencyId: 'usd', currencyCode: 'USD', amount: '10', count: 1 },
  ])
  assert.deepEqual(groups.map(group => group.currencyCode), ['IRR', 'USD'])
  assert.deepEqual(groups[1].buckets.map(bucket => bucket.date), ['2026-09-07', '2026-09-09'])
  assert.deepEqual(groups[1].buckets.map(bucket => bucket.amount), ['10', '20'])
  assert.deepEqual(groups[0].buckets.map(bucket => bucket.amount), ['1000'])
})

test('cash amounts preserve invariant decimal-string precision and date defaults use Tehran business date', () => {
  assert.equal(formatCashAmount('1234.5', 2, 'en'), '1,234.50')
  assert.equal(formatCashAmount('1234.5', 0, 'en'), '1,235')
  assert.equal(formatCashAmount('9007199254740993.55', 2, 'en'), '9,007,199,254,740,993.55')
  assert.equal(formatCashAmount('1234.123456789', 9, 'en'), '1,234.123456789')
  assert.equal(formatCashUnitPrice('0.001', 2, 'en'), '0.001')
  assert.equal(formatCashUnitPrice('0.000000000001', 0, 'en'), '0.000000000001')
  assert.equal(formatCashUnitPrice('1.230000000000', 2, 'en'), '1.23')
  assert.equal(formatCashAmount('0.001', 2, 'en'), '0.00', 'cash totals still follow currency precision')
  assert.equal(formatCashQuantity('9007199254740993.05', 'en'), '9,007,199,254,740,993.05')
  assert.equal(formatCashQuantity('0.014000', 'en'), '0.014')
  assert.equal(formatReportingRate('2200000.000000000', 'en'), '2,200,000')
  assert.equal(formatReportingRate('6.800000000', 'en'), '6.8')
  assert.equal(formatReportingRate('0.860000000', 'en'), '0.86')
  assert.equal(formatReportingRate('0.000000000001', 'en'), '0.000000000001')
  assert.equal(cashFlowVisualNumber('12.5'), 12.5)
  assert.equal(addCalendarDays('2026-09-07', 90), '2026-12-06')
  assert.equal(tehranTodayIso(new Date('2026-03-20T21:30:00Z')), '2026-03-21')
})

test('dashboard sections fail independently so one endpoint does not blank the whole page', async () => {
  const [dashboard, obligations] = await Promise.all([
    loadIndependentCashFlowSection(Promise.resolve({ currencies: ['IRR'] })),
    loadIndependentCashFlowSection(Promise.reject(new Error('CASH_FLOW_OBLIGATIONS_FAILED'))),
  ])

  assert.deepEqual(dashboard, { data: { currencies: ['IRR'] }, error: null })
  assert.equal(obligations.data, null)
  assert.equal(obligations.error?.kind, 'server')
  assert.equal(obligations.error?.code, 'CASH_FLOW_LOAD_FAILED')
})

test('RFC 7807 validation errors remain actionable and field-bound instead of becoming an outage', async () => {
  const apiError = await readCashFlowApiError(new Response(JSON.stringify({
    title: 'One or more validation errors occurred.',
    errors: { fromDate: ['Start must not be after end.'], ToDate: ['End must not be before start.'] },
  }), { status: 400, headers: { 'content-type': 'application/problem+json' } }))

  assert.equal(apiError.requestError.kind, 'validation')
  assert.equal(apiError.requestError.code, 'CASH_FLOW_VALIDATION_FAILED')
  assert.equal(cashFlowFieldHasError(apiError.requestError, 'fromDate'), true)
  assert.equal(cashFlowFieldHasError(apiError.requestError, 'toDate'), true, 'field matching is case-insensitive')
  assert.equal(cashFlowFieldHasError(apiError.requestError, 'asOfDate'), false)

  const gatewayError = await readCashFlowApiError(new Response('<html>Bad gateway</html>', { status: 502 }))
  assert.equal(gatewayError.requestError.kind, 'server')
  assert.equal(gatewayError.requestError.code, 'CASH_FLOW_LOAD_FAILED')
  assert.deepEqual(gatewayError.requestError.fieldErrors, {})
})

test('date-resolution failures distinguish invalid input, expired authentication, permission, and service failure', async () => {
  const validation = await readCashFlowApiError(new Response(JSON.stringify({ errors: { jalaliDate: ['Invalid date.'] } }), { status: 400 }))
  const authentication = await readCashFlowApiError(new Response('', { status: 401 }))
  const forbidden = await readCashFlowApiError(new Response('', { status: 403 }))
  const unavailable = await readCashFlowApiError(new Response('', { status: 503 }))

  assert.equal(cashFlowDateResolutionFailure(validation), 'validation')
  assert.equal(authentication.requestError.kind, 'authentication')
  assert.equal(cashFlowDateResolutionFailure(authentication), 'authentication')
  assert.equal(cashFlowDateResolutionFailure(forbidden), 'forbidden')
  assert.equal(cashFlowDateResolutionFailure(unavailable), 'unavailable')
})

test('filter resets produce explicit canonical defaults without retaining stale paging or search', () => {
  assert.equal(cashFlowFilterPath('/cash-flow', {
    asOfDate: '2026-09-07', horizonEnd: '2026-12-06', scope: 'COMMITTED', search: '', page: null,
  }), '/cash-flow?asOfDate=2026-09-07&horizonEnd=2026-12-06&scope=COMMITTED')
  assert.equal(cashFlowFilterPath('/cash-flow/costed-boms', { asOfDate: '2026-09-07', search: '' }),
    '/cash-flow/costed-boms?asOfDate=2026-09-07')
})

test('costed-BOM drilldown and back links preserve valuation, search, and page context', () => {
  const context = { asOfDate: '2026-09-07', search: ' A10 white ', page: 4, costBasisMode: 'MATERIAL_COST_BOOK' as const, priceBookRevisionId: 'revision-1' }
  assert.equal(
    costedBomDetailPath('product-1', context),
    '/cash-flow/costed-boms/product-1?asOfDate=2026-09-07&page=4&search=A10+white&costBasisMode=MATERIAL_COST_BOOK&priceBookRevisionId=revision-1',
  )
  assert.equal(
    costedBomListPath(context),
    '/cash-flow/costed-boms?asOfDate=2026-09-07&page=4&search=A10+white&costBasisMode=MATERIAL_COST_BOOK&priceBookRevisionId=revision-1',
  )
})

test('dual-date input exposes its equivalent date and validation error to assistive technology', async () => {
  const { DualDateFilter } = await cashFlowPageModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(DualDateFilter, {
      label: 'As-of date', value: '2026-09-07', language: 'en', surface: 'obligations',
      externalError: 'Choose a valid explicit as-of date.', onChange: () => undefined,
    }))
    await new Promise(resolve => setTimeout(resolve, 0))
  })

  const input = renderer.root.findByType('input')
  const equivalent = renderer.root.find(node => node.type === 'span' && node.props.className === 'cash-flow-date-equivalent')
  const error = renderer.root.find(node => node.type === 'small' && node.props.role === 'alert')
  const describedBy = String(input.props['aria-describedby']).split(' ')
  assert.ok(describedBy.includes(equivalent.props.id))
  assert.ok(describedBy.includes(error.props.id))
  assert.equal(input.props['aria-invalid'], true)
  await act(async () => renderer.unmount())
})

test('optional Jalali date filter accepts a cleared value without a validation error', async () => {
  const { DualDateFilter } = await cashFlowPageModule()
  let changed = 'not-called'
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(DualDateFilter, {
      label: 'از تاریخ', value: '', language: 'fa-IR', surface: 'purchase-price-history',
      allowEmpty: true, onChange: (value: string) => { changed = value },
    }))
    await new Promise(resolve => setTimeout(resolve, 0))
  })

  await act(async () => { renderer.root.findByType('input').props.onBlur() })
  assert.equal(changed, '')
  assert.equal(renderer.root.findAll(node => node.type === 'small' && node.props.role === 'alert').length, 0)
  await act(async () => renderer.unmount())
})

test('payment chart has a keyboard-accessible exact-data table and keeps decorative bars out of the accessibility tree', async () => {
  const { CashFlowChart } = await cashFlowPageModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(CashFlowChart, {
      language: 'en', loading: false,
      currencies: [{ currencyId: 'usd', currencyCode: 'USD', decimalPrecision: 2, overdueAmount: '0', due7DaysAmount: '25.50', due30DaysAmount: '25.50', due90DaysAmount: '25.50', horizonAmount: '25.50', obligationCount: 2 }],
      buckets: [{ date: '2026-09-08', currencyId: 'usd', currencyCode: 'USD', amount: '25.50', count: 2 }],
    }))
  })

  const visual = renderer.root.find(node => node.props.className === 'cash-flow-bar-chart')
  const details = renderer.root.findByType('details')
  renderer.root.findByType('table')
  assert.equal(String(visual.props['aria-hidden']), 'true')
  assert.equal(details.findByType('summary').children.join(''), 'View exact USD daily data')
  assert.match(JSON.stringify(renderer.toJSON()), /25\.50/)
  await act(async () => renderer.unmount())
})

test('section failures expose a real retry action for transient failures', async () => {
  const { SectionFailure } = await cashFlowPageModule()
  let retries = 0
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(SectionFailure, {
      error: { status: 503, kind: 'server', code: 'CASH_FLOW_LOAD_FAILED', fieldErrors: {} },
      onRetry: () => { retries += 1 },
    }))
  })
  const retry = renderer.root.findByType('button')
  await act(async () => retry.props.onClick())
  assert.equal(retries, 1)
  await act(async () => renderer.unmount())
})

test('each private Cash Flow surface resolves Jalali dates without using the Planning endpoint', () => {
  const paths = (['dashboard', 'obligations', 'costed-boms', 'reporting-rates', 'material-cost-books', 'purchase-price-history'] as const).map(cashFlowJalaliResolverPath)
  assert.deepEqual(paths, [
    '/v1/cash-flow/dashboard/resolve-jalali-date',
    '/v1/cash-flow/obligations/resolve-jalali-date',
    '/v1/cash-flow/costed-boms/resolve-jalali-date',
    '/v1/cash-flow/reporting-rate-sets/resolve-jalali-date',
    '/v1/cash-flow/material-cost-books/resolve-jalali-date',
    '/v1/cash-flow/purchase-price-history/resolve-jalali-date',
  ])
  assert.ok(paths.every(path => !path.includes('/planning/')))
})

test('dashboard-only permission never loads or links the obligations section', () => {
  const dashboardOnly = (permission: string) => permission === 'cash_flow.dashboard.view'
  const dashboardAndObligations = (permission: string) => ['cash_flow.dashboard.view', 'cash_flow.obligation.view'].includes(permission)
  assert.equal(canLoadDashboardObligations(dashboardOnly), false)
  assert.equal(canLoadDashboardObligations(dashboardAndObligations), true)
  assert.deepEqual(cashFlowDashboardRequestPaths(dashboardOnly), ['/dashboard'])
  assert.deepEqual(cashFlowDashboardRequestPaths(dashboardAndObligations), ['/dashboard', '/obligations'])
})

test('obligation contract exposes cancellation-adjusted projected amount as an exact decimal string', () => {
  const row: Pick<CashObligation, 'projectedAmount'> = { projectedAmount: '875.125' }
  assert.equal(row.projectedAmount, '875.125')
})

test('schedule-level obligations expose PO line and localized item provenance while PO-level advance stays explicit', () => {
  const scheduled: CashObligation = {
    obligationKey: 'CREDIT:schedule-1', expectedPaymentDate: '2026-09-20',
    purchaseOrderId: 'po-1', purchaseOrderNumber: 'PO-000001', purchaseOrderStatus: 'ORDERED',
    purchaseOrderLineId: 'line-1', purchaseOrderLineNumber: 2,
    itemId: 'item-1', itemCode: 'PRT-0001', itemNameFa: 'بدنه اصلی', itemNameEn: 'Main body',
    supplierId: 'supplier-1', supplierNumber: 'SUP-0001', supplierName: 'تأمین‌کننده نمونه',
    currencyId: 'currency-1', currencyCode: 'USD', currencyDecimalPrecision: 2,
    projectedAmount: '875.125', unitsPerUsd: '1', projectedAmountUsd: '875.125', stage: 'CREDIT', confidence: 'COMMITTED',
    dateSource: 'REVISED', state: 'SCHEDULED', anchorCode: 'SHIPPED',
    deliveryScheduleId: 'schedule-1', scheduleNumber: 3,
  }
  assert.equal(localizedName({ nameFa: scheduled.itemNameFa!, nameEn: scheduled.itemNameEn }, 'en'), 'Main body')
  assert.equal(localizedName({ nameFa: scheduled.itemNameFa!, nameEn: scheduled.itemNameEn }, 'fa-IR'), 'بدنه اصلی')
  assert.equal(scheduled.purchaseOrderLineNumber, 2)
  assert.equal(scheduled.scheduleNumber, 3)

  const advance: CashObligation = {
    ...scheduled,
    obligationKey: 'ADVANCE:po-1', stage: 'ADVANCE', dateSource: 'ORDERED', anchorCode: 'ORDERED',
    purchaseOrderLineId: null, purchaseOrderLineNumber: null,
    itemId: null, itemCode: null, itemNameFa: null, itemNameEn: null,
    deliveryScheduleId: null, scheduleNumber: null,
  }
  assert.equal(advance.itemNameFa, null)
  assert.equal(advance.purchaseOrderLineId, null)
})

test('costed-BOM contract uses bilingual parent names with Persian fallback', () => {
  const summary: CostedBomSummary = {
    parentItemId: 'product-1', parentItemCode: 'PRD-0001',
    parentItemNameFa: 'محصول نمونه', parentItemNameEn: null,
    bomRevisionId: 'bom-1', bomRevisionCode: 'BOM-000001',
    costedOutputQuantity: '1', outputUnitId: 'unit-1', outputUnitCode: 'pcs',
    asOfDate: '2026-09-07', costBasisSelection: { mode: 'PURCHASE_HISTORY', traversalSemantics: 'APPROVED_BOM' },
    currencyTotals: [], materialCostUsd: null, missingCostCount: 1, hasMixedCurrencies: false,
  }
  assert.equal(localizedName({ nameFa: summary.parentItemNameFa, nameEn: summary.parentItemNameEn }, 'en'), 'محصول نمونه')
})

test('dashboard defaults to USD while preserving an explicit original-currency choice', () => {
  assert.equal(cashFlowCurrencyView(null), 'USD')
  assert.equal(cashFlowCurrencyView('UNKNOWN'), 'USD')
  assert.equal(cashFlowCurrencyView('ORIGINAL'), 'ORIGINAL')
})

test('reporting-rate route and query helpers preserve status and encode object identity', () => {
  const query = buildReportingRateSetQuery({ page: 2, pageSize: 25, status: 'PUBLISHED' })
  assert.equal(query.toString(), 'page=2&pageSize=25&status=PUBLISHED')
  assert.equal(reportingRateSetPath('set/one', 'edit'), '/cash-flow/reporting-rates/set%2Fone/edit')
  assert.equal(reportingRateDisplayCount(4), 5, 'the locked USD base row is included in the displayed count')
  assert.equal(reportingRateEffectiveDateIsLocked('published-source-id'), true)
  assert.equal(reportingRateEffectiveDateIsLocked(null), false)
  assert.equal(isExactDecimalInput('2200000'), true)
  assert.equal(isExactDecimalInput('0.86'), true)
  assert.equal(isExactDecimalInput('9999999999999999.123456789012'), true)
  assert.equal(isExactDecimalInput('0'), false)
  assert.equal(isExactDecimalInput('-1'), false)
  assert.equal(isExactDecimalInput('10000000000000000'), false)
  assert.equal(isExactDecimalInput('1.1234567890123'), false)
  assert.equal(canRenderReportingRateEditor('create', undefined, true), true)
  assert.equal(canRenderReportingRateEditor('edit', 'DRAFT', true), true)
  assert.equal(canRenderReportingRateEditor('edit', 'PUBLISHED', true), false, 'published revisions stay immutable even on a direct edit URL')
  assert.equal(canRenderReportingRateEditor('edit', 'DRAFT', false), false, 'view permission never renders a writable form')
})

test('reporting-rate paste normalization preserves invalid syntax for validation instead of changing its financial meaning', () => {
  assert.equal(normalizeReportingRateInput('۲٬۲۰۰٬۰۰۰٫۵'), '2200000.5')
  assert.equal(normalizeReportingRateInput('.86'), '0.86')
  assert.equal(normalizeReportingRateInput('٫۸۶'), '0.86')
  assert.equal(normalizeReportingRateInput('-5'), '-5')
  assert.equal(normalizeReportingRateInput('1e3'), '1e3')
  assert.equal(normalizeReportingRateInput('1..3'), '1..3')
  assert.equal(isExactDecimalInput(normalizeReportingRateInput('.86')), true)
  assert.equal(isExactDecimalInput(normalizeReportingRateInput('-5')), false)
  assert.equal(isExactDecimalInput(normalizeReportingRateInput('1e3')), false)
})

test('reporting-rate commands reuse an id only for an ambiguous retry of the identical payload', () => {
  let sequence = 0
  const createId = () => `request-${++sequence}`
  const first = stableClientRequestAttempt(null, 'payload-a', createId)
  const transportRetry = stableClientRequestAttempt(first, 'payload-a', createId)
  const editedPayload = stableClientRequestAttempt(transportRetry, 'payload-b', createId)
  assert.strictEqual(transportRetry, first)
  assert.equal(first.clientRequestId, 'request-1')
  assert.equal(editedPayload.clientRequestId, 'request-2')
  assert.equal(retainClientRequestAttempt({ status: null, kind: 'server', code: 'CASH_FLOW_LOAD_FAILED', fieldErrors: {} }), true)
  assert.equal(retainClientRequestAttempt({ status: 503, kind: 'server', code: 'CASH_FLOW_LOAD_FAILED', fieldErrors: {} }), true)
  assert.equal(retainClientRequestAttempt({ status: 409, kind: 'conflict', code: 'STALE_VERSION', fieldErrors: {} }), false)
})

test('reporting-rate drafts retain inactive reference currencies and copied values never erase an existing source rate', () => {
  const currencies = reportingRateCurrenciesForEditor([
    { id: 'irr', code: 'IRR', nameFa: 'ریال', isActive: true, isBaseCurrency: false },
    { id: 'legacy', code: 'LEG', nameFa: 'ارز قدیمی', isActive: false, isBaseCurrency: false },
    { id: 'usd', code: 'USD', nameFa: 'دلار', isActive: true, isBaseCurrency: true },
  ])
  assert.deepEqual(currencies.map(currency => currency.id), ['usd', 'irr', 'legacy'])
  assert.deepEqual(reportingRateMissingCurrencies(currencies, [{ currencyId: 'irr' }]).map(currency => currency.id), ['legacy'])
  const copied = mergeCopiedReportingRateValues(
    { irr: '42000', legacy: '7.5' },
    ['irr', 'legacy'],
    [{ currencyId: 'irr', unitsPerUsd: '43000' }],
  )
  assert.deepEqual(copied, { irr: '43000', legacy: '7.5' })

  const missing = reportingRateServerInvalidIds(
    { status: 400, kind: 'validation', code: 'CASH_FLOW_VALIDATION_FAILED', fieldErrors: { rates: ['All referenced currencies require a rate.'] } },
    ['irr'], ['irr', 'legacy'], { irr: '43000', legacy: '' },
  )
  assert.deepEqual([...missing], ['legacy'])
})

test('USD/original toggle exposes pressed state and changes only on an explicit user action', async () => {
  const { CurrencyViewToggle } = await cashFlowPageModule()
  const selections: string[] = []
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(CurrencyViewToggle, { value: 'USD', onChange: (value: string) => selections.push(value) }))
  })
  const buttons = renderer.root.findAllByType('button')
  assert.equal(buttons[0].props['aria-pressed'], true)
  assert.equal(buttons[1].props['aria-pressed'], false)
  await act(async () => buttons[1].props.onClick())
  assert.deepEqual(selections, ['ORIGINAL'])
  await act(async () => renderer.unmount())
})

test('missing purchase price and missing FX remain independent cost-readiness states', async () => {
  const { CostReadiness } = await cashFlowPageModule()
  let renderer!: ReactTestRenderer
  await act(async () => { renderer = create(React.createElement(CostReadiness, { missingCostCount: 2, missingFx: true })) })
  const rendered = JSON.stringify(renderer.toJSON())
  assert.match(rendered, /2 items without a cost basis/)
  assert.match(rendered, /Missing FX rate/)
  await act(async () => renderer.unmount())
})

test('incomplete FX keeps unaffected USD KPIs and buckets visible while marking only null values unavailable', async () => {
  const { CashFlowUsdChart, UsdKpi } = await cashFlowPageModule()
  let kpiRenderer!: ReactTestRenderer
  let chartRenderer!: ReactTestRenderer
  await act(async () => {
    kpiRenderer = create(React.createElement(UsdKpi, {
      summary: { overdueAmount: '125', due7DaysAmount: null, due30DaysAmount: '900', due90DaysAmount: null, horizonAmount: '1025' },
      unresolvedCount: 0, loading: false, language: 'en', canViewObligations: false,
      scope: 'COMMITTED', asOfDate: '2026-09-07', horizonEnd: '2026-12-06',
    }))
    chartRenderer = create(React.createElement(CashFlowUsdChart, {
      buckets: [
        { date: '2026-09-08', amount: '125', count: 1 },
        { date: '2026-09-09', amount: null, count: 1 },
      ],
      loading: false, language: 'en',
    }))
  })
  const kpis = JSON.stringify(kpiRenderer.toJSON())
  const chart = JSON.stringify(chartRenderer.toJSON())
  assert.match(kpis, /125\.00/)
  assert.match(kpis, /1,025\.00/)
  assert.match(kpis, /Horizon total in USD/)
  assert.match(kpis, /USD unavailable/)
  assert.match(chart, /125\.00/)
  assert.match(chart, /USD unavailable/)
  const missingMarker = chartRenderer.root.find(node => node.props.className === 'cash-flow-bar cash-flow-bar--unavailable')
  assert.equal(missingMarker.findByType('span').children.join(''), '?')
  await act(async () => { kpiRenderer.unmount(); chartRenderer.unmount() })
})

test('reporting-rate inputs retain their row identity while a user types multiple characters', async () => {
  const { ReportingRateEditorTable } = await cashFlowReportingRatesPageModule()
  const currencies = [
    { id: 'usd', code: 'USD', nameFa: 'دلار آمریکا', nameEn: 'US Dollar', isActive: true, isBaseCurrency: true },
    { id: 'irr', code: 'IRR', nameFa: 'ریال', nameEn: 'Iranian Rial', isActive: true, isBaseCurrency: false },
  ]
  function Harness() {
    const [values, setValues] = React.useState<Record<string, string>>({ irr: '' })
    return React.createElement(ReportingRateEditorTable, {
      currencies, values, language: 'en', attempted: false, touched: new Set<string>(), serverInvalidIds: new Set<string>(),
      onChange: (currencyId: string, value: string) => setValues(previous => ({ ...previous, [currencyId]: normalizeReportingRateInput(value) })),
      onBlur: () => undefined,
    })
  }
  let renderer!: ReactTestRenderer
  await act(async () => { renderer = create(React.createElement(Harness)) })
  const firstInput = renderer.root.findByType('input')
  for (const value of ['2', '22', '220', '2200', '2200000']) {
    const current = renderer.root.findByType('input')
    await act(async () => current.props.onChange({ target: { value } }))
  }
  const finalInput = renderer.root.findByType('input')
  assert.strictEqual(finalInput, firstInput, 'the input node must not remount and lose focus')
  assert.equal(finalInput.props.value, '2200000')
  await act(async () => finalInput.props.onChange({ target: { value: '-5' } }))
  assert.equal(renderer.root.findByType('input').props.value, '-5', 'a negative paste must remain invalid instead of becoming positive')
  await act(async () => renderer.root.findByType('input').props.onChange({ target: { value: '1e3' } }))
  assert.equal(renderer.root.findByType('input').props.value, '1e3', 'scientific notation must remain visible and invalid instead of changing value')
  await act(async () => renderer.root.findByType('input').props.onChange({ target: { value: '٫۸۶' } }))
  assert.equal(renderer.root.findByType('input').props.value, '0.86')
  await act(async () => renderer.unmount())
})

test('reporting-rate table labels an inactive blank reference currency and binds its publish error to the row', async () => {
  const { ReportingRateEditorTable } = await cashFlowReportingRatesPageModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(ReportingRateEditorTable, {
      currencies: [{ id: 'legacy', code: 'LEG', nameFa: 'ارز قدیمی', nameEn: 'Legacy currency', isActive: false, isBaseCurrency: false }],
      values: { legacy: '' }, language: 'en', attempted: false, touched: new Set<string>(), serverInvalidIds: new Set(['legacy']),
      onChange: () => undefined, onBlur: () => undefined,
    }))
  })
  const rendered = JSON.stringify(renderer.toJSON())
  assert.match(rendered, /Inactive/)
  assert.match(rendered, /Enter a rate for this referenced currency/)
  assert.equal(renderer.root.findByType('input').props['aria-invalid'], true)
  await act(async () => renderer.unmount())
})

test('reporting-rate Cancel keeps a dirty draft mounted until discard is explicitly confirmed', async () => {
  const { ReportingRateEditorForm } = await cashFlowReportingRatesPageModule()
  function LocationProbe() {
    const location = useLocation()
    return React.createElement('output', null, location.pathname)
  }
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(MemoryRouter, { initialEntries: ['/cash-flow/reporting-rates/new'] },
      React.createElement(React.Fragment, null,
        React.createElement(ReportingRateEditorForm, {
          mode: 'create', language: 'en', onSaved: () => undefined,
          currencies: [
            { id: 'usd', code: 'USD', nameFa: 'دلار آمریکا', nameEn: 'US Dollar', isActive: true, isBaseCurrency: true },
            { id: 'irr', code: 'IRR', nameFa: 'ریال', nameEn: 'Iranian Rial', isActive: true, isBaseCurrency: false },
          ],
        }),
        React.createElement(LocationProbe),
      )), {
      createNodeMock: element => element.type === 'section' ? { querySelector: () => null }
        : element.type === 'button' ? { focus: () => undefined }
        : {},
    })
  })
  await act(async () => renderer.root.findByProps({ id: 'cash-rate-irr' }).props.onChange({ target: { value: '2200000' } }))
  const pageCancel = renderer.root.findAllByType('button').find(button => button.children.includes('Cancel'))!
  await act(async () => pageCancel.props.onClick())
  assert.equal(renderer.root.findByType('output').children.join(''), '/cash-flow/reporting-rates/new')
  let dialog = renderer.root.findByProps({ role: 'alertdialog' })
  await act(async () => dialog.findAllByType('button').find(button => !String(button.props.className).includes('button--danger'))!.props.onClick())
  assert.equal(renderer.root.findByType('output').children.join(''), '/cash-flow/reporting-rates/new', 'keep editing must preserve the draft route')
  assert.equal(renderer.root.findByProps({ id: 'cash-rate-irr' }).props.value, '2200000', 'keep editing must preserve the typed financial value')

  await act(async () => pageCancel.props.onClick())
  dialog = renderer.root.findByProps({ role: 'alertdialog' })
  await act(async () => dialog.findAllByType('button').find(button => String(button.props.className).includes('button--danger'))!.props.onClick())
  assert.equal(renderer.root.findByType('output').children.join(''), '/cash-flow/reporting-rates')
  await act(async () => renderer.unmount())
})

async function cashFlowPageModule() {
  if (pageModule) return pageModule
  const browserWindow = {
    fetch: globalThis.fetch.bind(globalThis),
    localStorage: { getItem: () => 'en', setItem: () => undefined },
    setTimeout: globalThis.setTimeout.bind(globalThis),
    clearTimeout: globalThis.clearTimeout.bind(globalThis),
    location: { href: 'http://localhost/cash-flow/reporting-rates/new', origin: 'http://localhost', pathname: '/cash-flow/reporting-rates/new', search: '' },
    history: { state: { idx: 1 }, go: () => undefined },
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }
  Object.assign(globalThis, {
    IS_REACT_ACT_ENVIRONMENT: true,
    window: browserWindow,
    document: {
      documentElement: { lang: '', dir: '' },
      activeElement: { focus: () => undefined },
      body: { style: { overflow: '' } },
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    },
  })
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  pageModule = await server.ssrLoadModule('/src/CashFlowPage.tsx') as typeof import('./CashFlowPage.tsx')
  return pageModule
}

async function cashFlowReportingRatesPageModule() {
  if (reportingRatesModule) return reportingRatesModule
  await cashFlowPageModule()
  reportingRatesModule = await server!.ssrLoadModule('/src/CashFlowReportingRatesPage.tsx') as typeof import('./CashFlowReportingRatesPage.tsx')
  return reportingRatesModule
}
