import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { DataTable, FilterBar, FormField, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'
import { normalizeLanguage } from './i18n'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { analyticsDisplayName, analyticsExportUrl } from './analyticsUi'
import { useAuth } from './AuthContext'
import { analyticsRoutes, canNavigate } from './workspaceNavigation'

type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number; provenance: { dataCutoffUtc: string; source: string } }
type LeadRow = { sourcingOptionId: string; sourcingNumber: string; version: number; itemCode: string; itemNameFa: string; itemNameEn?: string; supplierNumber: string; supplierNameFa: string; total?: Stats; stages: Stage[]; cleanCompletedCount: number; onTimePercent?: number }
type Stats = { sampleCount: number; mean: number; p50: number; p80: number; p90: number; recommendedDays?: number; sampleWindow: string }
type Stage = { stageCode: string; statistics?: Stats; configuredDays?: number; state: string }
type ForecastRow = { productCode: string; productNameFa: string; productNameEn?: string; jalaliYear: number; jalaliMonth: number; baselineForecast?: number; finalForecast?: number; actual: number; baselineApe?: number; finalApe?: number; baselineState: string; finalState: string }
type ForecastResponse = { page: Page<ForecastRow>; summary: { rowCount: number; baselineWape?: number; baselineBias?: number; finalWape?: number; finalBias?: number } }
type CommercialRow = { purchaseOrderNumber: string; supplierNumber: string; supplierNameFa: string; currencyCode: string; originalAmount: number; orderedQuantity: number; receivedQuantity: number; cancelledQuantity: number; openQuantity: number; finalReceiptDate?: string; completionOutcome: string; creditAnchor: string; creditAnchorDate?: string; expectedCreditDate?: string; anchorState: string }
type CommercialResponse = { page: Page<CommercialRow>; currencyTotals: { currencyCode: string; originalAmount: number }[] }
type InventoryRow = { itemCode: string; itemNameFa: string; itemNameEn?: string; warehouseCode: string; warehouseNameFa: string; warehouseNameEn?: string; unitCode: string; onHand: number; companyOnHand: number; inactiveWarehouseWarning: boolean }

const pageSize = 25
const isoToday = () => new Date().toISOString().slice(0, 10)
const format = (value?: number) => formatOperationalNumber(value)

export default function AnalyticsPage({ view }: { view: 'lead-time' | 'forecast' | 'commercial' | 'inventory' | 'reports' }) {
  const { t, i18n } = useTranslation(); const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const { has } = useAuth()
  const [params] = useSearchParams(); const navigate = useNavigate(); const location = useLocation()
  const page = Math.max(1, Number(params.get('page') ?? 1)); const search = params.get('search') ?? ''
  const evaluationDate = params.get('evaluationDate') ?? isoToday()
  const requestedCutoff = params.get('dataCutoffUtc') ?? ''
  const [payload, setPayload] = useState<unknown>(); const [loading, setLoading] = useState(view !== 'reports'); const [error, setError] = useState('')
  const setParams = (changes: Record<string, string | number>) => { const next = new URLSearchParams(params); Object.entries(changes).forEach(([key, value]) => next.set(key, String(value))); navigate(`${location.pathname}?${next}`) }
  useEffect(() => {
    if (view === 'reports') return
    const controller = new AbortController()
    void Promise.resolve().then(() => { setLoading(true); setError('') })
    const endpoint = view === 'lead-time' ? `lead-time?evaluationDate=${evaluationDate}` : view === 'forecast' ? 'forecast-accuracy?' : view === 'commercial' ? `commercial-commitments?evaluationDate=${evaluationDate}` : `inventory/positions?asOfDate=${evaluationDate}`
    const cutoff = requestedCutoff ? `&dataCutoffUtc=${encodeURIComponent(requestedCutoff)}` : ''
    fetch(`/api/v1/analytics/${endpoint}&page=${page}&pageSize=${pageSize}&search=${encodeURIComponent(search)}${cutoff}`, { signal: controller.signal })
      .then(async response => { if (!response.ok) throw new Error(await response.text()); return response.json() })
      .then(setPayload).catch(reason => { if (reason.name !== 'AbortError') setError(String(reason)) }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [view, page, search, evaluationDate, requestedCutoff])
  const pageData = useMemo(() => view === 'forecast' || view === 'commercial' ? (payload as ForecastResponse | CommercialResponse | undefined)?.page : payload as Page<unknown> | undefined, [payload, view])
  const effectiveCutoff = pageData?.provenance.dataCutoffUtc ?? requestedCutoff
  const exportReport = view === 'lead-time' ? 'lead-time' : view === 'forecast' ? 'forecast-accuracy' : view === 'commercial' ? 'commercial-commitments' : view === 'inventory' ? 'inventory-position' : undefined
  const exportUrl = exportReport ? analyticsExportUrl({ report: exportReport, evaluationDate, language, maximumRows: 10000, search, dataCutoffUtc: effectiveCutoff }) : ''
  return <main className="analytics-page">
    <PageHeader title={t('analytics.title')} subtitle={t('analytics.subtitle')} />
    <nav className="analytics-tabs" aria-label={t('analytics.title')}>
      {analyticsRoutes.filter(route => canNavigate(route.path, has)).map(route =>
        <Link key={route.path} className={view === route.view ? 'active' : ''} to={route.path}>{t(route.labelKey)}</Link>)}
    </nav>
    {view !== 'reports' && <FilterBar label={t('common.filterBar')}><FormField label={t('common.search')}><input value={search} onChange={event => setParams({ search: event.target.value, page: 1 })} /></FormField>{view !== 'forecast' && <FormField label={t('analytics.evaluationDate')}><input type="date" value={evaluationDate} onChange={event => setParams({ evaluationDate: event.target.value, page: 1 })} /></FormField>}</FilterBar>}
    {exportReport && pageData && has('analytics.export') && <div className="analytics-export-current"><a className="button button--secondary" href={exportUrl}>{t('analytics.excel')}</a></div>}
    {error && <MessageStrip tone="critical">{t('common.error')}: {error}</MessageStrip>}
    {view === 'lead-time' && <LeadTable data={payload as Page<LeadRow> | undefined} language={language} loading={loading} evaluationDate={evaluationDate} canApply={has('procurement.sourcing_lead_time.apply')} t={t} />}
    {view === 'forecast' && <ForecastTable data={payload as ForecastResponse | undefined} language={language} loading={loading} t={t} />}
    {view === 'commercial' && <CommercialTable data={payload as CommercialResponse | undefined} loading={loading} t={t} />}
    {view === 'inventory' && <InventoryTable data={payload as Page<InventoryRow> | undefined} language={language} loading={loading} t={t} />}
    {view === 'reports' && <Reports evaluationDate={evaluationDate} search={search} dataCutoffUtc={requestedCutoff} t={t} language={language} />}
    {view !== 'reports' && pageData && <Pagination page={page} pageSize={pageSize} totalCount={pageData.totalCount} onPageChange={next => setParams({ page: next })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(pageData.totalCount / pageSize)), count: pageData.totalCount })} />}
    {pageData?.provenance && <p className="analytics-provenance">{t('analytics.provenance')}: {pageData.provenance.source} · {pageData.provenance.dataCutoffUtc}</p>}
  </main>
}

function LeadTable({ data, language, loading, evaluationDate, canApply, t }: { data?: Page<LeadRow>; language: string; loading: boolean; evaluationDate: string; canApply: boolean; t: TFunction }) {
  const headers = [t('sourcing.option'), t('sourcing.item'), t('sourcing.supplier'), t('analytics.sample'), 'P50', 'P80', 'P90', t('analytics.onTime')]
  if (canApply) headers.push(t('common.actions'))
  return <Section title={t('analytics.leadTime')}><><TableToolbar title={t('analytics.sourcingStatistics')} count={data?.totalCount} /><DataTable headers={headers} loading={loading} empty={!data?.items.length} filtered={false} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}><>{data?.items.map(x => <tr key={x.sourcingOptionId}><td>{x.sourcingNumber}</td><td><bdi>{x.itemCode}</bdi> — {analyticsDisplayName(x.itemNameFa, x.itemNameEn, language)}</td><td>{x.supplierNumber} — {x.supplierNameFa}</td><td>{x.total?.sampleCount ?? 0} ({x.total?.sampleWindow ?? '—'})</td><td>{format(x.total?.p50)}</td><td>{format(x.total?.p80)}</td><td>{format(x.total?.p90)}</td><td>{format(x.onTimePercent)}%</td>{canApply && <td><LeadApply row={x} evaluationDate={evaluationDate} t={t} /></td>}</tr>)}</></DataTable></></Section>
}

function LeadApply({ row, evaluationDate, t }: { row: LeadRow; evaluationDate: string; t: TFunction }) {
  const available = row.stages.filter(x => x.statistics?.recommendedDays != null)
  const [selected, setSelected] = useState<string[]>([]); const [message, setMessage] = useState(''); const [busy, setBusy] = useState(false)
  if (available.length === 0) return <StatusBadge text={t('analytics.insufficientSample')} tone="warning" />
  const apply = async () => {
    setBusy(true); setMessage('')
    const response = await fetch(`/api/v1/analytics/lead-time/${row.sourcingOptionId}/apply`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: row.version, evaluationDate, stageCodes: selected, reason: t('analytics.p80Reason') }) })
    setBusy(false); setMessage(response.ok ? t('common.saved') : t('common.error'))
  }
  return <div className="analytics-stage-apply"><details><summary>{t('analytics.applyP80')}</summary>{available.map(stage => <label key={stage.stageCode}><input type="checkbox" checked={selected.includes(stage.stageCode)} onChange={event => setSelected(values => event.target.checked ? [...values, stage.stageCode] : values.filter(x => x !== stage.stageCode))} />{stage.stageCode}: {stage.statistics?.recommendedDays}</label>)}<button type="button" className="button button--primary" disabled={busy || selected.length === 0} onClick={() => void apply()}>{t('common.confirm')}</button>{message && <small>{message}</small>}</details></div>
}
function ForecastTable({ data, language, loading, t }: { data?: ForecastResponse; language: string; loading: boolean; t: TFunction }) {
  return <Section title={t('analytics.forecast')} description={data ? `WAPE: ${format(data.summary.finalWape)}% · Bias: ${format(data.summary.finalBias)}%` : undefined}><DataTable headers={[t('forecastActual.product'), t('forecastActual.period'), t('analytics.baseline'), t('analytics.final'), t('analytics.actual'), 'Baseline APE', 'Final APE']} loading={loading} empty={!data?.page.items.length} filtered={false} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}><>{data?.page.items.map(x => <tr key={`${x.productCode}-${x.jalaliYear}-${x.jalaliMonth}`}><td><bdi>{x.productCode}</bdi> — {analyticsDisplayName(x.productNameFa, x.productNameEn, language)}</td><td><bdi>{x.jalaliYear}/{String(x.jalaliMonth).padStart(2, '0')}</bdi></td><td>{x.baselineState === 'AVAILABLE' ? format(x.baselineForecast) : <StatusBadge text={t('forecastActual.missing')} tone="warning" />}</td><td>{x.finalState === 'AVAILABLE' ? format(x.finalForecast) : <StatusBadge text={t('forecastActual.missing')} tone="warning" />}</td><td>{format(x.actual)}</td><td>{format(x.baselineApe)}%</td><td>{format(x.finalApe)}%</td></tr>)}</></DataTable></Section>
}
function CommercialTable({ data, loading, t }: { data?: CommercialResponse; loading: boolean; t: TFunction }) {
  return <Section title={t('analytics.commercial')} description={data?.currencyTotals.map(x => `${x.currencyCode}: ${format(x.originalAmount)}`).join(' · ')}><DataTable headers={[t('purchaseOrders.poNumber'), t('purchaseOrders.supplier'), t('purchaseOrders.currency'), t('purchaseOrders.total'), t('purchaseOrders.orderedQty'), t('purchaseOrders.receivedQty'), t('purchaseOrders.openQty'), t('purchaseOrders.finalReceipt'), t('analytics.anchor'), t('analytics.expectedCredit')]} loading={loading} empty={!data?.page.items.length} filtered={false} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}><>{data?.page.items.map(x => <tr key={x.purchaseOrderNumber}><td>{x.purchaseOrderNumber}</td><td>{x.supplierNumber} — {x.supplierNameFa}</td><td>{x.currencyCode}</td><td>{format(x.originalAmount)}</td><td>{format(x.orderedQuantity)}</td><td>{format(x.receivedQuantity)}</td><td>{format(x.openQuantity)}</td><td>{x.finalReceiptDate ?? x.completionOutcome}</td><td>{x.creditAnchor}</td><td>{x.expectedCreditDate ?? <StatusBadge text={t('analytics.unresolved')} tone="warning" />}</td></tr>)}</></DataTable></Section>
}
function InventoryTable({ data, language, loading, t }: { data?: Page<InventoryRow>; language: string; loading: boolean; t: TFunction }) {
  return <Section title={t('analytics.inventory')}><DataTable headers={[t('common.code'), t('common.nameFa'), t('inventory.warehouse'), t('common.unit'), t('analytics.warehouseOnHand'), t('inventory.companyTotal'), t('analytics.warning')]} loading={loading} empty={!data?.items.length} filtered={false} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}><>{data?.items.map(x => <tr key={`${x.itemCode}-${x.warehouseCode}`}><td>{x.itemCode}</td><td>{analyticsDisplayName(x.itemNameFa, x.itemNameEn, language)}</td><td>{x.warehouseCode} — {analyticsDisplayName(x.warehouseNameFa, x.warehouseNameEn, language)}</td><td>{x.unitCode}</td><td>{format(x.onHand)}</td><td>{format(x.companyOnHand)}</td><td>{x.inactiveWarehouseWarning && <StatusBadge text={t('inventory.inactiveStock')} tone="warning" />}</td></tr>)}</></DataTable></Section>
}
function Reports({ evaluationDate, search, dataCutoffUtc, t, language }: { evaluationDate: string; search: string; dataCutoffUtc: string; t: TFunction; language: string }) {
  const reports = ['mrp-run-summary', 'material-risk', 'order-planning', 'purchase-order-receipt', 'lead-time', 'forecast-accuracy', 'inventory-position', 'inventory-movement']
  return <Section title={t('analytics.reports')} description={t('analytics.exportHint')}><div className="analytics-report-grid">{reports.map(report => <article className="analytics-report-card" key={report}><h3>{t(`analytics.report.${report}`)}</h3><a className="button button--primary" href={analyticsExportUrl({ report, evaluationDate, language, maximumRows: 10000, search, dataCutoffUtc })}>{t('analytics.excel')}</a></article>)}</div></Section>
}
