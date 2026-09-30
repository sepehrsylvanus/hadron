import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import { formatBusinessDate, formatBusinessDateTime } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber as formatQuantity } from './i18n/numberFormatting'
import { createMaterialPlanningApiError, formatMaterialPlanningProblem, MaterialPlanningApiError } from './materialPlanningProblem'
import { chooseInitialRunId, readRememberedRunId, rememberRunId, sourceNavigation } from './materialPlanningSupport'
import { DataTable, Definition, DefinitionGrid, EmptyState, FilterBar, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar, type StatusTone } from './ui'
import { useOptionalAuth } from './AuthContext'
import { PlannerInboxPanel, WhatIfPanel } from './MaterialPlanningCollaboration'

const api = '/api/material-planning'
const pageSize = 25
const runPreferenceStorage = () => typeof window === 'undefined' ? null : window.localStorage

type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
type Run = { id: string; runNumber: string; asOfDate: string; horizonEndDate: string; engineVersion: string; status: string; completeness: string; planRole: string; completedAtUtc: string; exceptionCount: number }
type RunHeader = Run & { startedAtUtc: string; snapshotCapturedAtUtc?: string; isLatest: boolean; materialCount: number }
type Item = { id: string; code: string; nameFa: string; nameEn?: string; itemType: string; unitCode: string; unitNameFa: string; unitNameEn?: string; categoryId?: string; categoryCode?: string; categoryNameFa?: string; categoryNameEn?: string; supplyMethod?: string }
type TraceItem = { id: string; code: string; nameFa: string; nameEn?: string; itemType: string; unitCode: string; unitNameFa: string; unitNameEn?: string }
type Facets = { categories: Array<{ id: string; code: string; nameFa: string; nameEn?: string }>; supplyMethods: string[] }
type Summary = { item: Item; startingOnHand: number; totalGrossDemand: number; totalFirmIncoming: number; totalNetRequirement: number; overdueFirmQty: number; missingEtaFirmQty: number; stockoutDate?: string; safetyBreachDate?: string; stockCoverageState?: string; stockCoverageDate?: string; stockCoverageDays?: number; firmCoverageState?: string; firmCoverageDate?: string; firmCoverageDays?: number; completeness: string; riskBand: string; riskReason: string; exceptionCount: number; primaryExceptionCode?: string }
type ProductRow = { product: Item; plannedProductionQty: number; materialCount: number; attentionMaterialCount: number; stockCoverageState?: string; stockCoverageDate?: string; stockCoverageDays?: number; firmCoverageState?: string; firmCoverageDate?: string; firmCoverageDays?: number; firstConstraintMaterial?: Item; firstConstraintDate?: string; riskBand: string; completeness: string; primaryExceptionCode?: string }
type RefreshStatus = { status: string; latestRunId?: string; latestRunNumber?: string; latestAsOfDate?: string; lastUpdatedAtUtc?: string; lastInputChangeAtUtc?: string; pendingChangeCount: number; errorCode?: string; errorMessage?: string; certifiedRunId?: string; certifiedRunNumber?: string; certifiedBusinessDate?: string; certifiedAtUtc?: string; certifiedResultHash?: string }
type Bucket = { startDate: string; endDate: string; grossDemand: number; firmIncoming: number; netRequirement: number; firmProjected: number; planningBalance: number; stockOnlyProjected: number; coverageDays?: number; zone: string; completeness: string; includesStockoutEvent: boolean; includesSafetyBreachEvent: boolean }
type Timeline = { item: Item; grain: string; safetyStockQty: number; stockCoverageState?: string; stockCoverageDate?: string; stockCoverageDays?: number; firmCoverageState?: string; firmCoverageDate?: string; firmCoverageDays?: number; usedByProducts: Array<{ id: string; code: string; nameFa: string; nameEn?: string }>; buckets: Bucket[] }
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
type ErrorArea = 'runs' | 'refresh' | 'header' | 'facets' | 'products' | 'topRisks' |
  'productMaterials' | 'summary' | 'timeline' | 'trace' | 'exceptions' | 'action'

export default function MaterialPlanningPage() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const [params, setParams] = useSearchParams()
  const auth = useOptionalAuth()
  const view = params.get('view') ?? 'dashboard'
  const canViewInbox = auth?.has('planning.work_item.view') ?? true
  const canManageInbox = auth?.has('planning.work_item.manage') ?? true
  const canAcknowledgeInbox = auth?.has('planning.work_item.acknowledge') ?? true
  const canViewWhatIf = auth?.has('planning.what_if.view') ?? true
  const canManageWhatIf = auth?.has('planning.what_if.manage') ?? true
  const runId = params.get('runId') ?? ''
  const page = positive(params.get('page'), 1)
  const productPage = positive(params.get('productPage'), 1)
  const productMaterialPage = positive(params.get('productMaterialPage'), 1)
  const runPage = positive(params.get('runPage'), 1)
  const exceptionPage = positive(params.get('exceptionPage'), 1)
  const search = params.get('search') ?? ''
  const productSearch = params.get('productSearch') ?? ''
  const productId = params.get('productId') ?? ''
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
  const [products, setProducts] = useState<Page<ProductRow> | null>(null)
  const [productMaterials, setProductMaterials] = useState<Page<Summary> | null>(null)
  const [topRisks, setTopRisks] = useState<Summary[]>([])
  const [refreshStatus, setRefreshStatus] = useState<RefreshStatus | null>(null)
  const [timeline, setTimeline] = useState<Timeline | null>(null)
  const [trace, setTrace] = useState<Trace | null>(null)
  const [exceptions, setExceptions] = useState<Page<ExceptionRow> | null>(null)
  const [loading, setLoading] = useState(true)
  const [errors, setErrors] = useState<Partial<Record<ErrorArea, string>>>({})
  const [runContextUnavailable, setRunContextUnavailable] = useState(false)
  const [showAdvancedFilters, setShowAdvancedFilters] = useState(false)
  const patch = useCallback((values: Record<string, string | null>) => {
    setParams(current => {
      const next = new URLSearchParams(current)
      Object.entries(values).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key))
      return next
    })
  }, [setParams])
  const reportError = useCallback((area: ErrorArea, caught: unknown) => {
    setErrors(current => ({ ...current, [area]: message(caught, t) }))
  }, [t])
  const clearError = useCallback((area: ErrorArea) => {
    setErrors(current => {
      if (!current[area]) return current
      const next = { ...current }
      delete next[area]
      return next
    })
  }, [])

  useEffect(() => {
    void get<Page<Run>>(`${api}/runs?page=${runPage}&pageSize=25`).then(body => {
      setRuns(body.items)
      setRunTotal(body.totalCount)
      clearError('runs')
      if (!runId && runPage === 1) {
        const selected = chooseInitialRunId('', readRememberedRunId(runPreferenceStorage()), body.items[0]?.id)
        if (selected.runId) patch({ runId: selected.runId })
      }
    }).catch(caught => reportError('runs', caught))
  }, [clearError, patch, reportError, runId, runPage])

  useEffect(() => {
    let active = true
    const load = async () => {
      try {
        const next = await get<RefreshStatus>(`${api}/refresh-status`)
        if (!active) return
        setRefreshStatus(next)
        clearError('refresh')
        if (next.latestRunId && next.latestRunId !== runId &&
          header?.id === runId && header.isLatest) {
          rememberRunId(runPreferenceStorage(), next.latestRunId)
          patch({ runId: next.latestRunId, page: null, productPage: null, productMaterialPage: null, productId: null, itemId: null })
        }
      } catch (caught) {
        if (active) reportError('refresh', caught)
      }
    }
    void load()
    const interval = window.setInterval(() => void load(), 5000)
    return () => { active = false; window.clearInterval(interval) }
  }, [clearError, header?.id, header?.isLatest, patch, reportError, runId])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (!runId || runContextUnavailable) { setProducts(null); setTopRisks([]); return }
      const productQuery = nonEmptyQuery({ page: productPage, pageSize, search: productSearch })
      void Promise.allSettled([
        get<Page<ProductRow>>(`${api}/runs/${runId}/products?${productQuery}`),
        get<Page<Summary>>(`${api}/runs/${runId}/summary?page=1&pageSize=10&sortBy=risk&sortDirection=asc`),
      ]).then(([productResult, riskResult]) => {
        if (productResult.status === 'fulfilled') { setProducts(productResult.value); clearError('products') }
        else reportError('products', productResult.reason)
        if (riskResult.status === 'fulfilled') { setTopRisks(riskResult.value.items); clearError('topRisks') }
        else reportError('topRisks', riskResult.reason)
      })
    }, 120)
    return () => window.clearTimeout(timer)
  }, [clearError, productPage, productSearch, reportError, runContextUnavailable, runId])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (!runId || !productId || runContextUnavailable) { setProductMaterials(null); return }
      void get<Page<Summary>>(`${api}/runs/${runId}/products/${productId}/materials?page=${productMaterialPage}&pageSize=${pageSize}`)
        .then(value => { setProductMaterials(value); clearError('productMaterials') })
        .catch(caught => reportError('productMaterials', caught))
    }, 0)
    return () => window.clearTimeout(timer)
  }, [clearError, productId, productMaterialPage, reportError, runContextUnavailable, runId])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void (async () => {
        if (!runId) { setLoading(false); return }
        setLoading(true)
        setRunContextUnavailable(false)
        const query = nonEmptyQuery({
          page, pageSize, search, itemType, categoryId,
          supplyMethod, riskBand, completeness, stockoutFrom, stockoutTo, sortBy, sortDirection,
        })
        if (hasExceptions) query.set('hasExceptions', hasExceptions)
        const exceptionQuery = nonEmptyQuery({ page: exceptionPage, pageSize: 25, severity: exceptionSeverity, code: exceptionCode, sourceModule: exceptionSource, date: exceptionDate })
        if (exceptionAffects) exceptionQuery.set('affectsDemand', exceptionAffects)
        if (itemId) exceptionQuery.set('itemId', itemId)
        const [headerResult, facetsResult, summaryResult, exceptionsResult] = await Promise.allSettled([
          get<RunHeader>(`${api}/runs/${runId}`),
          get<Facets>(`${api}/runs/${runId}/facets`),
          get<Page<Summary>>(`${api}/runs/${runId}/summary?${query}`),
          get<Page<ExceptionRow>>(`${api}/runs/${runId}/exceptions?${exceptionQuery}`),
        ])
        if (headerResult.status === 'fulfilled') {
          setHeader(headerResult.value)
          clearError('header')
          rememberRunId(runPreferenceStorage(), runId)
        } else {
          const caught = headerResult.reason
          if (caught instanceof MaterialPlanningApiError && caught.status === 404) {
            setHeader(null)
            setSummary(null)
            setExceptions(null)
            setRunContextUnavailable(true)
          } else reportError('header', caught)
        }
        if (facetsResult.status === 'fulfilled') { setFacets(facetsResult.value); clearError('facets') }
        else reportError('facets', facetsResult.reason)
        if (summaryResult.status === 'fulfilled') { setSummary(summaryResult.value); clearError('summary') }
        else reportError('summary', summaryResult.reason)
        if (exceptionsResult.status === 'fulfilled') { setExceptions(exceptionsResult.value); clearError('exceptions') }
        else reportError('exceptions', exceptionsResult.reason)
        setLoading(false)
      })()
    }, 0)
    return () => window.clearTimeout(timer)
  }, [categoryId, clearError, completeness, exceptionAffects, exceptionCode, exceptionDate, exceptionPage, exceptionSeverity, exceptionSource, hasExceptions, itemId, itemType, page, reportError, riskBand, runId, search, sortBy, sortDirection, stockoutFrom, stockoutTo, supplyMethod])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (!runId || !itemId || runContextUnavailable) { setTimeline(null); setTrace(null); return }
      void get<Timeline>(`${api}/runs/${runId}/items/${itemId}/timeline?grain=${grain}`)
        .then(value => { setTimeline(value); clearError('timeline') })
        .catch(caught => reportError('timeline', caught))
    }, 0)
    return () => window.clearTimeout(timer)
  }, [clearError, grain, itemId, reportError, runContextUnavailable, runId])

  const selectedProduct = products?.items.find(row => row.product.id === productId)
  const selected = productMaterials?.items.find(row => row.item.id === itemId) ??
    summary?.items.find(row => row.item.id === itemId)
  const title = t('materialPlanning.title')
  const filtered = Boolean(search || itemType || categoryId || supplyMethod || riskBand || completeness || hasExceptions || stockoutFrom || stockoutTo)
  const pageCount = Math.max(1, Math.ceil((summary?.totalCount ?? 0) / pageSize))
  const productPageCount = Math.max(1, Math.ceil((products?.totalCount ?? 0) / pageSize))
  const productMaterialPageCount = Math.max(1, Math.ceil((productMaterials?.totalCount ?? 0) / pageSize))
  const exceptionPages = Math.max(1, Math.ceil((exceptions?.totalCount ?? 0) / 25))
  const inspectTrace = async (date: string) => {
    if (!runId || !itemId) return
    try { setTrace(await get<Trace>(`${api}/runs/${runId}/items/${itemId}/trace?demandDate=${date}`)); clearError('trace') }
    catch (caught) { reportError('trace', caught) }
  }
  const selectRun = (nextRunId: string) => {
    rememberRunId(runPreferenceStorage(), nextRunId)
    setRunContextUnavailable(false)
    patch({ runId: nextRunId, page: null, productPage: null, productMaterialPage: null, productId: null, itemId: null })
  }

  const collaborationNavigation = <nav className="material-planning-views" aria-label={t('materialPlanning.collaboration.views')}>
    <button type="button" className={view === 'dashboard' ? 'is-active' : ''} onClick={() => patch({ view: null })}>{t('materialPlanning.collaboration.dashboard')}</button>
    {canViewInbox && <button type="button" className={view === 'inbox' ? 'is-active' : ''} onClick={() => patch({ view: 'inbox' })}>{t('materialPlanning.collaboration.inbox')}</button>}
    {canViewWhatIf && <button type="button" className={view === 'whatif' ? 'is-active' : ''} onClick={() => patch({ view: 'whatif' })}>{t('materialPlanning.collaboration.whatIf')}</button>}
  </nav>

  if (view === 'inbox' && canViewInbox) return <main className="material-planning-page">
    <PageHeader title={t('materialPlanning.collaboration.inboxTitle')} subtitle={t('materialPlanning.collaboration.inboxHint')} eyebrow={t('materialPlanning.title')} />
    {collaborationNavigation}<PlannerInboxPanel runId={runId} canManage={canManageInbox} canAcknowledge={canAcknowledgeInbox} />
  </main>
  if (view === 'whatif' && canViewWhatIf) return <main className="material-planning-page">
    <PageHeader title={t('materialPlanning.collaboration.whatIfTitle')} subtitle={t('materialPlanning.collaboration.whatIfHint')} eyebrow={t('materialPlanning.title')} />
    {collaborationNavigation}<WhatIfPanel runId={runId} canManage={canManageWhatIf} />
  </main>

  return <main className="material-planning-page">
    <PageHeader sticky title={title}
      subtitle={header ? `${t('materialPlanning.planVersion')}: ${header.runNumber} · ${t(`materialPlanning.planRoles.${header.planRole}`, { defaultValue: header.planRole })}` : t('materialPlanning.subtitle')}
      eyebrow={t('materialPlanning.snapshotOnly')}
      status={header && <StatusBadge text={t(`mrpRuns.status.${header.completeness}`, { defaultValue: header.completeness })} tone={header.completeness === 'INCOMPLETE' ? 'critical' : 'positive'} />} />
    {collaborationNavigation}
    <RequestProblem problem={errors.runs} />
    {runContextUnavailable && <MessageStrip tone="critical">{t('materialPlanning.runContextUnavailable')}</MessageStrip>}
    <RequestProblem problem={errors.refresh} />
    {refreshStatus && <RefreshBanner status={refreshStatus} language={language} t={t} />}
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('materialPlanning.planVersion')}><select value={runId} onChange={event => selectRun(event.target.value)}><option value="">{t('materialPlanning.noEligibleRun')}</option>{runContextUnavailable && runId && <option value={runId}>{t('materialPlanning.unavailableRun')}</option>}{header && !runs.some(run => run.id === header.id) && <option value={header.id}>{header.runNumber} — {formatBusinessDate(header.asOfDate, language)}</option>}{runs.map(run => <option value={run.id} key={run.id}>{t(`materialPlanning.planRoles.${run.planRole}`, { defaultValue: run.planRole })} — {formatBusinessDate(run.asOfDate, language)}</option>)}</select></FormField>
      <div className="ui-action-row"><button type="button" className="button button--quiet" disabled={runPage <= 1} onClick={() => patch({ runPage: String(runPage - 1) })}>{t('common.previous')}</button><span>{t('materialPlanning.runPage', { page: runPage, pages: Math.max(1, Math.ceil(runTotal / 25)) })}</span><button type="button" className="button button--quiet" disabled={runPage >= Math.ceil(runTotal / 25)} onClick={() => patch({ runPage: String(runPage + 1) })}>{t('common.next')}</button></div>
      <FormField label={t('common.search')}><input value={search} onChange={event => patch({ search: event.target.value, page: null })} placeholder={t('materialPlanning.searchPlaceholder')} /></FormField>
      <FormField label={t('materialPlanning.risk')}><select value={riskBand} onChange={event => patch({ riskBand: event.target.value, page: null })}><option value="">{t('common.all')}</option>{['INCOMPLETE','STOCKOUT','NO_FUTURE_DEMAND','BEYOND_HORIZON','CRITICAL','LOW','SAFE','EXCESS'].map(value => <option value={value} key={value}>{t(`materialPlanning.risks.${value}`)}</option>)}</select></FormField>
      <FormField label={t('materialPlanning.exceptionsOnly')}><select value={hasExceptions} onChange={event => patch({ hasExceptions: event.target.value, page: null })}><option value="">{t('common.all')}</option><option value="true">{t('common.yes')}</option><option value="false">{t('common.no')}</option></select></FormField>
      <button type="button" className="button button--quiet mrp-advanced-filter-toggle" aria-expanded={showAdvancedFilters} onClick={() => setShowAdvancedFilters(value => !value)}>{showAdvancedFilters ? t('materialPlanning.hideAdvancedFilters') : t('materialPlanning.showAdvancedFilters')}</button>
      {showAdvancedFilters && <div className="mrp-advanced-filters">
        <FormField label={t('materialPlanning.itemType')}><select value={itemType} onChange={event => patch({ itemType: event.target.value, page: null })}><option value="">{t('common.all')}</option><option value="PART">{t('materials.parts')}</option><option value="COMPONENT">{t('materials.components')}</option></select></FormField>
        <FormField label={t('common.category')}><select value={categoryId} onChange={event => patch({ categoryId: event.target.value, page: null })}><option value="">{t('common.all')}</option>{facets.categories.map(category => <option value={category.id} key={category.id}>{localizedName(category, language)} ({category.code})</option>)}</select></FormField>
        <FormField label={t('materials.supplyMethod')}><select value={supplyMethod} onChange={event => patch({ supplyMethod: event.target.value, page: null })}><option value="">{t('common.all')}</option>{facets.supplyMethods.map(method => <option value={method} key={method}>{t(`materials.supplyMethods.${method}`, { defaultValue: method })}</option>)}</select></FormField>
        <FormField label={t('mrpRuns.completeness')}><select value={completeness} onChange={event => patch({ completeness: event.target.value, page: null })}><option value="">{t('common.all')}</option>{['COMPLETE','COMPLETE_WITH_WARNINGS','INCOMPLETE'].map(value => <option value={value} key={value}>{t(`mrpRuns.status.${value}`)}</option>)}</select></FormField>
        <FormField label={t('materialPlanning.stockoutFrom')}><input type="date" dir="ltr" value={stockoutFrom} onChange={event => patch({ stockoutFrom: event.target.value, page: null })} /></FormField>
        <FormField label={t('materialPlanning.stockoutTo')}><input type="date" dir="ltr" value={stockoutTo} onChange={event => patch({ stockoutTo: event.target.value, page: null })} /></FormField>
      </div>}
    </FilterBar>
    {!runId ? <EmptyState title={t('materialPlanning.noEligibleRun')} description={t('materialPlanning.noEligibleRunHint')} /> : runContextUnavailable ? <EmptyState title={t('materialPlanning.unavailableRun')} description={t('materialPlanning.runContextUnavailable')} /> : loading ? <LoadingState label={t('common.loading')} /> : <>
      <RequestProblem problem={errors.header} />
      {header && <RunContext header={header} language={language} t={t} />}
      <RequestProblem problem={errors.facets} />
      <OperationalDashboard products={products} selectedProduct={selectedProduct}
        productMaterials={productMaterials} topRisks={topRisks} language={language}
        errors={{ products: errors.products, topRisks: errors.topRisks, productMaterials: errors.productMaterials }}
        exceptionCount={header?.exceptionCount ?? 0}
        productSearch={productSearch} productPage={productPage} productPageCount={productPageCount}
        productMaterialPage={productMaterialPage} productMaterialPageCount={productMaterialPageCount}
        onProductSearch={value => patch({ productSearch: value, productPage: null, productId: null, productMaterialPage: null, itemId: null })}
        onProductPage={next => patch({ productPage: String(next) })}
        onProductMaterialPage={next => patch({ productMaterialPage: String(next) })}
        onSelectProduct={id => patch({ productId: id, productMaterialPage: null, itemId: null })}
        onSelectMaterial={id => { setTrace(null); patch({ itemId: id }) }} t={t} />
      <Section title={t('materialPlanning.summary')} description={t('materialPlanning.riskHint')}><>
        <RequestProblem problem={errors.summary} />
        <TableToolbar title={t('materialPlanning.materials')} count={t('common.count', { count: summary?.totalCount ?? 0 })} actions={<div className="ui-action-row"><FormField label={t('materialPlanning.sortBy')}><select value={sortBy} onChange={event => patch({ sortBy: event.target.value, page: null })}>{['risk','code','name','stockout','firmcoverage','netrequirement'].map(value => <option value={value} key={value}>{t(`materialPlanning.sorts.${value}`)}</option>)}</select></FormField><FormField label={t('materialPlanning.sortDirection')}><select value={sortDirection} onChange={event => patch({ sortDirection: event.target.value, page: null })}><option value="asc">{t('materialPlanning.ascending')}</option><option value="desc">{t('materialPlanning.descending')}</option></select></FormField></div>} />
        <DataTable headers={[t('common.code'), t('materialPlanning.material'), t('materialPlanning.itemType'), t('materials.supplyMethod'), t('mrpRuns.stockout'), t('materialPlanning.firmCoverage'), t('materialPlanning.risk'), t('common.actions')]} loading={false} empty={!summary?.items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} emptyDescription={t('common.emptyDescription')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
          {summary?.items.map(row => <tr key={row.item.id} className={row.item.id === itemId ? 'is-selected' : undefined}><td className="technical-value">{row.item.code}</td><td><strong>{localizedName(row.item, language)}</strong><small>{localizedReference(row.item.categoryNameFa, row.item.categoryNameEn, language)}</small></td><td>{t(`materials.${row.item.itemType === 'PART' ? 'part' : 'component'}`)}</td><td>{row.item.supplyMethod ? t(`materials.supplyMethods.${row.item.supplyMethod}`, { defaultValue: row.item.supplyMethod }) : '—'}</td><td>{formatBusinessDate(row.stockoutDate, language)}</td><td><Coverage state={row.firmCoverageState} days={row.firmCoverageDays} date={row.firmCoverageDate} language={language} t={t} /></td><td><StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} /><small>{t(`materialPlanning.reasons.${row.riskReason}`, { defaultValue: row.riskReason })}</small></td><td><button type="button" className="button button--small" onClick={() => patch({ itemId: row.item.id })}>{t('materialPlanning.inspect')}</button></td></tr>)}
        </DataTable>
        <Pagination page={page} pageSize={pageSize} totalCount={summary?.totalCount ?? 0} onPageChange={next => patch({ page: String(next) })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: pageCount, count: summary?.totalCount ?? 0 })} />
      </></Section>
      {selected && <MaterialDetail row={selected} language={language} t={t} />}
      {selected && timeline && <OperationalExplanation row={selected} timeline={timeline} language={language} t={t} />}
      <RequestProblem problem={errors.timeline} />
      {timeline && <TimelineSection timeline={timeline} grain={grain} language={language} onGrainChange={value => patch({ grain: value })} onTrace={inspectTrace} t={t} />}
      <RequestProblem problem={errors.trace} />
      {selected && trace && <aside className="material-trace-drawer" aria-label={t('materialPlanning.trace')}><div className="material-trace-drawer__header"><strong>{t('materialPlanning.trace')} — {selected.item.code} — {formatBusinessDate(trace.demandDate, language)}</strong><button className="icon-button" type="button" onClick={() => setTrace(null)} aria-label={t('common.close')}>×</button></div><TraceView trace={trace} language={language} t={t} /></aside>}
      <RequestProblem problem={errors.action} />
      <ExceptionCenter error={errors.exceptions} rows={exceptions} page={exceptionPage} pages={exceptionPages} language={language} filters={{ exceptionSeverity, exceptionCode, exceptionSource, exceptionDate, exceptionAffects }} patch={patch} canCreateWorkItem={canManageInbox} onCreateWorkItem={async exceptionId => { try { await post(`${api}/planner-work-items`, { exceptionId, assignedUserId: null }); clearError('action'); patch({ view: 'inbox' }) } catch (caught) { reportError('action', caught) } }} t={t} />
    </>}
  </main>
}

function RefreshBanner({ status, language, t }: { status: RefreshStatus; language: string; t: Translate }) {
  const tone: StatusTone = status.status === 'FAILED' ? 'critical' :
    status.status === 'REFRESHING' || status.status === 'PENDING' ? 'warning' : 'positive'
  return <div className={`mrp-refresh-status mrp-refresh-status--${status.status.toLowerCase()}`} role="status">
    <StatusBadge text={t(`materialPlanning.refresh.${status.status}`, { defaultValue: status.status })} tone={tone} />
    <span>{status.status === 'REFRESHING' || status.status === 'PENDING'
      ? t('materialPlanning.refresh.pendingHint', { count: status.pendingChangeCount })
      : t('materialPlanning.refresh.lastUpdated', { date: formatBusinessDateTime(status.lastUpdatedAtUtc, language) })}</span>
    {status.errorCode && <strong className="technical-value">{status.errorCode}</strong>}
  </div>
}

function OperationalDashboard({ products, selectedProduct, productMaterials, topRisks, language,
  exceptionCount, errors,
  productSearch, productPage, productPageCount, productMaterialPage, productMaterialPageCount,
  onProductSearch, onProductPage, onProductMaterialPage, onSelectProduct, onSelectMaterial, t,
}: {
  products: Page<ProductRow> | null
  selectedProduct?: ProductRow
  productMaterials: Page<Summary> | null
  topRisks: Summary[]
  exceptionCount: number
  errors: { products?: string; topRisks?: string; productMaterials?: string }
  language: string
  productSearch: string
  productPage: number
  productPageCount: number
  productMaterialPage: number
  productMaterialPageCount: number
  onProductSearch: (value: string) => void
  onProductPage: (page: number) => void
  onProductMaterialPage: (page: number) => void
  onSelectProduct: (id: string) => void
  onSelectMaterial: (id: string) => void
  t: Translate
}) {
  const firstRisk = topRisks[0]
  return <Section title={t('materialPlanning.dashboardTitle')} description={t('materialPlanning.dashboardHint')}><>
    <RequestProblem problem={errors.products} />
    <RequestProblem problem={errors.topRisks} />
    <div className="mrp-kpi-grid">
      <article><span>{t('materialPlanning.forecastProducts')}</span><strong>{formatQuantity(products?.totalCount ?? 0, language)}</strong></article>
      <article><span>{t('materialPlanning.firstBottleneck')}</span><strong>{firstRisk?.item.code ?? '—'}</strong><small>{firstRisk ? localizedName(firstRisk.item, language) : t('materialPlanning.noBottleneck')}</small></article>
      <article><span>{t('materialPlanning.earliestStockout')}</span><strong>{formatBusinessDate(firstRisk?.stockoutDate, language)}</strong><small>{firstRisk ? t(`materialPlanning.risks.${firstRisk.riskBand}`) : '—'}</small></article>
      <article><span>{t('materialPlanning.openExceptions')}</span><strong>{formatQuantity(exceptionCount, language)}</strong><small>{t('materialPlanning.exceptionActionHint')}</small></article>
    </div>
    <div className="mrp-dashboard-grid">
      <div>
        <TableToolbar title={t('materialPlanning.productOutlook')} count={t('common.count', { count: products?.totalCount ?? 0 })} actions={<input value={productSearch} onChange={event => onProductSearch(event.target.value)} placeholder={t('materialPlanning.productSearch')} />} />
        <DataTable headers={[t('common.code'), t('forecastActual.product'), t('materialPlanning.plannedProduction'), t('materialPlanning.materialCount'), t('materialPlanning.stockCoverage'), t('materialPlanning.firmCoverage'), t('materialPlanning.firstConstraint'), t('materialPlanning.risk')]} empty={!products?.items.length} loadingLabel={t('common.loading')} emptyTitle={t('materialPlanning.noForecastProducts')} noResultTitle={t('common.noResultsTitle')}>
          {products?.items.map(row => <tr key={row.product.id} className={row.product.id === selectedProduct?.product.id ? 'is-selected' : undefined} onClick={() => onSelectProduct(row.product.id)} tabIndex={0} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') onSelectProduct(row.product.id) }}>
            <td className="technical-value">{row.product.code}</td><td><strong>{localizedName(row.product, language)}</strong></td>
            <td>{formatQuantity(row.plannedProductionQty, language)}</td><td>{formatQuantity(row.materialCount, language)}<small>{t('materialPlanning.attentionCount', { count: row.attentionMaterialCount })}</small></td>
            <td><Coverage state={row.stockCoverageState} days={row.stockCoverageDays} date={row.stockCoverageDate} language={language} t={t} /></td>
            <td><Coverage state={row.firmCoverageState} days={row.firmCoverageDays} date={row.firmCoverageDate} language={language} t={t} /></td>
            <td>{row.firstConstraintMaterial ? <><strong className="technical-value">{row.firstConstraintMaterial.code}</strong><small>{localizedName(row.firstConstraintMaterial, language)} · {formatBusinessDate(row.firstConstraintDate, language)}</small></> : '—'}</td>
            <td><StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} /></td>
          </tr>)}
        </DataTable>
        <Pagination page={productPage} pageSize={pageSize} totalCount={products?.totalCount ?? 0} onPageChange={onProductPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: productPage, pages: productPageCount, count: products?.totalCount ?? 0 })} />
      </div>
      <aside className="mrp-top-risks"><h3>{t('materialPlanning.topTen')}</h3>{topRisks.length ? <ol>{topRisks.map(row => <li key={row.item.id}><button type="button" onClick={() => onSelectMaterial(row.item.id)}><StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} /><span><strong className="technical-value">{row.item.code}</strong>{localizedName(row.item, language)}</span><small>{formatBusinessDate(row.stockoutDate ?? row.firmCoverageDate, language)}</small></button></li>)}</ol> : <p>{t('materialPlanning.noBottleneck')}</p>}</aside>
    </div>
    {selectedProduct && <div className="mrp-product-materials">
      <h3>{t('materialPlanning.productMaterials')} — <span className="technical-value">{selectedProduct.product.code}</span> — {localizedName(selectedProduct.product, language)}</h3>
      <MessageStrip tone="information">{t('materialPlanning.sharedMaterialRule')}</MessageStrip>
      <RequestProblem problem={errors.productMaterials} />
      <DataTable headers={[t('common.code'), t('materialPlanning.material'), t('mrpRuns.starting'), t('mrpRuns.gross'), t('mrpRuns.firm'), t('mrpRuns.net'), t('mrpRuns.stockout'), t('materialPlanning.risk'), t('common.actions')]} empty={!productMaterials?.items.length} loadingLabel={t('common.loading')} emptyTitle={t('materialPlanning.noProductMaterials')} noResultTitle={t('common.noResultsTitle')}>
        {productMaterials?.items.map(row => <tr key={row.item.id} className={`planning-zone planning-zone--${zoneClass(row.riskBand)}`}><td className="technical-value">{row.item.code}</td><td><strong>{localizedName(row.item, language)}</strong><small>{row.item.itemType}</small></td><td>{formatQuantity(row.startingOnHand, language)}</td><td>{formatQuantity(row.totalGrossDemand, language)}</td><td>{formatQuantity(row.totalFirmIncoming, language)}</td><td>{formatQuantity(row.totalNetRequirement, language)}</td><td>{formatBusinessDate(row.stockoutDate, language)}</td><td><StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} /></td><td><button className="button button--small" type="button" onClick={() => onSelectMaterial(row.item.id)}>{t('materialPlanning.whyCritical')}</button></td></tr>)}
      </DataTable>
      <Pagination page={productMaterialPage} pageSize={pageSize} totalCount={productMaterials?.totalCount ?? 0} onPageChange={onProductMaterialPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: productMaterialPage, pages: productMaterialPageCount, count: productMaterials?.totalCount ?? 0 })} />
    </div>}
  </></Section>
}

function RunContext({ header, language, t }: { header: RunHeader; language: string; t: Translate }) {
  return <Section title={t('materialPlanning.runContext')}>
    <div className="mrp-run-context-summary" aria-label={t('materialPlanning.runContext')}>
      <div><span>{t('mrpRuns.asOf')}</span><strong>{formatBusinessDate(header.asOfDate, language)}</strong></div>
      <div><span>{t('mrpRuns.horizon')}</span><strong>{formatBusinessDate(header.horizonEndDate, language)}</strong></div>
      <div><span>{t('common.status')}</span><StatusBadge text={t(`mrpRuns.status.${header.status}`, { defaultValue: header.status })} tone={header.status === 'COMPLETED' ? 'positive' : 'warning'} /></div>
      <div><span>{t('mrpRuns.completeness')}</span><StatusBadge text={t(`mrpRuns.status.${header.completeness}`, { defaultValue: header.completeness })} tone={header.completeness === 'COMPLETE' ? 'positive' : header.completeness === 'INCOMPLETE' ? 'critical' : 'warning'} /></div>
    </div>
    <details className="mrp-run-technical-details">
      <summary>{t('materialPlanning.showRunContext')}</summary>
      <DefinitionGrid>
        <Definition label={t('mrpRuns.number')} technical>{header.runNumber}</Definition>
        <Definition label={t('mrpRuns.engine')} technical>{header.engineVersion}</Definition>
        <Definition label={t('materialPlanning.completedAt')}>{formatBusinessDateTime(header.completedAtUtc, language)}</Definition>
        <Definition label={t('materialPlanning.materialCount')} technical>{formatQuantity(header.materialCount, language)}</Definition>
        <Definition label={t('mrpRuns.exceptions')} technical>{formatQuantity(header.exceptionCount, language)}</Definition>
        <Definition label={t('materialPlanning.history')}><StatusBadge text={t(`materialPlanning.planRoles.${header.planRole}`, { defaultValue: header.planRole })} tone={header.planRole === 'CURRENT' ? 'information' : header.planRole === 'CERTIFIED' ? 'positive' : 'neutral'} /></Definition>
      </DefinitionGrid>
    </details>
  </Section>
}

function MaterialDetail({ row, language, t }: { row: Summary; language: string; t: Translate }) {
  const groups: Array<{ title: string; facts: DetailFact[] }> = [
    { title: t('materialPlanning.material'), facts: [
      { label: t('common.code'), value: row.item.code, technical: true },
      { label: t('materialPlanning.material'), value: localizedName(row.item, language) },
      { label: t('materialPlanning.itemType'), value: t(`materials.${row.item.itemType === 'PART' ? 'part' : 'component'}`) },
      { label: t('common.category'), value: `${row.item.categoryCode ?? '—'} ${localizedReference(row.item.categoryNameFa, row.item.categoryNameEn, language)}` },
      { label: t('materials.supplyMethod'), value: row.item.supplyMethod ? t(`materials.supplyMethods.${row.item.supplyMethod}`, { defaultValue: row.item.supplyMethod }) : '—' },
      { label: t('common.unit'), value: `${row.item.unitCode} — ${localizedReference(row.item.unitNameFa, row.item.unitNameEn, language)}` },
    ] },
    { title: t('materialPlanning.summary'), facts: [
      { label: t('mrpRuns.starting'), value: formatQuantity(row.startingOnHand, language), technical: true },
      { label: t('mrpRuns.gross'), value: formatQuantity(row.totalGrossDemand, language), technical: true },
      { label: t('mrpRuns.firm'), value: formatQuantity(row.totalFirmIncoming, language), technical: true },
      { label: t('mrpRuns.net'), value: formatQuantity(row.totalNetRequirement, language), technical: true },
      { label: t('mrpRuns.stockout'), value: formatBusinessDate(row.stockoutDate, language) },
      { label: t('materialPlanning.safetyBreach'), value: formatBusinessDate(row.safetyBreachDate, language) },
      { label: t('materialPlanning.stockCoverage'), value: <Coverage state={row.stockCoverageState} days={row.stockCoverageDays} date={row.stockCoverageDate} language={language} t={t} /> },
      { label: t('materialPlanning.firmCoverage'), value: <Coverage state={row.firmCoverageState} days={row.firmCoverageDays} date={row.firmCoverageDate} language={language} t={t} /> },
    ] },
    { title: t('materialPlanning.warnings'), facts: [
      { label: t('materialPlanning.risk'), value: <StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} /> },
      { label: t('mrpRuns.completeness'), value: t(`mrpRuns.status.${row.completeness}`, { defaultValue: row.completeness }) },
      { label: t('mrpRuns.exceptions'), value: `${formatQuantity(row.exceptionCount, language)}${row.primaryExceptionCode ? ` — ${row.primaryExceptionCode}` : ''}` },
      { label: t('materialPlanning.overdue'), value: formatQuantity(row.overdueFirmQty, language), technical: true },
      { label: t('materialPlanning.missingEta'), value: formatQuantity(row.missingEtaFirmQty, language), technical: true },
    ] },
  ]
  const rows = Math.max(...groups.map(group => group.facts.length))
  return <Section title={`${t('materialPlanning.operationalDetail')} — ${row.item.code}`}>
    <div className="report-detail-table-wrap">
      <table className="report-detail-table" aria-label={t('materialPlanning.operationalDetail')}>
        <thead><tr>{groups.map(group => <th scope="colgroup" colSpan={2} key={group.title}>{group.title}</th>)}</tr></thead>
        <tbody>{Array.from({ length: rows }, (_, index) => <tr key={index}>{groups.flatMap((group, groupIndex) => {
          const fact = group.facts[index]
          return fact
            ? [<th scope="row" key={`${groupIndex}-label`}>{fact.label}</th>, <td className={fact.technical ? 'ui-technical ui-number' : undefined} key={`${groupIndex}-value`}>{fact.value}</td>]
            : [<td className="report-detail-table__empty" colSpan={2} key={`${groupIndex}-empty`} />]
        })}</tr>)}</tbody>
      </table>
    </div>
  </Section>
}

function OperationalExplanation({ row, timeline, language, t }: { row: Summary; timeline: Timeline; language: string; t: Translate }) {
  const consumers = timeline.usedByProducts.map(product => `${product.code} — ${localizedName(product, language)}`).join('، ')
  const status = row.stockoutDate
    ? t('materialPlanning.explanationStockout', { item: row.item.code, date: formatBusinessDate(row.stockoutDate, language) })
    : row.firmCoverageState === 'BEYOND_HORIZON'
      ? t('materialPlanning.explanationCovered', { item: row.item.code })
      : t('materialPlanning.explanationIncomplete', { item: row.item.code })
  return <Section title={t('materialPlanning.explanationTitle')} description={t('materialPlanning.explanationHint')}>
    <div className="mrp-operational-explanation" role="status">
      <StatusBadge text={t(`materialPlanning.risks.${row.riskBand}`)} tone={riskTone(row.riskBand)} />
      <p><strong>{status}</strong></p>
      <p>{t('materialPlanning.explanationBalance', { onHand: formatQuantity(row.startingOnHand, language), firm: formatQuantity(row.totalFirmIncoming, language), demand: formatQuantity(row.totalGrossDemand, language), net: formatQuantity(row.totalNetRequirement, language) })}</p>
      {consumers && <p>{t('materialPlanning.explanationConsumers', { products: consumers })}</p>}
      {(row.overdueFirmQty > 0 || row.missingEtaFirmQty > 0) && <p>{t('materialPlanning.explanationSupplyWarnings', { overdue: formatQuantity(row.overdueFirmQty, language), missing: formatQuantity(row.missingEtaFirmQty, language) })}</p>}
    </div>
  </Section>
}

function TimelineSection({ timeline, grain, language, onGrainChange, onTrace, t }: { timeline: Timeline; grain: string; language: string; onGrainChange: (value: string) => void; onTrace: (date: string) => Promise<void>; t: Translate }) {
  return <Section title={`${t('materialPlanning.timeline')} — ${timeline.item.code}`} actions={<select aria-label={t('materialPlanning.grain')} value={grain} onChange={event => onGrainChange(event.target.value)}>{['DAILY','WEEKLY','MONTHLY'].map(value => <option key={value} value={value}>{t(`materialPlanning.grains.${value}`)}</option>)}</select>}><>
    <div className="coverage-comparison"><div><span>{t('materialPlanning.stockCoverage')}</span><Coverage state={timeline.stockCoverageState} days={timeline.stockCoverageDays} date={timeline.stockCoverageDate} language={language} t={t} /></div><div><span>{t('materialPlanning.firmCoverage')}</span><Coverage state={timeline.firmCoverageState} days={timeline.firmCoverageDays} date={timeline.firmCoverageDate} language={language} t={t} /></div></div>
    {timeline.usedByProducts.length > 0 && <MessageStrip tone="information">{t('materialPlanning.usedByProducts')}: {timeline.usedByProducts.map(product => `${product.code} — ${localizedName(product, language)}`).join('، ')}</MessageStrip>}
    <div className="planning-zone-legend" aria-label={t('materialPlanning.zoneLegend')}>{['CRITICAL','LOW','SAFE','EXCESS'].map(zone => <span className={`planning-zone-legend__item planning-zone-legend__item--${zoneClass(zone)}`} key={zone}>{t(`materialPlanning.zones.${zone}`)}</span>)}</div>
    <DataTable headers={[t('materialPlanning.bucket'), t('mrpRuns.gross'), t('mrpRuns.firm'), t('materialPlanning.stockOnlyProjected'), t('mrpRuns.firmProjected'), t('mrpRuns.net'), t('materialPlanning.coverageDays'), t('materialPlanning.zone'), t('materialPlanning.events'), t('common.actions')]} empty={!timeline.buckets.length} loadingLabel={t('common.loading')} emptyTitle={t('materialPlanning.noTimeline')} noResultTitle={t('common.noResultsTitle')}><>{timeline.buckets.map(bucket => <tr className={`planning-zone planning-zone--${zoneClass(bucket.zone)}`} key={`${bucket.startDate}-${bucket.endDate}`}><td>{bucket.startDate === bucket.endDate ? formatBusinessDate(bucket.startDate, language) : `${formatBusinessDate(bucket.startDate, language)} — ${formatBusinessDate(bucket.endDate, language)}`}</td><td className="ui-number">{formatQuantity(bucket.grossDemand, language)}</td><td className="ui-number">{formatQuantity(bucket.firmIncoming, language)}</td><td className="ui-number">{formatQuantity(bucket.stockOnlyProjected, language)}</td><td className="ui-number">{formatQuantity(bucket.firmProjected, language)}</td><td className="ui-number">{formatQuantity(bucket.netRequirement, language)}</td><td className="ui-number">{bucket.coverageDays == null ? t('materialPlanning.beyondRun') : formatQuantity(bucket.coverageDays, language)}</td><td><StatusBadge text={t(`materialPlanning.zones.${bucket.zone}`, { defaultValue: bucket.zone })} tone={riskTone(bucket.zone)} /></td><td>{bucket.includesStockoutEvent && <StatusBadge text={t('materialPlanning.stockoutEvent')} tone="critical" />}{bucket.includesSafetyBreachEvent && <StatusBadge text={t('materialPlanning.safetyBreachEvent')} tone="warning" />}</td><td>{grain === 'DAILY' && <button className="button button--small" type="button" onClick={() => void onTrace(bucket.startDate)}>{t('mrpRuns.lineage')}</button>}</td></tr>)}</></DataTable>
  </></Section>
}

function TraceView({ trace, language, t }: { trace: Trace; language: string; t: Translate }) {
  return <div className="trace-report-stack">
    <TraceTable title={t('materialPlanning.demandTrace')} headers={[t('materialPlanning.forecastRevision'), t('mrpRuns.forecastPeriod'), t('mrpRuns.allocatedDemand'), t('mrpRuns.allocationState')]} empty={trace.origins.length === 0}>
      {trace.origins.map((row, index) => <tr key={index}><td className="ui-technical">{shortId(row.forecastRevisionId)}</td><td>{formatBusinessDate(row.periodStartDate, language)} — {formatBusinessDate(row.periodEndDate, language)}</td><td className="ui-number">{formatQuantity(row.allocatedDemandQty, language)}</td><td>{row.allocationState}</td></tr>)}
    </TraceTable>
    <TraceTable title={t('mrpRuns.bomLineage')} headers={['BOM', t('bom.parent'), t('bom.child'), t('bom.usage'), t('materials.scrap'), t('mrpRuns.gross')]} empty={trace.bom.length === 0}>
      {trace.bom.map((row, index) => <tr key={index}><td className="ui-technical">{row.revisionCode}</td><td className="ui-technical">{row.parentItemCode}</td><td className="ui-technical">{row.childItemCode}</td><td className="ui-number">{formatQuantity(row.baseRequirement, language)}</td><td className="ui-number">{formatQuantity(row.scrapPercent, language)}% <small>{row.scrapSource}</small></td><td className="ui-number">{formatQuantity(row.grossContribution, language)}</td></tr>)}
    </TraceTable>
    <TraceTable title={t('mrpRuns.inventoryLineage')} headers={[t('inventory.warehouse'), t('inventory.quantity'), t('common.status')]} empty={trace.inventory.length === 0} description={`${t('mrpRuns.companyOnHand')}: ${formatQuantity(trace.companyStartingOnHand, language)}`}>
      {trace.inventory.map(row => <tr key={row.warehouseId}><td className="ui-technical">{row.warehouseCode}</td><td className="ui-number">{formatQuantity(row.onHandQty, language)}</td><td>{row.warehouseIsActive ? t('common.active') : <StatusBadge text={t('mrpRuns.inactiveWarehouse')} tone="warning" />}</td></tr>)}
    </TraceTable>
    <TraceTable title={t('materialPlanning.supplyTrace')} headers={[t('purchaseOrders.poNumber'), t('purchaseOrders.supplier'), t('materialPlanning.poLineId'), t('materialPlanning.scheduleId'), t('purchaseOrders.openQty'), 'ETA', t('common.status')]} empty={trace.firmSupply.length === 0}>
      {trace.firmSupply.map(row => <tr key={row.deliveryScheduleId}><td className="ui-technical">{row.purchaseOrderNumber}</td><td>{row.supplierNumber ?? '—'}<small>{localizedReference(row.supplierNameFa, row.supplierNameEn, language)}</small></td><td className="ui-technical">{row.purchaseOrderLineId}</td><td className="ui-technical">{row.deliveryScheduleId}</td><td className="ui-number">{formatQuantity(row.openQty, language)}</td><td>{formatBusinessDate(row.eta, language)}<small>{row.etaStatus}</small></td><td><StatusBadge text={row.appliedByDemandDate ? t('mrpRuns.applied') : t('mrpRuns.excluded')} tone={row.appliedByDemandDate ? 'positive' : 'neutral'} /></td></tr>)}
    </TraceTable>
    <TraceTable title={t('mrpRuns.nettingLineage')} headers={[t('mrpRuns.gross'), t('mrpRuns.firm'), t('materialPlanning.safetyStock'), t('mrpRuns.net'), t('mrpRuns.firmProjected'), t('mrpRuns.planningBalance')]} empty={!trace.netting}>
      {trace.netting && <tr><td className="ui-number">{formatQuantity(trace.netting.grossRequirement, language)}</td><td className="ui-number">{formatQuantity(trace.netting.firmIncoming, language)}</td><td className="ui-number">{formatQuantity(trace.netting.safetyStock, language)}</td><td className="ui-number">{formatQuantity(trace.netting.netRequirement, language)}</td><td className="ui-number">{formatQuantity(trace.netting.firmProjectedAfter, language)}</td><td className="ui-number">{formatQuantity(trace.netting.planningBalance, language)}</td></tr>}
    </TraceTable>
    <TraceTable title={t('mrpRuns.routeLineage')} headers={[t('mrpRuns.route'), t('materialPlanning.source'), t('mrpRuns.exceptions')]} empty={false}>
      <tr><td>{trace.route.chosenRoute ?? '—'}</td><td>{trace.route.decisionSource ?? '—'}</td><td>{trace.route.exceptionCodes.join('، ') || '—'}</td></tr>
    </TraceTable>
  </div>
}

function ExceptionCenter({ error, rows, page, pages, language, filters, patch, canCreateWorkItem, onCreateWorkItem, t }: { error?: string; rows: Page<ExceptionRow> | null; page: number; pages: number; language: string; filters: Record<string, string>; patch: (values: Record<string, string | null>) => void; canCreateWorkItem: boolean; onCreateWorkItem: (exceptionId: string) => Promise<void>; t: Translate }) {
  return <Section title={t('materialPlanning.exceptionCenter')} description={t('materialPlanning.readOnlyExceptions')}><>
    <RequestProblem problem={error} />
    <FilterBar label={t('materialPlanning.exceptionFilters')}><FormField label={t('mrpRuns.exceptionSeverity')}><select value={filters.exceptionSeverity} onChange={event => patch({ exceptionSeverity: event.target.value, exceptionPage: null })}><option value="">{t('common.all')}</option>{['ERROR','WARNING','INFO'].map(value => <option key={value} value={value}>{value}</option>)}</select></FormField><FormField label={t('common.code')}><input value={filters.exceptionCode} onChange={event => patch({ exceptionCode: event.target.value, exceptionPage: null })} /></FormField><FormField label={t('materialPlanning.source')}><select value={filters.exceptionSource} onChange={event => patch({ exceptionSource: event.target.value, exceptionPage: null })}><option value="">{t('common.all')}</option>{['FORECAST','BOM','INVENTORY','PURCHASE_SUPPLY','CATALOG','MRP'].map(value => <option key={value} value={value}>{value}</option>)}</select></FormField><FormField label={t('mrpRuns.date')}><input type="date" dir="ltr" value={filters.exceptionDate} onChange={event => patch({ exceptionDate: event.target.value, exceptionPage: null })} /></FormField><FormField label={t('materialPlanning.affectsDemand')}><select value={filters.exceptionAffects} onChange={event => patch({ exceptionAffects: event.target.value, exceptionPage: null })}><option value="">{t('common.all')}</option><option value="true">{t('common.yes')}</option><option value="false">{t('common.no')}</option></select></FormField></FilterBar>
    <DataTable headers={[t('common.code'), t('materialPlanning.source'), t('mrpRuns.date'), t('materialPlanning.material'), t('mrpRuns.exceptionSeverity'), t('materialPlanning.affectsDemand'), t('materialPlanning.message'), t('common.actions')]} empty={!rows?.items.length} loadingLabel={t('common.loading')} emptyTitle={t('mrpRuns.noExceptions')} noResultTitle={t('common.noResultsTitle')}><>{rows?.items.map(row => { const destination = sourceNavigation(row.sourceModule, row.sourceReference); return <tr key={row.id}><td className="technical-value">{row.code}</td><td>{row.sourceModule}<small>{row.sourceReference}</small></td><td>{formatBusinessDate(row.date, language)}</td><td className="technical-value">{row.itemCode ?? '—'}</td><td><StatusBadge text={row.severity} tone={row.severity === 'ERROR' ? 'critical' : row.severity === 'WARNING' ? 'warning' : 'information'} /></td><td>{row.affectsDemand ? t('common.yes') : t('common.no')}</td><td>{row.message}</td><td><div className="ui-action-row">{destination && <Link className="button button--small" to={destination}>{t('materialPlanning.openSource')}</Link>}{canCreateWorkItem && <button className="button button--small" type="button" onClick={() => void onCreateWorkItem(row.id)}>{t('materialPlanning.collaboration.addToInbox')}</button>}</div></td></tr> })}</></DataTable>
    <Pagination page={page} pageSize={25} totalCount={rows?.totalCount ?? 0} onPageChange={next => patch({ exceptionPage: String(next) })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages, count: rows?.totalCount ?? 0 })} />
  </></Section>
}

type Translate = (key: string, options?: Record<string, unknown>) => string
type DetailFact = { label: string; value: ReactNode; technical?: boolean }
function RequestProblem({ problem }: { problem?: string }) { return problem ? <MessageStrip tone="critical">{problem}</MessageStrip> : null }
function TraceTable({ title, description, headers, empty, children }: { title: string; description?: string; headers: string[]; empty: boolean; children: ReactNode }) { return <section className="trace-report"><header><h3>{title}</h3>{description && <span>{description}</span>}</header><div className="trace-report__table-wrap"><table className="ui-data-table"><thead><tr>{headers.map(header => <th scope="col" key={header}>{header}</th>)}</tr></thead><tbody>{empty ? <tr><td colSpan={headers.length}>—</td></tr> : children}</tbody></table></div></section> }
function Coverage({ state, days, date, language, t }: { state?: string; days?: number; date?: string; language: string; t: Translate }) { return <span className="coverage-value"><strong>{t(`materialPlanning.coverage.${state ?? 'INCOMPLETE_DATA'}`, { defaultValue: state ?? 'INCOMPLETE_DATA' })}</strong>{state === 'DATE' && <small>{formatQuantity(days ?? 0, language)} {t('materialPlanning.days')} · {formatBusinessDate(date, language)}</small>}</span> }
function riskTone(risk: string): StatusTone { return risk === 'INCOMPLETE' || risk === 'STOCKOUT' ? 'critical' : risk === 'CRITICAL' || risk === 'LOW' ? 'warning' : risk === 'SAFE' ? 'positive' : 'information' }
function zoneClass(zone: string) { return zone === 'STOCKOUT' || zone === 'CRITICAL' ? 'critical' : zone === 'LOW' ? 'low' : zone === 'SAFE' ? 'safe' : zone === 'EXCESS' ? 'excess' : 'incomplete' }
function localizedReference(fa?: string, en?: string, language?: string) { if (!fa) return ''; return language?.toLowerCase().startsWith('en') && en?.trim() ? en : fa }
function positive(value: string | null, fallback: number) { const parsed = Number(value); return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback }
function shortId(value?: string) { return value ? value.slice(0, 8) : '—' }
function nonEmptyQuery(values: Record<string, string | number | undefined>) {
  const query = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && String(value).trim() !== '') query.set(key, String(value))
  })
  return query
}
async function get<T>(url: string): Promise<T> { const response = await fetch(url); if (!response.ok) throw await createMaterialPlanningApiError(response, url); return response.json() }
async function post<T>(url: string, body: unknown): Promise<T> { const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); if (!response.ok) throw await createMaterialPlanningApiError(response, url); return response.json() }
function message(error: unknown, t: Translate) { return formatMaterialPlanningProblem(error, t) }
