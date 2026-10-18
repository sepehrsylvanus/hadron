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

test('order money is normalized without IEEE-754 rounding or silent grouping repair', async () => {
  const { normalizeOrderAmount, formatOrderAmount } = await import('./tradingOrderSupport.ts')
  assert.equal(normalizeOrderAmount('۹۰۰۷۱۹۹۲۵۴۷۴۰۹۹۳٫۱۲۳۴۵۶'), '9007199254740993.123456')
  assert.equal(normalizeOrderAmount('٩٬٠٠٧٬١٩٩٬٢٥٤٬٧٤٠٬٩٩٣٫١٢٣٤٥٦'), '9007199254740993.123456')
  assert.equal(normalizeOrderAmount('0000123.450000'), '123.45')
  assert.equal(normalizeOrderAmount('0.000001'), '0.000001')
  assert.equal(normalizeOrderAmount('999999999999999999.999999'), '999999999999999999.999999')
  for (const bad of ['0','-2','1e5','NaN','Infinity','1,2','12,34,567','1.1234567','1000000000000000000','0.000000','1.2.3','']) assert.equal(normalizeOrderAmount(bad), null, bad)
  assert.equal(formatOrderAmount('9007199254740993.123456', 'en'), '9,007,199,254,740,993.123456')
  assert.equal(formatOrderAmount('1234.56789', 'fa'), '۱٬۲۳۴٫۵۶۷۸۹')
})

test('order create and edit commands have separate immutable-field allowlists', async () => {
  const { emptyOrder, orderPayload } = await import('./tradingOrderSupport.ts')
  const values = { ...emptyOrder, registrationNumber: '  REG-12  ', registeredAmount: '1,200.123456', currencyId: 'usd', notes: '  note  ', reason: '  typo correction  ' }
  assert.deepEqual(orderPayload(values, 'project', '2026-10-18'), { registrationNumber: 'REG-12', registrationDate: '2026-10-18', registeredAmount: '1200.123456', currencyId: 'usd', notes: 'note', projectId: 'project', status: 'DRAFT' })
  const edited = orderPayload(values, 'different-project', '2026-10-18', { id: 'order', version: 7 } as import('./tradingOrderSupport.ts').ManagedOrder)
  assert.equal('projectId' in edited, false)
  assert.equal('status' in edited, false)
  assert.equal('bankId' in edited, false)
  assert.deepEqual(edited, { registrationNumber: 'REG-12', registrationDate: '2026-10-18', registeredAmount: '1200.123456', currencyId: 'usd', notes: 'note', expectedVersion: 7, reason: 'typo correction' })
  assert.throws(() => orderPayload({ ...values, registeredAmount: '1e8' }, 'project', '2026-10-18'))
})

test('second-stage fields have current/proposed lineage and complete FA/EN labels', async () => {
  const { readFile } = await import('node:fs/promises')
  const fields = JSON.parse(await readFile(new URL('./tradingOrderFields.json', import.meta.url), 'utf8')) as Array<{ key: string; actual: string; proposed: string }>
  assert.deepEqual(fields.map(x => x.key), ['projectId','registrationNumber','registrationDate','currencyId','registeredAmount','status','notes'])
  for (const field of fields) {
    assert.ok(field.actual && field.proposed)
    assert.ok(fa.trading.orders.fields[field.key as keyof typeof fa.trading.orders.fields])
    assert.ok(en.trading.orders.fields[field.key as keyof typeof en.trading.orders.fields])
  }
  assert.deepEqual(tradingStatusOptions('ORDER', 'DRAFT', false), ['SUBMITTED', 'CANCELLED'])
  assert.ok(tradingStatusOptions('ORDER', 'SUBMITTED', true).includes('DRAFT'))
  assert.deepEqual(tradingStatusOptions('ORDER', 'CANCELLED', true), [])
})
