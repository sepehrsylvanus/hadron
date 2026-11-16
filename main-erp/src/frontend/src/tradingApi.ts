import { apiRoot } from './apiClient'
import { formatBusinessDate } from './i18n/businessDate'
import { normalizeJalaliDateInput, toAsciiDigits } from './mrpRunUiSupport'

export const tradingDateInput = (value: string | undefined, language: string) => !value || language.startsWith('en')
  ? value ?? ''
  : toAsciiDigits(formatBusinessDate(value, 'fa-IR')).replace(/[^0-9]/g, '')

export async function canonicalTradingDate(value: string | undefined, language: string, required = false, resolverPath = '/planning/resolve-jalali-date') {
  if (!value?.trim()) { if (required) throw new Error(language.startsWith('en') ? 'Business date is required.' : 'تاریخ کسب‌وکار الزامی است.'); return null }
  if (language.startsWith('en')) return value
  const normalized = normalizeJalaliDateInput(value)
  if (!normalized) throw new Error('تاریخ شمسی باید ۸ رقم و به‌صورت YYYYMMDD باشد.')
  const response = await fetch(`${apiRoot}${resolverPath}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ jalaliDate: normalized }) })
  if (!response.ok) throw new Error('تاریخ شمسی معتبر نیست.')
  return (await response.json() as { gregorianDate: string }).gregorianDate
}

export async function tradingRequest<T>(path: string, method = 'GET', body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${apiRoot}/trading${path}`, { signal, method, headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string; errors?: Record<string, string[]> } | null
    throw new Error(problem?.detail ?? Object.values(problem?.errors ?? {})[0]?.[0] ?? problem?.title ?? `HTTP ${response.status}`)
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}
