import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { formatBusinessDate, formatBusinessDateTime } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { DataTable, FilterBar, FormField, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'
import { useListQuery } from './ui/routeContext'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
type Run = { id: string; runNumber: string; status: string }
type RunList = { items: Run[]; page?: number; pageSize?: number; totalCount?: number }
type Generation = { id: string; generationSequence: number; status: string; isCurrent: boolean }
type Recommendation = {
  id: string; generationId: string; runId: string; runNumber: string; generationSequence: number
  runAsOfDate: string; evaluationDate: string; generatedAtUtc?: string; isCurrentGeneration: boolean; isOutdatedRun: boolean; itemId: string; itemCode: string
  itemNameFa: string; itemNameEn?: string | null; itemType: string; unitCode: string; needByDate: string
  routeOrigin: string
  orderByDate?: string | null; targetReceiptDate?: string | null; decisionHorizonDate?: string | null
  earliestFeasibleReceiptDate?: string | null; systemRecommendedQty?: number | null; baseRecommendedQty?: number | null
  minimumBaseline?: number | null; netRequirementQty: number; safetyStockQty: number; state: string; timingState: string
  priority?: string | null; nextAction: string; estimatedValue?: number | null; reasonCodes: string[]; sourceSelectionReason?: string; calculationTrace?: string; algorithmVersion?: string
  decisionVersion: number; sourcingNumber?: string | null; supplierNumber?: string | null; supplierNameFa?: string | null
  supplierNameEn?: string | null; currencyCode?: string | null; selectedCandidate?: Candidate | null
  candidates?: Candidate[]; decisions?: Decision[]; latestDecisionType?: string | null; reviewDate?: string | null
}
type Candidate = { id: string; sourcingOptionId: string; sourcingNumber: string; supplierId: string; supplierNumber: string; supplierNameFa: string; supplierNameEn?: string | null; isPreferred: boolean; isEligible: boolean; reasonCodes: string[]; minimumOrderQuantity?: number | null; totalLeadTimeDays?: number | null; earliestFeasibleReceiptDate?: string | null; canMeetNeedBy: boolean; unitPrice?: number | null; currencyCode?: string | null; shippingModeCode?: string | null; incotermCode?: string | null; supplierLeadTimeDays?: number | null; forwarderToShipmentDays?: number | null; shippingPeriodDays?: number | null; destinationCustomsDays?: number | null }
type Decision = { id: string; decisionVersion: number; decisionType: string; reason?: string | null; decisionDate: string; decidedAtUtc: string; reviewDate?: string | null; finalSourcingNumber?: string | null; finalSupplierNumber?: string | null; finalQty?: number | null; finalTargetReceiptDate?: string | null; finalOrderByDate?: string | null; finalUnitPrice?: number | null; finalCurrencyCode?: string | null; warningCodes: string }
type ConversionResponse = { groups: { success: boolean; purchaseOrderId?: string | null; purchaseOrderNumber?: string | null; decisions: { decisionId: string; success: boolean; purchaseOrderId?: string | null; purchaseOrderNumber?: string | null; code?: string | null; detailReasons: string[] }[] }[] }
type RecommendationList = { items: Recommendation[]; page: number; pageSize: number; totalCount: number }

export default function OrderPlanningPage({ view }: { view: 'list' | 'object' }) {
  return view === 'list' ? <RecommendationListPage /> : <RecommendationObjectPage />
}

function RecommendationListPage() {
  const { t, i18n } = useTranslation(); const query = useListQuery(25)
  const updateQuery = query.update
  const [data, setData] = useState<RecommendationList>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [runs, setRuns] = useState<Run[]>([]); const [loading, setLoading] = useState(true)
  const [generations, setGenerations] = useState<Generation[]>([])
  const [error, setError] = useState(''); const [generationError, setGenerationError] = useState(''); const [generating, setGenerating] = useState(false)
  const runId = query.params.get('runId') ?? ''; const state = query.params.get('state') ?? ''
  const generationId = query.params.get('generationId') ?? ''
  const priority = query.params.get('priority') ?? ''
  const sortBy = query.params.get('sortBy') ?? 'priority'; const sortDirection = query.params.get('sortDirection') ?? 'asc'
  const runSearch = query.params.get('runSearch') ?? ''; const runPage = Number(query.params.get('runPage') ?? '1')
  const search = query.value('search')
  const load = useCallback(async () => {
    setLoading(true); setError('')
    const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
    if (runId) params.set('runId', runId); if (generationId) params.set('generationId', generationId)
    if (state) params.set('state', state); if (priority) params.set('priority', priority)
    params.set('sortBy', sortBy); params.set('sortDirection', sortDirection)
    if (search) params.set('search', search)
    try { const response = await fetch(`${api}/order-planning/recommendations?${params}`); if (!response.ok) throw new Error(await problem(response)); setData(await response.json()) }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setLoading(false) }
  }, [generationId, priority, query.page, query.pageSize, search, runId, sortBy, sortDirection, state, t])
  useEffect(() => { void fetch(`${api}/order-planning/runs?search=${encodeURIComponent(runSearch)}&page=${runPage}&pageSize=25`).then(r => r.json()).then((body: RunList) => setRuns(body.items)).catch(() => setRuns([])) }, [runPage, runSearch])
  useEffect(() => {
    if (!runId) return
    void fetch(`${api}/order-planning/generations?runId=${runId}`).then(async response => {
      if (!response.ok) throw new Error(await problem(response)); const body: Generation[] = await response.json(); setGenerations(body)
      if (generationId) { setGenerationError(body.some(x => x.id === generationId && x.status === 'COMPLETED') ? '' : t('orderPlanning.generationUnavailable')); return }
      setGenerationError('')
      const remembered = window.localStorage.getItem(`hadron.orderPlanning.generation.${runId}`)
      const selected = body.find(x => x.id === remembered && x.status === 'COMPLETED') ?? body.find(x => x.isCurrent && x.status === 'COMPLETED')
      if (selected) updateQuery({ generationId: selected.id, page: 1 })
    }).catch(caught => setGenerationError(caught instanceof Error ? caught.message : t('common.error')))
  }, [generationId, runId, t, updateQuery])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load])
  async function generate() {
    if (!runId) return; setGenerating(true); setError('')
    try { const response = await fetch(`${api}/order-planning/generations`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ runId }) }); if (!response.ok) throw new Error(await problem(response)); const generated: Generation = await response.json(); window.localStorage.setItem(`hadron.orderPlanning.generation.${runId}`, generated.id); query.update({ generationId: generated.id, page: 1 }) }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setGenerating(false) }
  }
  const name = (row: Recommendation) => localizedName({ nameFa: row.itemNameFa, nameEn: row.itemNameEn }, i18n.resolvedLanguage)
  return <main><PageHeader title={t('orderPlanning.title')} subtitle={t('orderPlanning.subtitle')} />
    {runId && generationError && <MessageStrip tone="critical">{generationError}</MessageStrip>}{error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('orderPlanning.runSearch')}><input value={runSearch} onChange={event => query.update({ runSearch: event.target.value, runPage: 1 })} /></FormField>
      <FormField label={t('orderPlanning.run')}><select value={runId} onChange={event => query.update({ runId: event.target.value, generationId: null, page: 1 })}><option value="">{t('common.all')}</option>{runs.map(run => <option value={run.id} key={run.id}>{run.runNumber}</option>)}</select></FormField>
      <FormField label={t('orderPlanning.generation')}><select value={generationId} disabled={!runId} onChange={event => { window.localStorage.setItem(`hadron.orderPlanning.generation.${runId}`, event.target.value); query.update({ generationId: event.target.value, page: 1 }) }}><option value="">{t('orderPlanning.selectGeneration')}</option>{generations.filter(x => x.status === 'COMPLETED').map(value => <option value={value.id} key={value.id}>G{value.generationSequence} · {value.isCurrent ? t('orderPlanning.current') : t('orderPlanning.historical')}</option>)}</select></FormField>
      <FormField label={t('common.search')}><input value={search} onChange={event => query.update({ search: event.target.value, page: 1 })} placeholder={t('orderPlanning.search')} /></FormField>
      <FormField label={t('common.status')}><select value={state} onChange={event => query.update({ state: event.target.value, page: 1 })}><option value="">{t('common.all')}</option><option value="READY">READY</option><option value="BLOCKED">BLOCKED</option></select></FormField>
      <FormField label={t('orderPlanning.priority')}><select value={priority} onChange={event => query.update({ priority: event.target.value, page: 1 })}><option value="">{t('common.all')}</option>{['P0_EMERGENCY','P1_DUE_NOW','P2_UPCOMING','P3_PLANNED'].map(value => <option key={value}>{value}</option>)}</select></FormField>
    </FilterBar>
    <Section title={t('orderPlanning.worklist')}><>
      <TableToolbar title={t('orderPlanning.worklist')} count={t('common.count', { count: data.totalCount })} actions={<div className="ui-action-row"><button className="button" type="button" disabled={runPage <= 1} onClick={() => query.update({ runPage: runPage - 1 })}>{t('common.previous')}</button><button className="button" type="button" onClick={() => query.update({ runPage: runPage + 1 })}>{t('common.next')}</button><select aria-label={t('orderPlanning.sort')} value={sortBy} onChange={event => query.update({ sortBy: event.target.value, page: 1 })}><option value="priority">{t('orderPlanning.priority')}</option><option value="code">{t('common.code')}</option><option value="needBy">{t('orderPlanning.needBy')}</option><option value="quantity">{t('orderPlanning.quantity')}</option></select><select aria-label={t('orderPlanning.sortDirection')} value={sortDirection} onChange={event => query.update({ sortDirection: event.target.value, page: 1 })}><option value="asc">ASC</option><option value="desc">DESC</option></select><button className="button button--primary" type="button" disabled={!runId || generating} onClick={() => void generate()}>{generating ? t('common.loading') : t('orderPlanning.generate')}</button></div>} />
      <DataTable headers={[t('common.code'), t('orderPlanning.item'), t('orderPlanning.needBy'), t('orderPlanning.orderBy'), t('orderPlanning.quantity'), t('common.status'), t('orderPlanning.priority'), t('orderPlanning.source'), t('orderPlanning.expert')]}
        loading={loading} empty={!data.items.length} filtered={Boolean(search || state || priority || runId)} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}>
        {data.items.map(row => <tr key={row.id}><td><Link className="technical-value" to={`/mrp/order-planning/${row.id}?${query.params}`}>{row.itemCode}</Link></td><td>{name(row)}</td><td>{formatBusinessDate(row.needByDate, i18n.resolvedLanguage ?? 'fa-IR')}</td><td>{formatBusinessDate(row.orderByDate, i18n.resolvedLanguage ?? 'fa-IR')}</td><td className="technical-value">{row.systemRecommendedQty ?? '—'} {row.unitCode}</td><td><StatusBadge text={row.isOutdatedRun ? 'OUTDATED_RUN' : row.state} tone={row.state === 'BLOCKED' || row.isOutdatedRun ? 'critical' : 'positive'} /></td><td>{row.priority ?? '—'}</td><td className="technical-value">{row.sourcingNumber ?? '—'}</td><td>{row.latestDecisionType ?? '—'}</td></tr>)}
      </DataTable>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={data.totalCount} onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(data.totalCount / query.pageSize)), count: data.totalCount })} />
    </></Section></main>
}

function RecommendationObjectPage() {
  const { id } = useParams(); const { t, i18n } = useTranslation(); const navigate = useNavigate(); const location = useLocation()
  const [record, setRecord] = useState<Recommendation | null>(null); const [error, setError] = useState(''); const [busy, setBusy] = useState(false)
  const [decisionType, setDecisionType] = useState('ACCEPT'); const [reason, setReason] = useState(''); const [reviewDate, setReviewDate] = useState('')
  const [sourceId, setSourceId] = useState(''); const [finalQty, setFinalQty] = useState(''); const [target, setTarget] = useState('')
  const load = useCallback(async () => { if (!id) return; setError(''); try { const response = await fetch(`${api}/order-planning/recommendations/${id}`); if (!response.ok) throw new Error(await problem(response)); const body: Recommendation = await response.json(); setRecord(body); setSourceId(body.selectedCandidate?.sourcingOptionId ?? ''); setFinalQty(String(body.systemRecommendedQty ?? '')); setTarget(body.targetReceiptDate ?? '') } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } }, [id, t])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load])
  async function decide(event: FormEvent) { event.preventDefault(); if (!record) return; setBusy(true); setError(''); try { const response = await fetch(`${api}/order-planning/recommendations/${record.id}/decisions`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ decisionType, expectedVersion: record.decisionVersion, reason: reason || null, reviewDate: reviewDate || null, sourcingOptionId: decisionType === 'MODIFY' ? sourceId : null, finalQty: decisionType === 'MODIFY' ? Number(finalQty) : null, targetReceiptDate: decisionType === 'MODIFY' ? target : null }) }); if (!response.ok) throw new Error(await problem(response)); setRecord(await response.json()); setReason('') } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setBusy(false) } }
  async function convert(decision: Decision) { setBusy(true); setError(''); try { const response = await fetch(`${api}/purchase-orders/convert-decisions`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ decisionIds: [decision.id] }) }); if (!response.ok) throw new Error(await problem(response)); const result: ConversionResponse = await response.json(); const converted = result.groups.flatMap(group => group.decisions).find(value => value.decisionId === decision.id); if (!converted?.success || !converted.purchaseOrderId) throw new Error([converted?.code, ...(converted?.detailReasons ?? [])].filter(Boolean).join(' · ') || t('orderPlanning.conversionFailed')); navigate(`/mrp/purchase-orders/${converted.purchaseOrderId}`) } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setBusy(false) } }
  const lang = i18n.resolvedLanguage ?? 'fa-IR'; const candidateName = (x: Candidate) => localizedName({ nameFa: x.supplierNameFa, nameEn: x.supplierNameEn }, lang)
  const listTarget = `/mrp/supply/order-planning${location.search}`
  const acceptOrModifyBlocked = (decisionType === 'ACCEPT' || decisionType === 'MODIFY') && (record?.isOutdatedRun || record?.state === 'BLOCKED')
  if (!record) return <main><PageHeader title={t('orderPlanning.detail')} backTo={listTarget} backLabel={t('common.backToList')} />{error ? <MessageStrip tone="critical">{error}</MessageStrip> : <p>{t('common.loading')}</p>}</main>
  return <main><PageHeader title={`${record.itemCode} — ${localizedName({ nameFa: record.itemNameFa, nameEn: record.itemNameEn }, lang)}`} subtitle={`${record.runNumber} / G${record.generationSequence}`} backTo={listTarget} backLabel={t('common.backToList')} status={<StatusBadge text={record.isOutdatedRun ? 'OUTDATED_RUN' : record.state} tone={record.state === 'READY' && !record.isOutdatedRun ? 'positive' : 'critical'} />} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}{record.reasonCodes.length > 0 && <MessageStrip tone="warning">{record.reasonCodes.join(' · ')}</MessageStrip>}
    <Section title={t('orderPlanning.requirement')}><div className="detail-grid"><Fact label={t('orderPlanning.run')} value={record.runNumber} /><Fact label={t('orderPlanning.asOf')} value={formatBusinessDate(record.runAsOfDate, lang)} /><Fact label={t('orderPlanning.evaluationDate')} value={formatBusinessDate(record.evaluationDate, lang)} /><Fact label={t('orderPlanning.generation')} value={`G${record.generationSequence} · ${record.isCurrentGeneration ? t('orderPlanning.current') : t('orderPlanning.historical')}`} /><Fact label={t('orderPlanning.algorithm')} value={record.algorithmVersion ?? '—'} /><Fact label={t('orderPlanning.needBy')} value={formatBusinessDate(record.needByDate, lang)} /><Fact label={t('orderPlanning.target')} value={formatBusinessDate(record.targetReceiptDate, lang)} /><Fact label={t('orderPlanning.horizon')} value={formatBusinessDate(record.decisionHorizonDate, lang)} /><Fact label={t('orderPlanning.net')} value={`${record.netRequirementQty} ${record.unitCode}`} /><Fact label={t('orderPlanning.safety')} value={`${record.safetyStockQty} ${record.unitCode}`} /><Fact label={t('orderPlanning.route')} value={record.routeOrigin} /></div></Section>
    <Section title={t('orderPlanning.system')}><div className="detail-grid"><Fact label={t('orderPlanning.source')} value={record.selectedCandidate?.sourcingNumber ?? '—'} /><Fact label={t('orderPlanning.supplier')} value={record.selectedCandidate ? `${record.selectedCandidate.supplierNumber} — ${candidateName(record.selectedCandidate)}` : '—'} /><Fact label={t('orderPlanning.lead')} value={String(record.selectedCandidate?.totalLeadTimeDays ?? '—')} /><Fact label="MOQ" value={String(record.selectedCandidate?.minimumOrderQuantity ?? '—')} /><Fact label={t('orderPlanning.price')} value={record.selectedCandidate?.unitPrice == null ? '—' : `${record.selectedCandidate.unitPrice} ${record.selectedCandidate.currencyCode ?? ''}`} /><Fact label={t('orderPlanning.orderBy')} value={formatBusinessDate(record.orderByDate, lang)} /><Fact label={t('orderPlanning.minimumBaseline')} value={String(record.minimumBaseline ?? '—')} /><Fact label={t('orderPlanning.baseQty')} value={String(record.baseRecommendedQty ?? '—')} /><Fact label={t('orderPlanning.quantity')} value={String(record.systemRecommendedQty ?? '—')} /><Fact label={t('orderPlanning.timing')} value={record.timingState} /><Fact label={t('orderPlanning.priority')} value={record.priority ?? '—'} /><Fact label={t('orderPlanning.nextAction')} value={record.nextAction} /><Fact label={t('orderPlanning.selectionReason')} value={record.sourceSelectionReason ?? '—'} /><Fact label={t('orderPlanning.value')} value={record.estimatedValue == null ? '—' : `${record.estimatedValue} ${record.selectedCandidate?.currencyCode ?? ''}`} /></div></Section>
    <Section title={t('orderPlanning.calculationTrace')}><pre className="technical-value">{record.calculationTrace ?? '—'}</pre></Section>
    <Section title={t('orderPlanning.candidates')}><DataTable headers={[t('orderPlanning.source'), t('orderPlanning.supplier'), t('orderPlanning.leadStages'), t('orderPlanning.lead'), 'MOQ', t('orderPlanning.price'), t('orderPlanning.shipping'), t('orderPlanning.incoterm'), t('common.status')]} loadingLabel={t('common.loading')} empty={!record.candidates?.length} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}>{record.candidates?.map(candidate => <tr key={candidate.id}><td className="technical-value">{candidate.sourcingNumber}{candidate.isPreferred ? ' ★' : ''}</td><td>{candidate.supplierNumber} — {candidateName(candidate)}</td><td className="technical-value">{[candidate.supplierLeadTimeDays,candidate.forwarderToShipmentDays,candidate.shippingPeriodDays,candidate.destinationCustomsDays].map(value => value ?? '—').join(' / ')}</td><td>{candidate.totalLeadTimeDays ?? '—'}</td><td>{candidate.minimumOrderQuantity ?? '—'}</td><td>{candidate.unitPrice ?? '—'} {candidate.currencyCode}</td><td>{candidate.shippingModeCode ?? '—'}</td><td>{candidate.incotermCode ?? '—'}</td><td><StatusBadge text={candidate.isEligible ? 'READY' : candidate.reasonCodes.join(', ')} tone={candidate.isEligible ? 'positive' : 'critical'} /></td></tr>)}</DataTable></Section>
    <Section title={t('orderPlanning.expertDecision')}><form onSubmit={decide}><div className="form-grid"><FormField label={t('orderPlanning.decision')}><select value={decisionType} onChange={event => setDecisionType(event.target.value)}>{['ACCEPT','MODIFY','REJECT','DEFER'].map(value => <option key={value}>{value}</option>)}</select></FormField>{decisionType === 'MODIFY' && <><FormField label={t('orderPlanning.source')}><select value={sourceId} onChange={event => setSourceId(event.target.value)}>{record.candidates?.filter(x => x.isEligible).map(x => <option value={x.sourcingOptionId} key={x.id}>{x.sourcingNumber} — {candidateName(x)}</option>)}</select></FormField><FormField label={t('orderPlanning.finalQty')}><input type="number" min="0" step="0.000001" value={finalQty} onChange={event => setFinalQty(event.target.value)} /></FormField><FormField label={t('orderPlanning.target')}><input type="date" value={target} onChange={event => setTarget(event.target.value)} /></FormField></>}{decisionType === 'DEFER' && <FormField label={t('orderPlanning.reviewDate')}><input type="date" value={reviewDate} onChange={event => setReviewDate(event.target.value)} /></FormField>}{decisionType !== 'ACCEPT' && <FormField label={t('orderPlanning.reason')}><textarea value={reason} onChange={event => setReason(event.target.value)} /></FormField>}</div><button className="button button--primary" disabled={busy || acceptOrModifyBlocked}>{busy ? t('common.loading') : t('common.save')}</button></form></Section>
    <Section title={t('orderPlanning.history')}><DataTable headers={[t('orderPlanning.decision'), t('orderPlanning.decisionDate'), t('orderPlanning.reason'), t('orderPlanning.finalQty'), t('common.actions')]} loadingLabel={t('common.loading')} empty={!record.decisions?.length} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}>{record.decisions?.map(decision => <tr key={decision.id}><td>{decision.decisionType}</td><td>{formatBusinessDateTime(decision.decidedAtUtc, lang)}</td><td>{decision.reason ?? '—'}</td><td>{decision.finalQty ?? '—'} {decision.warningCodes}</td><td>{['ACCEPT','MODIFY'].includes(decision.decisionType) && <button className="button button--primary" type="button" disabled={busy} onClick={() => void convert(decision)}>{t('orderPlanning.convertToPo')}</button>}</td></tr>)}</DataTable></Section>
    <button className="button button--quiet" onClick={() => navigate(-1)}>{t('common.backToList')}</button></main>
}

function Fact({ label, value }: { label: string; value: string }) { return <div className="detail-field"><span>{label}</span><strong>{value}</strong></div> }
async function problem(response: Response) { const body = await response.json().catch(() => null); return body?.detail ?? body?.title ?? `HTTP ${response.status}` }
