import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { localizedName } from './i18n/localizedName'
import {
  DataTable, FilterBar, FormField, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar,
} from './ui'
import { useListQuery } from './ui/routeContext'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
type Run = { id: string; runNumber: string; asOfDate: string; horizonEndDate: string; engineVersion: string; status: string; completeness: string; assumptions?: string; exceptionCount: number }
type RunList = { items: Run[]; page: number; pageSize: number; totalCount: number }
type Item = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; unitCode: string; unitNameFa: string; unitNameEn?: string | null }
type Summary = { item: Item; startingOnHand: number; totalGrossDemand: number; totalFirmIncoming: number; totalNetRequirement: number; stockoutDate?: string | null; firmCoverageState?: string | null }
type SummaryList = { items: Summary[]; page: number; pageSize: number; totalCount: number }
type RunException = { id: string; itemCode?: string | null; date?: string | null; code: string; severity: string; affectsDemand: boolean; message: string }
type ExceptionList = { items: RunException[]; page: number; pageSize: number; totalCount: number }
type Candidate = { id: string; code: string; nameFa: string; nameEn?: string | null }
type CandidateList = { items: Candidate[]; totalCount: number }
type Daily = { date: string; grossDemand: number; firmIncoming: number; firmProjected: number; netRequirement: number; planningBalance: number }
type Timeline = { item: Item; daily: Daily[]; trace: { childGrossRequirement: number }[] }
type RequirementTrace = {
  runId: string; item: Item; demandDate: string
  origins: { productCode: string; forecastSeriesId?: string | null; forecastRevisionId?: string | null; periodStartDate: string; periodEndDate: string; allocatedDemandQty: number; officialForecastQty?: number | null; actualMtdQty: number; remainingForecastQty?: number | null; allocationState: string }[]
  bom: { revisionCode: string; parentItemCode: string; childItemCode: string; usageQty: number; baseOutputQty: number; scrapSource: string; scrapPercent: number; grossContribution: number }[]
  inventory: { warehouseCode: string; warehouseIsActive: boolean; onHandQty: number }[]
  companyStartingOnHand: number; includesInactiveWarehouseStock: boolean
  firmSupply: { deliveryScheduleId: string; purchaseOrderNumber: string; openQty: number; eta?: string | null; etaSource: string; etaStatus: string; appliedByDemandDate: boolean }[]
  netting?: { grossRequirement: number; firmIncoming: number; firmProjectedBefore: number; firmProjectedAfter: number; safetyStock: number; planningBeforeNeed: number; netRequirement: number; planningBalance: number } | null
  route: { supplyMethod?: string | null; chosenRoute?: string | null; decisionSource?: string | null; exceptionCodes: string[] }
}
type RouteDecision = { itemId: string; route: 'BOM' | 'EXTERNAL' }

export default function MrpRunsPage({ view }: { view: 'list' | 'create' | 'object' }) {
  return view === 'list' ? <RunListPage /> : view === 'create' ? <CreateRunPage /> : <RunObjectPage />
}

function RunListPage() {
  const { t } = useTranslation()
  const query = useListQuery(25)
  const [data, setData] = useState<RunList>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const response = await fetch(`${api}/mrp-runs?page=${query.page}&pageSize=${query.pageSize}`)
      if (!response.ok) throw new Error(await problem(response))
      setData(await response.json())
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoading(false)
    }
  }, [query.page, query.pageSize, t])
  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0)
    return () => window.clearTimeout(timer)
  }, [load])

  return <main>
    <PageHeader title={t('mrpRuns.title')} subtitle={t('mrpRuns.subtitle')} actions={
      <Link className="button button--primary" to="/mrp/planning/mrp-runs/new">{t('mrpRuns.new')}</Link>
    } />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <Section title={t('mrpRuns.list')}><>
      <TableToolbar title={t('mrpRuns.list')} count={t('common.count', { count: data.totalCount })} />
      <DataTable
        headers={[t('mrpRuns.number'), t('mrpRuns.asOf'), t('mrpRuns.horizon'), t('common.status'), t('mrpRuns.completeness'), t('mrpRuns.exceptions')]}
        loading={loading} empty={!data.items.length} loadingLabel={t('common.loading')}
        emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}
      >
        {data.items.map(run => <tr key={run.id}>
          <td><Link className="technical-value" to={`/mrp/planning/mrp-runs/${run.id}`}>{run.runNumber}</Link></td>
          <td className="technical-value">{run.asOfDate}</td><td className="technical-value">{run.horizonEndDate}</td>
          <td><RunStatus value={run.status} /></td><td><RunStatus value={run.completeness} /></td><td>{run.exceptionCount}</td>
        </tr>)}
      </DataTable>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={data.totalCount}
        onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(data.totalCount / query.pageSize)), count: data.totalCount })} />
    </></Section>
  </main>
}

function CreateRunPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [asOf, setAsOf] = useState('')
  const [horizon, setHorizon] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [decisions, setDecisions] = useState<RouteDecision[]>([])
  const [itemId, setItemId] = useState('')
  const [route, setRoute] = useState<RouteDecision['route']>('BOM')
  const [candidateSearch, setCandidateSearch] = useState('')
  const [candidates, setCandidates] = useState<Candidate[]>([])

  useEffect(() => {
    void fetch(`${api}/planning/business-date`).then(response => response.json())
      .then(body => setAsOf(body.tehranBusinessDate)).catch(() => undefined)
  }, [])
  useEffect(() => {
    const timer = window.setTimeout(() => {
      void fetch(`${api}/mrp-runs/route-candidates?search=${encodeURIComponent(candidateSearch)}&page=1&pageSize=25`)
        .then(response => response.json()).then((body: CandidateList) => setCandidates(body.items))
        .catch(() => setCandidates([]))
    }, 150)
    return () => window.clearTimeout(timer)
  }, [candidateSearch])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const response = await fetch(`${api}/mrp-runs`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ asOfDate: asOf, horizonEndDate: horizon || null, routeDecisions: decisions }),
      })
      if (!response.ok) throw new Error(await problem(response))
      const body = await response.json()
      navigate(`/mrp/planning/mrp-runs/${body.id}`)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setBusy(false)
    }
  }
  function addDecision() {
    if (!itemId || decisions.some(value => value.itemId === itemId)) return
    setDecisions([...decisions, { itemId, route }])
    setItemId('')
  }
  const candidateName = (candidate: Candidate) => localizedName(candidate, i18n.resolvedLanguage)

  return <main>
    <PageHeader title={t('mrpRuns.new')} subtitle={t('mrpRuns.createHint')}
      backTo="/mrp/planning/mrp-runs" backLabel={t('common.backToList')} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <form onSubmit={submit}>
      <Section title={t('mrpRuns.parameters')}><div className="form-grid">
        <FormField label={t('mrpRuns.asOf')} required>
          <input type="date" value={asOf} onChange={event => setAsOf(event.target.value)} required />
        </FormField>
        <FormField label={t('mrpRuns.horizon')} hint={t('mrpRuns.defaultHorizon')}>
          <input type="date" value={horizon} min={asOf} onChange={event => setHorizon(event.target.value)} />
        </FormField>
      </div></Section>
      <Section title={t('mrpRuns.hybridDecisions')} description={t('mrpRuns.hybridHint')}><>
        <div className="form-grid">
          <FormField label={t('mrpRuns.searchHybrid')}><input value={candidateSearch} onChange={event => setCandidateSearch(event.target.value)} /></FormField>
          <FormField label={t('mrpRuns.itemId')}><select value={itemId} onChange={event => setItemId(event.target.value)}>
            <option value="">{t('mrpRuns.selectItem')}</option>
            {candidates.map(candidate => <option value={candidate.id} key={candidate.id}>{candidate.code} — {candidateName(candidate)}</option>)}
          </select></FormField>
          <FormField label={t('mrpRuns.route')}><select value={route} onChange={event => setRoute(event.target.value as RouteDecision['route'])}>
            <option value="BOM">BOM</option><option value="EXTERNAL">EXTERNAL</option>
          </select></FormField>
          <button type="button" className="button" onClick={addDecision}>{t('common.create')}</button>
        </div>
        {decisions.map(value => <p className="technical-value" key={value.itemId}>
          {candidates.find(candidate => candidate.id === value.itemId)?.code ?? value.itemId} — {value.route}
        </p>)}
      </></Section>
      <div className="object-actions">
        <button type="submit" className="button button--primary" disabled={busy || !asOf}>
          {busy ? t('common.loading') : t('mrpRuns.execute')}
        </button>
        <Link className="button" to="/mrp/planning/mrp-runs">{t('common.cancel')}</Link>
      </div>
    </form>
  </main>
}

function RunObjectPage() {
  const { id } = useParams()
  const { t, i18n } = useTranslation()
  const query = useListQuery(25)
  const [searchParams, setSearchParams] = useSearchParams()
  const exceptionPage = Math.max(1, Number(searchParams.get('exceptionPage') ?? '1') || 1)
  const exceptionSeverity = searchParams.get('exceptionSeverity') ?? ''
  const [run, setRun] = useState<Run | null>(null)
  const [summaries, setSummaries] = useState<SummaryList>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [exceptions, setExceptions] = useState<ExceptionList>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [timeline, setTimeline] = useState<Timeline | null>(null)
  const [trace, setTrace] = useState<RequirementTrace | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const load = useCallback(async () => {
    if (!id) return
    setLoading(true)
    try {
      const responses = await Promise.all([
        fetch(`${api}/mrp-runs/${id}`),
        fetch(`${api}/mrp-runs/${id}/items?page=${query.page}&pageSize=${query.pageSize}`),
        fetch(`${api}/mrp-runs/${id}/exceptions?page=${exceptionPage}&pageSize=25${exceptionSeverity ? `&severity=${encodeURIComponent(exceptionSeverity)}` : ''}`),
      ])
      for (const response of responses) if (!response.ok) throw new Error(await problem(response))
      setRun(await responses[0].json())
      setSummaries(await responses[1].json())
      setExceptions(await responses[2].json())
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoading(false)
    }
  }, [exceptionPage, exceptionSeverity, id, query.page, query.pageSize, t])
  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0)
    return () => window.clearTimeout(timer)
  }, [load])
  const display = (item: Item) => localizedName(item, i18n.resolvedLanguage)
  async function inspect(itemId: string) {
    if (!id) return
    try {
      const response = await fetch(`${api}/mrp-runs/${id}/items/${itemId}/timeline`)
      if (!response.ok) throw new Error(await problem(response))
      setTimeline(await response.json())
      setTrace(null)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    }
  }
  async function inspectTrace(itemId: string, demandDate: string) {
    if (!id) return
    try {
      const response = await fetch(`${api}/mrp-runs/${id}/items/${itemId}/trace?demandDate=${demandDate}`)
      if (!response.ok) throw new Error(await problem(response))
      setTrace(await response.json())
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    }
  }
  function changeExceptionPage(page: number) {
    const next = new URLSearchParams(searchParams)
    next.set('exceptionPage', String(page))
    setSearchParams(next)
  }
  function changeExceptionSeverity(value: string) {
    const next = new URLSearchParams(searchParams)
    if (value) next.set('exceptionSeverity', value)
    else next.delete('exceptionSeverity')
    next.set('exceptionPage', '1')
    setSearchParams(next)
  }

  if (!run && loading) return <main><PageHeader title={t('mrpRuns.title')} /><p>{t('common.loading')}</p></main>
  return <main>
    <PageHeader title={run?.runNumber ?? t('mrpRuns.title')}
      subtitle={run ? `${run.asOfDate} — ${run.horizonEndDate}` : undefined}
      status={run && <RunStatus value={run.status} />} backTo="/mrp/planning/mrp-runs" backLabel={t('common.backToList')} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {run && <Section title={t('mrpRuns.overview')}><div className="detail-grid">
      <div><span>{t('mrpRuns.engine')}</span><strong className="technical-value">{run.engineVersion}</strong></div>
      <div><span>{t('mrpRuns.completeness')}</span><RunStatus value={run.completeness} /></div>
      <div><span>{t('mrpRuns.assumptions')}</span><strong className="technical-value">{run.assumptions}</strong></div>
    </div></Section>}
    <Section title={t('mrpRuns.results')}><>
      <DataTable
        headers={[t('common.code'), t('mrpRuns.item'), t('mrpRuns.starting'), t('mrpRuns.gross'), t('mrpRuns.firm'), t('mrpRuns.net'), t('mrpRuns.stockout'), t('mrpRuns.coverage'), t('common.actions')]}
        loading={loading} empty={!summaries.items.length} loadingLabel={t('common.loading')}
        emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}
      >
        {summaries.items.map(row => <tr key={row.item.id}>
          <td className="technical-value">{row.item.code}</td><td>{display(row.item)}</td><td>{row.startingOnHand}</td>
          <td>{row.totalGrossDemand}</td><td>{row.totalFirmIncoming}</td><td>{row.totalNetRequirement}</td>
          <td className="technical-value">{row.stockoutDate ?? '—'}</td><td>{row.firmCoverageState ?? '—'}</td>
          <td><button className="button button--small" type="button" onClick={() => void inspect(row.item.id)}>{t('mrpRuns.trace')}</button></td>
        </tr>)}
      </DataTable>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={summaries.totalCount}
        onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(summaries.totalCount / query.pageSize)), count: summaries.totalCount })} />
    </></Section>
    {timeline && <Section title={`${t('mrpRuns.timeline')} — ${timeline.item.code}`}><>
      <DataTable
        headers={[t('mrpRuns.date'), t('mrpRuns.gross'), t('mrpRuns.firm'), t('mrpRuns.firmProjected'), t('mrpRuns.net'), t('mrpRuns.planningBalance'), t('common.actions')]}
        empty={!timeline.daily.length} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')}
        noResultTitle={t('common.noResultsTitle')}
      >
        {timeline.daily.map(row => <tr key={row.date}>
          <td className="technical-value">{row.date}</td><td>{row.grossDemand}</td><td>{row.firmIncoming}</td>
          <td>{row.firmProjected}</td><td>{row.netRequirement}</td><td>{row.planningBalance}</td>
          <td><button className="button button--small" type="button" onClick={() => void inspectTrace(timeline.item.id, row.date)}>{t('mrpRuns.lineage')}</button></td>
        </tr>)}
      </DataTable>
    </></Section>}
    {trace && <Section title={`${t('mrpRuns.lineage')} — ${trace.item.code} — ${trace.demandDate}`}><>
      <h3>{t('mrpRuns.origin')}</h3>
      {trace.origins.length ? trace.origins.map((row, index) => <div className="detail-grid" key={`${row.productCode}-${index}`}>
        <div><span>{t('common.code')}</span><strong className="technical-value">{row.productCode}</strong></div>
        <div><span>{t('mrpRuns.forecastPeriod')}</span><strong className="technical-value">{row.periodStartDate} — {row.periodEndDate}</strong></div>
        <div><span>{t('mrpRuns.allocatedDemand')}</span><strong>{row.allocatedDemandQty}</strong></div>
        <div><span>{t('mrpRuns.allocationState')}</span><strong>{row.allocationState}</strong></div>
      </div>) : <p>{t('common.emptyTitle')}</p>}
      <h3>{t('mrpRuns.bomLineage')}</h3>
      {trace.bom.length ? trace.bom.map((row, index) => <p className="technical-value" key={`${row.revisionCode}-${index}`}>
        {row.revisionCode}: {row.parentItemCode} → {row.childItemCode}; {row.usageQty}/{row.baseOutputQty}; {row.scrapSource} {row.scrapPercent}%; {row.grossContribution}
      </p>) : <p>{t('common.emptyTitle')}</p>}
      <h3>{t('mrpRuns.inventoryLineage')}</h3>
      <p>{t('mrpRuns.companyOnHand')}: <strong>{trace.companyStartingOnHand}</strong></p>
      {trace.inventory.map(row => <p className="technical-value" key={row.warehouseCode}>{row.warehouseCode}: {row.onHandQty} {row.warehouseIsActive ? '' : `— ${t('mrpRuns.inactiveWarehouse')}`}</p>)}
      <h3>{t('mrpRuns.firmLineage')}</h3>
      {trace.firmSupply.length ? trace.firmSupply.map(row => <p className="technical-value" key={row.deliveryScheduleId}>
        {row.purchaseOrderNumber}: {row.openQty}; ETA {row.eta ?? '—'}; {row.etaStatus}; {row.appliedByDemandDate ? t('mrpRuns.applied') : t('mrpRuns.excluded')}
      </p>) : <p>{t('common.emptyTitle')}</p>}
      <h3>{t('mrpRuns.nettingLineage')}</h3>
      {trace.netting ? <p className="technical-value">Gross {trace.netting.grossRequirement}; Firm {trace.netting.firmProjectedBefore} → {trace.netting.firmProjectedAfter}; Safety {trace.netting.safetyStock}; Before {trace.netting.planningBeforeNeed}; Net {trace.netting.netRequirement}; Balance {trace.netting.planningBalance}</p> : <p>{t('common.emptyTitle')}</p>}
      <h3>{t('mrpRuns.routeLineage')}</h3>
      <p className="technical-value">{trace.route.supplyMethod ?? '—'} → {trace.route.chosenRoute ?? '—'}; {trace.route.decisionSource ?? '—'} {trace.route.exceptionCodes.join(', ')}</p>
    </></Section>}
    <Section title={t('mrpRuns.exceptions')}><>
      <FilterBar label={t('mrpRuns.exceptionSeverity')}>
        <FormField label={t('mrpRuns.exceptionSeverity')}>
          <select value={exceptionSeverity} onChange={event => changeExceptionSeverity(event.target.value)}>
            <option value="">{t('mrpRuns.allSeverities')}</option>
            <option value="ERROR">ERROR</option><option value="WARNING">WARNING</option><option value="INFO">INFO</option>
          </select>
        </FormField>
      </FilterBar>
      {exceptions.items.length ? exceptions.items.map(row => <MessageStrip key={row.id}
        tone={row.severity === 'ERROR' ? 'critical' : row.severity === 'WARNING' ? 'warning' : 'information'}>
        <strong className="technical-value">{row.code}</strong> {row.itemCode && `— ${row.itemCode}`} {row.date && `— ${row.date}`}<p>{row.message}</p>
      </MessageStrip>) : <p>{t('mrpRuns.noExceptions')}</p>}
      <Pagination page={exceptionPage} pageSize={exceptions.pageSize} totalCount={exceptions.totalCount}
        onPageChange={changeExceptionPage} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: exceptionPage, pages: Math.max(1, Math.ceil(exceptions.totalCount / exceptions.pageSize)), count: exceptions.totalCount })} />
    </></Section>
  </main>
}

function RunStatus({ value }: { value: string }) {
  const { t } = useTranslation()
  const tone = value === 'COMPLETED' || value === 'COMPLETE' ? 'positive'
    : value.includes('FAILED') || value === 'INCOMPLETE' ? 'critical'
      : value.includes('EXCEPTION') || value.includes('WARNING') ? 'warning' : 'information'
  return <StatusBadge text={t(`mrpRuns.status.${value}`, { defaultValue: value })} tone={tone} />
}

async function problem(response: Response) {
  try {
    const body = await response.json()
    return body.detail ?? body.title ?? JSON.stringify(body.errors ?? body)
  } catch {
    return `${response.status} ${response.statusText}`
  }
}
