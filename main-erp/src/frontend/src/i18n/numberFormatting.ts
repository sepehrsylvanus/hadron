const operationalFormatters = new Map<string, Intl.NumberFormat>()

function localeFor(language: string) {
  return language.toLowerCase().startsWith('fa') ? 'fa-IR' : 'en-US'
}

/**
 * Formats operational quantities for compact on-screen presentation.
 * This is display-only: persisted values and calculation precision are unchanged.
 */
export function formatOperationalNumber(value: number | null | undefined, language = 'en') {
  if (value == null || !Number.isFinite(Number(value))) return '—'
  const locale = localeFor(language)
  let formatter = operationalFormatters.get(locale)
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, {
      maximumFractionDigits: 1,
      minimumFractionDigits: 0,
      useGrouping: true,
    })
    operationalFormatters.set(locale, formatter)
  }
  return formatter.format(Number(value))
}
