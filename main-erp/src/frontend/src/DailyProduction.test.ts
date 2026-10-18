import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { act, create, type ReactTestRenderer, type ReactTestRendererJSON } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'
import fa from './i18n/locales/fa.ts'

let server: ViteDevServer | undefined
let Module: typeof import('./ForecastActualPage.tsx') | undefined

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

test('daily production is a Jalali editable table and derives its period on the server', async () => {
  const product = {
    id: '11111111-1111-1111-1111-111111111111', code: 'PRD-0001', nameFa: 'محصول نمونه',
    nameEn: 'Sample product', isActive: true,
    productCategory: { id: 'category', code: 'CAT', nameFa: 'گروه', nameEn: 'Category', isActive: true },
    businessUnit: null,
    baseUnit: { id: 'unit', code: 'PCS', nameFa: 'عدد', nameEn: 'Piece', symbol: 'pcs', isActive: true },
  }
  const period = {
    id: 'period', jalaliYear: 1405, jalaliMonth: 6, startDate: '2026-08-23',
    endDate: '2026-09-22', status: 'OPEN', version: 1,
  }
  let actualQty = 8
  let actualVersion = 1
  const calls: Array<{ input: string; body?: Record<string, unknown> }> = []
  const originalFetch = globalThis.fetch
  globalThis.fetch = async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input)
    const body = init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : undefined
    calls.push({ input: url, body })
    if (url.endsWith('/planning/business-date')) return json({
      tehranBusinessDate: '2026-08-31', jalaliYear: 1405, jalaliMonth: 6, jalaliDay: 9,
      jalaliDate: '14050609',
    })
    if (url.includes('/products?')) return json({ items: [product], page: 1, pageSize: 25, totalCount: 1 })
    if (url.includes('/planning/actuals?')) return json({
      items: [{
        id: 'actual-1', product, period, productionDate: '2026-08-30', unit: product.baseUnit,
        currentRevision: { id: 'revision-1', revisionNumber: actualVersion, actualQty, reason: null,
          createdAtUtc: '2026-08-30T12:00:00Z' }, version: actualVersion,
      }], page: 1, pageSize: 25, totalCount: 1,
    })
    if (url.endsWith('/planning/resolve-jalali-date')) return json({
      jalaliDate: '14050609', gregorianDate: '2026-08-31',
    })
    if (url.endsWith('/planning/actuals') && init?.method === 'POST') return json({ id: 'actual-2' }, 201)
    if (url.endsWith('/planning/actuals/actual-1/corrections')) {
      actualQty = Number(body?.actualQty)
      actualVersion++
      return json({ id: 'actual-1', version: actualVersion }, 201)
    }
    throw new Error(`Unexpected request ${url}`)
  }

  const renderer = await render()
  try {
    const initialText = renderedText(renderer.toJSON())
    assert.doesNotMatch(initialText, /دوره تولید|ایجاد دوره/)
    assert.match(initialText, /ثبت جدید و اصلاح مقدار تولید در یک جدول/)
    assert.match(initialText, /PRD-0001/)

    const jalaliInput = renderer.root.findAllByType('input').find(node =>
      node.props['aria-label'] === 'تاریخ تولید شمسی')!
    const quantityInputs = () => renderer.root.findAllByType('input').filter(node =>
      node.props['aria-label'] === 'مقدار واقعی')
    const productSelect = () => renderer.root.findAllByType('select').find(node =>
      node.props['aria-label'] === 'محصول')!
    await act(async () => jalaliInput.props.onChange({ target: { value: '۱۴۰۵۰۶۰۹' } }))
    await act(async () => productSelect().props.onChange({ target: { value: product.id } }))
    await act(async () => quantityInputs()[0].props.onChange({ target: { value: '۱۲٫۵' } }))
    assert.equal(renderer.root.findAllByType('input').find(node =>
      node.props['aria-label'] === 'تاریخ تولید شمسی')!.props.value, '14050609')
    assert.equal(quantityInputs()[0].props.value, '12.5')
    await act(async () => renderer.root.findAllByType('input').find(node =>
      node.props['aria-label'] === 'تاریخ تولید شمسی')!.props.onBlur())
    await flush()
    assert.match(renderedText(renderer.toJSON()), /دوره 140506/)

    const createButton = renderer.root.findAllByType('button').find(node =>
      instanceText(node) === 'ثبت تولید واقعی')!
    await act(async () => createButton.props.onClick?.())
    const createForm = renderer.root.findAllByType('form').find(node => node.props.id === 'daily-actual-create')!
    await act(async () => createForm.props.onSubmit({ preventDefault: () => undefined }))
    await flush()
    assert.deepEqual(calls.find(call => call.input.endsWith('/planning/actuals') && call.body)?.body, {
      productId: product.id, productionDate: '2026-08-31', actualQty: 12.5,
    })
    assert.ok(calls.some(call => call.input.includes('/planning/actuals?') && !call.input.includes('periodId=')))

    const edit = renderer.root.findAllByType('button').find(node => instanceText(node) === 'ویرایش')!
    await act(async () => edit.props.onClick())
    const correctedQty = renderer.root.findAllByType('input').find(node =>
      node.props['aria-label'] === 'مقدار اصلاح‌شده')!
    const reason = renderer.root.findAllByType('input').find(node =>
      node.props['aria-label'] === 'دلیل')!
    await act(async () => {
      correctedQty.props.onChange({ target: { value: '۹٫۷۵' } })
      reason.props.onChange({ target: { value: 'اصلاح ثبت اپراتور' } })
    })
    const save = renderer.root.findAllByType('button').find(node => instanceText(node) === 'ذخیره')!
    assert.equal(save.props.disabled, false)
    await act(async () => save.props.onClick())
    await flush()
    assert.deepEqual(calls.find(call => call.input.endsWith('/planning/actuals/actual-1/corrections'))?.body, {
      actualQty: 9.75, reason: 'اصلاح ثبت اپراتور', expectedVersion: 1,
    })
    assert.match(renderedText(renderer.toJSON()), /۹٫۸/)
  } finally {
    await act(async () => renderer.unmount())
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
      React.createElement(MemoryRouter, { initialEntries: ['/production/daily-production'] },
        React.createElement(component.default, { view: 'actual' }))))
  })
  await act(async () => new Promise(resolve => setTimeout(resolve, 350)))
  await flush()
  return renderer
}

async function module() {
  if (Module) return Module
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module = await server.ssrLoadModule('/src/ForecastActualPage.tsx') as typeof import('./ForecastActualPage.tsx')
  return Module
}

function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
}
function instanceText(node: { children: unknown[] }): string {
  return node.children.map(child => typeof child === 'string' ? child
    : child && typeof child === 'object' && 'children' in child
      ? instanceText(child as { children: unknown[] }) : '').join('')
}
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string {
  if (node == null) return ''
  if (typeof node === 'string') return node
  if (Array.isArray(node)) return node.map(renderedText).join(' ')
  return (node.children ?? []).map(renderedText).join(' ')
}
async function flush() {
  await act(async () => { for (let index = 0; index < 6; index++) await new Promise(resolve => setTimeout(resolve, 10)) })
}
