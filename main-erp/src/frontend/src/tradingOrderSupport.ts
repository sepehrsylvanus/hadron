export type OrderProject = { id: string; projectNumber: string; titleFa: string; titleEn?: string; status: string }
export type OrderCurrency = { id: string; code: string; nameFa: string; nameEn?: string }
export type ManagedOrder = { id: string; projectId: string; registrationNumber: string; registrationDate: string; registeredAmount: string; currencyId: string; currencyCode: string; currencyNameFa: string; currencyNameEn?: string; status: string; notes?: string; version: number }
export type OrderValues = { registrationNumber: string; registrationDate: string; registeredAmount: string; currencyId: string; notes: string; reason: string }
export type OrderPage<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
export const emptyOrder: OrderValues = { registrationNumber: '', registrationDate: '', registeredAmount: '', currencyId: '', notes: '', reason: '' }
export const orderStatuses = ['DRAFT', 'SUBMITTED', 'ACTIVE', 'SUSPENDED', 'CANCELLED'] as const

/** numeric(24,6), never parseFloat/Number: no rounding or scientific notation. */
export function normalizeOrderAmount(input: string): string | null {
  const ascii = input.trim().replace(/[۰-۹٠-٩]/g, c => String(c.charCodeAt(0) - (c >= '۰' ? 1776 : 1632)))
    .replace(/٬/g, ',').replace(/٫/g, '.')
  if (!/^(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d{1,6})?$/.test(ascii)) return null
  const [whole, fraction = ''] = ascii.replace(/,/g, '').split('.')
  const integer = whole.replace(/^0+(?=\d)/, '')
  if (integer.length > 18 || BigInt(integer + fraction.padEnd(6, '0')) <= 0n) return null
  const trimmed = fraction.replace(/0+$/, '')
  return integer + (trimmed ? '.' + trimmed : '')
}

export function formatOrderAmount(value: string, language: string): string {
  const normalized = normalizeOrderAmount(value)
  if (!normalized) return '—'
  const [whole, fraction] = normalized.split('.')
  const fa = language.startsWith('fa')
  return new Intl.NumberFormat(fa ? 'fa-IR' : 'en-US').format(BigInt(whole)) + (!fraction ? '' :
    (fa ? '٫' + fraction.replace(/\d/g, d => String.fromCharCode(1776 + Number(d))) : '.' + fraction))
}

/** Explicit creation/update allowlists. Project association and lifecycle cannot be edited here. */
export function orderPayload(values: OrderValues, projectId: string, date: string, editing?: ManagedOrder) {
  const amount = normalizeOrderAmount(values.registeredAmount)
  if (!amount) throw new Error('INVALID_ORDER_AMOUNT')
  const details = { registrationNumber: values.registrationNumber.trim(), registrationDate: date,
    registeredAmount: amount, currencyId: values.currencyId, notes: values.notes.trim() || null }
  return editing ? { ...details, expectedVersion: editing.version, reason: values.reason.trim() }
    : { ...details, projectId, status: 'DRAFT' }
}
