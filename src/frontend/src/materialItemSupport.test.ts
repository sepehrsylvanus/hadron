import assert from 'node:assert/strict'
import test from 'node:test'
import { requiresBaseUnitChangeConfirmation } from './materialItemSupport.ts'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'

test('changing a material base unit requires an explicit impact confirmation', () => {
  assert.equal(requiresBaseUnitChangeConfirmation('PCE', 'KG'), true)
  assert.equal(requiresBaseUnitChangeConfirmation('PCE', 'PCE'), false)
  assert.match(fa.materials.baseUnitChangeImpact, /BOM.*موجودی.*MRP/)
  assert.match(en.materials.baseUnitChangeImpact, /BOM.*inventory.*MRP/)
})
