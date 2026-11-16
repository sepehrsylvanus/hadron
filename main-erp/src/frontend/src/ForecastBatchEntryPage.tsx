import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, Search, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { apiRoot } from './apiClient'
import { forecastMatrixKey, normalizeJalaliMonthDate } from './forecastBatchSupport'
import { formatBusinessDate } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { EmptyState, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge } from './ui'

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
type ProductList = { items: Product[]; page: number; pageSize: number; totalCount: number }
type PlanMonth = {
  periodCode: string
  jalaliYear: number
  jalaliMonth: number
  startDate: string
  endDate: string
  businessPeriodId?: string | null
  status: 'NOT_CREATED' | 'OPEN' | 'CLOSED'
}
type PlanCell = {
  productId: string
  periodCode: string
  currentForecastQty?: number | null
  currentRevisionNumber?: number | null
  seriesVersion?: number | null
}
type PlanPreview = {
  name: string
  startJalaliDate: string
  endJalaliDate: string
  startDate: string
  endDate: string
  months: PlanMonth[]
  products: Product[]
  cells: PlanCell[]
}
type ForecastConflict = {
  productId: string
  productCode: string
  productNameFa: string
  productNameEn?: string | null
  periodCode: string
  existingQty: number
  proposedQty: number
  seriesVersion: number
}
type SaveResult = {
  planId: string
  name: string
  startDate: string
  endDate: string
  createdCount: number
  revisedCount: number
  unchangedCount: number
  entryCount: number
}
type ProblemBody = {
  detail?: string
  title?: string
  code?: string
  details?: ForecastConflict[]
  errors?: Record<string, string[]>
}

const productPageSize = 20

export default function ForecastBatchEntryPage({ canManage }: { canManage: boolean }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? 'fa-IR'
  const pickerRef = useRef<HTMLDivElement>(null)
  const [name, setName] = useState('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [selectedProducts, setSelectedProducts] = useState<Product[]>([])
  const [pickerOpen, setPickerOpen] = useState(false)
  const [productSearch, setProductSearch] = useState('')
  const [productPage, setProductPage] = useState(1)
  const [productOptions, setProductOptions] = useState<ProductList>({ items: [], page: 1, pageSize: productPageSize, totalCount: 0 })
  const [loadingProducts, setLoadingProducts] = useState(false)
  const [preview, setPreview] = useState<PlanPreview | null>(null)
  const [quantities, setQuantities] = useState<Record<string, string>>({})
  const [conflicts, setConflicts] = useState<ForecastConflict[]>([])
  const [reason, setReason] = useState('')
  const [loadingPreview, setLoadingPreview] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const display = useCallback((value: { nameFa: string; nameEn?: string | null }) =>
    localizedName(value, language), [language])

  useEffect(() => {
    void fetch(`${apiRoot}/planning/business-date`)
      .then(async response => {
        if (!response.ok) throw new Error(await problemText(response))
        return response.json() as Promise<{ jalaliYear: number; jalaliMonth: number }>
      })
      .then(body => {
        const value = `${body.jalaliYear}${String(body.jalaliMonth).padStart(2, '0')}01`
        setStartDate(current => current || value)
        setEndDate(current => current || value)
      })
      .catch(caught => setError(caught instanceof Error ? caught.message : t('common.error')))
  }, [t])

  const loadProducts = useCallback(async () => {
    if (!pickerOpen) return
    setLoadingProducts(true)
    try {
      const parameters = new URLSearchParams({
        search: productSearch,
        page: String(productPage),
        pageSize: String(productPageSize),
      })
      const response = await fetch(`${apiRoot}/planning/forecast-products?${parameters}`)
      if (!response.ok) throw new Error(await problemText(response))
      setProductOptions(await response.json() as ProductList)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoadingProducts(false)
    }
  }, [pickerOpen, productPage, productSearch, t])

  useEffect(() => {
    const timer = window.setTimeout(() => void loadProducts(), 180)
    return () => window.clearTimeout(timer)
  }, [loadProducts])

  useEffect(() => {
    function close(event: MouseEvent) {
      if (pickerRef.current && !pickerRef.current.contains(event.target as Node)) setPickerOpen(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [])

  function invalidatePreview() {
    setPreview(null)
    setQuantities({})
    setConflicts([])
    setReason('')
    setMessage('')
  }

  function toggleProduct(product: Product) {
    setSelectedProducts(current => current.some(item => item.id === product.id)
      ? current.filter(item => item.id !== product.id)
      : [...current, product].sort((left, right) => left.code.localeCompare(right.code)))
    invalidatePreview()
  }

  function removeProduct(productId: string) {
    setSelectedProducts(current => current.filter(item => item.id !== productId))
    invalidatePreview()
  }

  async function showTable(event: FormEvent) {
    event.preventDefault()
    setError('')
    setMessage('')
    setConflicts([])
    const start = normalizeJalaliMonthDate(startDate)
    const end = normalizeJalaliMonthDate(endDate)
    if (!name.trim()) { setError(t('forecastActual.planNameRequired')); return }
    if (!start || !end) { setError(t('forecastActual.planDateInvalid')); return }
    if (Number(start.slice(0, 6)) > Number(end.slice(0, 6))) { setError(t('forecastActual.planRangeInvalid')); return }
    if (!selectedProducts.length) { setError(t('forecastActual.noProductsSelected')); return }
    setLoadingPreview(true)
    try {
      const response = await fetch(`${apiRoot}/planning/forecast-plans/preview`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: name.trim(),
          startJalaliDate: start,
          endJalaliDate: end,
          productIds: selectedProducts.map(product => product.id),
        }),
      })
      if (!response.ok) throw new Error(await problemText(response))
      const result = await response.json() as PlanPreview
      setPreview(result)
      setSelectedProducts(result.products)
      setQuantities({})
    } catch (caught) {
      setPreview(null)
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setLoadingPreview(false)
    }
  }

  function changeQuantity(periodCode: string, productId: string, value: string) {
    if (value && (!/^\d*(\.\d{0,6})?$/.test(value) || Number(value) < 0)) return
    setQuantities(current => ({ ...current, [forecastMatrixKey(periodCode, productId)]: value }))
    setConflicts([])
    setMessage('')
  }

  const cells = useMemo(() => new Map(preview?.cells.map(cell =>
    [forecastMatrixKey(cell.periodCode, cell.productId), cell]) ?? []), [preview])

  function entries() {
    if (!preview) return null
    const result = []
    for (const month of preview.months) {
      for (const product of preview.products) {
        const key = forecastMatrixKey(month.periodCode, product.id)
        const value = quantities[key]?.trim() ?? ''
        if (value === '') return null
        const current = cells.get(key)
        result.push({
          productId: product.id,
          periodCode: month.periodCode,
          forecastQty: Number(value),
          expectedVersion: current?.seriesVersion ?? null,
        })
      }
    }
    return result
  }

  async function save(confirmDifferences = false) {
    if (!preview) return
    const values = entries()
    if (!values) { setError(t('forecastActual.fillAllForecasts')); return }
    if (confirmDifferences && !reason.trim()) { setError(t('forecastActual.correctionReasonRequired')); return }
    setSaving(true)
    setError('')
    setMessage('')
    try {
      const response = await fetch(`${apiRoot}/planning/forecast-plans`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: preview.name,
          startJalaliDate: preview.startJalaliDate,
          endJalaliDate: preview.endJalaliDate,
          reason: reason.trim() || null,
          confirmDifferences,
          entries: values,
        }),
      })
      if (response.status === 409) {
        const body = await response.json() as ProblemBody
        if (body.code === 'FORECAST_VALUE_CONFLICT' && Array.isArray(body.details)) {
          setConflicts(body.details)
          setPreview(current => current ? {
            ...current,
            cells: current.cells.map(cell => {
              const conflict = body.details?.find(item => item.productId === cell.productId && item.periodCode === cell.periodCode)
              return conflict ? { ...cell, currentForecastQty: conflict.existingQty, seriesVersion: conflict.seriesVersion } : cell
            }),
          } : current)
          return
        }
        throw new Error(problemBodyText(body, response.status))
      }
      if (!response.ok) throw new Error(await problemText(response))
      const result = await response.json() as SaveResult
      setMessage(t('forecastActual.planSaved', {
        created: result.createdCount,
        revised: result.revisedCount,
        unchanged: result.unchangedCount,
      }))
      setConflicts([])
      setReason('')
      setPreview(null)
      setQuantities({})
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t('common.error'))
    } finally {
      setSaving(false)
    }
  }

  const pickerPages = Math.max(1, Math.ceil(productOptions.totalCount / productPageSize))
  return <main className="app-shell forecast-plan-page">
    <PageHeader title={t('forecastActual.forecastTitle')} subtitle={t('forecastActual.namedPlanSubtitle')} />
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}

    <form onSubmit={event => void showTable(event)}>
      <Section title={t('forecastActual.planDefinition')} description={t('forecastActual.planDefinitionHint')}>
        <div className="forecast-plan-form">
          <FormField label={t('forecastActual.planName')} required>
            <input value={name} maxLength={200} required onChange={event => { setName(event.target.value); invalidatePreview() }} />
          </FormField>
          <FormField label={t('forecastActual.planStartDate')} required hint={t('forecastActual.monthDateHint')}>
            <input className="ui-technical" dir="ltr" inputMode="numeric" maxLength={8} placeholder="14050101"
              value={startDate} onChange={event => { setStartDate(event.target.value.replace(/\D/g, '').slice(0, 8)); invalidatePreview() }} />
          </FormField>
          <FormField label={t('forecastActual.planEndDate')} required hint={t('forecastActual.monthDateHint')}>
            <input className="ui-technical" dir="ltr" inputMode="numeric" maxLength={8} placeholder="14060601"
              value={endDate} onChange={event => { setEndDate(event.target.value.replace(/\D/g, '').slice(0, 8)); invalidatePreview() }} />
          </FormField>
          <FormField label={t('forecastActual.activeProducts')} required>
            <div className="forecast-product-select" ref={pickerRef}>
              <button type="button" className="forecast-product-trigger" aria-haspopup="listbox" aria-expanded={pickerOpen}
                onClick={() => setPickerOpen(value => !value)}>
                <span>{selectedProducts.length
                  ? t('forecastActual.selectedProductCount', { count: selectedProducts.length })
                  : t('forecastActual.selectProducts')}</span>
                <ChevronDown aria-hidden="true" />
              </button>
              {pickerOpen && <div className="forecast-product-popover">
                <label className="forecast-product-search">
                  <Search aria-hidden="true" />
                  <input autoFocus value={productSearch} placeholder={t('products.searchPlaceholder')}
                    onChange={event => { setProductSearch(event.target.value); setProductPage(1) }} />
                </label>
                <div className="forecast-product-options" role="listbox" aria-multiselectable="true">
                  {loadingProducts ? <LoadingState compact label={t('common.loading')} />
                    : productOptions.items.length === 0 ? <EmptyState compact filtered={Boolean(productSearch)}
                      title={t('common.noResultsTitle')} description={t('common.noResultsDescription')} />
                      : productOptions.items.map(product => {
                        const checked = selectedProducts.some(item => item.id === product.id)
                        return <button type="button" role="option" aria-selected={checked} key={product.id}
                          className={`forecast-product-option${checked ? ' is-selected' : ''}`}
                          onClick={() => toggleProduct(product)}>
                          <span className="forecast-product-check">{checked && <Check aria-hidden="true" />}</span>
                          <span><strong>{display(product)}</strong><small className="ui-technical">{product.code}</small></span>
                        </button>
                      })}
                </div>
                <Pagination page={productPage} pageSize={productPageSize} totalCount={productOptions.totalCount}
                  onPageChange={setProductPage} previousLabel={t('common.previous')} nextLabel={t('common.next')}
                  summary={t('common.page', { page: productPage, pages: pickerPages, count: productOptions.totalCount })} />
              </div>}
            </div>
          </FormField>
          <div className="forecast-selected-products" aria-live="polite">
            {selectedProducts.map(product => <span key={product.id} className="forecast-product-chip">
              <span>{display(product)}</span><small className="ui-technical">{product.code}</small>
              <button type="button" aria-label={`${t('common.cancel')} ${display(product)}`} onClick={() => removeProduct(product.id)}><X aria-hidden="true" /></button>
            </span>)}
          </div>
          <div className="forecast-plan-build-action">
            <button className="button button--primary" disabled={loadingPreview}>{loadingPreview ? t('common.loading') : t('forecastActual.showTable')}</button>
          </div>
        </div>
      </Section>
    </form>

    {loadingPreview && <LoadingState label={t('common.loading')} />}
    {preview && <>
      <Section title={t('forecastActual.monthlyMatrix')} description={t('forecastActual.atomicPlanHint')}
        actions={<StatusBadge tone="information" text={t('forecastActual.monthCount', { count: preview.months.length })} />}>
        <div className="forecast-plan-summary">
          <span><strong>{t('forecastActual.planName')}:</strong> {preview.name}</span>
          <span><strong>{t('forecastActual.planStartDate')}:</strong> <bdi>{preview.startJalaliDate}</bdi> · {formatBusinessDate(preview.startDate, language)}</span>
          <span><strong>{t('forecastActual.planEndDate')}:</strong> <bdi>{preview.endJalaliDate}</bdi> · {formatBusinessDate(preview.endDate, language)}</span>
        </div>
        <div className="forecast-matrix-scroll">
          <table className="forecast-matrix">
            <thead><tr><th scope="col">{t('forecastActual.month')}</th><th scope="col">{t('forecastActual.periodStart')} / {t('forecastActual.periodEnd')}</th>
              {preview.products.map(product => <th scope="col" key={product.id}><span>{display(product)}</span><small className="ui-technical">{product.code}</small></th>)}</tr></thead>
            <tbody>{preview.months.map(month => <tr key={month.periodCode}>
              <th scope="row" className="ui-technical">{month.periodCode}</th>
              <td><span>{formatBusinessDate(month.startDate, language)}</span><small>{formatBusinessDate(month.endDate, language)}</small></td>
              {preview.products.map(product => {
                const key = forecastMatrixKey(month.periodCode, product.id)
                const current = cells.get(key)
                return <td key={product.id}>
                  <input className="ui-technical forecast-matrix-input" dir="ltr" inputMode="decimal"
                    aria-label={`${t('forecastActual.forecastQty')} ${display(product)} ${month.periodCode}`}
                    value={quantities[key] ?? ''} placeholder={t('forecastActual.forecastQty')}
                    onChange={event => changeQuantity(month.periodCode, product.id, event.target.value)} />
                  <small>{current?.currentForecastQty == null
                    ? t('forecastActual.noPreviousForecast')
                    : t('forecastActual.previousForecastValue', { value: current.currentForecastQty })}</small>
                </td>
              })}
            </tr>)}</tbody>
          </table>
        </div>
        <div className="forecast-plan-savebar">
          <span>{t('forecastActual.completedForecasts', { count: Object.values(quantities).filter(value => value.trim() !== '').length, total: preview.months.length * preview.products.length })}</span>
          <button type="button" className="button button--primary" disabled={!canManage || saving} onClick={() => void save(false)}>{saving ? t('common.loading') : t('forecastActual.savePlan')}</button>
        </div>
      </Section>

      {conflicts.length > 0 && <Section title={t('forecastActual.conflictNoticeTitle')} description={t('forecastActual.conflictNoticeDetail')}>
        <MessageStrip tone="warning">{t('forecastActual.conflictNoticeDetail')}</MessageStrip>
        <div className="ui-table-wrap"><table className="ui-data-table"><thead><tr>
          <th scope="col">{t('forecastActual.product')}</th><th scope="col">{t('forecastActual.month')}</th><th scope="col">{t('forecastActual.existingValue')}</th><th scope="col">{t('forecastActual.proposedValue')}</th>
        </tr></thead><tbody>{conflicts.map(conflict => <tr key={`${conflict.productId}:${conflict.periodCode}`}>
          <td>{display({ nameFa: conflict.productNameFa, nameEn: conflict.productNameEn })}<small className="ui-technical forecast-conflict-code">{conflict.productCode}</small></td>
          <td className="ui-technical">{conflict.periodCode}</td><td className="ui-number">{formatOperationalNumber(conflict.existingQty, language)}</td><td className="ui-number">{formatOperationalNumber(conflict.proposedQty, language)}</td>
        </tr>)}</tbody></table></div>
        <div className="forecast-conflict-action">
          <FormField label={t('forecastActual.reason')} required hint={t('forecastActual.batchReasonHint')}>
            <textarea value={reason} maxLength={1000} onChange={event => setReason(event.target.value)} />
          </FormField>
          <button type="button" className="button button--primary" disabled={!canManage || saving || !reason.trim()} onClick={() => void save(true)}>{saving ? t('common.loading') : t('forecastActual.confirmCorrection')}</button>
        </div>
      </Section>}
    </>}
  </main>
}

async function problemText(response: Response) {
  const body = await response.json().catch(() => null) as ProblemBody | null
  return problemBodyText(body, response.status)
}

function problemBodyText(body: ProblemBody | null, status: number) {
  return [body?.code, body?.detail ?? body?.title, ...Object.values(body?.errors ?? {}).flat()]
    .filter(Boolean).join(' — ') || `HTTP ${status}`
}
