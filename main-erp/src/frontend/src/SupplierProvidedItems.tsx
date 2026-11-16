import { useEffect, useState, useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { History, Package, Pencil, Plus, Trash2, X } from 'lucide-react'
import { gregorianToJalaliParts, toPersianDigits } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { structureTypeKey } from './productStructureSupport'
import { tehranTodayIso } from './cashFlowUi'
import { EmptyState, FormField, LoadingState, MessageStrip, Section, StatusBadge } from './ui'
import {
  addSupplierProject,
  blankSupplierProjectDraft,
  ensureSupplierProjects,
  getSupplierProjectsSessionState,
  removeSupplierProject,
  subscribeSupplierProjectsSession,
  supplierProjectDisplayName,
  supplierProjectsFor,
  updateSupplierProject,
  type SupplierProject,
  type SupplierProjectDraft,
  type SupplierProjectItemRef,
} from './supplierProjectsSession'

type UnitRef = { id: string; code: string; nameFa: string; nameEn: string; symbol?: string | null; isActive: boolean }
type SuppliedItem = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; supplyMethod: string; isActive: boolean; baseUnit: UnitRef }
type SupplierSourcingOption = {
  imageUrl?: string | null
  isPreferred: boolean
  isActive?: boolean
  sourcingNumber?: string
  createdAtUtc?: string
  updatedAtUtc?: string
  totalConfiguredLeadTimeDays?: number | null
}
type SupplierItemGroup = { item: SuppliedItem; options?: SupplierSourcingOption[] }
type SupplierItemsPage = { items: SupplierItemGroup[]; totalItemCount: number }

/**
 * One supplied item with its sourcing history: an item counts as currently
 * supplied while at least one of its sourcing options is active; when every
 * option has been deactivated it becomes supply history. These records seed
 * the supplier's session projects (see supplierProjectsSession).
 */
type ProvidedItemRecord = {
  item: SuppliedItem
  imageUrl: string
  state: 'current' | 'past'
  since: string | null
  until: string | null
  sourcingNumbers: string[]
  optionsCount: number
  preferred: boolean
  leadTimeDays: number | null
  /** True for the clearly-labelled demo history of one showcase supplier. */
  demo?: boolean
}

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const pageSize = 100
const picsumImageUrl = (itemId: string) => `https://picsum.photos/seed/${encodeURIComponent(itemId)}/640/360`

/**
 * Demo supply history exists for exactly one showcase supplier —
 * Hadron Plast (SUP-0002) — so the history timeline can be seen even when the
 * database has no deactivated sourcing options yet. Every demo record is
 * badged «داده نمایشی / Demo data» and never mixes with real records.
 */
const DEMO_SUPPLY_HISTORY_SUPPLIER_NUMBER = 'SUP-0002'
const demoKgUnit: UnitRef = { id: 'demo-unit-kg', code: 'KG', nameFa: 'کیلوگرم', nameEn: 'Kilogram', symbol: 'kg', isActive: true }
const demoEachUnit: UnitRef = { id: 'demo-unit-ea', code: 'EA', nameFa: 'عدد', nameEn: 'Each', symbol: 'ea', isActive: true }

function demoSupplyHistoryRecords(supplierNumber?: string): ProvidedItemRecord[] {
  if (supplierNumber !== DEMO_SUPPLY_HISTORY_SUPPLIER_NUMBER) return []
  const make = (id: string, code: string, nameFa: string, nameEn: string, baseUnit: UnitRef, since: string, until: string, sourcingNumber: string, preferred: boolean): ProvidedItemRecord => ({
    item: { id, code, nameFa, nameEn, itemType: 'PART', supplyMethod: 'BUY', isActive: true, baseUnit },
    imageUrl: picsumImageUrl(id),
    state: 'past', since, until,
    sourcingNumbers: [sourcingNumber], optionsCount: 1, preferred, leadTimeDays: null, demo: true,
  })
  return [
    make('demo-history-ldpe', 'RM-2041', 'گرانول پلی‌اتیلن سبک', 'LDPE granules', demoKgUnit, '2020-04-01', '2021-09-18', 'SRC-1042', false),
    make('demo-history-masterbatch', 'RM-1187', 'مستربچ رنگی مشکی', 'Black masterbatch', demoKgUnit, '2021-06-15', '2023-02-27', 'SRC-1187', true),
    make('demo-history-pvc', 'RM-1263', 'رزین PVC گرید S65', 'PVC resin grade S65', demoKgUnit, '2022-10-03', '2024-05-30', 'SRC-1263', false),
    make('demo-history-stretch-film', 'PK-3310', 'فیلم استرچ بسته‌بندی', 'Stretch packaging film', demoEachUnit, '2023-03-12', '2025-01-08', 'SRC-1420', false),
  ]
}

/** Converts a real supplied-item record into a seeded session project. */
function recordToProjectSeed(record: ProvidedItemRecord): Omit<SupplierProject, 'id' | 'supplierId'> {
  const jalaliYear = record.since ? String(gregorianToJalaliParts(record.since)?.year ?? '') : ''
  return {
    manualInfo: '',
    shamsiYear: jalaliYear,
    orderCount: Math.max(1, record.optionsCount),
    pieceName: record.item.nameFa,
    productName: record.item.nameEn ?? '',
    orderYear: jalaliYear,
    orderRepeat: 1,
    pieceSerial: record.item.code,
    item: { id: record.item.id, code: record.item.code, nameFa: record.item.nameFa, nameEn: record.item.nameEn ?? null, itemType: record.item.itemType },
    status: record.state === 'current' ? 'current' : 'previous',
    demo: record.demo,
  }
}

type ProjectEditor = { projectId: string | null; draft: SupplierProjectDraft }

export default function SupplierProvidedItems({ supplierId, supplierNumber }: { supplierId: string; supplierNumber?: string }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const persian = !language.toLowerCase().startsWith('en')
  const session = useSyncExternalStore(subscribeSupplierProjectsSession, getSupplierProjectsSessionState, getSupplierProjectsSessionState)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [view, setView] = useState<'current' | 'previous'>('current')
  const [editor, setEditor] = useState<ProjectEditor | null>(null)
  const [editorError, setEditorError] = useState('')

  useEffect(() => {
    let active = true
    async function load() {
      setLoading(true)
      setError('')
      try {
        // includeInactive=true so deactivated sourcing options (the real
        // supply history) seed previous projects next to current ones.
        const loaded: ProvidedItemRecord[] = []
        const evaluationDate = tehranTodayIso()
        let page = 1
        let pageCount = 1
        do {
          const query = new URLSearchParams({
            evaluationDate, supplierId, includeInactive: 'true',
            page: String(page), pageSize: String(pageSize),
          })
          const response = await fetch(`${api}/sourcing-options/tree?${query}`)
          if (!response.ok) throw new Error(await problem(response))
          const body = await response.json() as SupplierItemsPage
          loaded.push(...body.items.map(toProvidedItemRecord))
          pageCount = Math.max(1, Math.ceil(body.totalItemCount / pageSize))
          page++
        } while (page <= pageCount)
        if (active) {
          ensureSupplierProjects(supplierId, [...loaded, ...demoSupplyHistoryRecords(supplierNumber)].map(recordToProjectSeed))
        }
      } catch (caught) {
        if (active) setError(caught instanceof Error ? caught.message : t('common.error'))
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [supplierId, supplierNumber, t])

  const projects = supplierProjectsFor(session, supplierId)
  const currentProjects = projects.filter(project => project.status === 'current')
  const previousProjects = projects.filter(project => project.status === 'previous')
  const visibleProjects = view === 'current' ? currentProjects : previousProjects

  const localizeDigits = (value: string) => persian ? toPersianDigits(value) : value
  const displayName = (project: SupplierProject) => localizeDigits(supplierProjectDisplayName(project))

  function openNewProject() {
    setEditor({ projectId: null, draft: { ...blankSupplierProjectDraft(), status: view } })
    setEditorError('')
  }

  function openEditProject(project: SupplierProject) {
    setEditor({
      projectId: project.id,
      draft: {
        manualInfo: project.manualInfo, shamsiYear: project.shamsiYear, orderCount: project.orderCount,
        pieceName: project.pieceName, productName: project.productName, orderYear: project.orderYear,
        orderRepeat: project.orderRepeat, pieceSerial: project.pieceSerial,
        item: project.item ? { ...project.item } : null, status: project.status, demo: project.demo,
      },
    })
    setEditorError('')
  }

  function saveProject() {
    if (!editor) return
    const draft = editor.draft
    if (!draft.pieceName.trim() && !draft.item) {
      setEditorError(t('suppliers.projects.validation'))
      return
    }
    if (editor.projectId) updateSupplierProject(supplierId, editor.projectId, draft)
    else addSupplierProject(supplierId, draft)
    setView(draft.status)
    setEditor(null)
    setEditorError('')
  }

  function deleteProject() {
    if (!editor?.projectId) return
    removeSupplierProject(supplierId, editor.projectId)
    setEditor(null)
  }

  const toggle = <div className="supplier-projects-actions">
    <div className="supplier-provided-toggle" role="tablist" aria-label={t('suppliers.projects.viewLabel')}>
      <button type="button" role="tab" aria-selected={view === 'current'} className={`supplier-provided-toggle__button${view === 'current' ? ' is-active' : ''}`} onClick={() => setView('current')}>
        <span className="supplier-provided-toggle__dot is-live" aria-hidden="true" />{t('suppliers.projects.current')}<span className="supplier-provided-toggle__count">{currentProjects.length}</span>
      </button>
      <button type="button" role="tab" aria-selected={view === 'previous'} className={`supplier-provided-toggle__button${view === 'previous' ? ' is-active' : ''}`} onClick={() => setView('previous')}>
        <History aria-hidden="true" />{t('suppliers.projects.previous')}<span className="supplier-provided-toggle__count">{previousProjects.length}</span>
      </button>
    </div>
    <button type="button" className="button button--primary" onClick={openNewProject}><Plus aria-hidden="true" />{t('suppliers.projects.newProject')}</button>
  </div>

  return <Section title={t('suppliers.projects.title')} description={t('suppliers.projects.description')} actions={toggle}>
    <div className="supplier-projects">
      <p className="supplier-projects-note" role="note">{t('suppliers.projects.sessionNote')}</p>
      {projects.some(project => project.demo) && <p className="supplier-supply-demo-note" role="note">{t('suppliers.providedHistory.demoNotice')}</p>}
      {error && <MessageStrip tone="critical">{error}</MessageStrip>}

      {editor && <ProjectEditorPanel
        editor={editor}
        displayName={supplierProjectDisplayName(editor.draft)}
        localizeDigits={localizeDigits}
        onChange={draft => { setEditor({ ...editor, draft }); setEditorError('') }}
        onSave={saveProject}
        onCancel={() => setEditor(null)}
        onDelete={editor.projectId ? deleteProject : undefined}
        error={editorError}
      />}

      {loading && projects.length === 0 ? <LoadingState label={t('common.loading')} compact /> : visibleProjects.length === 0
        ? <EmptyState title={t(view === 'current' ? 'suppliers.projects.emptyCurrent' : 'suppliers.projects.emptyPrevious')} compact />
        : <ul className="supplier-projects-grid">{visibleProjects.map(project => <li key={project.id} className={`supplier-project-card${project.status === 'previous' ? ' is-previous' : ''}`}>
          <div className="supplier-project-card__head">
            <strong className="supplier-project-card__name">{displayName(project)}</strong>
            <span className="supplier-project-card__badges">
              {project.demo && <span className="supplier-supply-chip is-demo">{t('suppliers.providedHistory.demoBadge')}</span>}
              <StatusBadge text={t(project.status === 'current' ? 'suppliers.projects.currentBadge' : 'suppliers.projects.endedBadge')} tone={project.status === 'current' ? 'positive' : 'neutral'} />
            </span>
          </div>
          <span className="supplier-project-card__serial">{t('suppliers.projects.serialLabel')}: <bdi className="ui-technical">{project.pieceSerial || '—'}</bdi></span>
          <div className="supplier-project-card__foot">
            {project.item
              ? <span className="supplier-supply-chip is-period"><Package aria-hidden="true" />{localizedName(project.item, language)} · <bdi className="ui-technical">{project.item.code}</bdi> · {t(structureTypeKey(project.item.itemType))}</span>
              : <span className="supplier-supply-chip">{t('suppliers.projects.noItemAssigned')}</span>}
            <button type="button" className="button button--quiet supplier-project-card__edit" onClick={() => openEditProject(project)}><Pencil aria-hidden="true" />{t('common.edit')}</button>
          </div>
        </li>)}</ul>}
    </div>
  </Section>
}

function ProjectEditorPanel({ editor, displayName, localizeDigits, onChange, onSave, onCancel, onDelete, error }: {
  editor: ProjectEditor
  displayName: string
  localizeDigits: (value: string) => string
  onChange: (draft: SupplierProjectDraft) => void
  onSave: () => void
  onCancel: () => void
  onDelete?: () => void
  error: string
}) {
  const { t } = useTranslation()
  const draft = editor.draft
  const set = (patch: Partial<SupplierProjectDraft>) => onChange({ ...draft, ...patch })
  const digitsOnly = (value: string) => value.replace(/[^0-9]/g, '')
  const numberOr = (value: string, fallback: number) => {
    const parsed = Number.parseInt(value, 10)
    return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback
  }

  return <div className="supplier-project-editor">
    <div className="supplier-project-editor__head">
      <strong>{t(editor.projectId ? 'suppliers.projects.editProject' : 'suppliers.projects.newProject')}</strong>
      <span className="supplier-project-editor__preview">{t('suppliers.projects.namePreview')}: <bdi>{displayName ? localizeDigits(displayName) : '—'}</bdi></span>
    </div>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <div className="ui-form-grid ui-form-grid--3">
      <FormField label={t('suppliers.projects.manualInfo')}><input value={draft.manualInfo} onChange={event => set({ manualInfo: event.target.value })} /></FormField>
      <FormField label={t('suppliers.projects.shamsiYear')}><input dir="ltr" inputMode="numeric" value={draft.shamsiYear} onChange={event => set({ shamsiYear: digitsOnly(event.target.value).slice(0, 4) })} /></FormField>
      <FormField label={t('suppliers.projects.orderCount')}><input dir="ltr" type="number" min="0" value={draft.orderCount} onChange={event => set({ orderCount: numberOr(event.target.value, 0) })} /></FormField>
      <FormField label={t('suppliers.projects.pieceName')}><input value={draft.pieceName} onChange={event => set({ pieceName: event.target.value })} /></FormField>
      <FormField label={t('suppliers.projects.productName')}><input value={draft.productName} onChange={event => set({ productName: event.target.value })} /></FormField>
      <FormField label={t('suppliers.projects.orderYear')}><input dir="ltr" inputMode="numeric" value={draft.orderYear} onChange={event => set({ orderYear: digitsOnly(event.target.value).slice(0, 4) })} /></FormField>
      <FormField label={t('suppliers.projects.orderRepeat')}><input dir="ltr" type="number" min="0" value={draft.orderRepeat} onChange={event => set({ orderRepeat: numberOr(event.target.value, 0) })} /></FormField>
      <FormField label={t('suppliers.projects.pieceSerial')}><input dir="ltr" value={draft.pieceSerial} onChange={event => set({ pieceSerial: event.target.value })} /></FormField>
      <FormField label={t('suppliers.projects.statusLabel')}>
        <select value={draft.status} onChange={event => set({ status: event.target.value as SupplierProjectDraft['status'] })}>
          <option value="current">{t('suppliers.projects.current')}</option>
          <option value="previous">{t('suppliers.projects.previous')}</option>
        </select>
      </FormField>
    </div>

    <ProjectItemAssign value={draft.item} onChange={item => set({ item })} />

    <div className="supplier-project-editor__actions">
      <button type="button" className="button button--primary" onClick={onSave}>{t('common.save')}</button>
      <button type="button" className="button" onClick={onCancel}>{t('common.cancel')}</button>
      {editor.projectId && (draft.status === 'current'
        ? <button type="button" className="button button--quiet" onClick={() => set({ status: 'previous' })}>{t('suppliers.projects.endProject')}</button>
        : <button type="button" className="button button--quiet" onClick={() => set({ status: 'current' })}>{t('suppliers.projects.reactivateProject')}</button>)}
      {onDelete && <button type="button" className="button button--danger supplier-project-editor__delete" onClick={onDelete}><Trash2 aria-hidden="true" />{t('suppliers.projects.deleteProject')}</button>}
    </div>
  </div>
}

const ITEM_RESOURCES: Record<string, string> = { PRODUCT: 'products', PART: 'parts', COMPONENT: 'components' }

/** Catalog picker: assign one product / part / component to the project. */
function ProjectItemAssign({ value, onChange }: { value: SupplierProjectItemRef | null; onChange: (item: SupplierProjectItemRef | null) => void }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const [itemType, setItemType] = useState('PRODUCT')
  const [search, setSearch] = useState('')
  const [results, setResults] = useState<SupplierProjectItemRef[]>([])
  const [searching, setSearching] = useState(false)

  useEffect(() => {
    if (value) return
    let active = true
    const timer = setTimeout(() => {
      setSearching(true)
      const query = new URLSearchParams({ page: '1', pageSize: '8', includeInactive: 'false' })
      if (search.trim()) query.set('search', search.trim())
      void fetch(`${api}/${ITEM_RESOURCES[itemType]}?${query}`)
        .then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() })
        .then(body => {
          if (!active) return
          const rows = (body.items ?? []) as Array<{ id: string; code: string; nameFa: string; nameEn?: string | null; itemType?: string }>
          setResults(rows.map(row => ({ id: row.id, code: row.code, nameFa: row.nameFa, nameEn: row.nameEn ?? null, itemType: row.itemType ?? itemType })))
        })
        .catch(() => { if (active) setResults([]) })
        .finally(() => { if (active) setSearching(false) })
    }, 200)
    return () => { active = false; clearTimeout(timer) }
  }, [itemType, search, value])

  return <fieldset className="supplier-project-assign">
    <legend>{t('suppliers.projects.assignItem')}</legend>
    {value ? <div className="supplier-project-assign__selected">
      <Package aria-hidden="true" />
      <strong>{localizedName(value, language)}</strong>
      <bdi className="ui-technical">{value.code}</bdi>
      <span>{t(structureTypeKey(value.itemType))}</span>
      <button type="button" className="button button--quiet" onClick={() => onChange(null)}><X aria-hidden="true" />{t('suppliers.projects.removeItem')}</button>
    </div> : <>
      <div className="supplier-project-assign__controls">
        <div className="segmented-control" role="group" aria-label={t('suppliers.projects.assignItem')}>
          {Object.keys(ITEM_RESOURCES).map(type => <button key={type} type="button" className={itemType === type ? 'active' : ''} aria-pressed={itemType === type} onClick={() => setItemType(type)}>{t(structureTypeKey(type))}</button>)}
        </div>
        <FormField label={t('common.search')}>
          <input value={search} placeholder={t('suppliers.projects.itemSearchPlaceholder')} onChange={event => setSearch(event.target.value)} />
        </FormField>
      </div>
      <div className="supplier-project-assign__results">
        {searching ? <LoadingState label={t('common.loading')} compact /> : results.length === 0
          ? <span className="supplier-project-assign__empty">{t('common.noResultsTitle')}</span>
          : results.map(item => <button key={item.id} type="button" className="supplier-project-assign__option" onClick={() => onChange(item)}>
            <span>{localizedName(item, language)}</span><bdi className="ui-technical">{item.code}</bdi>
          </button>)}
      </div>
    </>}
  </fieldset>
}

function toProvidedItemRecord(group: SupplierItemGroup): ProvidedItemRecord {
  const options = group.options ?? []
  // Options only count as ended when the API explicitly marks them inactive.
  const activeOptions = options.filter(option => option.isActive !== false)
  const state: ProvidedItemRecord['state'] = options.length === 0 || activeOptions.length > 0 ? 'current' : 'past'
  const createdDates = options.map(option => option.createdAtUtc?.slice(0, 10)).filter((value): value is string => Boolean(value)).sort()
  const updatedDates = options.map(option => option.updatedAtUtc?.slice(0, 10)).filter((value): value is string => Boolean(value)).sort()
  const preferredActive = activeOptions.find(option => option.isPreferred) ?? activeOptions[0]
  return {
    item: group.item,
    imageUrl: pickOptionImage(options, group.item.id),
    state,
    since: createdDates[0] ?? null,
    until: state === 'past' ? (updatedDates[updatedDates.length - 1] ?? createdDates[createdDates.length - 1] ?? null) : null,
    sourcingNumbers: options.map(option => option.sourcingNumber).filter((value): value is string => Boolean(value)),
    optionsCount: options.length,
    preferred: options.some(option => option.isPreferred),
    leadTimeDays: preferredActive?.totalConfiguredLeadTimeDays ?? null,
  }
}

function pickOptionImage(options: SupplierSourcingOption[], itemId: string) {
  const preferredImage = options.find(option => option.isPreferred && option.imageUrl?.trim())?.imageUrl?.trim()
  const anyImage = options.find(option => option.imageUrl?.trim())?.imageUrl?.trim()
  return preferredImage || anyImage || picsumImageUrl(itemId)
}

async function problem(response: Response) {
  const body = await response.json().catch(() => null) as { detail?: string; code?: string } | null
  return [body?.code, body?.detail].filter(Boolean).join(' — ') || `HTTP ${response.status}`
}
