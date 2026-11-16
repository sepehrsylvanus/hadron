import assert from 'node:assert/strict'
import test from 'node:test'
import { productionReportActual, productionReportStandard } from './productionOrderSupport.ts'

test('twenty incremental production reports preserve exact standard BOM consumption', () => {
  const samples = Array.from({ length: 20 }, () =>
    productionReportStandard(40, 20, 1, 0))
  assert.deepEqual(samples, Array.from({ length: 20 }, () => 2))
  assert.equal(samples.reduce((sum, value) => sum + value, 0), 40)
})

test('report standard includes rejected output and explicit variance overrides only that report', () => {
  assert.equal(productionReportStandard(40, 20, 1, 1), 4)
  assert.equal(productionReportActual('', 4), 4)
  assert.equal(productionReportActual('4.5', 4), 4.5)
})

test('report standard is bounded for empty and invalid output', () => {
  assert.equal(productionReportStandard(40, 0, 1, 0), 0)
  assert.equal(productionReportStandard(40, 20, -1, -2), 0)
})
