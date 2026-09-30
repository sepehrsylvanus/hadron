import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router-dom'
import { ArrowLeft, ArrowRight, Box, CheckCircle2, ChevronDown, ChevronLeft, ChevronRight, CircleOff, CircuitBoard, Copy, Eye, GitBranch, ListTree, Pencil, Plus, Save, ShieldCheck, Trash2, Wrench, X } from 'lucide-react'
import { localizedName } from './i18n/localizedName'
import { applicableItemsUrl } from './materialRelationshipSupport'
import ProductStructure from './ProductStructure'
import { BOM_PICKER_PAGE_SIZE, bomLifecycleStep, bomSubstructureState, childTypeFor, expandableBomParentIds, findBomBaseUnitMismatch, parseBomTrail, popBomTrail, preferredBomRevision, serializeBomTrail, validBomUsageCoefficient } from './bomBuilderSupport'
import {
  ConfirmationDialog,
  EmptyState,
  FilterBar,
  FormField,
  LoadingState,
  MessageStrip,
  PageHeader,
  Pagination,
  Section,
  StatusBadge,
  TableToolbar,
} from './ui'
import { useListQuery } from './ui/routeContext'
import { useAuth } from './AuthContext'

type Unit = { id: string; code: string; nameFa: string; nameEn?: string; symbol?: string | null; isActive: boolean }
type Item = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; isActive: boolean; baseUnit: Unit; isApplicable?: boolean }
type ItemRef = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; isActive: boolean; supplyMethod?: string | null; currentBaseUnit: Unit }
type Line = { id: string; childItem: ItemRef; childBaseUnitSnapshot: Unit; usageQty: number; scrapOverridePercent?: number | null; effectiveScrapPercent: number; sortOrder: number; integrityReasonCodes: string[] }
type Bom = { id: string; revisionCode: string; parentItem: ItemRef; parentBaseUnitSnapshot: Unit; status: string; baseOutputQty: number; revisionNotes?: string | null; copiedFromRevisionId?: string | null; version: number; lineCount: number; warningCodes: string[]; integrityReasonCodes: string[]; lines: Line[] }
type BomTreeRevision = Pick<Bom, 'id' | 'revisionCode' | 'parentItem' | 'parentBaseUnitSnapshot' | 'status' | 'baseOutputQty' | 'version' | 'lines'>
type BomBuilderTreeResponse = { rootItem: ItemRef; revisions: BomTreeRevision[]; missingParentItemIds: string[] }
type HeaderEditor = { id?: string; parentItemId: string; baseOutputQty: string; revisionNotes: string; version?: number }
type LineEditor = { id: string; childItemId: string; childUnit?: Unit; usageQty: string; scrapOverridePercent: string; sortOrder: string }
type ValidationResult = { isValidForApproval: boolean; warningCodes: string[]; blockingCodes: string[] }
type ActionPanel =
  | { kind: 'copySameParent'; source: Bom }
  | { kind: 'copyToProduct'; source: Bom; targetParentItemId: string }
  | { kind: 'discardDraft'; source: Bom }
  | { kind: 'approve' }
  | { kind: 'obsolete' }
  | null

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const emptyHeader: HeaderEditor = { parentItemId: '', baseOutputQty: '1', revisionNotes: '' }
const emptyLine: LineEditor = { id: '', childItemId: '', usageQty: '', scrapOverridePercent: '', sortOrder: '0' }
const tone = (status: string) => status === 'APPROVED' ? 'positive' : status === 'DRAFT' ? 'information' : 'neutral'

export default function BomMaster() {
  const query = useListQuery()
  const contextKey = [query.value('parentItemId') || 'ROOT', query.value('rootItemId'), query.value('rootRevisionId')].join(':')
  return <BomMasterWorkspace key={contextKey} />
}

function BomMasterWorkspace() {
  const { t, i18n } = useTranslation()
  const { has } = useAuth()
  const location = useLocation()
  const query = useListQuery()
  const updateQuery = query.update
  const search = query.value('search')
  const status = query.value('status')
  const itemType = query.value('itemType')
  const selectedId = query.value('selected')
  const parentItemId = query.value('parentItemId')
  const queryParentType = query.value('parentType') || 'PRODUCT'
  const rootItemId = query.value('rootItemId')
  const rootItemType = query.value('rootItemType')
  const rootRevisionId = query.value('rootRevisionId')
  const trailValue = query.value('trail')

  const [rows, setRows] = useState<Bom[]>([])
  const [total, setTotal] = useState(0)
  const [detail, setDetail] = useState<Bom | null>(null)
  const [parentContext, setParentContext] = useState<Item | null>(null)
  const [selectedLine, setSelectedLine] = useState<Line | null>(null)
  const [editor, setEditor] = useState<HeaderEditor | null>(null)
  const [parentType, setParentType] = useState(queryParentType)
  const [lineEditor, setLineEditor] = useState<LineEditor>({ ...emptyLine })
  const [lineEditing, setLineEditing] = useState(false)
  const [lineError, setLineError] = useState('')
  const [action, setAction] = useState<ActionPanel>(null)
  const [removeLine, setRemoveLine] = useState<Line | null>(null)
  const [validation, setValidation] = useState<ValidationResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [treeDetails, setTreeDetails] = useState<Record<string, BomTreeRevision>>({})
  const [builderRootItem, setBuilderRootItem] = useState<ItemRef | null>(null)
  const [knownEmptyParents, setKnownEmptyParents] = useState<Set<string>>(() => new Set())
  const [createThenAdd, setCreateThenAdd] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    const q = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
    if (search) q.set('search', search)
    if (status) q.set('status', status)
    if (itemType) q.set('itemType', itemType)
    if (parentItemId) q.set('parentItemId', parentItemId)
    try {
      const response = await fetch(`${api}/bom-revisions?${q}`)
      if (!response.ok) throw new Error(await problem(response))
      const body = await response.json() as { items: Bom[]; totalCount: number }
      setRows(body.items)
      setTotal(body.totalCount)
      if (parentItemId) {
        setKnownEmptyParents(current => {
          const next = new Set(current)
          if (body.items.length === 0) next.add(parentItemId)
          else next.delete(parentItemId)
          return next
        })
      }
      if (parentItemId && !selectedId && body.items.length > 0) {
        const preferred = preferredBomRevision(body.items)
        if (preferred) updateQuery({ selected: preferred.id })
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoading(false)
    }
  }, [itemType, parentItemId, query.page, query.pageSize, search, selectedId, status, t, updateQuery])

  const refresh = useCallback(async (id: string) => {
    const response = await fetch(`${api}/bom-revisions/${id}`)
    if (!response.ok) throw new Error(await problem(response))
    const value = await response.json() as Bom
    setDetail(value)
    setTreeDetails(current => ({ ...current, [value.parentItem.id]: value }))
    setKnownEmptyParents(current => {
      const next = new Set(current)
      next.delete(value.parentItem.id)
      return next
    })
    return value
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 100)
    return () => window.clearTimeout(timer)
  }, [load])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (selectedId) void refresh(selectedId).catch(caught => setError(caught instanceof Error ? caught.message : t('common.error')))
      else setDetail(null)
    }, 0)
    return () => window.clearTimeout(timer)
  }, [refresh, selectedId, t])

  useEffect(() => {
    if (!parentItemId) return
    const resource = resourceFor(queryParentType)
    let active = true
    void fetch(`${api}/${resource}/${parentItemId}`).then(async response => {
      if (!response.ok) throw new Error(await problem(response))
      return response.json()
    }).then(value => {
      if (active) setParentContext({ ...value, itemType: queryParentType } as Item)
    }).catch(caught => { if (active) setError(caught instanceof Error ? caught.message : t('common.error')) })
    return () => { active = false }
  }, [parentItemId, queryParentType, t])

  const trail = useMemo(() => parseBomTrail(trailValue), [trailValue])
  const rootNode = rootItemId && rootItemType
    ? { itemId: rootItemId, itemType: rootItemType }
    : parentItemId && queryParentType === 'PRODUCT'
      ? { itemId: parentItemId, itemType: queryParentType }
      : null
  const builderRootRequestId = rootNode?.itemId ?? parentItemId
  const requestedRootRevisionId = rootRevisionId ||
    (builderRootRequestId && builderRootRequestId === parentItemId ? selectedId : '')

  useEffect(() => {
    if (!builderRootRequestId) return
    let active = true
    const q = new URLSearchParams({ rootItemId: builderRootRequestId })
    if (requestedRootRevisionId) q.set('rootRevisionId', requestedRootRevisionId)
    void fetch(`${api}/bom-revisions/builder-tree?${q}`).then(async response => {
      if (!response.ok) throw new Error(await problem(response))
      return response.json() as Promise<BomBuilderTreeResponse>
    }).then(value => {
      if (!active) return
      setBuilderRootItem(value.rootItem)
      setTreeDetails(Object.fromEntries(value.revisions.map(revision => [revision.parentItem.id, revision])))
      setKnownEmptyParents(new Set(value.missingParentItemIds))
    }).catch(caught => { if (active) setError(caught instanceof Error ? caught.message : t('common.error')) })
    return () => { active = false }
  }, [builderRootRequestId, requestedRootRevisionId, t])

  function revealEditor(id: string) {
    window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
      const element = document.getElementById(id)
      element?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      element?.querySelector<HTMLElement>('input, select, textarea, button')?.focus({ preventScroll: true })
    }))
  }

  function openHeaderEditor(value: HeaderEditor) {
    setEditor(value)
    revealEditor('bom-header-editor')
  }

  function openLineEditor(value: LineEditor, line: Line | null = null) {
    setLineError('')
    setLineEditor(value)
    setLineEditing(true)
    setSelectedLine(line)
    revealEditor('bom-line-editor')
  }

  function beginNewRevision(addFirstChild = false) {
    setError('')
    setMessage('')
    setValidation(null)
    setSelectedLine(null)
    setLineEditing(false)
    setCreateThenAdd(addFirstChild)
    openHeaderEditor({ ...emptyHeader, parentItemId })
  }

  async function saveHeader(event: FormEvent) {
    event.preventDefault()
    if (!editor) return
    setError('')
    const payload = {
      baseOutputQty: Number(editor.baseOutputQty),
      revisionNotes: editor.revisionNotes || null,
      ...(editor.id ? { expectedVersion: editor.version } : { parentItemId: editor.parentItemId }),
    }
    try {
      const response = await fetch(`${api}/bom-revisions${editor.id ? `/${editor.id}` : ''}`, {
        method: editor.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload),
      })
      if (!response.ok) throw new Error(await problem(response))
      const saved = await response.json() as Bom
      setEditor(null)
      setValidation(null)
      setMessage(t('bom.draftSaved'))
      setTreeDetails(current => ({ ...current, [saved.parentItem.id]: saved }))
      setKnownEmptyParents(current => {
        const next = new Set(current)
        next.delete(saved.parentItem.id)
        return next
      })
      updateQuery({ parentItemId: saved.parentItem.id, parentType: saved.parentItem.itemType,
        selected: saved.id, rootRevisionId: saved.parentItem.id === rootNode?.itemId ? saved.id : rootRevisionId,
        search: null, status: null, itemType: null, page: 1 })
      await load()
      const refreshed = await refresh(saved.id)
      if (createThenAdd) {
        openLineEditor({ ...emptyLine })
      }
      setCreateThenAdd(false)
      setDetail(refreshed)
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
  }

  async function saveLine(event: FormEvent) {
    event.preventDefault()
    if (!detail) return
    if (!validBomUsageCoefficient(lineEditor.usageQty)) {
      setLineError(t('bom.usagePositiveError'))
      revealEditor('bom-line-editor')
      return
    }
    setError('')
    setLineError('')
    const payload = { usageQty: Number(lineEditor.usageQty), scrapOverridePercent: lineEditor.scrapOverridePercent === '' ? null : Number(lineEditor.scrapOverridePercent), sortOrder: Number(lineEditor.sortOrder), expectedVersion: detail.version, ...(!lineEditor.id ? { childItemId: lineEditor.childItemId } : {}) }
    try {
      const response = await fetch(`${api}/bom-revisions/${detail.id}/lines${lineEditor.id ? `/${lineEditor.id}` : ''}`, { method: lineEditor.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!response.ok) throw new Error(await problem(response))
      const updated = await response.json() as Bom
      setDetail(updated)
      setTreeDetails(current => ({ ...current, [updated.parentItem.id]: updated }))
      setLineEditor({ ...emptyLine })
      setLineEditing(false)
      setLineError('')
      setSelectedLine(null)
      setValidation(null)
      setMessage(t('bom.lineSaved'))
      await load()
    } catch (caught) {
      setLineError(caught instanceof Error ? caught.message : t('common.error'))
      revealEditor('bom-line-editor')
    }
  }

  async function confirmRemoveLine() {
    if (!detail || !removeLine) return
    setError('')
    try {
      const response = await fetch(`${api}/bom-revisions/${detail.id}/lines/${removeLine.id}/remove`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: detail.version, reason: null }) })
      if (!response.ok) throw new Error(await problem(response))
      const updated = await response.json() as Bom
      setDetail(updated)
      setTreeDetails(current => ({ ...current, [updated.parentItem.id]: updated }))
      setRemoveLine(null)
      setSelectedLine(null)
      setValidation(null)
      await load()
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
  }

  async function validateRevision() {
    if (!detail) return
    setError('')
    const response = await fetch(`${api}/bom-revisions/${detail.id}/validation`)
    if (!response.ok) { setError(await problem(response)); return }
    const value = await response.json() as ValidationResult
    setValidation(value)
    setMessage(value.isValidForApproval ? t('bom.validationPassed') : '')
  }

  async function beginApprove() {
    if (!detail) return
    setError('')
    setAction({ kind: 'approve' })
  }

  async function executeAction() {
    if (!action) return
    setError('')
    let response: Response
    if (action.kind === 'copySameParent') response = await fetch(`${api}/bom-revisions/${action.source.id}/copy`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ revisionNotes: `Copy of ${action.source.revisionCode}` }) })
    else if (action.kind === 'copyToProduct') {
      if (!action.targetParentItemId) return
      response = await fetch(`${api}/bom-revisions/${action.source.id}/copy`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ revisionNotes: `Copy of ${action.source.revisionCode}`, targetParentItemId: action.targetParentItemId }) })
    }
    else if (action.kind === 'discardDraft') response = await fetch(`${api}/bom-revisions/${action.source.id}/discard`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: action.source.version, reason: 'USER_DISCARDED_DRAFT' }) })
    else if (action.kind === 'approve') response = await fetch(`${api}/bom-revisions/${detail!.id}/approve`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: detail!.version, reason: null }) })
    else response = await fetch(`${api}/bom-revisions/${detail!.id}/obsolete`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: detail!.version, reason: null }) })
    if (!response.ok) { setError(await problem(response)); return }
    if (action.kind === 'discardDraft') {
      const discardedId = action.source.id
      setAction(null)
      setValidation(null)
      setDetail(null)
      setTreeDetails(current => {
        const next = { ...current }
        delete next[action.source.parentItem.id]
        return next
      })
      setMessage(t('bom.draftDiscarded'))
      updateQuery({ selected: null, rootRevisionId: rootRevisionId === discardedId ? null : rootRevisionId })
      return
    }
    const updated = await response.json() as Bom
    setAction(null)
    setValidation(null)
    setMessage(t('common.saved'))
    if (action.kind === 'copyToProduct') {
      updateQuery({
        parentItemId: updated.parentItem.id,
        parentType: updated.parentItem.itemType,
        rootItemId: updated.parentItem.id,
        rootItemType: updated.parentItem.itemType,
        rootRevisionId: updated.id,
        trail: null,
        selected: updated.id,
        revisionId: null,
        search: null,
        status: null,
        itemType: null,
        page: 1,
      })
      return
    }
    updateQuery({ selected: updated.id,
      rootRevisionId: updated.parentItem.id === rootNode?.itemId ? updated.id : rootRevisionId })
    setDetail(updated)
    setTreeDetails(current => ({ ...current, [updated.parentItem.id]: updated }))
    await load()
  }

  function openTreeNode(item: ItemRef, ancestors: { itemType: string; itemId: string }[]) {
    const root = rootNode ?? (item.itemType === 'PRODUCT' ? { itemType: item.itemType, itemId: item.id } : null)
    const selectedRootRevision = root?.itemId === currentParent?.id ? detail?.id : rootRevisionId
    updateQuery({ parentItemId: item.id, parentType: item.itemType, rootItemId: root?.itemId, rootItemType: root?.itemType, rootRevisionId: selectedRootRevision, trail: serializeBomTrail(ancestors), selected: null, revisionId: null, search: null, status: null, itemType: null, page: 1 })
    setParentContext(null); setDetail(null); setSelectedLine(null); setEditor(null); setLineEditing(false)
  }

  function returnToPreviousParent() {
    const popped = popBomTrail(trailValue)
    if (!popped.previous) return
    updateQuery({ parentItemId: popped.previous.itemId, parentType: popped.previous.itemType, trail: popped.trail, selected: null, revisionId: null, search: null, status: null, itemType: null, page: 1 })
    setParentContext(null); setDetail(null); setSelectedLine(null); setEditor(null)
  }

  function clearParentContext() {
    updateQuery({ parentItemId: null, parentType: null, rootItemId: null, rootItemType: null, rootRevisionId: null, trail: null, selected: null, revisionId: null, page: 1 })
    setParentContext(null); setDetail(null); setEditor(null); setBuilderRootItem(null); setTreeDetails({}); setKnownEmptyParents(new Set())
  }

  function openRevisionFromList(revision: Bom) {
    const isProduct = revision.parentItem.itemType === 'PRODUCT'
    updateQuery({
      parentItemId: revision.parentItem.id,
      parentType: revision.parentItem.itemType,
      rootItemId: isProduct ? revision.parentItem.id : null,
      rootItemType: isProduct ? revision.parentItem.itemType : null,
      rootRevisionId: isProduct ? revision.id : null,
      trail: null,
      selected: revision.id,
      revisionId: null,
      search: null,
      status: null,
      itemType: null,
      page: 1,
    })
    setParentContext(null); setDetail(null); setSelectedLine(null); setEditor(null); setLineEditing(false); setBuilderRootItem(null); setTreeDetails({}); setKnownEmptyParents(new Set())
  }

  const filtered = Boolean(search || status || itemType)
  const currentParent = detail?.parentItem ?? (parentContext ? toItemRef(parentContext) : null)
  const renderedTreeDetails = useMemo(() => detail
    ? { ...treeDetails, [detail.parentItem.id]: detail }
    : treeDetails, [detail, treeDetails])
  const treeRoot = builderRootItem ?? (rootNode
    ? renderedTreeDetails[rootNode.itemId]?.parentItem ?? (currentParent?.id === rootNode.itemId ? currentParent : null)
    : currentParent)
  const lifecycle = bomLifecycleStep(detail?.status, Boolean(validation?.isValidForApproval))
  const issueText = (code: string) => {
    if (code !== 'BASE_UNIT_CHANGED' || !detail) return code
    const mismatch = findBomBaseUnitMismatch(detail)
    if (mismatch?.role === 'PARENT') {
      return t('bom.parentBaseUnitChanged', {
        code: mismatch.code,
        snapshot: localizedName(mismatch.snapshot, i18n.resolvedLanguage),
        current: localizedName(mismatch.current, i18n.resolvedLanguage),
      })
    }
    return mismatch ? t('bom.childBaseUnitChanged', {
      code: mismatch.code,
      snapshot: localizedName(mismatch.snapshot, i18n.resolvedLanguage),
      current: localizedName(mismatch.current, i18n.resolvedLanguage),
    }) : t('bom.baseUnitChangedUnknown')
  }

  return <main className="app-shell bom-builder-page">
    <PageHeader eyebrow={t('bom.builderEyebrow')} title={t('bom.builderTitle')} subtitle={t('bom.treeBuilderSubtitle')} actions={<div className="ui-action-row">{currentParent && trail.length > 0 && <button className="button" onClick={returnToPreviousParent}>{i18n.dir() === 'rtl' ? <ArrowRight aria-hidden="true" /> : <ArrowLeft aria-hidden="true" />}{t('bom.backToParent')}</button>}{currentParent?.itemType === 'PRODUCT' && has('catalog.product.manage') && <Link className="button button--quiet" to={`/mrp/products/${currentParent.id}/edit?from=${encodeURIComponent(`${location.pathname}${location.search}`)}`}><Pencil aria-hidden="true" />{t('bom.editProductDetails')}</Link>}<button className="button button--quiet" onClick={clearParentContext}>{t('bom.changeParent')}</button>{currentParent && <button className="button" onClick={() => beginNewRevision(false)}><Plus aria-hidden="true" />{t('bom.newForParent')}</button>}</div>} />
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}{error && <MessageStrip tone="critical">{error}</MessageStrip>}

    {!currentParent && <>
      {editor && <div id="bom-header-editor" tabIndex={-1}><Section title={editor.id ? t('bom.editRevision') : t('bom.createDraft')} description={t('bom.createDraftHint')}><HeaderForm editor={editor} setEditor={setEditor} parentType={parentType} setParentType={setParentType} fixedParent={false} onSubmit={saveHeader} onCancel={() => { setEditor(null); setCreateThenAdd(false) }} /></Section></div>}
      {!editor && <Section title={t('bom.startWithProduct')} description={t('bom.startWithProductHint')} actions={<button className="button button--primary" onClick={() => beginNewRevision(false)}><Plus aria-hidden="true" />{t('bom.selectAndCreate')}</button>}><div className="bom-start-guide"><ListTree aria-hidden="true" /><div><strong>{t('bom.treeFlowTitle')}</strong><span>{t('bom.treeFlowHint')}</span></div></div></Section>}
      <FilterBar label={t('common.filterBar')}><FormField label={t('common.search')}><input value={search} onChange={event => updateQuery({ search: event.target.value }, true)} /></FormField><FormField label={t('bom.parent')}><select value={itemType} onChange={event => updateQuery({ itemType: event.target.value }, true)}><option value="">{t('common.all')}</option><option value="PRODUCT">{t('products.object')}</option><option value="PART">{t('materials.part')}</option><option value="COMPONENT">{t('materials.component')}</option></select></FormField><FormField label={t('common.status')}><select value={status} onChange={event => updateQuery({ status: event.target.value }, true)}><option value="">{t('common.all')}</option>{['DRAFT', 'APPROVED', 'OBSOLETE'].map(value => <option key={value} value={value}>{t(`bom.${value}`)}</option>)}</select></FormField></FilterBar>
      <section className="ui-table-surface bom-catalog-panel"><TableToolbar title={t('bom.revisions')} count={t('common.count', { count: total })} />{loading ? <LoadingState label={t('common.loading')} compact /> : rows.length === 0 ? <EmptyState title={filtered ? t('common.noResultsTitle') : t('bom.noRevision')} description={t('bom.noRevisionHint')} filtered={filtered} /> : <div className="bom-revision-list bom-revision-list--catalog">{rows.map(row => <button type="button" key={row.id} onClick={() => openRevisionFromList(row)}><span><strong className="ui-technical">{row.revisionCode}</strong><StatusBadge text={t(`bom.${row.status}`)} tone={tone(row.status)} /></span><b>{localizedName(row.parentItem, i18n.resolvedLanguage)}</b><small>{row.parentItem.code} · {t('bom.lineCount', { count: row.lineCount })}</small></button>)}</div>}<Pagination page={query.page} pageSize={query.pageSize} totalCount={total} onPageChange={page => updateQuery({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(total / query.pageSize)), count: total })} /></section>
    </>}

    {currentParent && <>
      {editor && <div id="bom-header-editor" tabIndex={-1}><Section title={editor.id ? t('bom.editRevision') : t('bom.createDraft')} description={createThenAdd ? t('bom.createAndContinueHint') : t('bom.createDraftHint')}><HeaderForm editor={editor} setEditor={setEditor} parentType={parentType} setParentType={setParentType} fixedParent onSubmit={saveHeader} onCancel={() => { setEditor(null); setCreateThenAdd(false) }} /></Section></div>}
      <section className="bom-tree-workbench bom-tree-workbench--visual" aria-label={t('bom.structureWorkspace')}>
        <header className="bom-tree-workbench__header"><span className="bom-node-header__icon"><ListTree aria-hidden="true" /></span><div className="bom-tree-workbench__identity"><small>{t('bom.selectedNode')}</small><h2>{localizedName(currentParent, i18n.resolvedLanguage)}</h2><span><bdi className="ui-technical">{currentParent.code}</bdi> · {t(typeKey(currentParent.itemType))} · {localizedName(currentParent.currentBaseUnit, i18n.resolvedLanguage)}</span></div></header>

        <section className="bom-lifecycle" aria-label={t('bom.lifecycleTitle')}>
          <div className="bom-lifecycle__intro"><div><strong>{t('bom.lifecycleTitle')}</strong><span>{t('bom.lifecycleHint')}</span></div>{detail && <StatusBadge text={t(`bom.${detail.status}`)} tone={tone(detail.status)} />}</div>
          <ol>
            <li className={detail ? 'is-complete' : 'is-current'}><span><GitBranch aria-hidden="true" /></span><div><strong>{t('bom.lifecycleDraft')}</strong><small>{t('bom.lifecycleDraftHint')}</small></div></li>
            <li className={`${lifecycle === 'VALIDATE' || lifecycle === 'APPROVE' ? 'is-complete' : detail?.status === 'DRAFT' ? 'is-current' : ''}`}><span><CheckCircle2 aria-hidden="true" /></span><div><strong>{t('bom.lifecycleValidate')}</strong><small>{t('bom.lifecycleValidateHint')}</small></div></li>
            <li className={`${lifecycle === 'APPROVE' ? 'is-complete' : lifecycle === 'VALIDATE' ? 'is-current' : ''}`}><span><ShieldCheck aria-hidden="true" /></span><div><strong>{t('bom.lifecycleApprove')}</strong><small>{t('bom.lifecycleApproveHint')}</small></div></li>
          </ol>
        </section>

        <div className="bom-revision-commandbar" aria-label={t('bom.revisionControl')}>
          <div className="bom-revision-commandbar__identity">{detail ? <><strong className="ui-technical">{detail.revisionCode}</strong><StatusBadge text={t(`bom.${detail.status}`)} tone={tone(detail.status)} /></> : <StatusBadge text={t('bom.structureMissing')} tone="neutral" />}</div>
          {rows.length > 0 && <select aria-label={t('bom.currentRevision')} value={detail?.id ?? ''} onChange={event => { updateQuery({ selected: event.target.value, rootRevisionId: currentParent.id === rootNode?.itemId ? event.target.value : rootRevisionId }); setSelectedLine(null); setEditor(null); setLineEditing(false); setValidation(null) }}>{rows.map(row => <option value={row.id} key={row.id}>{row.revisionCode} — {t(`bom.${row.status}`)}</option>)}</select>}
          <div className="bom-revision-commandbar__actions">
            {!detail && <button className="button button--primary" type="button" onClick={() => beginNewRevision(true)}><Plus aria-hidden="true" />{t('bom.createFirstDraft')}</button>}
            {detail?.status === 'DRAFT' && <>
              <button className="button button--quiet" type="button" onClick={() => openHeaderEditor(toHeader(detail))}><Pencil aria-hidden="true" />{t('bom.editRevision')}</button>
              <button className="button" type="button" onClick={() => void validateRevision()}><CheckCircle2 aria-hidden="true" />{t('bom.validate')}</button>
              <button className="button button--primary" type="button" disabled={!validation?.isValidForApproval} onClick={() => void beginApprove()}><ShieldCheck aria-hidden="true" />{t('bom.approve')}</button>
              <button className="button button--danger" type="button" onClick={() => setAction({ kind: 'discardDraft', source: detail })}><Trash2 aria-hidden="true" />{t('bom.discardDraft')}</button>
            </>}
            {detail?.status === 'APPROVED' && <>
              <button className="button button--primary" type="button" onClick={() => setAction({ kind: 'copySameParent', source: detail })}><Copy aria-hidden="true" />{t('bom.copySameParent')}</button>
              {detail.parentItem.itemType === 'PRODUCT' && <button className="button button--quiet" type="button" onClick={() => setAction({ kind: 'copyToProduct', source: detail, targetParentItemId: '' })}><Copy aria-hidden="true" />{t('bom.copyToProduct')}</button>}
              <button className="button button--quiet" type="button" onClick={() => setAction({ kind: 'obsolete' })}><CircleOff aria-hidden="true" />{t('bom.obsolete')}</button>
            </>}
            {detail?.status === 'OBSOLETE' && <>
              <button className="button button--primary" type="button" onClick={() => setAction({ kind: 'copySameParent', source: detail })}><Copy aria-hidden="true" />{t('bom.copySameParent')}</button>
              {detail.parentItem.itemType === 'PRODUCT' && <button className="button button--quiet" type="button" onClick={() => setAction({ kind: 'copyToProduct', source: detail, targetParentItemId: '' })}><Copy aria-hidden="true" />{t('bom.copyToProduct')}</button>}
            </>}
          </div>
        </div>

        {detail?.status === 'DRAFT' && !validation?.isValidForApproval && !validation && <MessageStrip tone="information">{t('bom.validateBeforeApprove')}</MessageStrip>}
        {detail?.status === 'APPROVED' && <MessageStrip tone="positive">{t('bom.approvedReadOnlyHint')}</MessageStrip>}

        {currentParent.itemType !== 'PRODUCT' && <MessageStrip tone="warning">{t('bom.sharedParentWarning')}</MessageStrip>}
        {lineEditing && detail?.status === 'DRAFT' && <div id="bom-line-editor" tabIndex={-1} className="bom-inline-editor"><Section title={lineEditor.id ? t('bom.editUsageCoefficient') : t('bom.addChildToCurrent')} description={t('bom.lineFormHint')}><LineForm detail={detail} editor={lineEditor} setEditor={value => { setLineEditor(value); setLineError('') }} error={lineError} onSubmit={saveLine} onCancel={() => { setLineEditor({ ...emptyLine }); setLineEditing(false); setLineError(''); setSelectedLine(null) }} /></Section></div>}
        {loading && !detail ? <LoadingState label={t('common.loading')} /> : <BomVisualTree key={(treeRoot ?? currentParent).id} root={treeRoot ?? currentParent} currentParentId={currentParent.id} currentDetail={detail} selectedLineId={selectedLine?.id ?? null} details={renderedTreeDetails} knownEmptyParents={knownEmptyParents} locale={i18n.resolvedLanguage} t={t} onSelect={openTreeNode} onInspectLine={line => setSelectedLine(current => current?.id === line.id ? null : line)} onAddChild={() => { if (!detail) beginNewRevision(true); else openLineEditor({ ...emptyLine }) }} onEditLine={line => openLineEditor(toLine(line), line)} onRemoveLine={setRemoveLine} />}

        {detail && !validation && [...detail.warningCodes, ...detail.integrityReasonCodes].length > 0 && <MessageStrip tone="warning">{[...detail.warningCodes, ...detail.integrityReasonCodes].map(issueText).join(' · ')}</MessageStrip>}
        {validation && <MessageStrip tone={validation.isValidForApproval ? 'positive' : 'critical'}>{validation.isValidForApproval ? t('bom.validationPassed') : [...validation.blockingCodes, ...validation.warningCodes].map(issueText).join(' · ')}</MessageStrip>}
      </section>
    </>}

    {rootNode?.itemType === 'PRODUCT' && <details className="bom-support-panel"><summary><ListTree aria-hidden="true" />{t('bom.operationalStructure')}<span>{t('bom.operationalStructureHint')}</span></summary><ProductStructure itemId={rootNode.itemId} itemType="PRODUCT" /></details>}

    <ConfirmationDialog open={Boolean(removeLine)} title={t('bom.removeLine')} description={t('bom.removeLineConfirm')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} onConfirm={() => void confirmRemoveLine()} onCancel={() => setRemoveLine(null)} />
    <ConfirmationDialog open={Boolean(action)} title={action ? t(`bom.${action.kind}`) : ''} confirmLabel={action?.kind === 'discardDraft' ? t('bom.discardDraftConfirm') : t('common.confirm')} cancelLabel={t('common.cancel')} tone={action?.kind === 'discardDraft' ? 'critical' : 'normal'} className={action?.kind === 'copyToProduct' ? 'bom-copy-dialog' : undefined} confirmDisabled={action?.kind === 'copyToProduct' && !action.targetParentItemId} onConfirm={() => void executeAction()} onCancel={() => setAction(null)}>
      {action?.kind === 'copySameParent' && <MessageStrip>{t('bom.copyCurrentHint')}</MessageStrip>}
      {action?.kind === 'copyToProduct' && <div className="bom-copy-target-dialog"><MessageStrip>{t('bom.copyToProductHint')}</MessageStrip><ItemPicker itemType="PRODUCT" selectedId={action.targetParentItemId} onSelect={targetParentItemId => setAction({ ...action, targetParentItemId })} label={t('bom.targetProduct')} disabledIds={new Set([action.source.parentItem.id])} /></div>}
      {action?.kind === 'discardDraft' && <MessageStrip tone="warning">{t('bom.discardDraftHint')}</MessageStrip>}
      {action?.kind === 'approve' && <div className="bom-approval-summary"><MessageStrip tone="warning">{t('bom.approveRevisionWarning')}</MessageStrip><p>{t('bom.replaceCurrentHint')}</p></div>}
      {action?.kind === 'obsolete' && <MessageStrip tone="warning">{t('bom.obsoleteCurrentHint')}</MessageStrip>}
    </ConfirmationDialog>
  </main>
}

type BomVisualTreeProps = {
  root: ItemRef
  currentParentId: string
  currentDetail: Bom | null
  selectedLineId: string | null
  details: Record<string, BomTreeRevision>
  knownEmptyParents: Set<string>
  locale?: string
  t: ReturnType<typeof useTranslation>['t']
  onSelect: (item: ItemRef, ancestors: { itemType: string; itemId: string }[]) => void
  onInspectLine: (line: Line) => void
  onAddChild: () => void
  onEditLine: (line: Line) => void
  onRemoveLine: (line: Line) => void
}

function BomVisualTree(props: BomVisualTreeProps) {
  const { root, details, t } = props
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(expandableBomParentIds(Object.values(details))))
  const knownExpandable = useRef(new Set<string>())
  useEffect(() => {
    const available = expandableBomParentIds(Object.values(details))
    const newlyLoaded = available.filter(itemId => !knownExpandable.current.has(itemId))
    knownExpandable.current = new Set(available)
    if (newlyLoaded.length > 0) setExpanded(current => new Set([...current, ...newlyLoaded]))
  }, [details])
  function toggle(itemId: string) { setExpanded(current => { const next = new Set(current); if (next.has(itemId)) next.delete(itemId); else next.add(itemId); return next }) }
  function expandAll() { setExpanded(new Set(expandableBomParentIds(Object.values(details)))) }
  function collapseAll() { setExpanded(new Set()) }
  return <section className="bom-visual-tree" aria-label={t('bom.productTree')}>
    <header className="bom-visual-tree__toolbar"><div><ListTree aria-hidden="true" /><strong>{t('bom.fullStructure')}</strong></div><div><button className="bom-icon-command" type="button" title={t('bom.expandAll')} aria-label={t('bom.expandAll')} onClick={expandAll}><ChevronDown aria-hidden="true" /></button><button className="bom-icon-command" type="button" title={t('bom.collapseAll')} aria-label={t('bom.collapseAll')} onClick={collapseAll}><ChevronRight aria-hidden="true" /></button></div></header>
    <div className="bom-visual-tree__canvas">
      <ul className="bom-visual-tree__root" role="tree"><BomVisualNode {...props} item={root} line={null} parentRevision={null} ancestors={[]} expanded={expanded} onToggle={toggle} /></ul>
    </div>
    <footer>{t('bom.visualTreeHelp')}</footer>
  </section>
}

function BomVisualNode({ item, line, parentRevision, ancestors, expanded, onToggle, ...props }: BomVisualTreeProps & { item: ItemRef; line: Line | null; parentRevision: BomTreeRevision | null; ancestors: { itemType: string; itemId: string }[]; expanded: Set<string>; onToggle: (itemId: string) => void }) {
  const { currentParentId, currentDetail, selectedLineId, details, knownEmptyParents, locale, t, onSelect, onInspectLine, onAddChild, onEditLine, onRemoveLine } = props
  const revision = details[item.id]
  const hasChildren = (revision?.lines.length ?? 0) > 0
  const isExpanded = expanded.has(item.id)
  const isCurrent = item.id === currentParentId
  const isDirectLine = Boolean(line && parentRevision?.parentItem.id === currentParentId)
  const lineEditable = isDirectLine && currentDetail?.status === 'DRAFT'
  const selected = Boolean(line && selectedLineId === line.id)
  const structureState = bomSubstructureState(Boolean(revision), knownEmptyParents.has(item.id))
  const missing = structureState === 'MISSING'
  const nextAncestors = [...ancestors, { itemType: item.itemType, itemId: item.id }]
  const statusText = revision ? t(`bom.${revision.status}`) : missing ? t('bom.structureMissing') : t('bom.notInspected')
  return <li role="treeitem" aria-expanded={hasChildren ? isExpanded : undefined} aria-selected={isCurrent} className={`bom-visual-node${isCurrent ? ' is-current' : ''}${selected ? ' is-selected' : ''}`}>
    <div className="bom-visual-node__card">
      <button type="button" className="bom-visual-node__toggle" disabled={!hasChildren} title={isExpanded ? t('bom.collapseNode') : t('bom.expandNode')} aria-label={isExpanded ? t('bom.collapseNode') : t('bom.expandNode')} onClick={() => onToggle(item.id)}>{hasChildren ? isExpanded ? <ChevronDown aria-hidden="true" /> : locale?.startsWith('fa') ? <ChevronLeft aria-hidden="true" /> : <ChevronRight aria-hidden="true" /> : <span className="bom-visual-node__leaf" />}</button>
      <span className={`bom-item-type-icon is-${item.itemType.toLowerCase()}`}>{item.itemType === 'PRODUCT' ? <Box aria-hidden="true" /> : item.itemType === 'PART' ? <Wrench aria-hidden="true" /> : <CircuitBoard aria-hidden="true" />}</span>
      <button type="button" className="bom-visual-node__identity" onClick={() => onSelect(item, ancestors)}><strong>{localizedName(item, locale)}</strong><span><bdi className="ui-technical">{item.code}</bdi> · {t(typeKey(item.itemType))}</span></button>
      <span className={`bom-visual-node__state is-${revision?.status.toLowerCase() ?? (missing ? 'missing' : 'unknown')}`} title={statusText}><span />{statusText}</span>
      {line && (lineEditable
        ? <button type="button" className="bom-usage-coefficient is-editable" title={t('bom.editUsageCoefficient')} aria-label={`${t('bom.editUsageCoefficient')} ${formatUsageQuantity(line)}`} onClick={() => onEditLine(line)}><b>{formatNumber(line.usageQty)}</b><span className="bom-usage-coefficient__unit">{unitToken(line.childBaseUnitSnapshot)}</span><Pencil aria-hidden="true" /></button>
        : <span className="bom-usage-coefficient" title={t('bom.usageCoefficient')}><b>{formatNumber(line.usageQty)}</b><span className="bom-usage-coefficient__unit">{unitToken(line.childBaseUnitSnapshot)}</span></span>)}
      <div className="bom-visual-node__actions">
        {line && <button className="bom-icon-command" type="button" aria-pressed={selected} title={t('bom.inspectLine')} aria-label={t('bom.inspectLine')} onClick={() => onInspectLine(line)}><Eye aria-hidden="true" /></button>}
        {!isCurrent && <button className="bom-icon-command" type="button" title={missing ? t('bom.defineSubstructure') : t('bom.openSubstructure')} aria-label={missing ? t('bom.defineSubstructure') : t('bom.openSubstructure')} onClick={() => onSelect(item, ancestors)}>{missing ? <Plus aria-hidden="true" /> : <GitBranch aria-hidden="true" />}</button>}
        {isCurrent && !currentDetail && <button className="bom-icon-command is-primary" type="button" title={t('bom.defineSubstructure')} aria-label={t('bom.defineSubstructure')} onClick={onAddChild}><Plus aria-hidden="true" /></button>}
        {isCurrent && currentDetail?.status === 'DRAFT' && <button className="bom-icon-command is-primary" type="button" title={t('bom.addChild')} aria-label={t('bom.addChild')} onClick={onAddChild}><Plus aria-hidden="true" /></button>}
        {lineEditable && <button className="bom-icon-command is-danger" type="button" title={t('bom.removeLine')} aria-label={t('bom.removeLine')} onClick={() => onRemoveLine(line!)}><Trash2 aria-hidden="true" /></button>}
      </div>
    </div>
    {selected && line && <div className="bom-visual-node__details"><span><bdi className="ui-technical">{line.childItem.code}</bdi></span><span>{t('bom.usageCoefficient')}: <bdi className="ui-technical">{formatUsageQuantity(line)}</bdi></span><span>{localizedName(line.childBaseUnitSnapshot, locale)}</span></div>}
    {hasChildren && isExpanded && <ul role="group">{revision!.lines.map(child => <BomVisualNode key={child.id} {...props} item={child.childItem} line={child} parentRevision={revision} ancestors={nextAncestors} expanded={expanded} onToggle={onToggle} />)}</ul>}
  </li>
}

function HeaderForm({ editor, setEditor, parentType, setParentType, fixedParent, onSubmit, onCancel }: { editor: HeaderEditor; setEditor: (value: HeaderEditor) => void; parentType: string; setParentType: (value: string) => void; fixedParent: boolean; onSubmit: (event: FormEvent) => void; onCancel: () => void }) {
  const { t } = useTranslation()
  return <form className="ui-object-form" onSubmit={onSubmit}>{!editor.id && !fixedParent && <div className="ui-form-grid ui-form-grid--2"><FormField label={t('bom.parentType')} required><select value={parentType} onChange={event => { setParentType(event.target.value); setEditor({ ...editor, parentItemId: '' }) }}><option value="PRODUCT">{t('products.object')}</option><option value="PART">{t('materials.part')}</option><option value="COMPONENT">{t('materials.component')}</option></select></FormField><ItemPicker itemType={parentType} selectedId={editor.parentItemId} onSelect={value => setEditor({ ...editor, parentItemId: value })} label={t('bom.selectParent')} /></div>}{editor.id ? <div className="ui-form-grid ui-form-grid--2"><FormField label={t('bom.output')} hint={t('bom.outputHint')} required><input type="number" min="0.000001" step="0.000001" required value={editor.baseOutputQty} onChange={event => setEditor({ ...editor, baseOutputQty: event.target.value })} /></FormField><FormField label={t('common.notes')}><textarea value={editor.revisionNotes} onChange={event => setEditor({ ...editor, revisionNotes: event.target.value })} /></FormField></div> : <div className="bom-draft-default"><GitBranch aria-hidden="true" /><div><strong>{t('bom.draftReady')}</strong><span>{t('bom.draftDefaultHint')}</span></div></div>}<div className="ui-action-row"><button className="button button--primary" disabled={!editor.parentItemId}><Save aria-hidden="true" />{t('bom.saveDraft')}</button><button type="button" className="button" onClick={onCancel}><X aria-hidden="true" />{t('common.cancel')}</button></div></form>
}

function LineForm({ detail, editor, setEditor, error, onSubmit, onCancel }: { detail: Bom; editor: LineEditor; setEditor: (value: LineEditor) => void; error: string; onSubmit: (event: FormEvent) => void; onCancel: () => void }) {
  const { t } = useTranslation(); const childType = childTypeFor(detail.parentItem.itemType)
  const childUnit = editor.childUnit
  const childUnitText = unitToken(childUnit)
  const parentUnitText = unitToken(detail.parentBaseUnitSnapshot)
  const usageLabel = childUnit ? t('bom.usageWithUnit', { unit: childUnitText }) : t('bom.usageCoefficient')
  const usageHint = childUnit
    ? t('bom.usageHintWithBasis', { childUnit: childUnitText, baseOutput: formatNumber(detail.baseOutputQty), parentUnit: parentUnitText })
    : t('bom.usageSelectHint')
  return <form noValidate className={`bom-compact-line-form${editor.id ? ' is-usage-only' : ''}`} onSubmit={onSubmit}>{!editor.id && <ItemPicker itemType={childType} applicableParentId={detail.parentItem.id} selectedId={editor.childItemId} onSelect={(value, item) => setEditor({ ...editor, childItemId: value, childUnit: item.baseUnit })} label={t('bom.selectChildType', { type: t(typeKey(childType)) })} disabledIds={new Set(detail.lines.map(line => line.childItem.id))} />}<div className="bom-usage-input"><bdi className="ui-technical">{childUnitText}</bdi><FormField label={usageLabel} hint={usageHint} error={error || undefined} required><input autoFocus={Boolean(editor.id)} type="number" min="0.000001" step="0.000001" required aria-invalid={Boolean(error)} value={editor.usageQty} onChange={event => setEditor({ ...editor, usageQty: event.target.value })} /></FormField></div><div className="ui-action-row"><button className="button button--primary" disabled={!editor.id && !editor.childItemId}><Save aria-hidden="true" />{t('common.save')}</button><button type="button" className="button" onClick={onCancel}><X aria-hidden="true" />{t('common.cancel')}</button></div></form>
}

function ItemPicker({ itemType, selectedId, onSelect, label, includeInactive = false, disabledIds, applicableParentId }: { itemType: string; selectedId: string; onSelect: (id: string, item: Item) => void; label: string; includeInactive?: boolean; disabledIds?: Set<string>; applicableParentId?: string }) {
  const { t, i18n } = useTranslation(); const { has } = useAuth(); const [rows, setRows] = useState<Item[]>([]); const [search, setSearch] = useState(''); const [page, setPage] = useState(1); const [total, setTotal] = useState(0); const [loading, setLoading] = useState(false); const [loadError, setLoadError] = useState(''); const [showAll, setShowAll] = useState(false); const [outside, setOutside] = useState<Item | null>(null); const resource = resourceFor(itemType); const pageSize = BOM_PICKER_PAGE_SIZE
  useEffect(() => { const timer = window.setTimeout(() => { setLoading(true); setLoadError(''); const q = new URLSearchParams({ page: String(page), pageSize: String(pageSize) }); if (search.trim()) q.set('search', search.trim()); const url = applicableParentId ? applicableItemsUrl(api, applicableParentId, showAll, search, page, pageSize) : `${api}/${resource}?${new URLSearchParams({ ...Object.fromEntries(q), includeInactive: String(includeInactive) })}`; void fetch(url).then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() }).then(body => { setRows(applicableParentId ? body.items.map((entry: { item: ItemRef; isApplicable: boolean }) => ({ ...entry.item, baseUnit: entry.item.currentBaseUnit, isApplicable: entry.isApplicable })) : body.items); setTotal(body.totalCount) }).catch(caught => { setRows([]); setTotal(0); setLoadError(caught instanceof Error ? caught.message : t('common.error')) }).finally(() => setLoading(false)) }, 120); return () => window.clearTimeout(timer) }, [applicableParentId, includeInactive, page, pageSize, resource, search, showAll, t])
  const selected = rows.find(row => row.id === selectedId)
  return <fieldset className="ui-search-picker"><legend>{label}</legend>{selected && <div className="bom-picker-selection"><CheckCircle2 aria-hidden="true" /><strong>{localizedName(selected, i18n.resolvedLanguage)}</strong><bdi className="ui-technical">{selected.code}</bdi></div>}<div className="bom-picker-filter-row"><FormField label={t('sourcing.pickerSearch')}><input value={search} onChange={event => { setSearch(event.target.value); setPage(1) }} /></FormField>{applicableParentId && has('engineering.bom_draft.manage') && <button type="button" className="button button--quiet" aria-pressed={showAll} onClick={() => { setShowAll(value => !value); setPage(1) }}>{showAll ? t('bom.showApplicableOnly') : t('bom.showAllItems')}</button>}</div><div className="ui-search-picker__options">{loading ? <LoadingState label={t('common.loading')} compact /> : loadError ? <MessageStrip tone="critical">{loadError}</MessageStrip> : rows.length === 0 ? <EmptyState title={applicableParentId && !showAll ? t('bom.noApplicableItems') : t('common.noResultsTitle')} compact filtered={Boolean(search)} /> : rows.map(row => { const disabled = disabledIds?.has(row.id) ?? false; return <button type="button" disabled={disabled} className={`${row.id === selectedId ? 'selected' : ''}${row.isApplicable === false ? ' outside-applicability' : ''}`} key={row.id} onClick={() => row.isApplicable === false ? setOutside(row) : onSelect(row.id, row)}><span>{localizedName(row, i18n.resolvedLanguage)}{disabled && <small>{t('bom.alreadyAdded')}</small>}{row.isApplicable === false && <small>{t('bom.outsideApplicability')}</small>}</span><span className="ui-technical">{row.code}</span></button> })}</div><div className="ui-search-picker__pager"><button type="button" className="button button--quiet" disabled={page === 1} onClick={() => setPage(page - 1)}>{t('common.previous')}</button><span>{t('common.page', { page, pages: Math.max(1, Math.ceil(total / pageSize)), count: total })}</span><button type="button" className="button button--quiet" disabled={page * pageSize >= total} onClick={() => setPage(page + 1)}>{t('common.next')}</button></div><ConfirmationDialog open={Boolean(outside)} title={t('bom.registerApplicabilityTitle')} description={t('bom.registerApplicabilityDescription')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} tone="normal" onConfirm={() => { if (outside) onSelect(outside.id, outside); setOutside(null) }} onCancel={() => setOutside(null)} /></fieldset>
}

function resourceFor(itemType: string) { return itemType === 'PRODUCT' ? 'products' : itemType === 'PART' ? 'parts' : 'components' }
function typeKey(itemType: string) { return itemType === 'PRODUCT' ? 'products.object' : itemType === 'PART' ? 'materials.part' : 'materials.component' }
function toHeader(value: Bom): HeaderEditor { return { id: value.id, parentItemId: value.parentItem.id, baseOutputQty: String(value.baseOutputQty), revisionNotes: value.revisionNotes ?? '', version: value.version } }
function toLine(value: Line): LineEditor { return { id: value.id, childItemId: value.childItem.id, childUnit: value.childBaseUnitSnapshot, usageQty: String(value.usageQty), scrapOverridePercent: value.scrapOverridePercent == null ? '' : String(value.scrapOverridePercent), sortOrder: String(value.sortOrder) } }
function toItemRef(value: Item): ItemRef { return { id: value.id, code: value.code, nameFa: value.nameFa, nameEn: value.nameEn, itemType: value.itemType, isActive: value.isActive, currentBaseUnit: value.baseUnit } }
function formatNumber(value: number) { return new Intl.NumberFormat('en-US', { maximumFractionDigits: 6 }).format(value) }
function unitToken(unit?: Unit | null) { return unit?.symbol?.trim() || unit?.code || '—' }
function formatUsageQuantity(line: Pick<Line, 'usageQty' | 'childBaseUnitSnapshot'>) { return `${formatNumber(line.usageQty)} ${unitToken(line.childBaseUnitSnapshot)}` }
async function problem(response: Response) { const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null; return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}` }
