import { FormEvent, useEffect, useId, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Copy, Eye, FilePenLine, LockKeyhole, Plus, RefreshCw, Send } from 'lucide-react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { apiRoot } from './apiClient'
import { useAuth } from './AuthContext'
import { DualDate, DualDateFilter, SectionFailure } from './CashFlowPage'
import {
  buildReportingRateSetQuery,
  canRenderReportingRateEditor,
  cashFlowFieldHasError,
  cashFlowRequestError,
  formatReportingRate,
  isExactDecimalInput,
  mergeCopiedReportingRateValues,
  normalizeReportingRateInput,
  positiveCashFlowPage,
  readCashFlowApiError,
  retainClientRequestAttempt,
  reportingRateDisplayCount,
  reportingRateCurrenciesForEditor,
  reportingRateEffectiveDateIsLocked,
  reportingRateMissingCurrencies,
  reportingRateServerInvalidIds,
  reportingRateSetPath,
  stableClientRequestAttempt,
  tehranTodayIso,
  type CashFlowRequestError,
  type FxRateSetStatus,
  type ReportingRateCurrency,
  type ReportingRateSet,
  type ReportingRateSetPage,
  type StableClientRequestAttempt,
} from './cashFlowUi'
import { normalizeLanguage } from './i18n'
import { formatBusinessDateTime } from './i18n/businessDate'
import { localizedName } from './i18n/localizedName'
import { formatOperationalNumber } from './i18n/numberFormatting'
import {
  ConfirmationDialog,
  DataTable,
  Definition,
  DefinitionGrid,
  FilterBar,
  FormField,
  LoadingState,
  MessageStrip,
  PageHeader,
  Pagination,
  Section,
  StatusBadge,
  TableToolbar,
} from './ui'
import { UnsavedChangesGuard } from './ui/UnsavedChangesGuard'

const api = `${apiRoot}/v1/cash-flow`
const pageSize = 25

export type ReportingRatesView = 'list' | 'create' | 'view' | 'edit'

export default function CashFlowReportingRatesPage({ view }: { view: ReportingRatesView }) {
  if (view === 'list') return <ReportingRateListPage />
  return <ReportingRateObjectPage mode={view} />
}

function ReportingRateListPage() {
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params, setParams] = useSearchParams()
  const page = positiveCashFlowPage(params.get('page'))
  const requestedStatus = params.get('status')
  const status: FxRateSetStatus | '' = requestedStatus === 'DRAFT' || requestedStatus === 'PUBLISHED' ? requestedStatus : ''
  const [data, setData] = useState<ReportingRateSetPage>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [retry, setRetry] = useState(0)
  const { has } = useAuth()
  const canManage = has('cash_flow.reporting_rate.manage')

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      void getJson<ReportingRateSetPage>(`${api}/reporting-rate-sets?${buildReportingRateSetQuery({ page, pageSize, status })}`, controller.signal)
        .then(value => { if (active) setData(value) })
        .catch(reason => { if (active && !(reason instanceof Error && reason.name === 'AbortError')) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [page, retry, status])

  const update = (changes: Record<string, string | number>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === '') next.delete(key)
      else next.set(key, String(value))
    }
    setParams(next)
  }

  return <main className="cash-flow-page cash-flow-reporting-rates">
    <PageHeader eyebrow={t('cashFlow.privateWorkspace')} title={t('cashFlow.reportingRates.title')} subtitle={t('cashFlow.reportingRates.subtitle')}
      actions={canManage && <Link className="button button--primary" to="/cash-flow/reporting-rates/new"><Plus aria-hidden="true" />{t('cashFlow.reportingRates.newSet')}</Link>} />
    <MessageStrip>{t('cashFlow.reportingRates.definition')}</MessageStrip>
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('common.status')}><select value={status} onChange={event => update({ status: event.target.value, page: 1 })}><option value="">{t('common.all')}</option><option value="DRAFT">{t('cashFlow.reportingRates.status.DRAFT')}</option><option value="PUBLISHED">{t('cashFlow.reportingRates.status.PUBLISHED')}</option></select></FormField>
      <button className="button button--quiet" type="button" onClick={() => setRetry(value => value + 1)}><RefreshCw aria-hidden="true" />{t('common.refresh')}</button>
    </FilterBar>
    {error && <SectionFailure error={error} onRetry={() => setRetry(value => value + 1)} />}
    {!error && <Section title={t('cashFlow.reportingRates.listTitle')} description={t('cashFlow.reportingRates.listHint')}><>
      <TableToolbar title={t('cashFlow.reportingRates.rateSets')} count={data ? t('common.count', { count: data.totalCount }) : undefined} />
      <DataTable headers={[t('cashFlow.reportingRates.effectiveDate'), t('cashFlow.reportingRates.revision'), t('common.status'), t('cashFlow.reportingRates.rateCount'), t('cashFlow.reportingRates.provenance'), t('common.actions')]}
        loading={loading} empty={!data?.items.length} filtered={Boolean(status)} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.reportingRates.emptyTitle')} emptyDescription={t('cashFlow.reportingRates.emptyHint')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
        <>{data?.items.map(rateSet => <tr key={rateSet.id}>
          <td><DualDate value={rateSet.effectiveDate} language={language} /></td>
          <td className="ui-number"><strong>{formatOperationalNumber(rateSet.revision, language)}</strong><small><bdi>{rateSet.baseCurrencyCode}</bdi></small></td>
          <td><RateSetStatus status={rateSet.status} /></td>
          <td className="ui-number">{formatOperationalNumber(reportingRateDisplayCount(rateSet.rateCount), language)}</td>
          <td><strong>{rateSet.publishedAtUtc ? t('cashFlow.reportingRates.publishedAt') : t('cashFlow.reportingRates.createdAt')}</strong><small><bdi>{formatBusinessDateTime(rateSet.publishedAtUtc ?? rateSet.createdAtUtc, language)}</bdi></small></td>
          <td><Link className="button button--compact" to={reportingRateSetPath(rateSet.id)}><Eye aria-hidden="true" />{t('common.details')}</Link></td>
        </tr>)}</>
      </DataTable>
      {data && <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={next => update({ page: next })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(data.totalCount / pageSize)), count: data.totalCount })} />}
    </></Section>}
  </main>
}

function ReportingRateObjectPage({ mode }: { mode: Exclude<ReportingRatesView, 'list'> }) {
  const { id } = useParams()
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [rateSet, setRateSet] = useState<ReportingRateSet>()
  const [currencies, setCurrencies] = useState<ReportingRateCurrency[]>([])
  const [loading, setLoading] = useState(mode !== 'create')
  const [currenciesLoading, setCurrenciesLoading] = useState(mode !== 'view')
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [currenciesError, setCurrenciesError] = useState<CashFlowRequestError | null>(null)
  const [retry, setRetry] = useState(0)
  const [dialog, setDialog] = useState<'publish' | 'correction' | null>(null)
  const [reason, setReason] = useState('')
  const [commandBusy, setCommandBusy] = useState(false)
  const [commandError, setCommandError] = useState<CashFlowRequestError | null>(null)
  const publishAttempt = useRef<StableClientRequestAttempt | null>(null)
  const correctionAttempt = useRef<StableClientRequestAttempt | null>(null)
  const navigate = useNavigate()
  const { has } = useAuth()
  const canManage = has('cash_flow.reporting_rate.manage')

  useEffect(() => {
    if (mode === 'create') return
    if (!id) return
    const controller = new AbortController()
    let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      void getJson<ReportingRateSet>(`${api}/reporting-rate-sets/${encodeURIComponent(id)}`, controller.signal)
        .then(value => {
          if (!active) return
          setRateSet(value)
          if (mode === 'view' && value.status === 'DRAFT') setCurrenciesLoading(true)
        })
        .catch(reasonValue => { if (active && !(reasonValue instanceof Error && reasonValue.name === 'AbortError')) setError(cashFlowRequestError(reasonValue)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [id, mode, retry])

  useEffect(() => {
    const shouldLoad = (mode === 'view' && rateSet?.status === 'DRAFT') || (mode !== 'view' && canManage && (mode === 'create' || rateSet?.status === 'DRAFT'))
    const controller = new AbortController()
    let active = true
    const timer = window.setTimeout(() => {
      if (!shouldLoad) { setCurrenciesLoading(false); return }
      setCurrenciesLoading(true); setCurrenciesError(null)
      void getJson<ReportingRateCurrency[]>(`${api}/reporting-rate-currencies`, controller.signal)
        .then(value => { if (active) setCurrencies(value) })
        .catch(reasonValue => { if (active && !(reasonValue instanceof Error && reasonValue.name === 'AbortError')) setCurrenciesError(cashFlowRequestError(reasonValue)) })
        .finally(() => { if (active) setCurrenciesLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [canManage, mode, rateSet?.status, retry])

  async function runCommand(kind: 'publish' | 'correction') {
    if (!rateSet || !reason.trim() || commandBusy) return
    setCommandBusy(true); setCommandError(null)
    const command = { expectedVersion: rateSet.version, reason: reason.trim() }
    const attemptRef = kind === 'publish' ? publishAttempt : correctionAttempt
    const attempt = stableClientRequestAttempt(attemptRef.current, JSON.stringify(command), newClientRequestId)
    attemptRef.current = attempt
    try {
      const result = await sendJson<ReportingRateSet>(`${api}/reporting-rate-sets/${rateSet.id}/${kind === 'publish' ? 'publish' : 'corrections'}`, 'POST', { clientRequestId: attempt.clientRequestId, ...command })
      setDialog(null); setReason('')
      if (kind === 'correction') navigate(reportingRateSetPath(result.id, 'edit'), { replace: true })
      else setRateSet(result)
    } catch (reasonValue) {
      const requestError = cashFlowRequestError(reasonValue)
      setCommandError(requestError)
      if (kind === 'publish' && cashFlowFieldHasError(requestError, 'rates')) setRetry(value => value + 1)
      if (!retainClientRequestAttempt(requestError)) attemptRef.current = null
    }
    finally { setCommandBusy(false) }
  }

  const editorAllowed = mode !== 'view' && canRenderReportingRateEditor(mode, rateSet?.status, canManage)
  const publishedEdit = mode === 'edit' && rateSet?.status === 'PUBLISHED'
  const missingRateCurrencies = useMemo(() => reportingRateMissingCurrencies(currencies, rateSet?.rates ?? []), [currencies, rateSet?.rates])
  const title = mode === 'create' ? t('cashFlow.reportingRates.createTitle') : mode === 'edit' && !publishedEdit ? t('cashFlow.reportingRates.editTitle') : t('cashFlow.reportingRates.objectTitle')
  const pageActions = mode === 'view' && rateSet && canManage
    ? rateSet.status === 'DRAFT'
      ? <><Link className="button button--secondary" to={reportingRateSetPath(rateSet.id, 'edit')}><FilePenLine aria-hidden="true" />{t('common.edit')}</Link><button className="button button--primary" type="button" onClick={() => { publishAttempt.current = null; setCommandError(null); setDialog('publish') }}><Send aria-hidden="true" />{t('cashFlow.reportingRates.publish')}</button></>
      : <button className="button button--secondary" type="button" onClick={() => { correctionAttempt.current = null; setCommandError(null); setDialog('correction') }}><Copy aria-hidden="true" />{t('cashFlow.reportingRates.createCorrection')}</button>
    : undefined

  return <main className="cash-flow-page cash-flow-reporting-rates">
    <PageHeader eyebrow={t('cashFlow.reportingRates.eyebrow')} title={title} subtitle={t('cashFlow.reportingRates.objectHint')} backTo="/cash-flow/reporting-rates" backLabel={t('common.backToList')}
      status={rateSet && <RateSetStatus status={rateSet.status} />} actions={pageActions} />
    {mode !== 'view' && !canManage && <SectionFailure error={{ status: 403, kind: 'forbidden', code: 'CASH_FLOW_FORBIDDEN', fieldErrors: {} }} />}
    {error && <SectionFailure error={error} onRetry={() => setRetry(value => value + 1)} />}
    {currenciesError && <SectionFailure error={currenciesError} compact onRetry={() => setRetry(value => value + 1)} />}
    {commandError && !dialog && <SectionFailure error={commandError} compact />}
    {(canManage || mode === 'view') && !error && (loading || currenciesLoading) && <Section title={t('cashFlow.reportingRates.rateTable')}><LoadingState label={t('common.loading')} /></Section>}
    {editorAllowed && !error && !currenciesError && !loading && !currenciesLoading && (mode === 'create' || rateSet) && <ReportingRateEditorForm mode={mode} source={rateSet} currencies={currencies} language={language} onSaved={saved => navigate(reportingRateSetPath(saved.id), { replace: true })} />}
    {!error && !loading && !currenciesLoading && (mode === 'view' || publishedEdit) && rateSet && <ReportingRateDisplay rateSet={rateSet} language={language} currencies={rateSet.status === 'DRAFT' && !currenciesError ? currencies : undefined} />}
    <ReasonDialog kind={dialog} reason={reason} busy={commandBusy} error={commandError} missingCurrencyCodes={missingRateCurrencies.map(currency => currency.code)} onReasonChange={setReason} onCancel={() => { setDialog(null); setReason(''); setCommandError(null) }} onConfirm={() => dialog && void runCommand(dialog)} />
  </main>
}

export function ReportingRateEditorForm({ mode, source, currencies, language, onSaved }: {
  mode: 'create' | 'edit'; source?: ReportingRateSet; currencies: ReportingRateCurrency[]; language: string; onSaved: (value: ReportingRateSet) => void
}) {
  const { t } = useTranslation()
  const [effectiveDate, setEffectiveDate] = useState(source?.effectiveDate ?? tehranTodayIso())
  const [changeReason, setChangeReason] = useState(source?.changeReason ?? '')
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(source?.rates.map(rate => [rate.currencyId, rate.unitsPerUsd]) ?? []))
  const [touched, setTouched] = useState<Set<string>>(new Set())
  const [attempted, setAttempted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [copying, setCopying] = useState(false)
  const [copyEmpty, setCopyEmpty] = useState(false)
  const [copiedFrom, setCopiedFrom] = useState<ReportingRateSet | null>(null)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [dirty, setDirty] = useState(false)
  const [discardRequested, setDiscardRequested] = useState(false)
  const requestAttempt = useRef<StableClientRequestAttempt | null>(null)
  const [submittedCurrencyIds, setSubmittedCurrencyIds] = useState<string[]>([])
  const formId = useId()
  const navigate = useNavigate()
  const effectiveDateLocked = reportingRateEffectiveDateIsLocked(source?.supersedesRateSetId)
  const cancelDestination = source ? reportingRateSetPath(source.id) : '/cash-flow/reporting-rates'

  const visibleCurrencies = useMemo(() => reportingRateCurrenciesForEditor(currencies), [currencies])
  const editableCurrencyIds = useMemo(() => visibleCurrencies.filter(currency => !currency.isBaseCurrency).map(currency => currency.id), [visibleCurrencies])
  const invalidIds = useMemo(() => new Set(visibleCurrencies.filter(currency => !currency.isBaseCurrency && values[currency.id]?.trim() && !isExactDecimalInput(values[currency.id].trim())).map(currency => currency.id)), [values, visibleCurrencies])
  const serverInvalidIds = useMemo(() => reportingRateServerInvalidIds(error, submittedCurrencyIds, editableCurrencyIds, values), [editableCurrencyIds, error, submittedCurrencyIds, values])

  async function copyLatest() {
    setCopying(true); setError(null); setCopiedFrom(null); setCopyEmpty(false)
    try {
      const current = await getOptionalJson<ReportingRateSet>(`${api}/reporting-rate-sets/current?${new URLSearchParams({ asOfDate: effectiveDate })}`)
      if (!current) { setCopyEmpty(true); return }
      setValues(previous => mergeCopiedReportingRateValues(previous, editableCurrencyIds, current.rates))
      setCopiedFrom(current); setDirty(true)
    } catch (reasonValue) { setError(cashFlowRequestError(reasonValue)) }
    finally { setCopying(false) }
  }

  async function submit(event: FormEvent) {
    event.preventDefault(); setAttempted(true); setError(null)
    if (invalidIds.size > 0 || !effectiveDate) return
    const rates = visibleCurrencies.filter(currency => !currency.isBaseCurrency && values[currency.id]?.trim()).map(currency => ({ currencyId: currency.id, unitsPerUsd: values[currency.id].trim() }))
    setSubmittedCurrencyIds(rates.map(rate => rate.currencyId))
    const command = { effectiveDate, ...(mode === 'edit' ? { expectedVersion: source!.version } : {}), changeReason: changeReason.trim() || null, rates }
    const attempt = stableClientRequestAttempt(requestAttempt.current, JSON.stringify(command), newClientRequestId)
    requestAttempt.current = attempt
    setSaving(true)
    try {
      const result = mode === 'create'
        ? await sendJson<ReportingRateSet>(`${api}/reporting-rate-sets`, 'POST', { clientRequestId: attempt.clientRequestId, ...command })
        : await sendJson<ReportingRateSet>(`${api}/reporting-rate-sets/${source!.id}`, 'PUT', { clientRequestId: attempt.clientRequestId, ...command })
      setDirty(false); onSaved(result)
    } catch (reasonValue) {
      const requestError = cashFlowRequestError(reasonValue)
      setError(requestError)
      if (!retainClientRequestAttempt(requestError)) requestAttempt.current = null
    }
    finally { setSaving(false) }
  }

  function cancel() {
    if (dirty) setDiscardRequested(true)
    else navigate(cancelDestination)
  }

  function discardAndLeave() {
    setDirty(false)
    setDiscardRequested(false)
    navigate(cancelDestination)
  }

  return <>
    <UnsavedChangesGuard when={dirty && !saving} />
    {error && <SectionFailure error={error} compact />}
    <form id={formId} className="cash-rate-form" onSubmit={submit}>
      <Section title={t('cashFlow.reportingRates.headerTitle')} description={t('cashFlow.reportingRates.headerHint')} actions={<>
        {!effectiveDateLocked && <button className="button button--secondary" type="button" disabled={copying || !effectiveDate} onClick={() => void copyLatest()}><Copy aria-hidden="true" />{copying ? t('common.loading') : t('cashFlow.reportingRates.copyLatest')}</button>}
        <button className="button button--primary" type="submit" disabled={saving}>{saving ? t('common.saving') : t('common.saveDraft')}</button>
        <button className="button" type="button" onClick={cancel}>{t('common.cancel')}</button>
      </>}>
        <div className="cash-rate-header-grid">
          {effectiveDateLocked
            ? <FormField label={t('cashFlow.reportingRates.effectiveDate')} hint={t('cashFlow.reportingRates.correctionDateLocked')}><div className="cash-rate-date-locked"><LockKeyhole aria-hidden="true" /><DualDate value={effectiveDate} language={language} /></div></FormField>
            : <DualDateFilter label={t('cashFlow.reportingRates.effectiveDate')} value={effectiveDate} language={language} surface="reporting-rates" externalError={cashFlowFieldHasError(error, 'effectiveDate') ? t('cashFlow.reportingRates.effectiveDateError') : undefined} onChange={value => { setEffectiveDate(value); setDirty(true); setCopiedFrom(null); setCopyEmpty(false) }} />}
          <FormField label={t('cashFlow.reportingRates.changeReason')} hint={t('cashFlow.reportingRates.changeReasonHint')} error={cashFlowFieldHasError(error, 'changeReason') ? t('cashFlow.reportingRates.changeReasonError') : undefined}><textarea rows={2} value={changeReason} onChange={event => { setChangeReason(event.target.value); setDirty(true) }} /></FormField>
        </div>
        {copiedFrom && <MessageStrip tone="positive">{t('cashFlow.reportingRates.copiedFrom', { revision: copiedFrom.revision })} <DualDate value={copiedFrom.effectiveDate} language={language} /></MessageStrip>}
        {copyEmpty && <MessageStrip tone="information">{t('cashFlow.reportingRates.noRateToCopy')}</MessageStrip>}
        {!copiedFrom && mode === 'create' && <p className="cash-rate-copy-hint">{t('cashFlow.reportingRates.copyHint')}</p>}
      </Section>
      <Section title={t('cashFlow.reportingRates.rateTable')} description={t('cashFlow.reportingRates.rateTableHint')}>
        <ReportingRateEditorTable currencies={visibleCurrencies} values={values} language={language} attempted={attempted} touched={touched} serverInvalidIds={serverInvalidIds}
          onBlur={currencyId => setTouched(previous => new Set(previous).add(currencyId))}
          onChange={(currencyId, value) => { setValues(previous => ({ ...previous, [currencyId]: normalizeReportingRateInput(value) })); setDirty(true) }} />
      </Section>
    </form>
    <ConfirmationDialog open={discardRequested} title={t('common.discardTitle')} description={t('common.discardDescription')} confirmLabel={t('common.discard')} cancelLabel={t('common.cancel')} onConfirm={discardAndLeave} onCancel={() => setDiscardRequested(false)} />
  </>
}

export function ReportingRateEditorTable({ currencies, values, language, attempted, touched, serverInvalidIds, onChange, onBlur }: {
  currencies: ReportingRateCurrency[]; values: Record<string, string>; language: string; attempted: boolean; touched: Set<string>; serverInvalidIds: Set<string>; onChange: (currencyId: string, value: string) => void; onBlur: (currencyId: string) => void
}) {
  const { t } = useTranslation()
  return <DataTable headers={[t('cashFlow.reportingRates.currency'), t('cashFlow.reportingRates.formula'), t('cashFlow.reportingRates.unitsPerUsd'), t('cashFlow.reportingRates.rowStatus')]}
    loading={false} empty={!currencies.length} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.reportingRates.noCurrencies')} emptyDescription={t('cashFlow.reportingRates.noCurrenciesHint')} noResultTitle={t('common.noResultsTitle')}>
    <>{currencies.map(currency => {
      const value = currency.isBaseCurrency ? '1' : values[currency.id] ?? ''
      const invalid = !currency.isBaseCurrency && Boolean(value.trim()) && !isExactDecimalInput(value.trim())
      const showError = ((attempted || touched.has(currency.id)) && invalid) || serverInvalidIds.has(currency.id)
      const inputId = `cash-rate-${currency.id}`
      const errorId = `${inputId}-error`
      return <tr key={currency.id} className={showError ? 'cash-rate-row--invalid' : ''}>
        <td><strong>{localizedName({ nameFa: currency.nameFa, nameEn: currency.nameEn }, language)}</strong><small><bdi dir="ltr">{currency.code}</bdi></small>{!currency.isActive && !currency.isBaseCurrency && <StatusBadge text={t('common.inactive')} tone="neutral" />}</td>
        <td><bdi dir="ltr">1 USD =</bdi></td>
        <td className="cash-rate-value-cell">{currency.isBaseCurrency
          ? <span className="cash-rate-locked"><LockKeyhole aria-hidden="true" /><strong dir="ltr">1 USD</strong><small>{t('cashFlow.reportingRates.baseLocked')}</small></span>
          : <><label className="sr-only" htmlFor={inputId}>{t('cashFlow.reportingRates.inputLabel', { currency: currency.code })}</label><div className="cash-rate-input"><input id={inputId} dir="ltr" inputMode="decimal" autoComplete="off" value={value} placeholder="0" aria-invalid={showError} aria-describedby={showError ? errorId : undefined} onChange={event => onChange(currency.id, event.target.value)} onBlur={() => onBlur(currency.id)} /><bdi dir="ltr">{currency.code}</bdi></div>{showError && <small id={errorId} className="ui-field-error" role="alert">{t(value.trim() ? 'cashFlow.reportingRates.invalidRate' : 'cashFlow.reportingRates.requiredRate')}</small>}</>}
        </td>
        <td>{currency.isBaseCurrency ? <StatusBadge text={t('cashFlow.reportingRates.fixed')} tone="neutral" /> : value.trim() && !invalid ? <StatusBadge text={t('cashFlow.reportingRates.entered')} tone="positive" /> : <StatusBadge text={t('cashFlow.reportingRates.notEntered')} tone="warning" />}</td>
      </tr>
    })}</>
  </DataTable>
}

function ReportingRateDisplay({ rateSet, language, currencies }: { rateSet: ReportingRateSet; language: string; currencies?: ReportingRateCurrency[] }) {
  const { t } = useTranslation()
  const enteredRates = new Map(rateSet.rates.map(rate => [rate.currencyId, rate]))
  const rows = [{ currencyId: rateSet.baseCurrencyId, currencyCode: rateSet.baseCurrencyCode, currencyNameFa: 'دلار آمریکا', currencyNameEn: 'US Dollar', currencyIsActive: true, unitsPerUsd: '1' as string | null }, ...(currencies
    ? reportingRateCurrenciesForEditor(currencies).filter(currency => !currency.isBaseCurrency).map(currency => enteredRates.get(currency.id) ?? {
      currencyId: currency.id, currencyCode: currency.code, currencyNameFa: currency.nameFa, currencyNameEn: currency.nameEn,
      currencyIsActive: currency.isActive, unitsPerUsd: null,
    })
    : rateSet.rates)]
  return <>
    {rateSet.status === 'PUBLISHED' && <MessageStrip tone="information"><LockKeyhole aria-hidden="true" />{t('cashFlow.reportingRates.publishedImmutable')}</MessageStrip>}
    <Section title={t('cashFlow.reportingRates.metadata')} description={t('cashFlow.reportingRates.provenanceHint')}>
      <DefinitionGrid>
        <Definition label={t('cashFlow.reportingRates.effectiveDate')}><DualDate value={rateSet.effectiveDate} language={language} /></Definition>
        <Definition label={t('cashFlow.reportingRates.revision')}>{formatOperationalNumber(rateSet.revision, language)}</Definition>
        <Definition label={t('common.status')}><RateSetStatus status={rateSet.status} /></Definition>
        <Definition label={t('cashFlow.reportingRates.createdAt')}><bdi>{formatBusinessDateTime(rateSet.createdAtUtc, language)}</bdi></Definition>
        <Definition label={t('cashFlow.reportingRates.updatedAt')}><bdi>{formatBusinessDateTime(rateSet.updatedAtUtc, language)}</bdi></Definition>
        {rateSet.publishedAtUtc && <Definition label={t('cashFlow.reportingRates.publishedAt')}><bdi>{formatBusinessDateTime(rateSet.publishedAtUtc, language)}</bdi></Definition>}
        <Definition label={t('cashFlow.reportingRates.createdBy')}>{rateSet.createdByIdentifier ?? '—'}</Definition>
        <Definition label={t('cashFlow.reportingRates.publishedBy')}>{rateSet.publishedByIdentifier ?? '—'}</Definition>
        <Definition label={t('cashFlow.reportingRates.reason')}>{rateSet.publishReason ?? rateSet.changeReason ?? '—'}</Definition>
      </DefinitionGrid>
    </Section>
    <Section title={t('cashFlow.reportingRates.rateTable')} description={t('cashFlow.reportingRates.rateTableHint')}>
      <DataTable headers={[t('cashFlow.reportingRates.currency'), t('cashFlow.reportingRates.formula'), t('cashFlow.reportingRates.value'), t('common.status')]} loading={false} empty={!rows.length} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.reportingRates.noRates')} noResultTitle={t('common.noResultsTitle')}>
        <>{rows.map(rate => <tr key={rate.currencyId}>
          <td><strong>{localizedName({ nameFa: rate.currencyNameFa, nameEn: rate.currencyNameEn }, language)}</strong><small><bdi dir="ltr">{rate.currencyCode}</bdi></small></td>
          <td><bdi dir="ltr">1 USD =</bdi></td>
          <td className="ui-number"><strong className={rate.unitsPerUsd == null ? 'cash-flow-value-unavailable' : undefined}>{rate.unitsPerUsd == null ? t('cashFlow.reportingRates.notEntered') : formatReportingRate(rate.unitsPerUsd, language)}</strong>{rate.unitsPerUsd != null && <small><bdi dir="ltr">{rate.currencyCode}</bdi></small>}</td>
          <td>{rate.unitsPerUsd == null ? <StatusBadge text={t('cashFlow.reportingRates.notEntered')} tone="warning" /> : rate.currencyIsActive ? <StatusBadge text={t('common.active')} tone="positive" /> : <StatusBadge text={t('common.inactive')} tone="neutral" />}</td>
        </tr>)}</>
      </DataTable>
    </Section>
  </>
}

function ReasonDialog({ kind, reason, busy, error, missingCurrencyCodes, onReasonChange, onCancel, onConfirm }: { kind: 'publish' | 'correction' | null; reason: string; busy: boolean; error: CashFlowRequestError | null; missingCurrencyCodes: string[]; onReasonChange: (value: string) => void; onCancel: () => void; onConfirm: () => void }) {
  const { t } = useTranslation()
  if (!kind) return null
  return <ConfirmationDialog open title={kind === 'publish' ? t('cashFlow.reportingRates.publishTitle') : t('cashFlow.reportingRates.correctionTitle')} description={kind === 'publish' ? t('cashFlow.reportingRates.publishHint') : t('cashFlow.reportingRates.correctionHint')} confirmLabel={busy ? t('common.saving') : kind === 'publish' ? t('cashFlow.reportingRates.publish') : t('cashFlow.reportingRates.createCorrection')} cancelLabel={t('common.cancel')} tone="normal" confirmDisabled={busy || !reason.trim()} onConfirm={onConfirm} onCancel={onCancel}>
    {error && kind === 'publish' && cashFlowFieldHasError(error, 'rates') && <MessageStrip tone="warning">{t('cashFlow.reportingRates.publishRatesIncomplete')}{missingCurrencyCodes.length > 0 && <small><bdi dir="ltr">{missingCurrencyCodes.join(' · ')}</bdi></small>}</MessageStrip>}
    {error && !cashFlowFieldHasError(error, 'reason') && !(kind === 'publish' && cashFlowFieldHasError(error, 'rates')) && <SectionFailure error={error} compact />}
    <FormField label={t('cashFlow.reportingRates.reason')} required error={error && cashFlowFieldHasError(error, 'reason') ? t('cashFlow.reportingRates.reasonError') : undefined}><textarea autoFocus rows={3} value={reason} onChange={event => onReasonChange(event.target.value)} /></FormField>
  </ConfirmationDialog>
}

function RateSetStatus({ status }: { status: FxRateSetStatus }) {
  const { t } = useTranslation()
  return <StatusBadge text={t(`cashFlow.reportingRates.status.${status}`)} tone={status === 'PUBLISHED' ? 'positive' : 'information'} />
}

function newClientRequestId() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID()
  const bytes = new Uint8Array(16)
  if (globalThis.crypto?.getRandomValues) globalThis.crypto.getRandomValues(bytes)
  else for (let index = 0; index < bytes.length; index += 1) bytes[index] = Math.floor(Math.random() * 256)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const value = [...bytes].map(byte => byte.toString(16).padStart(2, '0')).join('')
  return `${value.slice(0, 8)}-${value.slice(8, 12)}-${value.slice(12, 16)}-${value.slice(16, 20)}-${value.slice(20)}`
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal, cache: 'no-store' })
  if (!response.ok) throw await readCashFlowApiError(response)
  return response.json() as Promise<T>
}

async function getOptionalJson<T>(url: string): Promise<T | null> {
  const response = await fetch(url, { cache: 'no-store' })
  if (response.status === 404 || response.status === 204) return null
  if (!response.ok) throw await readCashFlowApiError(response)
  return response.json() as Promise<T>
}

async function sendJson<T>(url: string, method: 'POST' | 'PUT', body: unknown): Promise<T> {
  const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  if (!response.ok) throw await readCashFlowApiError(response)
  return response.json() as Promise<T>
}
