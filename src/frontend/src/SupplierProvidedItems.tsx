import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Boxes, CalendarRange, GitBranch, History, Star, Truck } from 'lucide-react'
import { formatBusinessDate } from './i18n/businessDate'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { localizedName } from './i18n/localizedName'
import { structureItemRoute, structureTypeKey } from './productStructureSupport'
import { tehranTodayIso } from './cashFlowUi'
import { EmptyState, LoadingState, MessageStrip, Section, StatusBadge } from './ui'

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

/**
 * One supplied item with its sourcing history: an item counts as currently
 * supplied while at least one of its sourcing options is active; when every
 * option has been deactivated it becomes supply history (previously
 * produced/supplied for us). Periods come from the real option timestamps.
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
}
type SupplierItemsPage = { items: SupplierItemGroup[]; totalItemCount: number }
type SupplierProvidedItem = SuppliedItem & { imageUrl: string }
type WhereUsedItem = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; isActive: boolean; currentBaseUnit: UnitRef }
type UsageRelationship = { usageQty: number; childBaseUnitSnapshot: UnitRef }
type WhereUsedParent = { parentItem: WhereUsedItem; revision: { revisionCode: string }; relationship: UsageRelationship }
type WhereUsedResponse = { directParents: WhereUsedParent[] }
type UsageTreeNode = { item: SuppliedItem | WhereUsedItem; revisionCode?: string; relationship?: UsageRelationship; children: UsageTreeNode[] }
type UsageStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const pageSize = 100
const picsumImageUrl = (itemId: string) => `https://picsum.photos/seed/${encodeURIComponent(itemId)}/640/360`

export default function SupplierProvidedItems({ supplierId, canViewBom }: { supplierId: string; canViewBom: boolean }) {
  const { t } = useTranslation()
  const [records, setRecords] = useState<ProvidedItemRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [view, setView] = useState<'current' | 'history'>('current')

  useEffect(() => {
    let active = true
    async function load() {
      setLoading(true)
      setError('')
      try {
        // includeInactive=true so deactivated sourcing options (the real
        // supply history) arrive next to the currently active ones.
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
        if (active) setRecords(loaded)
      } catch (caught) {
        if (active) setError(caught instanceof Error ? caught.message : t('common.error'))
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [supplierId, t])

  const currentItems = records.filter(record => record.state === 'current')
  const description = [t('suppliers.providedItemsHint'), ...(canViewBom ? [t('suppliers.providedItemsHoverHint')] : [])].join(' ')
  const toggle = records.length > 0 ? <div className="supplier-provided-toggle" role="tablist" aria-label={t('suppliers.providedHistory.viewLabel')}>
    <button type="button" role="tab" aria-selected={view === 'current'} className={`supplier-provided-toggle__button${view === 'current' ? ' is-active' : ''}`} onClick={() => setView('current')}>
      <span className="supplier-provided-toggle__dot is-live" aria-hidden="true" />{t('suppliers.providedHistory.current')}<span className="supplier-provided-toggle__count">{currentItems.length}</span>
    </button>
    <button type="button" role="tab" aria-selected={view === 'history'} className={`supplier-provided-toggle__button${view === 'history' ? ' is-active' : ''}`} onClick={() => setView('history')}>
      <History aria-hidden="true" />{t('suppliers.providedHistory.history')}<span className="supplier-provided-toggle__count">{records.length}</span>
    </button>
  </div> : undefined

  return <Section title={t('suppliers.providedItems')} description={description} actions={toggle}>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {loading ? <LoadingState label={t('common.loading')} compact /> : view === 'history'
      ? <SupplierSupplyHistory records={records} />
      : currentItems.length === 0
        ? <EmptyState title={t('suppliers.noProvidedItems')} compact />
        : <ul className="supplier-provided-items">{currentItems.map(record => <SupplierProvidedItem key={record.item.id} item={{ ...record.item, imageUrl: record.imageUrl }} canViewBom={canViewBom} />)}</ul>}
  </Section>
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

/** Whole calendar months between two ISO dates (never negative). */
function supplyDurationMonths(fromIso: string, toIso: string) {
  const [fromYear, fromMonth, fromDay] = fromIso.split('-').map(Number)
  const [toYear, toMonth, toDay] = toIso.split('-').map(Number)
  let months = (toYear - fromYear) * 12 + (toMonth - fromMonth)
  if (toDay < fromDay) months -= 1
  return Math.max(0, months)
}

function SupplierSupplyHistory({ records }: { records: ProvidedItemRecord[] }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const today = tehranTodayIso()
  const ordered = [...records].sort((left, right) => {
    if (left.state !== right.state) return left.state === 'current' ? -1 : 1
    const leftKey = left.until ?? left.since ?? ''
    const rightKey = right.until ?? right.since ?? ''
    return rightKey.localeCompare(leftKey)
  })
  const currentCount = records.filter(record => record.state === 'current').length
  const pastCount = records.length - currentCount
  const oldestSince = records.map(record => record.since).filter((value): value is string => Boolean(value)).sort()[0]

  function durationLabel(record: ProvidedItemRecord) {
    if (!record.since) return null
    const months = supplyDurationMonths(record.since, record.until ?? today)
    if (months <= 0) return t('suppliers.providedHistory.durationLessThanMonth')
    const years = Math.floor(months / 12)
    const rest = months % 12
    return [
      years > 0 ? t('suppliers.providedHistory.durationYears', { count: years }) : '',
      rest > 0 ? t('suppliers.providedHistory.durationMonths', { count: rest }) : '',
    ].filter(Boolean).join(' ')
  }

  return <div className="supplier-supply-history">
    <div className="supplier-supply-history__stats">
      <div className="supplier-supply-stat"><span className="supplier-supply-stat__dot is-live" aria-hidden="true" /><span>{t('suppliers.providedHistory.currentCount', { count: currentCount })}</span></div>
      <div className="supplier-supply-stat"><History aria-hidden="true" /><span>{t('suppliers.providedHistory.pastCount', { count: pastCount })}</span></div>
      {oldestSince && <div className="supplier-supply-stat"><CalendarRange aria-hidden="true" /><span>{t('suppliers.providedHistory.collaborationSince', { date: formatBusinessDate(oldestSince, language) })}</span></div>}
    </div>
    {ordered.length === 0
      ? <EmptyState title={t('suppliers.providedHistory.emptyHistory')} compact />
      : <ol className="supplier-supply-timeline">
        {ordered.map(record => <li key={record.item.id} className={`supplier-supply-entry is-${record.state}`}>
          <span className="supplier-supply-entry__node" aria-hidden="true" />
          <article className="supplier-supply-card">
            <img className="supplier-supply-card__image" src={record.imageUrl} alt="" loading="lazy" decoding="async" referrerPolicy="no-referrer"
              onError={event => { const fallback = picsumImageUrl(record.item.id); if (event.currentTarget.src !== fallback) event.currentTarget.src = fallback }} />
            <div className="supplier-supply-card__main">
              <div className="supplier-supply-card__title">
                <strong>{localizedName(record.item, language)}</strong>
                <bdi className="ui-technical">{record.item.code}</bdi>
                <span className={`supplier-supply-state is-${record.state}`}>{t(record.state === 'current' ? 'suppliers.providedHistory.ongoing' : 'suppliers.providedHistory.ended')}</span>
              </div>
              <span className="supplier-supply-card__meta">{t(structureTypeKey(record.item.itemType))} · {record.item.baseUnit.symbol ?? record.item.baseUnit.code}{!record.item.isActive ? ` · ${t('common.inactive')}` : ''}</span>
              <div className="supplier-supply-card__chips">
                {record.since && <span className="supplier-supply-chip is-period"><CalendarRange aria-hidden="true" />{formatBusinessDate(record.since, language)} ← {record.until ? formatBusinessDate(record.until, language) : t('suppliers.providedHistory.present')}</span>}
                {durationLabel(record) && <span className="supplier-supply-chip"><History aria-hidden="true" />{durationLabel(record)}</span>}
                {record.state === 'current' && record.leadTimeDays != null && <span className="supplier-supply-chip"><Truck aria-hidden="true" />{t('suppliers.providedHistory.leadTime', { count: record.leadTimeDays })}</span>}
                {record.preferred && <span className="supplier-supply-chip is-preferred"><Star aria-hidden="true" />{t('suppliers.providedHistory.preferredOption')}</span>}
                {record.sourcingNumbers.slice(0, 2).map(number => <span key={number} className="supplier-supply-chip ui-technical">{number}</span>)}
                {record.sourcingNumbers.length > 2 && <span className="supplier-supply-chip ui-technical">+{record.sourcingNumbers.length - 2}</span>}
              </div>
            </div>
          </article>
        </li>)}
      </ol>}
  </div>
}

function SupplierProvidedItem({ item, canViewBom }: { item: SupplierProvidedItem; canViewBom: boolean }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const [tree, setTree] = useState<UsageTreeNode | null>(null)
  const [status, setStatus] = useState<UsageStatus>('idle')
  const [open, setOpen] = useState(false)
  const [error, setError] = useState('')
  const popoverId = `supplier-usage-${item.id}`
  const statusRef = useRef<UsageStatus>('idle')
  const pointerOrFocusInside = useRef(false)
  const mounted = useRef(true)

  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])

  function showUsage() {
    if (!canViewBom || statusRef.current === 'empty') return
    pointerOrFocusInside.current = true
    setOpen(true)
    if (statusRef.current !== 'idle') return
    statusRef.current = 'loading'
    setStatus('loading')
    void loadUsageTree(item).then(value => {
      if (!mounted.current) return
      statusRef.current = value ? 'ready' : 'empty'
      setStatus(statusRef.current)
      setTree(value)
      if (!value) {
        pointerOrFocusInside.current = false
        setOpen(false)
      } else if (pointerOrFocusInside.current) setOpen(true)
    }).catch(caught => {
      if (!mounted.current) return
      statusRef.current = 'error'
      setStatus('error')
      setError(caught instanceof Error ? caught.message : t('common.error'))
      if (pointerOrFocusInside.current) setOpen(true)
    })
  }

  function hideUsage() {
    pointerOrFocusInside.current = false
    setOpen(false)
  }

  return <li className="supplier-provided-item" onMouseEnter={showUsage} onMouseLeave={event => {
    const focused = typeof document !== 'undefined' && event.currentTarget.contains(document.activeElement)
    if (!focused) hideUsage()
  }} onFocusCapture={showUsage} onBlurCapture={event => {
    const next = event.relatedTarget
    if (!(next instanceof Node) || !event.currentTarget.contains(next)) hideUsage()
  }}>
    <button type="button" className={`supplier-provided-item__trigger${canViewBom ? ' supplier-provided-item__trigger--interactive' : ''}`}
      aria-expanded={canViewBom ? open : undefined} aria-controls={canViewBom && open ? popoverId : undefined} onClick={showUsage}>
      <img className="supplier-provided-item__image" src={item.imageUrl} alt="" loading="lazy" decoding="async" referrerPolicy="no-referrer"
        onError={event => { const fallback = picsumImageUrl(item.id); if (event.currentTarget.src !== fallback) event.currentTarget.src = fallback }} />
      <span className="supplier-provided-item__content">
        <span className="supplier-provided-item__title"><strong>{localizedName(item, language)}</strong><bdi className="ui-technical">{item.code}</bdi></span>
        <span className="supplier-provided-item__metadata">{t(structureTypeKey(item.itemType))} · {item.baseUnit.symbol ?? item.baseUnit.code}</span>
        {!item.isActive && <StatusBadge text={t('common.inactive')} tone="warning" />}
      </span>
    </button>
    {canViewBom && open && <div id={popoverId} className="supplier-item-usage-popover" role="region" aria-label={t('productStructure.whereUsed')}>
      <h3>{t('productStructure.whereUsed')}</h3>
      {status === 'loading' && <LoadingState label={t('common.loading')} compact />}
      {status === 'error' && <MessageStrip tone="critical">{error}</MessageStrip>}
      {status === 'ready' && tree && <ul className="supplier-usage-tree"><UsageTreeBranch node={tree} language={language} t={t} /></ul>}
    </div>}
  </li>
}

function UsageTreeBranch({ node, language, t }: { node: UsageTreeNode; language: string; t: ReturnType<typeof useTranslation>['t'] }) {
  const route = structureItemRoute(node.item.itemType, node.item.id)
  const name = localizedName(node.item, language)
  const unit = node.relationship?.childBaseUnitSnapshot
  return <li className="supplier-usage-tree__branch">
    <div className="supplier-usage-tree__node">
      <span className="supplier-usage-tree__icon" aria-hidden="true">{node.children.length ? <GitBranch /> : <Boxes />}</span>
      <div className="supplier-usage-tree__identity">
        <strong>{route ? <Link to={route}>{name}</Link> : name}</strong>
        <span><bdi className="ui-technical">{node.item.code}</bdi> · {t(structureTypeKey(node.item.itemType))}</span>
        {node.revisionCode && <small><bdi>{node.revisionCode}</bdi></small>}
        {node.relationship && <small>{t('productStructure.usage')}: {formatOperationalNumber(node.relationship.usageQty, language)} {unit?.symbol ?? unit?.code}</small>}
      </div>
    </div>
    {node.children.length > 0 && <ul className="supplier-usage-tree__children">{node.children.map((child, index) =>
      <UsageTreeBranch key={`${node.item.id}-${child.item.id}-${index}`} node={child} language={language} t={t} />)}</ul>}
  </li>
}

async function loadUsageTree(root: SuppliedItem): Promise<UsageTreeNode | null> {
  const cache = new Map<string, Promise<WhereUsedResponse>>()
  const whereUsed = (itemId: string) => {
    const existing = cache.get(itemId)
    if (existing) return existing
    const query = new URLSearchParams({ itemId })
    const request = fetch(`${api}/bom-revisions/where-used?${query}`).then(async response => {
      if (!response.ok) throw new Error(await problem(response))
      return await response.json() as WhereUsedResponse
    })
    cache.set(itemId, request)
    return request
  }

  const rootResponse = await whereUsed(root.id)
  if (!rootResponse.directParents.length) return null

  async function expand(item: SuppliedItem | WhereUsedItem, via: WhereUsedParent | undefined, ancestors: Set<string>): Promise<UsageTreeNode> {
    const path = new Set(ancestors)
    path.add(item.id)
    const parentRows = item.itemType === 'PRODUCT' ? [] : (await whereUsed(item.id)).directParents
    const children = await Promise.all(parentRows.map(parent => path.has(parent.parentItem.id)
      ? Promise.resolve({ item: parent.parentItem, revisionCode: parent.revision.revisionCode, relationship: parent.relationship, children: [] })
      : expand(parent.parentItem, parent, path)))
    return { item, revisionCode: via?.revision.revisionCode, relationship: via?.relationship, children }
  }

  return expand(root, undefined, new Set())
}

async function problem(response: Response) {
  const body = await response.json().catch(() => null) as { detail?: string; code?: string } | null
  return [body?.code, body?.detail].filter(Boolean).join(' — ') || `HTTP ${response.status}`
}
