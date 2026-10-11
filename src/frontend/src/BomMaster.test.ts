import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
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
let BomModule: typeof import('./BomMaster.tsx') | undefined
let AuthModule: typeof import('./AuthContext.tsx') | undefined
const bomMasterSource = readFileSync(new URL('./BomMaster.tsx', import.meta.url), 'utf8')

test('BOM parent type filter uses accessible tabs instead of a dropdown', () => {
  const start = bomMasterSource.indexOf('<div className="bom-parent-filter">')
  const end = bomMasterSource.indexOf('<section className="ui-table-surface bom-catalog-panel">', start)
  const parentFilter = bomMasterSource.slice(start, end)
  assert.ok(start >= 0 && end > start)
  assert.match(parentFilter, /role="group" aria-label={t\('bom\.parent'\)}/)
  assert.match(parentFilter, /aria-pressed=\{!itemType\}/)
  assert.match(parentFilter, /aria-pressed=\{itemType === 'PRODUCT'\}/)
  assert.match(parentFilter, /aria-pressed=\{itemType === 'PART'\}/)
  assert.match(parentFilter, /aria-pressed=\{itemType === 'COMPONENT'\}/)
  assert.doesNotMatch(parentFilter, /<select value=\{itemType\}/)
})

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  window: {
    fetch: (...args: Parameters<typeof fetch>) => globalThis.fetch(...args),
    setTimeout: globalThis.setTimeout,
    clearTimeout: globalThis.clearTimeout,
    requestAnimationFrame: (callback: FrameRequestCallback) => { callback(0); return 1 },
    cancelAnimationFrame: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    localStorage: { getItem: () => null, setItem: () => undefined },
  },
  document: {
    activeElement: null,
    documentElement: { lang: 'fa-IR', dir: 'rtl' },
    getElementById: () => null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  },
})

after(async () => server?.close())

test('actual BOM edit keeps a wrong quantity local, then saves it with the child unit in the tree', async () => {
  const productId = '11111111-1111-1111-1111-111111111111'
  const partId = '22222222-2222-2222-2222-222222222222'
  const revisionId = '33333333-3333-3333-3333-333333333333'
  const lineId = '44444444-4444-4444-4444-444444444444'
  const parentUnit = { id: '55555555-5555-5555-5555-555555555555', code: 'PCS', nameFa: 'عدد', nameEn: 'Piece', symbol: 'pcs', isActive: true }
  const childUnit = { id: '66666666-6666-6666-6666-666666666666', code: 'KG', nameFa: 'کیلوگرم', nameEn: 'Kilogram', symbol: 'kg', isActive: true }
  const product = { id: productId, code: 'PRD-0001', nameFa: 'محصول نمونه', nameEn: 'Sample Product', itemType: 'PRODUCT', isActive: true, currentBaseUnit: parentUnit }
  const targetProduct = { id: '88888888-8888-8888-8888-888888888888', code: 'PRD-0002', nameFa: 'محصول مقصد', nameEn: 'Target Product', itemType: 'PRODUCT', isActive: true, baseUnit: parentUnit }
  const part = { id: partId, code: 'PRT-0001', nameFa: 'قطعه نمونه', nameEn: 'Sample Part', itemType: 'PART', isActive: true, currentBaseUnit: childUnit }
  const candidatePart = { id: '77777777-7777-7777-7777-777777777777', code: 'PRT-0002', nameFa: 'قطعه وزنی جدید', nameEn: 'New weighted part', itemType: 'PART', isActive: true, currentBaseUnit: childUnit }
  let revision = {
    id: revisionId, revisionCode: 'BOM-000001', parentItem: product, parentBaseUnitSnapshot: parentUnit,
    status: 'DRAFT', baseOutputQty: 1, revisionNotes: null, copiedFromRevisionId: null, version: 1,
    lineCount: 1, warningCodes: [], integrityReasonCodes: [],
    lines: [{ id: lineId, childItem: part, childBaseUnitSnapshot: childUnit, usageQty: 2, scrapOverridePercent: null, effectiveScrapPercent: 0, sortOrder: 0, integrityReasonCodes: [] }],
  }
  const putBodies: Record<string, unknown>[] = []
  const restore = installFetch((input, init) => {
    if (input.endsWith('/auth/bootstrap-status')) return json({ initialized: true })
    if (input.endsWith('/auth/me')) return json({
      id: 'admin', userName: 'admin', fullName: 'Administrator', emailAddress: 'admin@example.test', cellphoneNumber: '000',
      organizationalUnit: { id: 'unit', nameFa: 'مدیریت', nameEn: 'Management' }, photoUrl: '/api/auth/photo',
      mustChangePassword: false, isSystemAdministrator: true, dataScopes: [], permissions: [],
    })
    if (input.includes(`/products/${productId}`)) return json({ ...product, baseUnit: parentUnit })
    if (input.includes('/products?')) return json({ items: [{ ...product, baseUnit: parentUnit }, targetProduct], page: 1, pageSize: 25, totalCount: 2 })
    if (input.includes('/bom-revisions/builder-tree')) return json({ rootItem: product, revisions: [revision], missingParentItemIds: [partId] })
    if (input.includes('/bom-revisions/product-tree')) return json({ root: { item: product, revision: { id: revisionId, revisionCode: revision.revisionCode, baseOutputQty: 1 }, relationshipFromParent: null, children: [] } })
    if (input.includes('/bom-revisions/applicable-items?')) return json({ items: [{ item: candidatePart, isApplicable: true }], page: 1, pageSize: 25, totalCount: 1 })
    if (input.endsWith(`/bom-revisions/${revisionId}/validation`)) return json({ isValidForApproval: true, warningCodes: [], blockingCodes: [] })
    if (input.endsWith(`/bom-revisions/${revisionId}/approve`) && init?.method === 'POST') {
      revision = { ...revision, status: 'APPROVED', version: revision.version + 1 }
      return json(revision)
    }
    if (input.endsWith(`/bom-revisions/${revisionId}/lines/${lineId}`) && init?.method === 'PUT') {
      const body = JSON.parse(String(init.body)) as Record<string, unknown>
      putBodies.push(body)
      revision = { ...revision, version: 2, lines: [{ ...revision.lines[0], usageQty: Number(body.usageQty) }] }
      return json(revision)
    }
    if (input.endsWith(`/bom-revisions/${revisionId}`)) return json(revision)
    if (input.includes('/bom-revisions?')) return json({ items: [revision], totalCount: 1 })
    throw new Error(`Unexpected request: ${input}`)
  })

  const renderer = await render(`/mrp/engineering/bom?parentItemId=${productId}&parentType=PRODUCT&rootItemId=${productId}&rootItemType=PRODUCT&rootRevisionId=${revisionId}&selected=${revisionId}`)
  try {
    let rootNode = renderer.root.findAll(node => node.props.role === 'treeitem').find(node => node.props['aria-level'] === undefined) ?? renderer.root.findAll(node => node.props.role === 'treeitem')[0]
    assert.equal(rootNode.props['aria-expanded'], true, 'Loaded tree branches must open automatically')
    const collapseAll = renderer.root.findAllByType('button').find(node => node.props['aria-label'] === 'بستن همه')!
    const expandAll = renderer.root.findAllByType('button').find(node => node.props['aria-label'] === 'بازکردن همه')!
    await act(async () => collapseAll.props.onClick())
    rootNode = renderer.root.findAll(node => node.props.role === 'treeitem')[0]
    assert.equal(rootNode.props['aria-expanded'], false)
    await act(async () => expandAll.props.onClick())
    rootNode = renderer.root.findAll(node => node.props.role === 'treeitem')[0]
    assert.equal(rootNode.props['aria-expanded'], true)
    assert.ok(renderer.root.findAllByType('button').some(node => node.props['aria-label'] === 'افزودن قلم'))
    assert.ok(renderer.root.findAllByType('button').some(node => node.props['aria-label'] === 'حذف خط'))
    const discardDraft = renderer.root.findAllByType('button').find(node => instanceText(node) === 'حذف پیش‌نویس')!
    assert.ok(discardDraft, 'A Draft must expose its controlled delete action')
    await act(async () => discardDraft.props.onClick())
    let actionDialog = renderer.root.findAll(node => node.props.role === 'alertdialog')[0]
    assert.match(instanceText(actionDialog), /محصول، اطلاعات پایه اقلام و BOMهای تأییدشده یا منسوخ حذف نخواهند شد/)
    await act(async () => actionDialog.findAllByType('button').find(node => instanceText(node) === 'انصراف')!.props.onClick())

    const add = renderer.root.findAllByType('button').find(node => node.props['aria-label'] === 'افزودن قلم')!
    await act(async () => add.props.onClick())
    await flush()
    const candidate = renderer.root.findAllByType('button').find(node => instanceText(node).includes('قطعه وزنی جدید'))!
    assert.ok(candidate, 'The applicable-item picker must return the candidate with its current base unit')
    await act(async () => candidate.props.onClick())
    let text = renderedText(renderer.toJSON())
    assert.match(text, /مقدار مصرف \(kg\)/, 'Selecting a new weighted child must expose its unit before save')
    assert.match(text, /1 pcs/, 'The editor must explain the Parent output basis')
    const addForm = renderer.root.findAllByType('form').find(node => String(node.props.className).includes('bom-compact-line-form'))!
    const cancelAdd = addForm.findAllByType('button').find(node => instanceText(node) === 'انصراف')!
    await act(async () => cancelAdd.props.onClick())

    text = renderedText(renderer.toJSON())
    assert.match(text, /2 kg/, 'The tree must show the canonical child unit next to the quantity')
    assert.equal(text.includes('× 2'), false, 'A weighted quantity must not look like an item count')

    const edit = renderer.root.findAllByType('button').find(node => String(node.props['aria-label']).startsWith('ویرایش مقدار مصرف'))!
    assert.ok(edit, 'The unit-aware quantity itself must be the edit affordance')
    await act(async () => edit.props.onClick())

    text = renderedText(renderer.toJSON())
    assert.match(text, /مقدار مصرف \(kg\)/)
    assert.match(text, /1 pcs/)

    let coefficient = renderer.root.findAllByType('input').find(node => node.props.step === '0.000001')!
    await act(async () => coefficient.props.onChange({ target: { value: '0' } }))
    const form = renderer.root.findAllByType('form').find(node => String(node.props.className).includes('bom-compact-line-form'))!
    await act(async () => form.props.onSubmit({ preventDefault: () => undefined }))
    assert.equal(putBodies.length, 0, 'Invalid input must never reach the mutation endpoint')
    assert.match(renderedText(renderer.toJSON()), /مقدار مصرف باید عددی بزرگ‌تر از صفر باشد/)
    coefficient = renderer.root.findAllByType('input').find(node => node.props.step === '0.000001')!
    assert.equal(coefficient.props['aria-invalid'], true)

    await act(async () => coefficient.props.onChange({ target: { value: '7.75' } }))
    const correctedForm = renderer.root.findAllByType('form').find(node => String(node.props.className).includes('bom-compact-line-form'))!
    await act(async () => correctedForm.props.onSubmit({ preventDefault: () => undefined }))
    await flush()
    assert.equal(putBodies.length, 1)
    assert.equal(putBodies[0].usageQty, 7.75)
    assert.equal(putBodies[0].expectedVersion, 1)
    text = renderedText(renderer.toJSON())
    assert.match(text, /قلم BOM ذخیره شد/)
    assert.match(text, /7\.75 kg/)

    const validate = renderer.root.findAllByType('button').find(node => instanceText(node) === 'بررسی ساختار')!
    await act(async () => validate.props.onClick())
    await flush()
    const approve = renderer.root.findAllByType('button').find(node => instanceText(node) === 'تأیید و ثبت BOM')!
    assert.equal(approve.props.disabled, false)
    await act(async () => approve.props.onClick())
    const dialog = renderer.root.findAll(node => node.props.role === 'dialog')[0]
    const confirm = dialog.findAllByType('button').find(node => instanceText(node) === 'تأیید')!
    await act(async () => confirm.props.onClick())
    await flush()
    assert.match(renderedText(renderer.toJSON()), /تأییدشده/)
    assert.equal(renderer.root.findAllByType('button').some(node => String(node.props['aria-label']).startsWith('ویرایش مقدار مصرف')), false,
      'Approved BOM must become read-only')
    assert.ok(renderer.root.findAllByType('button').some(node => instanceText(node) === 'نسخه جدید برای همین محصول'))
    const copyToProduct = renderer.root.findAllByType('button').find(node => instanceText(node) === 'کپی ساختار برای محصول دیگر')!
    assert.ok(copyToProduct, 'Released product BOM must expose a distinct cross-product copy action')
    const editProduct = renderer.root.findAllByType('a').find(node => instanceText(node) === 'ویرایش مشخصات محصول')!
    assert.match(editProduct.props.href, new RegExp(`^/mrp/products/${productId}/edit\\?from=`))

    await act(async () => copyToProduct.props.onClick())
    await flush()
    actionDialog = renderer.root.findAll(node => node.props.role === 'dialog')[0]
    let confirmCopy = actionDialog.findAllByType('button').find(node => instanceText(node) === 'تأیید')!
    assert.equal(confirmCopy.props.disabled, true, 'Cross-product copy cannot run before a target is selected')
    const target = actionDialog.findAllByType('button').find(node => instanceText(node).includes('محصول مقصد'))!
    await act(async () => target.props.onClick())
    confirmCopy = actionDialog.findAllByType('button').find(node => instanceText(node) === 'تأیید')!
    assert.equal(confirmCopy.props.disabled, false)
    await act(async () => actionDialog.findAllByType('button').find(node => instanceText(node) === 'انصراف')!.props.onClick())
  } finally {
    await act(async () => renderer.unmount())
    restore()
  }
})

async function render(initialEntry: string) {
  const translation = createInstance()
  await translation.init({ resources: { 'fa-IR': { translation: fa }, en: { translation: en } }, lng: 'fa-IR', fallbackLng: 'fa-IR', interpolation: { escapeValue: false }, initAsync: false })
  if (!server) server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  BomModule ??= await server.ssrLoadModule('/src/BomMaster.tsx') as typeof import('./BomMaster.tsx')
  AuthModule ??= await server.ssrLoadModule('/src/AuthContext.tsx') as typeof import('./AuthContext.tsx')
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: translation },
      React.createElement(AuthModule!.AuthProvider, null,
        React.createElement(MemoryRouter, { initialEntries: [initialEntry] },
          React.createElement(Routes, null,
            React.createElement(Route, { path: '/mrp/engineering/bom', element: React.createElement(BomModule!.default) }))))))
  })
  await flush()
  return renderer
}

function installFetch(handler: (input: string, init?: RequestInit) => Response) {
  const original = globalThis.fetch
  globalThis.fetch = (input: string | URL | Request, init?: RequestInit) => Promise.resolve(handler(String(input), init))
  return () => { globalThis.fetch = original }
}

function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }) }
function instanceText(node: { children: unknown[] }): string { return node.children.map(child => typeof child === 'string' ? child : child && typeof child === 'object' && 'children' in child ? instanceText(child as { children: unknown[] }) : '').join('') }
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string { if (node == null) return ''; if (typeof node === 'string') return node; if (Array.isArray(node)) return node.map(renderedText).join(' '); return (node.children ?? []).map(renderedText).join(' ') }
async function flush() { await act(async () => { for (let index = 0; index < 8; index++) await new Promise(resolve => setTimeout(resolve, 15)) }) }
