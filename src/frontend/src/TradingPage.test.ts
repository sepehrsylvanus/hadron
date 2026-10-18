import assert from 'node:assert/strict'
import test from 'node:test'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'
import { buildTradingPerspectiveQuery, tradingLinearOptions, tradingStatusOptions } from './tradingUiSupport.ts'

test('Trading operational worklists preserve server paging, search, and perspective filters', () => {
  const shipment = buildTradingPerspectiveQuery('shipments', 2, 25, ' SHP-000042 ', 'IN_TRANSIT')
  assert.equal(shipment.get('page'), '2')
  assert.equal(shipment.get('pageSize'), '25')
  assert.equal(shipment.get('search'), 'SHP-000042')
  assert.equal(shipment.get('status'), 'IN_TRANSIT')

  const costs = buildTradingPerspectiveQuery('costs', 1, 25, '', 'ALL')
  assert.equal(costs.get('includeVoided'), 'true')
  assert.equal(costs.has('status'), false)
})

test('Trading main page flow lists the six steps in order in both languages', () => {
  const expected = ['createProject', 'orderRegistrations', 'fxOperations', 'shipments', 'transportStages', 'customs']
  assert.deepEqual(Object.keys(fa.trading.flow.steps), expected)
  assert.deepEqual(Object.keys(en.trading.flow.steps), expected)
  assert.equal(fa.trading.flow.steps.createProject, 'ایجاد پروژه جدید')
  assert.equal(fa.trading.flow.steps.shipments, 'ثبت و مدیریت محموله‌ها')
  assert.equal(fa.trading.flow.steps.customs, 'ثبت و مدیریت گمرک و ترخیص')
})

test('Trading UI names the independent workspace and original-currency reporting in FA and EN', () => {
  assert.equal(fa.tradingWorkspace.title, 'فضای کاری بازرگانی')
  assert.equal(en.tradingWorkspace.title, 'Trading Workspace')
  assert.equal(fa.trading.originalCurrencyTotals, 'هزینه معتبر به تفکیک ارز اصلی')
  assert.equal(en.trading.originalCurrencyTotals, 'Valid costs by original currency')
  assert.match(fa.trading.referencesOwnershipHint, /ارز و تأمین‌کننده/)
})

test('Trading UI exposes only canonical next states and explicit correction history', () => {
  assert.deepEqual(tradingStatusOptions('PROJECT', 'DRAFT', false), ['ACTIVE', 'CANCELLED'])
  assert.deepEqual(tradingStatusOptions('PROJECT', 'DELIVERED', false), ['CLOSED'])
  assert.deepEqual(tradingStatusOptions('PROJECT', 'CLOSED', false), [])
  assert.deepEqual(tradingStatusOptions('SHIPMENT', 'IN_CUSTOMS', false), ['PARTIAL_CLEARED', 'CLEARED', 'CANCELLED'])
  assert.deepEqual(tradingStatusOptions('LEG', 'ARRIVED', false), [])
  assert.ok(tradingStatusOptions('CUSTOMS', 'PARTIAL', true).includes('NOT_STARTED'))
  assert.deepEqual(tradingLinearOptions(['REQUEST', 'ALLOCATION', 'DEAL', 'RECEIPT'], 'REQUEST', false), ['ALLOCATION', 'CANCELLED'])
})

test('Trading status labels cover operational codes in both languages', () => {
  assert.equal(fa.trading.codes.IN_TRANSIT, 'در مسیر')
  assert.equal(en.trading.codes.IN_TRANSIT, 'In transit')
  assert.equal(fa.trading.codes.DEFINITIVE, 'قطعی')
  assert.equal(en.trading.codes.MANUAL_PERCENT, 'Manual percentage')
})

test('first-step payload is an allowlist and cannot submit facts from later stages', async () => {
  const { emptySetup, projectSetupPayload } = await import('./tradingProjectSetupSupport.ts')
  const body = projectSetupPayload({ ...emptySetup, titleFa: '  موضوع پروژه  ', responsibleUserId: 'owner', notes: '  توضیح  ' }, '2026-10-18')
  assert.deepEqual(body, { titleFa: 'موضوع پروژه', startDate: '2026-10-18', customerId: null, businessCardId: null, responsibleUserId: 'owner', notes: 'توضیح' })
  assert.deepEqual(Object.keys(body).sort(), ['titleFa', 'startDate', 'customerId', 'businessCardId', 'responsibleUserId', 'notes'].sort())
  assert.equal('priority' in body, false)
  assert.equal('status' in body, false)
  assert.equal('projectNumber' in body, false)
})

test('first-step field definition matches only the supplied form and has explicit lineage', async () => {
  const { readFile } = await import('node:fs/promises')
  const groups = JSON.parse(await readFile(new URL('./tradingProjectSetupFields.json', import.meta.url), 'utf8')) as Array<{ fields: Array<{ key: string; actual: string; proposed: string }> }>
  const fields = groups.flatMap(g => g.fields)
  assert.deepEqual(fields.map(x => x.key), ['projectNumber', 'startDate', 'titleFa', 'customerId', 'businessCardId', 'responsibleUserId', 'status', 'notes'])
  assert.ok(fields.every(f => f.actual && f.proposed))
  for (const field of fields) {
    assert.ok(fa.trading.setup.fields[field.key as keyof typeof fa.trading.setup.fields])
    assert.ok(en.trading.setup.fields[field.key as keyof typeof en.trading.setup.fields])
  }
})
