import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowDown, ArrowUp, CheckCircle2, Factory, Pencil, Plus, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useAuth } from './AuthContext'
import { localizedName } from './i18n/localizedName'
import { ConfirmationDialog, DataTable, FormField, MessageStrip, PageHeader, Section, StatusBadge } from './ui'

type Unit = { id: string; code: string; nameFa: string; nameEn?: string | null; symbol?: string | null }
type Item = { id: string; code: string; itemType: string; nameFa: string; nameEn?: string | null; baseUnit: Unit }
type UnitRef = { id: string; nameFa: string; nameEn?: string | null; isActive: boolean }
type WorkCenter = { id: string; code: string; nameFa: string; nameEn?: string | null; organizationalUnit: UnitRef; workingWeekMask: number; isActive: boolean; version: number }
type Operation = { id?: string; sequence: number; workCenter: WorkCenter; preparationWorkingDays: number; executionWorkingDays: number; notes?: string | null }
type Route = { id: string; code: string; item: Item; revisionNumber: number; status: 'DRAFT' | 'APPROVED' | 'OBSOLETE'; notes?: string | null; totalWorkingDays: number; version: number; operations: Operation[] }
type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
type OperationEditor = { key: string; workCenterId: string; preparationWorkingDays: string; executionWorkingDays: string; notes: string }

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const defaultWorkingWeekMask = [6, 0, 1, 2, 3, 4].reduce((mask, day) => mask | (1 << day), 0)

export default function ProductionRoutingPage() {
  const { t, i18n } = useTranslation()
  const { has } = useAuth()
  const [tab, setTab] = useState<'routes' | 'centers'>('routes')
  const [centers, setCenters] = useState<WorkCenter[]>([])
  const [routes, setRoutes] = useState<Route[]>([])
  const [units, setUnits] = useState<UnitRef[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [centerEditor, setCenterEditor] = useState<WorkCenter | 'new' | null>(null)
  const [routeEditor, setRouteEditor] = useState<Route | 'new' | null>(null)
  const [approveRoute, setApproveRoute] = useState<Route | null>(null)
  const display = useCallback((value: { nameFa: string; nameEn?: string | null }) =>
    localizedName(value, i18n.resolvedLanguage), [i18n.resolvedLanguage])

  const load = useCallback(async () => {
    setLoading(true); setError('')
    try {
      const [workCenters, manufacturingRoutes, organizationalUnits] = await Promise.all([
        get<Page<WorkCenter>>('/production-work-centers?includeInactive=true&page=1&pageSize=100'),
        get<Page<Route>>('/manufacturing-routes?page=1&pageSize=100'),
        get<UnitRef[]>('/production-work-centers/organizational-unit-candidates'),
      ])
      setCenters(workCenters.items); setRoutes(manufacturingRoutes.items); setUnits(organizationalUnits)
    } catch (reason) { setError(problemMessage(reason, t('common.error'))) }
    finally { setLoading(false) }
  }, [t])
  useEffect(() => {
    const timer = window.setTimeout(() => { void load() }, 0)
    return () => window.clearTimeout(timer)
  }, [load])

  async function approve() {
    if (!approveRoute) return
    try {
      await send(`/manufacturing-routes/${approveRoute.id}/approve`, 'POST', {
        expectedVersion: approveRoute.version,
        reason: t('productionRouting.approvalReason'),
      })
      setApproveRoute(null); setNotice(t('productionRouting.approved')); await load()
    } catch (reason) { setError(problemMessage(reason, t('common.error'))) }
  }

  return <main className="app-shell production-routing-page">
    <PageHeader title={t('productionRouting.title')} subtitle={t('productionRouting.subtitle')}
      actions={<div className="segmented-control" role="tablist" aria-label={t('productionRouting.title')}>
        <button type="button" role="tab" aria-selected={tab === 'routes'} className={tab === 'routes' ? 'active' : ''} onClick={() => setTab('routes')}>{t('productionRouting.routes')}</button>
        <button type="button" role="tab" aria-selected={tab === 'centers'} className={tab === 'centers' ? 'active' : ''} onClick={() => setTab('centers')}>{t('productionRouting.workCenters')}</button>
      </div>} />
    <MessageStrip tone="information">{t('productionRouting.architectureHint')}</MessageStrip>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {notice && <MessageStrip tone="positive">{notice}</MessageStrip>}
    {tab === 'routes' ? <Section title={t('productionRouting.routeWorklist')} description={t('productionRouting.routeHint')}
      actions={has('production.routing.manage') && centers.some(x => x.isActive) ? <button className="button button--primary" type="button" onClick={() => setRouteEditor('new')}><Plus />{t('productionRouting.newRoute')}</button> : undefined}>
      {!centers.some(x => x.isActive) && !loading && <MessageStrip tone="warning">{t('productionRouting.centerRequired')}</MessageStrip>}
      <DataTable headers={[t('common.code'), t('productionRouting.item'), t('productionRouting.revision'), t('productionRouting.operationCount'), t('productionRouting.totalLead'), t('common.status'), t('common.actions')]}
        loading={loading} empty={!routes.length} loadingLabel={t('common.loading')} emptyTitle={t('productionRouting.noRoutes')} noResultTitle={t('common.noResultsTitle')}>
        {routes.map(route => <tr key={route.id}>
          <td className="technical-value">{route.code}</td>
          <td><strong>{route.item.code}</strong><small className="table-secondary">{display(route.item)}</small></td>
          <td className="technical-value">{route.revisionNumber}</td><td>{route.operations.length}</td>
          <td>{t('productionRouting.workingDays', { count: route.totalWorkingDays })}</td>
          <td><StatusBadge text={t(`productionRouting.status.${route.status}`)} tone={route.status === 'APPROVED' ? 'positive' : route.status === 'DRAFT' ? 'information' : 'neutral'} /></td>
          <td><div className="inline-actions">
            {route.status === 'DRAFT' && has('production.routing.manage') && <button className="button button--compact" type="button" onClick={() => setRouteEditor(route)}><Pencil />{t('common.edit')}</button>}
            {route.status === 'DRAFT' && has('production.routing.approve') && <button className="button button--primary button--compact" type="button" onClick={() => setApproveRoute(route)}><CheckCircle2 />{t('common.approve')}</button>}
          </div></td>
        </tr>)}
      </DataTable>
    </Section> : <Section title={t('productionRouting.centerWorklist')} description={t('productionRouting.centerHint')}
      actions={has('production.work_center.manage') ? <button className="button button--primary" type="button" onClick={() => setCenterEditor('new')}><Plus />{t('productionRouting.newCenter')}</button> : undefined}>
      <DataTable headers={[t('common.code'), t('common.name'), t('productionRouting.department'), t('productionRouting.workingWeek'), t('common.status'), t('common.actions')]}
        loading={loading} empty={!centers.length} loadingLabel={t('common.loading')} emptyTitle={t('productionRouting.noCenters')} noResultTitle={t('common.noResultsTitle')}>
        {centers.map(center => <tr key={center.id}><td className="technical-value">{center.code}</td><td>{display(center)}</td><td>{display(center.organizationalUnit)}</td><td>{workingDaysLabel(center.workingWeekMask, t)}</td><td><StatusBadge text={center.isActive ? t('common.active') : t('common.inactive')} tone={center.isActive ? 'positive' : 'neutral'} /></td><td>{has('production.work_center.manage') && <button className="button button--compact" type="button" onClick={() => setCenterEditor(center)}><Pencil />{t('common.edit')}</button>}</td></tr>)}
      </DataTable>
    </Section>}
    {centerEditor && <CenterDialog key={centerEditor === 'new' ? 'new-center' : centerEditor.id} value={centerEditor} units={units} onClose={() => setCenterEditor(null)} onSaved={async () => { setCenterEditor(null); setNotice(t('common.saved')); await load() }} onError={setError} />}
    {routeEditor && <RouteDialog key={routeEditor === 'new' ? 'new-route' : routeEditor.id} value={routeEditor} centers={centers.filter(x => x.isActive)} onClose={() => setRouteEditor(null)} onSaved={async () => { setRouteEditor(null); setNotice(t('common.saved')); await load() }} onError={setError} />}
    <ConfirmationDialog open={Boolean(approveRoute)} title={t('productionRouting.approveTitle')} description={t('productionRouting.approveHint')} confirmLabel={t('common.approve')} cancelLabel={t('common.cancel')} tone="normal" onConfirm={() => void approve()} onCancel={() => setApproveRoute(null)} />
  </main>
}

function CenterDialog({ value, units, onClose, onSaved, onError }: { value: WorkCenter | 'new'; units: UnitRef[]; onClose: () => void; onSaved: () => Promise<void>; onError: (value: string) => void }) {
  const { t } = useTranslation(); const editing = value !== 'new'
  const [nameFa, setNameFa] = useState(editing ? value.nameFa : '')
  const [nameEn, setNameEn] = useState(editing ? value.nameEn ?? '' : '')
  const [unitId, setUnitId] = useState(editing ? value.organizationalUnit.id : units[0]?.id ?? '')
  const [mask, setMask] = useState(editing ? value.workingWeekMask : defaultWorkingWeekMask)
  async function submit(event: FormEvent) { event.preventDefault(); try { await send(editing ? `/production-work-centers/${value.id}` : '/production-work-centers', editing ? 'PUT' : 'POST', { nameFa, nameEn: nameEn || null, organizationalUnitId: unitId, workingWeekMask: mask, ...(editing ? { expectedVersion: value.version } : {}) }); await onSaved() } catch (reason) { onError(problemMessage(reason, t('common.error'))) } }
  return <ConfirmationDialog open={Boolean(value)} title={editing ? t('productionRouting.editCenter') : t('productionRouting.newCenter')} confirmLabel={t('common.save')} cancelLabel={t('common.cancel')} tone="normal" confirmDisabled={!nameFa.trim() || !unitId || mask === 0} onConfirm={() => (document.getElementById('work-center-form') as HTMLFormElement | null)?.requestSubmit()} onCancel={onClose}>
    <form id="work-center-form" className="ui-form-grid" onSubmit={e => void submit(e)}>
      <FormField label={t('common.nameFa')} required><input autoFocus required value={nameFa} onChange={e => setNameFa(e.target.value)} /></FormField>
      <FormField label={t('common.nameEn')}><input value={nameEn} onChange={e => setNameEn(e.target.value)} /></FormField>
      <FormField label={t('productionRouting.department')} required><select required value={unitId} onChange={e => setUnitId(e.target.value)}><option value="">{t('common.notSet')}</option>{units.map(unit => <option value={unit.id} key={unit.id}>{unit.nameFa}</option>)}</select></FormField>
      <fieldset className="production-working-week span-2"><legend>{t('productionRouting.workingWeek')}</legend>{[6, 0, 1, 2, 3, 4, 5].map(day => <label key={day}><input type="checkbox" checked={(mask & (1 << day)) !== 0} onChange={e => setMask(e.target.checked ? mask | (1 << day) : mask & ~(1 << day))} />{t(`productionRouting.weekday.${day}`)}</label>)}</fieldset>
    </form>
  </ConfirmationDialog>
}

function RouteDialog({ value, centers, onClose, onSaved, onError }: { value: Route | 'new'; centers: WorkCenter[]; onClose: () => void; onSaved: () => Promise<void>; onError: (value: string) => void }) {
  const { t, i18n } = useTranslation(); const editing = value !== 'new'
  const [items, setItems] = useState<Item[]>(editing ? [value.item] : [])
  const [search, setSearch] = useState('')
  const [itemId, setItemId] = useState(editing ? value.item.id : '')
  const [notes, setNotes] = useState(editing ? value.notes ?? '' : '')
  const [operations, setOperations] = useState<OperationEditor[]>(editing ? value.operations.map(toEditor) : [blankOperation(centers[0]?.id)])
  const display = (item: Item) => localizedName(item, i18n.resolvedLanguage)
  useEffect(() => { if (editing) return; const timer = window.setTimeout(() => { void get<Page<Item>>(`/manufacturing-routes/item-candidates?search=${encodeURIComponent(search)}&page=1&pageSize=25`).then(page => setItems(page.items)).catch(reason => onError(problemMessage(reason, t('common.error')))) }, 150); return () => clearTimeout(timer) }, [editing, onError, search, t])
  const total = useMemo(() => operations.reduce((sum, row) => sum + Number(row.preparationWorkingDays || 0) + Number(row.executionWorkingDays || 0), 0), [operations])
  function update(index: number, patch: Partial<OperationEditor>) { setOperations(rows => rows.map((row, rowIndex) => rowIndex === index ? { ...row, ...patch } : row)) }
  function move(index: number, delta: number) { setOperations(rows => { const next = [...rows]; const target = index + delta; if (target < 0 || target >= next.length) return rows; [next[index], next[target]] = [next[target], next[index]]; return next }) }
  async function submit(event: FormEvent) { event.preventDefault(); try { const body = { itemId, notes: notes || null, operations: operations.map((row, index) => ({ sequence: (index + 1) * 10, workCenterId: row.workCenterId, preparationWorkingDays: Number(row.preparationWorkingDays), executionWorkingDays: Number(row.executionWorkingDays), notes: row.notes || null })), ...(editing ? { expectedVersion: value.version } : {}) }; await send(editing ? `/manufacturing-routes/${value.id}` : '/manufacturing-routes', editing ? 'PUT' : 'POST', body); await onSaved() } catch (reason) { onError(problemMessage(reason, t('common.error'))) } }
  const valid = Boolean(itemId && operations.length && operations.every(row => row.workCenterId && Number(row.preparationWorkingDays) >= 0 && Number(row.executionWorkingDays) >= 0 && Number(row.preparationWorkingDays) + Number(row.executionWorkingDays) > 0))
  return <ConfirmationDialog open={Boolean(value)} title={editing ? `${t('productionRouting.editRoute')} — ${value.code}` : t('productionRouting.newRoute')} description={t('productionRouting.editorHint')} confirmLabel={t('common.saveDraft')} cancelLabel={t('common.cancel')} tone="normal" className="production-routing-dialog" confirmDisabled={!valid} onConfirm={() => (document.getElementById('manufacturing-route-form') as HTMLFormElement | null)?.requestSubmit()} onCancel={onClose}>
    <form id="manufacturing-route-form" onSubmit={e => void submit(e)}>
      {!editing && <FormField label={t('productionRouting.searchItem')}><input autoFocus value={search} onChange={e => setSearch(e.target.value)} /></FormField>}
      <FormField label={t('productionRouting.item')} required><select disabled={editing} required value={itemId} onChange={e => setItemId(e.target.value)}><option value="">{t('common.notSet')}</option>{items.map(item => <option key={item.id} value={item.id}>{item.code} — {display(item)}</option>)}</select></FormField>
      <div className="production-route-summary"><Factory /><span>{t('productionRouting.routeTotal')}</span><strong>{t('productionRouting.workingDays', { count: total })}</strong></div>
      <div className="production-operation-list">
        {operations.map((row, index) => <section className="production-operation" key={row.key}><header><strong>{t('productionRouting.operationNumber', { number: index + 1 })}</strong><div className="inline-actions"><button type="button" className="icon-button" aria-label={t('productionRouting.moveUp')} disabled={index === 0} onClick={() => move(index, -1)}><ArrowUp /></button><button type="button" className="icon-button" aria-label={t('productionRouting.moveDown')} disabled={index === operations.length - 1} onClick={() => move(index, 1)}><ArrowDown /></button><button type="button" className="icon-button icon-button--danger" aria-label={t('common.delete')} disabled={operations.length === 1} onClick={() => setOperations(rows => rows.filter((_, rowIndex) => rowIndex !== index))}><Trash2 /></button></div></header><div className="ui-form-grid ui-form-grid--3"><FormField label={t('productionRouting.workCenter')} required><select required value={row.workCenterId} onChange={e => update(index, { workCenterId: e.target.value })}><option value="">{t('common.notSet')}</option>{centers.map(center => <option value={center.id} key={center.id}>{center.code} — {localizedName(center, i18n.resolvedLanguage)}</option>)}</select></FormField><FormField label={t('productionRouting.preparationDays')} required><input type="number" min="0" step="1" value={row.preparationWorkingDays} onChange={e => update(index, { preparationWorkingDays: e.target.value })} /></FormField><FormField label={t('productionRouting.executionDays')} required><input type="number" min="0" step="1" value={row.executionWorkingDays} onChange={e => update(index, { executionWorkingDays: e.target.value })} /></FormField><FormField className="span-2" label={t('common.notes')}><input value={row.notes} onChange={e => update(index, { notes: e.target.value })} /></FormField></div></section>)}
      </div>
      <button className="button" type="button" onClick={() => setOperations(rows => [...rows, blankOperation(centers[0]?.id)])}><Plus />{t('productionRouting.addOperation')}</button>
      <FormField label={t('common.notes')}><textarea value={notes} onChange={e => setNotes(e.target.value)} /></FormField>
    </form>
  </ConfirmationDialog>
}

function blankOperation(workCenterId = ''): OperationEditor { return { key: crypto.randomUUID(), workCenterId, preparationWorkingDays: '', executionWorkingDays: '', notes: '' } }
function toEditor(value: Operation): OperationEditor { return { key: value.id ?? crypto.randomUUID(), workCenterId: value.workCenter.id, preparationWorkingDays: String(value.preparationWorkingDays), executionWorkingDays: String(value.executionWorkingDays), notes: value.notes ?? '' } }
function workingDaysLabel(mask: number, t: (key: string) => string) { return [6, 0, 1, 2, 3, 4, 5].filter(day => (mask & (1 << day)) !== 0).map(day => t(`productionRouting.weekdayShort.${day}`)).join('، ') }
async function get<T>(path: string): Promise<T> { const response = await fetch(`${api}${path}`); if (!response.ok) throw new Error(await readProblem(response)); return response.json() }
async function send<T = unknown>(path: string, method: string, body: object): Promise<T> { const response = await fetch(`${api}${path}`, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); if (!response.ok) throw new Error(await readProblem(response)); return response.json() }
async function readProblem(response: Response) { const body = await response.json().catch(() => null) as { title?: string; detail?: string; code?: string; errors?: Record<string, string[]> } | null; return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || body?.title || `HTTP ${response.status}` }
function problemMessage(reason: unknown, fallback: string) { return reason instanceof Error ? reason.message : fallback }
