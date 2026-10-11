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

let server: ViteDevServer | undefined
let Module: typeof import('./SupplierMaster.tsx') | undefined
let ProvidedItemsModule: typeof import('./SupplierProvidedItems.tsx') | undefined

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  window: {
    fetch: globalThis.fetch,
    setTimeout: globalThis.setTimeout,
    clearTimeout: globalThis.clearTimeout,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    localStorage: { getItem: () => null, setItem: () => undefined },
  },
  document: {
    activeElement: null,
    documentElement: { lang: '', dir: '' },
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  },
})

after(async () => server?.close())

test('Supplier list exposes Internal Owner management as a visible secondary action', async () => {
  const calls: string[] = []
  const restore = installFetch((input) => {
    calls.push(input)
    return json({ items: [], totalCount: 0 })
  })
  const renderer = await render('list', 'fa-IR', '/mrp/supply/suppliers')
  try {
    const link = renderer.root.findAllByType('a').find(node => node.props.href === '/mrp/supply/internal-people')
    assert.ok(link)
    assert.match(instanceText(link), /مدیریت مالک‌های داخلی/)
    assert.equal(renderer.root.findAllByType('details').length, 0)
    await act(async () => new Promise(resolve => setTimeout(resolve, 140)))
    assert.ok(calls.some(value => value.includes('/suppliers?') && value.includes('includeInactive=true')),
      'Supplier list must include inactive records by default so a newly created supplier remains discoverable')
    const status = renderer.root.findAllByType('select').find(node => node.props['aria-label'] === 'فیلتر وضعیت تأمین‌کننده')
    assert.equal(status?.props.value, 'all')
    assert.match(instanceText(status!), /همه وضعیت‌ها/)
    await act(async () => status!.props.onChange({ target: { value: 'active' } }))
    await act(async () => new Promise(resolve => setTimeout(resolve, 140)))
    assert.ok(calls.some(value => value.includes('/suppliers?') && value.includes('includeInactive=false')),
      'Active-only selection must retain the previous operational filter')
    assert.ok(!calls.some(value => value.includes('/internal-people')),
      'The Supplier list must not hide or eagerly load the management surface')
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Supplier list derives domestic or foreign classification from the country code', async () => {
  const makeSupplier = (id: string, countryCode: string, nameFa: string, nameEn: string) => ({
    id, supplierNumber: `SUP-${id}`, name: nameFa,
    country: { id: `country-${countryCode}`, code: countryCode, nameFa, nameEn, isActive: true },
    owner: null,
    paymentProfile: { paymentTermId: null, paymentTerm: null, advancePercent: 0, beforeShipmentPercent: 0, creditPercent: 0, creditDays: null, creditAnchor: null },
    isActive: true, version: 1, contacts: [],
  })
  const restore = installFetch(input => {
    if (input.includes('/suppliers?')) return json({ items: [
      makeSupplier('iran', 'IR', 'ایران', 'Iran'),
      makeSupplier('foreign', 'US', 'آمریکا', 'United States'),
    ], totalCount: 2 })
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await render('list', 'fa-IR', '/mrp/supply/suppliers')
  try {
    await act(async () => new Promise(resolve => setTimeout(resolve, 140)))
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('محدوده جغرافیایی'))
    const rows = renderer.root.findAllByType('tr').filter(row => row.findAllByType('td').length > 0)
    assert.equal(rows.length, 2)
    assert.equal(instanceText(rows[0].findAllByType('td')[3]), 'داخلی')
    assert.equal(instanceText(rows[1].findAllByType('td')[3]), 'خارجی')
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Supplier financial balance appears in the list and as a read-only value on the supplier page', async () => {
  const supplier = {
    id: 'supplier-balance', supplierNumber: 'SUP-0042', name: 'Balance Test Supplier',
    country: { id: 'country', code: 'US', nameFa: 'آمریکا', nameEn: 'United States', isActive: true },
    owner: null,
    paymentProfile: { paymentTermId: null, paymentTerm: null, advancePercent: 0, beforeShipmentPercent: 0, creditPercent: 0, creditDays: null, creditAnchor: null },
    financialBalanceAmount: '1234567.89', financialBalanceCurrencyId: 'currency-usd',
    expertLeadTimeScore: null, expertQualityScore: null, notes: null, isActive: true, version: 1, contacts: [],
  }
  const restore = installFetch((input) => {
    if (input.includes('/suppliers?')) return json({ items: [supplier], totalCount: 1 })
    if (input.endsWith('/suppliers/supplier-balance')) return json(supplier)
    if (input.includes('/reference-data/currencies')) return json([{ id: 'currency-usd', code: 'USD', decimalPrecision: 2, isActive: false }])
    if (input.includes('/reference-data/types/COUNTRY') || input.includes('/reference-data/types/PAYMENT_TERM') || input.includes('/internal-people')) return json([])
    throw new Error(`Unexpected request: ${input}`)
  })
  try {
    const list = await render('list', 'en', '/mrp/supply/suppliers')
    try {
      await act(async () => new Promise(resolve => setTimeout(resolve, 140)))
      const text = renderedText(list.toJSON())
      assert.ok(text.includes('Financial balance'))
      assert.ok(text.includes('1,234,567.89 USD'))
    } finally { await act(async () => list.unmount()) }

    const detail = await render('object', 'en', '/mrp/suppliers/supplier-balance')
    try {
      const balanceField = detail.root.findAllByType('input').find(node => node.props.readOnly)
      assert.equal(balanceField?.props.value, '1,234,567.89 USD')
      assert.ok(renderedText(detail.toJSON()).includes('Financial balance'))
    } finally { await act(async () => detail.unmount()) }
  } finally { restore() }
})

test('Supplier provided parts show a recursive BOM usage tree on hover and no tree for unused parts', async () => {
  const unit = { id: 'unit-each', code: 'EA', nameFa: 'عدد', nameEn: 'Each', symbol: 'ea', isActive: true }
  const wire = { id: 'part-wire', code: 'PART-10', nameFa: 'سیم مسی', nameEn: 'Copper wire', itemType: 'PART', supplyMethod: 'BUY', isActive: true, baseUnit: unit }
  const unused = { id: 'part-direct', code: 'PART-20', nameFa: 'برچسب فروش', nameEn: 'Retail label', itemType: 'PART', supplyMethod: 'BUY', isActive: true, baseUnit: unit }
  const motor = { id: 'component-motor', code: 'COMP-10', nameFa: 'مجموعه موتور', nameEn: 'Motor assembly', itemType: 'COMPONENT', isActive: true, currentBaseUnit: unit }
  const fan = { id: 'product-fan', code: 'PROD-10', nameFa: 'پنکه', nameEn: 'Ventilation fan', itemType: 'PRODUCT', isActive: true, currentBaseUnit: unit }
  const calls: string[] = []
  const restore = installFetch(input => {
    calls.push(input)
    if (input.includes('/sourcing-options/tree?')) return json({ items: [
      { item: wire, options: [{ isPreferred: true, imageUrl: 'https://images.example.test/copper-wire.png' }] },
      { item: unused, options: [] },
    ], page: 1, pageSize: 100, totalItemCount: 2, totalOptionCount: 2 })
    if (input.includes('/bom-revisions/where-used')) {
      const itemId = new URL(input, 'http://localhost').searchParams.get('itemId')
      if (itemId === wire.id) return json({ item: wire, directParents: [{ parentItem: motor, revision: { revisionCode: 'BOM-2' }, relationship: { usageQty: 4, childBaseUnitSnapshot: unit } }], finalProducts: [fan] })
      if (itemId === motor.id) return json({ item: motor, directParents: [{ parentItem: fan, revision: { revisionCode: 'BOM-5' }, relationship: { usageQty: 1, childBaseUnitSnapshot: unit } }], finalProducts: [fan] })
      if (itemId === unused.id) return json({ item: unused, directParents: [], finalProducts: [] })
    }
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await renderProvidedItems('en')
  try {
    const rows = renderer.root.findAll(node => node.props.className === 'supplier-provided-item')
    assert.equal(rows.length, 2)
    const usedRow = rows.find(row => instanceText(row).includes('Copper wire'))!
    const unusedRow = rows.find(row => instanceText(row).includes('Retail label'))!
    assert.equal(usedRow.findByType('img').props.src, 'https://images.example.test/copper-wire.png')
    assert.equal(unusedRow.findByType('img').props.src, `https://picsum.photos/seed/${unused.id}/640/360`)

    await act(async () => usedRow.props.onMouseEnter())
    await flush()
    const text = renderedText(renderer.toJSON())
    assert.ok(text.includes('Motor assembly'))
    assert.ok(text.includes('Ventilation fan'))
    assert.ok(calls.some(input => input.includes('itemId=component-motor')), 'Where-used lookup should continue through intermediate BOM parents')

    await act(async () => usedRow.props.onMouseLeave({ currentTarget: { contains: () => false } }))
    await act(async () => unusedRow.props.onMouseEnter())
    await flush()
    assert.equal(renderer.root.findAll(node => node.props.className === 'supplier-item-usage-popover').length, 0,
      'A sourced item with no current approved-BOM usage must not show a tree')
    assert.ok(calls.some(input => input.includes('itemId=part-direct')))
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Internal Owner page loads active and inactive people and exposes lifecycle actions', async () => {
  const restore = installFetch(() => json([
    { id: 'p1', displayNameFa: 'علی رضایی', displayNameEn: 'Ali Rezaei', isActive: true, version: 1 },
    { id: 'p2', displayNameFa: 'مریم احمدی', displayNameEn: null, isActive: false, version: 3 },
  ]))
  const renderer = await render('people', 'fa-IR', '/mrp/supply/internal-people')
  try {
    const text = renderedText(renderer.toJSON())
    for (const expected of ['مالک‌های داخلی', 'علی رضایی', 'Ali Rezaei', 'مریم احمدی', 'فعال', 'غیرفعال', 'ویرایش', 'فعال‌سازی', 'غیرفعال‌سازی'])
      assert.ok(text.includes(expected), `Expected visible text: ${expected}\n${text}`)
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Supplier create can define an owner inline and selects the new owner without losing the draft', async () => {
  const calls: Array<{ input: string; init?: RequestInit }> = []
  const restore = installFetch((input, init) => {
    calls.push({ input, init })
    if (input.includes('/reference-data/types/COUNTRY')) return json([{ id: 'country', code: 'IR', nameFa: 'ایران', nameEn: 'Iran', isActive: true }])
    if (input.includes('/reference-data/types/PAYMENT_TERM')) return json([])
    if (input.includes('/internal-people') && init?.method === 'POST') return json({ id: 'person-new', displayNameFa: 'مالک تازه', displayNameEn: 'New Owner', isActive: true, version: 1 }, 201)
    if (input.includes('/internal-people')) return json([])
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await render('object', 'fa-IR', '/mrp/suppliers/new')
  try {
    const supplierName = renderer.root.findAllByType('input').find(node => node.props.required && !node.props.autoFocus)!
    await act(async () => supplierName.props.onChange({ target: { value: 'تأمین‌کننده آزمایشی' } }))
    const define = renderer.root.findAllByType('button').find(node => instanceText(node).includes('تعریف مالک داخلی جدید'))!
    assert.ok(define)
    await act(async () => define.props.onClick())

    const faName = renderer.root.findAllByType('input').find(node => node.props.autoFocus)!
    await act(async () => faName.props.onChange({ target: { value: 'مالک تازه' } }))
    const enName = renderer.root.findAllByType('input').find(node => node.props.dir === 'ltr' && node.props.value === '')!
    await act(async () => enName.props.onChange({ target: { value: 'New Owner' } }))
    const personForm = renderer.root.findAllByType('form').find(node => node.findAllByProps({ autoFocus: true }).length === 1)!
    await act(async () => personForm.props.onSubmit({ preventDefault: () => undefined }))
    await flush()

    const createCall = calls.find(value => value.input.includes('/internal-people') && value.init?.method === 'POST')
    assert.ok(createCall)
    assert.deepEqual(JSON.parse(String(createCall.init?.body)), { displayNameFa: 'مالک تازه', displayNameEn: 'New Owner' })
    assert.ok(renderer.root.findAllByType('select').some(node => node.props.value === 'person-new'))
    assert.ok(renderer.root.findAllByType('input').some(node => node.props.value === 'تأمین‌کننده آزمایشی'),
      'The unsaved Supplier draft must remain intact')
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('failed Supplier activation closes confirmation and exposes the business error', async () => {
  const calls: Array<{ input: string; init?: RequestInit }> = []
  const supplier = {
    id: 'supplier-1', supplierNumber: 'SUP-0001', name: 'تأمین‌کننده ناقص',
    country: { id: 'country', code: 'IR', nameFa: 'ایران', nameEn: 'Iran', isActive: true },
    owner: null, paymentProfile: { paymentTermId: null, paymentTerm: null, advancePercent: 0, beforeShipmentPercent: 0, creditPercent: 0, creditDays: null, creditAnchor: null },
    expertLeadTimeScore: null, expertQualityScore: null, notes: null, isActive: false, version: 1, contacts: [],
  }
  const restore = installFetch((input, init) => {
    calls.push({ input, init })
    if (input.includes('/reference-data/types/COUNTRY')) return json([supplier.country])
    if (input.includes('/reference-data/types/PAYMENT_TERM')) return json([])
    if (input.includes('/internal-people')) return json([])
    if (input.endsWith('/suppliers/supplier-1/reactivate') && init?.method === 'POST')
      return json({ code: 'SUPPLIER_PAYMENT_TOTAL_INVALID', detail: 'مجموع درصدهای پرداخت باید دقیقاً ۱۰۰٫۰۰ باشد.' }, 400)
    if (input.endsWith('/suppliers/supplier-1')) return json(supplier)
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await render('object', 'fa-IR', '/mrp/suppliers/supplier-1')
  try {
    assert.match(renderedText(renderer.toJSON()), /ذخیره شده اما هنوز غیرفعال است/)
    const balanceField = renderer.root.findAllByType('input').find(node => node.props.readOnly)
    assert.equal(balanceField?.props.value, '—', 'Suppliers without API balance values must show an explicit empty marker')
    assert.ok(renderedText(renderer.toJSON()).includes('تراز مالی'))
    const activate = renderer.root.findAllByType('button').find(node => instanceText(node) === 'تکمیل و فعال‌سازی')!
    await act(async () => activate.props.onClick())
    const dialog = renderer.root.findAll(node => node.props.role === 'alertdialog')[0]
    assert.ok(dialog)
    assert.match(instanceText(dialog), /مالک داخلی، شرط پرداخت و مجموع درصدهای پرداخت/)
    const confirm = dialog.findAllByType('button').find(node => instanceText(node) === 'فعال‌سازی اکنون')!
    await act(async () => confirm.props.onClick())
    await flush()

    assert.equal(renderer.root.findAll(node => node.props.role === 'alertdialog').length, 0)
    const text = renderedText(renderer.toJSON())
    assert.match(text, /SUPPLIER_PAYMENT_TOTAL_INVALID/)
    assert.match(text, /مجموع درصدهای پرداخت باید دقیقاً ۱۰۰٫۰۰ باشد/)
    assert.equal(calls.filter(value => value.input.endsWith('/suppliers/supplier-1/reactivate')).length, 1)
  } finally { await act(async () => renderer.unmount()); restore() }
})

async function render(view: 'list' | 'people' | 'object', language: 'fa-IR' | 'en', initialEntry: string) {
  const translation = await translations(language)
  const module = await component()
  const Component = view === 'list' ? module.default : view === 'people' ? module.InternalPeoplePage : module.SupplierObjectPage
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement(MemoryRouter, { initialEntries: [initialEntry] },
        React.createElement(Routes, null,
          React.createElement(Route, { path: '/mrp/supply/suppliers', element: React.createElement(Component) }),
          React.createElement(Route, { path: '/mrp/supply/internal-people', element: React.createElement(Component) }),
          React.createElement(Route, { path: '/mrp/suppliers/new', element: React.createElement(Component) }),
          React.createElement(Route, { path: '/mrp/suppliers/:id', element: React.createElement(Component) })))))
  })
  await flush()
  return renderer
}

async function renderProvidedItems(language: 'fa-IR' | 'en') {
  const translation = await translations(language)
  await component()
  ProvidedItemsModule ??= await server!.ssrLoadModule('/src/SupplierProvidedItems.tsx') as typeof import('./SupplierProvidedItems.tsx')
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement(MemoryRouter, null,
        React.createElement(ProvidedItemsModule!.default, { supplierId: 'supplier-1', canViewBom: true }))))
  })
  await flush()
  return renderer
}

async function component() {
  if (Module) return Module
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module = await server.ssrLoadModule('/src/SupplierMaster.tsx') as typeof import('./SupplierMaster.tsx')
  return Module
}

async function translations(language: 'fa-IR' | 'en') {
  const instance = createInstance()
  await instance.init({ resources: { 'fa-IR': { translation: fa }, en: { translation: en } }, lng: language,
    fallbackLng: 'fa-IR', supportedLngs: ['fa-IR', 'en'], interpolation: { escapeValue: false }, initAsync: false })
  return instance
}

function installFetch(handler: (input: string, init?: RequestInit) => Response) {
  const original = globalThis.fetch
  globalThis.fetch = (input: string | URL | Request, init?: RequestInit) => Promise.resolve(handler(String(input), init))
  return () => { globalThis.fetch = original }
}

function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }) }
function instanceText(node: { children: unknown[] }): string { return node.children.map(child => typeof child === 'string' ? child : child && typeof child === 'object' && 'children' in child ? instanceText(child as { children: unknown[] }) : '').join('') }
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string { if (node == null) return ''; if (typeof node === 'string') return node; if (Array.isArray(node)) return node.map(renderedText).join(' '); return (node.children ?? []).map(renderedText).join(' ') }
async function flush() { await act(async () => { for (let index = 0; index < 4; index++) await new Promise(resolve => setTimeout(resolve, 10)) }) }
