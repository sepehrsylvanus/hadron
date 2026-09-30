import assert from 'node:assert/strict'
import test from 'node:test'
import fa from './i18n/locales/fa.ts'
import en from './i18n/locales/en.ts'
import { buildTransactionListQuery } from './inventoryUiSupport.ts'

test('inventory ledger omits an empty transaction type instead of sending an invalid filter', () => {
  const unfiltered = buildTransactionListQuery('', 1, 25)
  assert.equal(unfiltered.has('transactionType'), false)
  assert.equal(unfiltered.get('page'), '1')
  assert.equal(unfiltered.get('pageSize'), '25')

  const filtered = buildTransactionListQuery(' RECEIPT ', 2, 50)
  assert.equal(filtered.get('transactionType'), 'RECEIPT')
  assert.equal(filtered.get('page'), '2')
  assert.equal(filtered.get('pageSize'), '50')
})

test('inventory ledger date range labels are translated in both supported languages', () => {
  assert.equal(fa.common.from, 'از تاریخ')
  assert.equal(fa.common.to, 'تا تاریخ')
  assert.equal(en.common.from, 'From date')
  assert.equal(en.common.to, 'To date')
  assert.equal(fa.common.filters, 'فیلترها')
  assert.equal(en.common.filters, 'Filters')
  assert.equal(fa.inventory.companyTotal, 'کل موجودی شرکت')
  assert.equal(en.inventory.companyTotal, 'Company total')
})
