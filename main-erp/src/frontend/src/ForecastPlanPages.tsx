import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { Pencil, Search, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { apiRoot } from './apiClient'
import { formatDualBusinessDateRange } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber as formatQuantity } from './i18n/numberFormatting'
import { currentListContext, useListQuery, useListReturn } from './ui/routeContext'
import {
  ConfirmationDialog, DataTable, EmptyState, FilterBar, FormField, LoadingState,
  MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar,
} from './ui'

type Localized = { id: string; code: string; nameFa: string; nameEn?: string | null; isActive: boolean }
type Unit = Localized & { symbol?: string | null }
type Product = {
  id: string
  code: string
  nameFa: string
  nameEn?: string | null
  isActive: boolean
  productCategory: Localized
  businessUnit?: Localized | null
  baseUnit: Unit
}
type PlanSummary = {
  id: string
  name: string
  startJalaliDate: string
  endJalaliDate: string
  startDate: string
  endDate: string
  productCount: number
  monthCount: number
  entryCount: number
  actorIdentifier?: string | null
  createdAtUtc: string
}
type PlanList = { items: PlanSummary[]; page: number; pageSize: number; totalCount: number }
type PlanMonth = {
  periodCode: string
  jalaliYear: number
  jalaliMonth: number
  startDate: string
  endDate: string
  businessPeriodId: string
  status: 'OPEN' | 'CLOSED'
}
type PlanHistory = {
  revisionNumber: number
  forecastQty: number
  reason?: string | null
  actorIdentifier?: string | null
  createdAtUtc: string
}
type PlanCell = {
  entryId: string
  productId: string
  periodCode: string
  originalForecastQty: number
  forecastQty: number
  planRevisionNumber: number
  forecastRevisionId: string
  forecastRevisionNumber: number
  seriesVersion: number
  officialCurrentForecastQty: number
  officialCurrentRevisionNumber: number
  isOfficialCurrent: boolean
  monthlyActualQty: number
  dailyActualEntryCount: number
  history: PlanHistory[]
}
type PlanDetail = {
  plan: PlanSummary
  months: PlanMonth[]
  products: Product[]
  cells: PlanCell[]
  page: number
  pageSize: number
  totalProducts: number
}
type CorrectionConflict = {
  productId: string
  productCode: string
  productNameFa: string
  productNameEn?: string | null
  periodCode: string
  planForecastQty: number
  officialForecastQty: number
  proposedQty: number
  seriesVersion: number
  officialRevisionNumber: number
}
type ProblemBody = {
  detail?: string
  title?: string
  code?: string
  details?: CorrectionConflict[]
  errors?: Record<string, string[]>
}

const pageSize = 20

export function ForecastPlanListPage() {
  const { t, i18n } = useTranslation()
  const query = useListQuery(25)
  const location = useLocation()
  const search = query.value('search')
  const [data, setData] = useState<PlanList>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const language = i18n.resolvedLanguage ?? 'fa-IR'

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const parameters = new URLSearchParams({ search, page: String(query.page), pageSize: String(query.pageSize) })
      const response = await fetch(`${apiRoot}/planning/forecast-plans?${parameters}`)
      if (!response.ok) throw new Error(await problemText(response))
      setData(await response.json() as PlanList)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoading(false)
    }
  }, [query.page, query.pageSize, search, t])

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 180)
    return () => window.clearTimeout(timer)
  }, [load])

  const pages = Math.max(1, Math.ceil(data.totalCount / query.pageSize))
  const returnContext = currentListContext(location.pathname, location.search)
  return <main className="app-shell forecast-plan-page">
    <PageHeader title={t('forecastActual.forecastTitle')} subtitle={t('forecastActual.planListSubtitle')}
      actions={<Link className="button button--primary" to="/mrp/planning/production-forecast/new">{t('forecastActual.createPlan')}</Link>} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <FilterBar label={t('common.filters')}>
      <FormField label={t('common.search')}>
        <div className="ui-search-field"><Search aria-hidden="true" /><input value={search}
          placeholder={t('forecastActual.planSearchPlaceholder')}
          onChange={event => query.update({ search: event.target.value }, true)} /></div>
      </FormField>
    </FilterBar>
    <section className="ui-table-surface" aria-label={t('forecastActual.savedPlans')}>
      <TableToolbar title={t('forecastActual.savedPlans')} count={t('common.count', { count: data.totalCount })} />
      <DataTable headers={[t('forecastActual.planName'), t('forecastActual.planRange'), t('forecastActual.productsCount'),
        t('forecastActual.monthsCount'), t('forecastActual.valuesCount'), t('forecastActual.createdAt')]}
        loading={loading} empty={!data.items.length} filtered={Boolean(search)} loadingLabel={t('common.loading')}
        emptyTitle={t('forecastActual.noPlans')} emptyDescription={t('forecastActual.noPlansHint')}
        noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
        {data.items.map(plan => <tr key={plan.id}>
          <td><Link className="ui-object-link" to={`/mrp/planning/production-forecast/plans/${plan.id}?from=${returnContext}`}><strong>{plan.name}</strong></Link></td>
          <td><DualDateRange start={plan.startDate} end={plan.endDate} language={language} /></td>
          <td className="ui-technical">{plan.productCount}</td><td className="ui-technical">{plan.monthCount}</td>
          <td className="ui-technical">{plan.entryCount}</td><td>{new Date(plan.createdAtUtc).toLocaleString(language)}</td>
        </tr>)}
      </DataTable>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={data.totalCount}
        onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page: query.page, pages, count: data.totalCount })} />
    </section>
  </main>
}

export function ForecastPlanDetailPage({ canManage }: { canManage: boolean }) {
  const { id = '' } = useParams()
  const { t, i18n } = useTranslation()
  const query = useListQuery(pageSize)
  const backTo = useListReturn('/mrp/planning/production-forecast')
  const language = i18n.resolvedLanguage ?? 'fa-IR'
  const [data, setData] = useState<PlanDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [target, setTarget] = useState<PlanCell | null>(null)
  const [quantity, setQuantity] = useState('')
  const [reason, setReason] = useState('')
  const [conflict, setConflict] = useState<CorrectionConflict | null>(null)
  const [editError, setEditError] = useState('')
  const [saving, setSaving] = useState(false)

  const display = useCallback((value: { nameFa: string; nameEn?: string | null }) =>
    localizedName(value, language), [language])
  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const response = await fetch(`${apiRoot}/planning/forecast-plans/${id}?page=${query.page}&pageSize=${query.pageSize}`)
      if (!response.ok) throw new Error(await problemText(response))
      setData(await response.json() as PlanDetail)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoading(false)
    }
  }, [id, query.page, query.pageSize, t])
  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0)
    return () => window.clearTimeout(timer)
  }, [load])

  const cells = useMemo(() => new Map(data?.cells.map(cell => [`${cell.periodCode}:${cell.productId}`, cell]) ?? []), [data])
  const months = useMemo(() => new Map(data?.months.map(month => [month.periodCode, month]) ?? []), [data])
  const pages = Math.max(1, Math.ceil((data?.totalProducts ?? 0) / query.pageSize))
  const editingProduct = target ? data?.products.find(product => product.id === target.productId) : undefined

  function beginEdit(cell: PlanCell) {
    setTarget(cell)
    setQuantity(String(cell.forecastQty))
    setReason('')
    setConflict(null)
    setEditError('')
    setError('')
  }

  async function saveCorrection(event?: FormEvent, confirmDifference = false) {
    event?.preventDefault()
    if (!target) return
    if (!quantity.trim() || Number(quantity) < 0) { setEditError(t('forecastActual.forecastQtyRequired')); return }
    if (!reason.trim()) { setEditError(t('forecastActual.correctionReasonRequired')); return }
    setSaving(true)
    setError('')
    setEditError('')
    try {
      const response = await fetch(`${apiRoot}/planning/forecast-plans/${id}/entries/${target.entryId}/corrections`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          forecastQty: Number(quantity), reason: reason.trim(),
          expectedSeriesVersion: conflict?.seriesVersion ?? target.seriesVersion,
          expectedPlanRevisionNumber: target.planRevisionNumber, confirmDifference,
        }),
      })
      if (response.status === 409) {
        const body = await response.json() as ProblemBody
        if (body.code === 'FORECAST_VALUE_CONFLICT' && body.details?.length) {
          setConflict(body.details[0])
          return
        }
        throw new Error(problemBodyText(body, response.status))
      }
      if (!response.ok) throw new Error(await problemText(response))
      setTarget(null)
      setConflict(null)
      setReason('')
      setMessage(t('forecastActual.monthlyValueCorrected'))
      await load()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setSaving(false)
    }
  }

  if (loading && !data) return <main className="app-shell forecast-plan-page"><LoadingState label={t('common.loading')} /></main>
  if (!data) return <main className="app-shell forecast-plan-page">{error
    ? <MessageStrip tone="critical">{error}</MessageStrip>
    : <EmptyState title={t('common.emptyTitle')} />}</main>

  return <main className="app-shell forecast-plan-page">
    <PageHeader sticky eyebrow={t('forecastActual.savedPlan')} title={data.plan.name}
      subtitle={`${t('forecastActual.periodStart')}: ${data.plan.startJalaliDate} · ${t('forecastActual.periodEnd')}: ${data.plan.endJalaliDate}`}
      backTo={backTo} backLabel={t('forecastActual.backToPlans')}
      actions={<Link className="button" to="/mrp/planning/production-forecast/new">{t('forecastActual.createPlan')}</Link>} />
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <Section title={t('forecastActual.planSummary')}>
      <div className="forecast-plan-summary">
        <span><strong>{t('forecastActual.productsCount')}:</strong> {data.plan.productCount}</span>
        <span><strong>{t('forecastActual.monthsCount')}:</strong> {data.plan.monthCount}</span>
        <span><strong>{t('forecastActual.valuesCount')}:</strong> {data.plan.entryCount}</span>
      </div>
    </Section>
    <Section title={t('forecastActual.monthlyMatrix')} description={t('forecastActual.savedMatrixHint')}
      actions={<StatusBadge tone="information" text={t('forecastActual.monthCount', { count: data.months.length })} />}>
      <div className="forecast-matrix-scroll">
        <table className="forecast-matrix forecast-plan-detail-matrix">
          <thead><tr><th scope="col">{t('forecastActual.month')}</th><th scope="col"><span className="forecast-period-column-title"><span>{t('forecastActual.periodStart')}</span><span>{t('forecastActual.periodEnd')}</span></span></th>
            {data.products.map(product => <th scope="col" key={product.id}><span>{display(product)}</span><small className="ui-technical">{product.code}</small></th>)}</tr></thead>
          <tbody>{data.months.map(month => <tr key={month.periodCode}>
            <th scope="row"><span className="ui-technical">{month.periodCode}</span><small><StatusBadge tone={month.status === 'OPEN' ? 'positive' : 'neutral'} text={t(`forecastActual.${month.status}`)} /></small></th>
            <td><DualDateRange start={month.startDate} end={month.endDate} language={language} /></td>
            {data.products.map(product => {
              const cell = cells.get(`${month.periodCode}:${product.id}`)
              if (!cell) return <td key={product.id}>—</td>
              return <td key={product.id} className="forecast-saved-cell">
                <div className="forecast-actual-cell">
                  <div className="forecast-actual-row forecast-actual-row--forecast">
                    <div className="forecast-actual-row__heading">
                      <span>{t('forecastActual.forecast')}</span>
                      <button type="button" className="forecast-cell-edit" disabled={!canManage || months.get(cell.periodCode)?.status !== 'OPEN'}
                        aria-label={`${t('common.edit')} ${display(product)} ${cell.periodCode}`} title={t('forecastActual.editMonthlyValue')}
                        onClick={() => beginEdit(cell)}><Pencil aria-hidden="true" /></button>
                    </div>
                    <strong className="ui-technical">{formatQuantity(cell.forecastQty, language)}</strong>
                    <small>{t('common.version')} {cell.planRevisionNumber}</small>
                    {!cell.isOfficialCurrent && <small className="forecast-cell-warning"><TriangleAlert aria-hidden="true" />{t('forecastActual.newerOfficialForecast')}</small>}
                  </div>
                  <div className="forecast-actual-row forecast-actual-row--actual">
                    <span>{t('forecastActual.monthlyActual')}</span>
                    <strong className="ui-technical">{formatQuantity(cell.monthlyActualQty, language)}</strong>
                    <small>{cell.dailyActualEntryCount > 0
                      ? t('forecastActual.dailyActualEntries', { count: cell.dailyActualEntryCount })
                      : t('forecastActual.noDailyActualEntries')}</small>
                  </div>
                </div>
              </td>
            })}
          </tr>)}</tbody>
        </table>
      </div>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={data.totalProducts}
        onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('forecastActual.productPage', { page: query.page, pages, count: data.totalProducts })} />
    </Section>

    <ConfirmationDialog open={Boolean(target)} title={t('forecastActual.editMonthlyValue')}
      className="forecast-edit-dialog"
      description={target ? `${display(editingProduct ?? { nameFa: '' })} · ${editingProduct?.code ?? ''} · ${target.periodCode}` : undefined}
      confirmLabel={conflict ? t('forecastActual.confirmCorrection') : t('common.save')}
      cancelLabel={t('common.cancel')} tone="normal" onCancel={() => { setTarget(null); setConflict(null); setEditError('') }}
      confirmDisabled={saving || !isValidForecastQuantity(quantity) || !reason.trim()}
      onConfirm={() => void saveCorrection(undefined, Boolean(conflict))}>
      <form className="forecast-cell-edit-form" onSubmit={event => void saveCorrection(event, Boolean(conflict))}>
        {target && <div className="forecast-edit-context" aria-label={t('forecastActual.forecastActualComparison')}>
          <div><span>{t('forecastActual.currentForecast')}</span><strong className="ui-technical">{formatQuantity(target.forecastQty, language)}</strong></div>
          <div><span>{t('forecastActual.monthlyActual')}</span><strong className="ui-technical">{formatQuantity(target.monthlyActualQty, language)}</strong>
            <small>{target.dailyActualEntryCount > 0
              ? t('forecastActual.dailyActualEntries', { count: target.dailyActualEntryCount })
              : t('forecastActual.noDailyActualEntries')}</small></div>
        </div>}
        <MessageStrip>{t('forecastActual.actualReadOnlyHint')}</MessageStrip>
        <FormField label={t('forecastActual.newForecast')} required hint={t('forecastActual.quantityInputHint')}>
          <input autoFocus required className="ui-technical" dir="ltr" inputMode="decimal" value={quantity}
            onChange={event => {
              const value = normalizeDecimalInput(event.target.value)
              if (!value || /^\d*(\.\d{0,6})?$/.test(value)) setQuantity(value)
            }} />
        </FormField>
        <FormField label={t('forecastActual.reason')} required hint={t('forecastActual.editReasonHint')}>
          <textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} />
        </FormField>
        {conflict && <MessageStrip tone="warning">{t('forecastActual.planEditConflict', {
          plan: conflict.planForecastQty, official: conflict.officialForecastQty, proposed: conflict.proposedQty,
        })}</MessageStrip>}
        {editError && <MessageStrip tone="critical">{editError}</MessageStrip>}
        {target && <details className="forecast-cell-history"><summary>{t('forecastActual.changeHistory')}</summary><ol>
          {target.history.map(row => <li key={row.revisionNumber}><span className="ui-technical">v{row.revisionNumber} · {row.forecastQty}</span><span>{row.reason ?? '—'}</span></li>)}
        </ol></details>}
        <button type="submit" hidden disabled={saving || !isValidForecastQuantity(quantity) || !reason.trim()} />
      </form>
    </ConfirmationDialog>
  </main>
}

function DualDateRange({ start, end, language }: { start: string; end: string; language: string }) {
  const range = formatDualBusinessDateRange(start, end, language)
  return <div className="forecast-dual-date-range">
    <span className="forecast-dual-date-range__primary" dir="ltr">{range.primary}</span>
    <small className="forecast-dual-date-range__secondary" dir="ltr">{range.secondary}</small>
  </div>
}

async function problemText(response: Response) {
  const body = await response.json().catch(() => null) as ProblemBody | null
  return problemBodyText(body, response.status)
}

function problemBodyText(body: ProblemBody | null, status: number) {
  return [body?.code, body?.detail ?? body?.title, ...Object.values(body?.errors ?? {}).flat()]
    .filter(Boolean).join(' — ') || `HTTP ${status}`
}

const persianDigits = '۰۱۲۳۴۵۶۷۸۹'
const arabicDigits = '٠١٢٣٤٥٦٧٨٩'

function normalizeDecimalInput(value: string) {
  return value
    .replace(/[۰-۹]/g, digit => String(persianDigits.indexOf(digit)))
    .replace(/[٠-٩]/g, digit => String(arabicDigits.indexOf(digit)))
    .replace(/٫/g, '.')
    .replace(/[٬،,\s]/g, '')
}

function isValidForecastQuantity(value: string) {
  const normalized = normalizeDecimalInput(value)
  return /^\d+(\.\d{0,6})?$/.test(normalized) && Number.isFinite(Number(normalized)) && Number(normalized) >= 0
}
