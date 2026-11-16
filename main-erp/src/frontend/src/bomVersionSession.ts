/**
 * Demo multi-version BOMs with session-only version activation.
 *
 * This is a front-end-only playground for testing "activate a previous BOM
 * version" behaviour: exactly one revision per BOM is active, activating an
 * older revision deactivates the currently active (newer) one, and every
 * activation requires a written reason. Nothing is sent to the backend and
 * nothing is persisted — the state lives in module memory only, so a page
 * refresh restores the default active versions.
 */

export type DemoBomRevision = {
  id: string
  code: string
  /** ISO calendar date the revision was created. */
  createdAt: string
  lineCount: number
  noteFa: string
  noteEn: string
}

export type DemoBomFamily = {
  id: string
  parentCode: string
  nameFa: string
  nameEn: string
  itemType: 'PRODUCT' | 'COMPONENT' | 'PART'
  revisions: DemoBomRevision[]
  /** Revision that is active before any session activation. */
  defaultActiveId: string
}

const revision = (id: string, code: string, createdAt: string, lineCount: number, noteFa: string, noteEn: string): DemoBomRevision =>
  ({ id, code, createdAt, lineCount, noteFa, noteEn })

export const DEMO_BOM_FAMILIES: DemoBomFamily[] = [
  {
    id: 'demo-bom-fan', parentCode: 'PROD-100', nameFa: 'پنکه ایستاده', nameEn: 'Standing fan', itemType: 'PRODUCT',
    revisions: [
      revision('fan-bom-1', 'BOM-1', '2023-03-21', 14, 'ساختار اولیه محصول', 'Initial product structure'),
      revision('fan-bom-2', 'BOM-2', '2023-08-10', 15, 'تغییر تأمین‌کننده موتور', 'Motor supplier change'),
      revision('fan-bom-3', 'BOM-3', '2024-02-18', 16, 'افزودن محافظ ایمنی پره', 'Added blade safety guard'),
      revision('fan-bom-4', 'BOM-4', '2024-11-05', 16, 'بهبود سیم‌کشی و کلید لمسی', 'Wiring and touch-switch improvement'),
      revision('fan-bom-5', 'BOM-5', '2025-06-22', 17, 'کاهش نویز و بهینه‌سازی مصرف انرژی', 'Noise reduction and energy optimisation'),
    ],
    defaultActiveId: 'fan-bom-5',
  },
  {
    id: 'demo-bom-motor', parentCode: 'COMP-210', nameFa: 'مجموعه موتور', nameEn: 'Motor assembly', itemType: 'COMPONENT',
    revisions: [
      revision('motor-bom-1', 'BOM-1', '2023-05-14', 8, 'نسخه پایه مجموعه موتور', 'Base motor assembly'),
      revision('motor-bom-2', 'BOM-2', '2024-01-30', 9, 'تعویض بلبرینگ با مدل کم‌صدا', 'Low-noise bearing replacement'),
      revision('motor-bom-3', 'BOM-3', '2025-02-11', 9, 'تغییر جنس بدنه به آلومینیوم', 'Housing changed to aluminium'),
    ],
    defaultActiveId: 'motor-bom-3',
  },
  {
    id: 'demo-bom-board', parentCode: 'COMP-330', nameFa: 'برد کنترل', nameEn: 'Control board', itemType: 'COMPONENT',
    revisions: [
      revision('board-bom-1', 'BOM-1', '2022-09-01', 22, 'اولین نسخه برد کنترل', 'First control board revision'),
      revision('board-bom-2', 'BOM-2', '2023-04-17', 24, 'افزودن مدار محافظ ولتاژ', 'Added voltage protection circuit'),
      revision('board-bom-3', 'BOM-3', '2024-07-08', 23, 'حذف قطعات منسوخ و جایگزینی معادل', 'Obsolete parts replaced with equivalents'),
      revision('board-bom-4', 'BOM-4', '2025-09-15', 25, 'پشتیبانی از حسگر دمای جدید', 'Support for the new temperature sensor'),
    ],
    defaultActiveId: 'board-bom-4',
  },
  {
    id: 'demo-bom-packaging', parentCode: 'PKG-050', nameFa: 'بسته‌بندی صادراتی محصول', nameEn: 'Export packaging', itemType: 'PART',
    revisions: [
      revision('pack-bom-1', 'BOM-1', '2024-03-03', 6, 'بسته‌بندی اولیه صادراتی', 'Initial export packaging'),
      revision('pack-bom-2', 'BOM-2', '2025-04-19', 7, 'تقویت کارتن و افزودن ضربه‌گیر', 'Reinforced carton with shock absorbers'),
    ],
    defaultActiveId: 'pack-bom-2',
  },
]

export type BomActivationRecord = {
  familyId: string
  /** The revision that became active. */
  activatedRevisionId: string
  /** The revision that was active before and got deactivated (null if none). */
  deactivatedRevisionId: string | null
  /** Mandatory operator-supplied reason for the rollback. */
  reason: string
  /** Epoch milliseconds of the activation, for the session change log. */
  at: number
  seq: number
}

export type BomVersionSessionState = {
  activeByFamily: Record<string, string>
  log: BomActivationRecord[]
}

export type BomActivationResult =
  | { ok: true; deactivatedRevisionId: string | null }
  | { ok: false; error: 'REASON_REQUIRED' | 'UNKNOWN_FAMILY' | 'UNKNOWN_REVISION' | 'ALREADY_ACTIVE' }

const defaultState = (): BomVersionSessionState => ({
  activeByFamily: Object.fromEntries(DEMO_BOM_FAMILIES.map(family => [family.id, family.defaultActiveId])),
  log: [],
})

let state: BomVersionSessionState = defaultState()
let sequence = 0
const listeners = new Set<() => void>()

export function getBomVersionSessionState(): BomVersionSessionState {
  return state
}

export function subscribeBomVersionSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export function demoBomFamily(familyId: string): DemoBomFamily | undefined {
  return DEMO_BOM_FAMILIES.find(family => family.id === familyId)
}

export function activeRevisionId(family: DemoBomFamily, snapshot: BomVersionSessionState = state): string {
  return snapshot.activeByFamily[family.id] ?? family.defaultActiveId
}

/**
 * Activates `revisionId` for a BOM family, deactivating whichever revision
 * was active before. Requires a non-empty reason; state is module memory
 * only (refresh resets everything to the defaults).
 */
export function activateBomRevision(familyId: string, revisionId: string, reason: string): BomActivationResult {
  const family = demoBomFamily(familyId)
  if (!family) return { ok: false, error: 'UNKNOWN_FAMILY' }
  if (!family.revisions.some(item => item.id === revisionId)) return { ok: false, error: 'UNKNOWN_REVISION' }
  const trimmed = reason.trim()
  if (!trimmed) return { ok: false, error: 'REASON_REQUIRED' }
  const current = activeRevisionId(family)
  if (current === revisionId) return { ok: false, error: 'ALREADY_ACTIVE' }

  sequence += 1
  state = {
    activeByFamily: { ...state.activeByFamily, [familyId]: revisionId },
    log: [...state.log, {
      familyId,
      activatedRevisionId: revisionId,
      deactivatedRevisionId: current,
      reason: trimmed,
      at: Date.now(),
      seq: sequence,
    }],
  }
  for (const listener of listeners) listener()
  return { ok: true, deactivatedRevisionId: current }
}

/** Restores every family to its default active revision (used by refresh/tests). */
export function resetBomVersionSession() {
  state = defaultState()
  sequence = 0
  for (const listener of listeners) listener()
}
