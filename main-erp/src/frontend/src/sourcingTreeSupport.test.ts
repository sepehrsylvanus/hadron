import test from 'node:test'
import assert from 'node:assert/strict'
import { readinessReasonKey, selectedOptionAfterReload, toggleExpandedItem } from './sourcingTreeSupport.ts'

test('sourcing tree expansion preserves other expanded items', () => {
  const expanded = new Set(['item-a'])
  const withSecond = toggleExpandedItem(expanded, 'item-b')
  assert.deepEqual([...withSecond].sort(), ['item-a', 'item-b'])
  assert.deepEqual([...toggleExpandedItem(withSecond, 'item-a')], ['item-b'])
  assert.deepEqual([...expanded], ['item-a'])
})

test('sourcing readiness codes resolve to human translation keys with a safe fallback', () => {
  assert.equal(readinessReasonKey('BASE_UNIT_CHANGED'), 'sourcing.reasonBaseUnitChanged')
  assert.equal(readinessReasonKey('MOQ_MISSING'), 'sourcing.reasonMoqMissing')
  assert.equal(readinessReasonKey('NEW_SERVER_REASON'), 'sourcing.reasonUnknown')
})

test('selected sourcing option survives reload or falls back to the first visible option', () => {
  const groups = [{ options: [{ id: 'one' }, { id: 'two' }] }, { options: [{ id: 'three' }] }]
  assert.equal(selectedOptionAfterReload('two', groups)?.id, 'two')
  assert.equal(selectedOptionAfterReload('missing', groups)?.id, 'one')
  assert.equal(selectedOptionAfterReload(undefined, [])?.id, undefined)
})
