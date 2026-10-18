import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const materialMaster = readFileSync(new URL('./MaterialItemMaster.tsx', import.meta.url), 'utf8')

test('Components table includes a Brand column and displays a supplied brand value', () => {
  assert.match(materialMaster, /itemType === 'COMPONENT' \? \[t\('materials\.brand'\)\] : \[\]/)
  assert.match(materialMaster, /itemType === 'COMPONENT' && <td>\{item\.brand\?\.trim\(\) \|\| t\('common\.noData'\)\}<\/td>/)
})

test('Parts table shows linked supplier names to users with sourcing-view permission', () => {
  assert.match(materialMaster, /showSupplierColumn = itemType === 'PART' && has\('procurement\.sourcing\.view'\)/)
  assert.match(materialMaster, /headers=\{\[t\('common\.code'\), t\('common\.name'\), \.\.\.\(showSupplierColumn \? \[t\('sourcing\.supplier'\)\] : \[\]\)/)
  assert.match(materialMaster, /sourcing-options\/item-suppliers/)
  assert.match(materialMaster, /includeInactive: 'false'/)
  assert.match(materialMaster, /supplierLinks\.map\(link => <span key=\{link\.id\}>\{link\.supplier\.name\}/)
})
