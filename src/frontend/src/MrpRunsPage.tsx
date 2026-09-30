import { FormEvent, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { localizedName } from './i18n/localizedName'
import { formatBusinessDate, formatBusinessDateTime } from './i18n/businessDate'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { isPersianLanguage, normalizeJalaliDateInput } from './mrpRunUiSupport'
import {
  DataTable, Definition, DefinitionGrid, EmptyState, FilterBar, FormField, LoadingState, MessageStrip,
  PageHeader, Pagination, Section, StatusBadge, TableToolbar, type StatusTone,
} from './ui'
import { useListQuery } from './ui/routeContext'
import { useOptionalAuth } from './AuthContext'
import hadronCompanionThinking from './assets/brand/character/hadron-companion-thinking.webp'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
type Run = { id: string; runNumber: string; asOfDate: string; horizonEndDate: string; engineVersion: string; status: string; completeness: string; assumptions?: string; completedAtUtc?: string | null; exceptionCount: number }
type RunList = { items: Run[]; page: number; pageSize: number; totalCount: number }
type RefreshStatus = { status: string; latestRunId?: string; latestRunNumber?: string; latestAsOfDate?: string; lastUpdatedAtUtc?: string; pendingChangeCount: number; errorCode?: string; errorMessage?: string; certifiedRunId?: string; certifiedRunNumber?: string; certifiedBusinessDate?: string; certifiedAtUtc?: string; certifiedResultHash?: string }
type Item = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; unitCode: string; unitNameFa: string; unitNameEn?: string | null }
type Summary = { item: Item; startingOnHand: number; totalGrossDemand: number; totalFirmIncoming: number; totalNetRequirement: number; stockoutDate?: string | null; firmCoverageState?: string | null }
type SummaryList = { items: Summary[]; page: number; pageSize: number; totalCount: number }
type RunException = { itemId?: string | null; itemCode?: string | null; firstDate?: string | null; lastDate?: string | null; code: string; severity: string; affectsDemand: boolean; message: string; count: number }
type ExceptionList = { items: RunException[]; page: number; pageSize: number; totalCount: number }
type ExceptionOverview = { blockingCount: number; warningCount: number; informationCount: number; blockingGroups: ExceptionList; warningGroups: ExceptionList }
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
type RunDefaults = { asOfDate: string; asOfJalaliDate: string; defaultHorizonEndDate: string; defaultHorizonEndJalaliDate: string }
type ResolvedJalaliDate = { jalaliDate: string; gregorianDate: string }

export default function MrpRunsPage({ view }: { view: 'list' | 'create' | 'object' }) {
  return view === 'list' ? <RunListPage /> : view === 'create' ? <CreateRunPage /> : <RunObjectPage />
}

function RunListPage() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const query = useListQuery(25)
  const archive = query.value('view') === 'archive'
  const auth = useOptionalAuth()
  const canExecute = auth?.has('planning.mrp_run.execute') ?? true
  const [data, setData] = useState<RunList>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [refresh, setRefresh] = useState<RefreshStatus | null>(null)
  const [latest, setLatest] = useState<Run | null>(null)
  const [exceptionOverview, setExceptionOverview] = useState<ExceptionOverview | null>(null)
  const [exceptionOverviewLoading, setExceptionOverviewLoading] = useState(false)
  const [exceptionOverviewError, setExceptionOverviewError] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const loadedRunId = useRef<string | null>(null)
  const loadedExceptionRunId = useRef<string | null>(null)
  const canViewForecast = auth?.has('planning.forecast.view') ?? true
  const canViewSourcing = auth?.has('procurement.sourcing.view') ?? true
  const canViewMaterialPlanning = auth?.has('planning.material.view') ?? true
  const loadArchive = useCallback(async () => {
    if (!archive) return
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
  }, [archive, query.page, query.pageSize, t])
  useEffect(() => {
    if (!archive) return
    const timer = window.setTimeout(() => void loadArchive(), 0)
    return () => window.clearTimeout(timer)
  }, [archive, loadArchive])

  const loadCurrent = useCallback(async () => {
    if (archive) return
    setError('')
    try {
      const statusResponse = await fetch(`${api}/material-planning/refresh-status`)
      if (!statusResponse.ok) throw new Error(await problem(statusResponse))
      const next = await statusResponse.json() as RefreshStatus
      setRefresh(next)
      if (!next.latestRunId) {
        setLatest(null)
        setExceptionOverview(null)
        loadedRunId.current = null
        loadedExceptionRunId.current = null
        setLoading(false)
        return
      }
      if (loadedRunId.current !== next.latestRunId) {
        setLoading(true)
        const runResponse = await fetch(`${api}/mrp-runs/${next.latestRunId}`)
        if (!runResponse.ok) throw new Error(await problem(runResponse))
        setLatest(await runResponse.json())
        loadedRunId.current = next.latestRunId
        loadedExceptionRunId.current = null
        setExceptionOverview(null)
        setExceptionOverviewError('')
      }
      setLoading(false)
      if (loadedExceptionRunId.current !== next.latestRunId) {
        setExceptionOverviewLoading(true)
        try {
          setExceptionOverview(await loadExceptionOverview(next.latestRunId))
          loadedExceptionRunId.current = next.latestRunId
          setExceptionOverviewError('')
        } catch {
          setExceptionOverviewError(t('mrpRuns.exceptionOverviewUnavailable'))
        } finally {
          setExceptionOverviewLoading(false)
        }
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
      setLoading(false)
    }
  }, [archive, t])
  useEffect(() => {
    if (archive) return
    const timer = window.setTimeout(() => void loadCurrent(), 0)
    const interval = window.setInterval(() => void loadCurrent(), 5000)
    return () => { window.clearTimeout(timer); window.clearInterval(interval) }
  }, [archive, loadCurrent])

  if (!archive) return <main>
    <PageHeader sticky title={t('mrpRuns.currentTitle')} subtitle={t('mrpRuns.currentSubtitle')} actions={
      <Link className="button" to="/mrp/planning/mrp-runs?view=archive">{t('mrpRuns.archive')}</Link>
    } />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {refresh && <RunRefreshStatus status={refresh} language={language} t={t} />}
    <Section title={t('mrpRuns.currentResult')} description={t('mrpRuns.currentResultHint')}>
      {loading ? <LoadingState label={t('common.loading')} /> : !latest ? <EmptyState
        title={t('mrpRuns.noReadyResult')} description={t('mrpRuns.noReadyResultHint')}
        action={canExecute ? <Link className="button" to="/mrp/planning/mrp-runs/new">{t('mrpRuns.manualRun')}</Link> : undefined}
      /> : <>
        <DefinitionGrid>
          <Definition label={t('mrpRuns.number')} technical>{latest.runNumber}</Definition>
          <Definition label={t('mrpRuns.asOf')} technical>{formatBusinessDate(latest.asOfDate, language)}</Definition>
          <Definition label={t('mrpRuns.horizon')} technical>{formatBusinessDate(latest.horizonEndDate, language)}</Definition>
          <Definition label={t('common.status')}><RunStatus value={latest.status} /></Definition>
          <Definition label={t('mrpRuns.completeness')}><RunStatus value={latest.completeness} /></Definition>
          <Definition label={t('mrpRuns.exceptions')} technical>{latest.exceptionCount}</Definition>
        </DefinitionGrid>
        <CurrentRunExceptionOverview run={latest} overview={exceptionOverview}
          loading={exceptionOverviewLoading} error={exceptionOverviewError} language={language}
          canViewForecast={canViewForecast} canViewSourcing={canViewSourcing}
          canViewMaterialPlanning={canViewMaterialPlanning} t={t} />
        <div className="inline-actions">
          <Link className="button button--primary" to={`/mrp/planning/material-planning?runId=${latest.id}`}>{t('mrpRuns.openLatest')}</Link>
          <Link className="button" to={`/mrp/planning/mrp-runs/${latest.id}`}>{t('mrpRuns.openTechnicalRun')}</Link>
        </div>
      </>}
    </Section>
    {canExecute && <Section title={t('mrpRuns.advancedActions')} description={t('mrpRuns.manualRunHint')}>
      <Link className="button button--quiet" to="/mrp/planning/mrp-runs/new">{t('mrpRuns.manualRun')}</Link>
    </Section>}
  </main>

  return <main>
    <PageHeader sticky title={t('mrpRuns.archiveTitle')} subtitle={t('mrpRuns.archiveSubtitle')} actions={
      <Link className="button" to="/mrp/planning/mrp-runs">{t('mrpRuns.backToCurrent')}</Link>
    } />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <section className="ui-table-surface" aria-label={t('mrpRuns.list')}>
      <TableToolbar title={t('mrpRuns.list')} count={t('common.count', { count: data.totalCount })} />
      <DataTable
        headers={[t('mrpRuns.number'), t('mrpRuns.asOf'), t('mrpRuns.horizon'), t('common.status'), t('mrpRuns.completeness'), t('mrpRuns.exceptions')]}
        loading={loading} empty={!data.items.length} loadingLabel={t('common.loading')}
        emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}
      >
        {data.items.map(run => <tr key={run.id}>
          <td><Link className="technical-value" to={`/mrp/planning/mrp-runs/${run.id}`}>{run.runNumber}</Link></td>
          <td className="technical-value">{formatBusinessDate(run.asOfDate, language)}</td><td className="technical-value">{formatBusinessDate(run.horizonEndDate, language)}</td>
          <td><RunStatus value={run.status} /></td><td><RunStatus value={run.completeness} /></td><td>{run.exceptionCount}</td>
        </tr>)}
      </DataTable>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={data.totalCount}
        onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(data.totalCount / query.pageSize)), count: data.totalCount })} />
    </section>
  </main>
}

function CurrentRunExceptionOverview({ run, overview, loading, error, language, canViewForecast,
  canViewSourcing, canViewMaterialPlanning, t }: {
  run: Run; overview: ExceptionOverview | null; loading: boolean; error: string; language: string
  canViewForecast: boolean; canViewSourcing: boolean; canViewMaterialPlanning: boolean
  t: ReturnType<typeof useTranslation>['t']
}) {
  const displayDate = (value: string | null | undefined) => formatBusinessDate(value, language)
  if (run.exceptionCount === 0)
    return <MessageStrip tone="positive">{t('mrpRuns.noActionableExceptions')}</MessageStrip>

  return <section className="mrp-current-exceptions" aria-labelledby="mrp-current-exceptions-title">
    <header>
      <div>
        <h3 id="mrp-current-exceptions-title">{t('mrpRuns.exceptionOverviewTitle')}</h3>
        <p>{t('mrpRuns.exceptionOverviewHint')}</p>
      </div>
      <span className="mrp-current-exceptions__total">{t('mrpRuns.totalExceptionEvents', {
        count: formatOperationalNumber(run.exceptionCount, language),
      })}</span>
    </header>
    {loading && <LoadingState label={t('common.loading')} compact />}
    {error && <MessageStrip tone="warning">{error}</MessageStrip>}
    {overview && <div className="mrp-exception-overview-grid">
      <article className="mrp-exception-overview-card mrp-exception-overview-card--critical">
        <div className="mrp-exception-overview-card__heading">
          <div><StatusBadge text={t('mrpRuns.blockingErrors')} tone="critical" /><p>{t('mrpRuns.blockingErrorsHint')}</p></div>
          <strong>{formatOperationalNumber(overview.blockingCount, language)}</strong>
        </div>
        <ExceptionGroupPreview groups={overview.blockingGroups} language={language} displayDate={displayDate} t={t} />
        {overview.blockingCount > 0 && <div className="ui-action-row">
          {overview.blockingGroups.items.some(row => row.code === 'MISSING_FORECAST') && canViewForecast &&
            <Link className="button button--primary" to="/mrp/planning/production-forecast">{t('mrpRuns.fixForecast')}</Link>}
          {canViewMaterialPlanning && <Link className="button" to={`/mrp/planning/material-planning?runId=${run.id}&exceptionSeverity=ERROR&exceptionAffects=true`}>{t('mrpRuns.viewBlockingErrors')}</Link>}
        </div>}
      </article>
      <article className="mrp-exception-overview-card mrp-exception-overview-card--warning">
        <div className="mrp-exception-overview-card__heading">
          <div><StatusBadge text={t('mrpRuns.nonBlockingWarnings')} tone="warning" /><p>{t('mrpRuns.nonBlockingWarningsHint')}</p></div>
          <strong>{formatOperationalNumber(overview.warningCount, language)}</strong>
        </div>
        <ExceptionGroupPreview groups={overview.warningGroups} language={language} displayDate={displayDate} t={t} />
        {overview.warningCount > 0 && <div className="ui-action-row">
          {overview.warningGroups.items.some(row => row.code === 'NO_PLANNING_READY_EXTERNAL_SOURCE') && canViewSourcing &&
            <Link className="button button--primary" to="/mrp/supply/sourcing">{t('mrpRuns.defineSourcing')}</Link>}
          {canViewMaterialPlanning && <Link className="button" to={`/mrp/planning/material-planning?runId=${run.id}&exceptionSeverity=WARNING`}>{t('mrpRuns.viewWarnings')}</Link>}
        </div>}
      </article>
    </div>}
    {overview && overview.informationCount > 0 && <p className="mrp-current-exceptions__information">{t('mrpRuns.informationEvents', {
      count: formatOperationalNumber(overview.informationCount, language),
    })}</p>}
  </section>
}

function ExceptionGroupPreview({ groups, language, displayDate, t }: {
  groups: ExceptionList; language: string; displayDate: (value: string | null | undefined) => string
  t: ReturnType<typeof useTranslation>['t']
}) {
  if (!groups.items.length) return <p className="mrp-exception-overview-card__empty">{t('mrpRuns.noIssuesInLevel')}</p>
  return <>
    <ul className="mrp-exception-preview-list">
      {groups.items.map(row => <li key={`${row.itemId ?? 'global'}-${row.code}`}>
        <div><strong>{t(`mrpRuns.exceptionCodes.${row.code}`, { defaultValue: row.code })}</strong>{row.itemCode && <bdi className="technical-value">{row.itemCode}</bdi>}</div>
        <small>{t('mrpRuns.issueOccurrences', { count: formatOperationalNumber(row.count, language) })}{row.firstDate && <> · {row.count > 1
          ? t('mrpRuns.issueDateRange', { from: displayDate(row.firstDate), to: displayDate(row.lastDate) })
          : displayDate(row.firstDate)}</>}</small>
      </li>)}
    </ul>
    {groups.totalCount > groups.items.length && <p className="mrp-exception-overview-card__more">{t('mrpRuns.moreIssueGroups', {
      count: formatOperationalNumber(groups.totalCount - groups.items.length, language),
    })}</p>}
  </>
}

function RunRefreshStatus({ status, language, t }: { status: RefreshStatus; language: string; t: ReturnType<typeof useTranslation>['t'] }) {
  const tone: StatusTone = status.status === 'FAILED' ? 'critical'
    : status.status === 'PENDING' || status.status === 'REFRESHING' ? 'warning'
      : status.status === 'NO_RESULT' ? 'neutral' : 'positive'
  const isCalculating = status.status === 'PENDING' || status.status === 'REFRESHING'
  return <div className={`mrp-refresh-status mrp-refresh-status--${status.status.toLowerCase()}`} role="status">
    {isCalculating && <img className="mrp-refresh-companion" src={hadronCompanionThinking} alt="" width="44" height="55" aria-hidden="true" />}
    <StatusBadge text={t(`materialPlanning.refresh.${status.status}`, { defaultValue: status.status })} tone={tone} />
    <span>{isCalculating
      ? t('materialPlanning.refresh.pendingHint', { count: status.pendingChangeCount })
      : t('materialPlanning.refresh.lastUpdated', { date: formatBusinessDateTime(status.lastUpdatedAtUtc, language) })}</span>
    {status.errorCode && <strong className="technical-value">{status.errorCode}</strong>}
    {status.certifiedBusinessDate && <span>{t('materialPlanning.certifiedStatus', {
      date: formatBusinessDate(status.certifiedBusinessDate, language),
      time: formatBusinessDateTime(status.certifiedAtUtc, language),
      hash: status.certifiedResultHash ? status.certifiedResultHash.slice(0, 12) : t('common.noData'),
    })}</span>}
  </div>
}

function CreateRunPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [asOf, setAsOf] = useState('')
  const [horizon, setHorizon] = useState('')
  const [asOfJalali, setAsOfJalali] = useState('')
  const [horizonJalali, setHorizonJalali] = useState('')
  const [defaults, setDefaults] = useState<RunDefaults | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [decisions, setDecisions] = useState<RouteDecision[]>([])
  const [itemId, setItemId] = useState('')
  const [route, setRoute] = useState<RouteDecision['route']>('BOM')
  const [candidateSearch, setCandidateSearch] = useState('')
  const [candidates, setCandidates] = useState<Candidate[]>([])

  useEffect(() => {
    void fetchRunDefaults().then(body => {
      setDefaults(body)
      setAsOf(body.asOfDate)
      setAsOfJalali(body.asOfJalaliDate)
    }).catch(caught => setError(caught instanceof Error ? caught.message : t('common.error')))
  }, [t])
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
      let canonicalAsOf = asOf
      let canonicalHorizon = horizon || null
      if (isPersianLanguage(i18n.resolvedLanguage ?? i18n.language)) {
        const normalizedAsOf = normalizeJalaliDateInput(asOfJalali)
        const normalizedHorizon = horizonJalali.trim() ? normalizeJalaliDateInput(horizonJalali) : null
        if (!normalizedAsOf || (horizonJalali.trim() && !normalizedHorizon))
          throw new Error(t('mrpRuns.jalaliDateInvalid'))
        canonicalAsOf = (await resolveJalaliDate(normalizedAsOf)).gregorianDate
        canonicalHorizon = normalizedHorizon ? (await resolveJalaliDate(normalizedHorizon)).gregorianDate : null
      }
      const response = await fetch(`${api}/mrp-runs`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ asOfDate: canonicalAsOf, horizonEndDate: canonicalHorizon, routeDecisions: decisions }),
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
  async function refreshDefaultHorizon(canonicalAsOf: string) {
    const value = await fetchRunDefaults(canonicalAsOf)
    setDefaults(value)
    setAsOf(value.asOfDate)
    setAsOfJalali(value.asOfJalaliDate)
  }
  async function refreshPersianDefaultHorizon() {
    const normalized = normalizeJalaliDateInput(asOfJalali)
    if (!normalized) { setError(t('mrpRuns.jalaliDateInvalid')); return }
    try {
      const resolved = await resolveJalaliDate(normalized)
      await refreshDefaultHorizon(resolved.gregorianDate)
      setError('')
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
  }
  const language = i18n.resolvedLanguage ?? i18n.language
  const candidateName = (candidate: Candidate) => localizedName(candidate, language)
  const persian = isPersianLanguage(language)

  return <main>
    <PageHeader title={t('mrpRuns.new')} subtitle={t('mrpRuns.createHint')}
      backTo="/mrp/planning/mrp-runs" backLabel={t('common.backToList')} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <form onSubmit={submit}>
      <Section title={t('mrpRuns.parameters')}><div className="form-grid">
        <FormField label={t('mrpRuns.asOf')} required>
          {persian
            ? <input inputMode="numeric" dir="ltr" placeholder="14050608" value={asOfJalali} onChange={event => setAsOfJalali(event.target.value)} onBlur={() => void refreshPersianDefaultHorizon()} required />
            : <input type="date" value={asOf} onChange={event => setAsOf(event.target.value)} onBlur={() => { if (asOf) void refreshDefaultHorizon(asOf).catch(caught => setError(caught instanceof Error ? caught.message : t('common.error'))) }} required />}
        </FormField>
        <FormField label={t('mrpRuns.horizon')} hint={t('mrpRuns.defaultHorizon')}>
          {persian
            ? <input inputMode="numeric" dir="ltr" placeholder={defaults?.defaultHorizonEndJalaliDate ?? '14051229'} value={horizonJalali} onChange={event => setHorizonJalali(event.target.value)} />
            : <input type="date" value={horizon} min={asOf} onChange={event => setHorizon(event.target.value)} />}
        </FormField>
        {defaults && !horizonJalali && !horizon && <div className="mrp-default-horizon"><span>{t('mrpRuns.calculatedDefaultHorizon')}</span><strong className="technical-value">{persian ? defaults.defaultHorizonEndJalaliDate : formatBusinessDate(defaults.defaultHorizonEndDate, 'en')}</strong></div>}
      </div></Section>
      <MessageStrip tone="information">{t('mrpRuns.dateStorageHint')}</MessageStrip>
      <MessageStrip tone="warning"><strong>{t('mrpRuns.beforeRunTitle')}</strong><span>{t('mrpRuns.beforeRunHint')}</span> <Link to="/mrp/engineering/bom">{t('mrpRuns.openBom')}</Link></MessageStrip>
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
        <button type="submit" className="button button--primary" disabled={busy || (persian ? !asOfJalali : !asOf)}>
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
  const language = i18n.resolvedLanguage ?? i18n.language
  const load = useCallback(async () => {
    if (!id) return
    setLoading(true)
    try {
      const responses = await Promise.all([
        fetch(`${api}/mrp-runs/${id}`),
        fetch(`${api}/mrp-runs/${id}/items?page=${query.page}&pageSize=${query.pageSize}`),
        fetch(`${api}/mrp-runs/${id}/exception-summary?page=${exceptionPage}&pageSize=25${exceptionSeverity ? `&severity=${encodeURIComponent(exceptionSeverity)}` : ''}`),
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
  const display = (item: Item) => localizedName(item, language)
  const displayDate = (value: string | null | undefined) => formatBusinessDate(value, language)
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
    <PageHeader sticky title={t('mrpRuns.openTechnicalRun')} eyebrow={run?.runNumber}
      subtitle={run ? `${t('mrpRuns.asOf')}: ${displayDate(run.asOfDate)} · ${t('mrpRuns.horizon')}: ${displayDate(run.horizonEndDate)}` : undefined}
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
          <td className="technical-value">{row.item.code}</td><td>{display(row.item)}</td><td className="ui-number">{formatOperationalNumber(row.startingOnHand, language)}</td>
          <td className="ui-number">{formatOperationalNumber(row.totalGrossDemand, language)}</td><td className="ui-number">{formatOperationalNumber(row.totalFirmIncoming, language)}</td><td className="ui-number">{formatOperationalNumber(row.totalNetRequirement, language)}</td>
          <td className="technical-value">{displayDate(row.stockoutDate)}</td><td>{row.firmCoverageState ?? '—'}</td>
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
          <td className="technical-value">{displayDate(row.date)}</td><td className="ui-number">{formatOperationalNumber(row.grossDemand, language)}</td><td className="ui-number">{formatOperationalNumber(row.firmIncoming, language)}</td>
          <td className="ui-number">{formatOperationalNumber(row.firmProjected, language)}</td><td className="ui-number">{formatOperationalNumber(row.netRequirement, language)}</td><td className="ui-number">{formatOperationalNumber(row.planningBalance, language)}</td>
          <td><button className="button button--small" type="button" onClick={() => void inspectTrace(timeline.item.id, row.date)}>{t('mrpRuns.lineage')}</button></td>
        </tr>)}
      </DataTable>
    </></Section>}
    {trace && <Section title={`${t('mrpRuns.lineage')} — ${trace.item.code} — ${displayDate(trace.demandDate)}`}><div className="trace-report-stack">
      <TraceResultTable title={t('mrpRuns.origin')} headers={[t('common.code'), t('mrpRuns.forecastPeriod'), t('mrpRuns.allocatedDemand'), t('mrpRuns.allocationState')]} empty={!trace.origins.length}>{trace.origins.map((row, index) => <tr key={`${row.productCode}-${index}`}><td className="ui-technical">{row.productCode}</td><td>{displayDate(row.periodStartDate)} — {displayDate(row.periodEndDate)}</td><td className="ui-number">{formatOperationalNumber(row.allocatedDemandQty, language)}</td><td>{row.allocationState}</td></tr>)}</TraceResultTable>
      <TraceResultTable title={t('mrpRuns.bomLineage')} headers={['BOM', t('bom.parent'), t('bom.child'), t('bom.usage'), t('materials.scrap'), t('mrpRuns.gross')]} empty={!trace.bom.length}>{trace.bom.map((row, index) => <tr key={`${row.revisionCode}-${index}`}><td className="ui-technical">{row.revisionCode}</td><td className="ui-technical">{row.parentItemCode}</td><td className="ui-technical">{row.childItemCode}</td><td className="ui-number">{formatOperationalNumber(row.usageQty, language)} / {formatOperationalNumber(row.baseOutputQty, language)}</td><td className="ui-number">{formatOperationalNumber(row.scrapPercent, language)}%<small>{row.scrapSource}</small></td><td className="ui-number">{formatOperationalNumber(row.grossContribution, language)}</td></tr>)}</TraceResultTable>
      <TraceResultTable title={t('mrpRuns.inventoryLineage')} description={`${t('mrpRuns.companyOnHand')}: ${formatOperationalNumber(trace.companyStartingOnHand, language)}`} headers={[t('inventory.warehouse'), t('inventory.quantity'), t('common.status')]} empty={!trace.inventory.length}>{trace.inventory.map(row => <tr key={row.warehouseCode}><td className="ui-technical">{row.warehouseCode}</td><td className="ui-number">{formatOperationalNumber(row.onHandQty, language)}</td><td>{row.warehouseIsActive ? t('common.active') : <StatusBadge text={t('mrpRuns.inactiveWarehouse')} tone="warning" />}</td></tr>)}</TraceResultTable>
      <TraceResultTable title={t('mrpRuns.firmLineage')} headers={[t('purchaseOrders.poNumber'), t('purchaseOrders.openQty'), 'ETA', t('common.status')]} empty={!trace.firmSupply.length}>{trace.firmSupply.map(row => <tr key={row.deliveryScheduleId}><td className="ui-technical">{row.purchaseOrderNumber}</td><td className="ui-number">{formatOperationalNumber(row.openQty, language)}</td><td>{displayDate(row.eta)}<small>{row.etaStatus}</small></td><td><StatusBadge text={row.appliedByDemandDate ? t('mrpRuns.applied') : t('mrpRuns.excluded')} tone={row.appliedByDemandDate ? 'positive' : 'neutral'} /></td></tr>)}</TraceResultTable>
      <TraceResultTable title={t('mrpRuns.nettingLineage')} headers={[t('mrpRuns.gross'), t('mrpRuns.firm'), t('materialPlanning.safetyStock'), t('mrpRuns.net'), t('mrpRuns.planningBalance')]} empty={!trace.netting}>{trace.netting && <tr><td className="ui-number">{formatOperationalNumber(trace.netting.grossRequirement, language)}</td><td className="ui-number">{formatOperationalNumber(trace.netting.firmProjectedBefore, language)} → {formatOperationalNumber(trace.netting.firmProjectedAfter, language)}</td><td className="ui-number">{formatOperationalNumber(trace.netting.safetyStock, language)}</td><td className="ui-number">{formatOperationalNumber(trace.netting.netRequirement, language)}</td><td className="ui-number">{formatOperationalNumber(trace.netting.planningBalance, language)}</td></tr>}</TraceResultTable>
      <TraceResultTable title={t('mrpRuns.routeLineage')} headers={[t('materials.supplyMethod'), t('mrpRuns.route'), t('materialPlanning.source'), t('mrpRuns.exceptions')]} empty={false}><tr><td>{trace.route.supplyMethod ?? '—'}</td><td>{trace.route.chosenRoute ?? '—'}</td><td>{trace.route.decisionSource ?? '—'}</td><td>{trace.route.exceptionCodes.join(', ') || '—'}</td></tr></TraceResultTable>
    </div></Section>}
    <Section title={t('mrpRuns.exceptions')}><>
      <FilterBar label={t('mrpRuns.exceptionSeverity')}>
        <FormField label={t('mrpRuns.exceptionSeverity')}>
          <select value={exceptionSeverity} onChange={event => changeExceptionSeverity(event.target.value)}>
            <option value="">{t('mrpRuns.allSeverities')}</option>
            <option value="ERROR">ERROR</option><option value="WARNING">WARNING</option><option value="INFO">INFO</option>
          </select>
        </FormField>
      </FilterBar>
      {exceptions.items.length ? exceptions.items.map(row => <MessageStrip key={`${row.itemId ?? 'global'}-${row.code}-${row.severity}`}
        tone={row.severity === 'ERROR' ? 'critical' : row.severity === 'WARNING' ? 'warning' : 'information'}>
        <strong>{t(`mrpRuns.exceptionCodes.${row.code}`, { defaultValue: row.code })}</strong> {row.itemCode && <bdi className="technical-value">— {row.itemCode}</bdi>}
        <span className="mrp-exception-range">{row.count > 1 ? t('mrpRuns.repeatedException', { count: row.count, from: displayDate(row.firstDate), to: displayDate(row.lastDate) }) : displayDate(row.firstDate)}</span>
        <p>{t(`mrpRuns.exceptionMessages.${row.code}`, { defaultValue: row.message })}</p>
      </MessageStrip>) : <p>{t('mrpRuns.noExceptions')}</p>}
      <Pagination page={exceptionPage} pageSize={exceptions.pageSize} totalCount={exceptions.totalCount}
        onPageChange={changeExceptionPage} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: exceptionPage, pages: Math.max(1, Math.ceil(exceptions.totalCount / exceptions.pageSize)), count: exceptions.totalCount })} />
    </></Section>
  </main>
}

function TraceResultTable({ title, description, headers, empty, children }: { title: string; description?: string; headers: string[]; empty: boolean; children: ReactNode }) {
  return <section className="trace-report"><header><h3>{title}</h3>{description && <span>{description}</span>}</header><div className="trace-report__table-wrap"><table className="ui-data-table"><thead><tr>{headers.map(header => <th scope="col" key={header}>{header}</th>)}</tr></thead><tbody>{empty ? <tr><td colSpan={headers.length}>—</td></tr> : children}</tbody></table></div></section>
}

async function loadExceptionOverview(runId: string): Promise<ExceptionOverview> {
  const query = (resource: 'exceptions' | 'exception-summary', severity: 'ERROR' | 'WARNING', pageSize: number) =>
    `${api}/mrp-runs/${runId}/${resource}?severity=${severity}&page=1&pageSize=${pageSize}`
  const responses = await Promise.all([
    fetch(query('exceptions', 'ERROR', 1)),
    fetch(query('exceptions', 'WARNING', 1)),
    fetch(`${api}/mrp-runs/${runId}/exceptions?severity=INFO&page=1&pageSize=1`),
    fetch(query('exception-summary', 'ERROR', 6)),
    fetch(query('exception-summary', 'WARNING', 6)),
  ])
  for (const response of responses) if (!response.ok) throw new Error(await problem(response))
  const [blockingEvents, warningEvents, informationEvents, blockingGroups, warningGroups] = await Promise.all([
    responses[0].json() as Promise<{ totalCount: number }>,
    responses[1].json() as Promise<{ totalCount: number }>,
    responses[2].json() as Promise<{ totalCount: number }>,
    responses[3].json() as Promise<ExceptionList>,
    responses[4].json() as Promise<ExceptionList>,
  ])
  return {
    blockingCount: blockingEvents.totalCount,
    warningCount: warningEvents.totalCount,
    informationCount: informationEvents.totalCount,
    blockingGroups,
    warningGroups,
  }
}

async function resolveJalaliDate(jalaliDate: string) {
  const response = await fetch(`${api}/planning/resolve-jalali-date`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ jalaliDate }),
  })
  if (!response.ok) throw new Error(await problem(response))
  return response.json() as Promise<ResolvedJalaliDate>
}

async function fetchRunDefaults(asOfDate?: string) {
  const query = asOfDate ? `?asOfDate=${encodeURIComponent(asOfDate)}` : ''
  const response = await fetch(`${api}/mrp-runs/default-parameters${query}`)
  if (!response.ok) throw new Error(await problem(response))
  return response.json() as Promise<RunDefaults>
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
