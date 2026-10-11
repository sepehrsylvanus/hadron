import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'

const productMaster = readFileSync(new URL('./ProductMaster.tsx', import.meta.url), 'utf8')
const materialMaster = readFileSync(new URL('./MaterialItemMaster.tsx', import.meta.url), 'utf8')

test('critical-days label describes days remaining to critical conditions', () => {
  assert.equal(fa.products.criticalDays, 'روز مانده به شرایط بحرانی')
  assert.equal(en.products.criticalDays, 'Days remaining to critical conditions')
})

test('Product code is displayed below its name in the overview', () => {
  const objectPage = productMaster.slice(productMaster.indexOf('export function ProductObjectPage'))
  assert.match(objectPage, /<Definition label={t\('common\.name'\)}>\{product\.nameFa\}<small className="product-object-code">\{t\('common\.code'\)\}: <bdi className="ui-technical">\{product\.code\}<\/bdi><\/small><\/Definition>/)
  assert.doesNotMatch(objectPage, /<Definition label={t\('common\.code'\)} technical>/)
})

test('Product, Part, and Component masters expose one field labelled Name', () => {
  for (const source of [productMaster, materialMaster]) {
    assert.match(source, /<FormField label={t\('common\.name'\)} required>/)
    assert.doesNotMatch(source, /<(?:FormField|Definition) label={t\('common\.name(?:Fa|En)'\)}/)
  }
  assert.match(productMaster, /<Definition label={t\('common\.name'\)}>\{product\.nameFa\}/)
})

test('Product supply type is available in the editor and display page', () => {
  assert.match(productMaster, /t\('products\.supplyMethod'\)/)
  assert.match(productMaster, /supplyMethods\.map\(method =>/)
  assert.match(productMaster, /<Definition label={t\('products\.supplyMethod'\)}>\{product\.supplyMethod/)
  assert.match(productMaster, /supplyMethodUiOnly/)
  assert.match(productMaster, /const \{ supplyMethod: selectedSupplyMethod, \.\.\.apiEditor \} = editor/)
  assert.match(productMaster, /const payload = \{ \.\.\.apiEditor/)
})

test('Product Business Unit and Notes are hidden on the object page without clearing stored values', () => {
  const objectPage = productMaster.slice(productMaster.indexOf('export function ProductObjectPage'))
  assert.doesNotMatch(objectPage, /<FormField label={t\('products\.businessUnit'\)}/)
  assert.doesNotMatch(objectPage, /<Definition label={t\('products\.businessUnit'\)}/)
  assert.doesNotMatch(objectPage, /<FormField[^>]*label={t\('common\.notes'\)}/)
  assert.doesNotMatch(objectPage, /<Definition label={t\('common\.notes'\)}/)
  assert.match(objectPage, /businessUnitId: product\.businessUnit\?\.id \?\? ''/)
  assert.match(objectPage, /notes: product\.notes \?\? ''/)
  assert.match(objectPage, /businessUnitId: editor\.businessUnitId \|\| null/)
  assert.match(objectPage, /notes: editor\.notes \|\| null/)
})

test('Part and Component Notes are hidden from their shared create, edit, and display form while preserved on save', () => {
  assert.doesNotMatch(materialMaster, /<FormField[^>]*label={t\('common\.notes'\)}/)
  assert.doesNotMatch(materialMaster, /<Definition label={t\('common\.notes'\)}/)
  assert.match(materialMaster, /notes: item\.notes \?\? ''/)
  assert.match(materialMaster, /notes: editor\.notes \|\| null/)
})
