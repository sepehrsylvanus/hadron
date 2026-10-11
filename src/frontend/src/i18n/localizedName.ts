export type LocalizedName = {
  nameFa: string
  nameEn?: string | null
}

export function localizedName(value: LocalizedName, language?: string | null): string {
  return language?.toLowerCase().startsWith('en') && value.nameEn?.trim()
    ? value.nameEn
    : value.nameFa
}

/**
 * Single-name display for the Product / Part / Component masters (display-only merge:
 * prefers NameEn when present, otherwise falls back to NameFa).
 * Edit forms keep the two underlying inputs; this only changes what is shown.
 */
export function mergedName(value: LocalizedName): string {
  return value.nameEn?.trim() ? value.nameEn : value.nameFa
}
