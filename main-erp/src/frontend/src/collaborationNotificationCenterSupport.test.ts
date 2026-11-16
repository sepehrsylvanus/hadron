import assert from 'node:assert/strict'
import test from 'node:test'
import { createLatestRequestGate, focusOnNextFrame } from './collaborationNotificationCenterSupport.ts'

test('only the latest notification request may update the visible state', () => {
  const gate = createLatestRequestGate()
  const slowRequest = gate.begin()
  const fastRequest = gate.begin()

  assert.equal(gate.isLatest(slowRequest), false)
  assert.equal(gate.isLatest(fastRequest), true)

  gate.invalidate()
  assert.equal(gate.isLatest(fastRequest), false, 'unmount or permission changes invalidate in-flight work')
})

test('dialog focus is scheduled after the surface has rendered', () => {
  let scheduled = false
  let focused = false

  focusOnNextFrame(
    { focus: () => { focused = true } },
    callback => {
      scheduled = true
      assert.equal(focused, false)
      callback()
    },
  )

  assert.equal(scheduled, true)
  assert.equal(focused, true)
})

test('missing focus targets are safe during permission-aware rendering', () => {
  let scheduled = false
  focusOnNextFrame(null, () => { scheduled = true })
  assert.equal(scheduled, false)
})
