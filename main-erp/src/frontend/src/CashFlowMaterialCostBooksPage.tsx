import { FormEvent, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Copy, Eye, FilePenLine, Plus, RefreshCw, Save, Send, Trash2, X } from 'lucide-react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { apiRoot } from './apiClient'
import { useAuth } from './AuthContext'
import { DualDate, DualDateFilter, SectionFailure } from './CashFlowPage'
import {
  cashFlowFieldHasError,
  cashFlowRequestError,
  formatCashAmount,
  formatCashUnitPrice,
  positiveCashFlowPage,
  readCashFlowApiError,
  retainClientRequestAttempt,
  stableClientRequestAttempt,
  tehranTodayIso,
  type CashFlowRequestError,
  type StableClientRequestAttempt,
} from './cashFlowUi'
import {
  buildMaterialCostBookOptionsQuery,
  buildMaterialCostBookQuery,
  findExactPriceUnit,
  isPositiveCostBookDecimal,
  materialCostBookItemsPath,
  materialCostBookRevisionPath,
  normalizeCostBookDecimalInput,
  preservePagedSelection,
  previewNormalizedUnitPrice,
  type MaterialCostBookCurrencyOption,
  type MaterialCostBookItemOption,
  type MaterialCostBookItemOptionPage,
  type MaterialCostBookLine,
  type MaterialCostBookLinePage,
  type MaterialCostBookPage,
  type MaterialCostBookRevision,
  type MaterialCostBookStatus,
  type MaterialCostBookSupplierOptionPage,
} from './cashFlowMaterialCostingUi'
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

export type MaterialCostBooksView = 'list' | 'create' | 'view' | 'edit'

export default function CashFlowMaterialCostBooksPage({ view }: { view: MaterialCostBooksView }) {
  return view === 'list' ? <MaterialCostBookList /> : <MaterialCostBookObject mode={view} />
}

function MaterialCostBookList() {
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [params, setParams] = useSearchParams()
  const page = positiveCashFlowPage(params.get('page'))
  const search = params.get('search') ?? ''
  const statusParam = params.get('status')
  const status: MaterialCostBookStatus | '' = statusParam === 'DRAFT' || statusParam === 'PUBLISHED' ? statusParam : ''
  const [searchDraft, setSearchDraft] = useState(search)
  const [data, setData] = useState<MaterialCostBookPage>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [retry, setRetry] = useState(0)
  const { has } = useAuth()

  useEffect(() => { const timer = window.setTimeout(() => setSearchDraft(search), 0); return () => window.clearTimeout(timer) }, [search])
  useEffect(() => {
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      void getJson<MaterialCostBookPage>(`${api}/material-cost-books?${buildMaterialCostBookQuery({ page, pageSize, search, status })}`, controller.signal)
        .then(value => { if (active) setData(value) })
        .catch(reason => { if (active && !isAbort(reason)) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [page, retry, search, status])

  function update(values: Record<string, string | number>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(values)) {
      if (value === '') next.delete(key)
      else next.set(key, String(value))
    }
    setParams(next)
  }

  return <main className="cash-flow-page cash-material-books">
    <PageHeader eyebrow={t('cashFlow.materialCostBooks.eyebrow')} title={t('cashFlow.materialCostBooks.title')} subtitle={t('cashFlow.materialCostBooks.subtitle')}
      actions={has('cash_flow.material_cost_book.manage') && <Link className="button button--primary" to="/cash-flow/material-cost-books/new"><Plus aria-hidden="true" />{t('cashFlow.materialCostBooks.newBook')}</Link>} />
    <MessageStrip>{t('cashFlow.materialCostBooks.boundary')}</MessageStrip>
    <form onSubmit={event => { event.preventDefault(); update({ search: searchDraft.trim(), page: 1 }) }}>
      <FilterBar label={t('common.filterBar')}>
        <FormField label={t('common.search')}><input value={searchDraft} placeholder={t('cashFlow.materialCostBooks.searchPlaceholder')} onChange={event => setSearchDraft(event.target.value)} /></FormField>
        <FormField label={t('common.status')}><select value={status} onChange={event => update({ status: event.target.value, page: 1 })}><option value="">{t('common.all')}</option><option value="DRAFT">{t('cashFlow.materialCostBooks.status.DRAFT')}</option><option value="PUBLISHED">{t('cashFlow.materialCostBooks.status.PUBLISHED')}</option></select></FormField>
        <button className="button button--primary" type="submit">{t('common.search')}</button>
        <button className="button button--quiet" type="button" onClick={() => setRetry(value => value + 1)}><RefreshCw aria-hidden="true" />{t('common.refresh')}</button>
      </FilterBar>
    </form>
    {error && <SectionFailure error={error} onRetry={() => setRetry(value => value + 1)} />}
    {!error && <Section title={t('cashFlow.materialCostBooks.listTitle')} description={t('cashFlow.materialCostBooks.listHint')}><>
      <TableToolbar title={t('cashFlow.materialCostBooks.books')} count={data && t('common.count', { count: data.totalCount })} />
      <DataTable headers={[t('cashFlow.materialCostBooks.book'), t('cashFlow.materialCostBooks.effectiveDate'), t('cashFlow.materialCostBooks.revision'), t('common.status'), t('cashFlow.materialCostBooks.coverage'), t('cashFlow.materialCostBooks.updated'), t('common.actions')]}
        loading={loading} empty={!data?.items.length} filtered={Boolean(search || status)} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.materialCostBooks.emptyTitle')} emptyDescription={t('cashFlow.materialCostBooks.emptyHint')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
        <>{data?.items.map(book => <tr key={book.revisionId}>
          <td><strong>{localizedName(book, language) || book.nameFa}</strong><small><bdi>{book.bookNumber}</bdi></small></td>
          <td><DualDate value={book.effectiveDate} language={language} /></td>
          <td className="ui-number">{formatOperationalNumber(book.revision, language)}</td>
          <td><BookStatus status={book.status} /></td>
          <td className="ui-number">{t('cashFlow.materialCostBooks.lineCount', { count: book.lineCount })}</td>
          <td><small><bdi>{formatBusinessDateTime(book.updatedAtUtc, language)}</bdi></small></td>
          <td><Link className="button button--compact" to={materialCostBookRevisionPath(book.revisionId)}><Eye aria-hidden="true" />{t('common.details')}</Link></td>
        </tr>)}</>
      </DataTable>
      {data && <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={next => update({ page: next })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(data.totalCount / pageSize)), count: data.totalCount })} />}
    </></Section>}
  </main>
}

function MaterialCostBookObject({ mode }: { mode: Exclude<MaterialCostBooksView, 'list'> }) {
  const { revisionId } = useParams()
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const navigate = useNavigate()
  const { has } = useAuth()
  const canManage = has('cash_flow.material_cost_book.manage')
  const canPublish = has('cash_flow.material_cost_book.publish')
  const [revision, setRevision] = useState<MaterialCostBookRevision>()
  const [loading, setLoading] = useState(mode !== 'create')
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [retry, setRetry] = useState(0)
  const [dialog, setDialog] = useState<'publish' | 'correction' | null>(null)
  const [reason, setReason] = useState('')
  const [commandBusy, setCommandBusy] = useState(false)
  const [commandError, setCommandError] = useState<CashFlowRequestError | null>(null)
  const commandAttempt = useRef<StableClientRequestAttempt | null>(null)

  useEffect(() => {
    if (mode === 'create' || !revisionId) return
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      void getJson<MaterialCostBookRevision>(`${api}/material-cost-book-revisions/${encodeURIComponent(revisionId)}`, controller.signal)
        .then(value => { if (active) setRevision(value) })
        .catch(reasonValue => { if (active && !isAbort(reasonValue)) setError(cashFlowRequestError(reasonValue)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [mode, retry, revisionId])

  async function runCommand(kind: 'publish' | 'correction') {
    if (!revision || !reason.trim()) return
    const signature = JSON.stringify({ kind, revisionId: revision.revisionId, version: revision.version, reason: reason.trim() })
    const attempt = stableClientRequestAttempt(commandAttempt.current, signature, newClientRequestId)
    commandAttempt.current = attempt; setCommandBusy(true); setCommandError(null)
    try {
      const result = await sendJson<MaterialCostBookRevision>(`${api}/material-cost-book-revisions/${revision.revisionId}/${kind === 'publish' ? 'publish' : 'corrections'}`, 'POST', { expectedVersion: revision.version, clientRequestId: attempt.clientRequestId, reason: reason.trim() })
      setDialog(null); setReason(''); commandAttempt.current = null
      if (kind === 'correction') navigate(materialCostBookRevisionPath(result.revisionId, 'edit'), { replace: true })
      else setRevision(result)
    } catch (reasonValue) {
      const failure = cashFlowRequestError(reasonValue); setCommandError(failure)
      if (!retainClientRequestAttempt(failure)) commandAttempt.current = null
    } finally { setCommandBusy(false) }
  }

  const editable = mode === 'create' || mode === 'edit' && revision?.status === 'DRAFT'
  const actions = revision ? <>
    {revision.status === 'DRAFT' && canManage && mode === 'view' && <>
      <Link className="button button--primary" to={materialCostBookItemsPath(revision.revisionId)}><Plus aria-hidden="true" />{t('cashFlow.materialCostBooks.addItems')}</Link>
      <Link className="button button--secondary" to={materialCostBookRevisionPath(revision.revisionId, 'edit')}><FilePenLine aria-hidden="true" />{t('cashFlow.materialCostBooks.editHeader')}</Link>
    </>}
    {revision.status === 'DRAFT' && canManage && mode === 'edit' && <a className="button button--primary" href="#material-cost-book-items"><Plus aria-hidden="true" />{t('cashFlow.materialCostBooks.addItems')}</a>}
    {revision.status === 'DRAFT' && canPublish && mode === 'view' && <button className="button button--secondary" type="button" onClick={() => { setReason(''); setCommandError(null); setDialog('publish') }}><Send aria-hidden="true" />{t('cashFlow.materialCostBooks.publish')}</button>}
    {revision.status === 'PUBLISHED' && canManage && mode === 'view' && <button className="button button--secondary" type="button" onClick={() => { setReason(''); setCommandError(null); setDialog('correction') }}><Copy aria-hidden="true" />{t('cashFlow.materialCostBooks.createCorrection')}</button>}
  </> : undefined

  return <main className="cash-flow-page cash-material-books">
    <PageHeader eyebrow={t('cashFlow.materialCostBooks.eyebrow')} title={mode === 'create' ? t('cashFlow.materialCostBooks.createTitle') : revision ? localizedName(revision, language) || revision.nameFa : t('cashFlow.materialCostBooks.objectTitle')}
      subtitle={revision ? `${revision.bookNumber} · ${t('cashFlow.materialCostBooks.revision')} ${formatOperationalNumber(revision.revision, language)}` : t('cashFlow.materialCostBooks.objectHint')}
      backTo="/cash-flow/material-cost-books" backLabel={t('common.backToList')} status={revision && <BookStatus status={revision.status} />} actions={actions} />
    {mode !== 'view' && !canManage && <SectionFailure error={{ status: 403, kind: 'forbidden', code: 'CASH_FLOW_FORBIDDEN', fieldErrors: {} }} />}
    {error && <SectionFailure error={error} onRetry={() => setRetry(value => value + 1)} />}
    {commandError && !dialog && <SectionFailure error={commandError} compact />}
    {loading && <Section title={t('cashFlow.materialCostBooks.book')}><LoadingState label={t('common.loading')} /></Section>}
    {!loading && !error && editable && canManage && <MaterialCostBookEditor mode={mode === 'create' ? 'create' : 'edit'} revision={revision} language={language} onSaved={saved => {
      if (mode === 'create') navigate(materialCostBookItemsPath(saved.revisionId), { replace: true })
      else setRevision(saved)
    }} />}
    {!loading && !error && !editable && revision && <MaterialCostBookDisplay revision={revision} language={language} onRevisionChanged={setRevision} />}
    <ReasonDialog kind={dialog} reason={reason} busy={commandBusy} error={commandError} lineCount={revision?.lineCount ?? 0} onChange={setReason} onCancel={() => { setDialog(null); setReason(''); setCommandError(null) }} onConfirm={() => dialog && void runCommand(dialog)} />
  </main>
}

function MaterialCostBookEditor({ mode, revision, language, onSaved }: { mode: 'create' | 'edit'; revision?: MaterialCostBookRevision; language: string; onSaved: (value: MaterialCostBookRevision) => void }) {
  const { t } = useTranslation()
  const [nameFa, setNameFa] = useState(revision?.nameFa ?? '')
  const [nameEn, setNameEn] = useState(revision?.nameEn ?? '')
  const [effectiveDate, setEffectiveDate] = useState(revision?.effectiveDate ?? tehranTodayIso())
  const [changeReason, setChangeReason] = useState(revision?.changeReason ?? '')
  const [saving, setSaving] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [workingRevision, setWorkingRevision] = useState(revision)
  const attemptRef = useRef<StableClientRequestAttempt | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!nameFa.trim()) { setError({ status: 400, kind: 'validation', code: 'CASH_FLOW_VALIDATION_FAILED', fieldErrors: { nameFa: [t('cashFlow.materialCostBooks.requiredName')] } }); return }
    const command = { nameFa: nameFa.trim(), nameEn: nameEn.trim() || null, effectiveDate, expectedVersion: workingRevision?.version, changeReason: changeReason.trim() || null }
    const attempt = stableClientRequestAttempt(attemptRef.current, JSON.stringify(command), newClientRequestId)
    attemptRef.current = attempt; setSaving(true); setError(null)
    try {
      const response = await sendJson<MaterialCostBookRevision>(mode === 'create' ? `${api}/material-cost-books` : `${api}/material-cost-book-revisions/${revision!.revisionId}`, mode === 'create' ? 'POST' : 'PUT', { ...command, clientRequestId: attempt.clientRequestId })
      setWorkingRevision(response); setDirty(false); attemptRef.current = null; onSaved(response)
    } catch (reason) {
      const failure = cashFlowRequestError(reason); setError(failure)
      if (!retainClientRequestAttempt(failure)) attemptRef.current = null
    } finally { setSaving(false) }
  }
  const set = (setter: (value: string) => void) => (value: string) => { setter(value); setDirty(true); attemptRef.current = null }
  return <>
    <UnsavedChangesGuard when={dirty} />
    {error && <SectionFailure error={error} compact />}
    <Section title={t('cashFlow.materialCostBooks.header')} description={mode === 'create' ? t('cashFlow.materialCostBooks.createHeaderHint') : t('cashFlow.materialCostBooks.headerHint')}>
      <form className="cash-material-book-form" onSubmit={submit}>
        <FormField required label={t('cashFlow.materialCostBooks.nameFa')} error={cashFlowFieldHasError(error, 'nameFa') ? t('cashFlow.materialCostBooks.requiredName') : undefined}><input value={nameFa} onChange={event => set(setNameFa)(event.target.value)} /></FormField>
        <FormField label={t('cashFlow.materialCostBooks.nameEn')} error={cashFlowFieldHasError(error, 'nameEn') ? t('cashFlow.materialCostBooks.invalidField') : undefined}><input dir="ltr" value={nameEn} onChange={event => set(setNameEn)(event.target.value)} /></FormField>
        <DualDateFilter label={t('cashFlow.materialCostBooks.effectiveDate')} value={effectiveDate} language={language} surface="material-cost-books" externalError={cashFlowFieldHasError(error, 'effectiveDate') ? t('cashFlow.dateInvalid') : undefined} onChange={value => { setEffectiveDate(value); setDirty(true) }} />
        <FormField label={t('cashFlow.materialCostBooks.changeReason')} error={cashFlowFieldHasError(error, 'changeReason') ? t('cashFlow.materialCostBooks.invalidField') : undefined}><textarea value={changeReason} onChange={event => set(setChangeReason)(event.target.value)} /></FormField>
        <div className="cash-material-book-form__actions"><button className="button button--primary" type="submit" disabled={saving}><Save aria-hidden="true" />{saving ? t('common.saving') : mode === 'create' ? t('cashFlow.materialCostBooks.saveAndAddItems') : t('common.save')}</button></div>
      </form>
    </Section>
    {mode === 'edit' && workingRevision && <MaterialCostBookLines revision={workingRevision} language={language} onRevision={setWorkingRevision} />}
  </>
}

function MaterialCostBookDisplay({ revision, language, onRevisionChanged }: { revision: MaterialCostBookRevision; language: string; onRevisionChanged: (value: MaterialCostBookRevision) => void }) {
  const { t } = useTranslation()
  return <>
    <Section title={t('cashFlow.materialCostBooks.header')}>
      <DefinitionGrid>
        <Definition label={t('cashFlow.materialCostBooks.bookNumber')}><bdi>{revision.bookNumber}</bdi></Definition>
        <Definition label={t('cashFlow.materialCostBooks.effectiveDate')}><DualDate value={revision.effectiveDate} language={language} /></Definition>
        <Definition label={t('cashFlow.materialCostBooks.revision')}>{formatOperationalNumber(revision.revision, language)}</Definition>
        <Definition label={t('cashFlow.materialCostBooks.coverage')}>{t('cashFlow.materialCostBooks.lineCount', { count: revision.lineCount })}</Definition>
        <Definition label={t('cashFlow.materialCostBooks.updated')}>{formatBusinessDateTime(revision.updatedAtUtc, language)}</Definition>
        <Definition label={t('cashFlow.materialCostBooks.changeReason')}>{revision.changeReason || '—'}</Definition>
      </DefinitionGrid>
    </Section>
    <MaterialCostBookLines revision={revision} language={language} readOnly onRevision={onRevisionChanged} />
  </>
}

function MaterialCostBookLines({ revision, language, readOnly = false, onRevision }: { revision: MaterialCostBookRevision; language: string; readOnly?: boolean; onRevision?: (value: MaterialCostBookRevision) => void }) {
  const { t } = useTranslation()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [searchDraft, setSearchDraft] = useState('')
  const [data, setData] = useState<MaterialCostBookLinePage>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [reload, setReload] = useState(0)
  const [editor, setEditor] = useState<{ item: MaterialCostBookItemOption; line?: MaterialCostBookLine } | null>(null)
  const [candidateSearch, setCandidateSearch] = useState('')
  const [candidatePage, setCandidatePage] = useState(1)
  const [candidates, setCandidates] = useState<MaterialCostBookItemOptionPage>()
  const [candidateLoading, setCandidateLoading] = useState(false)
  const [candidateError, setCandidateError] = useState<CashFlowRequestError | null>(null)
  const [removeTarget, setRemoveTarget] = useState<MaterialCostBookLine | null>(null)
  const [removeReason, setRemoveReason] = useState('')
  const [removeBusy, setRemoveBusy] = useState(false)
  const removeAttemptRef = useRef<StableClientRequestAttempt | null>(null)

  useEffect(() => {
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setLoading(true); setError(null)
      void getJson<MaterialCostBookLinePage>(`${api}/material-cost-book-revisions/${revision.revisionId}/lines?${buildMaterialCostBookOptionsQuery({ page, pageSize, search })}`, controller.signal)
        .then(value => {
          if (!active) return
          setData(value)
          if (value.revisionVersion !== revision.version) {
            onRevision?.({ ...revision, version: value.revisionVersion, lineCount: value.totalCount })
          }
        })
        .catch(reason => { if (active && !isAbort(reason)) setError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [onRevision, page, reload, revision, search])

  useEffect(() => {
    if (readOnly) return
    const controller = new AbortController(); let active = true
    const timer = window.setTimeout(() => {
      setCandidateLoading(true); setCandidateError(null)
      void getJson<MaterialCostBookItemOptionPage>(`${api}/material-cost-books/item-options?${buildMaterialCostBookOptionsQuery({ page: candidatePage, pageSize: 10, search: candidateSearch })}`, controller.signal)
        .then(value => { if (active) setCandidates(value) })
        .catch(reason => { if (active && !isAbort(reason)) setCandidateError(cashFlowRequestError(reason)) })
        .finally(() => { if (active) setCandidateLoading(false) })
    }, 0)
    return () => { active = false; window.clearTimeout(timer); controller.abort() }
  }, [candidatePage, candidateSearch, readOnly, reload])

  async function editLine(line: MaterialCostBookLine) {
    try {
      const found = await getJson<MaterialCostBookItemOptionPage>(`${api}/material-cost-books/item-options?${buildMaterialCostBookOptionsQuery({ page: 1, pageSize: 25, search: line.itemCode })}`)
      const item = found.items.find(value => value.itemId === line.itemId)
      if (item) setEditor({ item, line })
    } catch (reason) { setError(cashFlowRequestError(reason)) }
  }

  async function removeLine() {
    if (!removeTarget || !removeReason.trim()) return
    const command = { expectedVersion: revision.version, reason: removeReason.trim() }
    const attempt = stableClientRequestAttempt(removeAttemptRef.current, JSON.stringify({ ...command, itemId: removeTarget.itemId }), newClientRequestId)
    removeAttemptRef.current = attempt; setRemoveBusy(true); setError(null)
    try {
      const response = await sendJson<MaterialCostBookRevision>(`${api}/material-cost-book-revisions/${revision.revisionId}/lines/${removeTarget.itemId}/remove`, 'POST', { ...command, clientRequestId: attempt.clientRequestId })
      removeAttemptRef.current = null; setRemoveTarget(null); setRemoveReason(''); onRevision?.(response); setReload(value => value + 1)
    } catch (reason) {
      const failure = cashFlowRequestError(reason); setError(failure)
      if (!retainClientRequestAttempt(failure)) removeAttemptRef.current = null
    } finally { setRemoveBusy(false) }
  }

  return <div id="material-cost-book-items" className="cash-material-book-items">
    {editor && <MaterialCostBookLineEditor revision={revision} item={editor.item} line={editor.line} language={language} onCancel={() => setEditor(null)} onSaved={nextRevision => { setEditor(null); onRevision?.(nextRevision); setReload(value => value + 1) }} />}
    {!readOnly && <Section title={t('cashFlow.materialCostBooks.addItems')} description={t('cashFlow.materialCostBooks.addItemsHint')}>
      <div className="cash-material-candidates">
        <FormField label={t('common.search')}><input value={candidateSearch} placeholder={t('cashFlow.materialCostBooks.itemSearchPlaceholder')} onChange={event => { setCandidateSearch(event.target.value); setCandidatePage(1) }} /></FormField>
        {candidateError && <SectionFailure error={candidateError} compact onRetry={() => setReload(value => value + 1)} />}
        <DataTable headers={[t('cashFlow.materialCostBooks.item'), t('cashFlow.materialCostBooks.baseUnit'), t('cashFlow.materialCostBooks.priceUnits'), t('common.actions')]}
          loading={candidateLoading} empty={!candidates?.items.length} filtered={Boolean(candidateSearch)} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.materialCostBooks.noCandidates')} noResultTitle={t('common.noResultsTitle')}>
          <>{candidates?.items.map(item => <tr key={item.itemId}><td><strong>{fieldName(item.itemNameFa, item.itemNameEn, language)}</strong><small><bdi>{item.itemCode}</bdi></small></td><td><bdi>{item.baseUnitCode}</bdi></td><td>{item.priceUnits.map(unit => unit.unitCode).join(' · ')}</td><td><button className="button button--compact" type="button" onClick={() => setEditor({ item })}><Plus aria-hidden="true" />{t('common.add')}</button></td></tr>)}</>
        </DataTable>
        {candidates && <Pagination page={candidatePage} pageSize={10} totalCount={candidates.totalCount} onPageChange={setCandidatePage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: candidatePage, pages: Math.max(1, Math.ceil(candidates.totalCount / 10)), count: candidates.totalCount })} />}
      </div>
    </Section>}
    <Section title={t('cashFlow.materialCostBooks.priceLines')} description={t('cashFlow.materialCostBooks.partialCoverage')}>
      <form className="cash-material-lines-search" onSubmit={event => { event.preventDefault(); setSearch(searchDraft.trim()); setPage(1) }}><FormField label={t('common.search')}><input value={searchDraft} onChange={event => setSearchDraft(event.target.value)} /></FormField><button className="button button--secondary" type="submit">{t('common.search')}</button></form>
      {error && <SectionFailure error={error} compact onRetry={() => setReload(value => value + 1)} />}
      <TableToolbar title={t('cashFlow.materialCostBooks.lines')} count={data && t('common.count', { count: data.totalCount })} />
      <DataTable headers={[t('cashFlow.materialCostBooks.item'), t('cashFlow.materialCostBooks.originalPrice'), t('cashFlow.materialCostBooks.pricingBasis'), t('cashFlow.materialCostBooks.normalizedPrice'), t('cashFlow.materialCostBooks.supplier'), t('cashFlow.materialCostBooks.source'), ...(readOnly ? [] : [t('common.actions')])]}
        loading={loading} empty={!data?.items.length} filtered={Boolean(search)} loadingLabel={t('common.loading')} emptyTitle={t('cashFlow.materialCostBooks.noLines')} emptyDescription={t('cashFlow.materialCostBooks.noLinesHint')} noResultTitle={t('common.noResultsTitle')}>
        <>{data?.items.map(line => <tr key={line.id}><td><strong>{fieldName(line.itemNameFa, line.itemNameEn, language)}</strong><small><bdi>{line.itemCode}</bdi></small></td><td className="ui-number"><strong>{formatCashAmount(line.originalPriceAmount, line.currencyDecimalPrecision, language)}</strong><small dir="ltr">{line.currencyCode}</small></td><td className="ui-number"><strong>{line.priceBasisQuantity}</strong><small>{line.priceUnitCode} × {line.priceUnitFactorToBase}</small></td><td className="ui-number cash-material-calculated"><strong>{formatCashUnitPrice(line.normalizedUnitPrice, line.currencyDecimalPrecision, language)}</strong><small dir="ltr">{line.currencyCode} / {line.itemBaseUnitCode}</small></td><td>{line.supplierName || '—'}{line.supplierNumber && <small><bdi>{line.supplierNumber}</bdi></small>}</td><td>{line.sourceReference || '—'}{line.notes && <small>{line.notes}</small>}</td>{!readOnly && <td><div className="inline-actions"><button className="button button--compact" type="button" onClick={() => void editLine(line)}><FilePenLine aria-hidden="true" />{t('common.edit')}</button><button className="button button--compact button--danger" type="button" onClick={() => { setRemoveTarget(line); setRemoveReason(''); setError(null) }}><Trash2 aria-hidden="true" />{t('common.delete')}</button></div></td>}</tr>)}</>
      </DataTable>
      {data && <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={setPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(data.totalCount / pageSize)), count: data.totalCount })} />}
    </Section>
    <ConfirmationDialog open={removeTarget !== null} title={t('cashFlow.materialCostBooks.removeLine')} description={t('cashFlow.materialCostBooks.removeLineHint')} confirmLabel={t('common.delete')} cancelLabel={t('common.cancel')} confirmDisabled={!removeReason.trim() || removeBusy} onConfirm={() => void removeLine()} onCancel={() => { setRemoveTarget(null); setRemoveReason(''); setError(null) }}><FormField required label={t('common.reason')}><textarea autoFocus value={removeReason} onChange={event => { setRemoveReason(event.target.value); removeAttemptRef.current = null }} /></FormField></ConfirmationDialog>
  </div>
}

function MaterialCostBookLineEditor({ revision, item, line, language, onCancel, onSaved }: { revision: MaterialCostBookRevision; item: MaterialCostBookItemOption; line?: MaterialCostBookLine; language: string; onCancel: () => void; onSaved: (revision: MaterialCostBookRevision) => void }) {
  const { t } = useTranslation()
  const [currencies, setCurrencies] = useState<MaterialCostBookCurrencyOption[]>([])
  const [suppliers, setSuppliers] = useState<MaterialCostBookSupplierOptionPage>()
  const [supplierSearch, setSupplierSearch] = useState(line?.supplierName ?? '')
  const [supplierPage, setSupplierPage] = useState(1)
  const [amount, setAmount] = useState(line?.originalPriceAmount ?? '')
  const [quantity, setQuantity] = useState(line?.priceBasisQuantity ?? '1')
  const [currencyId, setCurrencyId] = useState(line?.currencyId ?? '')
  const [unitId, setUnitId] = useState(line?.priceUnitId ?? item.baseUnitId)
  const [supplierId, setSupplierId] = useState(line?.supplierId ?? '')
  const [supplierLabel, setSupplierLabel] = useState(line?.supplierId
    ? [line.supplierName, line.supplierNumber].filter(Boolean).join(' — ')
    : '')
  const [sourceReference, setSourceReference] = useState(line?.sourceReference ?? '')
  const [notes, setNotes] = useState(line?.notes ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<CashFlowRequestError | null>(null)
  const [dirty, setDirty] = useState(false)
  const [removeOpen, setRemoveOpen] = useState(false)
  const [removeReason, setRemoveReason] = useState('')
  const attemptRef = useRef<StableClientRequestAttempt | null>(null)
  const removeAttemptRef = useRef<StableClientRequestAttempt | null>(null)
  const selectedUnit = findExactPriceUnit(item.priceUnits, unitId)
  const storedUnitUnavailable = Boolean(line && !selectedUnit)
  const selectedCurrency = currencies.find(value => value.currencyId === currencyId)
  const storedCurrencyUnavailable = Boolean(line && currencies.length > 0 && !selectedCurrency)
  const preview = selectedUnit ? previewNormalizedUnitPrice(amount, quantity, selectedUnit.factorToBase) : null
  const supplierOptions = suppliers?.items.map(supplier => ({
    id: supplier.supplierId,
    label: `${supplier.supplierName} — ${supplier.supplierNumber}`,
    isActive: supplier.isActive,
  })) ?? []
  const resolvedSupplier = supplierOptions.find(option => option.id === supplierId) ??
    (suppliers?.selected?.supplierId === supplierId ? {
      id: suppliers.selected.supplierId,
      label: `${suppliers.selected.supplierName} — ${suppliers.selected.supplierNumber}`,
      isActive: suppliers.selected.isActive,
    } : undefined)
  const visibleSupplierOptions = preservePagedSelection(supplierId, supplierOptions,
    resolvedSupplier ?? { id: supplierId, label: supplierLabel || t('cashFlow.materialCostBooks.selectedSupplierOutsidePage'), isActive: false })
  const storedSupplierUnavailable = Boolean(suppliers && supplierId &&
    (!resolvedSupplier || !resolvedSupplier.isActive))

  useEffect(() => {
    const controller = new AbortController()
    void getJson<MaterialCostBookCurrencyOption[]>(`${api}/material-cost-books/currency-options`, controller.signal)
      .then(setCurrencies)
      .catch(reason => { if (!isAbort(reason)) setError(cashFlowRequestError(reason)) })
    return () => controller.abort()
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    const query = buildMaterialCostBookOptionsQuery({ page: supplierPage, pageSize: 20, search: supplierSearch })
    if (supplierId) query.set('selectedSupplierId', supplierId)
    void getJson<MaterialCostBookSupplierOptionPage>(`${api}/material-cost-books/supplier-options?${query}`, controller.signal)
      .then(setSuppliers).catch(reason => { if (!isAbort(reason)) setError(cashFlowRequestError(reason)) })
    return () => controller.abort()
  }, [supplierId, supplierPage, supplierSearch])

  const set = (setter: (value: string) => void) => (value: string) => { setter(value); setDirty(true); attemptRef.current = null }
  async function save(event: FormEvent) {
    event.preventDefault()
    const normalizedAmount = normalizeCostBookDecimalInput(amount); const normalizedQuantity = normalizeCostBookDecimalInput(quantity)
    const fieldErrors: Record<string, string[]> = {}
    if (!isPositiveCostBookDecimal(normalizedAmount)) fieldErrors.originalPriceAmount = [t('cashFlow.materialCostBooks.invalidField')]
    if (!isPositiveCostBookDecimal(normalizedQuantity)) fieldErrors.priceBasisQuantity = [t('cashFlow.materialCostBooks.invalidField')]
    if (!selectedUnit) fieldErrors.priceUnitId = [t('cashFlow.materialCostBooks.invalidField')]
    if (!selectedCurrency) fieldErrors.currencyId = [t('cashFlow.materialCostBooks.invalidField')]
    if (storedSupplierUnavailable) fieldErrors.supplierId = [t('cashFlow.materialCostBooks.invalidField')]
    if (Object.keys(fieldErrors).length > 0) { setError({ status: 400, kind: 'validation', code: 'CASH_FLOW_VALIDATION_FAILED', fieldErrors }); return }
    const command = { currencyId, originalPriceAmount: normalizedAmount, priceBasisQuantity: normalizedQuantity, priceUnitId: unitId, supplierId: supplierId || null, sourceReference: sourceReference.trim() || null, notes: notes.trim() || null, expectedVersion: revision.version }
    const attempt = stableClientRequestAttempt(attemptRef.current, JSON.stringify(command), newClientRequestId); attemptRef.current = attempt
    setSaving(true); setError(null)
    try {
      await sendJson<{ line: MaterialCostBookLine; revisionVersion: number }>(`${api}/material-cost-book-revisions/${revision.revisionId}/lines/${item.itemId}`, 'PUT', { ...command, clientRequestId: attempt.clientRequestId })
      const latestRevision = await getJson<MaterialCostBookRevision>(`${api}/material-cost-book-revisions/${revision.revisionId}`)
      setDirty(false); attemptRef.current = null; onSaved(latestRevision)
    } catch (reason) { const failure = cashFlowRequestError(reason); setError(failure); if (!retainClientRequestAttempt(failure)) attemptRef.current = null }
    finally { setSaving(false) }
  }

  async function remove() {
    if (!line || !removeReason.trim()) return
    const command = { expectedVersion: revision.version, reason: removeReason.trim() }
    const attempt = stableClientRequestAttempt(removeAttemptRef.current, JSON.stringify(command), newClientRequestId)
    removeAttemptRef.current = attempt
    setSaving(true); setError(null)
    try {
      const response = await sendJson<MaterialCostBookRevision>(`${api}/material-cost-book-revisions/${revision.revisionId}/lines/${item.itemId}/remove`, 'POST', { ...command, clientRequestId: attempt.clientRequestId })
      setDirty(false); setRemoveOpen(false); removeAttemptRef.current = null; onSaved(response)
    } catch (reason) { const failure = cashFlowRequestError(reason); setError(failure); if (!retainClientRequestAttempt(failure)) removeAttemptRef.current = null }
    finally { setSaving(false) }
  }

  return <>
    <UnsavedChangesGuard when={dirty} />
    <Section title={`${t('cashFlow.materialCostBooks.priceLine')} — ${fieldName(item.itemNameFa, item.itemNameEn, language)}`} description={`${item.itemCode} · ${t('cashFlow.materialCostBooks.baseUnit')}: ${item.baseUnitCode}`}
      actions={<button className="button button--quiet" type="button" onClick={onCancel}><X aria-hidden="true" />{t('common.close')}</button>}>
      {error && <SectionFailure error={error} compact />}
      {(storedUnitUnavailable || storedCurrencyUnavailable || storedSupplierUnavailable) && <MessageStrip tone="warning">{t('cashFlow.materialCostBooks.unavailableSelection')}</MessageStrip>}
      <form className="cash-material-line-editor" onSubmit={save}>
        <FormField required label={t('cashFlow.materialCostBooks.originalPrice')} hint={t('cashFlow.materialCostBooks.exactInputHint')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'amount', 'originalPriceAmount')}><input className="cash-material-input" inputMode="decimal" dir="ltr" value={amount} onChange={event => set(setAmount)(normalizeCostBookDecimalInput(event.target.value))} /></FormField>
        <FormField required label={t('cashFlow.materialCostBooks.pricingQuantity')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'quantity', 'priceBasisQuantity')}><input className="cash-material-input" inputMode="decimal" dir="ltr" value={quantity} onChange={event => set(setQuantity)(normalizeCostBookDecimalInput(event.target.value))} /></FormField>
        <FormField required label={t('cashFlow.materialCostBooks.priceUnit')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'priceUnitId', 'priceConversion')}><select className="cash-material-input" value={unitId} onChange={event => set(setUnitId)(event.target.value)}>{storedUnitUnavailable && <option value={line!.priceUnitId} disabled>{line!.priceUnitCode} — {t('common.inactive')}</option>}{item.priceUnits.map(unit => <option key={unit.unitId} value={unit.unitId}>{fieldName(unit.unitNameFa, unit.unitNameEn, language)} ({unit.unitCode})</option>)}</select></FormField>
        <FormField required label={t('cashFlow.materialCostBooks.currency')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'currencyId')}><select className="cash-material-input" value={currencyId} onChange={event => set(setCurrencyId)(event.target.value)}><option value="">—</option>{storedCurrencyUnavailable && <option value={line!.currencyId} disabled>{line!.currencyCode} — {t('common.inactive')}</option>}{currencies.map(currency => <option key={currency.currencyId} value={currency.currencyId}>{currency.currencyCode} — {fieldName(currency.currencyNameFa, currency.currencyNameEn, language)}</option>)}</select></FormField>
        <div className="cash-material-supplier-picker"><FormField label={t('cashFlow.materialCostBooks.supplier')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'supplierId')}><input value={supplierSearch} placeholder={t('common.search')} onChange={event => { setSupplierSearch(event.target.value); setSupplierPage(1) }} /></FormField><select aria-label={t('cashFlow.materialCostBooks.supplier')} value={supplierId} onChange={event => { const next = event.target.value; set(setSupplierId)(next); setSupplierLabel(supplierOptions.find(option => option.id === next)?.label ?? '') }}><option value="">{t('common.none')}</option>{visibleSupplierOptions.map(supplier => <option key={supplier.id} value={supplier.id} disabled={!supplier.isActive}>{supplier.label}{!supplier.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select>{suppliers && <div className="cash-material-supplier-picker__paging"><button type="button" className="button button--quiet" disabled={supplierPage <= 1} onClick={() => setSupplierPage(value => value - 1)}>{t('common.previous')}</button><span>{t('common.page', { page: supplierPage, pages: Math.max(1, Math.ceil(suppliers.totalCount / 20)), count: suppliers.totalCount })}</span><button type="button" className="button button--quiet" disabled={supplierPage >= Math.ceil(suppliers.totalCount / 20)} onClick={() => setSupplierPage(value => value + 1)}>{t('common.next')}</button></div>}</div>
        <FormField label={t('cashFlow.materialCostBooks.source')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'sourceReference')}><input value={sourceReference} onChange={event => set(setSourceReference)(event.target.value)} /></FormField>
        <FormField className="cash-material-line-editor__wide" label={t('cashFlow.materialCostBooks.notes')} error={materialLineFieldError(error, t('cashFlow.materialCostBooks.invalidField'), 'notes')}><textarea value={notes} onChange={event => set(setNotes)(event.target.value)} /></FormField>
        <div className="cash-material-formula cash-material-line-editor__wide" aria-live="polite"><span>{t('cashFlow.materialCostBooks.formula')}</span><code dir="ltr">{amount || '—'} ÷ ({quantity || '—'} × {selectedUnit?.factorToBase ?? '—'})</code><strong dir="ltr">{preview ?? '—'} {currencies.find(value => value.currencyId === currencyId)?.currencyCode ?? ''} / {item.baseUnitCode}</strong><small>{t('cashFlow.materialCostBooks.backendAuthority')}</small></div>
        <div className="cash-material-line-editor__actions cash-material-line-editor__wide"><button className="button button--primary" disabled={saving} type="submit"><Save aria-hidden="true" />{saving ? t('common.saving') : t('common.save')}</button>{line && <button className="button button--danger" type="button" onClick={() => setRemoveOpen(true)}><Trash2 aria-hidden="true" />{t('common.delete')}</button>}</div>
      </form>
    </Section>
    <ConfirmationDialog open={removeOpen} title={t('cashFlow.materialCostBooks.removeLine')} description={t('cashFlow.materialCostBooks.removeLineHint')} confirmLabel={t('common.delete')} cancelLabel={t('common.cancel')} confirmDisabled={!removeReason.trim() || saving} onConfirm={() => void remove()} onCancel={() => setRemoveOpen(false)}><FormField required label={t('common.reason')}><textarea autoFocus value={removeReason} onChange={event => setRemoveReason(event.target.value)} /></FormField></ConfirmationDialog>
  </>
}

function BookStatus({ status }: { status: MaterialCostBookStatus }) {
  const { t } = useTranslation()
  return <StatusBadge text={t(`cashFlow.materialCostBooks.status.${status}`)} tone={status === 'PUBLISHED' ? 'positive' : 'information'} />
}

function materialLineFieldError(error: CashFlowRequestError | null, message: string, ...fields: string[]) {
  return fields.some(field => cashFlowFieldHasError(error, field)) ? message : undefined
}

function ReasonDialog({ kind, reason, busy, error, lineCount, onChange, onCancel, onConfirm }: { kind: 'publish' | 'correction' | null; reason: string; busy: boolean; error: CashFlowRequestError | null; lineCount: number; onChange: (value: string) => void; onCancel: () => void; onConfirm: () => void }) {
  const { t } = useTranslation()
  return <ConfirmationDialog open={kind !== null} tone="normal" title={kind === 'publish' ? t('cashFlow.materialCostBooks.publishTitle') : t('cashFlow.materialCostBooks.correctionTitle')} description={kind === 'publish' ? t('cashFlow.materialCostBooks.publishHint', { count: lineCount }) : t('cashFlow.materialCostBooks.correctionHint')} confirmLabel={kind === 'publish' ? t('cashFlow.materialCostBooks.publish') : t('cashFlow.materialCostBooks.createCorrection')} cancelLabel={t('common.cancel')} confirmDisabled={!reason.trim() || busy} onConfirm={onConfirm} onCancel={onCancel}>
    {error && <SectionFailure error={error} compact />}
    <FormField required label={t('common.reason')}><textarea autoFocus value={reason} onChange={event => onChange(event.target.value)} /></FormField>
  </ConfirmationDialog>
}

function newClientRequestId() { return crypto.randomUUID() }
function fieldName(nameFa: string, nameEn: string | null | undefined, language: string) { return language.startsWith('en') ? nameEn || nameFa : nameFa || nameEn || '—' }
function isAbort(reason: unknown) { return reason instanceof Error && reason.name === 'AbortError' }
async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> { const response = await fetch(url, { signal, cache: 'no-store' }); if (!response.ok) throw await readCashFlowApiError(response); return response.json() as Promise<T> }
async function sendJson<T>(url: string, method: 'POST' | 'PUT', body: unknown): Promise<T> { const response = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); if (!response.ok) throw await readCashFlowApiError(response); return response.json() as Promise<T> }
