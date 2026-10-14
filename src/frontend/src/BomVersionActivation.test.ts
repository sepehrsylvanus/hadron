import assert from 'node:assert/strict'
import test, { after, before } from 'node:test'
import * as React from 'react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { act, create, type ReactTestRenderer, type ReactTestRendererJSON } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'
import { resetBomVersionSession } from './bomVersionSession.ts'

let server: ViteDevServer | undefined
let Module: typeof import('./BomVersionActivation.tsx') | undefined

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  window: { addEventListener: () => undefined, removeEventListener: () => undefined, setTimeout: globalThis.setTimeout, clearTimeout: globalThis.clearTimeout },
  document: {
    activeElement: null,
    body: { style: { overflow: '' } },
    documentElement: { lang: '', dir: '' },
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  },
})

before(() => resetBomVersionSession())
after(async () => server?.close())

test('activating a previous BOM version warns, requires an explanation, then swaps the active version', async () => {
  const renderer = await render('fa-IR')
  try {
    let text = renderedText(renderer.toJSON())
    for (const expected of ['فعال‌سازی نسخه‌های قبلی BOM', 'پنکه ایستاده', 'مجموعه موتور', 'برد کنترل', 'بسته‌بندی صادراتی محصول', 'داده نمایشی', 'فقط در همین نشست'])
      assert.ok(text.includes(expected), `Expected panel text: ${expected}\n${text}`)
    // Newest revision of the fan is active by default.
    const fanCard = familyCard(renderer, 'پنکه ایستاده')
    assert.ok(instanceText(fanCard).includes('BOM-5'))

    // Ask to activate the oldest revision (BOM-1) of the fan.
    const activateButton = fanCard.findAllByType('button').find(node => instanceText(node).includes('فعال‌سازی این نسخه'))!
    assert.ok(activateButton)
    // The first activate button in DOM order belongs to the newest inactive
    // revision (BOM-4); pick the last one (BOM-1) for a true rollback.
    const activateButtons = fanCard.findAllByType('button').filter(node => instanceText(node).includes('فعال‌سازی این نسخه'))
    await act(async () => activateButtons[activateButtons.length - 1].props.onClick())

    text = renderedText(renderer.toJSON())
    assert.ok(text.includes('فعال‌سازی نسخه BOM-1'), `Expected modal title\n${text}`)
    assert.ok(text.includes('غیرفعال می‌شود'), `Expected deactivation warning\n${text}`)
    assert.ok(text.includes('BOM-5'), 'warning must name the newer version that will be deactivated')

    const confirmButton = renderer.root.findAllByType('button').find(node => instanceText(node) === 'فعال‌سازی نسخه')!
    assert.ok(confirmButton)
    assert.equal(confirmButton.props.disabled, true, 'confirm must stay disabled without an explanation')

    const textarea = renderer.root.findByType('textarea')
    await act(async () => textarea.props.onChange({ target: { value: 'نسخه جدید روی خط تولید مشکل کیفیت داشت' } }))
    const enabledConfirm = renderer.root.findAllByType('button').find(node => instanceText(node) === 'فعال‌سازی نسخه')!
    assert.equal(enabledConfirm.props.disabled, false)
    await act(async () => enabledConfirm.props.onClick())

    text = renderedText(renderer.toJSON())
    assert.ok(text.includes('فعال شد'), `Expected success message\n${text}`)
    const updatedCard = familyCard(renderer, 'پنکه ایستاده')
    const updatedText = instanceText(updatedCard)
    assert.ok(updatedText.includes('نسخه جدید روی خط تولید مشکل کیفیت داشت'), 'session change log must keep the reason')
    assert.ok(updatedText.includes('بازگشت از BOM-5 به BOM-1'))
    // BOM-1 is now the single active revision: its row shows the Active pill
    // and offers no activate button for it, while BOM-5 offers one.
    const entries = updatedCard.findAll(node => node.type === 'li' && typeof node.props.className === 'string' && node.props.className.startsWith('bom-version-entry'))
    const activeEntries = entries.filter(entry => entry.props.className.includes('is-active'))
    assert.equal(activeEntries.length, 1)
    assert.ok(instanceText(activeEntries[0]).includes('BOM-1'))
  } finally { await act(async () => renderer.unmount()) }
})

function familyCard(renderer: ReactTestRenderer, familyName: string) {
  const card = renderer.root.findAll(node => node.props.className === 'bom-version-family')
    .find(node => instanceText(node).includes(familyName))
  assert.ok(card, `family card not found: ${familyName}`)
  return card
}

async function render(language: 'fa-IR' | 'en') {
  const instance = createInstance()
  await instance.init({ resources: { 'fa-IR': { translation: fa }, en: { translation: en } }, lng: language,
    fallbackLng: 'fa-IR', supportedLngs: ['fa-IR', 'en'], interpolation: { escapeValue: false }, initAsync: false })
  server ??= await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module ??= await server.ssrLoadModule('/src/BomVersionActivation.tsx') as typeof import('./BomVersionActivation.tsx')
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n: instance }, React.createElement(Module!.default)))
  })
  return renderer
}

function instanceText(node: { children: unknown[] }): string { return node.children.map(child => typeof child === 'string' ? child : child && typeof child === 'object' && 'children' in child ? instanceText(child as { children: unknown[] }) : '').join('') }
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string { if (node == null) return ''; if (typeof node === 'string') return node; if (Array.isArray(node)) return node.map(renderedText).join(' '); return (node.children ?? []).map(renderedText).join(' ') }
