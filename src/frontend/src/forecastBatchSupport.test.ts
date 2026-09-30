import assert from 'node:assert/strict'
import test from 'node:test'
import { batchEntries, forecastMatrixKey, monthCodesBetween, normalizeJalaliMonthDate, normalizePeriodCode, updateForecastDraft } from './forecastBatchSupport.ts'

test('Jalali forecast period accepts exactly YYYYMM with a valid month', () => {
  assert.equal(normalizePeriodCode('140501'), '140501')
  assert.equal(normalizePeriodCode(' 140512 '), '140512')
  assert.equal(normalizePeriodCode('1405/01'), null)
  assert.equal(normalizePeriodCode('140500'), null)
  assert.equal(normalizePeriodCode('140513'), null)
})

test('forecast draft keeps zero distinct from blank and ignores unchanged values', () => {
  const missing = { productId: 'p1', currentForecastQty: null, seriesVersion: null }
  const existing = { productId: 'p2', currentForecastQty: 25, seriesVersion: 3 }
  let drafts = updateForecastDraft({}, missing, '0')
  assert.equal(drafts.p1.forecastQty, '0')
  assert.equal(drafts.p1.isRevision, false)
  drafts = updateForecastDraft(drafts, existing, '30')
  assert.deepEqual(drafts.p2, { productId: 'p2', forecastQty: '30', expectedVersion: 3, isRevision: true })
  drafts = updateForecastDraft(drafts, existing, '25')
  assert.equal(drafts.p2, undefined)
  drafts = updateForecastDraft(drafts, missing, '')
  assert.deepEqual(drafts, {})
})

test('batch payload preserves explicit values and expected versions', () => {
  const entries = batchEntries({
    p1: { productId: 'p1', forecastQty: '0', expectedVersion: null, isRevision: false },
    p2: { productId: 'p2', forecastQty: '12.500001', expectedVersion: 4, isRevision: true },
  })
  assert.deepEqual(entries, [
    { productId: 'p1', forecastQty: 0, expectedVersion: null },
    { productId: 'p2', forecastQty: 12.500001, expectedVersion: 4 },
  ])
})

test('named forecast range accepts only first-day Jalali month anchors', () => {
  assert.equal(normalizeJalaliMonthDate('14050101'), '14050101')
  assert.equal(normalizeJalaliMonthDate(' 14051201 '), '14051201')
  assert.equal(normalizeJalaliMonthDate('14050102'), null)
  assert.equal(normalizeJalaliMonthDate('14051301'), null)
  assert.equal(normalizeJalaliMonthDate('140501'), null)
})

test('named forecast range creates every inclusive month across Jalali years', () => {
  assert.deepEqual(monthCodesBetween('14051101', '14060201'), ['140511', '140512', '140601', '140602'])
  assert.deepEqual(monthCodesBetween('14050301', '14050201'), [])
  assert.equal(forecastMatrixKey('140501', 'product-1'), '140501:product-1')
})
