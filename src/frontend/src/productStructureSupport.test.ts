import assert from 'node:assert/strict'
import test from 'node:test'
import { localizedStructureName, structureItemRoute, structureTypeKey } from './productStructureSupport.ts'

test('product structure names follow fa/en fallback without changing stored values', () => {
  const bilingual = { nameFa: 'بدنه', nameEn: 'Housing' }
  const persianOnly = { nameFa: 'پیچ', nameEn: '  ' }
  assert.equal(localizedStructureName(bilingual, 'fa-IR'), 'بدنه')
  assert.equal(localizedStructureName(bilingual, 'en'), 'Housing')
  assert.equal(localizedStructureName(persianOnly, 'en'), 'پیچ')
  assert.deepEqual(bilingual, { nameFa: 'بدنه', nameEn: 'Housing' })
})

test('product structure links preserve the official master object routes', () => {
  assert.equal(structureItemRoute('PRODUCT', 'p1'), '/mrp/products/p1')
  assert.equal(structureItemRoute('PART', 'p2'), '/mrp/parts/p2')
  assert.equal(structureItemRoute('COMPONENT', 'p3'), '/mrp/components/p3')
  assert.equal(structureItemRoute('UNKNOWN', 'p4'), null)
  assert.equal(structureTypeKey('PRODUCT'), 'products.object')
  assert.equal(structureTypeKey('PART'), 'materials.part')
  assert.equal(structureTypeKey('COMPONENT'), 'materials.component')
})
