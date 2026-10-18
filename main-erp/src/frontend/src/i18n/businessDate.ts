const tehranTimeZone = 'Asia/Tehran'

export function formatBusinessDate(value: string | null | undefined, language: string) {
  if (!value) return '—'
  const date = /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? new Date(`${value}T12:00:00Z`)
    : new Date(value)
  const locale = isEnglish(language) ? 'en-GB-u-ca-gregory' : 'fa-IR-u-ca-persian'
  return new Intl.DateTimeFormat(locale, {
    timeZone: tehranTimeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date)
}

export function formatDualBusinessDateRange(start: string, end: string, language: string) {
  const englishPrimary = isEnglish(language)
  const secondaryLanguage = englishPrimary ? 'fa-IR' : 'en'
  return {
    primary: `${formatBusinessDate(start, language)} - ${formatBusinessDate(end, language)}`,
    secondary: `${formatBusinessDate(start, secondaryLanguage)} - ${formatBusinessDate(end, secondaryLanguage)}`,
  }
}

export function formatBusinessDateTime(value: string | null | undefined, language: string) {
  if (!value) return '—'
  const locale = isEnglish(language) ? 'en-GB-u-ca-gregory' : 'fa-IR-u-ca-persian'
  return new Intl.DateTimeFormat(locale, {
    timeZone: tehranTimeZone,
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

export type JalaliDateParts = { year: number; month: number; day: number }

const persianDigits = '۰۱۲۳۴۵۶۷۸۹'

export function toPersianDigits(value: string | number) {
  return String(value).replace(/\d/g, digit => persianDigits[Number(digit)])
}

export function toAsciiDigits(value: string) {
  return value
    .replace(/[۰-۹]/g, digit => String(persianDigits.indexOf(digit)))
    .replace(/[٠-٩]/g, digit => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)))
}

/** Converts a Gregorian ISO date (yyyy-mm-dd) to its Jalali (Shamsi) parts. */
export function gregorianToJalaliParts(value: string | null | undefined): JalaliDateParts | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null
  const parts = new Intl.DateTimeFormat('fa-IR-u-ca-persian-nu-latn', {
    timeZone: 'UTC',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date(`${value}T12:00:00Z`))
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find(item => item.type === type)?.value)
  const jalali = { year: part('year'), month: part('month'), day: part('day') }
  return Number.isInteger(jalali.year) && Number.isInteger(jalali.month) && Number.isInteger(jalali.day) ? jalali : null
}

/**
 * Converts a Jalali (Shamsi) date to a Gregorian ISO date, or null when the
 * Jalali date does not exist (for example 30 Esfand in a non-leap year).
 * The Gregorian date is located by binary search using the platform Persian
 * calendar, so no leap-year table has to be maintained here.
 */
export function jalaliToGregorianIso(year: number, month: number, day: number): string | null {
  if (!Number.isInteger(year) || !Number.isInteger(month) || !Number.isInteger(day)) return null
  if (month < 1 || month > 12 || day < 1 || day > (month <= 6 ? 31 : 30)) return null
  const target: JalaliDateParts = { year, month, day }
  const dayMs = 86_400_000
  let low = Date.UTC(year + 620, 0, 1)
  let high = Date.UTC(year + 623, 0, 1)
  while (low < high) {
    const middle = low + Math.floor((high - low) / (2 * dayMs)) * dayMs
    const middleJalali = gregorianToJalaliParts(new Date(middle).toISOString().slice(0, 10))
    if (middleJalali && compareJalali(middleJalali, target) < 0) low = middle + dayMs
    else high = middle
  }
  const candidate = new Date(low).toISOString().slice(0, 10)
  const resolved = gregorianToJalaliParts(candidate)
  return resolved && compareJalali(resolved, target) === 0 ? candidate : null
}

/**
 * Parses a Jalali date typed by a user. Accepts Persian, Arabic, or Latin
 * digits, with or without separators: ۱۴۰۵/۰۶/۰۲, 1405/06/02, or 14050602.
 * Returns the Gregorian ISO date or null when invalid.
 */
export function parseJalaliDateInput(value: string): string | null {
  const digits = toAsciiDigits(value).replace(/[^0-9]/g, '')
  if (!/^\d{8}$/.test(digits)) return null
  return jalaliToGregorianIso(Number(digits.slice(0, 4)), Number(digits.slice(4, 6)), Number(digits.slice(6, 8)))
}

/** Formats a Gregorian ISO date as a Persian-digit Jalali input value: ۱۴۰۵/۰۶/۰۲ */
export function formatJalaliInputValue(value: string | null | undefined) {
  const jalali = gregorianToJalaliParts(value ?? '')
  if (!jalali) return ''
  const pad = (part: number, length = 2) => String(part).padStart(length, '0')
  return toPersianDigits(`${pad(jalali.year, 4)}/${pad(jalali.month)}/${pad(jalali.day)}`)
}

function compareJalali(left: JalaliDateParts, right: JalaliDateParts) {
  return left.year - right.year || left.month - right.month || left.day - right.day
}

export function businessDirection(language: string): 'rtl' | 'ltr' {
  return isEnglish(language) ? 'ltr' : 'rtl'
}

function isEnglish(language: string) {
  return language.toLowerCase().startsWith('en')
}
