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

test('Supplier without an API balance gets a clickable fake balance that opens its ledger statement', async () => {
  const supplier = {
    id: 'supplier-fake-balance', supplierNumber: 'SUP-0099', name: 'تأمین‌کننده نمایشی',
    country: { id: 'country', code: 'IR', nameFa: 'ایران', nameEn: 'Iran', isActive: true },
    owner: null,
    paymentProfile: { paymentTermId: null, paymentTerm: null, advancePercent: 0, beforeShipmentPercent: 0, creditPercent: 0, creditDays: null, creditAnchor: null },
    isActive: true, version: 1, contacts: [],
  }
  const restore = installFetch((input) => {
    if (input.includes('/suppliers?')) return json({ items: [supplier], totalCount: 1 })
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await render('list', 'fa-IR', '/mrp/supply/suppliers')
  try {
    await act(async () => new Promise(resolve => setTimeout(resolve, 140)))
    const balanceButton = renderer.root.findAllByType('button').find(node => String(node.props['aria-label'] ?? '').includes('مشاهده ریز تراز مالی'))!
    assert.ok(balanceButton, 'financial balance must be a clickable button')
    assert.match(instanceText(balanceButton), /IRR/)
    assert.match(instanceText(balanceButton), /داده نمایشی/)

    await act(async () => balanceButton.props.onClick())
    const dialog = renderer.root.findAll(node => node.props.role === 'dialog')[0]
    assert.ok(dialog, 'clicking the balance must open the ledger statement')
    const dialogText = instanceText(dialog)
    for (const expected of ['ریز تراز مالی', 'تأمین‌کننده نمایشی', 'تراز مالی مجموع', 'مانده ابتدای دوره', 'جمع قابل پرداخت دوره', 'جمع پرداختی دوره', 'مانده پایان دوره', 'پروژه / دادوستد', 'تراز روز قبل', 'قابل پرداخت این پروژه', 'پرداختی امروز', 'فرمول تراز مالی: تراز روز قبل + قابل پرداخت این پروژه − پرداختی امروز', 'PRJ-'])
      assert.ok(dialogText.includes(expected), `Expected ledger text: ${expected}\n${dialogText}`)
    assert.doesNotMatch(dialogText, /20\d{2}-\d{2}-\d{2}/, 'Persian ledger must not show Gregorian dates')
    const exportButton = dialog.findAllByType('button').find(node => instanceText(node).includes('خروجی Excel'))!
    assert.ok(exportButton, 'statement drawer must offer Excel export')
    await act(async () => exportButton.props.onClick())
    const ledgerRows = dialog.findAllByType('tr').filter(row => row.findAllByType('td').length === 6)
    assert.ok(ledgerRows.length >= 6, `expected at least 6 project ledger rows, got ${ledgerRows.length}`)

    // Persian UI must use Shamsi (Jalali) text inputs with Persian digits, never Gregorian date inputs.
    assert.equal(dialog.findAllByType('input').filter(node => node.props.type === 'date').length, 0)
    const dateInputs = dialog.findAllByType('input').filter(node => node.props.inputMode === 'numeric')
    assert.equal(dateInputs.length, 2)
    assert.match(String(dateInputs[0].props.value), /^[۰-۹]{4}\/[۰-۹]{2}\/[۰-۹]{2}$/)
    assert.match(instanceText(dialog), /تاریخ شمسی/)
    // 1378/10/01 and 1378/10/30 Shamsi are far before every generated entry.
    await act(async () => dateInputs[0].props.onChange({ target: { value: '13781001' } }))
    await act(async () => dateInputs[0].props.onBlur())
    await act(async () => dateInputs[1].props.onChange({ target: { value: '13781030' } }))
    await act(async () => dateInputs[1].props.onBlur())
    assert.match(instanceText(dialog), /در این بازه گردشی وجود ندارد/)
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Supplier supplied items become session projects with the product - year - repeat name', async () => {
  const unit = { id: 'unit-each', code: 'EA', nameFa: 'عدد', nameEn: 'Each', symbol: 'ea', isActive: true }
  const currentItem = { id: 'part-now', code: 'PART-30', nameFa: 'موتور فن', nameEn: 'Fan motor', itemType: 'PART', supplyMethod: 'BUY', isActive: true, baseUnit: unit }
  const pastItem = { id: 'part-before', code: 'PART-40', nameFa: 'تسمه قدیمی', nameEn: 'Legacy belt', itemType: 'PART', supplyMethod: 'BUY', isActive: true, baseUnit: unit }
  const restore = installFetch(input => {
    if (input.includes('/sourcing-options/tree?')) return json({ items: [
      { item: currentItem, options: [{ isPreferred: true, isActive: true, sourcingNumber: 'SRC-30', imageUrl: null, createdAtUtc: '2024-02-10T08:00:00Z', updatedAtUtc: '2024-02-10T08:00:00Z', totalConfiguredLeadTimeDays: 25 }] },
      { item: pastItem, options: [{ isPreferred: false, isActive: false, sourcingNumber: 'SRC-40', imageUrl: null, createdAtUtc: '2022-04-01T08:00:00Z', updatedAtUtc: '2023-09-15T08:00:00Z', totalConfiguredLeadTimeDays: 10 }] },
    ], page: 1, pageSize: 100, totalItemCount: 2, totalOptionCount: 2 })
    if (input.includes('/products?')) return json({ items: [{ id: 'prod-fan', code: 'PROD-100', nameFa: 'پنکه ایستاده', nameEn: 'Standing fan', itemType: 'PRODUCT', isActive: true, baseUnit: unit }], totalCount: 1 })
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await renderProvidedItems('fa-IR', undefined, 'supplier-projects-1')
  try {
    const cards = () => renderer.root.findAll(node => node.type === 'li' && typeof node.props.className === 'string' && node.props.className.startsWith('supplier-project-card'))
    assert.ok(renderedText(renderer.toJSON()).includes('پروژه‌ها'), 'section must be titled Projects')
    assert.equal(cards().length, 1, 'one current project is seeded from the supplied items')
    assert.ok(instanceText(cards()[0]).includes('موتور فن'))
    assert.ok(instanceText(cards()[0]).includes('PART-30'), 'piece serial line must show the item code by default')

    // Create a project manually: separate inputs, one final formatted name.
    const newButton = renderer.root.findAllByType('button').find(node => instanceText(node).includes('پروژه جدید'))!
    await act(async () => newButton.props.onClick())
    const editor = renderer.root.findAll(node => node.props.className === 'supplier-project-editor')[0]
    const inputs = editor.findAllByType('input')
    for (const [index, value] of ['پروژه خط تولید', '1404', '3', 'پره فن', 'پنکه ایستاده', '1404', '2', 'SN-7788'].entries())
      await act(async () => inputs[index].props.onChange({ target: { value } }))
    await act(async () => new Promise(resolve => setTimeout(resolve, 350)))
    const option = renderer.root.findAll(node => node.props.className === 'supplier-project-assign__option')[0]
    assert.ok(option, 'catalog picker must offer products to assign')
    await act(async () => option.props.onClick())
    assert.ok(renderedText(renderer.toJSON()).includes('پنکه ایستاده - ۱۴۰۴ - ۲'), 'live preview must show the product - order year - repeat name')
    assert.ok(renderedText(renderer.toJSON()).includes('(پروژه خط تولید)'), 'live preview must show the manual info as the parenthesized note below the name')
    const save = renderer.root.findAllByType('button').find(node => instanceText(node) === 'ذخیره')!
    await act(async () => save.props.onClick())
    assert.equal(cards().length, 2)
    const allCurrent = cards().map(card => instanceText(card)).join('\n')
    assert.ok(allCurrent.includes('پنکه ایستاده - ۱۴۰۴ - ۲'), 'saved card must show the product - order year - repeat name')
    assert.ok(allCurrent.includes('(پروژه خط تولید)'), 'saved card must show the manual info note under the name')
    assert.ok(allCurrent.includes('SN-7788'))
    assert.ok(allCurrent.includes('PROD-100'))

    // Ending the seeded project from its editor moves it to previous projects.
    const seededCard = cards().find(card => instanceText(card).includes('موتور فن'))!
    await act(async () => seededCard.findAllByType('button').find(node => instanceText(node).includes('ویرایش'))!.props.onClick())
    await act(async () => renderer.root.findAllByType('button').find(node => instanceText(node).includes('پایان پروژه'))!.props.onClick())
    await act(async () => renderer.root.findAllByType('button').find(node => instanceText(node) === 'ذخیره')!.props.onClick())
    const previousCards = cards().map(card => instanceText(card)).join('\n')
    assert.equal(cards().length, 2, 'ended project plus the previously supplied item')
    assert.ok(previousCards.includes('موتور فن'))
    assert.ok(previousCards.includes('تسمه قدیمی'))
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Hadron Plast (SUP-0002) shows clearly-badged demo records in its previous projects', async () => {
  const restore = installFetch(input => {
    if (input.includes('/sourcing-options/tree?')) return json({ items: [], page: 1, pageSize: 100, totalItemCount: 0, totalOptionCount: 0 })
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await renderProvidedItems('fa-IR', 'SUP-0002', 'supplier-demo-1')
  try {
    const historyTab = renderer.root.findAllByType('button').find(node => instanceText(node).includes('پروژه‌های قبلی'))!
    assert.ok(historyTab, 'demo records must make the previous projects view reachable')
    await act(async () => historyTab.props.onClick())
    const text = renderedText(renderer.toJSON())
    for (const expected of ['گرانول پلی‌اتیلن سبک', 'مستربچ رنگی مشکی', 'رزین PVC گرید S65', 'فیلم استرچ بسته‌بندی', 'داده نمایشی', 'پایان‌یافته'])
      assert.ok(text.includes(expected), `Expected demo history text: ${expected}\n${text}`)
    const entries = renderer.root.findAll(node => node.type === 'li' && typeof node.props.className === 'string' && node.props.className.startsWith('supplier-project-card'))
    assert.equal(entries.length, 4)
  } finally { await act(async () => renderer.unmount()); restore() }
})

test('Other suppliers get no demo projects', async () => {
  const restore = installFetch(input => {
    if (input.includes('/sourcing-options/tree?')) return json({ items: [], page: 1, pageSize: 100, totalItemCount: 0, totalOptionCount: 0 })
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await renderProvidedItems('fa-IR', 'SUP-0007', 'supplier-demo-2')
  try {
    assert.equal(renderer.root.findAll(node => node.type === 'li' && typeof node.props.className === 'string' && node.props.className.startsWith('supplier-project-card')).length, 0)
    assert.ok(!renderedText(renderer.toJSON()).includes('گرانول پلی‌اتیلن سبک'))
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

async function renderProvidedItems(language: 'fa-IR' | 'en', supplierNumber?: string, supplierId = 'supplier-1') {
  const translation = await translations(language)
  await component()
  ProvidedItemsModule ??= await server!.ssrLoadModule('/src/SupplierProvidedItems.tsx') as typeof import('./SupplierProvidedItems.tsx')
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement(MemoryRouter, null,
        React.createElement(ProvidedItemsModule!.default, { supplierId, supplierNumber }))))
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
