export type LocalizedName = {
  nameFa: string
  nameEn?: string | null
}

export function localizedName(value: LocalizedName, language?: string | null): string {
  return language?.toLowerCase().startsWith('en') && value.nameEn?.trim()
    ? value.nameEn
    : value.nameFa
}
