export type ForecastDraft = {
  productId: string
  forecastQty: string
  expectedVersion: number | null
  isRevision: boolean
}

export function normalizePeriodCode(value: string) {
  const code = value.trim()
  if (!/^\d{6}$/.test(code)) return null
  const year = Number(code.slice(0, 4))
  const month = Number(code.slice(4, 6))
  return year > 0 && month >= 1 && month <= 12 ? code : null
}

export function normalizeJalaliMonthDate(value: string) {
  const code = value.trim()
  if (!/^\d{8}$/.test(code)) return null
  const year = Number(code.slice(0, 4))
  const month = Number(code.slice(4, 6))
  const day = Number(code.slice(6, 8))
  return year > 0 && month >= 1 && month <= 12 && day === 1 ? code : null
}

export function monthCodesBetween(startValue: string, endValue: string) {
  const start = normalizeJalaliMonthDate(startValue)
  const end = normalizeJalaliMonthDate(endValue)
  if (!start || !end) return []
  let year = Number(start.slice(0, 4))
  let month = Number(start.slice(4, 6))
  const endKey = Number(end.slice(0, 6))
  if (year * 100 + month > endKey) return []
  const result: string[] = []
  while (year * 100 + month <= endKey && result.length < 120) {
    result.push(`${String(year).padStart(4, '0')}${String(month).padStart(2, '0')}`)
    if (month === 12) { year += 1; month = 1 } else month += 1
  }
  return result
}

export const forecastMatrixKey = (periodCode: string, productId: string) => `${periodCode}:${productId}`

export function updateForecastDraft(
  drafts: Record<string, ForecastDraft>,
  row: { productId: string; currentForecastQty?: number | null; seriesVersion?: number | null },
  value: string,
) {
  const next = { ...drafts }
  const normalized = value.trim()
  const original = row.currentForecastQty == null ? '' : String(row.currentForecastQty)
  if (normalized === '' || normalized === original) {
    delete next[row.productId]
    return next
  }
  next[row.productId] = {
    productId: row.productId,
    forecastQty: normalized,
    expectedVersion: row.seriesVersion ?? null,
    isRevision: row.currentForecastQty != null,
  }
  return next
}

export function batchEntries(drafts: Record<string, ForecastDraft>) {
  return Object.values(drafts).map(draft => ({
    productId: draft.productId,
    forecastQty: Number(draft.forecastQty),
    expectedVersion: draft.expectedVersion,
  }))
}
