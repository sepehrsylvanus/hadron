import { FormEvent, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatBusinessDate } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { normalizeJalaliDateInput, toAsciiDigits } from './mrpRunUiSupport'
import {
  ConfirmationDialog, DataTable, FilterBar, FormField, MessageStrip, PageHeader,
  Pagination, Section, StatusBadge, TableToolbar,
} from './ui'
import { useListQuery } from './ui/routeContext'
import { ForecastPlanListPage } from './ForecastPlanPages'

type Localized = { id: string; code: string; nameFa: string; nameEn?: string | null; isActive: boolean }
type Unit = Localized & { symbol?: string | null }
type Product = { id: string; code: string; nameFa: string; nameEn?: string | null; isActive: boolean; productCategory: Localized; businessUnit?: Localized | null; baseUnit: Unit }
type Period = { id: string; jalaliYear: number; jalaliMonth: number; startDate: string; endDate: string; status: 'OPEN' | 'CLOSED'; version: number }
type BomReadiness = { state: string; reasonCodes: string[]; evaluationStart?: string | null; evaluationEnd?: string | null }
type ForecastRow = { product: Product; seriesId?: string | null; currentRevisionId?: string | null; currentRevisionNumber?: number | null; forecastQty?: number | null; seriesVersion?: number | null; monthlyActual: number; actualMtd: number; remainingForecast?: number | null; planningQty?: number | null; periodPosition: string; forecastState: string; eligibleAllocationStart?: string | null; planningExceptionCode?: string | null; bomReadiness: BomReadiness }
type ForecastList = { period: Period; evaluationDate: string; items: ForecastRow[]; page: number; pageSize: number; totalCount: number }
type Actual = { id: string; product: Product; period: Period; productionDate: string; unit: Unit; currentRevision: { id: string; revisionNumber: number; actualQty: number; reason?: string | null; createdAtUtc: string }; version: number }
type ActualList = { items: Actual[]; page: number; pageSize: number; totalCount: number }
type ProductList = { items: Product[]; totalCount: number }
type ForecastHistory = { revisions: { id: string; revisionNumber: number; forecastQty: number; reason?: string | null; createdAtUtc: string }[] }
type ResolvedProductionDate = { jalaliDate: string; gregorianDate: string }
type PlanningView = 'forecast' | 'actual'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')

export default function ForecastActualPage({ view }: { view: PlanningView }) {
  if (view === 'forecast') return <ForecastPlanListPage />
  return <ForecastActualLegacyPage view={view} />
}

function ForecastActualLegacyPage({ view }: { view: PlanningView }) {
  const { t, i18n } = useTranslation()
  const query = useListQuery(25)
  const periodId = query.value('period')
  const evaluationDate = query.value('evaluationDate')
  const search = query.value('search')
  const includeInactive = query.flag('includeInactive')
  const categoryId = query.value('category')
  const businessUnitId = query.value('businessUnit')
  const updateQueryRef = useRef(query.update)
  const [periods, setPeriods] = useState<Period[]>([])
  const [periodPage, setPeriodPage] = useState(1)
  const [periodTotal, setPeriodTotal] = useState(0)
  const [rows, setRows] = useState<ForecastRow[]>([])
  const [total, setTotal] = useState(0)
  const [actuals, setActuals] = useState<Actual[]>([])
  const [actualTotal, setActualTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [periodForm, setPeriodForm] = useState({ year: '', month: '' })
  const [forecastTarget, setForecastTarget] = useState<ForecastRow | null>(null)
  const [forecastQty, setForecastQty] = useState('')
  const [forecastReason, setForecastReason] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [actualForm, setActualForm] = useState({ productId: '', productionJalaliDate: '', actualQty: '' })
  const [resolvedProductionDate, setResolvedProductionDate] = useState<ResolvedProductionDate | null>(null)
  const [categories, setCategories] = useState<Localized[]>([])
  const [businessUnits, setBusinessUnits] = useState<Localized[]>([])
  const [correction, setCorrection] = useState<Actual | null>(null)
  const [correctionQty, setCorrectionQty] = useState('')
  const [correctionReason, setCorrectionReason] = useState('')
  const [history, setHistory] = useState<ForecastHistory | null>(null)
  const [periodAction, setPeriodAction] = useState<'close' | 'reopen' | null>(null)
  const [periodReason, setPeriodReason] = useState('')
  const selectedPeriod = periods.find(value => value.id === periodId)
  const display = (value: { nameFa: string; nameEn?: string | null }) => localizedName(value, i18n.resolvedLanguage)

  useEffect(() => { updateQueryRef.current = query.update }, [query.update])

  const loadPeriods = useCallback(async () => {
    const response = await fetch(`${api}/planning/periods?page=${periodPage}&pageSize=24`)
    if (!response.ok) throw new Error(await problem(response))
    const body = await response.json()
    setPeriods(body.items)
    setPeriodTotal(body.totalCount)
    if (!periodId && body.items.length) updateQueryRef.current({ period: body.items[0].id })
  }, [periodId, periodPage])

  const load = useCallback(async () => {
    if (view === 'forecast' && (!periodId || !evaluationDate)) {
      setRows([]); setTotal(0); setActuals([]); setActualTotal(0); setLoading(false)
      return
    }
    setLoading(true)
    setError('')
    try {
      if (view === 'forecast') {
        const parameters = new URLSearchParams({
          periodId, evaluationDate, search, includeInactive: String(includeInactive),
          page: String(query.page), pageSize: String(query.pageSize),
        })
        if (categoryId) parameters.set('productCategoryId', categoryId)
        if (businessUnitId) parameters.set('businessUnitId', businessUnitId)
        const response = await fetch(`${api}/planning/forecast?${parameters}`)
        if (!response.ok) throw new Error(await problem(response))
        const body = await response.json() as ForecastList
        setRows(body.items)
        setTotal(body.totalCount)
      } else {
        const parameters = new URLSearchParams({
          page: String(query.page), pageSize: String(query.pageSize),
        })
        const response = await fetch(`${api}/planning/actuals?${parameters}`)
        if (!response.ok) throw new Error(await problem(response))
        const body = await response.json() as ActualList
        setActuals(body.items)
        setActualTotal(body.totalCount)
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoading(false)
    }
  }, [businessUnitId, categoryId, evaluationDate, includeInactive, periodId, query.page, query.pageSize, search, t, view])

  useEffect(() => {
    if (view !== 'forecast' || evaluationDate) return
    void fetch(`${api}/planning/business-date`)
      .then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() })
      .then(body => updateQueryRef.current({ evaluationDate: body.tehranBusinessDate }))
      .catch(() => setError(t('common.error')))
  }, [evaluationDate, t, view])

  useEffect(() => {
    if (view !== 'actual' || actualForm.productionJalaliDate) return
    void fetch(`${api}/planning/business-date`)
      .then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() })
      .then((body: { jalaliDate: string; tehranBusinessDate: string }) => {
        setActualForm(current => current.productionJalaliDate
          ? current
          : { ...current, productionJalaliDate: body.jalaliDate })
        setResolvedProductionDate({ jalaliDate: body.jalaliDate, gregorianDate: body.tehranBusinessDate })
      })
      .catch(() => setError(t('common.error')))
  }, [actualForm.productionJalaliDate, t, view])

  useEffect(() => {
    if (view !== 'forecast') return
    const timer = window.setTimeout(() => void loadPeriods().catch(caught =>
      setError(caught instanceof Error ? caught.message : t('common.error'))), 0)
    return () => window.clearTimeout(timer)
  }, [loadPeriods, t, view])

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 100)
    return () => window.clearTimeout(timer)
  }, [load])

  useEffect(() => {
    if (view !== 'actual') return
    const timer = window.setTimeout(() => {
      const parameters = new URLSearchParams({ search: productSearch, includeInactive: 'false', page: '1', pageSize: '25' })
      void fetch(`${api}/products?${parameters}`)
        .then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() })
        .then((body: ProductList) => setProducts(body.items))
        .catch(() => setProducts([]))
    }, 150)
    return () => window.clearTimeout(timer)
  }, [productSearch, view])

  useEffect(() => {
    if (view !== 'forecast') return
    void Promise.all([
      fetch(`${api}/reference-data/types/PRODUCT_CATEGORY/values`).then(response => response.json()),
      fetch(`${api}/reference-data/types/BUSINESS_UNIT/values`).then(response => response.json()),
    ]).then(([categoryRows, businessRows]) => {
      setCategories(categoryRows)
      setBusinessUnits(businessRows)
    }).catch(() => setError(t('common.error')))
  }, [t, view])

  const filtered = Boolean(search || includeInactive || categoryId || businessUnitId)
  const periodPages = Math.max(1, Math.ceil(periodTotal / 24))
  const actualPages = Math.max(1, Math.ceil(actualTotal / query.pageSize))

  async function createPeriod(event: FormEvent) {
    event.preventDefault()
    await mutate(`${api}/planning/periods`, {
      jalaliYear: Number(periodForm.year), jalaliMonth: Number(periodForm.month),
    }, async body => {
      setPeriodForm({ year: '', month: '' })
      setMessage(t('common.saved'))
      await loadPeriods()
      query.update({ period: String(body.id), page: 1 })
    })
  }

  async function saveForecast(event: FormEvent) {
    event.preventDefault()
    if (!forecastTarget || !periodId) return
    const revision = Boolean(forecastTarget.seriesId)
    const url = revision ? `${api}/planning/forecast/${forecastTarget.seriesId}/revisions` : `${api}/planning/forecast`
    const body = revision
      ? { forecastQty: Number(forecastQty), reason: forecastReason || null, expectedVersion: forecastTarget.seriesVersion }
      : { productId: forecastTarget.product.id, businessPeriodId: periodId, forecastQty: Number(forecastQty), reason: forecastReason || null }
    await mutate(url, body, async () => {
      setForecastTarget(null); setForecastReason(''); setMessage(t('common.saved')); await load()
    })
  }

  async function createActual(event: FormEvent) {
    event.preventDefault()
    setError('')
    const normalized = normalizeJalaliDateInput(actualForm.productionJalaliDate)
    if (!normalized) { setError(t('productionActual.jalaliDateInvalid')); return }
    try {
      const response = await fetch(`${api}/planning/resolve-jalali-date`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jalaliDate: normalized }),
      })
      if (!response.ok) throw new Error(await problem(response))
      const resolved = await response.json() as ResolvedProductionDate
      setResolvedProductionDate(resolved)
      await mutate(`${api}/planning/actuals`, {
        productId: actualForm.productId, productionDate: resolved.gregorianDate,
        actualQty: Number(actualForm.actualQty),
      }, async () => {
        setActualForm(current => ({ ...current, productId: '', actualQty: '' }))
        setMessage(t('common.saved'))
        await load()
      })
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    }
  }

  async function previewProductionDate(value: string) {
    const normalized = normalizeJalaliDateInput(value)
    if (!normalized) { setResolvedProductionDate(null); return }
    try {
      const response = await fetch(`${api}/planning/resolve-jalali-date`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jalaliDate: normalized }),
      })
      if (!response.ok) throw new Error(await problem(response))
      setResolvedProductionDate(await response.json() as ResolvedProductionDate)
    } catch {
      setResolvedProductionDate(null)
    }
  }

  async function correctActual(event?: FormEvent) {
    event?.preventDefault()
    if (!correction) return
    await mutate(`${api}/planning/actuals/${correction.id}/corrections`, {
      actualQty: Number(correctionQty), reason: correctionReason, expectedVersion: correction.version,
    }, async () => {
      setCorrection(null); setCorrectionReason(''); setMessage(t('common.saved')); await load()
    })
  }

  async function changePeriod() {
    if (!selectedPeriod || !periodAction) return
    await mutate(`${api}/planning/periods/${selectedPeriod.id}/${periodAction}`, {
      expectedVersion: selectedPeriod.version, reason: periodReason || null,
    }, async () => {
      setPeriodAction(null); setPeriodReason(''); setMessage(t('common.saved'))
      await loadPeriods(); await load()
    })
  }

  async function showHistory(seriesId: string) {
    try {
      const response = await fetch(`${api}/planning/forecast/${seriesId}/revisions`)
      if (!response.ok) throw new Error(await problem(response))
      setHistory(await response.json())
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    }
  }

  async function mutate(url: string, body: object, success: (body: Record<string, unknown>) => Promise<void>) {
    setError(''); setMessage('')
    try {
      const response = await fetch(url, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
      })
      if (!response.ok) throw new Error(await problem(response))
      await success(await response.json())
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    }
  }

  const readinessTone = (state: string) => state === 'READY'
    ? 'positive' : state === 'NOT_APPLICABLE' ? 'neutral' : state === 'BLOCKED' ? 'critical' : 'warning'

  return <main className="app-shell">
    <PageHeader
      title={t(view === 'forecast' ? 'forecastActual.forecastTitle' : 'productionActual.title')}
      subtitle={t(view === 'forecast' ? 'forecastActual.forecastSubtitle' : 'productionActual.subtitle')}
      actions={view === 'forecast' && selectedPeriod && <>
        <StatusBadge text={t(`forecastActual.${selectedPeriod.status}`)} tone={selectedPeriod.status === 'OPEN' ? 'positive' : 'neutral'} />
        {view === 'forecast' && <button className="button" onClick={() => setPeriodAction(selectedPeriod.status === 'OPEN' ? 'close' : 'reopen')}>
          {selectedPeriod.status === 'OPEN' ? t('forecastActual.closePeriod') : t('forecastActual.reopenPeriod')}
        </button>}
      </>}
    />
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}

    {view === 'forecast' && <Section title={t('forecastActual.periods')} actions={<span className="ui-count">{periodPage} / {periodPages}</span>}>
      <div className="ui-form-grid ui-form-grid--3">
        <FormField label={t('forecastActual.period')}>
          <select value={periodId} onChange={event => query.update({ period: event.target.value, page: 1 })}>
            <option value="">{t('common.notSet')}</option>
            {periods.map(period => <option key={period.id} value={period.id}>
              {period.jalaliYear}/{String(period.jalaliMonth).padStart(2, '0')} — {t(`forecastActual.${period.status}`)}
            </option>)}
          </select>
        </FormField>
        <FormField label={t('forecastActual.evaluationDate')}>
          <input dir="ltr" type="date" value={evaluationDate} onChange={event => query.update({ evaluationDate: event.target.value, page: 1 })} />
        </FormField>
        <div className="inline-actions">
          <button className="button button--quiet" disabled={periodPage <= 1} onClick={() => setPeriodPage(value => value - 1)}>{t('common.previous')}</button>
          <button className="button button--quiet" disabled={periodPage >= periodPages} onClick={() => setPeriodPage(value => value + 1)}>{t('common.next')}</button>
        </div>
      </div>
      <form className="ui-form-grid ui-form-grid--3 planning-inline-form" onSubmit={event => void createPeriod(event)}>
        <FormField label={t('forecastActual.jalaliYear')} required>
          <input required inputMode="numeric" value={periodForm.year} onChange={event => setPeriodForm({ ...periodForm, year: event.target.value })} />
        </FormField>
        <FormField label={t('forecastActual.jalaliMonth')} required>
          <input required type="number" min="1" max="12" value={periodForm.month} onChange={event => setPeriodForm({ ...periodForm, month: event.target.value })} />
        </FormField>
        <button className="button button--primary" type="submit">{t('forecastActual.createPeriod')}</button>
      </form>
    </Section>}

    {view === 'forecast' && <ForecastSurface
      rows={rows} total={total} loading={loading} filtered={filtered} categories={categories}
      businessUnits={businessUnits} categoryId={categoryId} businessUnitId={businessUnitId}
      search={search} includeInactive={includeInactive} selectedPeriod={selectedPeriod}
      display={display} readinessTone={readinessTone} query={query}
      onSelectForecast={row => { setForecastTarget(row); setForecastQty(String(row.forecastQty ?? '')); setForecastReason('') }}
      onShowHistory={seriesId => void showHistory(seriesId)}
    />}

    {view === 'forecast' && forecastTarget && <Section title={forecastTarget.seriesId ? t('forecastActual.revise') : t('forecastActual.enterForecast')}>
      <form className="ui-form-grid ui-form-grid--3" onSubmit={event => void saveForecast(event)}>
        <FormField label={t('forecastActual.product')}><input disabled value={`${forecastTarget.product.code} — ${display(forecastTarget.product)}`} /></FormField>
        <FormField label={t('forecastActual.forecast')} required><input required className="ui-technical" type="number" min="0" step="0.000001" value={forecastQty} onChange={event => setForecastQty(event.target.value)} /></FormField>
        <FormField label={t('forecastActual.reason')} required={Boolean(forecastTarget.seriesId)}><input required={Boolean(forecastTarget.seriesId)} value={forecastReason} onChange={event => setForecastReason(event.target.value)} /></FormField>
        <div className="inline-actions"><button className="button button--primary" type="submit">{t('common.save')}</button><button className="button" type="button" onClick={() => setForecastTarget(null)}>{t('common.cancel')}</button></div>
      </form>
    </Section>}

    {view === 'forecast' && history && <Section title={t('forecastActual.history')} actions={<button className="button button--quiet" onClick={() => setHistory(null)}>{t('common.close')}</button>}>
      <DataTable headers={[t('common.version'), t('forecastActual.forecast'), t('forecastActual.reason'), t('forecastActual.createdAt')]} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')} empty={!history.revisions.length}>
        {history.revisions.map(revision => <tr key={revision.id}><td>{revision.revisionNumber}</td><td className="ui-number">{formatOperationalNumber(revision.forecastQty, i18n.resolvedLanguage)}</td><td>{revision.reason ?? '—'}</td><td className="ui-technical">{new Date(revision.createdAtUtc).toLocaleString()}</td></tr>)}
      </DataTable>
    </Section>}

    {view === 'actual' && <ActualSurface
      actuals={actuals} total={actualTotal} loading={loading} products={products}
      productSearch={productSearch} actualForm={actualForm} resolvedProductionDate={resolvedProductionDate}
      display={display} page={query.page} pageSize={query.pageSize} pages={actualPages}
      language={i18n.resolvedLanguage ?? 'fa-IR'} correction={correction}
      correctionQty={correctionQty} correctionReason={correctionReason}
      onProductSearch={setProductSearch} onFormChange={value => {
        if (value.productionJalaliDate !== actualForm.productionJalaliDate) setResolvedProductionDate(null)
        setActualForm(value)
      }}
      onSubmit={createActual} onPageChange={page => query.update({ page })}
      onCorrect={actual => { setCorrection(actual); setCorrectionQty(String(actual.currentRevision.actualQty)); setCorrectionReason('') }}
      onPreviewDate={previewProductionDate} onCorrectionQty={setCorrectionQty}
      onCorrectionReason={setCorrectionReason} onSubmitCorrection={correctActual}
      onCancelCorrection={() => { setCorrection(null); setCorrectionReason('') }}
    />}

    {view === 'forecast' && <ConfirmationDialog open={periodAction !== null} title={periodAction === 'close' ? t('forecastActual.closePeriod') : t('forecastActual.reopenPeriod')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} onConfirm={() => void changePeriod()} onCancel={() => setPeriodAction(null)}>
      {periodAction === 'reopen' && <FormField label={t('forecastActual.reason')} required><input autoFocus required value={periodReason} onChange={event => setPeriodReason(event.target.value)} /></FormField>}
    </ConfirmationDialog>}
  </main>
}

type QueryModel = ReturnType<typeof useListQuery>

function ForecastSurface({ rows, total, loading, filtered, categories, businessUnits, categoryId,
  businessUnitId, search, includeInactive, selectedPeriod, display, readinessTone, query,
  onSelectForecast, onShowHistory }: {
  rows: ForecastRow[]; total: number; loading: boolean; filtered: boolean; categories: Localized[];
  businessUnits: Localized[]; categoryId: string; businessUnitId: string; search: string;
  includeInactive: boolean; selectedPeriod?: Period; display: (value: Localized | Product | Unit) => string;
  readinessTone: (state: string) => 'positive' | 'neutral' | 'critical' | 'warning'; query: QueryModel;
  onSelectForecast: (row: ForecastRow) => void; onShowHistory: (seriesId: string) => void;
}) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  return <>
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('common.search')}><input value={search} onChange={event => query.update({ search: event.target.value }, true)} placeholder={t('products.searchPlaceholder')} /></FormField>
      <FormField label={t('common.category')}><select value={categoryId} onChange={event => query.update({ category: event.target.value }, true)}><option value="">{t('common.all')}</option>{categories.map(value => <option key={value.id} value={value.id}>{display(value)}</option>)}</select></FormField>
      <FormField label={t('products.businessUnit')}><select value={businessUnitId} onChange={event => query.update({ businessUnit: event.target.value }, true)}><option value="">{t('common.all')}</option>{businessUnits.map(value => <option key={value.id} value={value.id}>{display(value)}</option>)}</select></FormField>
      <label className="checkbox-label"><input type="checkbox" checked={includeInactive} onChange={event => query.update({ includeInactive: event.target.checked }, true)} />{t('common.includeInactive')}</label>
    </FilterBar>
    <section className="ui-table-surface">
      <TableToolbar title={t('forecastActual.forecastGrid')} count={t('common.count', { count: total })} />
      <DataTable headers={[t('common.code'), t('forecastActual.product'), t('forecastActual.forecast'), t('forecastActual.actualMtd'), t('forecastActual.remaining'), t('forecastActual.planningQty'), t('forecastActual.bomReadiness'), t('common.actions')]} loading={loading} empty={!rows.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} emptyDescription={t('common.emptyDescription')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
        {rows.map(row => <tr key={row.product.id}>
          <td className="ui-technical">{row.product.code}</td>
          <td>{display(row.product)}{!row.product.isActive && <small> — {t('common.inactive')}</small>}</td>
          <td className="ui-number">{row.forecastQty == null ? t('forecastActual.missing') : formatOperationalNumber(row.forecastQty, language)}</td>
          <td className="ui-number">{formatOperationalNumber(row.actualMtd, language)}</td>
          <td className="ui-number">{formatOperationalNumber(row.remainingForecast, language)}</td>
          <td className="ui-number">{formatOperationalNumber(row.planningQty, language)}{row.planningExceptionCode && <small className="ui-field-error">{t('forecastActual.unallocatable')}</small>}</td>
          <td><StatusBadge text={t(`forecastActual.readiness.${row.bomReadiness.state}`)} tone={readinessTone(row.bomReadiness.state)} /></td>
          <td className="actions"><button className="button button--quiet" disabled={selectedPeriod?.status !== 'OPEN'} onClick={() => onSelectForecast(row)}>{row.seriesId ? t('forecastActual.revise') : t('forecastActual.enterForecast')}</button>{row.seriesId && <button className="button button--quiet" onClick={() => onShowHistory(row.seriesId!)}>{t('forecastActual.history')}</button>}</td>
        </tr>)}
      </DataTable>
      <Pagination page={query.page} pageSize={query.pageSize} totalCount={total} onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(total / query.pageSize)), count: total })} />
    </section>
  </>
}

function ActualSurface({ actuals, total, loading, products, productSearch, actualForm,
  resolvedProductionDate, display, language, page, pageSize, pages, correction, correctionQty,
  correctionReason, onProductSearch, onFormChange, onSubmit, onPageChange, onCorrect,
  onPreviewDate, onCorrectionQty, onCorrectionReason, onSubmitCorrection, onCancelCorrection }: {
  actuals: Actual[]; total: number; loading: boolean; products: Product[];
  productSearch: string; actualForm: { productId: string; productionJalaliDate: string; actualQty: string };
  resolvedProductionDate: ResolvedProductionDate | null;
  display: (value: Product | Unit) => string; language: string; page: number; pageSize: number; pages: number;
  correction: Actual | null; correctionQty: string; correctionReason: string;
  onProductSearch: (value: string) => void;
  onFormChange: (value: { productId: string; productionJalaliDate: string; actualQty: string }) => void;
  onSubmit: (event: FormEvent) => Promise<void>; onPageChange: (page: number) => void;
  onCorrect: (actual: Actual) => void; onPreviewDate: (value: string) => Promise<void>;
  onCorrectionQty: (value: string) => void; onCorrectionReason: (value: string) => void;
  onSubmitCorrection: () => Promise<void>; onCancelCorrection: () => void;
}) {
  const { t } = useTranslation()
  const selectedProduct = products.find(product => product.id === actualForm.productId)
  const createFormId = 'daily-actual-create'
  return <Section title={t('forecastActual.dailyActual')} description={t('productionActual.tableHint')}>
    <div className="ui-table-surface daily-production-surface">
      <TableToolbar title={t('forecastActual.actualList')} count={t('common.count', { count: total })}
        actions={<label className="daily-production-search"><span>{t('productionActual.productSearch')}</span>
          <input value={productSearch} onChange={event => onProductSearch(event.target.value)}
            placeholder={t('products.searchPlaceholder')} /></label>} />
      <form id={createFormId} onSubmit={event => void onSubmit(event)} />
      <div className="ui-table-wrap">
        <table className="ui-data-table daily-production-table">
          <thead><tr>
            <th scope="col">{t('forecastActual.productionDate')}</th><th scope="col">{t('common.code')}</th>
            <th scope="col">{t('forecastActual.product')}</th><th scope="col">{t('forecastActual.actualQty')}</th>
            <th scope="col">{t('common.unit')}</th><th scope="col">{t('common.version')}</th>
            <th scope="col">{t('forecastActual.reason')}</th><th scope="col">{t('common.actions')}</th>
          </tr></thead>
          <tbody>
            <tr className="daily-production-new-row">
              <td><input form={createFormId} required className="ui-technical daily-production-date-input"
                dir="ltr" inputMode="numeric" maxLength={8} placeholder="14050609"
                aria-label={t('productionActual.jalaliDate')}
                value={actualForm.productionJalaliDate}
                onChange={event => onFormChange({ ...actualForm,
                  productionJalaliDate: toAsciiDigits(event.target.value).replace(/[^0-9]/g, '').slice(0, 8) })}
                onBlur={() => void onPreviewDate(actualForm.productionJalaliDate)} />
                <small>{resolvedProductionDate
                  ? t('productionActual.detectedDate', {
                    period: resolvedProductionDate.jalaliDate.slice(0, 6),
                    date: formatBusinessDate(resolvedProductionDate.gregorianDate, 'en'),
                  })
                  : t('productionActual.jalaliDateHint')}</small></td>
              <td className="ui-technical">{selectedProduct?.code ?? '—'}</td>
              <td><select form={createFormId} required value={actualForm.productId}
                aria-label={t('forecastActual.product')}
                onChange={event => onFormChange({ ...actualForm, productId: event.target.value })}>
                <option value="">{t('common.notSet')}</option>
                {products.map(product => <option key={product.id} value={product.id}>
                  {product.code} — {display(product)}
                </option>)}
              </select></td>
              <td><input form={createFormId} required className="ui-technical" dir="ltr" inputMode="decimal"
                aria-label={t('forecastActual.actualQty')} value={actualForm.actualQty}
                onChange={event => onFormChange({ ...actualForm,
                  actualQty: normalizeQuantityInput(event.target.value) })} /></td>
              <td>{selectedProduct ? display(selectedProduct.baseUnit) : '—'}</td>
              <td><StatusBadge text={t('productionActual.newRecord')} tone="information" /></td>
              <td>{t('productionActual.initialReasonNotRequired')}</td>
              <td><button form={createFormId} className="button button--primary" type="submit">
                {t('forecastActual.enterActual')}
              </button></td>
            </tr>
            {loading && <tr><td colSpan={8}>{t('common.loading')}</td></tr>}
            {!loading && actuals.length === 0 && <tr><td colSpan={8} className="daily-production-empty">
              {t('productionActual.noRecords')}
            </td></tr>}
            {!loading && actuals.map(actual => {
              const editing = correction?.id === actual.id
              return <tr key={actual.id} className={editing ? 'is-selected daily-production-edit-row' : undefined}>
                <td className="ui-technical">{formatBusinessDate(actual.productionDate, language)}</td>
                <td className="ui-technical">{actual.product.code}</td>
                <td>{display(actual.product)}<small>{actual.period.jalaliYear}/{String(actual.period.jalaliMonth).padStart(2, '0')}</small></td>
                <td>{editing
                  ? <input autoFocus className="ui-technical" dir="ltr" inputMode="decimal"
                    aria-label={t('productionActual.correctedQuantity')} value={correctionQty}
                    onChange={event => onCorrectionQty(normalizeQuantityInput(event.target.value))} />
                  : <span className="ui-number">{formatOperationalNumber(actual.currentRevision.actualQty, language)}</span>}</td>
                <td>{display(actual.unit)}</td>
                <td><span className="ui-technical">v{actual.currentRevision.revisionNumber}</span></td>
                <td>{editing
                  ? <input required value={correctionReason} aria-label={t('forecastActual.reason')}
                    placeholder={t('productionActual.correctionReasonHint')}
                    onChange={event => onCorrectionReason(event.target.value)} />
                  : actual.currentRevision.reason ?? t('productionActual.initialEntry')}</td>
                <td className="actions">{editing
                  ? <><button type="button" className="button button--primary"
                    disabled={!correctionQty || !correctionReason.trim()}
                    onClick={() => void onSubmitCorrection()}>
                    {t('common.save')}
                  </button><button type="button" className="button" onClick={onCancelCorrection}>{t('common.cancel')}</button></>
                  : <button type="button" className="button button--quiet" disabled={actual.period.status !== 'OPEN'}
                    onClick={() => onCorrect(actual)}>{t('common.edit')}</button>}</td>
              </tr>
            })}
          </tbody>
        </table>
      </div>
      <Pagination page={page} pageSize={pageSize} totalCount={total} onPageChange={onPageChange}
        previousLabel={t('common.previous')} nextLabel={t('common.next')}
        summary={t('common.page', { page, pages, count: total })} />
    </div>
  </Section>
}

async function problem(response: Response) {
  const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null
  return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}`
}

function normalizeQuantityInput(value: string) {
  const normalized = toAsciiDigits(value).replace('٫', '.').replace(/[٬,\s]/g, '')
  return !normalized || /^\d*(\.\d{0,6})?$/.test(normalized) ? normalized : value
}
