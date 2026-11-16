import { FormEvent, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileSearch, RefreshCw, RotateCcw } from 'lucide-react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { apiRoot } from './apiClient'
import { useAuth } from './AuthContext'
import { DualDate, DualDateFilter, SectionFailure } from './CashFlowPage'
import { cashFlowFilterPath, cashFlowRequestError, formatCashAmount, formatCashQuantity, formatCashUnitPrice, positiveCashFlowPage, readCashFlowApiError, tehranTodayIso, type CashFlowRequestError } from './cashFlowUi'
import {
  buildPurchasePriceHistoryQuery,
  preservePagedSelection,
  type PurchasePriceHistoryFilterValues,
  type PurchasePriceHistoryItemOptionPage,
  type PurchasePriceHistoryPage,
  type PurchasePriceHistorySupplierOptionPage,
} from './cashFlowMaterialCostingUi'
import { normalizeLanguage } from './i18n'
import { formatBusinessDateTime } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { DataTable, FilterBar, FormField, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'

const api = `${apiRoot}/v1/cash-flow`
const pageSize = 25

export default function CashFlowPurchasePriceHistoryPage() {
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params] = useSearchParams()
  const navigate = useNavigate(); const location = useLocation(); const { has } = useAuth()
  const page = positiveCashFlowPage(params.get('page'))
  const filters = useMemo(() => ({
    search: params.get('search') ?? '', itemId: params.get('itemId') ?? '', supplierId: params.get('supplierId') ?? '',
    currencyCode: params.get('currencyCode') ?? '', purchaseOrderStatus: params.get('purchaseOrderStatus') ?? '',
    fromOrderDate: params.get('fromOrderDate') ?? '', toOrderDate: params.get('toOrderDate') ?? '',
    valuationFxRateSetId: params.get('valuationFxRateSetId') ?? '',
    valuationAsOfDate: params.get('valuationAsOfDate') ?? '',
  }), [params])
  const [draft, setDraft] = useState(filters)
  const [data, setData] = useState<PurchasePriceHistoryPage>()
  const [items, setItems] = useState<PurchasePriceHistoryItemOptionPage>()
  const [suppliers, setSuppliers] = useState<PurchasePriceHistorySupplierOptionPage>()
  const [itemOptionSearch, setItemOptionSearch] = useState('')
  const [itemOptionPage, setItemOptionPage] = useState(1)
  const [supplierOptionSearch, setSupplierOptionSearch] = useState('')
  const [supplierOptionPage, setSupplierOptionPage] = useState(1)
  const [values, setValues] = useState<PurchasePriceHistoryFilterValues>({ currencyCodes: [], purchaseOrderStatuses: [] })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [valuesError, setValuesError] = useState<CashFlowRequestError | null>(null)
  const [itemsError, setItemsError] = useState<CashFlowRequestError | null>(null)
  const [suppliersError, setSuppliersError] = useState<CashFlowRequestError | null>(null)
  const [retry, setRetry] = useState(0)
  const usdRequested = Boolean(filters.valuationFxRateSetId || filters.valuationAsOfDate)

  useEffect(() => { const timer = window.setTimeout(() => setDraft(filters), 0); return () => window.clearTimeout(timer) }, [filters])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    void getJson<PurchasePriceHistoryFilterValues>(`${api}/purchase-price-history/filter-options/values`, controller.signal)
      .then(filterValues => { if (active) { setValues(filterValues); setValuesError(null) } })
      .catch(reason => { if (active && !isAbort(reason)) setValuesError(cashFlowRequestError(reason)) })
    return () => { active = false; controller.abort() }
  }, [retry])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    const query = new URLSearchParams({ page: String(itemOptionPage), pageSize: '20' }); if (itemOptionSearch.trim()) query.set('search', itemOptionSearch.trim())
    void getJson<PurchasePriceHistoryItemOptionPage>(`${api}/purchase-price-history/filter-options/items?${query}`, controller.signal)
      .then(result => { if (active) { setItems(result); setItemsError(null) } }).catch(reason => { if (active && !isAbort(reason)) setItemsError(cashFlowRequestError(reason)) })
    return () => { active = false; controller.abort() }
  }, [itemOptionPage, itemOptionSearch, retry])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    const query = new URLSearchParams({ page: String(supplierOptionPage), pageSize: '20' }); if (supplierOptionSearch.trim()) query.set('search', supplierOptionSearch.trim())
    void getJson<PurchasePriceHistorySupplierOptionPage>(`${api}/purchase-price-history/filter-options/suppliers?${query}`, controller.signal)
      .then(result => { if (active) { setSuppliers(result); setSuppliersError(null) } }).catch(reason => { if (active && !isAbort(reason)) setSuppliersError(cashFlowRequestError(reason)) })
    return () => { active = false; controller.abort() }
  }, [retry, supplierOptionPage, supplierOptionSearch])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      const query = buildPurchasePriceHistoryQuery({ page, pageSize, ...filters })
      void getJson<PurchasePriceHistoryPage>(`${api}/purchase-price-history?${query}`, controller.signal)
        .then(value => { if (active) setData(value) })
        .catch(reason => { if (active && !isAbort(reason)) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [filters, page, retry])

  function apply(next: typeof draft, nextPage = 1) {
    navigate(cashFlowFilterPath(location.pathname, { ...next, page: nextPage }))
  }
  function submit(event: FormEvent) { event.preventDefault(); apply(draft) }
  function set<K extends keyof typeof draft>(key: K, value: typeof draft[K]) { setDraft(current => ({ ...current, [key]: value })) }
  function setUsd(requested: boolean) {
    setDraft(current => requested
      ? current.valuationFxRateSetId || current.valuationAsOfDate ? current : { ...current, valuationAsOfDate: tehranTodayIso() }
      : { ...current, valuationFxRateSetId: '', valuationAsOfDate: '' })
  }

  return <main className="cash-flow-page cash-purchase-history">
    <PageHeader eyebrow={t('cashFlow.purchasePriceHistory.eyebrow')} title={t('cashFlow.purchasePriceHistory.title')} subtitle={t('cashFlow.purchasePriceHistory.subtitle')} />
    <MessageStrip>{t('cashFlow.purchasePriceHistory.boundary')}</MessageStrip>
    <form onSubmit={submit}>
      <FilterBar label={t('common.filterBar')}>
        <FormField label={t('common.search')}><input value={draft.search} placeholder={t('cashFlow.purchasePriceHistory.searchPlaceholder')} onChange={event => set('search', event.target.value)} /></FormField>
        <PagedFilterLookup label={t('cashFlow.purchasePriceHistory.item')} search={itemOptionSearch} page={itemOptionPage} totalCount={items?.totalCount ?? 0} selected={draft.itemId} onSearch={value => { setItemOptionSearch(value); setItemOptionPage(1) }} onPage={setItemOptionPage} onSelected={value => set('itemId', value)} options={items?.items.map(item => ({ id: item.itemId, label: `${localizedName({ nameFa: item.itemNameFa, nameEn: item.itemNameEn }, language)} — ${item.itemCode}` })) ?? []} />
        <PagedFilterLookup label={t('cashFlow.purchasePriceHistory.supplier')} search={supplierOptionSearch} page={supplierOptionPage} totalCount={suppliers?.totalCount ?? 0} selected={draft.supplierId} onSearch={value => { setSupplierOptionSearch(value); setSupplierOptionPage(1) }} onPage={setSupplierOptionPage} onSelected={value => set('supplierId', value)} options={suppliers?.items.map(supplier => ({ id: supplier.supplierId, label: `${supplier.supplierName} — ${supplier.supplierNumber}` })) ?? []} />
        <FormField label={t('cashFlow.purchasePriceHistory.currency')}><select value={draft.currencyCode} onChange={event => set('currencyCode', event.target.value)}><option value="">{t('common.all')}</option>{values.currencyCodes.map(code => <option key={code} value={code}>{code}</option>)}</select></FormField>
        <FormField label={t('cashFlow.purchasePriceHistory.orderStatus')}><select value={draft.purchaseOrderStatus} onChange={event => set('purchaseOrderStatus', event.target.value)}><option value="">{t('common.all')}</option>{values.purchaseOrderStatuses.map(status => <option key={status} value={status}>{t(`cashFlow.purchasePriceHistory.status.${status}`, { defaultValue: status })}</option>)}</select></FormField>
        <DualDateFilter allowEmpty label={t('common.from')} value={draft.fromOrderDate} language={language} surface="purchase-price-history" onChange={value => set('fromOrderDate', value)} />
        <DualDateFilter allowEmpty label={t('common.to')} value={draft.toOrderDate} language={language} surface="purchase-price-history" onChange={value => set('toOrderDate', value)} />
        <label className="cash-purchase-history__usd-toggle"><input type="checkbox" checked={Boolean(draft.valuationFxRateSetId || draft.valuationAsOfDate)} onChange={event => setUsd(event.target.checked)} /><span>{t('cashFlow.purchasePriceHistory.showUsd')}</span></label>
        {!draft.valuationFxRateSetId && draft.valuationAsOfDate && <DualDateFilter label={t('cashFlow.purchasePriceHistory.valuationDate')} value={draft.valuationAsOfDate} language={language} surface="purchase-price-history" onChange={value => set('valuationAsOfDate', value)} />}
        <button className="button button--primary" type="submit"><FileSearch aria-hidden="true" />{t('common.search')}</button>
        <button className="button button--quiet" type="button" onClick={() => { const empty = { search: '', itemId: '', supplierId: '', currencyCode: '', purchaseOrderStatus: '', fromOrderDate: '', toOrderDate: '', valuationFxRateSetId: '', valuationAsOfDate: '' }; setDraft(empty); apply(empty) }}><RotateCcw aria-hidden="true" />{t('cashFlow.resetFilters')}</button>
        <button className="button button--quiet" type="button" onClick={() => setRetry(value => value + 1)}><RefreshCw aria-hidden="true" />{t('common.refresh')}</button>
      </FilterBar>
    </form>
    {(valuesError || itemsError || suppliersError) && <SectionFailure error={valuesError ?? itemsError ?? suppliersError!} compact onRetry={() => setRetry(value => value + 1)} />}
    {error && <SectionFailure error={error} onRetry={() => setRetry(value => value + 1)} />}
    {!error && <>
      <UsdValuationNotice data={data} language={language} canViewRates={has('cash_flow.reporting_rate.view')} />
      <Section title={t('cashFlow.purchasePriceHistory.listTitle')} description={t('cashFlow.purchasePriceHistory.oneRowSemantics')}><>
        <TableToolbar title={t('cashFlow.purchasePriceHistory.lines')} count={data && t('common.count', { count: data.totalCount })} />
        <DataTable headers={[t('cashFlow.purchasePriceHistory.order'), t('cashFlow.purchasePriceHistory.item'), t('cashFlow.purchasePriceHistory.supplier'), t('cashFlow.purchasePriceHistory.originalPrice'), t('cashFlow.purchasePriceHistory.quantityAmount'), t('cashFlow.purchasePriceHistory.receipts'), ...(usdRequested ? [t('cashFlow.purchasePriceHistory.usdValuation')] : [])]}
          loading={loading} empty={!data?.items.length} filtered={Object.values(filters).some(Boolean)} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.purchasePriceHistory.emptyTitle')} emptyDescription={t('cashFlow.purchasePriceHistory.emptyHint')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
          <>{data?.items.map(row => <tr key={row.purchaseOrderLineId}>
            <td>{has('procurement.purchase_order.view') ? <Link to={`/mrp/purchase-orders/${row.purchaseOrderId}`}><strong><bdi>{row.purchaseOrderNumber}</bdi></strong></Link> : <strong><bdi>{row.purchaseOrderNumber}</bdi></strong>}<small>{t('cashFlow.purchasePriceHistory.lineNumber', { number: row.lineNumber })} · <DualDate value={row.orderedDate} language={language} /></small><StatusBadge text={t(`cashFlow.purchasePriceHistory.status.${row.purchaseOrderStatus}`, { defaultValue: row.purchaseOrderStatus })} tone="neutral" /></td>
            <td><strong>{localizedName({ nameFa: row.itemNameFa, nameEn: row.itemNameEn }, language)}</strong><small><bdi>{row.itemCode}</bdi></small></td>
            <td><strong>{row.supplierName}</strong><small><bdi>{row.supplierNumber}</bdi></small></td>
            <td className="ui-number"><strong>{formatCashUnitPrice(row.originalUnitPrice, row.originalCurrencyDecimalPrecision, language)}</strong><small dir="ltr">{row.originalCurrencyCode} / {row.priceBaseUnitCode}</small></td>
            <td className="ui-number"><strong>{formatCashQuantity(row.orderedQuantity, language)} {row.priceBaseUnitCode}</strong><small dir="ltr">{formatCashAmount(row.originalOrderAmount, row.originalCurrencyDecimalPrecision, language)} {row.originalCurrencyCode}</small></td>
            <td><strong>{t('cashFlow.purchasePriceHistory.receiptCount', { count: row.receipts.postedReceiptCount })}</strong><small>{formatCashQuantity(row.receipts.postedQuantity, language)} {row.receipts.quantityUnitCode}</small><small>{row.receipts.firstPostedReceiptDate ? <><DualDate value={row.receipts.firstPostedReceiptDate} language={language} />{row.receipts.lastPostedReceiptDate && row.receipts.lastPostedReceiptDate !== row.receipts.firstPostedReceiptDate ? <> — <DualDate value={row.receipts.lastPostedReceiptDate} language={language} /></> : null}</> : t('cashFlow.purchasePriceHistory.noReceipt')}</small></td>
            {usdRequested && <td className="ui-number">{row.valuationOrderAmountUsd == null ? <StatusBadge text={t('cashFlow.purchasePriceHistory.usdUnavailable')} tone="warning" /> : <><strong>{formatCashAmount(row.valuationOrderAmountUsd, 2, language)}</strong><small dir="ltr">USD · {t('cashFlow.purchasePriceHistory.reportingOnly')}</small>{row.valuationUnitPriceUsd && <small dir="ltr">{formatCashUnitPrice(row.valuationUnitPriceUsd, 2, language)} USD / {row.priceBaseUnitCode}</small>}</>}</td>}
          </tr>)}</>
        </DataTable>
        {data && <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={next => apply(filters, next)} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(data.totalCount / pageSize)), count: data.totalCount })} />}
      </></Section>
      {data?.generatedAtUtc && <p className="cash-purchase-history__freshness">{t('cashFlow.purchasePriceHistory.generatedAt')}: <bdi>{formatBusinessDateTime(data.generatedAtUtc, language)}</bdi></p>}
    </>}
  </main>
}

function UsdValuationNotice({ data, language, canViewRates }: { data?: PurchasePriceHistoryPage; language: string; canViewRates: boolean }) {
  const { t } = useTranslation(); const context = data?.usdValuationContext
  if (!context?.requested) return null
  const complete = context.status === 'COMPLETE'
  return <MessageStrip tone={complete ? 'positive' : 'warning'}><strong>{complete ? t('cashFlow.purchasePriceHistory.fxComplete') : t('cashFlow.purchasePriceHistory.fxIncomplete')}</strong><p>{t('cashFlow.purchasePriceHistory.fxPurpose')}</p>{context.valuationAsOfDate && <p>{t('cashFlow.purchasePriceHistory.valuationDate')}: <DualDate value={context.valuationAsOfDate} language={language} /></p>}{context.rateSetEffectiveDate && <p>{t('cashFlow.purchasePriceHistory.selectedRateSet')}: <DualDate value={context.rateSetEffectiveDate} language={language} /> · {t('cashFlow.materialCostBooks.revision')} {context.rateSetRevision ?? '—'}</p>}{context.missingCurrencyCodes.length > 0 && <p>{t('cashFlow.purchasePriceHistory.missingCurrencies')}: <bdi dir="ltr">{context.missingCurrencyCodes.join(' · ')}</bdi></p>}{canViewRates && <Link to={context.rateSetId ? `/cash-flow/reporting-rates/${context.rateSetId}` : '/cash-flow/reporting-rates'}>{t('cashFlow.openReportingRates')}</Link>}</MessageStrip>
}

function PagedFilterLookup({ label, search, page, totalCount, selected, options, onSearch, onPage, onSelected }: { label: string; search: string; page: number; totalCount: number; selected: string; options: { id: string; label: string }[]; onSearch: (value: string) => void; onPage: (value: number) => void; onSelected: (value: string) => void }) {
  const { t } = useTranslation(); const pages = Math.max(1, Math.ceil(totalCount / 20))
  const visibleOptions = preservePagedSelection(selected, options,
    { id: selected, label: t('cashFlow.purchasePriceHistory.selectedOutsidePage') })
  return <div className="cash-purchase-history__lookup"><FormField label={label}><input value={search} placeholder={t('common.search')} onChange={event => onSearch(event.target.value)} /></FormField><select aria-label={label} value={selected} onChange={event => onSelected(event.target.value)}><option value="">{t('common.all')}</option>{visibleOptions.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</select><div><button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label={t('common.previous')}>‹</button><span>{page}/{pages}</span><button type="button" disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label={t('common.next')}>›</button></div></div>
}

function isAbort(reason: unknown) { return reason instanceof Error && reason.name === 'AbortError' }
async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> { const response = await fetch(url, { signal, cache: 'no-store' }); if (!response.ok) throw await readCashFlowApiError(response); return response.json() as Promise<T> }
