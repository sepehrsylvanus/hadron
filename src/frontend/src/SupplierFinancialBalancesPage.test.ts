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
let Module: typeof import('./SupplierFinancialBalancesPage.tsx') | undefined

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

const suppliers = [
  {
    id: 'supplier-ir', supplierNumber: 'SUP-0002', name: 'Hadron Plast',
    country: { id: 'country-ir', code: 'IR', nameFa: 'ایران', nameEn: 'Iran', isActive: true },
    owner: null,
    paymentProfile: { paymentTermId: null, paymentTerm: null, advancePercent: 0, beforeShipmentPercent: 0, creditPercent: 0, creditDays: null, creditAnchor: null },
    isActive: true, version: 1, contacts: [],
  },
  {
    id: 'supplier-us', supplierNumber: 'SUP-0007', name: 'Global Resin Co',
    country: { id: 'country-us', code: 'US', nameFa: 'آمریکا', nameEn: 'United States', isActive: true },
    owner: null,
    paymentProfile: { paymentTermId: null, paymentTerm: null, advancePercent: 0, beforeShipmentPercent: 0, creditPercent: 0, creditDays: null, creditAnchor: null },
    isActive: true, version: 1, contacts: [],
  },
]

test('overall balances page merges every supplier ledger and opens a single statement from the summary', async () => {
  const restore = installFetch(input => {
    if (input.includes('/suppliers?')) return json({ items: suppliers, totalCount: suppliers.length })
    throw new Error(`Unexpected request: ${input}`)
  })
  const renderer = await render('fa-IR')
  try {
    await flush()
    const text = renderedText(renderer.toJSON())
    for (const expected of ['تراز مالی کلی تأمین‌کنندگان', 'تراز کلی به تفکیک ارز', 'IRR', 'USD', 'دفتر گردش مالی همه تأمین‌کنندگان', 'خلاصه تراز هر تأمین‌کننده', 'Hadron Plast', 'Global Resin Co', 'مانده تأمین‌کننده'])
      assert.ok(text.includes(expected), `Expected page text: ${expected}\n${text}`)
    assert.doesNotMatch(text, /20\d{2}-\d{2}-\d{2}/, 'Persian page must not show Gregorian dates')

    // Shamsi (Jalali) date filters, like the single-supplier statement.
    const dateInputs = renderer.root.findAllByType('input').filter(node => node.props.inputMode === 'numeric')
    assert.equal(dateInputs.length, 2)
    assert.match(String(dateInputs[0].props.value), /^[۰-۹]{4}\/[۰-۹]{2}\/[۰-۹]{2}$/)

    // The merged table holds entries from both suppliers.
    const tables = renderer.root.findAllByType('table')
    assert.equal(tables.length, 2)
    const combinedRows = tables[0].findAllByType('tr').filter(row => row.findAllByType('td').length === 9)
    assert.ok(combinedRows.length >= 10, `expected a full page of merged entries, got ${combinedRows.length}`)
    const combinedText = combinedRows.map(row => instanceText(row)).join('\n')
    assert.ok(combinedText.includes('Hadron Plast') || combinedText.includes('Global Resin Co'))
    const summaryRows = tables[1].findAllByType('tr').filter(row => row.findAllByType('td').length === 8)
    assert.equal(summaryRows.length, 2)

    // Narrowing the Shamsi range far into the past empties the merged table.
    await act(async () => dateInputs[0].props.onChange({ target: { value: '13781001' } }))
    await act(async () => dateInputs[0].props.onBlur())
    await act(async () => dateInputs[1].props.onChange({ target: { value: '13781030' } }))
    await act(async () => dateInputs[1].props.onBlur())
    assert.match(renderedText(renderer.toJSON()), /در این بازه گردشی وجود ندارد/)

    // Reset to the full range, then open one supplier statement from its total balance.
    const allPreset = renderer.root.findAllByType('button').find(node => instanceText(node) === 'همه دوره')!
    await act(async () => allPreset.props.onClick())
    await flush()
    const balanceButtons = renderer.root.findAllByType('button').filter(node => String(node.props['aria-label'] ?? '').includes('مشاهده ریز تراز مالی'))
    assert.equal(balanceButtons.length, 2)
    await act(async () => balanceButtons[0].props.onClick())
    const dialog = renderer.root.findAll(node => node.props.role === 'dialog')[0]
    assert.ok(dialog, 'clicking a supplier total must open the single-supplier statement')
    assert.match(instanceText(dialog), /تراز مالی مجموع/)
  } finally { await act(async () => renderer.unmount()); restore() }
})

async function render(language: 'fa-IR' | 'en') {
  const translation = await translations(language)
  const module = await component()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement(MemoryRouter, { initialEntries: ['/mrp/supply/supplier-balances'] },
        React.createElement(Routes, null,
          React.createElement(Route, { path: '/mrp/supply/supplier-balances', element: React.createElement(module.default) })))))
  })
  await flush()
  return renderer
}

async function component() {
  if (Module) return Module
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module = await server.ssrLoadModule('/src/SupplierFinancialBalancesPage.tsx') as typeof import('./SupplierFinancialBalancesPage.tsx')
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
async function flush() { await act(async () => { for (let index = 0; index < 5; index++) await new Promise(resolve => setTimeout(resolve, 10)) }) }
