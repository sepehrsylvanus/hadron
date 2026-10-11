import test from 'node:test'
import assert from 'node:assert/strict'
import { changeSourcingRouteScope, isExternalSourcingEligible, sourcingLeadTimeFields, updateSourcingEditor, type SourcingEditor } from './sourcingFormSupport.ts'

const empty: SourcingEditor = {
  itemId: '', supplierId: '', routeScope: 'DOMESTIC', minimumOrderQuantity: '', shippingModeId: '', incotermId: '',
  supplierLeadTimeDays: '', forwarderToShipmentDays: '', shippingPeriodDays: '', destinationCustomsDays: '', imageUrl: '',
}

test('rapid sourcing form updates preserve item, supplier, and commercial fields', () => {
  let editor = updateSourcingEditor(empty, { itemId: '8674c7c2-e285-4b6c-886f-a7151b6620c6' })
  editor = updateSourcingEditor(editor, { supplierId: '43cd1140-cc8b-4d24-aa9d-6a3b1a69f5b8' })
  editor = updateSourcingEditor(editor, { minimumOrderQuantity: '1000' })
  editor = updateSourcingEditor(editor, { shippingModeId: 'shipping-mode-id' })
  editor = updateSourcingEditor(editor, { incotermId: 'incoterm-id' })

  assert.equal(editor.itemId, '8674c7c2-e285-4b6c-886f-a7151b6620c6')
  assert.equal(editor.supplierId, '43cd1140-cc8b-4d24-aa9d-6a3b1a69f5b8')
  assert.equal(editor.minimumOrderQuantity, '1000')
  assert.equal(editor.shippingModeId, 'shipping-mode-id')
  assert.equal(editor.incotermId, 'incoterm-id')
})

test('source image URL stays attached to the sourcing option draft', () => {
  const imageUrl = 'https://images.example.test/part.png'
  const editor = updateSourcingEditor(empty, { itemId: 'item-1', supplierId: 'supplier-1', imageUrl })
  const updated = updateSourcingEditor(editor, { minimumOrderQuantity: '12' })

  assert.equal(updated.imageUrl, imageUrl)
  assert.equal(updated.itemId, 'item-1')
  assert.equal(updated.supplierId, 'supplier-1')
})

test('domestic scope clears import-only fields without losing common sourcing data', () => {
  const importEditor: SourcingEditor = {
    ...empty,
    routeScope: 'IMPORT',
    itemId: 'item',
    supplierId: 'supplier',
    incotermId: 'incoterm',
    supplierLeadTimeDays: '4',
    forwarderToShipmentDays: '2',
    shippingPeriodDays: '3',
    destinationCustomsDays: '5',
  }

  const domestic = changeSourcingRouteScope(importEditor, 'DOMESTIC')

  assert.equal(domestic.itemId, 'item')
  assert.equal(domestic.supplierId, 'supplier')
  assert.equal(domestic.supplierLeadTimeDays, '4')
  assert.equal(domestic.shippingPeriodDays, '3')
  assert.equal(domestic.incotermId, '')
  assert.equal(domestic.forwarderToShipmentDays, '')
  assert.equal(domestic.destinationCustomsDays, '')
})

test('supplier sourcing accepts external methods and routes pure MAKE to manufacturing', () => {
  assert.equal(isExternalSourcingEligible('BUY'), true)
  assert.equal(isExternalSourcingEligible('OUTSOURCE'), true)
  assert.equal(isExternalSourcingEligible('HYBRID'), true)
  assert.equal(isExternalSourcingEligible('MAKE'), false)
})

test('domestic timeline contains only supplier preparation and local warehouse transport', () => {
  assert.deepEqual(sourcingLeadTimeFields('DOMESTIC'), [
    'supplierLeadTimeDays',
    'shippingPeriodDays',
  ])
  assert.deepEqual(sourcingLeadTimeFields('IMPORT'), [
    'supplierLeadTimeDays',
    'forwarderToShipmentDays',
    'shippingPeriodDays',
    'destinationCustomsDays',
  ])
})
