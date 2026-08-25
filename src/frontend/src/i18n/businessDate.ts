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

export function formatBusinessDateTime(value: string | null | undefined, language: string) {
  if (!value) return '—'
  const locale = isEnglish(language) ? 'en-GB-u-ca-gregory' : 'fa-IR-u-ca-persian'
  return new Intl.DateTimeFormat(locale, {
    timeZone: tehranTimeZone,
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

export function businessDirection(language: string): 'rtl' | 'ltr' {
  return isEnglish(language) ? 'ltr' : 'rtl'
}

function isEnglish(language: string) {
  return language.toLowerCase().startsWith('en')
}
