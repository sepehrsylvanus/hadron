import { type CSSProperties, FormEvent, useEffect, useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { AlertTriangle, CalendarClock, CircleDollarSign, Clock3, Eye, FileQuestion, RefreshCw, RotateCcw, ShieldCheck } from 'lucide-react'
import { useAuth } from './AuthContext'
import { apiRoot } from './apiClient'
import {
  addCalendarDays,
  buildCashFlowDashboardQuery,
  buildCashObligationsQuery,
  buildCostedBomQuery,
  cashFlowDateResolutionFailure,
  cashFlowCurrencyView,
  cashFlowFilterPath,
  cashFlowFieldHasError,
  cashFlowRequestError,
  cashFlowDashboardRequestPaths,
  cashFlowVisualNumber,
  cashFlowJalaliResolverPath,
  costedBomDetailPath,
  costedBomListPath,
  formatCashAmount,
  formatCashUnitPrice,
  formatReportingRate,
  formatCashQuantity,
  groupCashBucketsByCurrency,
  isPositiveExactDecimal,
  loadIndependentCashFlowSection,
  positiveCashFlowPage,
  readCashFlowApiError,
  tehranTodayIso,
  type CashCurrencySummary,
  type CashFlowBucket,
  type CashFlowCurrencyView,
  type CashFlowDashboard,
  type CashFlowDateSurface,
  type CashFlowScope,
  type CashFlowRequestError,
  type CashObligation,
  type CashObligationPage,
  type CashObligationState,
  type CostBasisKind,
  type CostBasisMode,
  type CostBasisSelectionContext,
  type CostCurrencyTotal,
  type CostedBomDetail,
  type CostedBomLine,
  type CostedBomPage,
  type FxConversionContext,
} from './cashFlowUi'
import { buildEffectiveMaterialCostBookSelectionsQuery, type MaterialCostBookSelection } from './cashFlowMaterialCostingUi'
import { formatBusinessDate, formatBusinessDateTime } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { normalizeLanguage } from './i18n'
import { normalizeJalaliDateInput, toAsciiDigits } from './mrpRunUiSupport'
import { DataTable, FilterBar, FormField, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar, type StatusTone } from './ui'

const api = `${apiRoot}/v1/cash-flow`
const pageSize = 25

export default function CashFlowPage({ view }: { view: 'dashboard' | 'obligations' | 'costed-boms' | 'costed-bom' }) {
  if (view === 'dashboard') return <CashFlowDashboardPage />
  if (view === 'obligations') return <CashObligationListPage />
  if (view === 'costed-boms') return <CostedBomListPage />
  return <CostedBomDetailPage />
}

function CashFlowDashboardPage() {
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const location = useLocation()
  const defaults = defaultDates()
  const asOfDate = canonicalDateParam(params.get('asOfDate'), defaults.asOfDate)
  const horizonEnd = canonicalDateParam(params.get('horizonEnd'), defaults.horizonEnd)
  const scope = scopeParam(params.get('scope'))
  const currencyView: CashFlowCurrencyView = cashFlowCurrencyView(params.get('currencyView'))
  const [dashboard, setDashboard] = useState<CashFlowDashboard>()
  const [obligations, setObligations] = useState<CashObligationPage>()
  const [dashboardLoading, setDashboardLoading] = useState(true)
  const [obligationLoading, setObligationLoading] = useState(true)
  const [dashboardError, setDashboardError] = useState<CashFlowRequestError | null>(null)
  const [obligationError, setObligationError] = useState<CashFlowRequestError | null>(null)
  const [dashboardRetry, setDashboardRetry] = useState(0)
  const [obligationRetry, setObligationRetry] = useState(0)
  const { has } = useAuth()
  const canViewUpcomingObligations = cashFlowDashboardRequestPaths(has).includes('/obligations')
  const update = (changes: Record<string, string | number>) => updateSearch(navigate, location.pathname, params, changes)
  const filtersAreDefault = asOfDate === defaults.asOfDate && horizonEnd === defaults.horizonEnd && scope === 'COMMITTED' && currencyView === 'USD'

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const timer = window.setTimeout(() => {
      setDashboardLoading(true); setDashboardError(null)
      const query = buildCashFlowDashboardQuery({ asOfDate, horizonEnd, scope })
      void loadIndependentCashFlowSection(getJson<CashFlowDashboard>(`${api}/dashboard?${query}`, controller.signal))
        .then(result => {
          if (!active) return
          if (result.error === null) setDashboard(result.data)
          else if (result.error.code !== 'ABORTED') setDashboardError(result.error)
        })
        .finally(() => { if (active) setDashboardLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [asOfDate, dashboardRetry, horizonEnd, scope])

  useEffect(() => {
    if (!canViewUpcomingObligations) return
    const controller = new AbortController()
    let active = true
    const timer = window.setTimeout(() => {
      setObligationLoading(true); setObligationError(null)
      const query = buildCashObligationsQuery({ asOfDate, fromDate: asOfDate, toDate: horizonEnd, scope, page: 1, pageSize: 8 })
      void loadIndependentCashFlowSection(getJson<CashObligationPage>(`${api}/obligations?${query}`, controller.signal))
        .then(result => {
          if (!active) return
          if (result.error === null) setObligations(result.data)
          else if (result.error.code !== 'ABORTED') setObligationError(result.error)
        })
        .finally(() => { if (active) setObligationLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [asOfDate, canViewUpcomingObligations, horizonEnd, obligationRetry, scope])

  return <main className="cash-flow-page">
    <PageHeader eyebrow={t('cashFlow.privateWorkspace')} title={t('cashFlow.dashboardTitle')} subtitle={t('cashFlow.dashboardSubtitle')}
      status={<StatusBadge text={t('cashFlow.restricted')} tone="neutral" />} />
    <ForecastBoundaryNotice />
    <form onSubmit={event => event.preventDefault()}>
      <FilterBar label={t('common.filterBar')}>
        <DualDateFilter label={t('cashFlow.asOfDate')} value={asOfDate} language={language} surface="dashboard" externalError={validationFieldError(dashboardError, 'asOfDate', t)} onChange={value => update({ asOfDate: value })} />
        <DualDateFilter label={t('cashFlow.horizonEnd')} value={horizonEnd} language={language} surface="dashboard" externalError={validationFieldError(dashboardError, 'horizonEnd', t)} onChange={value => update({ horizonEnd: value })} />
        <CashScopeToggle scope={scope} onChange={value => update({ scope: value })} />
        <CurrencyViewToggle value={currencyView} onChange={value => update({ currencyView: value })} />
        <button className="button button--quiet" type="button" disabled={filtersAreDefault} onClick={() => navigate(cashFlowFilterPath(location.pathname, { asOfDate: defaults.asOfDate, horizonEnd: defaults.horizonEnd, scope: 'COMMITTED', currencyView: 'USD' }))}><RotateCcw aria-hidden="true" />{t('cashFlow.resetFilters')}</button>
      </FilterBar>
    </form>

    {dashboardError && <SectionFailure error={dashboardError} onRetry={() => { setDashboardLoading(true); setDashboardError(null); setDashboardRetry(value => value + 1) }} />}
    {!dashboardError && <>
      <FxContextNotice context={dashboard?.fxConversionContext} loading={dashboardLoading} language={language} />
      {currencyView === 'USD'
        ? <><UsdKpi summary={dashboard?.usdSummary} unresolvedCount={dashboard?.unresolvedCount ?? 0} loading={dashboardLoading} language={language} canViewObligations={canViewUpcomingObligations} scope={scope} asOfDate={asOfDate} horizonEnd={horizonEnd} /><CashFlowUsdChart buckets={dashboard?.usdBuckets ?? []} loading={dashboardLoading} language={language} /></>
        : <><CurrencyKpis currencies={dashboard?.currencies ?? []} unresolvedCount={dashboard?.unresolvedCount ?? 0} loading={dashboardLoading} language={language} canViewObligations={canViewUpcomingObligations} scope={scope} asOfDate={asOfDate} horizonEnd={horizonEnd} /><CashFlowChart buckets={dashboard?.buckets ?? []} currencies={dashboard?.currencies ?? []} loading={dashboardLoading} language={language} /></>}
    </>}

    {canViewUpcomingObligations && <Section title={t('cashFlow.upcomingObligations')} description={t('cashFlow.upcomingHint')}
      actions={<Link className="button button--secondary" to={withScope('/cash-flow/obligations', scope, asOfDate, horizonEnd)}>{t('cashFlow.viewFullTimeline')}</Link>}>
      {obligationError && <SectionFailure error={obligationError} compact onRetry={() => { setObligationLoading(true); setObligationError(null); setObligationRetry(value => value + 1) }} />}
      {!obligationError && <ObligationTable data={obligations?.items ?? []} loading={obligationLoading} language={language} filtered={false} />}
    </Section>}
    {dashboard?.generatedAtUtc && <Freshness value={dashboard.generatedAtUtc} language={language} />}
  </main>
}

function CurrencyKpis({ currencies, unresolvedCount, loading, language, canViewObligations, scope, asOfDate, horizonEnd }: { currencies: CashCurrencySummary[]; unresolvedCount: number; loading: boolean; language: string; canViewObligations: boolean; scope: CashFlowScope; asOfDate: string; horizonEnd: string }) {
  const { t } = useTranslation()
  if (loading) return <div className="cash-flow-kpi-grid" aria-busy="true"><article className="cash-flow-kpi-card skeleton" /><article className="cash-flow-kpi-card skeleton" /></div>
  if (!currencies.length && unresolvedCount === 0) return <MessageStrip>{t('cashFlow.noDashboardData')}</MessageStrip>
  return <section className="cash-flow-kpi-grid" aria-label={t('cashFlow.currencyKpis')}>
    {currencies.map(currency => <article className="cash-flow-kpi-card" key={currency.currencyId}>
      <header><CircleDollarSign aria-hidden="true" /><div><strong dir="ltr">{currency.currencyCode}</strong><small>{t('cashFlow.originalCurrency')}</small></div></header>
      <dl>
        <Metric label={t('cashFlow.overdue')} value={formatCashAmount(currency.overdueAmount, currency.decimalPrecision, language)} critical={isPositiveExactDecimal(currency.overdueAmount)} />
        <Metric label={t('cashFlow.due7Days')} value={formatCashAmount(currency.due7DaysAmount, currency.decimalPrecision, language)} />
        <Metric label={t('cashFlow.due30Days')} value={formatCashAmount(currency.due30DaysAmount, currency.decimalPrecision, language)} />
        <Metric label={t('cashFlow.due90Days')} value={formatCashAmount(currency.due90DaysAmount, currency.decimalPrecision, language)} />
      </dl>
      <footer><span>{t('cashFlow.horizonAmount')}</span><strong dir="ltr">{formatCashAmount(currency.horizonAmount, currency.decimalPrecision, language)} {currency.currencyCode}</strong><small>{t('cashFlow.obligationCount', { count: currency.obligationCount })}</small></footer>
    </article>)}
    {unresolvedCount > 0 && <article className="cash-flow-kpi-card cash-flow-kpi-card--warning"><header><FileQuestion aria-hidden="true" /><div><strong>{formatOperationalNumber(unresolvedCount, language)}</strong><small>{t('cashFlow.unresolvedDates')}</small></div></header><p>{t('cashFlow.unresolvedHint')}</p>{canViewObligations && <Link to={`${withScope('/cash-flow/obligations', scope, asOfDate, horizonEnd)}&state=UNRESOLVED`}>{t('cashFlow.reviewUnresolved')}</Link>}</article>}
  </section>
}

function Metric({ label, value, critical = false, unavailable = false }: { label: string; value: string; critical?: boolean; unavailable?: boolean }) {
  return <div className={critical ? 'is-critical' : ''}><dt>{label}</dt><dd dir={unavailable ? undefined : 'ltr'} className={unavailable ? 'cash-flow-value-unavailable' : undefined}>{value}</dd></div>
}

export function UsdKpi({ summary, unresolvedCount, loading, language, canViewObligations, scope, asOfDate, horizonEnd }: {
  summary: CashFlowDashboard['usdSummary'] | undefined; unresolvedCount: number; loading: boolean; language: string; canViewObligations: boolean; scope: CashFlowScope; asOfDate: string; horizonEnd: string
}) {
  const { t } = useTranslation()
  if (loading) return <div className="cash-flow-kpi-grid" aria-busy="true"><article className="cash-flow-kpi-card skeleton" /></div>
  if (!summary) return <MessageStrip tone="warning"><strong>{t('cashFlow.usdUnavailable')}</strong><p>{t('cashFlow.usdUnavailableHint')}</p></MessageStrip>
  const metric = (value: string | null) => value == null
    ? { value: t('cashFlow.usdUnavailableShort'), unavailable: true }
    : { value: formatCashAmount(value, 2, language), unavailable: false }
  return <section className="cash-flow-kpi-grid cash-flow-kpi-grid--usd" aria-label={t('cashFlow.usdKpis')}>
    <article className="cash-flow-kpi-card">
      <header><CircleDollarSign aria-hidden="true" /><div><strong dir="ltr">USD</strong><small>{t('cashFlow.reportingCurrency')}</small></div></header>
      <dl>
        <Metric label={t('cashFlow.overdue')} {...metric(summary.overdueAmount)} critical={summary.overdueAmount != null && isPositiveExactDecimal(summary.overdueAmount)} />
        <Metric label={t('cashFlow.due7Days')} {...metric(summary.due7DaysAmount)} />
        <Metric label={t('cashFlow.due30Days')} {...metric(summary.due30DaysAmount)} />
        <Metric label={t('cashFlow.due90Days')} {...metric(summary.due90DaysAmount)} />
      </dl>
      <footer><span>{t('cashFlow.horizonAmountUsd')}</span>{summary.horizonAmount == null
        ? <strong className="cash-flow-value-unavailable">{t('cashFlow.usdUnavailableShort')}</strong>
        : <strong dir="ltr">{formatCashAmount(summary.horizonAmount, 2, language)} USD</strong>}</footer>
    </article>
    {unresolvedCount > 0 && <article className="cash-flow-kpi-card cash-flow-kpi-card--warning"><header><FileQuestion aria-hidden="true" /><div><strong>{formatOperationalNumber(unresolvedCount, language)}</strong><small>{t('cashFlow.unresolvedDates')}</small></div></header><p>{t('cashFlow.unresolvedHint')}</p>{canViewObligations && <Link to={`${withScope('/cash-flow/obligations', scope, asOfDate, horizonEnd)}&state=UNRESOLVED`}>{t('cashFlow.reviewUnresolved')}</Link>}</article>}
  </section>
}

function FxContextNotice({ context, loading, language }: { context?: FxConversionContext; loading: boolean; language: string }) {
  const { t } = useTranslation()
  const { has } = useAuth()
  if (loading) return null
  if (!context || context.completeness === 'MISSING_SET') return <MessageStrip tone="warning"><strong>{t('cashFlow.fxMissingSet')}</strong><p>{t('cashFlow.fxMissingSetHint')}</p>{has('cash_flow.reporting_rate.view') && <Link to="/cash-flow/reporting-rates">{t('cashFlow.openReportingRates')}</Link>}</MessageStrip>
  const incomplete = context.completeness === 'INCOMPLETE'
  return <div className={`cash-flow-fx-context cash-flow-fx-context--${incomplete ? 'incomplete' : 'complete'}`} role="status">
    <ShieldCheck aria-hidden="true" />
    <span><strong>{incomplete ? t('cashFlow.fxIncomplete') : t('cashFlow.fxComplete')}</strong><small>{t('cashFlow.fxContextMeta', { revision: context.revision ?? '—' })} · <DualDate value={context.effectiveDate} language={language} /></small>{incomplete && <small>{t('cashFlow.fxMissingCurrencies')}: <bdi dir="ltr">{context.missingCurrencyCodes.join(' · ')}</bdi></small>}</span>
    {has('cash_flow.reporting_rate.view') && <Link to={context.rateSetId ? `/cash-flow/reporting-rates/${context.rateSetId}` : '/cash-flow/reporting-rates'}>{t('common.details')}</Link>}
  </div>
}

export function CurrencyViewToggle({ value, onChange }: { value: CashFlowCurrencyView; onChange: (value: CashFlowCurrencyView) => void }) {
  const { t } = useTranslation()
  return <fieldset className="cash-flow-scope cash-flow-currency-view"><legend>{t('cashFlow.displayCurrency')}</legend><div>
    <button type="button" aria-pressed={value === 'USD'} className={value === 'USD' ? 'active' : ''} onClick={() => onChange('USD')}><CircleDollarSign aria-hidden="true" />{t('cashFlow.usdView')}</button>
    <button type="button" aria-pressed={value === 'ORIGINAL'} className={value === 'ORIGINAL' ? 'active' : ''} onClick={() => onChange('ORIGINAL')}>{t('cashFlow.originalView')}</button>
  </div></fieldset>
}

export function CashFlowUsdChart({ buckets, loading, language }: { buckets: CashFlowDashboard['usdBuckets']; loading: boolean; language: string }) {
  const { t } = useTranslation()
  const maximum = Math.max(...buckets.map(item => cashFlowVisualNumber(item.amount ?? '0')), 1)
  const labelEvery = Math.max(1, Math.ceil(buckets.length / 12))
  return <Section title={t('cashFlow.chartTitleUsd')} description={t('cashFlow.chartHintUsd')}>
    {loading ? <div className="cash-flow-chart-skeleton" aria-busy="true" /> : !buckets.length ? <MessageStrip>{t('cashFlow.noDatedObligations')}</MessageStrip> : <article className="cash-flow-chart-card">
      <header><strong dir="ltr">USD</strong><span>{t('cashFlow.reportingCurrencySeries')}</span></header>
      <div className="cash-flow-bar-chart" aria-hidden="true">
        {buckets.map((bucket, index) => {
          const height = bucket.amount == null ? 0 : Math.max(3, (cashFlowVisualNumber(bucket.amount) / maximum) * 100)
          const amountLabel = bucket.amount == null ? t('cashFlow.usdUnavailableShort') : `${formatCashAmount(bucket.amount, 2, language)} USD`
          return <div className={`cash-flow-bar${bucket.amount == null ? ' cash-flow-bar--unavailable' : ''}`} key={bucket.date} title={`${formatBusinessDate(bucket.date, language)} — ${amountLabel}`}><span style={{ '--cash-bar-height': `${height}%` } as CSSProperties} aria-hidden="true">{bucket.amount == null ? '?' : null}</span><small>{index % labelEvery === 0 || index === buckets.length - 1 ? shortDate(bucket.date, language) : ''}</small></div>
        })}
      </div>
      <details className="cash-flow-chart-data"><summary>{t('cashFlow.chartData', { currency: 'USD' })}</summary><DataTable headers={[t('cashFlow.paymentDate'), t('cashFlow.amount'), t('cashFlow.obligationCountHeader')]} loading={false} empty={false} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.noDatedObligations')} noResultTitle={t('common.noResultsTitle')}><>{buckets.map(bucket => <tr key={bucket.date}><td><DualDate value={bucket.date} language={language} /></td><td className="ui-number"><strong className={bucket.amount == null ? 'cash-flow-value-unavailable' : undefined}>{bucket.amount == null ? t('cashFlow.usdUnavailableShort') : formatCashAmount(bucket.amount, 2, language)}</strong>{bucket.amount != null && <small dir="ltr">USD</small>}</td><td className="ui-number">{formatOperationalNumber(bucket.count, language)}</td></tr>)}</></DataTable></details>
    </article>}
  </Section>
}

export function CashFlowChart({ buckets, currencies, loading, language }: { buckets: CashFlowBucket[]; currencies: CashCurrencySummary[]; loading: boolean; language: string }) {
  const { t } = useTranslation()
  const groups = useMemo(() => groupCashBucketsByCurrency(buckets), [buckets])
  const precision = new Map(currencies.map(currency => [currency.currencyCode, currency.decimalPrecision]))
  return <Section title={t('cashFlow.chartTitle')} description={t('cashFlow.chartHint')}>
    {loading ? <div className="cash-flow-chart-skeleton" aria-busy="true" /> : !groups.length ? <MessageStrip>{t('cashFlow.noDatedObligations')}</MessageStrip> : <div className="cash-flow-chart-grid">
      {groups.map(group => {
        const maximum = Math.max(...group.buckets.map(bucket => cashFlowVisualNumber(bucket.amount)), 1)
        const labelEvery = Math.max(1, Math.ceil(group.buckets.length / 12))
        return <article className="cash-flow-chart-card" key={group.currencyCode}>
          <header><strong dir="ltr">{group.currencyCode}</strong><span>{t('cashFlow.currencySeries')}</span></header>
          <div className="cash-flow-bar-chart" aria-hidden="true">
            {group.buckets.map((bucket, index) => {
              const height = Math.max(3, (cashFlowVisualNumber(bucket.amount) / maximum) * 100)
              const title = `${formatBusinessDate(bucket.date, language)} — ${formatCashAmount(bucket.amount, precision.get(group.currencyCode) ?? 2, language)} ${group.currencyCode}`
              return <div className="cash-flow-bar" key={`${bucket.currencyId}-${bucket.date}`} title={title}>
                <span style={{ '--cash-bar-height': `${height}%` } as CSSProperties} aria-hidden="true" />
                <small>{index % labelEvery === 0 || index === group.buckets.length - 1 ? shortDate(bucket.date, language) : ''}</small>
              </div>
            })}
          </div>
          <details className="cash-flow-chart-data">
            <summary>{t('cashFlow.chartData', { currency: group.currencyCode })}</summary>
            <DataTable headers={[t('cashFlow.paymentDate'), t('cashFlow.amount'), t('cashFlow.obligationCountHeader')]}
              loading={false} empty={false} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.noDatedObligations')} noResultTitle={t('common.noResultsTitle')}>
              <>{group.buckets.map(bucket => <tr key={`${bucket.currencyId}-${bucket.date}`}>
                <td><DualDate value={bucket.date} language={language} /></td>
                <td className="ui-number"><strong>{formatCashAmount(bucket.amount, precision.get(group.currencyCode) ?? 2, language)}</strong><small dir="ltr">{group.currencyCode}</small></td>
                <td className="ui-number">{formatOperationalNumber(bucket.count, language)}</td>
              </tr>)}</>
            </DataTable>
          </details>
        </article>
      })}
    </div>}
  </Section>
}

function CashObligationListPage() {
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params] = useSearchParams()
  const navigate = useNavigate(); const location = useLocation()
  const defaults = defaultDates()
  const asOfDate = canonicalDateParam(params.get('asOfDate'), defaults.asOfDate)
  const fromDate = canonicalDateParam(params.get('fromDate'), asOfDate)
  const toDate = canonicalDateParam(params.get('toDate'), defaults.horizonEnd)
  const scope = scopeParam(params.get('scope'))
  const page = positiveCashFlowPage(params.get('page'))
  const search = params.get('search') ?? ''
  const currencyCode = params.get('currencyCode') ?? ''
  const stage = params.get('stage') ?? ''
  const state = params.get('state') ?? ''
  const [searchDraft, setSearchDraft] = useState(search)
  const [data, setData] = useState<CashObligationPage>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [retry, setRetry] = useState(0)
  const update = (changes: Record<string, string | number>) => updateSearch(navigate, location.pathname, params, changes)
  const filtersAreDefault = asOfDate === defaults.asOfDate && fromDate === defaults.asOfDate && toDate === defaults.horizonEnd && scope === 'COMMITTED' && !searchDraft.trim() && !currencyCode && !stage && !state && page === 1

  useEffect(() => { const timer = window.setTimeout(() => setSearchDraft(search), 0); return () => window.clearTimeout(timer) }, [search])
  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      const query = buildCashObligationsQuery({ asOfDate, fromDate, toDate, scope, search, currencyCode, stage, state, page, pageSize })
      void getJson<CashObligationPage>(`${api}/obligations?${query}`, controller.signal)
        .then(value => { if (active) setData(value) })
        .catch(reason => { if (active && !(reason instanceof Error && reason.name === 'AbortError')) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [asOfDate, currencyCode, fromDate, page, retry, scope, search, stage, state, toDate])

  function applySearch(event: FormEvent) { event.preventDefault(); update({ search: searchDraft.trim(), page: 1 }) }
  function resetFilters() {
    setSearchDraft('')
    navigate(cashFlowFilterPath(location.pathname, { asOfDate: defaults.asOfDate, fromDate: defaults.asOfDate, toDate: defaults.horizonEnd, scope: 'COMMITTED' }))
  }
  return <main className="cash-flow-page">
    <PageHeader eyebrow={t('cashFlow.privateWorkspace')} title={t('cashFlow.timelineTitle')} subtitle={t('cashFlow.timelineSubtitle')} />
    <ForecastBoundaryNotice />
    <form onSubmit={applySearch}>
      <FilterBar label={t('common.filterBar')}>
        <FormField label={t('common.search')}><input value={searchDraft} placeholder={t('cashFlow.searchPlaceholder')} onChange={event => setSearchDraft(event.target.value)} /></FormField>
        <DualDateFilter label={t('cashFlow.asOfDate')} value={asOfDate} language={language} surface="obligations" externalError={validationFieldError(error, 'asOfDate', t)} onChange={value => update({ asOfDate: value, page: 1 })} />
        <DualDateFilter label={t('common.from')} value={fromDate} language={language} surface="obligations" externalError={validationFieldError(error, 'fromDate', t)} onChange={value => update({ fromDate: value, page: 1 })} />
        <DualDateFilter label={t('common.to')} value={toDate} language={language} surface="obligations" externalError={validationFieldError(error, 'toDate', t)} onChange={value => update({ toDate: value, page: 1 })} />
        <FormField label={t('cashFlow.currency')} error={validationFieldError(error, 'currencyCode', t)}><input dir="ltr" maxLength={8} value={currencyCode} onChange={event => update({ currencyCode: event.target.value.toUpperCase(), page: 1 })} /></FormField>
        <FormField label={t('cashFlow.paymentStage')} error={validationFieldError(error, 'stage', t)}><select value={stage} onChange={event => update({ stage: event.target.value, page: 1 })}><option value="">{t('common.all')}</option>{(['ADVANCE','BEFORE_SHIPMENT','CREDIT'] as const).map(value => <option key={value} value={value}>{t(`cashFlow.stage.${value}`)}</option>)}</select></FormField>
        <FormField label={t('common.status')} error={validationFieldError(error, 'state', t)}><select value={state} onChange={event => update({ state: event.target.value, page: 1 })}><option value="">{t('common.all')}</option>{(['OVERDUE','DUE_SOON','SCHEDULED','UNRESOLVED'] as const).map(value => <option key={value} value={value}>{t(`cashFlow.state.${value}`)}</option>)}</select></FormField>
        <button className="button button--primary" type="submit">{t('common.search')}</button>
        <button className="button button--quiet" type="button" disabled={filtersAreDefault} onClick={resetFilters}><RotateCcw aria-hidden="true" />{t('cashFlow.resetFilters')}</button>
        <CashScopeToggle scope={scope} onChange={value => update({ scope: value, page: 1 })} />
      </FilterBar>
    </form>
    {state === 'UNRESOLVED' && <MessageStrip tone="information">{t('cashFlow.unresolvedRangeHint')}</MessageStrip>}
    {error && <SectionFailure error={error} onRetry={() => { setLoading(true); setError(null); setRetry(value => value + 1) }} />}
    {!error && <>
      <FxContextNotice context={data?.fxConversionContext} loading={loading} language={language} />
      <Section title={t('cashFlow.paymentWorklist')} description={scope === 'COMMITTED' ? t('cashFlow.committedHint') : t('cashFlow.allExposureHint')}>
      <><TableToolbar title={t('cashFlow.obligations')} count={data ? <span className="cash-flow-toolbar-total">{t('common.count', { count: data.totalCount })}<strong dir="ltr">{data.totalUsdAmount == null ? t('cashFlow.usdTotalUnavailable') : `${formatCashAmount(data.totalUsdAmount, 2, language)} USD`}</strong></span> : undefined} />
        <ObligationTable data={data?.items ?? []} loading={loading} language={language} filtered={Boolean(search || currencyCode || stage || state)} />
        {data && <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={next => update({ page: next })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(data.totalCount / pageSize)), count: data.totalCount })} />}
      </>
    </Section></>}
    {data?.generatedAtUtc && <Freshness value={data.generatedAtUtc} language={language} />}
  </main>
}

function ObligationTable({ data, loading, language, filtered }: { data: CashObligation[]; loading: boolean; language: string; filtered: boolean }) {
  const { t } = useTranslation(); const { has } = useAuth()
  return <DataTable headers={[t('cashFlow.paymentDate'), t('cashFlow.payee'), t('cashFlow.paymentStage'), t('cashFlow.obligationSource'), t('cashFlow.originalAmount'), t('cashFlow.reportingRate'), t('cashFlow.usdAmount'), t('cashFlow.confidenceLabel'), t('common.status')]}
    loading={loading} empty={!data.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.noObligations')} emptyDescription={t('cashFlow.noObligationsHint')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
    <>{data.map(row => {
      const itemName = cashFlowDisplayName({ nameFa: row.itemNameFa, nameEn: row.itemNameEn }, language)
      const isPurchaseOrderLevel = row.purchaseOrderLineId == null
      return <tr className={`cash-obligation-row cash-obligation-row--${row.state.toLowerCase()}`} key={row.obligationKey}>
        <td><DualDate value={row.expectedPaymentDate} language={language} unresolved={row.state === 'UNRESOLVED'} /></td>
        <td><strong>{row.supplierName}</strong><small><bdi>{row.supplierNumber}</bdi></small></td>
        <td><StatusBadge text={t(`cashFlow.stage.${row.stage}`)} tone="information" /><small>{t(`cashFlow.anchor.${row.anchorCode}`, { defaultValue: row.anchorCode || t('cashFlow.unresolved') })}</small></td>
        <td className="cash-obligation-source-cell">
          {has('procurement.purchase_order.view') ? <Link to={`/mrp/purchase-orders/${row.purchaseOrderId}`}><bdi>{row.purchaseOrderNumber}</bdi></Link> : <bdi>{row.purchaseOrderNumber}</bdi>}
          {isPurchaseOrderLevel
            ? <small>{t('cashFlow.purchaseOrderLevel')} · {t(`cashFlow.dateSource.${row.dateSource}`)}</small>
            : <>
              <span className="cash-obligation-source-item"><strong>{itemName || row.itemCode || '—'}</strong>{row.itemCode && itemName && <bdi>{row.itemCode}</bdi>}</span>
              <small>{t('cashFlow.purchaseOrderLine', { number: row.purchaseOrderLineNumber ?? '—' })} · {t('cashFlow.scheduleNumber', { number: row.scheduleNumber ?? '—' })}</small>
              <small>{t(`cashFlow.dateSource.${row.dateSource}`)}</small>
            </>}
        </td>
        <td className="ui-number"><strong>{formatCashAmount(row.projectedAmount, row.currencyDecimalPrecision, language)}</strong><small dir="ltr">{row.currencyCode}</small></td>
        <td className="ui-number">{row.unitsPerUsd == null ? <StatusBadge text={t('cashFlow.missingFx')} tone="warning" /> : <><strong>{formatReportingRate(row.unitsPerUsd, language)}</strong><small dir="ltr">1 USD = {row.currencyCode}</small></>}</td>
        <td className="ui-number">{row.projectedAmountUsd == null ? <StatusBadge text={t('cashFlow.usdUnavailableShort')} tone="warning" /> : <><strong>{formatCashAmount(row.projectedAmountUsd, 2, language)}</strong><small dir="ltr">USD</small></>}</td>
        <td><StatusBadge text={t(`cashFlow.confidence.${row.confidence}`)} tone={row.confidence === 'COMMITTED' ? 'positive' : 'neutral'} /></td>
        <td><StatusBadge text={t(`cashFlow.state.${row.state}`)} tone={stateTone(row.state)} /></td>
      </tr>
    })}</>
  </DataTable>
}

function CostedBomListPage() {
  const { t, i18n } = useTranslation(); const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params] = useSearchParams(); const navigate = useNavigate(); const location = useLocation()
  const defaults = defaultDates()
  const asOfDate = canonicalDateParam(params.get('asOfDate'), defaults.asOfDate)
  const search = params.get('search') ?? ''; const page = positiveCashFlowPage(params.get('page'))
  const costBasisMode: CostBasisMode = params.get('costBasisMode') === 'MATERIAL_COST_BOOK' ? 'MATERIAL_COST_BOOK' : 'PURCHASE_HISTORY'
  const priceBookRevisionId = params.get('priceBookRevisionId') ?? ''
  const [searchDraft, setSearchDraft] = useState(search); const [data, setData] = useState<CostedBomPage>(); const [loading, setLoading] = useState(true); const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [priceBooks, setPriceBooks] = useState<MaterialCostBookSelection[]>([])
  const [priceBooksLoading, setPriceBooksLoading] = useState(false)
  const [priceBooksError, setPriceBooksError] = useState<CashFlowRequestError | null>(null)
  const [priceBookSearch, setPriceBookSearch] = useState('')
  const [priceBookPage, setPriceBookPage] = useState(1)
  const [retry, setRetry] = useState(0)
  const update = (changes: Record<string, string | number>) => updateSearch(navigate, location.pathname, params, changes)
  const filtersAreDefault = asOfDate === defaults.asOfDate && !searchDraft.trim() && page === 1 && costBasisMode === 'PURCHASE_HISTORY'
  useEffect(() => { const timer = window.setTimeout(() => setSearchDraft(search), 0); return () => window.clearTimeout(timer) }, [search])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setPriceBooksLoading(true); setPriceBooksError(null)
      void getJson<MaterialCostBookSelection[]>(`${api}/material-cost-books/effective-selections?${buildEffectiveMaterialCostBookSelectionsQuery({ asOfDate, page: priceBookPage, pageSize: 20, search: priceBookSearch })}`, controller.signal)
        .then(value => { if (active) setPriceBooks(value) })
        .catch(reason => { if (active && !(reason instanceof Error && reason.name === 'AbortError')) setPriceBooksError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setPriceBooksLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [asOfDate, priceBookPage, priceBookSearch, retry])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      if (costBasisMode === 'MATERIAL_COST_BOOK' && !priceBookRevisionId) { setData(undefined); setLoading(false); setError(null); return }
      setLoading(true); setError(null)
      const query = buildCostedBomQuery({ asOfDate, search, page, pageSize, costBasisMode, priceBookRevisionId: priceBookRevisionId || undefined })
      void getJson<CostedBomPage>(`${api}/costed-boms?${query}`, controller.signal)
        .then(value => { if (active) setData(value) })
        .catch(reason => { if (active && !(reason instanceof Error && reason.name === 'AbortError')) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [asOfDate, costBasisMode, page, priceBookRevisionId, retry, search])
  return <main className="cash-flow-page">
    <PageHeader eyebrow={t('cashFlow.costingEyebrow')} title={t('cashFlow.costedBomTitle')} subtitle={t('cashFlow.costedBomSubtitle')} />
    <MessageStrip>{t('cashFlow.bomPriceBoundary')}</MessageStrip>
    <form onSubmit={event => { event.preventDefault(); update({ search: searchDraft.trim(), page: 1 }) }}><FilterBar label={t('common.filterBar')}>
      <FormField label={t('common.search')}><input value={searchDraft} placeholder={t('cashFlow.costSearchPlaceholder')} onChange={event => setSearchDraft(event.target.value)} /></FormField>
      <DualDateFilter label={t('cashFlow.valuationDate')} value={asOfDate} language={language} surface="costed-boms" externalError={validationFieldError(error, 'asOfDate', t)} onChange={value => { setPriceBookPage(1); update({ asOfDate: value, page: 1 }) }} />
      <CostBasisSelector mode={costBasisMode} priceBookRevisionId={priceBookRevisionId} priceBooks={priceBooks} loading={priceBooksLoading} language={language} search={priceBookSearch} page={priceBookPage} hasNext={priceBooks.length === 20} onSearch={value => { setPriceBookSearch(value); setPriceBookPage(1) }} onPage={setPriceBookPage} onChange={(nextMode, nextRevisionId) => update({ costBasisMode: nextMode === 'PURCHASE_HISTORY' ? '' : nextMode, priceBookRevisionId: nextRevisionId, page: 1 })} />
      <button className="button button--primary" type="submit">{t('common.search')}</button>
      <button className="button button--quiet" type="button" disabled={filtersAreDefault} onClick={() => { setSearchDraft(''); navigate(cashFlowFilterPath(location.pathname, { asOfDate: defaults.asOfDate })) }}><RotateCcw aria-hidden="true" />{t('cashFlow.resetFilters')}</button>
    </FilterBar></form>
    {priceBooksError && <SectionFailure error={priceBooksError} compact onRetry={() => setRetry(value => value + 1)} />}
    {costBasisMode === 'MATERIAL_COST_BOOK' && !priceBookRevisionId && !priceBooksLoading && <MessageStrip tone="warning">{t('cashFlow.costBasisSelectionRequired')}</MessageStrip>}
    {error && <SectionFailure error={error} onRetry={() => { setLoading(true); setError(null); setRetry(value => value + 1) }} />}
    {!error && <><CostBasisContextNotice context={data?.costBasisSelection} loading={loading} language={language} /><FxContextNotice context={data?.fxConversionContext} loading={loading} language={language} /><Section title={t('cashFlow.costedProducts')} description={t('cashFlow.costedProductsHint')}><>
      <TableToolbar title={t('cashFlow.costedBomList')} count={data ? t('common.count', { count: data.totalCount }) : undefined} />
      <DataTable headers={[t('cashFlow.product'), t('cashFlow.bomRevision'), t('cashFlow.valuationDate'), t('cashFlow.materialCostUsd'), t('cashFlow.originalCosts'), t('cashFlow.costReadiness'), t('common.actions')]} loading={loading} empty={!data?.items.length} filtered={Boolean(search)} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.noCostedBoms')} emptyDescription={t('cashFlow.noCostedBomsHint')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
        <>{data?.items.map(row => <tr key={`${row.parentItemId}-${row.asOfDate}`}>
          <td><strong>{cashFlowDisplayName({ nameFa: row.parentItemNameFa, nameEn: row.parentItemNameEn }, language)}</strong><small><bdi>{row.parentItemCode}</bdi></small></td>
          <td><bdi>{row.bomRevisionCode}</bdi><small>{t('cashFlow.costedOutput')}: <bdi>{formatCashQuantity(row.costedOutputQuantity, language)} {row.outputUnitCode}</bdi></small></td>
          <td><DualDate value={row.asOfDate} language={language} /></td>
          <td className="ui-number">{row.materialCostUsd == null ? <StatusBadge text={row.missingCostCount > 0 ? t('cashFlow.missingPrice') : t('cashFlow.missingFx')} tone="warning" /> : <><strong>{formatCashAmount(row.materialCostUsd, 2, language)}</strong><small dir="ltr">USD</small></>}</td>
          <td><CurrencyTotals values={row.currencyTotals} language={language} /></td>
          <td><CostReadiness missingCostCount={row.missingCostCount} missingFx={row.currencyTotals.some(total => total.unitsPerUsd == null)} /></td>
          <td><Link className="button button--compact" to={costedBomDetailPath(row.parentItemId, { asOfDate: row.asOfDate, search, page, costBasisMode, priceBookRevisionId: priceBookRevisionId || undefined })}><Eye aria-hidden="true" />{t('common.details')}</Link></td>
        </tr>)}</>
      </DataTable>
      {data && <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={next => update({ page: next })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(data.totalCount / pageSize)), count: data.totalCount })} />}
    </></Section></>}
  </main>
}

function CostedBomDetailPage() {
  const { parentItemId } = useParams(); const { t, i18n } = useTranslation(); const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params] = useSearchParams(); const asOfDate = canonicalDateParam(params.get('asOfDate'), defaultDates().asOfDate)
  const costBasisMode: CostBasisMode = params.get('costBasisMode') === 'MATERIAL_COST_BOOK' ? 'MATERIAL_COST_BOOK' : 'PURCHASE_HISTORY'
  const priceBookRevisionId = params.get('priceBookRevisionId') ?? ''
  const listContext = { asOfDate, search: params.get('search') ?? '', page: positiveCashFlowPage(params.get('page')), costBasisMode, priceBookRevisionId: priceBookRevisionId || undefined }
  const backTo = costedBomListPath(listContext)
  const [data, setData] = useState<CostedBomDetail>(); const [loading, setLoading] = useState(true); const [error, setError] = useState<CashFlowRequestError | null>(null); const [retry, setRetry] = useState(0); const { has } = useAuth()
  useEffect(() => {
    if (!parentItemId) return
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      const query = buildCostedBomQuery({ asOfDate, page: 1, pageSize: 1, costBasisMode, priceBookRevisionId: priceBookRevisionId || undefined })
      query.delete('page'); query.delete('pageSize')
      void getJson<CostedBomDetail>(`${api}/costed-boms/${parentItemId}?${query}`, controller.signal)
        .then(value => { if (active) setData(value) })
        .catch(reason => { if (active && !(reason instanceof Error && reason.name === 'AbortError')) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [asOfDate, costBasisMode, parentItemId, priceBookRevisionId, retry])
  if (error) return <main className="cash-flow-page"><PageHeader title={t('cashFlow.costedBomTitle')} backTo={backTo} backLabel={t('common.backToList')} /><SectionFailure error={error} onRetry={() => { setLoading(true); setError(null); setRetry(value => value + 1) }} /></main>
  return <main className="cash-flow-page">
    <PageHeader eyebrow={t('cashFlow.costingEyebrow')} title={data ? `${cashFlowDisplayName({ nameFa: data.parentItemNameFa, nameEn: data.parentItemNameEn }, language)} — ${data.parentItemCode}` : t('cashFlow.costedBomTitle')} subtitle={data ? `${t('cashFlow.bomRevision')}: ${data.bomRevisionCode}` : t('common.loading')} backTo={backTo} backLabel={t('common.backToList')}
      status={data && <CostReadiness missingCostCount={data.missingCosts.length} missingFx={data.currencyTotals.some(total => total.unitsPerUsd == null)} />} />
    {loading ? <Section title={t('cashFlow.costBreakdown')}><div className="cash-flow-chart-skeleton" aria-busy="true" /></Section> : data && <>
      <CostBasisContextNotice context={data.costBasisSelection} loading={false} language={language} />
      <FxContextNotice context={data.fxConversionContext} loading={false} language={language} />
      <section className="cash-flow-cost-summary" aria-label={t('cashFlow.costSummary')}>
        <article><Clock3 aria-hidden="true" /><span>{t('cashFlow.valuationDate')}<strong><DualDate value={data.asOfDate} language={language} /></strong></span></article>
        <article><ShieldCheck aria-hidden="true" /><span>{t('cashFlow.costedOutput')}<strong dir="ltr">{formatCashQuantity(data.costedOutputQuantity, language)} {data.outputUnitCode}</strong></span></article>
        <article><CircleDollarSign aria-hidden="true" /><span>{t('cashFlow.materialCostUsd')}<strong dir="ltr">{data.materialCostUsd == null ? '—' : `${formatCashAmount(data.materialCostUsd, 2, language)} USD`}</strong></span></article>
        {data.currencyTotals.map(total => <article key={total.currencyId}><CircleDollarSign aria-hidden="true" /><span>{t('cashFlow.materialCost')}<strong dir="ltr">{formatCashAmount(total.materialCost, total.decimalPrecision, language)} {total.currencyCode}</strong></span></article>)}
      </section>
      {(data.hasMixedCurrencies || data.currencyTotals.length > 1) && <MessageStrip tone="information">{t('cashFlow.originalCurrencyPreserved')}</MessageStrip>}
      <Section title={t('cashFlow.costBreakdown')} description={t('cashFlow.costBreakdownHint')}>
        <DataTable headers={[t('cashFlow.structureItem'), t('cashFlow.quantity'), t('cashFlow.costBasisLabel'), t('cashFlow.unitPrice'), t('cashFlow.extendedCost'), t('cashFlow.reportingRate'), t('cashFlow.extendedCostUsd'), t('cashFlow.sourceDocument')]} loading={false} empty={!data.lines.length} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.noCostLines')} noResultTitle={t('common.noResultsTitle')}>
          <>{data.lines.map(line => <CostedBomLineRow key={`${line.path}-${line.itemId}`} line={line} language={language} canViewPurchaseOrder={has('procurement.purchase_order.view')} unitsPerUsd={data.currencyTotals.find(total => total.currencyCode === line.costBasis?.currencyCode)?.unitsPerUsd ?? null} />)}</>
        </DataTable>
      </Section>
      {data.missingCosts.length > 0 && <Section title={t('cashFlow.missingCosts')} description={t('cashFlow.missingCostsHint')}><ul className="cash-flow-missing-list">{data.missingCosts.map(item => <li key={`${item.itemId}-${item.path ?? ''}`}><AlertTriangle aria-hidden="true" /><span><strong>{cashFlowDisplayName({ nameFa: item.itemNameFa, nameEn: item.itemNameEn }, language)}</strong><small><bdi>{item.itemCode}</bdi>{item.path ? ` · ${item.path}` : ''}</small><small>{t(`cashFlow.missingReason.${item.reason}`, { defaultValue: item.reason })}</small></span></li>)}</ul></Section>}
    </>}
  </main>
}

function CostedBomLineRow({ line, language, canViewPurchaseOrder, unitsPerUsd }: { line: CostedBomLine; language: string; canViewPurchaseOrder: boolean; unitsPerUsd: string | null }) {
  const { t } = useTranslation()
  const basis = line.costBasis
  const isMissing = basis?.kind === 'MISSING'
  const precision = basis?.currencyDecimalPrecision ?? 2
  const purchaseOrder = basis?.purchaseOrderNumber
  const costSource = basis?.sourceDocumentNumber
  const showSeparateSource = Boolean(costSource && costSource !== purchaseOrder)

  return <tr className={isMissing ? 'cash-cost-line--missing' : ''}>
    <td><div className="cash-cost-tree-cell" style={{ '--cost-level': Math.max(0, line.level) } as CSSProperties}><span aria-hidden="true" /><div><strong>{cashFlowDisplayName({ nameFa: line.itemNameFa, nameEn: line.itemNameEn }, language)}</strong><small><bdi>{line.itemCode}</bdi> · {line.path}</small></div></div></td>
    <td className="ui-number"><strong>{formatCashQuantity(line.quantity, language)}</strong><small dir="ltr">{line.unitCode} · {t('cashFlow.scrapPercent', { value: formatCashQuantity(line.scrapPercent, language) })}</small></td>
    <td>{basis ? <><StatusBadge text={t(`cashFlow.costBasis.${basis.kind}`)} tone={costBasisTone(basis.kind)} />{basis.supplierName && <small>{basis.supplierName} · <bdi>{basis.supplierNumber}</bdi></small>}{basis.missingReason && <small>{t(`cashFlow.missingReason.${basis.missingReason}`, { defaultValue: basis.missingReason })}</small>}</> : <StatusBadge text={t('cashFlow.rolledUpNode')} tone="neutral" />}</td>
    <td className="ui-number">{!basis || isMissing ? '—' : <><strong>{formatCashUnitPrice(basis.unitPrice, precision, language)}</strong><small dir="ltr">{basis.currencyCode}{basis.unitCode ? ` / ${basis.unitCode}` : ''}</small></>}</td>
    <td className="ui-number">{!basis || isMissing ? '—' : <><strong>{formatCashAmount(line.extendedCost, precision, language)}</strong><small dir="ltr">{basis.currencyCode}</small></>}</td>
    <td className="ui-number">{!basis || isMissing ? '—' : unitsPerUsd == null ? <StatusBadge text={t('cashFlow.missingFx')} tone="warning" /> : <><strong>{formatReportingRate(unitsPerUsd, language)}</strong><small dir="ltr">1 USD = {basis.currencyCode}</small></>}</td>
    <td className="ui-number">{!basis ? '—' : isMissing ? <StatusBadge text={t('cashFlow.missingPrice')} tone="warning" /> : line.extendedCostUsd == null ? <StatusBadge text={t('cashFlow.missingFx')} tone="warning" /> : <><strong>{formatCashAmount(line.extendedCostUsd, 2, language)}</strong><small dir="ltr">USD</small></>}</td>
    <td>{!basis || (!purchaseOrder && !costSource) ? '—' : <>
      {purchaseOrder && (canViewPurchaseOrder && basis.purchaseOrderId ? <Link to={`/mrp/purchase-orders/${basis.purchaseOrderId}`}><bdi>{purchaseOrder}</bdi></Link> : <bdi>{purchaseOrder}</bdi>)}
      {showSeparateSource && <small>{t('cashFlow.costSource')}: <bdi>{costSource}</bdi></small>}
      {basis.sourceDate && <small><DualDate value={basis.sourceDate} language={language} /></small>}
    </>}</td>
  </tr>
}

function CostBasisSelector({ mode, priceBookRevisionId, priceBooks, loading, language, search, page, hasNext, onSearch, onPage, onChange }: { mode: CostBasisMode; priceBookRevisionId: string; priceBooks: MaterialCostBookSelection[]; loading: boolean; language: string; search: string; page: number; hasNext: boolean; onSearch: (value: string) => void; onPage: (value: number) => void; onChange: (mode: CostBasisMode, revisionId: string) => void }) {
  const { t } = useTranslation()
  const selectedOutsidePage = Boolean(priceBookRevisionId && !priceBooks.some(book => book.revisionId === priceBookRevisionId))
  return <fieldset className="cash-flow-cost-basis-selector"><legend>{t('cashFlow.costBasisMode')}</legend><div className="cash-flow-cost-basis-selector__modes">
    <button type="button" className={mode === 'PURCHASE_HISTORY' ? 'active' : ''} aria-pressed={mode === 'PURCHASE_HISTORY'} onClick={() => onChange('PURCHASE_HISTORY', '')}>{t('cashFlow.costBasisPurchaseHistory')}</button>
    <button type="button" className={mode === 'MATERIAL_COST_BOOK' ? 'active' : ''} aria-pressed={mode === 'MATERIAL_COST_BOOK'} onClick={() => onChange('MATERIAL_COST_BOOK', priceBookRevisionId)}>{t('cashFlow.costBasisMaterialBook')}</button>
  </div>{mode === 'MATERIAL_COST_BOOK' && <div className="cash-flow-cost-basis-selector__picker">
    <input value={search} maxLength={200} placeholder={t('common.search')} aria-label={t('cashFlow.searchMaterialCostBook')} onChange={event => onSearch(event.target.value)} />
    <select aria-label={t('cashFlow.selectMaterialCostBook')} value={priceBookRevisionId} disabled={loading} onChange={event => onChange('MATERIAL_COST_BOOK', event.target.value)}><option value="">{loading ? t('common.loading') : t('cashFlow.selectMaterialCostBook')}</option>{selectedOutsidePage && <option value={priceBookRevisionId}>{t('cashFlow.selectedMaterialCostBookOutsidePage')}</option>}{priceBooks.map(book => <option key={book.revisionId} value={book.revisionId}>{localizedName({ nameFa: book.nameFa, nameEn: book.nameEn }, language)} — {book.bookNumber} / {book.revision}</option>)}</select>
    <div><button type="button" className="button button--quiet" disabled={loading || page <= 1} onClick={() => onPage(page - 1)} aria-label={t('common.previous')}>‹</button><bdi>{page}</bdi><button type="button" className="button button--quiet" disabled={loading || !hasNext} onClick={() => onPage(page + 1)} aria-label={t('common.next')}>›</button></div>
  </div>}</fieldset>
}

function CostBasisContextNotice({ context, loading, language }: { context?: CostBasisSelectionContext; loading: boolean; language: string }) {
  const { t } = useTranslation()
  if (loading || !context) return null
  if (context.mode === 'PURCHASE_HISTORY') return <MessageStrip>{t('cashFlow.costBasisPurchaseHistoryHint')}</MessageStrip>
  return <MessageStrip tone="information"><strong>{t('cashFlow.costBasisMaterialBook')}</strong><p>{localizedName({ nameFa: context.priceBookNameFa ?? '', nameEn: context.priceBookNameEn }, language)} · <bdi>{context.priceBookNumber}</bdi> · {t('cashFlow.materialCostBooks.revision')} {context.priceBookRevision ?? '—'}</p>{context.priceBookEffectiveDate && <p>{t('cashFlow.materialCostBooks.effectiveDate')}: <DualDate value={context.priceBookEffectiveDate} language={language} /></p>}<small>{t('cashFlow.costBasisStrictHint')}</small></MessageStrip>
}

function CashScopeToggle({ scope, onChange }: { scope: CashFlowScope; onChange: (scope: CashFlowScope) => void }) {
  const { t } = useTranslation()
  return <fieldset className="cash-flow-scope"><legend>{t('cashFlow.exposureScope')}</legend><div>
    <button type="button" aria-pressed={scope === 'COMMITTED'} className={scope === 'COMMITTED' ? 'active' : ''} onClick={() => onChange('COMMITTED')}><ShieldCheck aria-hidden="true" />{t('cashFlow.committedOnly')}</button>
    <button type="button" aria-pressed={scope === 'ALL'} className={scope === 'ALL' ? 'active' : ''} onClick={() => onChange('ALL')}><CalendarClock aria-hidden="true" />{t('cashFlow.includePlanned')}</button>
  </div></fieldset>
}

export function DualDateFilter({ label, value, language, surface, externalError, allowEmpty = false, onChange }: { label: string; value: string; language: string; surface: CashFlowDateSurface; externalError?: string; allowEmpty?: boolean; onChange: (value: string) => void }) {
  const { t } = useTranslation()
  const isPersian = !language.startsWith('en')
  const localizedValue = isPersian ? jalaliInputValue(value) : value
  const [draft, setDraft] = useState(localizedValue)
  const [error, setError] = useState('')
  const [resolving, setResolving] = useState(false)
  const hintId = useId()
  const equivalentId = useId()
  const errorId = useId()
  const displayedError = error || externalError || ''
  const describedBy = [equivalentId, isPersian ? hintId : '', displayedError ? errorId : ''].filter(Boolean).join(' ')

  useEffect(() => {
    const timer = window.setTimeout(() => { setDraft(localizedValue); setError('') }, 0)
    return () => window.clearTimeout(timer)
  }, [localizedValue])

  async function commitJalaliDate() {
    if (!isPersian || resolving) return
    if (allowEmpty && draft.trim() === '') {
      setError('')
      onChange('')
      return
    }
    const normalized = normalizeJalaliDateInput(draft)
    if (!normalized) { setError(t('cashFlow.jalaliDateInvalid')); return }
    setResolving(true); setError('')
    try {
      const query = new URLSearchParams({ jalaliDate: normalized })
      const response = await fetch(`${apiRoot}${cashFlowJalaliResolverPath(surface)}?${query}`, {
        cache: 'no-store',
      })
      if (!response.ok) throw await readCashFlowApiError(response)
      const result = await response.json() as { gregorianDate: string }
      onChange(result.gregorianDate)
    } catch (reason) {
      const failure = cashFlowDateResolutionFailure(reason)
      setError(failure === 'validation' ? t('cashFlow.jalaliDateInvalid')
        : failure === 'authentication' ? t('cashFlow.dateResolverAuthentication')
          : failure === 'forbidden' ? t('cashFlow.dateResolverForbidden')
            : t('cashFlow.dateResolverUnavailable'))
    }
    finally { setResolving(false) }
  }

  return <FormField label={label}>
    <>
      <div className="cash-flow-date-filter">
      <input type={isPersian ? 'text' : 'date'} inputMode={isPersian ? 'numeric' : undefined} dir="ltr" value={draft} maxLength={isPersian ? 8 : undefined} placeholder={isPersian ? '14050616' : undefined} aria-describedby={describedBy} aria-invalid={Boolean(displayedError)} aria-busy={resolving}
        onChange={event => {
          const next = isPersian ? toAsciiDigits(event.target.value).replace(/[^0-9]/g, '').slice(0, 8) : event.target.value
          setDraft(next); setError('')
          if (!isPersian) onChange(next)
        }}
        onBlur={() => { void commitJalaliDate() }}
        onKeyDown={event => { if (isPersian && event.key === 'Enter') { event.preventDefault(); void commitJalaliDate() } }} />
      <span id={equivalentId} className="cash-flow-date-equivalent" dir="ltr">{formatBusinessDate(value, isPersian ? 'en' : 'fa')}</span>
      </div>
      {isPersian && <small id={hintId}>{t('cashFlow.jalaliDateHint')}</small>}
      {displayedError && <small id={errorId} className="ui-field-error" role="alert">{displayedError}</small>}
    </>
  </FormField>
}

export function DualDate({ value, language, unresolved = false }: { value?: string | null; language: string; unresolved?: boolean }) {
  const { t } = useTranslation()
  if (!value || unresolved) return <StatusBadge text={t('cashFlow.unresolved')} tone="warning" />
  const secondary = language.startsWith('en') ? 'fa-IR' : 'en'
  return <span className="cash-flow-dual-date"><strong dir="ltr">{formatBusinessDate(value, language)}</strong><small dir="ltr">{formatBusinessDate(value, secondary)}</small></span>
}

function CurrencyTotals({ values, language }: { values: CostCurrencyTotal[]; language: string }) {
  const { t } = useTranslation()
  if (!values.length) return <StatusBadge text={t('cashFlow.noCost')} tone="warning" />
  return <span className="cash-flow-currency-totals">{values.map(value => <span key={value.currencyId} dir="ltr"><strong>{formatCashAmount(value.materialCost, value.decimalPrecision, language)}</strong> {value.currencyCode}</span>)}</span>
}

export function CostReadiness({ missingCostCount, missingFx }: { missingCostCount: number; missingFx: boolean }) {
  const { t } = useTranslation()
  if (!missingCostCount && !missingFx) return <StatusBadge text={t('cashFlow.costCompleteUsd')} tone="positive" />
  return <span className="cash-flow-readiness-badges">
    {missingCostCount > 0 && <StatusBadge text={t('cashFlow.missingCostCount', { count: missingCostCount })} tone="warning" />}
    {missingFx && <StatusBadge text={t('cashFlow.missingFx')} tone="warning" />}
  </span>
}

function ForecastBoundaryNotice() {
  const { t } = useTranslation()
  return <MessageStrip tone="information">{t('cashFlow.forecastBoundary')}</MessageStrip>
}

function Freshness({ value, language }: { value: string; language: string }) {
  const { t } = useTranslation()
  return <p className="cash-flow-freshness"><Clock3 aria-hidden="true" />{t('cashFlow.lastUpdated')}: <bdi>{formatBusinessDateTime(value, language)}</bdi></p>
}

export function SectionFailure({ error, compact = false, onRetry }: { error: CashFlowRequestError; compact?: boolean; onRetry?: () => void }) {
  const { t } = useTranslation()
  const validation = error.kind === 'validation'
  const authentication = error.kind === 'authentication'
  const forbidden = error.kind === 'forbidden'
  const notFound = error.kind === 'not-found'
  const conflict = error.kind === 'conflict'
  const retryable = Boolean(onRetry && !validation && !authentication && !forbidden && !notFound)
  const title = validation ? t('cashFlow.validationTitle') : authentication ? t('cashFlow.authenticationTitle') : forbidden ? t('cashFlow.forbiddenTitle') : notFound ? t('cashFlow.notFoundTitle') : conflict ? t('cashFlow.conflictTitle') : t('cashFlow.sectionUnavailable')
  const hint = validation ? t('cashFlow.validationHint') : authentication ? t('cashFlow.authenticationHint') : forbidden ? t('cashFlow.forbiddenHint') : notFound ? t('cashFlow.notFoundHint') : conflict ? t('cashFlow.conflictHint') : t('cashFlow.sectionUnavailableHint')
  return <MessageStrip tone={validation || conflict || notFound ? 'warning' : 'critical'}><strong>{title}</strong><p>{hint}</p>{!validation && error.code !== 'CASH_FLOW_LOAD_FAILED' && <small><bdi>{t('cashFlow.supportCode')}: {error.code}</bdi></small>}{retryable && <button type="button" className={`button button--secondary${compact ? ' button--compact' : ''}`} onClick={onRetry}><RefreshCw aria-hidden="true" />{t('cashFlow.retry')}</button>}</MessageStrip>
}

function stateTone(state: CashObligationState): StatusTone {
  if (state === 'OVERDUE') return 'critical'
  if (state === 'DUE_SOON' || state === 'UNRESOLVED') return 'warning'
  return 'positive'
}

function costBasisTone(kind: CostBasisKind): StatusTone {
  if (kind === 'RECEIPT') return 'positive'
  if (kind === 'PURCHASE_ORDER_ESTIMATE' || kind === 'MATERIAL_COST_BOOK') return 'information'
  return 'warning'
}

function scopeParam(value: string | null): CashFlowScope { return value === 'ALL' ? 'ALL' : 'COMMITTED' }
function canonicalDateParam(value: string | null, fallback: string) { return value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : fallback }
function defaultDates() { const asOfDate = tehranTodayIso(); return { asOfDate, horizonEnd: addCalendarDays(asOfDate, 90) } }
function jalaliInputValue(value: string) { return toAsciiDigits(formatBusinessDate(value, 'fa')).replace(/[^0-9]/g, '') }
function shortDate(value: string, language: string) { return new Intl.DateTimeFormat(language.startsWith('fa') ? 'fa-IR-u-ca-persian' : 'en-GB', { timeZone: 'Asia/Tehran', month: '2-digit', day: '2-digit' }).format(new Date(`${value}T12:00:00Z`)) }

function updateSearch(navigate: ReturnType<typeof useNavigate>, pathname: string, current: URLSearchParams, changes: Record<string, string | number>) {
  const next = new URLSearchParams(current)
  for (const [key, value] of Object.entries(changes)) {
    if (String(value).trim()) next.set(key, String(value))
    else next.delete(key)
  }
  navigate(`${pathname}?${next}`)
}

function withScope(path: string, scope: CashFlowScope, asOfDate: string, horizonEnd: string) {
  return `${path}?${new URLSearchParams({ scope, asOfDate, fromDate: asOfDate, toDate: horizonEnd })}`
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  // Cash-flow is an operational, time-sensitive read model. A stale cached 404
  // (for example immediately after a module deployment) must not mask a newly
  // available endpoint, and users must always see the current projection.
  const response = await fetch(url, { signal, cache: 'no-store' })
  if (!response.ok) throw await readCashFlowApiError(response)
  return response.json() as Promise<T>
}

function validationFieldError(error: CashFlowRequestError | null, field: string, t: ReturnType<typeof useTranslation>['t']) {
  if (!cashFlowFieldHasError(error, field)) return undefined
  const supported = ['asOfDate', 'horizonEnd', 'fromDate', 'toDate', 'currencyCode', 'stage', 'state']
  const key = supported.includes(field) ? field : 'generic'
  return t(`cashFlow.validation.${key}`)
}

function cashFlowDisplayName(value: { nameFa?: string | null; nameEn?: string | null }, language?: string | null) {
  const nameFa = value.nameFa?.trim() ?? ''
  const nameEn = value.nameEn?.trim() || null
  if (!nameFa) return nameEn ?? ''
  return localizedName({ nameFa, nameEn }, language)
}
