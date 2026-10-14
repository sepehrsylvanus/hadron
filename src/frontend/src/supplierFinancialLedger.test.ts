import assert from 'node:assert/strict'
import test from 'node:test'
import {
  formatJalaliInputValue,
  gregorianToJalaliParts,
  jalaliToGregorianIso,
  parseJalaliDateInput,
} from './i18n/businessDate.ts'
import {
  buildSupplierLedger,
  fakeBalanceForSupplier,
  resolveSupplierBalance,
  summarizeSupplierLedger,
  supplierLedgerDateRange,
} from './supplierFinancialLedger.ts'

const foreignSupplier = { id: 'supplier-1', supplierNumber: 'SUP-0001', country: { code: 'US' } }
const domesticSupplier = { id: 'supplier-2', supplierNumber: 'SUP-0002', country: { code: 'IR' } }

test('fake supplier balances are deterministic per supplier', () => {
  const first = fakeBalanceForSupplier(foreignSupplier)
  const second = fakeBalanceForSupplier(foreignSupplier)
  assert.deepEqual(first, second)
  assert.equal(first.currencyCode, 'USD')
  assert.equal(first.decimalPrecision, 2)
  assert.notEqual(first.amount, '0.00')

  const domestic = fakeBalanceForSupplier(domesticSupplier)
  assert.equal(domestic.currencyCode, 'IRR')
  assert.equal(domestic.decimalPrecision, 0)
  assert.match(domestic.amount, /^-?\d+$/)
})

test('real API balances win over fake balances and unresolved currencies stay pending', () => {
  const real = resolveSupplierBalance({
    ...foreignSupplier,
    financialBalanceAmount: '1234567.89',
    financialBalanceCurrencyId: 'currency-usd',
  }, [{ id: 'currency-usd', code: 'USD', decimalPrecision: 2 }])
  assert.deepEqual(real, { amount: '1234567.89', currencyCode: 'USD', decimalPrecision: 2, isFake: false, isPending: false })

  const pending = resolveSupplierBalance({
    ...foreignSupplier,
    financialBalanceAmount: '10.00',
    financialBalanceCurrencyId: 'currency-unknown',
  })
  assert.equal(pending.isPending, true)
  assert.equal(pending.isFake, false)

  const fake = resolveSupplierBalance(foreignSupplier)
  assert.equal(fake.isFake, true)
  assert.equal(fake.isPending, false)
})

test('fake ledger entries always reconcile exactly to the displayed total', () => {
  for (const supplier of [foreignSupplier, domesticSupplier]) {
    const balance = resolveSupplierBalance(supplier)
    const entries = buildSupplierLedger(supplier, balance, new Date('2026-10-14T12:00:00Z'))
    assert.ok(entries.length >= 9, `expected a useful statement, got ${entries.length} entries`)
    assert.equal(entries[entries.length - 1].runningBalance, balance.amount)
    const signedTotal = entries.reduce((total, entry) => total + Number(entry.signedAmount), 0)
    assert.ok(Math.abs(signedTotal - Number(balance.amount)) < 0.000001)
    const dates = entries.map(entry => entry.date)
    assert.deepEqual(dates, [...dates].sort(), 'ledger dates must be ascending')
    for (const entry of entries) {
      assert.ok(Number(entry.debit) === 0 || Number(entry.credit) === 0, 'an entry is either debit or credit')
      assert.match(entry.reference, /^(BAL|INV|ADV|PAY|FRG|RET|ADJ)-\d+$/)
    }
  }
})

test('Jalali (Shamsi) dates convert both ways, including leap years', () => {
  assert.deepEqual(gregorianToJalaliParts('2026-08-24'), { year: 1405, month: 6, day: 2 })
  assert.equal(formatJalaliInputValue('2026-08-24'), '۱۴۰۵/۰۶/۰۲')
  assert.equal(jalaliToGregorianIso(1405, 6, 2), '2026-08-24')
  assert.equal(jalaliToGregorianIso(1405, 1, 1), '2026-03-21')
  // 1403 is a leap year (30 Esfand exists); 1404 is not.
  assert.equal(jalaliToGregorianIso(1403, 12, 30), '2025-03-20')
  assert.equal(jalaliToGregorianIso(1404, 12, 30), null)
  assert.equal(jalaliToGregorianIso(1405, 13, 1), null)
})

test('Jalali date input accepts Persian, Arabic, and Latin digits', () => {
  assert.equal(parseJalaliDateInput('۱۴۰۵/۰۶/۰۲'), '2026-08-24')
  assert.equal(parseJalaliDateInput('1405/06/02'), '2026-08-24')
  assert.equal(parseJalaliDateInput('14050602'), '2026-08-24')
  assert.equal(parseJalaliDateInput('۱۴۰۵۰۶۰۲'), '2026-08-24')
  assert.equal(parseJalaliDateInput('1405/06/32'), null)
  assert.equal(parseJalaliDateInput('1405/6/2'), null)
  assert.equal(parseJalaliDateInput(''), null)
})

test('ledger period summary respects the from/to date filter', () => {
  const balance = resolveSupplierBalance(foreignSupplier)
  const entries = buildSupplierLedger(foreignSupplier, balance, new Date('2026-10-14T12:00:00Z'))
  const range = supplierLedgerDateRange(entries)

  const full = summarizeSupplierLedger(entries, range.from, range.to, balance.decimalPrecision)
  assert.equal(full.count, entries.length)
  assert.equal(full.openingBalance, '0.00')
  assert.equal(full.closingBalance, balance.amount)

  const empty = summarizeSupplierLedger(entries, '2000-01-01', '2000-01-31', balance.decimalPrecision)
  assert.equal(empty.count, 0)
  assert.equal(empty.closingBalance, '0.00')

  const firstDay = summarizeSupplierLedger(entries, range.from, range.from, balance.decimalPrecision)
  assert.equal(firstDay.count, 1)
  assert.equal(firstDay.closingBalance, entries[0].runningBalance)
})
