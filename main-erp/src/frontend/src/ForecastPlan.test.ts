import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { act, create, type ReactTestRenderer, type ReactTestRendererJSON } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'
import fa from './i18n/locales/fa.ts'

let server: ViteDevServer | undefined
let Module: typeof import('./ForecastBatchEntryPage.tsx') | undefined
let PlanModule: typeof import('./ForecastPlanPages.tsx') | undefined

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  window: {
    fetch: (...args: Parameters<typeof fetch>) => globalThis.fetch(...args),
    setTimeout: globalThis.setTimeout,
    clearTimeout: globalThis.clearTimeout,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  },
  document: {
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  },
})

after(async () => server?.close())

test('named forecast UI builds an inclusive product month matrix and requires explicit conflict confirmation', async () => {
  const product = {
    id: '11111111-1111-1111-1111-111111111111', code: 'PRD-0001', nameFa: 'محصول نمونه', nameEn: 'Sample product', isActive: true,
    productCategory: { id: 'category', code: 'CAT', nameFa: 'گروه', nameEn: 'Category', isActive: true },
    businessUnit: null,
    baseUnit: { id: 'unit', code: 'PCS', nameFa: 'عدد', nameEn: 'Piece', symbol: 'pcs', isActive: true },
  }
  const calls: Array<{ input: string; body?: Record<string, unknown> }> = []
  let saveAttempt = 0
  const originalFetch = globalThis.fetch
  globalThis.fetch = async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input)
    const body = init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : undefined
    calls.push({ input: url, body })
    if (url.endsWith('/planning/business-date')) return json({ jalaliYear: 1405, jalaliMonth: 1 })
    if (url.includes('/planning/forecast-products')) return json({ items: [product], page: 1, pageSize: 20, totalCount: 1 })
    if (url.endsWith('/planning/forecast-plans/preview')) return json({
      name: 'برنامه فروش داخلی', startJalaliDate: '14050101', endJalaliDate: '14050201',
      startDate: '2026-03-21', endDate: '2026-05-21', products: [product],
      months: [
        { periodCode: '140501', jalaliYear: 1405, jalaliMonth: 1, startDate: '2026-03-21', endDate: '2026-04-20', businessPeriodId: null, status: 'NOT_CREATED' },
        { periodCode: '140502', jalaliYear: 1405, jalaliMonth: 2, startDate: '2026-04-21', endDate: '2026-05-21', businessPeriodId: null, status: 'NOT_CREATED' },
      ],
      cells: [
        { productId: product.id, periodCode: '140501', currentForecastQty: 8, currentRevisionNumber: 1, seriesVersion: 2 },
        { productId: product.id, periodCode: '140502', currentForecastQty: null, currentRevisionNumber: null, seriesVersion: null },
      ],
    })
    if (url.endsWith('/planning/forecast-plans')) {
      saveAttempt++
      if (saveAttempt === 1) return json({
        title: 'Planning conflict', code: 'FORECAST_VALUE_CONFLICT',
        detail: 'مقدار متفاوت است.', details: [{
          productId: product.id, productCode: product.code, productNameFa: product.nameFa,
          productNameEn: product.nameEn, periodCode: '140501', existingQty: 8,
          proposedQty: 10, seriesVersion: 2,
        }],
      }, 409)
      return json({ planId: 'plan-1', name: 'برنامه فروش داخلی', startDate: '2026-03-21',
        endDate: '2026-05-21', createdCount: 1, revisedCount: 1, unchangedCount: 0, entryCount: 2 }, 201)
    }
    throw new Error(`Unexpected request ${url}`)
  }

  const renderer = await render()
  try {
    const name = renderer.root.findAllByType('input').find(node => node.props.maxLength === 200)!
    const dates = renderer.root.findAllByType('input').filter(node => node.props.maxLength === 8)
    await act(async () => {
      name.props.onChange({ target: { value: 'برنامه فروش داخلی' } })
      dates[0].props.onChange({ target: { value: '14050101' } })
      dates[1].props.onChange({ target: { value: '14050201' } })
    })
    const picker = renderer.root.findAllByType('button').find(node => node.props['aria-haspopup'] === 'listbox')!
    await act(async () => picker.props.onClick())
    await act(async () => new Promise(resolve => setTimeout(resolve, 240)))
    const option = renderer.root.findAllByType('button').find(node => node.props.role === 'option')!
    await act(async () => option.props.onClick())
    const definitionForm = renderer.root.findAllByType('form')[0]
    await act(async () => definitionForm.props.onSubmit({ preventDefault: () => undefined }))
    await flush()

    const matrixInputs = renderer.root.findAllByType('input').filter(node => String(node.props.className).includes('forecast-matrix-input'))
    assert.equal(matrixInputs.length, 2)
    assert.deepEqual(matrixInputs.map(node => node.props.value), ['', ''])
    await act(async () => {
      matrixInputs[0].props.onChange({ target: { value: '10' } })
      matrixInputs[1].props.onChange({ target: { value: '20' } })
    })
    const save = renderer.root.findAllByType('button').find(node => instanceText(node) === 'ذخیره برنامه پیش‌بینی')!
    await act(async () => save.props.onClick())
    await flush()
    const conflictText = renderedText(renderer.toJSON())
    assert.match(conflictText, /اختلاف با پیش‌بینی موجود/)
    assert.match(conflictText, /8/)
    assert.match(conflictText, /10/)

    const reason = renderer.root.findByType('textarea')
    await act(async () => reason.props.onChange({ target: { value: 'اصلاح مورد تأیید برنامه‌ریز' } }))
    const confirm = renderer.root.findAllByType('button').find(node => instanceText(node) === 'تأیید و ثبت نسخه')!
    await act(async () => confirm.props.onClick())
    await flush()
    assert.match(renderedText(renderer.toJSON()), /1 پیش‌بینی جدید، 1 نسخه اصلاحی و 0 مقدار بدون تغییر ثبت شد/)

    const previewCall = calls.find(call => call.input.endsWith('/planning/forecast-plans/preview'))!
    assert.deepEqual(previewCall.body, {
      name: 'برنامه فروش داخلی', startJalaliDate: '14050101', endJalaliDate: '14050201', productIds: [product.id],
    })
    const saveCalls = calls.filter(call => call.input.endsWith('/planning/forecast-plans'))
    assert.equal(saveCalls.length, 2)
    assert.equal(saveCalls[0].body?.confirmDifferences, false)
    assert.equal(saveCalls[1].body?.confirmDifferences, true)
    assert.equal(saveCalls[1].body?.reason, 'اصلاح مورد تأیید برنامه‌ریز')
  } finally {
    await act(async () => renderer.unmount())
    globalThis.fetch = originalFetch
  }
})

test('saved forecast plans are listed and a monthly cell correction is submitted with its reason and versions', async () => {
  const product = {
    id: '11111111-1111-1111-1111-111111111111', code: 'PRD-0001', nameFa: 'محصول نمونه', nameEn: 'Sample product', isActive: true,
    productCategory: { id: 'category', code: 'CAT', nameFa: 'گروه', nameEn: 'Category', isActive: true },
    businessUnit: null,
    baseUnit: { id: 'unit', code: 'PCS', nameFa: 'عدد', nameEn: 'Piece', symbol: 'pcs', isActive: true },
  }
  const summary = {
    id: 'plan-1', name: 'برنامه سال ۱۴۰۵', startJalaliDate: '14050101', endJalaliDate: '14050201',
    startDate: '2026-03-21', endDate: '2026-05-21', productCount: 1, monthCount: 2, entryCount: 2,
    actorIdentifier: 'planner', createdAtUtc: '2026-08-29T10:00:00Z',
  }
  const month = { periodCode: '140501', jalaliYear: 1405, jalaliMonth: 1, startDate: '2026-03-21', endDate: '2026-04-20', businessPeriodId: 'period-1', status: 'OPEN' }
  let currentQty = 10
  let planRevision = 1
  const correctionBodies: Record<string, unknown>[] = []
  const originalFetch = globalThis.fetch
  globalThis.fetch = async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/planning/forecast-plans?')) return json({ items: [summary], page: 1, pageSize: 25, totalCount: 1 })
    if (url.includes('/planning/forecast-plans/plan-1?')) return json({
      plan: summary, months: [month], products: [product], page: 1, pageSize: 20, totalProducts: 1,
      cells: [{
        entryId: 'entry-1', productId: product.id, periodCode: '140501', originalForecastQty: 10,
        forecastQty: currentQty, planRevisionNumber: planRevision, forecastRevisionId: `revision-${planRevision}`,
        forecastRevisionNumber: planRevision, seriesVersion: planRevision, officialCurrentForecastQty: currentQty,
        officialCurrentRevisionNumber: planRevision, isOfficialCurrent: true, monthlyActualQty: 9,
        dailyActualEntryCount: 2,
        history: planRevision === 1
          ? [{ revisionNumber: 1, forecastQty: 10, reason: null, actorIdentifier: 'planner', createdAtUtc: '2026-08-29T10:00:00Z' }]
          : [{ revisionNumber: 1, forecastQty: 10, reason: null, actorIdentifier: 'planner', createdAtUtc: '2026-08-29T10:00:00Z' },
            { revisionNumber: 2, forecastQty: 12.5, reason: 'اصلاح برنامه', actorIdentifier: 'planner', createdAtUtc: '2026-08-29T11:00:00Z' }],
      }],
    })
    if (url.endsWith('/planning/forecast-plans/plan-1/entries/entry-1/corrections')) {
      correctionBodies.push(JSON.parse(String(init?.body)) as Record<string, unknown>)
      currentQty = 12.5
      planRevision = 2
      return json({ planId: 'plan-1', entryId: 'entry-1', forecastQty: 12.5, planRevisionNumber: 2,
        forecastRevisionId: 'revision-2', forecastRevisionNumber: 2, seriesVersion: 2,
        forecastRevisionCreated: true, history: { revisionNumber: 2, forecastQty: 12.5, reason: 'اصلاح برنامه', actorIdentifier: 'planner', createdAtUtc: '2026-08-29T11:00:00Z' } })
    }
    throw new Error(`Unexpected request ${url}`)
  }

  const list = await renderPlan('/mrp/planning/production-forecast?search=۱۴۰۵', 'list')
  try {
    assert.match(renderedText(list.toJSON()), /برنامه سال ۱۴۰۵/)
    assert.match(renderedText(list.toJSON()), /1/)
    const planLink = list.root.findAllByType('a').find(node => instanceText(node) === 'برنامه سال ۱۴۰۵')!
    assert.match(planLink.props.href, /production-forecast\/plans\/plan-1/)
    assert.match(planLink.props.href, /from=/)
  } finally {
    await act(async () => list.unmount())
  }

  const detail = await renderPlan('/mrp/planning/production-forecast/plans/plan-1?from=%2Fmrp%2Fplanning%2Fproduction-forecast', 'detail')
  try {
    assert.match(renderedText(detail.toJSON()), /PRD-0001/)
    assert.match(renderedText(detail.toJSON()), /140501/)
    const dateRange = detail.root.findAll(node => node.props.className === 'forecast-dual-date-range')[0]
    assert.equal(dateRange.type, 'div')
    assert.match(instanceText(dateRange), /۱۴۰۵\/۰۱\/۰۱ - ۱۴۰۵\/۰۱\/۳۱/)
    assert.match(instanceText(dateRange), /21\/03\/2026 - 20\/04\/2026/)
    assert.ok(dateRange.findAll(node => node.props.dir === 'ltr').length >= 2)
    assert.match(renderedText(detail.toJSON()), /تولید واقعی ماه/)
    assert.match(renderedText(detail.toJSON()), /2 ثبت روزانه/)
    const edit = detail.root.findAllByType('button').find(node => String(node.props.className).includes('forecast-cell-edit'))!
    assert.equal(edit.props.disabled, false)
    await act(async () => edit.props.onClick())
    const initialSave = detail.root.findAllByType('button').find(node => instanceText(node) === 'ذخیره')!
    assert.equal(initialSave.props.disabled, true, 'save stays disabled until a correction reason is supplied')
    const quantity = detail.root.findAllByType('input').find(node => node.props.inputMode === 'decimal')!
    const reason = detail.root.findByType('textarea')
    await act(async () => quantity.props.onChange({ target: { value: '1' } }))
    const quantityAfterFirstDigit = detail.root.findAllByType('input').find(node => node.props.inputMode === 'decimal')!
    assert.equal(quantityAfterFirstDigit.props.value, '1')
    await act(async () => {
      quantityAfterFirstDigit.props.onChange({ target: { value: '۱۲٫۵' } })
      reason.props.onChange({ target: { value: 'اصلاح برنامه' } })
    })
    assert.equal(detail.root.findAllByType('input').find(node => node.props.inputMode === 'decimal')!.props.value, '12.5')
    const save = detail.root.findAllByType('button').find(node => instanceText(node) === 'ذخیره')!
    assert.equal(save.props.disabled, false)
    await act(async () => save.props.onClick())
    await flush()
    assert.deepEqual(correctionBodies, [{ forecastQty: 12.5, reason: 'اصلاح برنامه', expectedSeriesVersion: 1,
      expectedPlanRevisionNumber: 1, confirmDifference: false }])
    assert.match(renderedText(detail.toJSON()), /مقدار ماهانه با حفظ تاریخچه اصلاح شد/)
    assert.match(renderedText(detail.toJSON()), /۱۲٫۵/)
  } finally {
    await act(async () => detail.unmount())
    globalThis.fetch = originalFetch
  }
})

async function render() {
  const component = await module()
  const i18n = createInstance()
  await i18n.init({ resources: { 'fa-IR': { translation: fa } }, lng: 'fa-IR', fallbackLng: 'fa-IR',
    interpolation: { escapeValue: false }, initAsync: false })
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n },
      React.createElement(component.default, { canManage: true })))
  })
  await flush()
  return renderer
}

async function module() {
  if (Module) return Module
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module = await server.ssrLoadModule('/src/ForecastBatchEntryPage.tsx') as typeof import('./ForecastBatchEntryPage.tsx')
  return Module
}

async function renderPlan(path: string, page: 'list' | 'detail') {
  const component = await planModule()
  const i18n = createInstance()
  await i18n.init({ resources: { 'fa-IR': { translation: fa } }, lng: 'fa-IR', fallbackLng: 'fa-IR',
    interpolation: { escapeValue: false }, initAsync: false })
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n },
      React.createElement(MemoryRouter, { initialEntries: [path] },
        React.createElement(Routes, null,
          page === 'list'
            ? React.createElement(Route, { path: '/mrp/planning/production-forecast', element: React.createElement(component.ForecastPlanListPage) })
            : React.createElement(Route, { path: '/mrp/planning/production-forecast/plans/:id', element: React.createElement(component.ForecastPlanDetailPage, { canManage: true }) })))))
  })
  await flush()
  await act(async () => new Promise(resolve => setTimeout(resolve, 220)))
  await flush()
  return renderer
}

async function planModule() {
  if (PlanModule) return PlanModule
  if (!server) server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  PlanModule = await server.ssrLoadModule('/src/ForecastPlanPages.tsx') as typeof import('./ForecastPlanPages.tsx')
  return PlanModule
}

function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }) }
function instanceText(node: { children: unknown[] }): string { return node.children.map(child => typeof child === 'string' ? child : child && typeof child === 'object' && 'children' in child ? instanceText(child as { children: unknown[] }) : '').join('') }
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string { if (node == null) return ''; if (typeof node === 'string') return node; if (Array.isArray(node)) return node.map(renderedText).join(' '); return (node.children ?? []).map(renderedText).join(' ') }
async function flush() { await act(async () => { for (let index = 0; index < 5; index++) await new Promise(resolve => setTimeout(resolve, 10)) }) }
