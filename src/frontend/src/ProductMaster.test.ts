import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const productMaster = readFileSync(new URL('./ProductMaster.tsx', import.meta.url), 'utf8')
const materialMaster = readFileSync(new URL('./MaterialItemMaster.tsx', import.meta.url), 'utf8')

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
