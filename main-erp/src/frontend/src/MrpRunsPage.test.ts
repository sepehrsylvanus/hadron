import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { createInstance } from 'i18next'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { act, create, type ReactTestRenderer, type ReactTestRendererJSON } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'
import fa from './i18n/locales/fa.ts'

const runId = '11111111-1111-1111-1111-111111111111'
let server: ViteDevServer | undefined
let Module: typeof import('./MrpRunsPage.tsx') | undefined

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  window: {
    fetch: (...args: Parameters<typeof fetch>) => globalThis.fetch(...args),
    setTimeout: globalThis.setTimeout,
    clearTimeout: globalThis.clearTimeout,
    setInterval: globalThis.setInterval,
    clearInterval: globalThis.clearInterval,
  },
  document: { documentElement: { lang: '', dir: '' } },
})

after(async () => server?.close())

test('MRP entry shows the live plan while retained plan versions remain a secondary surface', async () => {
  const calls: string[] = []
  const originalFetch = globalThis.fetch
  globalThis.fetch = async (input: string | URL | Request) => {
    const url = String(input)
    calls.push(url)
    if (url.endsWith('/api/material-planning/refresh-status')) return json({
      status: 'UP_TO_DATE', latestRunId: runId, latestRunNumber: 'MRP-RUN-000008', latestAsOfDate: '2026-08-31',
      lastUpdatedAtUtc: '2026-08-31T08:30:00Z', pendingChangeCount: 0,
      certifiedBusinessDate: '2026-08-30', certifiedAtUtc: '2026-08-31T00:05:00Z',
      certifiedResultHash: '0123456789abcdef0123456789abcdef',
    })
    if (url.includes(`/api/mrp-runs/${runId}/exceptions?severity=ERROR`)) return json({ items: [], page: 1, pageSize: 1, totalCount: 24 })
    if (url.includes(`/api/mrp-runs/${runId}/exceptions?severity=WARNING`)) return json({ items: [], page: 1, pageSize: 1, totalCount: 482 })
    if (url.includes(`/api/mrp-runs/${runId}/exceptions?severity=INFO`)) return json({ items: [], page: 1, pageSize: 1, totalCount: 0 })
    if (url.includes(`/api/mrp-runs/${runId}/exception-summary?severity=ERROR`)) return json(exceptionSummary('ERROR'))
    if (url.includes(`/api/mrp-runs/${runId}/exception-summary?severity=WARNING`)) return json(exceptionSummary('WARNING'))
    if (url.endsWith(`/api/mrp-runs/${runId}`)) return json(run())
    if (url.includes('/api/mrp-runs?page=')) return json({ items: [run()], page: 1, pageSize: 25, totalCount: 1 })
    throw new Error(`Unexpected request ${url}`)
  }

  const current = await render('/mrp/planning/mrp-runs')
  try {
    const text = renderedText(current.toJSON())
    assert.match(text, /وضعیت جاری MRP/)
    assert.match(text, /آخرین نتیجه آماده/)
    assert.match(text, /MRP-RUN-000008/)
    assert.match(text, /به‌روز/)
    assert.match(text, /خطاهای مسدودکننده/)
    assert.match(text, /۲۴/)
    assert.match(text, /هشدارهای غیرمسدودکننده/)
    assert.match(text, /۴۸۲/)
    assert.match(text, /پیش‌بینی رسمی ثبت نشده است/)
    assert.match(text, /منبع تأمین آماده برنامه‌ریزی وجود ندارد/)
    assert.match(text, /PRD-0002/)
    assert.match(text, /CMP-0005/)
    assert.match(text, /0123456789ab/)
    assert.doesNotMatch(text, /\{\{hash\}\}/)
    const links = current.root.findAllByType('a')
    assert.ok(links.some(link => link.props.href === `/mrp/planning/material-planning?runId=${runId}`))
    assert.ok(links.some(link => link.props.href === '/mrp/planning/production-forecast'))
    assert.ok(links.some(link => link.props.href === '/mrp/supply/sourcing'))
    assert.ok(links.some(link => link.props.href === `/mrp/planning/material-planning?runId=${runId}&exceptionSeverity=ERROR&exceptionAffects=true`))
    assert.ok(links.some(link => link.props.href === `/mrp/planning/material-planning?runId=${runId}&exceptionSeverity=WARNING`))
    assert.ok(links.some(link => link.props.href === '/mrp/planning/mrp-runs?view=archive'))
    assert.equal(calls.some(url => url.includes('/api/mrp-runs?page=')), false, 'history must not be loaded on the operational entry page')
  } finally {
    await act(async () => current.unmount())
  }

  calls.length = 0
  const archive = await render('/mrp/planning/mrp-runs?view=archive')
  try {
    const text = renderedText(archive.toJSON())
    assert.match(text, /نسخه‌های برنامه‌ریزی مواد/)
    assert.match(text, /MRP-RUN-000008/)
    assert.ok(calls.some(url => url.includes('/api/mrp-runs?page=1&pageSize=25')))
    assert.equal(calls.some(url => url.endsWith('/api/material-planning/refresh-status')), false)
  } finally {
    await act(async () => archive.unmount())
    globalThis.fetch = originalFetch
  }
})

test('a failed exception overview does not hide the latest ready MRP result', async () => {
  const originalFetch = globalThis.fetch
  globalThis.fetch = async (input: string | URL | Request) => {
    const url = String(input)
    if (url.endsWith('/api/material-planning/refresh-status')) return json({
      status: 'UP_TO_DATE', latestRunId: runId, latestRunNumber: 'MRP-RUN-000008', latestAsOfDate: '2026-08-31',
      lastUpdatedAtUtc: '2026-08-31T08:30:00Z', pendingChangeCount: 0,
    })
    if (url.endsWith(`/api/mrp-runs/${runId}`)) return json(run())
    if (url.includes(`/api/mrp-runs/${runId}/exceptions`) || url.includes(`/api/mrp-runs/${runId}/exception-summary`))
      return json({ title: 'raw server failure' }, 500)
    throw new Error(`Unexpected request ${url}`)
  }

  const current = await render('/mrp/planning/mrp-runs')
  try {
    const text = renderedText(current.toJSON())
    assert.match(text, /MRP-RUN-000008/)
    assert.match(text, /جزئیات خطاها موقتاً در دسترس نیست/)
    assert.doesNotMatch(text, /raw server failure/)
  } finally {
    await act(async () => current.unmount())
    globalThis.fetch = originalFetch
  }
})

test('the Hadron thinking companion appears only while the live MRP result is being recalculated', async () => {
  const originalFetch = globalThis.fetch
  globalThis.fetch = async (input: string | URL | Request) => {
    const url = String(input)
    if (url.endsWith('/api/material-planning/refresh-status')) return json({
      status: 'REFRESHING', latestRunId: runId, latestRunNumber: 'MRP-RUN-000008',
      latestAsOfDate: '2026-08-31', lastUpdatedAtUtc: '2026-08-31T08:30:00Z', pendingChangeCount: 2,
    })
    if (url.endsWith(`/api/mrp-runs/${runId}`)) return json(run())
    if (url.includes(`/api/mrp-runs/${runId}/exceptions?severity=`)) return json({ items: [], page: 1, pageSize: 1, totalCount: 0 })
    if (url.includes(`/api/mrp-runs/${runId}/exception-summary?severity=`)) return json({ items: [], page: 1, pageSize: 6, totalCount: 0 })
    throw new Error(`Unexpected request ${url}`)
  }

  const current = await render('/mrp/planning/mrp-runs')
  try {
    const companions = current.root.findAll(node => node.type === 'img' && node.props.className === 'mrp-refresh-companion')
    assert.equal(companions.length, 1)
    assert.equal(companions[0].props.alt, '')
    assert.equal(companions[0].props['aria-hidden'], 'true')
  } finally {
    await act(async () => current.unmount())
    globalThis.fetch = originalFetch
  }
})

function run() {
  return {
    id: runId, runNumber: 'MRP-RUN-000008', asOfDate: '2026-08-31', horizonEndDate: '2027-07-22',
    engineVersion: 'MRP-10.1.0', status: 'COMPLETED_WITH_EXCEPTIONS', completeness: 'INCOMPLETE',
    assumptions: 'CALENDAR_DAYS', completedAtUtc: '2026-08-31T08:30:00Z', exceptionCount: 506,
    planRole: 'CURRENT',
  }
}

function exceptionSummary(severity: 'ERROR' | 'WARNING') {
  if (severity === 'ERROR') return { items: [
    { itemId: '22222222-2222-2222-2222-222222222222', itemCode: 'PRD-0002', code: 'MISSING_FORECAST', severity, affectsDemand: true, message: 'Missing forecast', firstDate: '2026-08-23', lastDate: '2027-07-23', count: 12 },
    { itemId: '33333333-3333-3333-3333-333333333333', itemCode: 'PRD-0003', code: 'MISSING_FORECAST', severity, affectsDemand: true, message: 'Missing forecast', firstDate: '2026-08-23', lastDate: '2027-07-23', count: 12 },
  ], page: 1, pageSize: 6, totalCount: 2 }
  return { items: [
    { itemId: '44444444-4444-4444-4444-444444444444', itemCode: 'CMP-0005', code: 'NO_PLANNING_READY_EXTERNAL_SOURCE', severity, affectsDemand: false, message: 'No source', firstDate: '2027-05-31', lastDate: '2027-08-22', count: 84 },
    { itemId: '55555555-5555-5555-5555-555555555555', itemCode: 'CMP-0006', code: 'NO_PLANNING_READY_EXTERNAL_SOURCE', severity, affectsDemand: false, message: 'No source', firstDate: '2027-03-12', lastDate: '2027-08-22', count: 164 },
    { itemId: '66666666-6666-6666-6666-666666666666', itemCode: 'CMP-0014', code: 'NO_PLANNING_READY_EXTERNAL_SOURCE', severity, affectsDemand: false, message: 'No source', firstDate: '2027-06-17', lastDate: '2027-08-22', count: 67 },
    { itemId: '77777777-7777-7777-7777-777777777777', itemCode: 'CMP-0015', code: 'NO_PLANNING_READY_EXTERNAL_SOURCE', severity, affectsDemand: false, message: 'No source', firstDate: '2027-03-09', lastDate: '2027-08-22', count: 167 },
  ], page: 1, pageSize: 6, totalCount: 4 }
}

async function render(path: string) {
  const component = await pageModule()
  const i18n = createInstance()
  await i18n.init({ resources: { 'fa-IR': { translation: fa } }, lng: 'fa-IR', fallbackLng: 'fa-IR',
    interpolation: { escapeValue: false }, initAsync: false })
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(I18nextProvider, { i18n },
      React.createElement(MemoryRouter, { initialEntries: [path] },
        React.createElement(Routes, null,
          React.createElement(Route, { path: '/mrp/planning/mrp-runs', element: React.createElement(component.default, { view: 'list' }) })))))
  })
  await flush()
  return renderer
}

async function pageModule() {
  if (Module) return Module
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module = await server.ssrLoadModule('/src/MrpRunsPage.tsx') as typeof import('./MrpRunsPage.tsx')
  return Module
}

function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }) }
function renderedText(node: ReactTestRendererJSON | ReactTestRendererJSON[] | string | null): string { if (node == null) return ''; if (typeof node === 'string') return node; if (Array.isArray(node)) return node.map(renderedText).join(' '); return (node.children ?? []).map(renderedText).join(' ') }
async function flush() { await act(async () => { for (let index = 0; index < 6; index++) await new Promise(resolve => setTimeout(resolve, 10)) }) }
