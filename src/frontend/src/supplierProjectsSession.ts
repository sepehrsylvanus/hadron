/**
 * Supplier projects (پروژه‌ها) — session-only, front-end-only for now.
 *
 * A project replaces the plain "supplied item" presentation on a supplier:
 * its display name is
 *   {product name} - {order year} - {order repeat}
 * with the manual info shown as a parenthesized note under the name, the
 * piece serial on its own line, and one assigned catalog item
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
  /** Free-form manual info shown as the parenthesized note under the project name (اطلاعات دستی). */
  manualInfo: string
  /** Display name of the product; leads the project name (نام محصول). */
  productName: string
  /** Order year shown in the project name (سال سفارش). */
  orderYear: string
  /** Order repeat number (شماره تکرار سفارش). */
  orderRepeat: number
  /** Piece serial carried from the seeded supplied item, shown on the card (سریال قطعه). */
  pieceSerial: string
  /** Supplied catalog item the project was seeded from, if any. */
  item: SupplierProjectItemRef | null
  status: SupplierProjectStatus
  /** Seeded demo record (currently only Hadron Plast SUP-0002 history). */
  demo?: boolean
}

export type SupplierProjectDraft = Omit<SupplierProject, 'id' | 'supplierId'>

const emptyDraft = (): SupplierProjectDraft => ({
  manualInfo: '', productName: '',
  orderYear: '', orderRepeat: 1, pieceSerial: '', item: null, status: 'current',
})

export function blankSupplierProjectDraft(): SupplierProjectDraft {
  return emptyDraft()
}

/**
 * Builds the project's display name from its inputs:
 * `{product name} - {order year} - {order repeat}`. Empty leading parts are
 * skipped so seeded projects still read cleanly; the repeat always closes
 * the name.
 */
export function supplierProjectDisplayName(project: Pick<SupplierProject, 'productName' | 'orderYear' | 'orderRepeat'>): string {
  return [project.productName.trim(), project.orderYear.trim(), String(project.orderRepeat)]
    .filter((part, index) => part !== '' || index === 2)
    .join(' - ')
}

/** The manual info as the parenthesized note shown under the name, or '' when blank. */
export function supplierProjectManualNote(project: Pick<SupplierProject, 'manualInfo'>): string {
  const note = project.manualInfo.trim()
  return note === '' ? '' : `(${note})`
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
