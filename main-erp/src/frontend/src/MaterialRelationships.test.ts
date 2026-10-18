import assert from 'node:assert/strict'
import test from 'node:test'
import { applicableItemsUrl, supplierLinkPayload } from './materialRelationshipSupport.ts'

test('material supplier link contains no price or currency contract', () => {
  const payload = supplierLinkPayload('item-1', 'supplier-1')
  assert.deepEqual(payload, {
    itemId: 'item-1',
    supplierId: 'supplier-1',
    minimumOrderQuantity: null,
    shippingModeId: null,
    incotermId: null,
    supplierLeadTimeDays: null,
    forwarderToShipmentDays: null,
    shippingPeriodDays: null,
    destinationCustomsDays: null,
  })
  assert.equal('price' in payload, false)
  assert.equal('currencyId' in payload, false)
})

test('BOM child picker defaults to applicable items and manager override is explicit', () => {
  const normal = new URL(applicableItemsUrl('/api', 'parent-1', false, ' A10 ', 2, 25), 'http://localhost')
  assert.equal(normal.searchParams.get('parentItemId'), 'parent-1')
  assert.equal(normal.searchParams.get('showAll'), 'false')
  assert.equal(normal.searchParams.get('search'), 'A10')
  assert.equal(normal.searchParams.get('page'), '2')

  const override = new URL(applicableItemsUrl('/api', 'parent-1', true, '', 1, 25), 'http://localhost')
  assert.equal(override.searchParams.get('showAll'), 'true')
})
