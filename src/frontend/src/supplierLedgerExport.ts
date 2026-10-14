import type {
  CombinedSupplierLedgerRow,
  SupplierLedgerEntry,
  SupplierLedgerKind,
} from './supplierFinancialLedger'

/**
 * Excel (.xlsx) export for supplier financial statements, built entirely on
 * the client because the ledger rows are generated in the browser. The
 * workbook writer below is a minimal, dependency-free XLSX (ZIP-STORE)
 * builder — the same hand-rolled approach the backend export service uses —
 * with real numeric cells so amounts can be summed inside Excel.
 */

export type XlsxCell = string | number | null

export type XlsxSheet = {
  name: string
  rows: XlsxCell[][]
  /** Zero-based row indexes rendered bold (headers, totals). */
  boldRows?: number[]
  columnWidths?: number[]
}

const amountNumber = (value: string) => {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : 0
}
const projectTitle = (entry: SupplierLedgerEntry, language: string) =>
  language.toLowerCase().startsWith('en') ? entry.project.titleEn : entry.project.titleFa

export type StatementExportLabels = {
  supplier: string
  number: string
  currency: string
  period: string
  totalBalance: string
  openingBalance: string
  totalPayable: string
  totalPaid: string
  closingBalance: string
  date: string
  projectCode: string
  projectTitle: string
  type: string
  previousBalance: string
  payable: string
  paidToday: string
  balance: string
  periodTotal: string
}

/** One supplier's statement (the rows currently shown in the drawer). */
export function supplierStatementSheet(input: {
  sheetName: string
  supplierName: string
  supplierNumber: string
  currencyCode: string | null
  periodLabel: string
  totalBalance: string
  openingBalance: string
  totalPayable: string
  totalPaid: string
  closingBalance: string
  entries: SupplierLedgerEntry[]
  language: string
  formatDate: (isoDate: string) => string
  kindLabel: (kind: SupplierLedgerKind) => string
  labels: StatementExportLabels
}): XlsxSheet {
  const { labels } = input
  const headerRowIndex = 7
  const rows: XlsxCell[][] = [
    [labels.supplier, input.supplierName],
    [labels.number, input.supplierNumber],
    [labels.currency, input.currencyCode ?? '—'],
    [labels.period, input.periodLabel],
    [labels.totalBalance, amountNumber(input.totalBalance)],
    [labels.openingBalance, amountNumber(input.openingBalance)],
    [labels.closingBalance, amountNumber(input.closingBalance)],
    [labels.date, labels.projectCode, labels.projectTitle, labels.type, labels.previousBalance, labels.payable, labels.paidToday, labels.balance],
    ...input.entries.map((entry): XlsxCell[] => [
      input.formatDate(entry.date),
      entry.project.code,
      projectTitle(entry, input.language),
      input.kindLabel(entry.kind),
      amountNumber(entry.previousBalance),
      amountNumber(entry.payableAmount),
      amountNumber(entry.paidAmount),
      amountNumber(entry.balance),
    ]),
    [null, null, labels.periodTotal, null, null, amountNumber(input.totalPayable), amountNumber(input.totalPaid), amountNumber(input.closingBalance)],
  ]
  return {
    name: input.sheetName,
    rows,
    boldRows: [headerRowIndex, rows.length - 1],
    columnWidths: [16, 16, 36, 20, 18, 18, 18, 18],
  }
}

export type SelectedExportLabels = {
  supplier: string
  supplierNumber: string
  date: string
  projectCode: string
  projectTitle: string
  type: string
  currency: string
  previousBalance: string
  payable: string
  paidToday: string
  balance: string
  rowsCount: string
  payableTotal: string
  paidTotal: string
  netTotal: string
}

/** The ticked rows of the overall ledger, plus a per-currency summary sheet. */
export function selectedLedgerSheets(input: {
  rowsSheetName: string
  summarySheetName: string
  rows: CombinedSupplierLedgerRow[]
  language: string
  formatDate: (isoDate: string) => string
  kindLabel: (kind: SupplierLedgerKind) => string
  labels: SelectedExportLabels
}): XlsxSheet[] {
  const { labels } = input
  const entryRows: XlsxCell[][] = [
    [labels.supplier, labels.supplierNumber, labels.date, labels.projectCode, labels.projectTitle, labels.type, labels.currency, labels.previousBalance, labels.payable, labels.paidToday, labels.balance],
    ...input.rows.map((row): XlsxCell[] => [
      row.supplierName,
      row.supplierNumber,
      input.formatDate(row.date),
      row.project.code,
      projectTitle(row, input.language),
      input.kindLabel(row.kind),
      row.currencyCode ?? '—',
      amountNumber(row.previousBalance),
      amountNumber(row.payableAmount),
      amountNumber(row.paidAmount),
      amountNumber(row.balance),
    ]),
  ]

  const byCurrency = new Map<string, { currencyCode: string | null; count: number; payable: number; paid: number }>()
  for (const row of input.rows) {
    const key = `${row.currencyCode ?? ''}|${row.decimalPrecision}`
    const group = byCurrency.get(key) ?? { currencyCode: row.currencyCode, count: 0, payable: 0, paid: 0 }
    group.count += 1
    group.payable += amountNumber(row.payableAmount)
    group.paid += amountNumber(row.paidAmount)
    byCurrency.set(key, group)
  }
  const summaryRows: XlsxCell[][] = [
    [labels.currency, labels.rowsCount, labels.payableTotal, labels.paidTotal, labels.netTotal],
    ...[...byCurrency.values()]
      .sort((left, right) => (left.currencyCode ?? '').localeCompare(right.currencyCode ?? ''))
      .map((group): XlsxCell[] => [group.currencyCode ?? '—', group.count, group.payable, group.paid, group.payable - group.paid]),
  ]

  return [
    { name: input.rowsSheetName, rows: entryRows, boldRows: [0], columnWidths: [24, 16, 14, 14, 32, 20, 12, 18, 18, 18, 18] },
    { name: input.summarySheetName, rows: summaryRows, boldRows: [0], columnWidths: [14, 14, 20, 20, 20] },
  ]
}

/* ------------------------------------------------------------------ */
/* Minimal XLSX (ZIP-STORE) writer                                     */
/* ------------------------------------------------------------------ */

const textEncoder = new TextEncoder()
const escapeXml = (value: string) => value
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
const sanitizeSheetName = (name: string) => name.replace(/[\\/?*[\]:]/g, '-').slice(0, 31).trim() || 'Sheet'

function columnLetter(index: number) {
  let letters = ''
  let value = index + 1
  while (value > 0) {
    const remainder = (value - 1) % 26
    letters = String.fromCharCode(65 + remainder) + letters
    value = Math.floor((value - 1) / 26)
  }
  return letters
}

function sheetXml(sheet: XlsxSheet) {
  const bold = new Set(sheet.boldRows ?? [])
  const cols = sheet.columnWidths?.length
    ? `<cols>${sheet.columnWidths.map((width, index) => `<col min="${index + 1}" max="${index + 1}" width="${width}" customWidth="1"/>`).join('')}</cols>`
    : ''
  const body = sheet.rows.map((row, rowIndex) => {
    const cells = row.map((cell, columnIndex) => {
      if (cell === null || cell === '') return ''
      const ref = `${columnLetter(columnIndex)}${rowIndex + 1}`
      const style = bold.has(rowIndex) ? ' s="1"' : ''
      if (typeof cell === 'number') return `<c r="${ref}"${style}><v>${cell}</v></c>`
      return `<c r="${ref}"${style} t="inlineStr"><is><t xml:space="preserve">${escapeXml(cell)}</t></is></c>`
    }).join('')
    return `<row r="${rowIndex + 1}">${cells}</row>`
  }).join('')
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">${cols}<sheetData>${body}</sheetData></worksheet>`
}

const STYLES_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs></styleSheet>`

export function buildXlsxWorkbook(sheets: XlsxSheet[]): Uint8Array {
  const named = sheets.map(sheet => ({ ...sheet, name: sanitizeSheetName(sheet.name) }))
  const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>${named.map((_, index) => `<Override PartName="/xl/worksheets/sheet${index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join('')}<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>`
  const rootRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`
  const workbook = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>${named.map((sheet, index) => `<sheet name="${escapeXml(sheet.name)}" sheetId="${index + 1}" r:id="rId${index + 1}"/>`).join('')}</sheets></workbook>`
  const workbookRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">${named.map((_, index) => `<Relationship Id="rId${index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet${index + 1}.xml"/>`).join('')}<Relationship Id="rId${named.length + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>`

  return buildZipArchive([
    { name: '[Content_Types].xml', data: textEncoder.encode(contentTypes) },
    { name: '_rels/.rels', data: textEncoder.encode(rootRels) },
    { name: 'xl/workbook.xml', data: textEncoder.encode(workbook) },
    { name: 'xl/_rels/workbook.xml.rels', data: textEncoder.encode(workbookRels) },
    { name: 'xl/styles.xml', data: textEncoder.encode(STYLES_XML) },
    ...named.map((sheet, index) => ({ name: `xl/worksheets/sheet${index + 1}.xml`, data: textEncoder.encode(sheetXml(sheet)) })),
  ])
}

let crcTable: Uint32Array | undefined
function crc32(bytes: Uint8Array): number {
  if (!crcTable) {
    crcTable = new Uint32Array(256)
    for (let index = 0; index < 256; index++) {
      let value = index
      for (let bit = 0; bit < 8; bit++) value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1
      crcTable[index] = value >>> 0
    }
  }
  let crc = 0xffffffff
  for (const byte of bytes) crc = (crc >>> 8) ^ crcTable[(crc ^ byte) & 0xff]
  return (crc ^ 0xffffffff) >>> 0
}

function dosDateTime(date: Date) {
  const time = ((date.getHours() & 0x1f) << 11) | ((date.getMinutes() & 0x3f) << 5) | ((date.getSeconds() >> 1) & 0x1f)
  const day = (((date.getFullYear() - 1980) & 0x7f) << 9) | (((date.getMonth() + 1) & 0xf) << 5) | (date.getDate() & 0x1f)
  return { time, day }
}

/** ZIP archive with STORE (no compression) — enough for a generated XLSX. */
export function buildZipArchive(entries: Array<{ name: string; data: Uint8Array }>): Uint8Array {
  const { time, day } = dosDateTime(new Date())
  const parts: Uint8Array[] = []
  const central: Uint8Array[] = []
  let offset = 0
  const push = (part: Uint8Array) => { parts.push(part); offset += part.length }

  for (const entry of entries) {
    const nameBytes = textEncoder.encode(entry.name)
    const crc = crc32(entry.data)
    const local = new DataView(new ArrayBuffer(30))
    local.setUint32(0, 0x04034b50, true)
    local.setUint16(4, 20, true)
    local.setUint16(6, 0x0800, true) // UTF-8 file names
    local.setUint16(8, 0, true) // STORE
    local.setUint16(10, time, true)
    local.setUint16(12, day, true)
    local.setUint32(14, crc, true)
    local.setUint32(18, entry.data.length, true)
    local.setUint32(22, entry.data.length, true)
    local.setUint16(26, nameBytes.length, true)
    const localOffset = offset
    push(new Uint8Array(local.buffer))
    push(nameBytes)
    push(entry.data)

    const header = new DataView(new ArrayBuffer(46))
    header.setUint32(0, 0x02014b50, true)
    header.setUint16(4, 20, true)
    header.setUint16(6, 20, true)
    header.setUint16(8, 0x0800, true)
    header.setUint16(10, 0, true)
    header.setUint16(12, time, true)
    header.setUint16(14, day, true)
    header.setUint32(16, crc, true)
    header.setUint32(20, entry.data.length, true)
    header.setUint32(24, entry.data.length, true)
    header.setUint16(28, nameBytes.length, true)
    header.setUint32(42, localOffset, true)
    central.push(new Uint8Array(header.buffer), nameBytes)
  }

  const centralSize = central.reduce((total, part) => total + part.length, 0)
  const end = new DataView(new ArrayBuffer(22))
  end.setUint32(0, 0x06054b50, true)
  end.setUint16(8, entries.length, true)
  end.setUint16(10, entries.length, true)
  end.setUint32(12, centralSize, true)
  end.setUint32(16, offset, true)
  parts.push(...central, new Uint8Array(end.buffer))

  const total = parts.reduce((sum, part) => sum + part.length, 0)
  const archive = new Uint8Array(total)
  let cursor = 0
  for (const part of parts) { archive.set(part, cursor); cursor += part.length }
  return archive
}

/** Triggers a browser download of the generated workbook. Returns false outside a DOM. */
export function downloadXlsxFile(filename: string, bytes: Uint8Array): boolean {
  if (typeof document === 'undefined' || typeof document.createElement !== 'function') return false
  if (typeof URL === 'undefined' || typeof URL.createObjectURL !== 'function') return false
  const blob = new Blob([bytes as unknown as BlobPart], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  document.body?.appendChild(anchor)
  anchor.click()
  anchor.remove()
  if (typeof setTimeout === 'function') setTimeout(() => URL.revokeObjectURL(url), 1000)
  return true
}
