import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { formatBusinessDate, formatBusinessDateTime } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { chooseInitialRunId, readRememberedRunId, rememberRunId, sourceNavigation } from './materialPlanningSupport'
import { DataTable, EmptyState, FilterBar, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar, type StatusTone } from './ui'

const api = '/api/material-planning'
const pageSize = 25
const runPreferenceStorage = () => typeof window === 'undefined' ? null : window.localStorage

type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
type Run = { id: string; runNumber: string; asOfDate: string; horizonEndDate: string; engineVersion: string; status: string; completeness: string; completedAtUtc: string; exceptionCount: number }
type RunHeader = Run & { startedAtUtc: string; snapshotCapturedAtUtc?: string; isLatest: boolean; materialCount: number }
type Item = { id: string; code: string; nameFa: string; nameEn?: string; itemType: string; unitCode: string; unitNameFa: string; unitNameEn?: string; categoryId?: string; categoryCode?: string; categoryNameFa?: string; categoryNameEn?: string; supplyMethod?: string }
type TraceItem = { id: string; code: string; nameFa: string; nameEn?: string; itemType: string; unitCode: string; unitNameFa: string; unitNameEn?: string }
type Facets = { categories: Array<{ id: string; code: string; nameFa: string; nameEn?: string }>; supplyMethods: string[] }
type Summary = { item: Item; startingOnHand: number; totalGrossDemand: number; totalFirmIncoming: number; totalNetRequirement: number; overdueFirmQty: number; missingEtaFirmQty: number; stockoutDate?: string; safetyBreachDate?: string; stockCoverageState?: string; stockCoverageDate?: string; stockCoverageDays?: number; firmCoverageState?: string; firmCoverageDate?: string; firmCoverageDays?: number; completeness: string; riskBand: string; riskReason: string; exceptionCount: number; primaryExceptionCode?: string }
type Bucket = { startDate: string; endDate: string; grossDemand: number; firmIncoming: number; netRequirement: number; firmProjected: number; planningBalance: number; completeness: string; includesStockoutEvent: boolean; includesSafetyBreachEvent: boolean }
type Timeline = { item: Item; grain: string; safetyStockQty: number; stockCoverageState?: string; stockCoverageDate?: string; stockCoverageDays?: number; firmCoverageState?: string; firmCoverageDate?: string; firmCoverageDays?: number; buckets: Bucket[] }
type ExceptionRow = { id: string; itemId?: string; itemCode?: string; date?: string; code: string; severity: string; affectsDemand: boolean; message: string; sourceModule: string; sourceReference?: string }
type Trace = {
  runId: string
  item: TraceItem
  demandDate: string
  origins: Array<{ forecastRevisionId?: string; periodStartDate: string; periodEndDate: string; allocatedDemandQty: number; allocationState: string }>
  bom: Array<{ revisionCode: string; parentItemCode: string; childItemCode: string; baseRequirement: number; scrapPercent: number; scrapSource: string; grossContribution: number }>
  inventory: Array<{ warehouseId: string; warehouseCode: string; warehouseIsActive: boolean; onHandQty: number }>
  companyStartingOnHand: number
  includesInactiveWarehouseStock: boolean
  firmSupply: Array<{ purchaseOrderId: string; purchaseOrderNumber: string; purchaseOrderLineId: string; deliveryScheduleId: string; supplierNumber?: string; supplierNameFa?: string; supplierNameEn?: string; openQty: number; eta?: string; etaSource: string; etaStatus: string; purchaseOrderStatus: string; appliedByDemandDate: boolean }>
  netting?: { grossRequirement: number; firmIncoming: number; safetyStock: number; netRequirement: number; firmProjectedAfter: number; planningBalance: number }
  route: { chosenRoute?: string; decisionSource?: string; exceptionCodes: string[] }
}

export default function MaterialPlanningPage() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const [params, setParams] = useSearchParams()
  const runId = params.get('runId') ?? ''
  const page = positive(params.get('page'), 1)
  const runPage = positive(params.get('runPage'), 1)
  const exceptionPage = positive(params.get('exceptionPage'), 1)
  const search = params.get('search') ?? ''
  const itemType = params.get('itemType') ?? ''
  const categoryId = params.get('categoryId') ?? ''
  const supplyMethod = params.get('supplyMethod') ?? ''
  const riskBand = params.get('riskBand') ?? ''
  const completeness = params.get('completeness') ?? ''
  const hasExceptions = params.get('hasExceptions') ?? ''
  const stockoutFrom = params.get('stockoutFrom') ?? ''
  const stockoutTo = params.get('stockoutTo') ?? ''
  const sortBy = params.get('sortBy') ?? 'risk'
  const sortDirection = params.get('sortDirection') ?? 'asc'
  const grain = params.get('grain') ?? 'DAILY'
  const itemId = params.get('itemId') ?? ''
  const exceptionSeverity = params.get('exceptionSeverity') ?? ''
  const exceptionCode = params.get('exceptionCode') ?? ''
  const exceptionSource = params.get('exceptionSource') ?? ''
  const exceptionDate = params.get('exceptionDate') ?? ''
  const exceptionAffects = params.get('exceptionAffects') ?? ''
  const [runs, setRuns] = useState<Run[]>([])
  const [runTotal, setRunTotal] = useState(0)
  const [header, setHeader] = useState<RunHeader | null>(null)
  const [facets, setFacets] = useState<Facets>({ categories: [], supplyMethods: [] })
  const [summary, setSummary] = useState<Page<Summary> | null>(null)
  const [timeline, setTimeline] = useState<Timeline | null>(null)
  const [trace, setTrace] = useState<Trace | null>(null)
  const [exceptions, setExceptions] = useState<Page<ExceptionRow> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [runContextUnavailable, setRunContextUnavailable] = useState(false)
  const patch = useCallback((values: Record<string, string | null>) => {
    setParams(current => {
      const next = new URLSearchParams(current)
      Object.entries(values).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key))
      return next
    })
  }, [setParams])

  useEffect(() => {
    void get<Page<Run>>(`${api}/runs?page=${runPage}&pageSize=25`).then(body => {
      setRuns(body.items)
      setRunTotal(body.totalCount)
      if (!runId && runPage === 1) {
        const selected = chooseInitialRunId('', readRememberedRunId(runPreferenceStorage()), body.items[0]?.id)
        if (selected.runId) patch({ runId: selected.runId })
      }
    }).catch(caught => setError(message(caught, t)))
  }, [patch, runId, runPage, t])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void (async () => {
        if (!runId) { setLoading(false); return }
        setLoading(true)
        setError('')
        setRunContextUnavailable(false)
        const query = new URLSearchParams({
          page: String(page), pageSize: String(pageSize), search, itemType, categoryId,
          supplyMethod, riskBand, completeness, stockoutFrom, stockoutTo, sortBy, sortDirection,
        })
        if (hasExceptions) query.set('hasExceptions', hasExceptions)
        const exceptionQuery = new URLSearchParams({ page: String(exceptionPage), pageSize: '25', severity: exceptionSeverity, code: exceptionCode, sourceModule: exceptionSource, date: exceptionDate })
        if (exceptionAffects) exceptionQuery.set('affectsDemand', exceptionAffects)
        if (itemId) exceptionQuery.set('itemId', itemId)
        try {
          const [nextHeader, nextFacets, nextSummary, nextExceptions] = await Promise.all([
            get<RunHeader>(`${api}/runs/${runId}`),
            get<Facets>(`${api}/runs/${runId}/facets`),
            get<Page<Summary>>(`${api}/runs/${runId}/summary?${query}`),
            get<Page<ExceptionRow>>(`${api}/runs/${runId}/exceptions?${exceptionQuery}`),
          ])
          setHeader(nextHeader)
          setFacets(nextFacets)
          setSummary(nextSummary)
          setExceptions(nextExceptions)
          rememberRunId(runPreferenceStorage(), runId)
        } catch (caught) {
          if (caught instanceof ApiError && caught.status === 404) {
            setHeader(null)
            setSummary(null)
            setExceptions(null)
            setRunContextUnavailable(true)
          } else setError(message(caught, t))
        } finally { setLoading(false) }
      })()
    }, 0)
    return () => window.clearTimeout(timer)
  }, [categoryId, completeness, exceptionAffects, exceptionCode, exceptionDate, exceptionPage, exceptionSeverity, exceptionSource, hasExceptions, itemId, itemType, page, riskBand, runId, search, sortBy, sortDirection, stockoutFrom, stockoutTo, supplyMethod, t])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (!runId || !itemId || runContextUnavailable) { setTimeline(null); setTrace(null); return }
      void get<Timeline>(`${api}/runs/${runId}/items/${itemId}/timeline?grain=${grain}`)
        .then(setTimeline).catch(caught => setError(message(caught, t)))
    }, 0)
    return () => window.clearTimeout(timer)
  }, [grain, itemId, runContextUnavailable, runId, t])

  const selected = summary?.items.find(row => row.item.id === itemId)
  const title = header?.runNumber ?? t('materialPlanning.title')
  const filtered = Boolean(search || itemType || categoryId || supplyMethod || riskBand || completeness || hasExceptions || stockoutFrom || stockoutTo)
  const pageCount = Math.max(1, Math.ceil((summary?.totalCount ?? 0) / pageSize))
  const exceptionPages = Math.max(1, Math.ceil((exceptions?.totalCount ?? 0) / 25))
  const inspectTrace = async (date: string) => {
    if (!runId || !itemId) return
    try { setTrace(await get<Trace>(`${api}/runs/${runId}/items/${itemId}/trace?demandDate=${date}`)) }
    catch (caught) { setError(message(caught, t)) }
  }
  const selectRun = (nextRunId: string) => {
    rememberRunId(runPreferenceStorage(), nextRunId)
    setRunContextUnavailable(false)
    patch({ runId: nextRunId, page: null, itemId: null })
  }

  return <main className="material-planning-page">
    <PageHeader title={title} subtitle={t('materialPlanning.subtitle')} eyebrow={t('materialPlanning.snapshotOnly')} status={header && <StatusBadge text={t(`mrpRuns.status.${header.completeness}`, { defaultValue: header.completeness })} tone={header.completeness === 'INCOMPLETE' ? 'critical' : 'positive'} />} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {runContextUnavailable && <MessageStrip tone="critical">{t('materialPlanning.runContextUnavailable')}</MessageStrip>}
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('materialPlanning.run')}><select value={runId} onChange={event => selectRun(event.target.value)}><option value="">{t('materialPlanning.noEligibleRun')}</option>{runContextUnavailable && runId && <option value={runId}>{t('materialPlanning.unavailableRun')}</option>}{header && !runs.some(run => run.id === header.id) && <option value={header.id}>{header.runNumber} — {formatBusinessDate(header.asOfDate, language)}</option>}{runs.map(run => <option value={run.id} key={run.id}>{run.runNumber} — {formatBusinessDate(run.asOfDate, language)}</option>)}</select></FormField>
      <div className="ui-action-row"><button type="button" className="button button--quiet" disabled={runPage <= 1} onClick={() => patch({ runPage: String(runPage - 1) })}>{t('common.previous')}</button><span>{t('materialPlanning.runPage', { page: runPage, pages: Math.max(1, Math.ceil(runTotal / 25)) })}</span><button type="button" className="button button--quiet" disabled={runPage >= Math.ceil(runTotal / 25)} onClick={() => patch({ runPage: String(runPage + 1) })}>{t('common.next')}</button></div>
      <FormField label={t('common.search')}><input value={search} onChange={event => patch({ search: event.target.value, page: null })} placeholder={t('materialPlanning.searchPlaceholder')} /></FormField>
      <FormField label={t('materialPlanning.itemType')}><select value={itemType} onChange={event => patch({ itemType: event.target.value, page: null })}><option value="">{t('common.all')}</option><option value="PART">{t('materials.parts')}</option><option value="COMPONENT">{t('materials.components')}</option></select></FormField>
      <FormField label={t('common.category')}><select value={categoryId} onChange={event => patch({ categoryId: event.target.value, page: null })}><option value="">{t('common.all')}</option>{facets.categories.map(category => <option value={category.id} key={category.id}>{localizedName(category, language)} ({category.code})</option>)}</select></FormField>
      <FormField label={t('materials.supplyMethod')}><select value={supplyMethod} onChange={event => patch({ supplyMethod: event.target.value, page: null })}><option value="">{t('common.all')}</option>{facets.supplyMethods.map(method => <option value={method} key={method}>{t(`materials.supplyMethods.${method}`, { defaultValue: method })}</option>)}</select></FormField>
      <FormField label={t('materialPlanning.risk')}><select value={riskBand} onChange={event => patch({ riskBand: event.target.value, page: null })}><option value="">{t('common.all')}</option>{['INCOMPLETE','STOCKOUT','NO_FUTURE_DEMAND','BEYOND_HORIZON','CRITICAL','LOW','SAFE','EXCESS'].map(value => <option value={value} key={value}>{t(`materialPlanning.risks.${value}`)}</option>)}</select></FormField>
      <FormField label={t('mrpRuns.completeness')}><select value={completeness} onChange={event => patch({ completeness: event.target.value, page: null })}><option value="">{t('common.all')}</option>{['COMPLETE','COMPLETE_WITH_WARNINGS','INCOMPLETE'].map(value => <option value={value} key={value}>{t(`mrpRuns.status.${value}`)}</option>)}</select></FormField>
      <FormField label={t('materialPlanning.stockoutFrom')}><input type="date" dir="ltr" value={stockoutFrom} onChange={event => patch({ stockoutFrom: event.target.value, page: null })} /></FormField>
      <FormField label={t('materialPlanning.stockoutTo')}><input type="date" dir="ltr" value={stockoutTo} onChange={event => patch({ stockoutTo: event.target.value, page: null })} /></FormField>
      <FormField label={t('materialPlanning.exceptionsOnly')}><select value={hasExceptions} onChange={event => patch({ hasExceptions: event.target.value, page: null })}><option value="">{t('common.all')}</option><option value="true">{t('common.yes')}</option><option value="false">{t('common.no')}</option></select></FormField>
    </FilterBar>
    {!runId ? <EmptyState title={t('materialPlanning.noEligibleRun')} description={t('materialPlanning.noEligibleRunHint')} /> : runContextUnavailable ? <EmptyState title={t('materialPlanning.unavailableRun')} description={t('materialPlanning.runContextUnavailable')} /> : loading ? <LoadingState label={t('common.loading')} /> : <>
      {header && <RunContext header={header} language={language} t={t} />}
      <Section title={t('materialPlanning.summary')} description={t('materialPlanning.riskHint')}><>
        <TableToolbar title={t('materialPlanning.materials')} count={t('common.count', { count: summary?.totalCount ?? 0 })} actions={<div className="ui-action-row"><FormField label={t('materialPlanning.sortBy')}><select value={sortBy} onChange={event => patch({ sortBy: event.target.value, page: null })}>{['risk','code','name','stockout','firmcoverage','netrequirement'].map(value => <option value={value} key={value}>{t(`materialPlanning.sorts.${value}`)}</option>)}</select></FormField><FormField label={t('materialPlanning.sortDirection')}><select value={sortDirection} onChange={event => patch({ sortDirection: event.target.value, page: null })}><option value="asc">{t('materialPlanning.ascending')}</option><option value="desc">{t('materialPlanning.descending')}</option></select></FormField></div>} />
        <DataTable headers={[t('common.code'), t('materialPlanning.material'), t('materialPlanning.itemType'), t('materials.supplyMethod'), t('mrpRuns.stockout'), t('materialPlanning.firmCoverage'), t('materialPlanning.risk'), t('common.actions')]} loading={false} empty={!summary?.items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} emptyDescription={t('common.emptyDescription')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
          {summary?.items.map(row => <tr key={row.item.id} className={row.item.id === itemId ? 'is-selected' : undefined}><td className="technical-value">{row.item.code}</td><td><strong>{localizedName(row.item, language)}</strong><small>{localizedReference(row.item.categoryNameFa, row.item.categoryNameEn, language)}</small></td><td>{t(`materials.${row.item.itemType === 'PART' ? 'part' : 'component'}`)}</td><td>{row.item.supplyMethod ? t(`materials.supplyMethods.${row.item.supplyMethod}`, { defaultValue: row.item.supplyMethod }) : '—'}</td><td>{formatBusinessDate(row.stockoutDate, language)}</td><td><Coverage state={row.firmCoverageState} days={row.firmCoverageDays} date={row.firmCoverageDate} language={language} t={t} /></td><td><StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} /><small>{t(`materialPlanning.reasons.${row.riskReason}`, { defaultValue: row.riskReason })}</small></td><td><button type="button" className="button button--small" onClick={() => patch({ itemId: row.item.id })}>{t('materialPlanning.inspect')}</button></td></tr>)}
        </DataTable>
        <Pagination page={page} pageSize={pageSize} totalCount={summary?.totalCount ?? 0} onPageChange={next => patch({ page: String(next) })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: pageCount, count: summary?.totalCount ?? 0 })} />
      </></Section>
      {selected && <MaterialDetail row={selected} language={language} t={t} />}
      {timeline && <TimelineSection timeline={timeline} grain={grain} language={language} onGrainChange={value => patch({ grain: value })} onTrace={inspectTrace} t={t} />}
      {selected && trace && <Section title={`${t('materialPlanning.trace')} — ${selected.item.code} — ${formatBusinessDate(trace.demandDate, language)}`}><TraceView trace={trace} language={language} t={t} /></Section>}
      <ExceptionCenter rows={exceptions} page={exceptionPage} pages={exceptionPages} language={language} filters={{ exceptionSeverity, exceptionCode, exceptionSource, exceptionDate, exceptionAffects }} patch={patch} t={t} />
    </>}
  </main>
}

function RunContext({ header, language, t }: { header: RunHeader; language: string; t: Translate }) {
  return <Section title={t('materialPlanning.runContext')}><div className="detail-grid"><Detail label={t('mrpRuns.number')} value={header.runNumber} technical /><Detail label={t('mrpRuns.asOf')} value={formatBusinessDate(header.asOfDate, language)} /><Detail label={t('mrpRuns.horizon')} value={formatBusinessDate(header.horizonEndDate, language)} /><Detail label={t('mrpRuns.engine')} value={header.engineVersion} technical /><Detail label={t('common.status')} value={t(`mrpRuns.status.${header.status}`, { defaultValue: header.status })} /><Detail label={t('mrpRuns.completeness')} value={t(`mrpRuns.status.${header.completeness}`, { defaultValue: header.completeness })} /><Detail label={t('materialPlanning.completedAt')} value={formatBusinessDateTime(header.completedAtUtc, language)} /><Detail label={t('materialPlanning.materialCount')} value={header.materialCount} /><Detail label={t('mrpRuns.exceptions')} value={header.exceptionCount} /><Detail label={t('materialPlanning.history')} value={header.isLatest ? t('materialPlanning.latest') : t('materialPlanning.historical')} /></div></Section>
}

function MaterialDetail({ row, language, t }: { row: Summary; language: string; t: Translate }) {
  return <Section title={`${t('materialPlanning.operationalDetail')} — ${row.item.code}`}><div className="detail-grid"><Detail label={t('common.code')} value={row.item.code} technical /><Detail label={t('materialPlanning.material')} value={localizedName(row.item, language)} /><Detail label={t('materialPlanning.itemType')} value={t(`materials.${row.item.itemType === 'PART' ? 'part' : 'component'}`)} /><Detail label={t('common.category')} value={`${row.item.categoryCode ?? '—'} ${localizedReference(row.item.categoryNameFa, row.item.categoryNameEn, language)}`} /><Detail label={t('materials.supplyMethod')} value={row.item.supplyMethod ? t(`materials.supplyMethods.${row.item.supplyMethod}`, { defaultValue: row.item.supplyMethod }) : '—'} /><Detail label={t('common.unit')} value={`${row.item.unitCode} — ${localizedReference(row.item.unitNameFa, row.item.unitNameEn, language)}`} /><Detail label={t('mrpRuns.starting')} value={row.startingOnHand} /><Detail label={t('mrpRuns.firm')} value={row.totalFirmIncoming} /><Detail label={t('mrpRuns.net')} value={row.totalNetRequirement} /><Detail label={t('mrpRuns.stockout')} value={formatBusinessDate(row.stockoutDate, language)} /><Detail label={t('materialPlanning.safetyBreach')} value={formatBusinessDate(row.safetyBreachDate, language)} /><Detail label={t('materialPlanning.stockCoverage')} value={<Coverage state={row.stockCoverageState} days={row.stockCoverageDays} date={row.stockCoverageDate} language={language} t={t} />} /><Detail label={t('materialPlanning.firmCoverage')} value={<Coverage state={row.firmCoverageState} days={row.firmCoverageDays} date={row.firmCoverageDate} language={language} t={t} />} /><Detail label={t('materialPlanning.overdue')} value={row.overdueFirmQty} /><Detail label={t('materialPlanning.missingEta')} value={row.missingEtaFirmQty} /><Detail label={t('materialPlanning.risk')} value={t(`materialPlanning.risks.${row.riskBand}`)} /><Detail label={t('mrpRuns.completeness')} value={t(`mrpRuns.status.${row.completeness}`, { defaultValue: row.completeness })} /><Detail label={t('mrpRuns.exceptions')} value={`${row.exceptionCount}${row.primaryExceptionCode ? ` — ${row.primaryExceptionCode}` : ''}`} /></div></Section>
}

function TimelineSection({ timeline, grain, language, onGrainChange, onTrace, t }: { timeline: Timeline; grain: string; language: string; onGrainChange: (value: string) => void; onTrace: (date: string) => Promise<void>; t: Translate }) {
  return <Section title={`${t('materialPlanning.timeline')} — ${timeline.item.code}`} actions={<select aria-label={t('materialPlanning.grain')} value={grain} onChange={event => onGrainChange(event.target.value)}>{['DAILY','WEEKLY','MONTHLY'].map(value => <option key={value} value={value}>{t(`materialPlanning.grains.${value}`)}</option>)}</select>}><>
    <div className="coverage-comparison"><div><span>{t('materialPlanning.stockCoverage')}</span><Coverage state={timeline.stockCoverageState} days={timeline.stockCoverageDays} date={timeline.stockCoverageDate} language={language} t={t} /></div><div><span>{t('materialPlanning.firmCoverage')}</span><Coverage state={timeline.firmCoverageState} days={timeline.firmCoverageDays} date={timeline.firmCoverageDate} language={language} t={t} /></div></div>
    <DataTable headers={[t('materialPlanning.bucket'), t('mrpRuns.gross'), t('mrpRuns.firm'), t('mrpRuns.net'), t('materialPlanning.safetyStock'), t('mrpRuns.firmProjected'), t('mrpRuns.planningBalance'), t('materialPlanning.events'), t('common.actions')]} empty={!timeline.buckets.length} loadingLabel={t('common.loading')} emptyTitle={t('materialPlanning.noTimeline')} noResultTitle={t('common.noResultsTitle')}><>{timeline.buckets.map(bucket => <tr key={`${bucket.startDate}-${bucket.endDate}`}><td>{bucket.startDate === bucket.endDate ? formatBusinessDate(bucket.startDate, language) : `${formatBusinessDate(bucket.startDate, language)} — ${formatBusinessDate(bucket.endDate, language)}`}</td><td>{bucket.grossDemand}</td><td>{bucket.firmIncoming}</td><td>{bucket.netRequirement}</td><td>{timeline.safetyStockQty}</td><td>{bucket.firmProjected}</td><td>{bucket.planningBalance}</td><td>{bucket.includesStockoutEvent && <StatusBadge text={t('materialPlanning.stockoutEvent')} tone="critical" />}{bucket.includesSafetyBreachEvent && <StatusBadge text={t('materialPlanning.safetyBreachEvent')} tone="warning" />}</td><td>{grain === 'DAILY' && <button className="button button--small" type="button" onClick={() => void onTrace(bucket.startDate)}>{t('mrpRuns.lineage')}</button>}</td></tr>)}</></DataTable>
  </></Section>
}

function TraceView({ trace, language, t }: { trace: Trace; language: string; t: Translate }) {
  return <div className="trace-grid"><div><h3>{t('materialPlanning.demandTrace')}</h3>{trace.origins.map((row, index) => <p key={index}>{t('materialPlanning.forecastRevision')}: {shortId(row.forecastRevisionId)} · {formatBusinessDate(row.periodStartDate, language)} — {formatBusinessDate(row.periodEndDate, language)} · {row.allocatedDemandQty} · {row.allocationState}</p>)}{trace.bom.map((row, index) => <p key={index} className="technical-value">{row.revisionCode}: {row.parentItemCode} → {row.childItemCode}; {row.baseRequirement} × scrap {row.scrapPercent}% ({row.scrapSource}) = {row.grossContribution}</p>)}</div><div><h3>{t('materialPlanning.supplyTrace')}</h3><p>{t('mrpRuns.companyOnHand')}: {trace.companyStartingOnHand}</p>{trace.inventory.map(row => <p key={row.warehouseId}>{row.warehouseCode} · {row.onHandQty}{!row.warehouseIsActive ? ` · ${t('mrpRuns.inactiveWarehouse')}` : ''}</p>)}{trace.firmSupply.map(row => <div className="trace-supply-row" key={row.deliveryScheduleId}><p><strong>{row.purchaseOrderNumber}</strong> · {row.supplierNumber ?? '—'} {localizedReference(row.supplierNameFa, row.supplierNameEn, language)}</p><p className="technical-value">{t('materialPlanning.poLineId')}: {row.purchaseOrderLineId} · {t('materialPlanning.scheduleId')}: {row.deliveryScheduleId}</p><p>{t('purchaseOrders.openQty')}: {row.openQty} · ETA {formatBusinessDate(row.eta, language)} · {row.etaStatus} · {row.appliedByDemandDate ? t('mrpRuns.applied') : t('mrpRuns.excluded')}</p></div>)}</div><div><h3>{t('mrpRuns.nettingLineage')}</h3>{trace.netting ? <p className="technical-value">Gross {trace.netting.grossRequirement}; Firm {trace.netting.firmIncoming}; Safety {trace.netting.safetyStock}; Net {trace.netting.netRequirement}; FirmProjected {trace.netting.firmProjectedAfter}; PlanningBalance {trace.netting.planningBalance}</p> : <p>—</p>}</div><div><h3>{t('mrpRuns.routeLineage')}</h3><p>{trace.route.chosenRoute ?? '—'} · {trace.route.decisionSource ?? '—'}</p></div></div>
}

function ExceptionCenter({ rows, page, pages, language, filters, patch, t }: { rows: Page<ExceptionRow> | null; page: number; pages: number; language: string; filters: Record<string, string>; patch: (values: Record<string, string | null>) => void; t: Translate }) {
  return <Section title={t('materialPlanning.exceptionCenter')} description={t('materialPlanning.readOnlyExceptions')}><>
    <FilterBar label={t('materialPlanning.exceptionFilters')}><FormField label={t('mrpRuns.exceptionSeverity')}><select value={filters.exceptionSeverity} onChange={event => patch({ exceptionSeverity: event.target.value, exceptionPage: null })}><option value="">{t('common.all')}</option>{['ERROR','WARNING','INFO'].map(value => <option key={value} value={value}>{value}</option>)}</select></FormField><FormField label={t('common.code')}><input value={filters.exceptionCode} onChange={event => patch({ exceptionCode: event.target.value, exceptionPage: null })} /></FormField><FormField label={t('materialPlanning.source')}><select value={filters.exceptionSource} onChange={event => patch({ exceptionSource: event.target.value, exceptionPage: null })}><option value="">{t('common.all')}</option>{['FORECAST','BOM','INVENTORY','PURCHASE_SUPPLY','CATALOG','MRP'].map(value => <option key={value} value={value}>{value}</option>)}</select></FormField><FormField label={t('mrpRuns.date')}><input type="date" dir="ltr" value={filters.exceptionDate} onChange={event => patch({ exceptionDate: event.target.value, exceptionPage: null })} /></FormField><FormField label={t('materialPlanning.affectsDemand')}><select value={filters.exceptionAffects} onChange={event => patch({ exceptionAffects: event.target.value, exceptionPage: null })}><option value="">{t('common.all')}</option><option value="true">{t('common.yes')}</option><option value="false">{t('common.no')}</option></select></FormField></FilterBar>
    <DataTable headers={[t('common.code'), t('materialPlanning.source'), t('mrpRuns.date'), t('materialPlanning.material'), t('mrpRuns.exceptionSeverity'), t('materialPlanning.affectsDemand'), t('materialPlanning.message'), t('common.actions')]} empty={!rows?.items.length} loadingLabel={t('common.loading')} emptyTitle={t('mrpRuns.noExceptions')} noResultTitle={t('common.noResultsTitle')}><>{rows?.items.map(row => { const destination = sourceNavigation(row.sourceModule, row.sourceReference); return <tr key={row.id}><td className="technical-value">{row.code}</td><td>{row.sourceModule}<small>{row.sourceReference}</small></td><td>{formatBusinessDate(row.date, language)}</td><td className="technical-value">{row.itemCode ?? '—'}</td><td><StatusBadge text={row.severity} tone={row.severity === 'ERROR' ? 'critical' : row.severity === 'WARNING' ? 'warning' : 'information'} /></td><td>{row.affectsDemand ? t('common.yes') : t('common.no')}</td><td>{row.message}</td><td>{destination ? <Link className="button button--small" to={destination}>{t('materialPlanning.openSource')}</Link> : '—'}</td></tr> })}</></DataTable>
    <Pagination page={page} pageSize={25} totalCount={rows?.totalCount ?? 0} onPageChange={next => patch({ exceptionPage: String(next) })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages, count: rows?.totalCount ?? 0 })} />
  </></Section>
}

type Translate = (key: string, options?: Record<string, unknown>) => string
function Detail({ label, value, technical = false }: { label: string; value: ReactNode; technical?: boolean }) { return <div><span>{label}</span><strong className={technical ? 'technical-value' : undefined}>{value}</strong></div> }
function Coverage({ state, days, date, language, t }: { state?: string; days?: number; date?: string; language: string; t: Translate }) { return <span className="coverage-value"><strong>{t(`materialPlanning.coverage.${state ?? 'INCOMPLETE_DATA'}`, { defaultValue: state ?? 'INCOMPLETE_DATA' })}</strong>{state === 'DATE' && <small>{days ?? 0} {t('materialPlanning.days')} · {formatBusinessDate(date, language)}</small>}</span> }
function riskTone(risk: string): StatusTone { return risk === 'INCOMPLETE' || risk === 'STOCKOUT' ? 'critical' : risk === 'CRITICAL' || risk === 'LOW' ? 'warning' : risk === 'SAFE' ? 'positive' : 'information' }
function localizedReference(fa?: string, en?: string, language?: string) { if (!fa) return ''; return language?.toLowerCase().startsWith('en') && en?.trim() ? en : fa }
function positive(value: string | null, fallback: number) { const parsed = Number(value); return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback }
function shortId(value?: string) { return value ? value.slice(0, 8) : '—' }
class ApiError extends Error { constructor(public status: number, detail: string) { super(detail) } }
async function get<T>(url: string): Promise<T> { const response = await fetch(url); if (!response.ok) throw new ApiError(response.status, await response.text() || `HTTP ${response.status}`); return response.json() }
function message(error: unknown, t: (key: string) => string) { return error instanceof Error && error.message ? error.message : t('common.error') }
