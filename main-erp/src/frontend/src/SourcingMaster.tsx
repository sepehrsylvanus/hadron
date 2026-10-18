import { FormEvent, KeyboardEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { ChevronDown, ChevronLeft, ChevronsDownUp, ChevronsUpDown, Eye, PackageSearch, Pencil, Plus, Star, Truck } from 'lucide-react'
import { useAuth } from './AuthContext'
import { formatBusinessDate } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { changeSourcingRouteScope, isExternalSourcingEligible, sourcingLeadTimeFields, updateSourcingEditor, type SourcingEditor, type SourcingLeadTimeField } from './sourcingFormSupport'
import { readinessReasonKey, selectedOptionAfterReload, toggleExpandedItem } from './sourcingTreeSupport'
import { ConfirmationDialog, Definition, DefinitionGrid, DisplayEditActions, EmptyState, FilterBar, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'
import { currentListContext, useListQuery, useListReturn } from './ui/routeContext'
import { UnsavedChangesGuard } from './ui/UnsavedChangesGuard'

type Ref = { id: string; code: string; nameFa: string; nameEn: string; isActive: boolean }
type Unit = Ref & { symbol?: string | null }
type Item = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; supplyMethod: string; isActive: boolean; baseUnit: Unit }
type Supplier = { id: string; supplierNumber: string; name: string; isActive: boolean }
type Option = { id: string; sourcingNumber: string; item: Item; supplier: Supplier; itemBaseUnit: Unit; routeScope: 'DOMESTIC' | 'IMPORT'; minimumOrderQuantity?: number | null; shippingMode?: Ref | null; incoterm?: Ref | null; supplierLeadTimeDays?: number | null; forwarderToShipmentDays?: number | null; shippingPeriodDays?: number | null; destinationCustomsDays?: number | null; totalConfiguredLeadTimeDays?: number | null; imageUrl?: string | null; isPreferred: boolean; isActive: boolean; version: number; evaluationDate: string; readiness: { state: string; reasonCodes: string[] } }
type ItemGroup = { item: Item; optionCount: number; readyOptionCount: number; preferredOptionId?: string | null; options: Option[] }
type TreeResponse = { items: ItemGroup[]; page: number; pageSize: number; totalItemCount: number; totalOptionCount: number; evaluationDate: string }
type Editor = SourcingEditor
const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const todayTehran = () => { const parts = new Intl.DateTimeFormat('en', { timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date()); const values = Object.fromEntries(parts.map(x => [x.type, x.value])); return `${values.year}-${values.month}-${values.day}` }
const empty: Editor = { itemId: '', supplierId: '', routeScope: 'DOMESTIC', minimumOrderQuantity: '', shippingModeId: '', incotermId: '', supplierLeadTimeDays: '', forwarderToShipmentDays: '', shippingPeriodDays: '', destinationCustomsDays: '', imageUrl: '' }
const listPath = '/mrp/supply/sourcing'
const readinessTone = (state: string) => state === 'READY' ? 'positive' : state === 'BLOCKED' ? 'critical' : state === 'INCOMPLETE_DATA' ? 'warning' : 'neutral'

export default function SourcingMaster() {
  const { t, i18n } = useTranslation()
  const { has } = useAuth()
  const query = useListQuery(20)
  const location = useLocation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const date = query.value('evaluationDate') || todayTehran()
  const search = query.value('search')
  const type = query.value('type')
  const readiness = query.value('readiness')
  const preferred = query.value('preferred')
  const includeInactive = query.flag('includeInactive')
  const itemId = query.value('itemId')
  const supplierId = query.value('supplierId')
  const canManage = has('procurement.sourcing.manage')
  const canSetPreferred = has('procurement.sourcing_preferred.manage')
  const [groups, setGroups] = useState<ItemGroup[]>([])
  const [totalItems, setTotalItems] = useState(0)
  const [totalOptions, setTotalOptions] = useState(0)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [selectedId, setSelectedId] = useState<string>()
  const [preferredTarget, setPreferredTarget] = useState<Option | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    const params = new URLSearchParams({
      evaluationDate: date,
      search,
      includeInactive: String(includeInactive),
      page: String(query.page),
      pageSize: String(query.pageSize),
    })
    if (type) params.set('itemType', type)
    if (readiness) params.set('readiness', readiness)
    if (preferred) params.set('preferred', preferred)
    if (itemId) params.set('itemId', itemId)
    if (supplierId) params.set('supplierId', supplierId)
    try {
      const response = await fetch(`${api}/sourcing-options/tree?${params}`)
      if (!response.ok) throw new Error(await problem(response))
      const body = await response.json() as TreeResponse
      setGroups(body.items)
      setTotalItems(body.totalItemCount)
      setTotalOptions(body.totalOptionCount)
      setExpanded(current => current.size || !body.items.length ? current : new Set([body.items[0].item.id]))
      setSelectedId(current => selectedOptionAfterReload(current, body.items)?.id)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
      setGroups([])
      setTotalItems(0)
      setTotalOptions(0)
    } finally {
      setLoading(false)
    }
  }, [date, includeInactive, itemId, preferred, query.page, query.pageSize, readiness, search, supplierId, t, type])

  useEffect(() => {
    if (!query.params.has('evaluationDate')) query.update({ evaluationDate: date })
  }, [date, query])
  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 150)
    return () => window.clearTimeout(timer)
  }, [load])

  const from = currentListContext(location.pathname, location.search)
  const filtered = Boolean(search || type || readiness || preferred || includeInactive || itemId || supplierId)
  const selected = groups.flatMap(group => group.options).find(option => option.id === selectedId) ?? null
  const allExpanded = groups.length > 0 && groups.every(group => expanded.has(group.item.id))

  async function makePreferred() {
    if (!preferredTarget) return
    const lookupQuery = new URLSearchParams({ evaluationDate: date, itemId: preferredTarget.item.id,
      preferred: 'true', includeInactive: 'false', page: '1', pageSize: '1' })
    try {
      const lookup = await fetch(`${api}/sourcing-options?${lookupQuery}`)
      if (!lookup.ok) throw new Error(await problem(lookup))
      const current = ((await lookup.json()) as { items: Option[] }).items[0]
      const response = await fetch(`${api}/sourcing-options/${preferredTarget.id}/set-preferred?evaluationDate=${date}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
          expectedVersion: preferredTarget.version,
          expectedCurrentPreferredId: current?.id ?? null,
          expectedCurrentPreferredVersion: current?.version ?? null,
        }),
      })
      if (!response.ok) throw new Error(await problem(response))
      setPreferredTarget(null)
      await load()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    }
  }

  function resetFilters() {
    query.update({ search: null, type: null, readiness: null, preferred: null,
      includeInactive: null, itemId: null, supplierId: null }, true)
  }

  return <main className="app-shell sourcing-workbench">
    <PageHeader title={t('sourcing.title')} subtitle={t('sourcing.subtitle')}
      actions={canManage && <Link className="button button--primary" to={`/mrp/supply/sourcing/new?from=${from}&evaluationDate=${date}`}><Plus aria-hidden="true" />{t('sourcing.new')}</Link>} />
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('common.search')}><input value={search} placeholder={t('sourcing.searchPlaceholder')} onChange={event => query.update({ search: event.target.value }, true)} /></FormField>
      <FormField label={t('sourcing.type')}><select value={type} onChange={event => query.update({ type: event.target.value }, true)}><option value="">{t('common.all')}</option><option value="PART">{t('materials.part')}</option><option value="COMPONENT">{t('materials.component')}</option></select></FormField>
      <FormField label={t('sourcing.readiness')}><select value={readiness} onChange={event => query.update({ readiness: event.target.value }, true)}><option value="">{t('common.all')}</option>{['BLOCKED', 'INCOMPLETE_DATA', 'READY', 'INACTIVE'].map(value => <option key={value} value={value}>{t(`sourcing.${value}`)}</option>)}</select></FormField>
      <details className="sourcing-more-filters" open={Boolean(preferred || includeInactive)}>
        <summary>{t('sourcing.moreFilters')}</summary>
        <div className="sourcing-more-filters__content">
          <FormField label={t('sourcing.preferred')}><select value={preferred} onChange={event => query.update({ preferred: event.target.value }, true)}><option value="">{t('common.all')}</option><option value="true">{t('common.yes')}</option><option value="false">{t('common.no')}</option></select></FormField>
          <FormField label={t('sourcing.evaluationDate')}><input className="ui-technical" dir="ltr" type="date" required value={date} onChange={event => query.update({ evaluationDate: event.target.value }, true)} /></FormField>
          <label className="checkbox-label"><input type="checkbox" checked={includeInactive} onChange={event => query.update({ includeInactive: event.target.checked }, true)} />{t('common.includeInactive')}</label>
        </div>
      </details>
      {filtered && <button type="button" className="button button--quiet" onClick={resetFilters}>{t('sourcing.clearFilters')}</button>}
    </FilterBar>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <section className="ui-table-surface sourcing-tree-surface">
      <TableToolbar title={t('sourcing.treeTitle')}
        description={t('sourcing.treeHint')}
        count={t('sourcing.resultCount', { items: totalItems, options: totalOptions })}
        actions={<button type="button" className="button button--quiet" disabled={!groups.length}
          onClick={() => setExpanded(allExpanded ? new Set() : new Set(groups.map(group => group.item.id)))}>
          {allExpanded ? <ChevronsDownUp aria-hidden="true" /> : <ChevronsUpDown aria-hidden="true" />}
          {allExpanded ? t('sourcing.collapseAll') : t('sourcing.expandAll')}
        </button>} />
      {loading ? <LoadingState label={t('common.loading')} /> : !groups.length
        ? <EmptyState filtered={filtered} title={filtered ? t('common.noResultsTitle') : t('common.emptyTitle')}
          description={filtered ? t('common.noResultsDescription') : t('sourcing.emptyDescription')} />
        : <div className="sourcing-tree-layout">
          <div className="sourcing-tree" role="tree" aria-label={t('sourcing.treeTitle')}>
            {groups.map(group => <SourcingTreeGroup key={group.item.id} group={group}
              expanded={expanded.has(group.item.id)} selectedId={selectedId} language={language}
              canManage={canManage} from={from} date={date}
              onToggle={() => setExpanded(current => toggleExpandedItem(current, group.item.id))}
              onSelect={setSelectedId} t={t} />)}
          </div>
          <SourcingDetailPanel option={selected} language={language} canManage={canManage}
            canSetPreferred={canSetPreferred} from={from} date={date} onPreferred={setPreferredTarget} t={t} />
        </div>}
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={totalItems}
        onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(totalItems / query.pageSize)), count: totalItems })} />
    </section>
    <ConfirmationDialog open={Boolean(preferredTarget)} title={t('sourcing.setPreferred')}
      description={preferredTarget ? t('sourcing.setPreferredConfirm', { supplier: preferredTarget.supplier.name, item: localizedName(preferredTarget.item, language) }) : undefined}
      confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} tone="normal"
      onConfirm={() => void makePreferred()} onCancel={() => setPreferredTarget(null)} />
  </main>
}

function SourcingTreeGroup({ group, expanded, selectedId, language, canManage, from, date, onToggle, onSelect, t }:
  { group: ItemGroup; expanded: boolean; selectedId?: string; language: string; canManage: boolean; from: string; date: string; onToggle: () => void; onSelect: (id: string) => void; t: ReturnType<typeof useTranslation>['t'] }) {
  const preferredOption = group.options.find(option => option.id === group.preferredOptionId)
  return <article className="sourcing-tree__group">
    <div className="sourcing-tree__item-row" role="treeitem" aria-level={1} aria-expanded={expanded}>
      <button type="button" className="sourcing-tree__toggle" onClick={onToggle} aria-label={expanded ? t('sourcing.collapseItem') : t('sourcing.expandItem')}>
        {expanded ? <ChevronDown aria-hidden="true" /> : <ChevronLeft aria-hidden="true" />}
      </button>
      <PackageSearch aria-hidden="true" className="sourcing-tree__item-icon" />
      <button type="button" className="sourcing-tree__identity" onClick={onToggle}>
        <strong>{localizedName(group.item, language)}</strong>
        <span><bdi>{group.item.code}</bdi> · {t(group.item.itemType === 'PART' ? 'materials.part' : 'materials.component')} · {group.item.baseUnit.symbol ?? group.item.baseUnit.code}</span>
      </button>
      <div className="sourcing-tree__summary">
        <span>{t('sourcing.optionCount', { count: group.optionCount })}</span>
        <span>{t('sourcing.readyCount', { count: group.readyOptionCount })}</span>
        {preferredOption && <span className="sourcing-tree__preferred-summary"><Star aria-hidden="true" />{preferredOption.supplier.name}</span>}
      </div>
      {canManage && <Link className="button button--icon" title={t('sourcing.addForItem')} aria-label={t('sourcing.addForItem')}
        to={`/mrp/supply/sourcing/new?from=${from}&evaluationDate=${date}&itemId=${group.item.id}&itemType=${group.item.itemType}`}>
        <Plus aria-hidden="true" />
      </Link>}
    </div>
    {expanded && <div className="sourcing-tree__children" role="group">
      <div className="sourcing-tree__columns" aria-hidden="true">
        <span>{t('sourcing.supplierAndSource')}</span><span>MOQ</span><span>{t('sourcing.route')}</span><span>{t('sourcing.totalLead')}</span><span>{t('sourcing.readiness')}</span><span>{t('common.actions')}</span>
      </div>
      {group.options.map(option => <SourcingOptionRow key={option.id} option={option} selected={selectedId === option.id}
        language={language} canManage={canManage} from={from} date={date} onSelect={() => onSelect(option.id)} t={t} />)}
    </div>}
  </article>
}

function SourcingOptionRow({ option, selected, language, canManage, from, date, onSelect, t }:
  { option: Option; selected: boolean; language: string; canManage: boolean; from: string; date: string; onSelect: () => void; t: ReturnType<typeof useTranslation>['t'] }) {
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect() }
  }
  const route = [t(`sourcing.scope.${option.routeScope}`), option.shippingMode && localizedName(option.shippingMode, language), option.routeScope === 'IMPORT' && option.incoterm?.code].filter(Boolean).join(' · ') || '—'
  return <div className={`sourcing-tree__option-row${selected ? ' is-selected' : ''}${!option.isActive ? ' is-inactive' : ''}`}
    role="treeitem" aria-level={2} aria-selected={selected} tabIndex={0} onClick={onSelect} onKeyDown={onKeyDown}>
    <div className="sourcing-tree__source"><span className="sourcing-tree__supplier"><Truck aria-hidden="true" />{option.supplier.name}{option.isPreferred && <Star aria-label={t('sourcing.preferred')} />}</span><small><bdi>{option.sourcingNumber}</bdi> · <bdi>{option.supplier.supplierNumber}</bdi></small></div>
    <div className="ui-number">{formatOperationalNumber(option.minimumOrderQuantity, language)} <bdi>{option.itemBaseUnit.symbol ?? option.itemBaseUnit.code}</bdi></div>
    <div>{route}</div>
    <div className="ui-number">{option.totalConfiguredLeadTimeDays == null ? '—' : t('sourcing.daysValue', { count: option.totalConfiguredLeadTimeDays })}</div>
    <div><StatusBadge text={t(`sourcing.${option.readiness.state}`)} tone={readinessTone(option.readiness.state)} /></div>
    <div className="sourcing-tree__row-actions" onClick={event => event.stopPropagation()}>
      <Link className="button button--quiet" to={`/mrp/supply/sourcing/${option.id}?from=${from}&evaluationDate=${date}`}><Eye aria-hidden="true" />{t('common.details')}</Link>
      {canManage && <Link className="button button--quiet" to={`/mrp/supply/sourcing/${option.id}/edit?from=${from}&evaluationDate=${date}`}><Pencil aria-hidden="true" />{t('common.edit')}</Link>}
    </div>
  </div>
}

function SourcingDetailPanel({ option, language, canManage, canSetPreferred, from, date, onPreferred, t }:
  { option: Option | null; language: string; canManage: boolean; canSetPreferred: boolean; from: string; date: string; onPreferred: (option: Option) => void; t: ReturnType<typeof useTranslation>['t'] }) {
  if (!option) return <aside className="sourcing-detail"><EmptyState title={t('sourcing.selectSource')} description={t('sourcing.selectSourceHint')} /></aside>
  const stageLabels: Record<SourcingLeadTimeField, string> = {
    supplierLeadTimeDays: t('sourcing.supplierLead'),
    forwarderToShipmentDays: t('sourcing.forwarderLead'),
    shippingPeriodDays: option.routeScope === 'DOMESTIC' ? t('sourcing.domesticShippingLead') : t('sourcing.shippingLead'),
    destinationCustomsDays: t('sourcing.destinationLead'),
  }
  const stages = sourcingLeadTimeFields(option.routeScope).map(field => [stageLabels[field], option[field]] as const)
  return <aside className="sourcing-detail" aria-label={t('sourcing.sourceDetail')} aria-live="polite">
    <header className="sourcing-detail__header">
      <div><span>{t('sourcing.option')}</span><h3><bdi>{option.sourcingNumber}</bdi></h3><p>{option.supplier.name}</p></div>
      <StatusBadge text={t(`sourcing.${option.readiness.state}`)} tone={readinessTone(option.readiness.state)} />
    </header>
    {option.isPreferred && <div className="sourcing-detail__preferred"><Star aria-hidden="true" />{t('sourcing.preferredForItem')}</div>}
    <dl className="sourcing-detail__facts">
      <div><dt>{t('sourcing.item')}</dt><dd>{localizedName(option.item, language)}<small><bdi>{option.item.code}</bdi></small></dd></div>
      <div><dt>{t('sourcing.supplier')}</dt><dd>{option.supplier.name}<small><bdi>{option.supplier.supplierNumber}</bdi></small></dd></div>
      <div><dt>{t('sourcing.routeScope')}</dt><dd>{t(`sourcing.scope.${option.routeScope}`)}</dd></div>
      <div><dt>MOQ</dt><dd>{formatOperationalNumber(option.minimumOrderQuantity, language)} <bdi>{option.itemBaseUnit.symbol ?? option.itemBaseUnit.code}</bdi></dd></div>
      <div><dt>{t('sourcing.shippingMode')}</dt><dd>{option.shippingMode ? localizedName(option.shippingMode, language) : '—'}</dd></div>
      {option.routeScope === 'IMPORT' && <div><dt>{t('sourcing.incoterm')}</dt><dd>{option.incoterm?.code ?? '—'}</dd></div>}
      <div><dt>{t('sourcing.evaluationDate')}</dt><dd>{formatBusinessDate(option.evaluationDate, language)}</dd></div>
    </dl>
    <section className="sourcing-route-timeline">
      <header><h4>{t('sourcing.routeTimeline')}</h4><strong>{option.totalConfiguredLeadTimeDays == null ? '—' : t('sourcing.daysValue', { count: option.totalConfiguredLeadTimeDays })}</strong></header>
      <ol>{stages.map(([label, value], index) => <li key={label}><span>{index + 1}</span><div><strong>{label}</strong><small>{value == null ? t('common.notSet') : t('sourcing.daysValue', { count: value })}</small></div></li>)}</ol>
    </section>
    <section className="sourcing-detail__readiness">
      <h4>{t('sourcing.readiness')}</h4>
      {option.readiness.reasonCodes.length
        ? <ul>{option.readiness.reasonCodes.map(code => <li key={code}>{t(readinessReasonKey(code), { code })}</li>)}</ul>
        : <p>{t('sourcing.readyHint')}</p>}
    </section>
    <div className="sourcing-detail__actions">
      <Link className="button" to={`/mrp/supply/sourcing/${option.id}?from=${from}&evaluationDate=${date}`}><Eye aria-hidden="true" />{t('common.details')}</Link>
      {canManage && <Link className="button" to={`/mrp/supply/sourcing/${option.id}/edit?from=${from}&evaluationDate=${date}`}><Pencil aria-hidden="true" />{t('common.edit')}</Link>}
      {canSetPreferred && option.isActive && !option.isPreferred && <button type="button" className="button" onClick={() => onPreferred(option)}><Star aria-hidden="true" />{t('sourcing.setPreferred')}</button>}
    </div>
  </aside>
}

export function SourcingObjectPage() {
  const { t, i18n } = useTranslation(); const english = i18n.resolvedLanguage === 'en'; const { has } = useAuth(); const { id } = useParams(); const location = useLocation(); const navigate = useNavigate(); const back = useListReturn(listPath); const editing = !id || location.pathname.endsWith('/edit'); const pageParams = new URLSearchParams(location.search); const date = pageParams.get('evaluationDate') || todayTehran(); const presetItemId = pageParams.get('itemId') ?? ''; const presetItemType = pageParams.get('itemType') === 'COMPONENT' ? 'COMPONENT' : 'PART'; const canManage = has('procurement.sourcing.manage'); const canSetPreferred = has('procurement.sourcing_preferred.manage')
  const [option, setOption] = useState<Option | null>(null); const [editor, setEditor] = useState<Editor>({ ...empty }); const [initial, setInitial] = useState(''); const [shipping, setShipping] = useState<Ref[]>([]); const [incoterms, setIncoterms] = useState<Ref[]>([]); const [itemType, setItemType] = useState('PART'); const [selectedItem, setSelectedItem] = useState<Item | null>(null); const [blockedItem, setBlockedItem] = useState<Item | null>(null); const [selectedSupplier, setSelectedSupplier] = useState<Supplier | null>(null); const [loading, setLoading] = useState(Boolean(id)); const [saving, setSaving] = useState(false); const [error, setError] = useState(''); const [confirmState, setConfirmState] = useState(false); const [confirmPreferred, setConfirmPreferred] = useState(false); const [discard, setDiscard] = useState(false)
  const dirty = editing && JSON.stringify(editor) !== initial
  const load = useCallback(async () => {
    setLoading(Boolean(id || presetItemId)); setError(''); setBlockedItem(null)
    try {
      if (!id) {
        const next = { ...empty }
        setItemType(presetItemType); setSelectedItem(null)
        if (presetItemId) {
          const response = await fetch(`${api}/${presetItemType === 'COMPONENT' ? 'components' : 'parts'}/${presetItemId}`)
          if (!response.ok) throw new Error(await problem(response))
          const item = await response.json() as Item
          if (isExternalSourcingEligible(item.supplyMethod)) { next.itemId = item.id; setSelectedItem(item) }
          else setBlockedItem(item)
        }
        setEditor(next); setInitial(JSON.stringify(next)); return
      }
      const response = await fetch(`${api}/sourcing-options/${id}?evaluationDate=${date}`)
      if (!response.ok) throw new Error(await problem(response))
      const value = await response.json() as Option
      setOption(value); setSelectedItem(value.item); setSelectedSupplier(value.supplier); setItemType(value.item.itemType)
      const next = toEditor(value); setEditor(next); setInitial(JSON.stringify(next))
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
    finally { setLoading(false) }
  }, [date, id, presetItemId, presetItemType, t])
  useEffect(() => { void Promise.all([fetch(`${api}/reference-data/types/SHIPPING_MODE/values?includeInactive=true`).then(r => r.json()), fetch(`${api}/reference-data/types/INCOTERM/values?includeInactive=true`).then(r => r.json())]).then(([s, i]) => { setShipping(s); setIncoterms(i) }).catch(() => setError(t('common.error'))) }, [t]); useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load])
  async function save(event: FormEvent) { event.preventDefault(); if (!canManage) return; setSaving(true); setError(''); const imported = editor.routeScope === 'IMPORT'; const payload = { routeScope: editor.routeScope, imageUrl: editor.imageUrl.trim(), minimumOrderQuantity: num(editor.minimumOrderQuantity), shippingModeId: editor.shippingModeId || null, incotermId: imported ? editor.incotermId || null : null, supplierLeadTimeDays: num(editor.supplierLeadTimeDays), forwarderToShipmentDays: imported ? num(editor.forwarderToShipmentDays) : null, shippingPeriodDays: num(editor.shippingPeriodDays), destinationCustomsDays: imported ? num(editor.destinationCustomsDays) : null, ...(id ? { expectedVersion: editor.version } : { itemId: editor.itemId, supplierId: editor.supplierId }) }; try { const response = await fetch(`${api}/sourcing-options${id ? `/${id}` : ''}?evaluationDate=${date}`, { method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (!response.ok) throw new Error(await problem(response)); const saved = await response.json() as Option; const next = toEditor(saved); setOption(saved); setSelectedItem(saved.item); setSelectedSupplier(saved.supplier); setItemType(saved.item.itemType); setEditor(next); setInitial(JSON.stringify(next)); navigate(`/mrp/supply/sourcing/${saved.id}?from=${encodeURIComponent(back)}&evaluationDate=${date}`, { replace: true }) } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setSaving(false) } }
  async function changeState() { if (!option) return; try { const response = await fetch(`${api}/sourcing-options/${option.id}/${option.isActive ? 'deactivate' : 'reactivate'}?evaluationDate=${date}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: option.version }) }); if (!response.ok) throw new Error(await problem(response)); setConfirmState(false); await load() } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } }
  async function makePreferred() { if (!option) return; const lookupQuery = new URLSearchParams({ evaluationDate: date, itemId: option.item.id, preferred: 'true', includeInactive: 'false', page: '1', pageSize: '1' }); try { const lookup = await fetch(`${api}/sourcing-options?${lookupQuery}`); if (!lookup.ok) throw new Error(await problem(lookup)); const current = ((await lookup.json()) as { items: Option[] }).items[0]; const response = await fetch(`${api}/sourcing-options/${option.id}/set-preferred?evaluationDate=${date}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: option.version, expectedCurrentPreferredId: current?.id ?? null, expectedCurrentPreferredVersion: current?.version ?? null }) }); if (!response.ok) throw new Error(await problem(response)); setConfirmPreferred(false); await load() } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } }
  function cancel() { if (dirty) setDiscard(true); else navigate(id ? `/mrp/supply/sourcing/${id}?from=${encodeURIComponent(back)}&evaluationDate=${date}` : back) }
  if (loading) return <main className="app-shell"><LoadingState label={t('common.loading')} /></main>
  return <main className="app-shell"><PageHeader sticky title={option?.sourcingNumber ?? t('sourcing.new')} subtitle={`${t('sourcing.evaluationDate')}: ${formatBusinessDate(date, i18n.resolvedLanguage ?? i18n.language)}`} backTo={back} backLabel={t('common.backToList')} status={option && !editing && <StatusBadge text={t(`sourcing.${option.readiness.state}`)} tone={readinessTone(option.readiness.state)} />} actions={<>{canManage && <DisplayEditActions editing={editing} dirty={dirty} editLabel={t('common.edit')} saveLabel={t('common.save')} cancelLabel={t('common.cancel')} formId="sourcing-form" saving={saving} saveDisabled={!id && (!editor.itemId || !editor.supplierId)} onEdit={() => navigate(`/mrp/supply/sourcing/${id}/edit?from=${encodeURIComponent(back)}&evaluationDate=${date}`)} onCancel={cancel} />}{option && !editing && canSetPreferred && !option.isPreferred && option.isActive && <button className="button" onClick={() => setConfirmPreferred(true)}>{t('sourcing.setPreferred')}</button>}{option && !editing && canManage && <button className="button button--quiet" onClick={() => setConfirmState(true)}>{option.isActive ? t('common.deactivate') : t('common.activate')}</button>}</>} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {!option && <MessageStrip tone="information">{t('sourcing.externalBoundaryHint')}</MessageStrip>}
    {blockedItem && <MessageStrip tone="warning"><div className="sourcing-boundary-guidance"><strong>{t('sourcing.makeItemSelected', { code: blockedItem.code })}</strong><p>{t('sourcing.makeItemGuidance')}</p><div className="inline-actions"><Link className="button button--primary" to="/production/routing">{t('sourcing.openManufacturingRoute')}</Link><Link className="button" to={`/mrp/${blockedItem.itemType === 'COMPONENT' ? 'components' : 'parts'}/${blockedItem.id}/edit?from=${encodeURIComponent(location.pathname + location.search)}`}>{t('sourcing.editSupplyMethod')}</Link></div></div></MessageStrip>}
    <form id="sourcing-form" className="ui-object-form" onSubmit={event => void save(event)}><Section title={t('sourcing.relationship')}><div className="ui-form-grid">{editing && !option ? <><FormField label={t('sourcing.type')} required><select value={itemType} onChange={event => { setItemType(event.target.value); setSelectedItem(null); setBlockedItem(null); setError(''); setEditor(current => updateSourcingEditor(current, { itemId: '' })) }}><option value="PART">{t('materials.part')}</option><option value="COMPONENT">{t('materials.component')}</option></select></FormField><ServerPicker kind={itemType === 'PART' ? 'parts' : 'components'} label={t('sourcing.item')} selectedId={editor.itemId} selectedLabel={selectedItem ? localizedName(selectedItem, i18n.resolvedLanguage) : undefined} onSelect={(value, label) => { setEditor(current => updateSourcingEditor(current, { itemId: value })); setSelectedItem(label as Item); setBlockedItem(null); setError('') }} onIncompatible={item => { setEditor(current => updateSourcingEditor(current, { itemId: '' })); setSelectedItem(null); setBlockedItem(item); setError('') }} activeOnly externalOnly /><ServerPicker kind="suppliers" label={t('sourcing.supplier')} selectedId={editor.supplierId} selectedLabel={selectedSupplier?.name} onSelect={(value, label) => { setEditor(current => updateSourcingEditor(current, { supplierId: value })); setSelectedSupplier(label as Supplier); setError('') }} activeOnly /></> : <DefinitionGrid><Definition label={t('sourcing.item')}>{option && localizedName(option.item, i18n.resolvedLanguage)}<small className="cell-sub ui-technical">{option?.item.code}</small></Definition><Definition label={t('sourcing.supplier')}>{option?.supplier.name}</Definition><Definition label={t('common.unit')}>{option && localizedName(option.itemBaseUnit, i18n.resolvedLanguage)}</Definition></DefinitionGrid>}</div>{editing && <div className="ui-form-grid"><FormField className="span-2" label={t('sourcing.imageUrl')} hint={t('sourcing.imageUrlHint')}><input type="url" dir="ltr" maxLength={2048} disabled={!canManage} value={editor.imageUrl} placeholder="https://" onChange={event => setEditor(current => updateSourcingEditor(current, { imageUrl: event.target.value }))} /></FormField></div>}</Section>
      <Section title={t('sourcing.commercial')}>{editing ? <div className="ui-form-grid ui-form-grid--3"><FormField label={t('sourcing.routeScope')} required><select value={editor.routeScope} onChange={event => setEditor(current => changeSourcingRouteScope(current, event.target.value as Editor['routeScope']))}><option value="DOMESTIC">{t('sourcing.scope.DOMESTIC')}</option><option value="IMPORT">{t('sourcing.scope.IMPORT')}</option></select></FormField><TextNumber label={t('sourcing.moq')} value={editor.minimumOrderQuantity} disabled={false} set={value => setEditor(current => updateSourcingEditor(current, { minimumOrderQuantity: value }))} /><FormField label={t('sourcing.shippingMode')}><select value={editor.shippingModeId} onChange={event => setEditor(current => updateSourcingEditor(current, { shippingModeId: event.target.value }))}><option value="">{t('common.notSet')}</option>{shipping.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>{editor.routeScope === 'IMPORT' && <FormField label={t('sourcing.incoterm')}><select value={editor.incotermId} onChange={event => setEditor(current => updateSourcingEditor(current, { incotermId: event.target.value }))}><option value="">{t('common.notSet')}</option>{incoterms.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>}</div> : option && <DefinitionGrid><Definition label={t('sourcing.routeScope')}>{t(`sourcing.scope.${option.routeScope}`)}</Definition><Definition label={t('sourcing.moq')} technical>{formatOperationalNumber(option.minimumOrderQuantity, i18n.resolvedLanguage)} {option.itemBaseUnit.symbol ?? option.itemBaseUnit.code}</Definition><Definition label={t('sourcing.shippingMode')}>{option.shippingMode ? localizedName(option.shippingMode, i18n.resolvedLanguage) : t('common.notSet')}</Definition>{option.routeScope === 'IMPORT' && <Definition label={t('sourcing.incoterm')} technical>{option.incoterm?.code ?? t('common.notSet')}</Definition>}</DefinitionGrid>}</Section>
      <Section title={t('sourcing.route')}>{editing ? <div className="ui-form-grid ui-form-grid--3"><TextNumber label={t('sourcing.supplierLead')} integer value={editor.supplierLeadTimeDays} disabled={false} set={value => setEditor(current => updateSourcingEditor(current, { supplierLeadTimeDays: value }))} />{editor.routeScope === 'IMPORT' && <TextNumber label={t('sourcing.forwarderLead')} integer value={editor.forwarderToShipmentDays} disabled={false} set={value => setEditor(current => updateSourcingEditor(current, { forwarderToShipmentDays: value }))} />}<TextNumber label={editor.routeScope === 'DOMESTIC' ? t('sourcing.domesticShippingLead') : t('sourcing.shippingLead')} integer value={editor.shippingPeriodDays} disabled={false} set={value => setEditor(current => updateSourcingEditor(current, { shippingPeriodDays: value }))} />{editor.routeScope === 'IMPORT' && <TextNumber label={t('sourcing.destinationLead')} integer value={editor.destinationCustomsDays} disabled={false} set={value => setEditor(current => updateSourcingEditor(current, { destinationCustomsDays: value }))} />}</div> : option && <DefinitionGrid><Definition label={t('sourcing.supplierLead')} technical>{leadTime(option.supplierLeadTimeDays, t)}</Definition>{option.routeScope === 'IMPORT' && <Definition label={t('sourcing.forwarderLead')} technical>{leadTime(option.forwarderToShipmentDays, t)}</Definition>}<Definition label={option.routeScope === 'DOMESTIC' ? t('sourcing.domesticShippingLead') : t('sourcing.shippingLead')} technical>{leadTime(option.shippingPeriodDays, t)}</Definition>{option.routeScope === 'IMPORT' && <Definition label={t('sourcing.destinationLead')} technical>{leadTime(option.destinationCustomsDays, t)}</Definition>}<Definition label={t('sourcing.totalLead')} technical>{leadTime(option.totalConfiguredLeadTimeDays, t)}</Definition></DefinitionGrid>}</Section>
    </form>
    {option && <Section title={t('sourcing.readiness')}>{editing ? <MessageStrip tone="information">{t('sourcing.readinessAfterSave')}</MessageStrip> : <><DefinitionGrid><Definition label={t('common.status')}><StatusBadge text={t(`sourcing.${option.readiness.state}`)} tone={readinessTone(option.readiness.state)} /></Definition><Definition label={t('sourcing.evaluationDate')}>{formatBusinessDate(option.evaluationDate, i18n.resolvedLanguage ?? i18n.language)}</Definition><Definition label={t('common.version')} technical>{option.version}</Definition></DefinitionGrid>{option.readiness.reasonCodes.length > 0 && <MessageStrip tone={option.readiness.state === 'BLOCKED' ? 'critical' : 'warning'}><ul className="sourcing-readiness-list">{option.readiness.reasonCodes.map(code => <li key={code}>{t(readinessReasonKey(code), { code })}</li>)}</ul></MessageStrip>}</>}</Section>}
    <ConfirmationDialog open={confirmState} title={t('common.stateConfirmTitle')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} onConfirm={() => void changeState()} onCancel={() => setConfirmState(false)} /><ConfirmationDialog open={confirmPreferred} title={t('sourcing.setPreferred')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} tone="normal" onConfirm={() => void makePreferred()} onCancel={() => setConfirmPreferred(false)} /><ConfirmationDialog open={discard} title={t('common.discardTitle')} description={t('common.discardDescription')} confirmLabel={t('common.discard')} cancelLabel={t('common.cancel')} onConfirm={() => navigate(id ? `/mrp/supply/sourcing/${id}?from=${encodeURIComponent(back)}&evaluationDate=${date}` : back)} onCancel={() => setDiscard(false)} /><UnsavedChangesGuard when={dirty} />
  </main>
}

function ServerPicker({ kind, label, selectedId, selectedLabel, onSelect, onIncompatible, activeOnly, externalOnly }: { kind: 'parts' | 'components' | 'suppliers'; label: string; selectedId: string; selectedLabel?: string; onSelect: (id: string, value: Item | Supplier) => void; onIncompatible?: (item: Item) => void; activeOnly?: boolean; externalOnly?: boolean }) {
  const { t, i18n } = useTranslation(); const [search, setSearch] = useState(''); const [page, setPage] = useState(1); const [rows, setRows] = useState<(Item | Supplier)[]>([]); const [total, setTotal] = useState(0); const pageSize = 10
  useEffect(() => { const timer = window.setTimeout(() => { const q = new URLSearchParams({ search, page: String(page), pageSize: String(pageSize), includeInactive: String(!activeOnly) }); void fetch(`${api}/${kind}?${q}`).then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() }).then(body => { setRows(body.items); setTotal(body.totalCount) }).catch(() => { setRows([]); setTotal(0) }) }, 100); return () => window.clearTimeout(timer) }, [activeOnly, kind, page, search])
  return <fieldset className="ui-search-picker"><legend>{label}</legend><FormField label={t('sourcing.pickerSearch')}><input value={search} onChange={event => { setSearch(event.target.value); setPage(1) }} /></FormField>{selectedId && selectedLabel && <StatusBadge text={selectedLabel} tone="information" />}<div className="ui-search-picker__options">{rows.map(row => { const supplier = kind === 'suppliers'; const item = supplier ? null : row as Item; const incompatible = Boolean(item && externalOnly && !isExternalSourcingEligible(item.supplyMethod)); const display = supplier ? (row as Supplier).name : `${localizedName(item!, i18n.resolvedLanguage)} — ${item!.code}`; return <button type="button" className={`${row.id === selectedId ? 'selected' : ''}${incompatible ? ' sourcing-picker__incompatible' : ''}`} key={row.id} onClick={() => incompatible ? onIncompatible?.(item!) : onSelect(row.id, row)}><span>{display}</span>{item && <small>{incompatible ? t('sourcing.makeRouteRequired') : t(`materials.${item.supplyMethod}`)}</small>}</button> })}</div><div className="ui-search-picker__pager"><button type="button" className="button button--quiet" disabled={page === 1} onClick={() => setPage(page - 1)}>{t('common.previous')}</button><span>{t('common.page', { page, pages: Math.max(1, Math.ceil(total / pageSize)), count: total })}</span><button type="button" className="button button--quiet" disabled={page * pageSize >= total} onClick={() => setPage(page + 1)}>{t('common.next')}</button></div></fieldset>
}

function TextNumber({ label, value, set, integer, required, disabled }: { label: string; value: string; set: (value: string) => void; integer?: boolean; required?: boolean; disabled: boolean }) { return <FormField label={label} required={required}><input className="ui-technical" type="number" min="0" step={integer ? '1' : '0.000001'} required={required} disabled={disabled} value={value} onChange={event => set(event.target.value)} /></FormField> }
function num(value: string) { return value === '' ? null : Number(value) }
function text(value?: number | null) { return value == null ? '' : String(value) }
function leadTime(value: number | null | undefined, t: ReturnType<typeof useTranslation>['t']) { return value == null ? '—' : t('sourcing.daysValue', { count: value }) }
function toEditor(value: Option): Editor { return { itemId: value.item.id, supplierId: value.supplier.id, routeScope: value.routeScope, minimumOrderQuantity: text(value.minimumOrderQuantity), shippingModeId: value.shippingMode?.id ?? '', incotermId: value.incoterm?.id ?? '', supplierLeadTimeDays: text(value.supplierLeadTimeDays), forwarderToShipmentDays: text(value.forwarderToShipmentDays), shippingPeriodDays: text(value.shippingPeriodDays), destinationCustomsDays: text(value.destinationCustomsDays), imageUrl: value.imageUrl ?? '', version: value.version } }
async function problem(response: Response) { const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null; return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}` }
