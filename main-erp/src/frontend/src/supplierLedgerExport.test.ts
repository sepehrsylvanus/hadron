import assert from 'node:assert/strict'
import test from 'node:test'
import { buildSupplierLedger, buildCombinedSupplierLedger, resolveSupplierBalance } from './supplierFinancialLedger.ts'
import { buildXlsxWorkbook, buildZipArchive, downloadXlsxFile, selectedLedgerSheets, supplierStatementSheet } from './supplierLedgerExport.ts'

const supplier = { id: 'supplier-1', supplierNumber: 'SUP-0001', country: { code: 'US' } }
const statementLabels = {
  supplier: 'Supplier', number: 'Number', currency: 'Currency', period: 'Period', totalBalance: 'Total balance',
  openingBalance: 'Opening', totalPayable: 'Payable total', totalPaid: 'Paid total', closingBalance: 'Closing',
  date: 'Date', projectCode: 'Project code', projectTitle: 'Project', type: 'Type', previousBalance: 'Previous',
  payable: 'Payable', paidToday: 'Paid today', balance: 'Balance', periodTotal: 'Period total',
}
const selectedLabels = {
  supplier: 'Supplier', supplierNumber: 'Number', date: 'Date', projectCode: 'Project code', projectTitle: 'Project',
  type: 'Type', currency: 'Currency', previousBalance: 'Previous', payable: 'Payable', paidToday: 'Paid today',
  balance: 'Balance', rowsCount: 'Rows', payableTotal: 'Payable', paidTotal: 'Paid', netTotal: 'Net',
}
const formatDate = (iso: string) => iso

test('statement export rows keep the balance formula and numeric Excel cells', () => {
  const balance = resolveSupplierBalance(supplier)
  const entries = buildSupplierLedger(supplier, balance, new Date('2026-10-14T12:00:00Z'))
  const sheet = supplierStatementSheet({
    sheetName: 'Statement', supplierName: 'Global Resin Co', supplierNumber: 'SUP-0001', currencyCode: 'USD',
    periodLabel: '2026-01-01 — 2026-10-14', totalBalance: balance.amount,
    openingBalance: entries[0].previousBalance,
    totalPayable: '1000.00', totalPaid: '400.00', closingBalance: entries[entries.length - 1].balance,
    entries, language: 'en', formatDate, kindLabel: kind => kind, labels: statementLabels,
  })
  const headerIndex = sheet.rows.findIndex(row => row[0] === 'Date')
  assert.ok(headerIndex > 0)
  const dataRows = sheet.rows.slice(headerIndex + 1, -1)
  assert.equal(dataRows.length, entries.length)
  for (const row of dataRows) {
    assert.equal(typeof row[4], 'number')
    assert.ok(Math.abs((row[4] as number) + (row[5] as number) - (row[6] as number) - (row[7] as number)) < 0.000001,
      'exported rows must satisfy previous + payable - paid = balance')
  }
  const totals = sheet.rows[sheet.rows.length - 1]
  assert.equal(totals[2], 'Period total')
  assert.equal(totals[5], 1000)
  assert.deepEqual(sheet.boldRows, [headerIndex, sheet.rows.length - 1])
})

test('selected rows export adds a per-currency summary sheet', () => {
  const items = [supplier, { id: 'supplier-2', supplierNumber: 'SUP-0002', country: { code: 'IR' } }]
    .map(item => ({ supplier: item, balance: resolveSupplierBalance(item) }))
  const rows = buildCombinedSupplierLedger(items)
  const sheets = selectedLedgerSheets({
    rowsSheetName: 'Selected entries', summarySheetName: 'Selection summary',
    rows, language: 'en', formatDate, kindLabel: kind => kind, labels: selectedLabels,
  })
  assert.equal(sheets.length, 2)
  assert.equal(sheets[0].rows.length, rows.length + 1)
  const currencies = [...new Set(rows.map(row => row.currencyCode))]
  assert.equal(sheets[1].rows.length, currencies.length + 1)
  for (const currency of currencies) {
    const currencyRows = rows.filter(row => row.currencyCode === currency)
    const summary = sheets[1].rows.find(row => row[0] === currency)!
    assert.equal(summary[1], currencyRows.length)
    const payable = currencyRows.reduce((total, row) => total + Number(row.payableAmount), 0)
    const paid = currencyRows.reduce((total, row) => total + Number(row.paidAmount), 0)
    assert.ok(Math.abs((summary[2] as number) - payable) < 0.000001)
    assert.ok(Math.abs((summary[4] as number) - (payable - paid)) < 0.000001, 'net = payable - paid')
  }
})

test('generated workbook is a real XLSX zip with readable sheet XML', () => {
  const balance = resolveSupplierBalance(supplier)
  const entries = buildSupplierLedger(supplier, balance, new Date('2026-10-14T12:00:00Z'))
  const bytes = buildXlsxWorkbook([supplierStatementSheet({
    sheetName: 'صورت‌حساب', supplierName: 'Hadron Plast', supplierNumber: 'SUP-0001', currencyCode: 'USD',
    periodLabel: '2026-01-01 — 2026-10-14', totalBalance: balance.amount, openingBalance: entries[0].previousBalance,
    totalPayable: '1', totalPaid: '0', closingBalance: balance.amount,
    entries: entries.slice(0, 2), language: 'fa-IR', formatDate, kindLabel: kind => kind, labels: statementLabels,
  })])
  assert.equal(String.fromCharCode(bytes[0], bytes[1]), 'PK')
  const text = new TextDecoder().decode(bytes)
  assert.ok(text.includes('[Content_Types].xml'))
  assert.ok(text.includes('xl/worksheets/sheet1.xml'))
  assert.ok(text.includes('صورت‌حساب'), 'sheet name must be stored as UTF-8')
  assert.ok(text.includes('Hadron Plast'))
  assert.ok(text.includes('PK\x05\x06'), 'zip end-of-central-directory record must be present')

  const zip = buildZipArchive([{ name: 'a.txt', data: new TextEncoder().encode('hello') }])
  assert.equal(String.fromCharCode(zip[0], zip[1]), 'PK')
  assert.ok(new TextDecoder().decode(zip).includes('hello'))
})

test('download helper safely no-ops outside a browser DOM', () => {
  assert.equal(downloadXlsxFile('test.xlsx', new Uint8Array([1, 2, 3])), false)
})
