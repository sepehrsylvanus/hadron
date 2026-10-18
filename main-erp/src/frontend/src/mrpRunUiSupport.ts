export function toAsciiDigits(value: string) {
  return value.replace(/[۰-۹]/g, digit => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit)))
    .replace(/[٠-٩]/g, digit => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)))
}

export function normalizeJalaliDateInput(value: string) {
  const normalized = toAsciiDigits(value).replace(/[^0-9]/g, '')
  if (!/^\d{8}$/.test(normalized)) return null
  const year = Number(normalized.slice(0, 4))
  const month = Number(normalized.slice(4, 6))
  const day = Number(normalized.slice(6, 8))
  return year > 0 && month >= 1 && month <= 12 && day >= 1 && day <= 31 ? normalized : null
}

export function isPersianLanguage(language: string) {
  return !language.toLowerCase().startsWith('en')
}
