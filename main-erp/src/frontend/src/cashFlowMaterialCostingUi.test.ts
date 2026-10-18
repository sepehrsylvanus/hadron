import assert from 'node:assert/strict'
import test from 'node:test'
import {
  buildMaterialCostBookOptionsQuery,
  buildMaterialCostBookQuery,
  buildEffectiveMaterialCostBookSelectionsQuery,
  buildPurchasePriceHistoryQuery,
  findExactPriceUnit,
  isPositiveCostBookDecimal,
  materialCostBookItemsPath,
  materialCostBookRevisionPath,
  normalizeCostBookDecimalInput,
  preservePagedSelection,
  previewNormalizedUnitPrice,
} from './cashFlowMaterialCostingUi.ts'

test('material cost-book queries preserve server paging and normalized search', () => {
  assert.equal(buildMaterialCostBookQuery({ page: 3, pageSize: 25, search: '  MCB-10  ', status: 'DRAFT' }).toString(), 'page=3&pageSize=25&search=MCB-10&status=DRAFT')
  assert.equal(buildMaterialCostBookOptionsQuery({ page: 8, pageSize: 20, search: ' PRT-100 ' }).toString(), 'page=8&pageSize=20&search=PRT-100')
  assert.equal(buildEffectiveMaterialCostBookSelectionsQuery({ asOfDate: '2026-09-07', page: 4, pageSize: 20, search: '  MCB-2  ' }).toString(), 'asOfDate=2026-09-07&page=4&pageSize=20&search=MCB-2')
})

test('purchase history omits blank date filters and keeps one explicit valuation date', () => {
  const query = buildPurchasePriceHistoryQuery({ page: 2, pageSize: 25, search: ' PO-1 ', itemId: 'item-1', supplierId: '', fromOrderDate: '', toOrderDate: '', valuationAsOfDate: '2026-09-07' })
  assert.equal(query.get('search'), 'PO-1')
  assert.equal(query.get('itemId'), 'item-1')
  assert.equal(query.has('supplierId'), false)
  assert.equal(query.has('fromOrderDate'), false)
  assert.equal(query.has('toOrderDate'), false)
  assert.equal(query.get('valuationAsOfDate'), '2026-09-07')

  const exactRateSet = buildPurchasePriceHistoryQuery({ page: 1, pageSize: 25, valuationFxRateSetId: 'fx-revision-7' })
  assert.equal(exactRateSet.get('valuationFxRateSetId'), 'fx-revision-7')
  assert.equal(exactRateSet.has('valuationAsOfDate'), false)
})

test('a selected paged lookup filter remains visible and clearable outside its source page', () => {
  const page = [{ id: 'item-21', label: 'Item 21' }]
  assert.deepEqual(preservePagedSelection('item-01', page, { id: 'item-01', label: 'Selected item (outside this page)' }), [
    { id: 'item-01', label: 'Selected item (outside this page)' },
    ...page,
  ])
  assert.strictEqual(preservePagedSelection('item-21', page, { id: 'item-21', label: 'unused' }), page)
  assert.strictEqual(preservePagedSelection('', page, { id: '', label: 'unused' }), page)
})

test('cost book routes distinguish view from edit', () => {
  assert.equal(materialCostBookRevisionPath('a/b'), '/cash-flow/material-cost-books/revisions/a%2Fb')
  assert.equal(materialCostBookRevisionPath('rev-1', 'edit'), '/cash-flow/material-cost-books/revisions/rev-1/edit')
  assert.equal(materialCostBookItemsPath('a/b'), '/cash-flow/material-cost-books/revisions/a%2Fb/edit#material-cost-book-items')
})

test('financial input normalizes Persian digits and stays strict', () => {
  assert.equal(normalizeCostBookDecimalInput('۱٬۲۳۴٫۵۰۰'), '1234.500')
  assert.equal(isPositiveCostBookDecimal('1234.500'), true)
  assert.equal(isPositiveCostBookDecimal('0'), false)
  assert.equal(isPositiveCostBookDecimal('1e3'), false)
  assert.equal(isPositiveCostBookDecimal('1.1234567890123'), false)
})

test('formula preview uses exact bigint arithmetic', () => {
  assert.equal(previewNormalizedUnitPrice('2200000', '1000', '1', 8), '2200')
  assert.equal(previewNormalizedUnitPrice('1', '3', '1', 12), '0.333333333333')
  assert.equal(previewNormalizedUnitPrice('0.1', '0.2', '0.5', 12), '1')
  assert.equal(previewNormalizedUnitPrice('not-a-number', '1', '1'), null)
})

test('an unavailable stored price unit never falls back to another unit', () => {
  const units = [{ unitId: 'kg', code: 'KG' }, { unitId: 'bag', code: 'BAG' }]
  assert.equal(findExactPriceUnit(units, 'retired-unit'), undefined)
  assert.deepEqual(findExactPriceUnit(units, 'bag'), units[1])
})
