import assert from 'node:assert/strict'
import test from 'node:test'
import { fetchReferenceTypesUntilAvailable, usesGeneratedReferenceCode } from './referenceDataSupport.ts'

test('only generic allowlisted Reference Values use generated codes', () => {
  for (const type of ['COUNTRY', 'BUSINESS_UNIT', 'PRODUCT_CATEGORY', 'PART_CATEGORY',
    'COMPONENT_CATEGORY', 'PAYMENT_TERM', 'SHIPPING_MODE', 'INCOTERM']) {
    assert.equal(usesGeneratedReferenceCode(type), true)
  }
  assert.equal(usesGeneratedReferenceCode('CURRENCY'), false)
  assert.equal(usesGeneratedReferenceCode('UNIT'), false)
})

test('Reference Data retries a startup fetch failure and returns all eight canonical types', async () => {
  const controller = new AbortController()
  const failures: string[] = []
  let calls = 0
  const canonical = [
    'COUNTRY', 'BUSINESS_UNIT', 'PRODUCT_CATEGORY', 'PART_CATEGORY',
    'COMPONENT_CATEGORY', 'PAYMENT_TERM', 'SHIPPING_MODE', 'INCOTERM',
  ].map((code, index) => ({ id: String(index + 1), code, nameFa: code, nameEn: code }))

  const rows = await fetchReferenceTypesUntilAvailable('/api/reference-data/types', controller.signal,
    message => failures.push(message), async () => {
      calls += 1
      if (calls === 1) throw new TypeError('Failed to fetch')
      return new Response(JSON.stringify(canonical), { status: 200,
        headers: { 'Content-Type': 'application/json' } })
    }, async () => undefined)

  assert.equal(calls, 2)
  assert.deepEqual(failures, ['Failed to fetch'])
  assert.deepEqual(rows.map(row => row.code).sort(), canonical.map(row => row.code).sort())
})

test('Reference Data reports an API problem before retrying', async () => {
  const controller = new AbortController()
  const failures: string[] = []
  let calls = 0

  const rows = await fetchReferenceTypesUntilAvailable('/api/reference-data/types', controller.signal,
    message => failures.push(message), async () => {
      calls += 1
      return calls === 1
        ? new Response(JSON.stringify({ detail: 'Reference Data API unavailable' }), { status: 503,
          headers: { 'Content-Type': 'application/json' } })
        : new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })
    }, async () => undefined)

  assert.deepEqual(failures, ['Reference Data API unavailable'])
  assert.deepEqual(rows, [])
})
