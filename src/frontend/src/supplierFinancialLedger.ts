export type SupplierBalanceCurrencyLike = {
  id: string
  code: string
  decimalPrecision?: number
}

export type SupplierBalanceSource = {
  id: string
  supplierNumber: string
  countryCode?: string | null
  country?: { code?: string | null } | null
  financialBalanceAmount?: string | number | null
  financialBalanceCurrencyId?: string | null
  financialBalanceCurrency?: SupplierBalanceCurrencyLike | null
  financialBalanceCurrencyCode?: string | null
  financialBalanceCurrencyPrecision?: number | null
}

export type ResolvedSupplierBalance = {
  /** Exact decimal string in major currency units, e.g. "1234567.89". */
  amount: string
  currencyCode: string | null
  decimalPrecision: number
  /** True when the amount is generated demo/fake data instead of an API value. */
  isFake: boolean
  /** True when the API sent an amount whose currency has not been resolved yet. */
  isPending: boolean
}

export type SupplierLedgerKind =
  | 'OPENING'
  | 'PURCHASE'
  | 'ADVANCE_PAYMENT'
  | 'BANK_PAYMENT'
  | 'FREIGHT'
  | 'PURCHASE_RETURN'
  | 'ADJUSTMENT'

export type SupplierLedgerEntry = {
  id: string
  /** ISO calendar date (yyyy-mm-dd). */
  date: string
  reference: string
  kind: SupplierLedgerKind
  /** Positive exact-decimal string when the entry is a debit, otherwise "0…". */
  debit: string
  /** Positive exact-decimal string when the entry is a credit, otherwise "0…". */
  credit: string
  /** Signed amount: credit − debit. */
  signedAmount: string
  /** Cumulative signed balance after this entry (credit − debit from the start). */
  runningBalance: string
}

export type SupplierLedgerPeriodSummary = {
  rows: SupplierLedgerEntry[]
  openingBalance: string
  totalDebit: string
  totalCredit: string
  closingBalance: string
  count: number
}

const FAKE_CURRENCY_BY_COUNTRY: Record<string, { code: string; decimalPrecision: number }> = {
  IR: { code: 'IRR', decimalPrecision: 0 },
  CN: { code: 'CNY', decimalPrecision: 2 },
  AE: { code: 'AED', decimalPrecision: 2 },
  TR: { code: 'TRY', decimalPrecision: 2 },
  GB: { code: 'GBP', decimalPrecision: 2 },
  IN: { code: 'INR', decimalPrecision: 2 },
  DE: { code: 'EUR', decimalPrecision: 2 },
  FR: { code: 'EUR', decimalPrecision: 2 },
  IT: { code: 'EUR', decimalPrecision: 2 },
  ES: { code: 'EUR', decimalPrecision: 2 },
  NL: { code: 'EUR', decimalPrecision: 2 },
  BE: { code: 'EUR', decimalPrecision: 2 },
  AT: { code: 'EUR', decimalPrecision: 2 },
}

/**
 * Resolves the balance shown for a supplier in the list.
 *
 * Real API balances always win. Only when the API provides no balance do we
 * fall back to a deterministic fake amount (same supplier → same amount on
 * every render/machine) so the UI can be reviewed end-to-end.
 */
export function resolveSupplierBalance(
  supplier: SupplierBalanceSource,
  currencies: SupplierBalanceCurrencyLike[] = [],
): ResolvedSupplierBalance {
  const rawAmount = supplier.financialBalanceAmount
  if (rawAmount != null && String(rawAmount).trim() !== '') {
    const currency = supplier.financialBalanceCurrency
      ?? currencies.find(value => value.id === supplier.financialBalanceCurrencyId)
    const currencyCode = currency?.code ?? supplier.financialBalanceCurrencyCode ?? null
    if (supplier.financialBalanceCurrencyId && !currencyCode) {
      return { amount: String(rawAmount), currencyCode: null, decimalPrecision: 2, isFake: false, isPending: true }
    }
    return {
      amount: String(rawAmount),
      currencyCode,
      decimalPrecision: currency?.decimalPrecision ?? supplier.financialBalanceCurrencyPrecision ?? 2,
      isFake: false,
      isPending: false,
    }
  }
  const fake = fakeBalanceForSupplier(supplier)
  return { ...fake, isFake: true, isPending: false }
}

/** Deterministic fake total for one supplier (stable across renders). */
export function fakeBalanceForSupplier(supplier: SupplierBalanceSource) {
  const countryCode = (supplier.countryCode ?? supplier.country?.code ?? '').trim().toUpperCase()
  const currency = FAKE_CURRENCY_BY_COUNTRY[countryCode] ?? { code: 'USD', decimalPrecision: 2 }
  const random = mulberry32(hashSeed(`${supplier.id}|${supplier.supplierNumber}|fake-balance`))
  // Roughly one supplier in five is in credit (advances exceed invoices).
  const sign = random() < 0.22 ? -1 : 1
  const magnitude = currency.decimalPrecision === 0
    ? Math.round((480_000_000 + random() * 26_500_000_000) / 1_000_000) * 1_000_000
    : Math.round((1_250 + random() * 412_000) * 100) / 100
  return {
    amount: minorToAmountString(sign * toMinor(magnitude, currency.decimalPrecision), currency.decimalPrecision),
    currencyCode: currency.code as string | null,
    decimalPrecision: currency.decimalPrecision,
  }
}

/**
 * Builds a deterministic fake ledger whose entries sum exactly to the
 * resolved total balance, so the statement always reconciles with the
 * number clicked in the list (آخرین «مانده» = تراز مالی مجموع).
 */
export function buildSupplierLedger(
  supplier: SupplierBalanceSource,
  balance: Pick<ResolvedSupplierBalance, 'amount' | 'currencyCode' | 'decimalPrecision'>,
  today: Date = new Date(),
): SupplierLedgerEntry[] {
  const precision = balance.decimalPrecision
  const totalMinor = toMinor(balance.amount, precision)
  const random = mulberry32(hashSeed(`${supplier.id}|${supplier.supplierNumber}|ledger|${balance.amount}|${balance.currencyCode ?? ''}`))
  const scale = 10 ** precision
  // Fallback activity scale for (near-)zero balances so the statement is not empty.
  const activityBaseMinor = Math.max(Math.abs(totalMinor), (precision === 0 ? 950_000_000 : 28_000) * scale)

  type Draft = { date: string; kind: SupplierLedgerKind; signedMinor: number }
  const drafts: Draft[] = []
  let cursor = addCalendarDays(toIsoDate(today), -186)
  let runningMinor = 0

  const openingMinor = Math.round(totalMinor * (0.26 + random() * 0.22))
  if (openingMinor !== 0) {
    drafts.push({ date: cursor, kind: 'OPENING', signedMinor: openingMinor })
    runningMinor += openingMinor
  }

  const intermediateCount = 8 + Math.floor(random() * 4)
  for (let index = 0; index < intermediateCount; index++) {
    cursor = addCalendarDays(cursor, 9 + Math.floor(random() * 11))
    const roll = random()
    const kind: SupplierLedgerKind = roll < 0.46 ? 'PURCHASE' : roll < 0.76 ? 'BANK_PAYMENT' : roll < 0.86 ? 'ADVANCE_PAYMENT' : roll < 0.94 ? 'FREIGHT' : 'PURCHASE_RETURN'
    const isSmall = kind === 'FREIGHT' || kind === 'PURCHASE_RETURN'
    const magnitude = Math.max(1, Math.round(activityBaseMinor * (isSmall ? 0.02 + random() * 0.05 : 0.07 + random() * 0.27)))
    const signedMinor = (kind === 'PURCHASE' || kind === 'FREIGHT' ? 1 : -1) * magnitude
    drafts.push({ date: cursor, kind, signedMinor })
    runningMinor += signedMinor
  }

  // Final entry reconciles the statement exactly to the displayed total.
  const differenceMinor = totalMinor - runningMinor
  if (differenceMinor !== 0) {
    cursor = addCalendarDays(cursor, 6 + Math.floor(random() * 9))
    const kind: SupplierLedgerKind = Math.abs(differenceMinor) <= Math.abs(totalMinor) * 0.03
      ? 'ADJUSTMENT'
      : differenceMinor > 0 ? 'PURCHASE' : 'BANK_PAYMENT'
    drafts.push({ date: cursor, kind, signedMinor: differenceMinor })
  }

  let cumulativeMinor = 0
  return drafts.map((draft, index) => {
    cumulativeMinor += draft.signedMinor
    return {
      id: `${supplier.id}-ledger-${index + 1}`,
      date: draft.date,
      reference: `${referencePrefix(draft.kind)}-${4100 + index * 113 + Math.floor(random() * 89)}`,
      kind: draft.kind,
      debit: minorToAmountString(draft.signedMinor < 0 ? -draft.signedMinor : 0, precision),
      credit: minorToAmountString(draft.signedMinor > 0 ? draft.signedMinor : 0, precision),
      signedAmount: minorToAmountString(draft.signedMinor, precision),
      runningBalance: minorToAmountString(cumulativeMinor, precision),
    }
  })
}

export type SupplierWithBalance = {
  supplier: SupplierBalanceSource & { name: string }
  balance: ResolvedSupplierBalance
}

export type CombinedSupplierLedgerRow = SupplierLedgerEntry & {
  supplierId: string
  supplierNumber: string
  supplierName: string
  currencyCode: string | null
  decimalPrecision: number
  isFakeBalance: boolean
}

export type CurrencyBalanceTotal = {
  currencyCode: string | null
  decimalPrecision: number
  totalAmount: string
  supplierCount: number
  fakeCount: number
}

/**
 * Merges every supplier's statement into one big ledger. Each row keeps its
 * own supplier's running balance, so rows stay reconcilable per supplier
 * even when the merged table is sorted or filtered globally.
 */
export function buildCombinedSupplierLedger(items: SupplierWithBalance[]): CombinedSupplierLedgerRow[] {
  return items
    .flatMap(({ supplier, balance }) => buildSupplierLedger(supplier, balance).map(entry => ({
      ...entry,
      supplierId: supplier.id,
      supplierNumber: supplier.supplierNumber,
      supplierName: supplier.name,
      currencyCode: balance.currencyCode,
      decimalPrecision: balance.decimalPrecision,
      isFakeBalance: balance.isFake,
    })))
    .sort((left, right) => left.date.localeCompare(right.date) || left.supplierNumber.localeCompare(right.supplierNumber) || left.id.localeCompare(right.id))
}

/** Totals the overall balance per currency; currencies are never mixed. */
export function totalBalancesByCurrency(items: SupplierWithBalance[]): CurrencyBalanceTotal[] {
  const groups = new Map<string, { currencyCode: string | null; decimalPrecision: number; minor: number; supplierCount: number; fakeCount: number }>()
  for (const { balance } of items) {
    const key = balance.currencyCode ?? ''
    const group = groups.get(key) ?? { currencyCode: balance.currencyCode, decimalPrecision: balance.decimalPrecision, minor: 0, supplierCount: 0, fakeCount: 0 }
    group.minor += toMinor(balance.amount, balance.decimalPrecision)
    group.supplierCount += 1
    if (balance.isFake) group.fakeCount += 1
    groups.set(key, group)
  }
  return [...groups.values()]
    .sort((left, right) => (left.currencyCode ?? '').localeCompare(right.currencyCode ?? ''))
    .map(group => ({
      currencyCode: group.currencyCode,
      decimalPrecision: group.decimalPrecision,
      totalAmount: minorToAmountString(group.minor, group.decimalPrecision),
      supplierCount: group.supplierCount,
      fakeCount: group.fakeCount,
    }))
}

export function supplierLedgerDateRange(entries: SupplierLedgerEntry[]) {
  if (!entries.length) return { from: '', to: '' }
  return { from: entries[0].date, to: entries[entries.length - 1].date }
}

/**
 * Filters the ledger to [from, to] and summarises the period. The running
 * balance stored on each row is global, so the closing balance of any period
 * is opening + (credit − debit) inside that period.
 */
export function summarizeSupplierLedger(
  entries: SupplierLedgerEntry[],
  from: string,
  to: string,
  decimalPrecision: number,
): SupplierLedgerPeriodSummary {
  const rows = entries.filter(entry => (!from || entry.date >= from) && (!to || entry.date <= to))
  const sumMinor = (values: SupplierLedgerEntry[], pick: (entry: SupplierLedgerEntry) => string) =>
    values.reduce((total, entry) => total + toMinor(pick(entry), decimalPrecision), 0)
  const openingMinor = sumMinor(entries.filter(entry => from && entry.date < from), entry => entry.signedAmount)
  const debitMinor = sumMinor(rows, entry => entry.debit)
  const creditMinor = sumMinor(rows, entry => entry.credit)
  return {
    rows,
    openingBalance: minorToAmountString(openingMinor, decimalPrecision),
    totalDebit: minorToAmountString(debitMinor, decimalPrecision),
    totalCredit: minorToAmountString(creditMinor, decimalPrecision),
    closingBalance: minorToAmountString(openingMinor + creditMinor - debitMinor, decimalPrecision),
    count: rows.length,
  }
}

export function addCalendarDays(isoDate: string, days: number) {
  const date = new Date(`${isoDate}T12:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

function referencePrefix(kind: SupplierLedgerKind) {
  switch (kind) {
    case 'OPENING': return 'BAL'
    case 'PURCHASE': return 'INV'
    case 'ADVANCE_PAYMENT': return 'ADV'
    case 'BANK_PAYMENT': return 'PAY'
    case 'FREIGHT': return 'FRG'
    case 'PURCHASE_RETURN': return 'RET'
    case 'ADJUSTMENT': return 'ADJ'
  }
}

function toIsoDate(date: Date) {
  return date.toISOString().slice(0, 10)
}

function toMinor(amount: string | number, precision: number) {
  const value = typeof amount === 'number' ? amount : Number.parseFloat(amount)
  if (!Number.isFinite(value)) return 0
  return Math.round(value * 10 ** precision)
}

function minorToAmountString(minor: number, precision: number) {
  const scale = 10 ** precision
  const sign = minor < 0 ? '-' : ''
  const absolute = Math.abs(Math.round(minor))
  const whole = Math.floor(absolute / scale)
  if (precision === 0) return `${sign}${whole}`
  const fraction = String(absolute % scale).padStart(precision, '0')
  return `${sign}${whole}.${fraction}`
}

/** FNV-1a hash → stable seed per supplier. */
function hashSeed(value: string) {
  let hash = 0x811c9dc5
  for (let index = 0; index < value.length; index++) {
    hash ^= value.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193)
  }
  return hash >>> 0
}

/** Small deterministic PRNG; same seed always yields the same sequence. */
function mulberry32(seed: number) {
  let state = seed
  return () => {
    state = (state + 0x6d2b79f5) | 0
    let value = Math.imul(state ^ (state >>> 15), 1 | state)
    value = (value + Math.imul(value ^ (value >>> 7), 61 | value)) ^ value
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296
  }
}
