export type BomBuilderNode = { itemType: string; itemId: string }

const allowedTypes = new Set(['PRODUCT', 'PART', 'COMPONENT'])

export const BOM_PICKER_PAGE_SIZE = 25

export type BomSubstructureState = 'DEFINED' | 'MISSING' | 'UNINSPECTED'
export type BomLifecycleStep = 'DRAFT' | 'VALIDATE' | 'APPROVE'
export type BomBaseUnitMismatch =
  | { role: 'PARENT'; code: string; snapshot: { id: string; nameFa: string; nameEn?: string | null }; current: { id: string; nameFa: string; nameEn?: string | null } }
  | { role: 'CHILD'; code: string; snapshot: { id: string; nameFa: string; nameEn?: string | null }; current: { id: string; nameFa: string; nameEn?: string | null } }

export function bomSubstructureState(hasRevision: boolean, inspectedWithoutRevision: boolean): BomSubstructureState {
  if (hasRevision) return 'DEFINED'
  return inspectedWithoutRevision ? 'MISSING' : 'UNINSPECTED'
}

export function expandableBomParentIds<T extends { parentItem: { id: string }; lines: unknown[] }>(revisions: T[]) {
  return revisions.filter(revision => revision.lines.length > 0).map(revision => revision.parentItem.id)
}

export function validBomUsageCoefficient(value: string) {
  if (!value.trim()) return false
  const quantity = Number(value)
  return Number.isFinite(quantity) && quantity > 0
}

export function preferredBomRevision<T extends { status: string }>(revisions: T[]) {
  return revisions.find(revision => revision.status === 'DRAFT') ?? revisions[0] ?? null
}

export function bomLifecycleStep(status: string | null | undefined, validationPassed: boolean): BomLifecycleStep {
  if (status === 'APPROVED' || status === 'OBSOLETE') return 'APPROVE'
  return validationPassed ? 'VALIDATE' : 'DRAFT'
}

export function findBomBaseUnitMismatch(value: {
  parentItem: { code: string; currentBaseUnit: { id: string; nameFa: string; nameEn?: string | null } }
  parentBaseUnitSnapshot: { id: string; nameFa: string; nameEn?: string | null }
  lines: Array<{
    childItem: { code: string; currentBaseUnit: { id: string; nameFa: string; nameEn?: string | null } }
    childBaseUnitSnapshot: { id: string; nameFa: string; nameEn?: string | null }
  }>
}): BomBaseUnitMismatch | null {
  if (value.parentItem.currentBaseUnit.id !== value.parentBaseUnitSnapshot.id) {
    return { role: 'PARENT', code: value.parentItem.code, snapshot: value.parentBaseUnitSnapshot, current: value.parentItem.currentBaseUnit }
  }
  const line = value.lines.find(candidate => candidate.childItem.currentBaseUnit.id !== candidate.childBaseUnitSnapshot.id)
  return line
    ? { role: 'CHILD', code: line.childItem.code, snapshot: line.childBaseUnitSnapshot, current: line.childItem.currentBaseUnit }
    : null
}

export function parseBomTrail(value: string | null | undefined): BomBuilderNode[] {
  if (!value) return []
  return value.split(',').map(segment => {
    const separator = segment.indexOf(':')
    return separator < 0
      ? null
      : { itemType: segment.slice(0, separator), itemId: segment.slice(separator + 1) }
  }).filter((entry): entry is BomBuilderNode => Boolean(
    entry && allowedTypes.has(entry.itemType) && /^[0-9a-f-]{36}$/i.test(entry.itemId),
  ))
}

export function serializeBomTrail(entries: BomBuilderNode[]) {
  return entries.map(entry => `${entry.itemType}:${entry.itemId}`).join(',')
}

export function pushBomTrail(value: string | null | undefined, current: BomBuilderNode) {
  return serializeBomTrail([...parseBomTrail(value), current])
}

export function popBomTrail(value: string | null | undefined) {
  const entries = parseBomTrail(value)
  const previous = entries.pop() ?? null
  return { previous, trail: serializeBomTrail(entries) }
}

export function childTypeFor(parentType: string) {
  return parentType === 'PRODUCT' ? 'PART' : 'COMPONENT'
}

export function bomBuilderPath(node: BomBuilderNode, root?: BomBuilderNode) {
  const query = new URLSearchParams({ parentItemId: node.itemId, parentType: node.itemType })
  if (root) {
    query.set('rootItemId', root.itemId)
    query.set('rootItemType', root.itemType)
  }
  return `/mrp/engineering/bom?${query}`
}
