import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { useEffect } from 'react'
import { createInstance, type i18n } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { act, create, type ReactTestRenderer, type ReactTestRendererJSON } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'
import { businessDirection, formatBusinessDate } from './i18n/businessDate.ts'
import { localizedName } from './i18n/localizedName.ts'
import { formatOperationalNumber } from './i18n/numberFormatting.ts'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'
import {
  chooseInitialRunId,
  materialPlanningRequiredRunHeaderFields,
  materialPlanningRequiredSummaryFields,
  materialPlanningRequiredSupplyTraceFields,
  readRememberedRunId,
  rememberRunId,
  sourceNavigation,
  type StorageLike,
} from './materialPlanningSupport.ts'

const materialPlanningPath = '/mrp/planning/material-planning'
const latestRunId = '00000000-0000-0000-0000-000000000001'
const historicalRunId = '00000000-0000-0000-0000-000000000002'
const itemId = '00000000-0000-0000-0000-000000000101'
const productId = '00000000-0000-0000-0000-000000000201'

test('operational numbers use localized grouping and at most one fractional digit', () => {
  assert.equal(formatOperationalNumber(71959.733333, 'en'), '71,959.7')
  assert.equal(formatOperationalNumber(33.666667, 'en'), '33.7')
  assert.equal(formatOperationalNumber(12, 'en'), '12')
  assert.equal(formatOperationalNumber(null, 'en'), '—')
  assert.equal(formatOperationalNumber(71959.733333, 'fa-IR'), '۷۱٬۹۵۹٫۷')
})

class MemoryStorage implements StorageLike {
  private readonly values = new Map<string, string>()
  getItem(key: string) { return this.values.get(key) ?? null }
  setItem(key: string, value: string) { this.values.set(key, value) }
  removeItem(key: string) { this.values.delete(key) }
}

test('historical run selection helpers preserve URL authority and remembered context', () => {
  const storage = new MemoryStorage()
  rememberRunId(storage, 'historical-run')
  assert.equal(readRememberedRunId(storage), 'historical-run')
  assert.deepEqual(chooseInitialRunId('', readRememberedRunId(storage), 'latest-run'), {
    runId: 'historical-run', source: 'REMEMBERED',
  })
  assert.deepEqual(chooseInitialRunId('url-run', readRememberedRunId(storage), 'latest-run'), {
    runId: 'url-run', source: 'URL',
  })
  assert.equal(chooseInitialRunId('', 'unavailable-run', 'latest-run').runId, 'unavailable-run')
})

test('explicit latest selection replaces the remembered historical run', () => {
  const storage = new MemoryStorage()
  rememberRunId(storage, 'historical-run')
  rememberRunId(storage, 'latest-run')
  assert.equal(readRememberedRunId(storage), 'latest-run')
})

test('Material Planning uses an exact Jalali Persian and Gregorian English business date', () => {
  assert.equal(formatBusinessDate('2026-08-24', 'fa-IR'), '۱۴۰۵/۰۶/۰۲')
  assert.equal(formatBusinessDate('2026-08-24', 'en'), '24/08/2026')
  assert.equal(businessDirection('fa-IR'), 'rtl')
  assert.equal(businessDirection('en'), 'ltr')
})

test('Material Planning bilingual names use NameEn with NameFa fallback without changing data', () => {
  const bilingual = { nameFa: 'نام ماده', nameEn: 'Material name' }
  assert.equal(localizedName(bilingual, 'fa-IR'), 'نام ماده')
  assert.equal(localizedName(bilingual, 'en'), 'Material name')
  assert.equal(localizedName({ ...bilingual, nameEn: ' ' }, 'en'), 'نام ماده')
})

test('required presentation contracts enumerate every canonical visible field', () => {
  assert.deepEqual([...materialPlanningRequiredRunHeaderFields], [
    'runNumber', 'asOfDate', 'horizonEndDate', 'engineVersion', 'runStatus',
    'planningCompleteness', 'completedAtUtc', 'latestHistoricalIndicator',
  ])
  assert.deepEqual([...materialPlanningRequiredSummaryFields], [
    'officialItemCode', 'itemName', 'itemType', 'category', 'supplyMethod', 'unit',
    'startingOnHand', 'totalFirmIncoming', 'totalNetRequirement', 'stockoutDate',
    'safetyBreachDate', 'stockCoverage', 'firmCoverage', 'overdueFirmQty',
    'missingEtaFirmQty', 'riskBand', 'planningCompleteness', 'exceptionCount',
  ])
  assert.deepEqual([...materialPlanningRequiredSupplyTraceFields], [
    'purchaseOrderNumber', 'purchaseOrderLineId', 'deliveryScheduleId', 'supplierSnapshot',
    'openQty', 'eta', 'etaStatus', 'appliedState',
  ])
})

test('Exception source navigation is emitted only for sufficient immutable references and real routes', () => {
  assert.equal(sourceNavigation('BOM', 'BOM-001'), '/mrp/engineering/bom?search=BOM-001')
  assert.equal(sourceNavigation('PURCHASE_SUPPLY', 'PO-001'), '/mrp/supply/purchase-orders?search=PO-001')
  assert.equal(sourceNavigation('INVENTORY', 'WH-0001'), '/inventory/warehouses?search=WH-0001')
  assert.equal(sourceNavigation('FORECAST', null), null)
  assert.equal(sourceNavigation('MRP', 'technical-only'), null)
})

let viteServer: ViteDevServer | undefined
let loadedPage: React.ComponentType | undefined

after(async () => {
  await viteServer?.close()
})

test('actual router restores a selected historical run and preserves URL Back Forward and refresh context', async () => {
  const restoreFetch = installMaterialPlanningFetch()
  const storage = new MemoryStorage()
  const restoreWindow = installWindow(storage)
  const translation = await testI18n('en')
  let currentLocation = ''
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'en', [materialPlanningPath], value => { currentLocation = value })
    assert.match(currentLocation, new RegExp(`runId=${latestRunId}`))

    await changeSelectedRun(renderer, historicalRunId)
    assert.match(currentLocation, new RegExp(`runId=${historicalRunId}`))
    assert.equal(readRememberedRunId(storage), historicalRunId)

    await clickAction(renderer, 'away')
    assert.equal(currentLocation, '/mrp/master-data/products')
    await clickAction(renderer, 'material-planning')
    await flushEffects()
    assert.match(currentLocation, new RegExp(`runId=${historicalRunId}`))
    assert.doesNotMatch(currentLocation, new RegExp(`runId=${latestRunId}`))

    await clickAction(renderer, 'away')
    await clickAction(renderer, 'back')
    assert.match(currentLocation, new RegExp(`runId=${historicalRunId}`))
    await clickAction(renderer, 'forward')
    assert.equal(currentLocation, '/mrp/master-data/products')
    await clickAction(renderer, 'material-planning')
    await flushEffects()

    const refreshLocation = currentLocation
    await act(async () => renderer?.unmount())
    renderer = await renderPage(translation, 'en', [refreshLocation], value => { currentLocation = value })
    assert.match(currentLocation, new RegExp(`runId=${historicalRunId}`))

    await changeSelectedRun(renderer, latestRunId)
    assert.match(currentLocation, new RegExp(`runId=${latestRunId}`))
    assert.equal(readRememberedRunId(storage), latestRunId)
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

test('actual English page renders all Run Header Material Summary and Supply Trace fields in LTR', async () => {
  const restoreFetch = installMaterialPlanningFetch()
  const restoreWindow = installWindow(new MemoryStorage())
  const translation = await testI18n('en')
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'en',
      [`${materialPlanningPath}?runId=${historicalRunId}&itemId=${itemId}`], () => undefined)
    await clickLineage(renderer)
    const text = renderedText(renderer.toJSON())
    const localeRoot = renderer.root.findByProps({ 'data-material-planning-locale': 'en' })
    assert.equal(localeRoot.props.dir, 'ltr')

    for (const expected of [
      'Material Planning', 'Run number', 'MRP-RUN-110001', 'As-of date', '24/08/2026',
      'Horizon end', '02/09/2026', 'Engine version', 'MRP-10-v1', 'Status', 'Completed',
      'Completeness', 'Complete', 'Completed at', 'Historical run',
      'Starting on-hand', '125.5', 'Firm incoming', '30.3', 'Net requirement', '14.8',
      'Stockout date', '26/08/2026', 'Safety breach', '25/08/2026', 'Stock coverage',
      'Firm coverage', 'Overdue firm', '7.5', 'Missing ETA firm', '4.3', 'Risk band', 'Critical',
      'نام ماده بدون انگلیسی',
      'Supply trace', 'PO-110001', 'PO line ID', 'line-110001', 'Delivery schedule ID',
      'schedule-110001', 'SUP-1101', 'Snapshot Supplier', 'Open quantity', '12.5',
      '28/08/2026', 'ON_TIME', 'applied', 'PO-110002', 'schedule-110002', 'excluded',
      'Production and material operations dashboard', 'Forecast products', 'PRD-1101',
      'Shared demand product', 'Consumed by products', 'Up to date',
      'Open calculation exceptions', 'Why does this item need attention?',
      'Reliable supply for PRT-1101 will no longer cover consumption on 26/08/2026.',
      'This item is consumed by: PRD-1101 — Shared demand product.',
    ]) assert.ok(text.includes(expected), `Expected visible text: ${expected}`)

    const runContextTable = renderer.root.findByProps({ 'aria-label': 'Historical run context' })
    assert.equal(runContextTable.type, 'div')
    assert.ok(runContextTable.findAllByType('strong').length >= 2)
    assert.equal(renderer.root.findAll(node => node.props.className === 'mrp-run-technical-details').length, 1)
    const detailTable = renderer.root.findByProps({ 'aria-label': 'Operational material detail' })
    assert.equal(detailTable.type, 'table')
    assert.equal(detailTable.findAllByType('thead')[0].findAllByType('th').length, 3)
    assert.equal(detailTable.findAllByType('tbody')[0].findAllByType('tr').length, 8)
    assert.ok(renderer.root.findAllByProps({ className: 'trace-report' }).length >= 6)
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

test('advanced planning filters are progressively disclosed without losing the operational cockpit', async () => {
  const restoreFetch = installMaterialPlanningFetch()
  const restoreWindow = installWindow(new MemoryStorage())
  const translation = await testI18n('en')
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'en', [`${materialPlanningPath}?runId=${latestRunId}`], () => undefined)
    const toggle = renderer.root.findAllByType('button').find(node => instanceText(node).includes('More filters'))
    assert.ok(toggle, 'More filters action must remain visible')
    assert.equal(toggle.props['aria-expanded'], false)
    const dateInputsBefore = renderer.root.findAll(node => node.type === 'input' && node.props.type === 'date').length

    await act(async () => toggle.props.onClick())
    await flushEffects()

    const expanded = renderer.root.findAllByType('button').find(node => instanceText(node).includes('Hide more filters'))
    assert.ok(expanded)
    assert.equal(expanded.props['aria-expanded'], true)
    assert.equal(renderer.root.findAll(node => node.type === 'input' && node.props.type === 'date').length, dateInputsBefore + 2)
    assert.ok(renderedText(renderer.toJSON()).includes('Production and material operations dashboard'))
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

test('empty optional filters are omitted and one failed card does not replace the dashboard', async () => {
  const requests: string[] = []
  const restoreFetch = installMaterialPlanningFetch(requests, '/exceptions')
  const restoreWindow = installWindow(new MemoryStorage())
  const translation = await testI18n('fa-IR')
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'fa-IR', [`${materialPlanningPath}?runId=${latestRunId}`], () => undefined)
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 180)) })
    await flushEffects()
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('داشبورد عملیاتی تولید و مواد'))
    assert.ok(text.includes('PRD-1101'))
    assert.ok(text.includes('بارگذاری مرکز استثناها در سرور انجام نشد.'))
    assert.ok(!text.includes('An error occurred while processing your request'))
    const summaryRequest = requests.find(value => value.includes('/summary?') && value.includes('pageSize=25'))
    const exceptionRequest = requests.find(value => value.includes('/exceptions?'))
    assert.ok(summaryRequest)
    assert.ok(exceptionRequest)
    for (const request of [summaryRequest, exceptionRequest]) {
      assert.ok(!request.includes('categoryId='))
      assert.ok(!request.includes('stockoutFrom='))
      assert.ok(!request.includes('stockoutTo='))
      assert.ok(!request.includes('date='))
    }
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

test('actual Persian Material Planning page renders RTL labels Jalali dates and Persian business names', async () => {
  const restoreFetch = installMaterialPlanningFetch()
  const restoreWindow = installWindow(new MemoryStorage())
  const translation = await testI18n('fa-IR')
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'fa-IR',
      [`${materialPlanningPath}?runId=${historicalRunId}&itemId=${itemId}`], () => undefined)
    const text = renderedText(renderer.toJSON())
    const localeRoot = renderer.root.findByProps({ 'data-material-planning-locale': 'fa-IR' })
    assert.equal(localeRoot.props.dir, 'rtl')
    const runContextTable = renderer.root.findByProps({ 'aria-label': 'اطلاعات اجرای انتخاب‌شده' })
    assert.equal(runContextTable.type, 'div')
    assert.ok(runContextTable.findAllByType('strong').length >= 2)
    assert.ok(text.includes('جزئیات عملیاتی ماده'))
    assert.ok(text.includes('موجودی شروع'))
    assert.ok(text.includes('تاریخ مبنای محاسبه'))
    assert.ok(text.includes('۱۴۰۵/۰۶/۰۲'))
    assert.ok(text.includes('نام ماده Snapshot'))
    assert.ok(text.includes('نام ماده بدون انگلیسی'))
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

test('Planner Inbox is server-paged, supports saved views and acknowledges without mutating Run exceptions', async () => {
  const requests: string[] = []
  const restoreFetch = installMaterialPlanningFetch(requests)
  const restoreWindow = installWindow(new MemoryStorage())
  const translation = await testI18n('en')
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'en',
      [`${materialPlanningPath}?runId=${latestRunId}&view=inbox&inboxPage=2`], () => undefined)
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 180)) })
    await flushEffects()
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('Planner inbox'))
    assert.ok(text.includes('TEST_PLANNER_EXCEPTION'))
    assert.ok(text.includes('Snapshot Planner'))
    assert.ok(text.includes('My critical exceptions'))
    assert.ok(requests.some(value => value.includes('/planner-inbox?') && value.includes('page=2')))
    assert.ok(requests.some(value => value.includes('/planner-inbox?') && value.includes(`runId=${latestRunId}`)))
    assert.ok(requests.every(value => !value.includes('assignedUserId=')))
    assert.ok(requests.some(value => value.includes('/planner-assignees?') && value.includes('pageSize=25')))

    const assigneeSearch = renderer.root.findByProps({ 'aria-label': 'Search assignable users' })
    await act(async () => assigneeSearch.props.onChange({ target: { value: 'warehouse' } }))
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 180)) })
    await flushEffects()
    assert.ok(requests.some(value => value.includes('/planner-assignees?') && value.includes('search=warehouse') && value.includes('page=1')))

    const acknowledge = renderer.root.findAllByType('button')
      .find(node => instanceText(node).includes('Acknowledge'))
    assert.ok(acknowledge)
    await act(async () => acknowledge.props.onClick())
    await flushEffects()
    assert.ok(requests.includes('POST /api/material-planning/planner-work-items/work-item-1/acknowledge'))
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

test('What-if comparison is visibly non-official, snapshot-based and keeps server paging reachable', async () => {
  const requests: string[] = []
  const restoreFetch = installMaterialPlanningFetch(requests)
  const restoreWindow = installWindow(new MemoryStorage())
  const translation = await testI18n('fa-IR')
  let renderer: ReactTestRenderer | undefined
  try {
    renderer = await renderPage(translation, 'fa-IR',
      [`${materialPlanningPath}?runId=${latestRunId}&view=whatif&scenarioId=scenario-1&scenarioResultPage=2`],
      () => undefined)
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 180)) })
    await flushEffects()
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('سناریوی چه می‌شود اگر؟'))
    assert.ok(text.includes('غیررسمی'))
    assert.ok(text.includes('PRT-1101'))
    assert.ok(text.includes('11'))
    assert.ok(requests.some(value => value.includes('/what-if-scenarios/scenario-1/results?') && value.includes('page=2')))
    assert.ok(requests.some(value => value.includes('/what-if/items?') && value.includes('pageSize=25')))
    assert.ok(requests.some(value => value.includes('/what-if/firm-supplies?') && value.includes('pageSize=25')))
  } finally {
    if (renderer) await act(async () => renderer?.unmount())
    restoreWindow()
    restoreFetch()
  }
})

async function pageComponent() {
  if (loadedPage) return loadedPage
  viteServer = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  loadedPage = (await viteServer.ssrLoadModule('/src/MaterialPlanningPage.tsx')).default as React.ComponentType
  return loadedPage
}

async function testI18n(language: 'fa-IR' | 'en') {
  const instance = createInstance()
  await instance.init({ resources: { 'fa-IR': { translation: fa }, en: { translation: en } }, lng: language,
    fallbackLng: 'fa-IR', supportedLngs: ['fa-IR', 'en'], interpolation: { escapeValue: false }, initAsync: false })
  return instance
}

async function renderPage(translation: i18n, language: 'fa-IR' | 'en', initialEntries: string[],
  onLocation: (value: string) => void) {
  const Page = await pageComponent()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement(MemoryRouter, { initialEntries },
        React.createElement(MaterialPlanningTestRouter, { Page, language, onLocation }))))
  })
  await flushEffects()
  return renderer
}

function MaterialPlanningTestRouter({ Page, language, onLocation }: {
  Page: React.ComponentType
  language: 'fa-IR' | 'en'
  onLocation: (value: string) => void
}) {
  const navigate = useNavigate()
  const location = useLocation()
  useEffect(() => { onLocation(`${location.pathname}${location.search}`) }, [location, onLocation])
  return React.createElement('div', { dir: businessDirection(language), lang: language,
    'data-material-planning-locale': language },
  React.createElement('button', { 'data-action': 'away', onClick: () => navigate('/mrp/master-data/products') }, 'Products'),
  React.createElement('button', { 'data-action': 'material-planning', onClick: () => navigate(materialPlanningPath) }, 'Material Planning'),
  React.createElement('button', { 'data-action': 'back', onClick: () => navigate(-1) }, 'Back'),
  React.createElement('button', { 'data-action': 'forward', onClick: () => navigate(1) }, 'Forward'),
  React.createElement(Routes, null,
    React.createElement(Route, { path: materialPlanningPath, element: React.createElement(Page) }),
    React.createElement(Route, { path: '/mrp/master-data/products', element: React.createElement('main', null, 'Products workspace') })))
}

async function changeSelectedRun(renderer: ReactTestRenderer, runId: string) {
  const selector = renderer.root.findAll(node => node.type === 'select' &&
    [latestRunId, historicalRunId].includes(String(node.props.value)))[0]
  assert.ok(selector, 'MRP Run selector must be rendered')
  await act(async () => selector.props.onChange({ target: { value: runId } }))
  await flushEffects()
}

async function clickAction(renderer: ReactTestRenderer, action: string) {
  const button = renderer.root.findByProps({ 'data-action': action })
  await act(async () => button.props.onClick())
  await flushEffects()
}

async function clickLineage(renderer: ReactTestRenderer) {
  const button = renderer.root.findAllByType('button').find(node => instanceText(node).toLowerCase().includes('lineage'))
  assert.ok(button, 'Daily lineage action must be visible')
  await act(async () => button.props.onClick())
  await flushEffects()
}

function instanceText(node: { children: Array<string | { children: unknown[] }> }): string {
  return node.children.map(child => typeof child === 'string' ? child :
    instanceText(child as { children: Array<string | { children: unknown[] }> })).join(' ')
}

async function flushEffects() {
  await act(async () => {
    await new Promise(resolve => setTimeout(resolve, 0))
    await new Promise(resolve => setTimeout(resolve, 0))
  })
}

function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string {
  if (node === null) return ''
  if (typeof node === 'string') return node
  if (Array.isArray(node)) return node.map(renderedText).join(' ')
  return (node.children ?? []).map(child => renderedText(child as ReactTestRendererJSON | string)).join(' ')
}

function installWindow(storage: MemoryStorage) {
  const hadWindow = Object.prototype.hasOwnProperty.call(globalThis, 'window')
  const previous = (globalThis as typeof globalThis & { window?: unknown }).window
  const hadDocument = Object.prototype.hasOwnProperty.call(globalThis, 'document')
  const previousDocument = (globalThis as typeof globalThis & { document?: unknown }).document
  Object.defineProperty(globalThis, 'document', { configurable: true, writable: true,
    value: { documentElement: { lang: '', dir: '' } } })
  Object.defineProperty(globalThis, 'window', { configurable: true, writable: true, value: {
    localStorage: storage,
    fetch: globalThis.fetch.bind(globalThis),
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => true,
    setTimeout: globalThis.setTimeout.bind(globalThis),
    clearTimeout: globalThis.clearTimeout.bind(globalThis),
    setInterval: globalThis.setInterval.bind(globalThis),
    clearInterval: globalThis.clearInterval.bind(globalThis),
  } })
  ;(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
  return () => {
    if (hadWindow) Object.defineProperty(globalThis, 'window', { configurable: true, writable: true, value: previous })
    else delete (globalThis as typeof globalThis & { window?: unknown }).window
    if (hadDocument) Object.defineProperty(globalThis, 'document', { configurable: true, writable: true, value: previousDocument })
    else delete (globalThis as typeof globalThis & { document?: unknown }).document
  }
}

function installMaterialPlanningFetch(requests: string[] = [], failedPathSuffix?: string) {
  const previous = globalThis.fetch
  globalThis.fetch = async (input, init) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
      'http://hadron.test')
    const path = url.pathname
    const method = init?.method ?? (input instanceof Request ? input.method : 'GET')
    requests.push(`${method.toUpperCase()} ${path}${url.search}`)
    if (failedPathSuffix && path.endsWith(failedPathSuffix)) return new Response(JSON.stringify({
      type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
      title: 'An error occurred while processing your request.',
      status: 500,
      traceId: 'trace-card-isolation',
    }), { status: 500, headers: { 'content-type': 'application/problem+json' } })
    if (path === '/api/material-planning/planner-inbox') return json({ items: [{ id: 'work-item-1', runId: latestRunId,
      exceptionId: 'exception-1', runNumber: 'MRP-RUN-110002', itemId, itemCode: 'PRT-1101', date: '2026-08-26',
      code: 'TEST_PLANNER_EXCEPTION', severity: 'WARNING', affectsDemand: true, message: 'Snapshot exception',
      assignedUserId: 'user-1', assignedUserName: 'planner', assignedUserFullName: 'Snapshot Planner', status: 'OPEN',
      updatedAtUtc: '2026-08-24T06:05:00Z', version: 1 }], page: 2, pageSize: 25, totalCount: 26 })
    if (path === '/api/material-planning/planner-assignees') return json({ items: [
      { id: 'user-1', userName: 'planner', fullName: 'Snapshot Planner' }], page: Number(url.searchParams.get('page') ?? 1), pageSize: 25, totalCount: 101 })
    if (path === '/api/material-planning/saved-views') return json([
      { id: 'view-1', name: 'My critical exceptions', filtersJson: '{"inboxStatus":"OPEN"}', updatedAtUtc: '2026-08-24T06:05:00Z', version: 1 }])
    if (path.includes('/planner-work-items/') && method.toUpperCase() === 'POST') return json({})
    if (path === '/api/material-planning/what-if-scenarios' && method.toUpperCase() === 'GET') return json({ items: [
      { id: 'scenario-1', baseRunId: latestRunId, baseRunNumber: 'MRP-RUN-110002', name: 'سناریوی تست',
        status: 'CALCULATED', completeness: 'COMPLETE', createdAtUtc: '2026-08-24T06:10:00Z', overrideCount: 3 }],
      page: 1, pageSize: 25, totalCount: 1 })
    if (path.endsWith('/what-if/items')) return json({ items: [
      { id: productId, code: 'PRD-1101', nameFa: 'محصول', nameEn: 'Product', kind: 'PRODUCT' },
      { id: itemId, code: 'PRT-1101', nameFa: 'ماده', nameEn: 'Material', kind: 'PART' }],
      page: 1, pageSize: 25, totalCount: 102 })
    if (path.endsWith('/what-if/firm-supplies')) return json({ items: [{ deliveryScheduleId: 'schedule-110001',
      itemId, itemCode: 'PRT-1101', itemNameFa: 'ماده', itemNameEn: 'Material', purchaseOrderNumber: 'PO-110001',
      openQty: 12.5, eta: '2026-08-28', etaStatus: 'ON_TIME' }], page: 1, pageSize: 25, totalCount: 101 })
    if (path.endsWith('/what-if/forecast-periods')) return json([{ productId, businessPeriodId: 'period-1',
      periodStartDate: '2026-08-25', periodEndDate: '2026-09-22', officialForecastQty: 10,
      actualMtdQty: 0, allocationState: 'ALLOCATED' }])
    if (path.endsWith('/what-if-scenarios/scenario-1/results')) return json({ items: [{ itemId,
      itemCode: 'PRT-1101', nameFa: 'ماده', nameEn: 'Material', itemType: 'PART', officialStartingOnHand: 0,
      scenarioStartingOnHand: 5, officialNetRequirement: 8, scenarioNetRequirement: 11,
      netRequirementDelta: 3, officialStockoutDate: '2026-08-25', scenarioStockoutDate: '2026-08-26',
      completeness: 'COMPLETE' }], page: 2, pageSize: 25, totalCount: 26 })
    if (path === '/api/material-planning/refresh-status') return json({ status: 'UP_TO_DATE', latestRunId,
      latestRunNumber: 'MRP-RUN-110002', latestAsOfDate: '2026-08-24', lastUpdatedAtUtc: '2026-08-24T06:05:00Z', pendingChangeCount: 0 })
    if (path === '/api/material-planning/runs') return json({ items: runList(), page: 1, pageSize: 25, totalCount: 2 })
    if (path.endsWith('/facets')) return json({ categories: [], supplyMethods: [] })
    if (path.endsWith(`/products/${productId}/materials`)) return json({ items: summaryRows(), page: 1, pageSize: 25, totalCount: 2 })
    if (path.endsWith('/products')) return json({ items: productRows(), page: 1, pageSize: 25, totalCount: 1 })
    if (path.endsWith('/summary')) return json({ items: summaryRows(), page: 1, pageSize: 25, totalCount: 2 })
    if (path.endsWith('/exceptions')) return json({ items: [], page: 1, pageSize: 25, totalCount: 0 })
    if (path.endsWith('/timeline')) return json(timeline())
    if (path.endsWith('/trace')) return json(trace())
    const run = runList().find(row => path.endsWith(`/runs/${row.id}`))
    if (run) return json({ ...run, startedAtUtc: '2026-08-24T06:00:00Z',
      snapshotCapturedAtUtc: '2026-08-24T06:01:00Z', isLatest: run.id === latestRunId, materialCount: 2 })
    return new Response('Not found', { status: 404 })
  }
  return () => { globalThis.fetch = previous }
}

function runList() {
  const shared = { asOfDate: '2026-08-24', horizonEndDate: '2026-09-02', engineVersion: 'MRP-10-v1',
    completeness: 'COMPLETE', completedAtUtc: '2026-08-24T06:05:00Z', exceptionCount: 0 }
  return [
    { ...shared, id: latestRunId, runNumber: 'MRP-RUN-110002', status: 'COMPLETED' },
    { ...shared, id: historicalRunId, runNumber: 'MRP-RUN-110001', status: 'COMPLETED_WITH_EXCEPTIONS',
      completedAtUtc: '2026-08-24T05:05:00Z', exceptionCount: 1 },
  ]
}

function summaryRows() {
  const item = { id: itemId, code: 'PRT-1101', nameFa: 'نام ماده Snapshot', nameEn: 'Snapshot Material',
    itemType: 'PART', unitCode: 'EA', unitNameFa: 'عدد', unitNameEn: 'Each', categoryId: 'category-1',
    categoryCode: 'CAT', categoryNameFa: 'گروه', categoryNameEn: 'Category', supplyMethod: 'MAKE' }
  const quantities = { startingOnHand: 125.5, totalGrossDemand: 40, totalFirmIncoming: 30.25,
    totalNetRequirement: 14.75, overdueFirmQty: 7.5, missingEtaFirmQty: 4.25, stockoutDate: '2026-08-26',
    safetyBreachDate: '2026-08-25', stockCoverageState: 'DATE', stockCoverageDate: '2026-08-25',
    stockCoverageDays: 1, firmCoverageState: 'DATE', firmCoverageDate: '2026-08-27', firmCoverageDays: 3,
    completeness: 'COMPLETE', riskBand: 'CRITICAL', riskReason: 'BELOW_CRITICAL_DAYS', exceptionCount: 1,
    primaryExceptionCode: 'TEST_WARNING' }
  return [
    { item, ...quantities },
    { item: { ...item, id: 'item-fallback', code: 'CMP-1102', nameFa: 'نام ماده بدون انگلیسی', nameEn: '' },
      ...quantities, stockoutDate: null, safetyBreachDate: null, exceptionCount: 0, primaryExceptionCode: null },
  ]
}

function productRows() {
  return [{ product: { id: productId, code: 'PRD-1101', nameFa: 'محصول تقاضای مشترک', nameEn: 'Shared demand product',
    itemType: 'PRODUCT', unitCode: 'EA', unitNameFa: 'عدد', unitNameEn: 'Each' }, plannedProductionQty: 1000,
    materialCount: 2, attentionMaterialCount: 1, stockCoverageState: 'DATE', stockCoverageDate: '2026-08-25',
    stockCoverageDays: 1, firmCoverageState: 'DATE', firmCoverageDate: '2026-08-27', firmCoverageDays: 3,
    firstConstraintMaterial: summaryRows()[0].item, firstConstraintDate: '2026-08-26', riskBand: 'CRITICAL',
    completeness: 'COMPLETE' }]
}

function timeline() {
  return { item: summaryRows()[0].item, grain: 'DAILY', safetyStockQty: 10, stockCoverageState: 'DATE',
    stockCoverageDate: '2026-08-25', stockCoverageDays: 1, firmCoverageState: 'DATE',
    firmCoverageDate: '2026-08-27', firmCoverageDays: 3, usedByProducts: [{ id: productId,
      code: 'PRD-1101', nameFa: 'محصول تقاضای مشترک', nameEn: 'Shared demand product' }], buckets: [{ startDate: '2026-08-26',
      endDate: '2026-08-26', grossDemand: 5, firmIncoming: 2, netRequirement: 3, firmProjected: -1,
      planningBalance: 10, stockOnlyProjected: -5, coverageDays: 0, zone: 'CRITICAL', completeness: 'COMPLETE', includesStockoutEvent: true,
      includesSafetyBreachEvent: false }] }
}

function trace() {
  const supply = { purchaseOrderId: 'po-id', purchaseOrderLineId: 'line-110001', supplierNumber: 'SUP-1101',
    supplierNameFa: 'تأمین‌کننده Snapshot', supplierNameEn: 'Snapshot Supplier', openQty: 12.5,
    eta: '2026-08-28', etaSource: 'ORIGINAL', etaStatus: 'ON_TIME', purchaseOrderStatus: 'ORDERED' }
  return { runId: historicalRunId, item: summaryRows()[0].item, demandDate: '2026-08-26', origins: [], bom: [],
    inventory: [], companyStartingOnHand: 125.5, includesInactiveWarehouseStock: false, firmSupply: [
      { ...supply, purchaseOrderNumber: 'PO-110001', deliveryScheduleId: 'schedule-110001', appliedByDemandDate: true },
      { ...supply, purchaseOrderNumber: 'PO-110002', purchaseOrderLineId: 'line-110002',
        deliveryScheduleId: 'schedule-110002', appliedByDemandDate: false },
    ], netting: { grossRequirement: 5, firmIncoming: 2, safetyStock: 10, netRequirement: 3,
      firmProjectedAfter: -1, planningBalance: 10 }, route: { chosenRoute: 'BOM', decisionSource: 'SNAPSHOT',
      exceptionCodes: [] } }
}

function json(value: unknown) {
  return new Response(JSON.stringify(value), { status: 200, headers: { 'content-type': 'application/json' } })
}
