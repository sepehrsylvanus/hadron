import assert from 'node:assert/strict'
import test from 'node:test'
import { BOM_PICKER_PAGE_SIZE, bomBuilderPath, bomLifecycleStep, bomSubstructureState, childTypeFor, expandableBomParentIds, findBomBaseUnitMismatch, parseBomTrail, popBomTrail, preferredBomRevision, pushBomTrail, validBomUsageCoefficient } from './bomBuilderSupport.ts'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'

const product = { itemType: 'PRODUCT', itemId: '11111111-1111-1111-1111-111111111111' }
const part = { itemType: 'PART', itemId: '22222222-2222-2222-2222-222222222222' }

test('BOM builder keeps a deterministic unlimited parent trail', () => {
  const first = pushBomTrail('', product)
  const second = pushBomTrail(first, part)
  assert.deepEqual(parseBomTrail(second), [product, part])
  assert.deepEqual(popBomTrail(second), { previous: part, trail: first })
  assert.deepEqual(popBomTrail(first), { previous: product, trail: '' })
})

test('BOM builder rejects malformed trail entries', () => {
  assert.deepEqual(parseBomTrail('PRODUCT:not-a-guid,UNKNOWN:11111111-1111-1111-1111-111111111111'), [])
})

test('BOM relationship and direct object routes follow the canonical hierarchy', () => {
  assert.equal(childTypeFor('PRODUCT'), 'PART')
  assert.equal(childTypeFor('PART'), 'COMPONENT')
  assert.equal(childTypeFor('COMPONENT'), 'COMPONENT')
  assert.equal(
    bomBuilderPath(product, product),
    '/mrp/engineering/bom?parentItemId=11111111-1111-1111-1111-111111111111&parentType=PRODUCT&rootItemId=11111111-1111-1111-1111-111111111111&rootItemType=PRODUCT',
  )
})

test('BOM picker keeps more than the first ten eligible items reachable', () => {
  assert.equal(BOM_PICKER_PAGE_SIZE, 25)
  assert.ok(BOM_PICKER_PAGE_SIZE > 15)
})

test('substructure opens the editable Draft when present and otherwise requires explicit creation', () => {
  const approved = { id: 'approved', status: 'APPROVED' }
  const draft = { id: 'draft', status: 'DRAFT' }
  assert.deepEqual(preferredBomRevision([approved, draft]), draft)
  assert.deepEqual(preferredBomRevision([approved]), approved)
  assert.equal(preferredBomRevision([]), null)
})

test('BOM tree distinguishes a missing substructure from an uninspected item with an explicit action', () => {
  assert.equal(bomSubstructureState(true, true), 'DEFINED')
  assert.equal(bomSubstructureState(false, true), 'MISSING')
  assert.equal(bomSubstructureState(false, false), 'UNINSPECTED')
  assert.equal(fa.bom.structureMissing, 'فاقد زیرساختار BOM')
  assert.equal(fa.bom.defineSubstructure, 'تعریف BOM')
  assert.equal(fa.bom.usageCoefficient, 'مقدار مصرف')
  assert.match(fa.bom.visualTreeHelp, /واحد واقعی/)
  assert.equal(en.bom.structureMissing, 'No BOM substructure')
  assert.equal(en.bom.defineSubstructure, 'Define BOM')
  assert.equal(en.bom.usageCoefficient, 'Usage quantity')
  assert.match(en.bom.visualTreeHelp, /actual unit/)
})

test('expand all includes every loaded parent with children and excludes leaf revisions', () => {
  const revisions = [
    { parentItem: { id: 'product' }, lines: [{ id: 'part-line' }] },
    { parentItem: { id: 'part' }, lines: [{ id: 'component-line' }] },
    { parentItem: { id: 'component' }, lines: [] },
  ]
  assert.deepEqual(expandableBomParentIds(revisions), ['product', 'part'])
})

test('BOM release workflow makes Draft check and final approval explicit', () => {
  assert.equal(bomLifecycleStep('DRAFT', false), 'DRAFT')
  assert.equal(bomLifecycleStep('DRAFT', true), 'VALIDATE')
  assert.equal(bomLifecycleStep('APPROVED', false), 'APPROVE')
  assert.equal(bomLifecycleStep('OBSOLETE', false), 'APPROVE')
  assert.equal(fa.bom.validate, 'بررسی ساختار')
  assert.equal(fa.bom.approve, 'تأیید و ثبت BOM')
  assert.match(fa.bom.lifecycleHint, /پیش.نویس/)
  assert.equal(en.bom.validate, 'Check structure')
  assert.equal(en.bom.approve, 'Approve and release BOM')
})

test('usage coefficient editing rejects empty, zero, negative, and nonnumeric input without changing BOM truth', () => {
  assert.equal(validBomUsageCoefficient(''), false)
  assert.equal(validBomUsageCoefficient('0'), false)
  assert.equal(validBomUsageCoefficient('-1'), false)
  assert.equal(validBomUsageCoefficient('not-a-number'), false)
  assert.equal(validBomUsageCoefficient('0.000001'), true)
  assert.equal(validBomUsageCoefficient('7.75'), true)
})

test('BOM unit drift identifies the parent header before child lines and supports a precise UI message', () => {
  const piece = { id: 'piece', nameFa: 'عدد', nameEn: 'Piece' }
  const kilogram = { id: 'kg', nameFa: 'کیلوگرم', nameEn: 'Kilogram' }
  const mismatch = findBomBaseUnitMismatch({
    parentItem: { code: 'PRT-0004', currentBaseUnit: kilogram },
    parentBaseUnitSnapshot: piece,
    lines: [{ childItem: { code: 'CMP-0015', currentBaseUnit: kilogram }, childBaseUnitSnapshot: kilogram }],
  })
  assert.deepEqual(mismatch, { role: 'PARENT', code: 'PRT-0004', snapshot: piece, current: kilogram })
  assert.match(fa.bom.parentBaseUnitChanged, /حذف و ایجاد مجدد شاخه‌ها مشخصات نسخه را اصلاح نمی‌کند/)
  assert.match(en.bom.parentBaseUnitChanged, /Rebuilding branches does not replace the header snapshot/)
})
