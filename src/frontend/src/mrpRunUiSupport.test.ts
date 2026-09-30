import assert from 'node:assert/strict'
import test from 'node:test'
import { isPersianLanguage, normalizeJalaliDateInput, toAsciiDigits } from './mrpRunUiSupport.ts'
import { formatBusinessDate, formatDualBusinessDateRange } from './i18n/businessDate.ts'

test('MRP Jalali input accepts Persian or ASCII digits as one YYYYMMDD value', () => {
  assert.equal(toAsciiDigits('۱۴۰۵۰۶۰۸'), '14050608')
  assert.equal(normalizeJalaliDateInput('۱۴۰۵/۰۶/۰۸'), '14050608')
  assert.equal(normalizeJalaliDateInput('14050608'), '14050608')
  assert.equal(normalizeJalaliDateInput('14051301'), null)
  assert.equal(normalizeJalaliDateInput('140506'), null)
})

test('MRP business dates render Jalali in Persian and Gregorian in English', () => {
  assert.match(formatBusinessDate('2026-08-30', 'fa-IR'), /۱۴۰۵/)
  assert.equal(formatBusinessDate('2026-08-30', 'en'), '30/08/2026')
  assert.equal(isPersianLanguage('fa-IR'), true)
  assert.equal(isPersianLanguage('en'), false)
})

test('forecast periods show the complete Jalali and Gregorian ranges with locale priority', () => {
  assert.deepEqual(formatDualBusinessDateRange('2026-03-21', '2026-04-20', 'fa-IR'), {
    primary: '۱۴۰۵/۰۱/۰۱ - ۱۴۰۵/۰۱/۳۱',
    secondary: '21/03/2026 - 20/04/2026',
  })
  assert.deepEqual(formatDualBusinessDateRange('2026-03-21', '2026-04-20', 'en'), {
    primary: '21/03/2026 - 20/04/2026',
    secondary: '۱۴۰۵/۰۱/۰۱ - ۱۴۰۵/۰۱/۳۱',
  })
})
