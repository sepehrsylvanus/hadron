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
  assert.equal(sourceNavigation('INVENTORY', 'WH-0001'), '/mrp/inventory/warehouses?search=WH-0001')
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
      'Starting on-hand', '125.5', 'Firm incoming', '30.25', 'Net requirement', '14.75',
      'Stockout date', '26/08/2026', 'Safety breach', '25/08/2026', 'Stock coverage',
      'Firm coverage', 'Overdue firm', '7.5', 'Missing ETA firm', '4.25', 'Risk band', 'Critical',
      'نام ماده بدون انگلیسی',
      'Supply trace', 'PO-110001', 'PO line ID', 'line-110001', 'Delivery schedule ID',
      'schedule-110001', 'SUP-1101', 'Snapshot Supplier', 'Open quantity', '12.5',
      '28/08/2026', 'ON_TIME', 'applied', 'PO-110002', 'schedule-110002', 'excluded',
    ]) assert.ok(text.includes(expected), `Expected visible text: ${expected}`)
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
    assert.ok(text.includes('جزئیات عملیاتی ماده'))
    assert.ok(text.includes('موجودی شروع'))
    assert.ok(text.includes('تاریخ As-Of'))
    assert.ok(text.includes('۱۴۰۵/۰۶/۰۲'))
    assert.ok(text.includes('نام ماده Snapshot'))
    assert.ok(text.includes('نام ماده بدون انگلیسی'))
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
  Object.defineProperty(globalThis, 'window', { configurable: true, writable: true, value: {
    localStorage: storage,
    setTimeout: globalThis.setTimeout.bind(globalThis),
    clearTimeout: globalThis.clearTimeout.bind(globalThis),
  } })
  ;(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
  return () => {
    if (hadWindow) Object.defineProperty(globalThis, 'window', { configurable: true, writable: true, value: previous })
    else delete (globalThis as typeof globalThis & { window?: unknown }).window
  }
}

function installMaterialPlanningFetch() {
  const previous = globalThis.fetch
  globalThis.fetch = async input => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
      'http://hadron.test')
    const path = url.pathname
    if (path === '/api/material-planning/runs') return json({ items: runList(), page: 1, pageSize: 25, totalCount: 2 })
    if (path.endsWith('/facets')) return json({ categories: [], supplyMethods: [] })
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

function timeline() {
  return { item: summaryRows()[0].item, grain: 'DAILY', safetyStockQty: 10, stockCoverageState: 'DATE',
    stockCoverageDate: '2026-08-25', stockCoverageDays: 1, firmCoverageState: 'DATE',
    firmCoverageDate: '2026-08-27', firmCoverageDays: 3, buckets: [{ startDate: '2026-08-26',
      endDate: '2026-08-26', grossDemand: 5, firmIncoming: 2, netRequirement: 3, firmProjected: -1,
      planningBalance: 10, completeness: 'COMPLETE', includesStockoutEvent: true,
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
