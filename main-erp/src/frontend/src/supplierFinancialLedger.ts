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

export type SupplierLedgerKind = 'PAYABLE' | 'PAYMENT' | 'PAYABLE_AND_PAYMENT'

export type SupplierLedgerProject = {
  code: string
  titleFa: string
  titleEn: string
}

export type SupplierLedgerEntry = {
  id: string
  /** ISO calendar date (yyyy-mm-dd). */
  date: string
  kind: SupplierLedgerKind
  /** The project / deal (دادوستد) this entry belongs to. */
  project: SupplierLedgerProject
  /** Balance carried from the previous day, before this entry (تراز روز قبل). */
  previousBalance: string
  /** Amount that became payable for this project on this date (قابل پرداخت این پروژه). */
  payableAmount: string
  /** Amount paid on this date for this project (پرداختی امروز). */
  paidAmount: string
  /** payableAmount − paidAmount. */
  signedAmount: string
  /** Financial balance after this entry: previousBalance + payableAmount − paidAmount. */
  balance: string
}

export type SupplierLedgerPeriodSummary = {
  rows: SupplierLedgerEntry[]
  /** Balance before the period (sum of all entries before `from`). */
  openingBalance: string
  totalPayable: string
  totalPaid: string
  closingBalance: string
  count: number
}

const FAKE_PROJECT_TITLES: Array<{ titleFa: string; titleEn: string }> = [
  { titleFa: 'خرید مواد اولیه تولید', titleEn: 'Production raw-material purchase' },
  { titleFa: 'تأمین قطعات خط مونتاژ', titleEn: 'Assembly-line parts supply' },
  { titleFa: 'خرید بسته‌بندی و ملزومات مصرفی', titleEn: 'Packaging and consumables purchase' },
  { titleFa: 'واردات رزین و افزودنی‌ها', titleEn: 'Resin and additives import' },
  { titleFa: 'خرید ورق و پروفیل صنعتی', titleEn: 'Industrial sheet and profile purchase' },
  { titleFa: 'تأمین تجهیزات جانبی انبار', titleEn: 'Warehouse auxiliary equipment supply' },
  { titleFa: 'خرید مواد شیمیایی فرایندی', titleEn: 'Process chemicals purchase' },
  { titleFa: 'سفارش خرید قطعات یدکی', titleEn: 'Spare-parts purchase order' },
]

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
 * Builds a deterministic fake, project-based ledger for one supplier.
 *
 * The supplier's activity is a handful of projects / deals (دادوستد). Each
 * project first makes an amount payable and is then paid in installments.
 * Every row follows the balance formula exactly:
 *
 *   balance = previousBalance (تراز روز قبل) + payableAmount (قابل پرداخت این پروژه) − paidAmount (پرداختی امروز)
 *
 * The carried-in previous balance of the first row makes the final row's
 * balance equal the resolved total balance, so the statement always
 * reconciles with the number clicked in the list.
 */
export function buildSupplierLedger(
  supplier: SupplierBalanceSource,
  balance: Pick<ResolvedSupplierBalance, 'amount' | 'currencyCode' | 'decimalPrecision'>,
  today: Date = new Date(),
): SupplierLedgerEntry[] {
  const precision = balance.decimalPrecision
  const totalMinor = toMinor(balance.amount, precision)
  const random = mulberry32(hashSeed(`${supplier.id}|${supplier.supplierNumber}|project-ledger|${balance.amount}|${balance.currencyCode ?? ''}`))
  const scale = 10 ** precision
  // Fallback activity scale for (near-)zero balances so the statement is not empty.
  const activityBaseMinor = Math.max(Math.abs(totalMinor), (precision === 0 ? 950_000_000 : 28_000) * scale)
  const overpaid = totalMinor < 0

  type DraftEvent = { date: string; project: SupplierLedgerProject; payableMinor: number; paidMinor: number }
  const events: DraftEvent[] = []
  const projectCount = 3 + Math.floor(random() * 3)
  const titleOffset = Math.floor(random() * FAKE_PROJECT_TITLES.length)
  let projectStart = addCalendarDays(toIsoDate(today), -175)

  for (let projectIndex = 0; projectIndex < projectCount; projectIndex++) {
    if (projectIndex > 0) projectStart = addCalendarDays(projectStart, 4 + Math.floor(random() * 18))
    const title = FAKE_PROJECT_TITLES[(titleOffset + projectIndex) % FAKE_PROJECT_TITLES.length]
    const project: SupplierLedgerProject = {
      code: `PRJ-${1040 + projectIndex * 137 + Math.floor(random() * 89)}`,
      titleFa: title.titleFa,
      titleEn: title.titleEn,
    }
    const payableMinor = Math.max(1, Math.round((activityBaseMinor * 1.35 / projectCount) * (0.7 + random() * 0.6)))
    // Suppliers in credit (negative balance) have paid more than was payable.
    const paidRatio = overpaid ? 1.08 + random() * 0.37 : 0.35 + random() * 0.5
    const totalPaidMinor = Math.round(payableMinor * paidRatio)
    const paymentCount = 1 + Math.floor(random() * 3)
    const chunks = splitMinor(totalPaidMinor, paymentCount, random)
    let paymentDate = random() < 0.5 ? projectStart : addCalendarDays(projectStart, 3 + Math.floor(random() * 10))
    let paidOnStartMinor = 0
    const paymentEvents: DraftEvent[] = []
    chunks.forEach((chunkMinor, chunkIndex) => {
      if (chunkIndex > 0) paymentDate = addCalendarDays(paymentDate, 10 + Math.floor(random() * 19))
      if (paymentDate === projectStart) paidOnStartMinor += chunkMinor
      else if (chunkMinor > 0) paymentEvents.push({ date: paymentDate, project, payableMinor: 0, paidMinor: chunkMinor })
    })
    events.push({ date: projectStart, project, payableMinor, paidMinor: paidOnStartMinor })
    events.push(...paymentEvents)
  }

  events.sort((left, right) => left.date.localeCompare(right.date) || left.project.code.localeCompare(right.project.code) || right.payableMinor - left.payableMinor)
  const netMinor = events.reduce((total, event) => total + event.payableMinor - event.paidMinor, 0)
  // تراز روز قبل carried into the first visible row (balance from before this period).
  let previousMinor = totalMinor - netMinor
  return events.map((event, index) => {
    const balanceMinor = previousMinor + event.payableMinor - event.paidMinor
    const entry: SupplierLedgerEntry = {
      id: `${supplier.id}-ledger-${index + 1}`,
      date: event.date,
      kind: event.payableMinor > 0 && event.paidMinor > 0 ? 'PAYABLE_AND_PAYMENT' : event.payableMinor > 0 ? 'PAYABLE' : 'PAYMENT',
      project: event.project,
      previousBalance: minorToAmountString(previousMinor, precision),
      payableAmount: minorToAmountString(event.payableMinor, precision),
      paidAmount: minorToAmountString(event.paidMinor, precision),
      signedAmount: minorToAmountString(event.payableMinor - event.paidMinor, precision),
      balance: minorToAmountString(balanceMinor, precision),
    }
    previousMinor = balanceMinor
    return entry
  })
}

/** Splits an amount into `parts` positive-ish chunks that sum back exactly. */
function splitMinor(totalMinor: number, parts: number, random: () => number) {
  if (parts <= 1) return [totalMinor]
  const weights = Array.from({ length: parts }, () => 0.5 + random())
  const weightSum = weights.reduce((total, weight) => total + weight, 0)
  const chunks = weights.map(weight => Math.floor((totalMinor * weight) / weightSum))
  chunks[parts - 1] += totalMinor - chunks.reduce((total, chunk) => total + chunk, 0)
  return chunks
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
 * Filters the ledger to [from, to] and summarises the period with the same
 * formula: closing = opening (تراز قبل از دوره) + payable − paid.
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
  // The previous-day balance carried into the first included row is the
  // opening balance; a range past the last row opens at the final balance.
  const firstIncluded = entries.findIndex(entry => (!from || entry.date >= from) && (!to || entry.date <= to))
  const openingMinor = firstIncluded === -1
    ? (entries.length && from && from > entries[entries.length - 1].date
      ? toMinor(entries[entries.length - 1].balance, decimalPrecision)
      : 0)
    : toMinor(entries[firstIncluded].previousBalance, decimalPrecision)
  const payableMinor = sumMinor(rows, entry => entry.payableAmount)
  const paidMinor = sumMinor(rows, entry => entry.paidAmount)
  return {
    rows,
    openingBalance: minorToAmountString(openingMinor, decimalPrecision),
    totalPayable: minorToAmountString(payableMinor, decimalPrecision),
    totalPaid: minorToAmountString(paidMinor, decimalPrecision),
    closingBalance: minorToAmountString(openingMinor + payableMinor - paidMinor, decimalPrecision),
    count: rows.length,
  }
}

export function addCalendarDays(isoDate: string, days: number) {
  const date = new Date(`${isoDate}T12:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
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
