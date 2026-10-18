export type StructureName = { nameFa: string; nameEn?: string | null }

export function localizedStructureName(item: StructureName, language: string) {
  return language === 'en' && item.nameEn?.trim() ? item.nameEn : item.nameFa
}

export function structureItemRoute(itemType: string, itemId: string) {
  if (itemType === 'PRODUCT') return `/mrp/products/${itemId}`
  if (itemType === 'PART') return `/mrp/parts/${itemId}`
  if (itemType === 'COMPONENT') return `/mrp/components/${itemId}`
  return null
}

export function structureTypeKey(itemType: string) {
  if (itemType === 'PRODUCT') return 'products.object'
  if (itemType === 'PART') return 'materials.part'
  return 'materials.component'
}
