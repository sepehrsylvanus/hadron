import assert from 'node:assert/strict'
import test, { beforeEach } from 'node:test'
import {
  DEMO_BOM_FAMILIES,
  activateBomRevision,
  activeRevisionId,
  demoBomFamily,
  getBomVersionSessionState,
  resetBomVersionSession,
} from './bomVersionSession.ts'

beforeEach(() => resetBomVersionSession())

test('demo BOMs start with their newest revision active, exactly one per family', () => {
  assert.ok(DEMO_BOM_FAMILIES.length >= 3)
  for (const family of DEMO_BOM_FAMILIES) {
    assert.ok(family.revisions.length >= 2, 'each demo BOM must be multi-version')
    const codes = family.revisions.map(item => item.code)
    assert.equal(activeRevisionId(family), family.defaultActiveId)
    assert.equal(family.revisions[family.revisions.length - 1].id, family.defaultActiveId, 'default active must be the newest revision')
    assert.equal(new Set(codes).size, codes.length)
  }
})

test('activating a previous version deactivates the newer active one', () => {
  const family = demoBomFamily('demo-bom-fan')!
  const oldest = family.revisions[0]
  const result = activateBomRevision(family.id, oldest.id, 'مشکل کیفیت در نسخه جدید')
  assert.deepEqual(result, { ok: true, deactivatedRevisionId: 'fan-bom-5' })
  assert.equal(activeRevisionId(family), oldest.id)

  const state = getBomVersionSessionState()
  assert.equal(state.log.length, 1)
  assert.equal(state.log[0].reason, 'مشکل کیفیت در نسخه جدید')
  assert.equal(state.log[0].deactivatedRevisionId, 'fan-bom-5')

  // Rolling forward again deactivates the rolled-back version.
  const forward = activateBomRevision(family.id, 'fan-bom-5', 'ایراد برطرف شد')
  assert.deepEqual(forward, { ok: true, deactivatedRevisionId: oldest.id })
  assert.equal(activeRevisionId(family), 'fan-bom-5')
  assert.equal(getBomVersionSessionState().log.length, 2)
})

test('activation without an explanation is rejected and changes nothing', () => {
  const family = demoBomFamily('demo-bom-fan')!
  for (const blank of ['', '   ', '\n\t']) {
    assert.deepEqual(activateBomRevision(family.id, family.revisions[0].id, blank), { ok: false, error: 'REASON_REQUIRED' })
  }
  assert.equal(activeRevisionId(family), family.defaultActiveId)
  assert.equal(getBomVersionSessionState().log.length, 0)
})

test('activating the already active version is a no-op error', () => {
  const family = demoBomFamily('demo-bom-motor')!
  assert.deepEqual(activateBomRevision(family.id, family.defaultActiveId, 'دلیل'), { ok: false, error: 'ALREADY_ACTIVE' })
  assert.deepEqual(activateBomRevision('missing-family', 'x', 'دلیل'), { ok: false, error: 'UNKNOWN_FAMILY' })
  assert.deepEqual(activateBomRevision(family.id, 'missing-revision', 'دلیل'), { ok: false, error: 'UNKNOWN_REVISION' })
})

test('session reset (page refresh) restores the default active versions', () => {
  const family = demoBomFamily('demo-bom-board')!
  activateBomRevision(family.id, family.revisions[0].id, 'بازگشت آزمایشی')
  assert.notEqual(activeRevisionId(family), family.defaultActiveId)
  resetBomVersionSession()
  assert.equal(activeRevisionId(family), family.defaultActiveId)
  assert.equal(getBomVersionSessionState().log.length, 0)
})
