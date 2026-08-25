import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { act, create, type ReactTestRenderer, type ReactTestRendererJSON } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'

const runId = '00000000-0000-0000-0000-000000000012'
const recommendationId = '00000000-0000-0000-0000-000000001200'
let server: ViteDevServer | undefined
let Page: React.ComponentType<{ view: 'list' | 'object' }> | undefined

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  window: { setTimeout: globalThis.setTimeout, clearTimeout: globalThis.clearTimeout,
    localStorage: { getItem: () => null, setItem: () => undefined } },
})

after(async () => server?.close())

test('Order Planning worklist sends URL paging and filters to the server beyond the first 100 records', async () => {
  const calls: string[] = []
  const restore = installFetch((input) => {
    calls.push(input)
    if (input.includes('/order-planning/runs')) return json({ items: [{ id: runId, runNumber: 'MRP-RUN-000012', status: 'COMPLETED' }], page: 1, pageSize: 25, totalCount: 1 })
    if (input.includes('/order-planning/generations')) return json([{ id: 'gen-current', generationSequence: 2, status: 'COMPLETED', isCurrent: true }])
    return json({ items: [listRow()], page: 2, pageSize: 100, totalCount: 125 })
  })
  const renderer = await render('list', 'en', `/mrp/supply/order-planning?runId=${runId}&generationId=gen-current&page=2&pageSize=100&search=CMP`)
  try {
    assert.ok(calls.some(value => value.includes('page=2') && value.includes('pageSize=100') && value.includes('search=CMP')))
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('CMP-0001'), text)
    assert.ok(text.includes('Page 2 of 2'))
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('explicit historical generationId remains URL authority and is sent to the backend', async () => {
  const historical = '00000000-0000-0000-0000-000000001111'; const calls: string[] = []
  const restore = installFetch((input) => {
    calls.push(input)
    if (input.includes('/order-planning/runs')) return json({ items: [{ id: runId, runNumber: 'MRP-RUN-000012', status: 'COMPLETED' }] })
    if (input.includes('/order-planning/generations')) return json([
      { id: 'gen-current', generationSequence: 2, status: 'COMPLETED', isCurrent: true },
      { id: historical, generationSequence: 1, status: 'COMPLETED', isCurrent: false },
    ])
    return json({ items: [{ ...listRow(), generationId: historical, generationSequence: 1, isCurrentGeneration: false }], page: 1, pageSize: 25, totalCount: 1 })
  })
  const renderer = await render('list', 'en', `/mrp/supply/order-planning?runId=${runId}&generationId=${historical}`)
  try {
    assert.ok(calls.some(value => value.includes(`generationId=${historical}`)))
    assert.ok(renderedText(renderer.toJSON()).includes('Historical'))
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('invalid generationId is surfaced and is never silently replaced by current generation', async () => {
  const invalid = '00000000-0000-0000-0000-000000009999'; const calls: string[] = []
  const restore = installFetch((input) => {
    calls.push(input)
    if (input.includes('/order-planning/runs')) return json({ items: [{ id: runId, runNumber: 'MRP-RUN-000012', status: 'COMPLETED' }] })
    if (input.includes('/order-planning/generations')) return json([
      { id: 'gen-current', generationSequence: 2, status: 'COMPLETED', isCurrent: true },
    ])
    return json({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  })
  const renderer = await render('list', 'en', `/mrp/supply/order-planning?runId=${runId}&generationId=${invalid}`)
  try {
    assert.ok(calls.some(value => value.includes(`generationId=${invalid}`)))
    assert.ok(renderedText(renderer.toJSON()).includes('Generation context is unavailable'))
    assert.ok(!calls.some(value => value.includes('generationId=gen-current')))
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('English detail renders immutable machine context separately from expert decisions and preserves list URL context', async () => {
  const restore = installFetch(() => json(detailRow()))
  const renderer = await render('object', 'en',
    `/mrp/order-planning/${recommendationId}?runId=${runId}&page=3&priority=P2_UPCOMING`)
  try {
    const text = renderedText(renderer.toJSON())
    for (const expected of ['CMP-0001 — Test component', 'MRP-RUN-000012', '25/08/2026', 'MRP-12-v1',
      'Test component', 'SRC-0002', 'SUP-0002', '80', '7 CNY', '560 CNY',
      'P2_UPCOMING', 'ORDER_BY_DATE', 'PREFERRED_ELIGIBLE_ON_TIME', 'Expert decision', 'Decision history'])
      assert.ok(text.includes(expected), `Expected visible text: ${expected}\n${text}`)
    assert.ok(!text.toLowerCase().includes('create po'))
    const back = renderer.root.findAllByType('a').find(node => String(node.props.href).includes('/mrp/supply/order-planning'))
    assert.ok(back)
    assert.match(back.props.href, /runId=.*page=3.*priority=P2_UPCOMING/)
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Persian detail uses NameFa Jalali dates and RTL while stored machine codes remain unchanged', async () => {
  const restore = installFetch(() => json(detailRow()))
  const renderer = await render('object', 'fa-IR', `/mrp/order-planning/${recommendationId}`)
  try {
    const root = renderer.root.findByProps({ 'data-order-planning-locale': 'fa-IR' })
    assert.equal(root.props.dir, 'rtl')
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('قطعه آزمون'))
    assert.ok(text.includes('۱۴۰۵/۰۶/۰۳'))
    assert.ok(text.includes('SRC-0002'))
    assert.ok(!text.includes('Test component'))
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('blocked machine recommendation prevents Accept but still permits append-only Reject or Defer choice', async () => {
  const blocked = { ...detailRow(), state: 'BLOCKED', reasonCodes: ['NO_READY_SOURCE'], selectedCandidate: null, candidates: [] }
  const restore = installFetch(() => json(blocked))
  const renderer = await render('object', 'en', `/mrp/order-planning/${recommendationId}`)
  try {
    const save = () => renderer.root.findAllByType('button').find(node => instanceText(node).includes('Save'))!
    assert.equal(save().props.disabled, true)
    const decision = renderer.root.findAllByType('select').find(node => node.props.value === 'ACCEPT')!
    await act(async () => decision.props.onChange({ target: { value: 'REJECT' } }))
    assert.equal(save().props.disabled, false)
  } finally { await act(async () => renderer.unmount()); restore() }
})

async function render(view: 'list' | 'object', language: 'fa-IR' | 'en', initialEntry: string) {
  const translation = await translations(language)
  const Component = await component()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement('div', { dir: language === 'fa-IR' ? 'rtl' : 'ltr', lang: language,
        'data-order-planning-locale': language },
      React.createElement(MemoryRouter, { initialEntries: [initialEntry] },
        React.createElement(Routes, null,
          React.createElement(Route, { path: '/mrp/supply/order-planning', element: React.createElement(Component, { view: 'list' }) }),
          React.createElement(Route, { path: '/mrp/order-planning/:id', element: React.createElement(Component, { view }) }))))))
  })
  await flush()
  return renderer
}

async function component() {
  if (Page) return Page
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Page = (await server.ssrLoadModule('/src/OrderPlanningPage.tsx')).default as React.ComponentType<{ view: 'list' | 'object' }>
  return Page
}

async function translations(language: 'fa-IR' | 'en') {
  const instance = createInstance()
  await instance.init({ resources: { 'fa-IR': { translation: fa }, en: { translation: en } }, lng: language,
    fallbackLng: 'fa-IR', supportedLngs: ['fa-IR', 'en'], interpolation: { escapeValue: false }, initAsync: false })
  return instance
}

function installFetch(handler: (input: string) => Response) {
  const original = globalThis.fetch
  globalThis.fetch = (input: string | URL | Request) => Promise.resolve(handler(String(input)))
  return () => { globalThis.fetch = original }
}

function json(value: unknown) { return new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } }) }
function listRow() { return { ...detailRow(), candidates: undefined, decisions: undefined } }
function detailRow() {
  return {
    id: recommendationId, generationId: 'g1', runId, runNumber: 'MRP-RUN-000012', generationSequence: 1,
    runAsOfDate: '2026-08-25', evaluationDate: '2026-08-25', generatedAtUtc: '2026-08-25T05:00:00Z',
    isCurrentGeneration: true, isOutdatedRun: false, itemId: 'i1', itemCode: 'CMP-0001',
    itemNameFa: 'قطعه آزمون', itemNameEn: 'Test component', itemType: 'COMPONENT', unitCode: 'PCS',
    needByDate: '2026-08-30', routeOrigin: 'BUY', orderByDate: '2026-08-27', targetReceiptDate: '2026-08-30',
    decisionHorizonDate: '2026-09-01', earliestFeasibleReceiptDate: '2026-08-28', systemRecommendedQty: 80,
    baseRecommendedQty: 55, minimumBaseline: -45, netRequirementQty: 7, safetyStockQty: 10,
    state: 'READY', timingState: 'ON_TIME', priority: 'P2_UPCOMING', nextAction: 'ORDER_BY_DATE', estimatedValue: 560,
    reasonCodes: [], sourceSelectionReason: 'PREFERRED_ELIGIBLE_ON_TIME', calculationTrace: '{}', algorithmVersion: 'MRP-12-v1',
    decisionVersion: 0, sourcingNumber: 'SRC-0002', supplierNumber: 'SUP-0002', supplierNameFa: 'تأمین‌کننده',
    supplierNameEn: 'Supplier', currencyCode: 'CNY', latestDecisionType: null, reviewDate: null,
    selectedCandidate: candidate(), candidates: [candidate()], decisions: [],
  }
}
function candidate() { return { id: 'c1', sourcingOptionId: 's1', sourcingNumber: 'SRC-0002', supplierId: 'sup1', supplierNumber: 'SUP-0002', supplierNameFa: 'تأمین‌کننده', supplierNameEn: 'Supplier', isPreferred: true, isEligible: true, reasonCodes: [], minimumOrderQuantity: 80, totalLeadTimeDays: 3, earliestFeasibleReceiptDate: '2026-08-28', canMeetNeedBy: true, unitPrice: 7, currencyCode: 'CNY', shippingModeCode: 'SEA', incotermCode: null } }

async function flush() { await act(async () => { for (let index = 0; index < 4; index++) await new Promise(resolve => setTimeout(resolve, 10)) }) }
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string {
  if (node == null) return ''
  if (typeof node === 'string') return node
  if (Array.isArray(node)) return node.map(renderedText).join(' ')
  return node.children?.map(child => renderedText(child as ReactTestRendererJSON | string)).join(' ') ?? ''
}
function instanceText(node: { children: Array<string | { children: unknown[] }> }): string {
  return node.children.map(child => typeof child === 'string' ? child : instanceText(child as { children: Array<string | { children: unknown[] }> })).join(' ')
}
