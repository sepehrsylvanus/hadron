import assert from 'node:assert/strict'
import test from 'node:test'
import { localizedName } from './localizedName.ts'

const bilingual = { nameFa: 'نام فارسی', nameEn: 'English name' }

test('MRP-05 Sourcing and MRP-06 BOM use NameEn for English Item references', () => {
  assert.equal(localizedName(bilingual, 'en'), 'English name')
})

test('MRP-05 Sourcing and MRP-06 BOM fall back to NameFa for English Item references', () => {
  assert.equal(localizedName({ nameFa: 'نام فارسی', nameEn: null }, 'en'), 'نام فارسی')
  assert.equal(localizedName({ nameFa: 'نام فارسی', nameEn: '   ' }, 'en'), 'نام فارسی')
})

test('MRP-05 Sourcing and MRP-06 BOM use NameFa for Persian Item references', () => {
  assert.equal(localizedName(bilingual, 'fa-IR'), 'نام فارسی')
})

test('MRP-06 current and historical inactive Unit references follow the same language rule', () => {
  const inactiveUnit = { nameFa: 'واحد فارسی', nameEn: 'English unit', isActive: false }
  assert.equal(localizedName(inactiveUnit, 'en'), 'English unit')
  assert.equal(localizedName(inactiveUnit, 'fa-IR'), 'واحد فارسی')
  assert.equal(localizedName({ ...inactiveUnit, nameEn: null }, 'en'), 'واحد فارسی')
})

test('MRP-07 Forecast and Actual Product, Unit, Category, and Business Unit references use the canonical bilingual fallback', () => {
  assert.equal(localizedName(bilingual, 'en'), 'English name')
  assert.equal(localizedName({ nameFa: 'فقط فارسی', nameEn: null }, 'en'), 'فقط فارسی')
  assert.equal(localizedName(bilingual, 'fa-IR'), 'نام فارسی')
})
