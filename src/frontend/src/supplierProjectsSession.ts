/**
 * Supplier projects (پروژه‌ها) — session-only, front-end-only for now.
 *
 * A project replaces the plain "supplied item" presentation on a supplier:
 * it has a structured name built from manual inputs
 *   {manual info} {shamsi year} - {order count} - {piece}-{product}-{year}-{repeat}
 * with the piece serial shown on its own line, and one assigned catalog item
 * (product / part / component). Projects live in module memory keyed by
 * supplier: nothing is sent to the backend and a page refresh resets them
 * to the seeded state (the supplier's real supplied items).
 */

export type SupplierProjectStatus = 'current' | 'previous'

export type SupplierProjectItemRef = {
  id: string
  code: string
  nameFa: string
  nameEn?: string | null
  itemType: string
}

export type SupplierProject = {
  id: string
  supplierId: string
  /** Free-form manual info shown at the start of the project name (اطلاعات دستی). */
  manualInfo: string
  /** Shamsi year shown after the manual info, e.g. "1404". */
  shamsiYear: string
  /** Number of orders (تعداد سفارش). */
  orderCount: number
  /** Display name of the piece (نام قطعه). */
  pieceName: string
  /** Display name of the product (نام محصول). */
  productName: string
  /** Year inside the piece-product-year-repeat group. */
  orderYear: string
  /** Order repeat number (شماره تکرار سفارش). */
  orderRepeat: number
  /** Piece serial number, shown wrapped on its own line (سریال قطعه). */
  pieceSerial: string
  /** Assigned catalog product / part / component, if any. */
  item: SupplierProjectItemRef | null
  status: SupplierProjectStatus
  /** Seeded demo record (currently only Hadron Plast SUP-0002 history). */
  demo?: boolean
}

export type SupplierProjectDraft = Omit<SupplierProject, 'id' | 'supplierId'>

const emptyDraft = (): SupplierProjectDraft => ({
  manualInfo: '', shamsiYear: '', orderCount: 1, pieceName: '', productName: '',
  orderYear: '', orderRepeat: 1, pieceSerial: '', item: null, status: 'current',
})

export function blankSupplierProjectDraft(): SupplierProjectDraft {
  return emptyDraft()
}

/**
 * Builds the full project display name from its inputs:
 * `{manual} {shamsiYear} - {orderCount} - {piece}-{product}-{orderYear}-{repeat}`.
 * Empty leading parts are skipped so seeded projects still read cleanly.
 */
export function supplierProjectDisplayName(project: Pick<SupplierProject, 'manualInfo' | 'shamsiYear' | 'orderCount' | 'pieceName' | 'productName' | 'orderYear' | 'orderRepeat'>): string {
  const head = [project.manualInfo.trim(), project.shamsiYear.trim()].filter(Boolean).join(' ')
  const group = [project.pieceName.trim(), project.productName.trim(), project.orderYear.trim(), String(project.orderRepeat)]
    .filter((part, index) => part !== '' || index === 3)
    .join('-')
  return [head, String(project.orderCount), group].filter(part => part !== '').join(' - ')
}

export type SupplierProjectsSessionState = {
  projectsBySupplier: Record<string, SupplierProject[]>
  seededSuppliers: string[]
}

let state: SupplierProjectsSessionState = { projectsBySupplier: {}, seededSuppliers: [] }
let counter = 0
const listeners = new Set<() => void>()

const emit = () => { for (const listener of listeners) listener() }

export function getSupplierProjectsSessionState(): SupplierProjectsSessionState {
  return state
}

export function subscribeSupplierProjectsSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export function supplierProjectsFor(stateSnapshot: SupplierProjectsSessionState, supplierId: string): SupplierProject[] {
  return stateSnapshot.projectsBySupplier[supplierId] ?? []
}

/** Seeds a supplier's projects once per session; later calls keep session edits. */
export function ensureSupplierProjects(supplierId: string, seed: Array<Omit<SupplierProject, 'id' | 'supplierId'>>) {
  if (state.seededSuppliers.includes(supplierId)) return
  const seeded = seed.map(draft => ({ ...draft, id: nextId(), supplierId }))
  state = {
    projectsBySupplier: {
      ...state.projectsBySupplier,
      [supplierId]: [...(state.projectsBySupplier[supplierId] ?? []), ...seeded],
    },
    seededSuppliers: [...state.seededSuppliers, supplierId],
  }
  emit()
}

export function addSupplierProject(supplierId: string, draft: SupplierProjectDraft): SupplierProject {
  const project: SupplierProject = { ...draft, id: nextId(), supplierId }
  state = {
    ...state,
    projectsBySupplier: {
      ...state.projectsBySupplier,
      [supplierId]: [...(state.projectsBySupplier[supplierId] ?? []), project],
    },
  }
  emit()
  return project
}

export function updateSupplierProject(supplierId: string, projectId: string, patch: Partial<SupplierProjectDraft>) {
  const projects = state.projectsBySupplier[supplierId]
  if (!projects?.some(project => project.id === projectId)) return
  state = {
    ...state,
    projectsBySupplier: {
      ...state.projectsBySupplier,
      [supplierId]: projects.map(project => project.id === projectId ? { ...project, ...patch } : project),
    },
  }
  emit()
}

export function setSupplierProjectStatus(supplierId: string, projectId: string, status: SupplierProjectStatus) {
  updateSupplierProject(supplierId, projectId, { status })
}

export function removeSupplierProject(supplierId: string, projectId: string) {
  const projects = state.projectsBySupplier[supplierId]
  if (!projects) return
  state = {
    ...state,
    projectsBySupplier: {
      ...state.projectsBySupplier,
      [supplierId]: projects.filter(project => project.id !== projectId),
    },
  }
  emit()
}

/** Restores the pristine session (used by tests; a refresh does this naturally). */
export function resetSupplierProjectsSession() {
  state = { projectsBySupplier: {}, seededSuppliers: [] }
  counter = 0
  emit()
}

function nextId() {
  counter += 1
  return `session-project-${counter}`
}
