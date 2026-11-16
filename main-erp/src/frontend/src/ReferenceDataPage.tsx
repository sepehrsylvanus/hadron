import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  ConfirmationDialog,
  DataTable,
  FilterBar,
  FormField,
  MessageStrip,
  PageHeader,
  Section,
  StatusBadge,
  TableToolbar,
} from './ui'
import {
  fetchReferenceTypesUntilAvailable,
  referenceDataProblem,
  type ReferenceDataType,
  usesGeneratedReferenceCode,
} from './referenceDataSupport'

type ReferenceItem = {
  id: string; code?: string; isoCode?: string; nameFa: string; nameEn: string; symbol?: string | null
  decimalPrecision?: number; isActive: boolean; sortOrder?: number; version: number
}
type EditorState = {
  id?: string; code: string; nameFa: string; nameEn: string; symbol: string
  decimalPrecision: number; sortOrder: number; version?: number
}

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const currencyType: ReferenceDataType = { id: 'currency', code: 'CURRENCY', nameFa: 'ارز', nameEn: 'Currency' }
const unitType: ReferenceDataType = { id: 'unit', code: 'UNIT', nameFa: 'واحد اندازه‌گیری', nameEn: 'Unit' }
const emptyEditor: EditorState = { code: '', nameFa: '', nameEn: '', symbol: '', decimalPrecision: 0, sortOrder: 0 }

export default function ReferenceDataPage() {
  const { t, i18n } = useTranslation()
  const english = i18n.resolvedLanguage === 'en'
  const [types, setTypes] = useState<ReferenceDataType[]>([])
  const [typesReady, setTypesReady] = useState(false)
  const [selectedCode, setSelectedCode] = useState('COUNTRY')
  const [items, setItems] = useState<ReferenceItem[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [search, setSearch] = useState('')
  const [editor, setEditor] = useState<EditorState | null>(null)
  const [confirmItem, setConfirmItem] = useState<ReferenceItem | null>(null)
  const [loading, setLoading] = useState(true)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const allTypes = useMemo(() => typesReady ? [...types, currencyType, unitType] : [], [types, typesReady])
  const selectedType = allTypes.find(type => type.code === selectedCode) ?? allTypes[0]
  const hasStandardCode = !usesGeneratedReferenceCode(selectedCode)
  const resourcePath = useCallback(() => selectedCode === 'CURRENCY' ? 'reference-data/currencies' : selectedCode === 'UNIT' ? 'reference-data/units' : `reference-data/types/${selectedCode}/values`, [selectedCode])

  const loadItems = useCallback(async () => {
    if (!typesReady) { setLoading(true); return }
    setLoading(true); setError('')
    try {
      const query = new URLSearchParams({ includeInactive: String(includeInactive) })
      if (search.trim()) query.set('search', search.trim())
      const response = await fetch(`${api}/${resourcePath()}?${query}`)
      if (!response.ok) throw new Error(await referenceDataProblem(response))
      setItems(await response.json() as ReferenceItem[])
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
    finally { setLoading(false) }
  }, [includeInactive, resourcePath, search, t, typesReady])

  useEffect(() => {
    const controller = new AbortController()
    void fetchReferenceTypesUntilAvailable(`${api}/reference-data/types`, controller.signal,
      message => setError(message)).then(rows => {
        if (controller.signal.aborted) return
        setTypes(rows); setTypesReady(true); setError('')
      }).catch(caught => {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : t('common.error'))
      })
    return () => controller.abort()
  }, [t])
  useEffect(() => { const timer = window.setTimeout(() => void loadItems(), 120); return () => window.clearTimeout(timer) }, [loadItems])

  async function submit(event: FormEvent) {
    event.preventDefault(); if (!editor) return
    const isEdit = Boolean(editor.id)
    const payload = selectedCode === 'CURRENCY'
      ? isEdit ? { nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, decimalPrecision: editor.decimalPrecision, expectedVersion: editor.version }
        : { isoCode: editor.code, nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, decimalPrecision: editor.decimalPrecision }
      : selectedCode === 'UNIT'
        ? isEdit ? { nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, sortOrder: editor.sortOrder, expectedVersion: editor.version }
          : { code: editor.code, nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, sortOrder: editor.sortOrder }
        : isEdit ? { nameFa: editor.nameFa, nameEn: editor.nameEn, sortOrder: editor.sortOrder, expectedVersion: editor.version }
          : { nameFa: editor.nameFa, nameEn: editor.nameEn, sortOrder: editor.sortOrder }
    const base = resourcePath().replace(/types\/[^/]+\/values$/, 'values')
    try {
      const response = await fetch(`${api}/${isEdit ? `${base}/${editor.id}` : resourcePath()}`, { method: isEdit ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!response.ok) throw new Error(await referenceDataProblem(response))
      setEditor(null); setMessage(t('common.saved')); await loadItems()
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
  }

  async function changeState() {
    if (!confirmItem) return
    const base = resourcePath().replace(/types\/[^/]+\/values$/, 'values')
    try {
      const response = await fetch(`${api}/${base}/${confirmItem.id}/${confirmItem.isActive ? 'deactivate' : 'reactivate'}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: confirmItem.version }) })
      if (!response.ok) throw new Error(await referenceDataProblem(response))
      setConfirmItem(null); setMessage(t('common.saved')); await loadItems()
    } catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
  }

  const beginEdit = (item: ReferenceItem) => setEditor({ id: item.id, code: item.code ?? item.isoCode ?? '', nameFa: item.nameFa, nameEn: item.nameEn, symbol: item.symbol ?? '', decimalPrecision: item.decimalPrecision ?? 0, sortOrder: item.sortOrder ?? 0, version: item.version })
  const filtered = Boolean(search || includeInactive)
  return <main className="app-shell">
    <PageHeader title={t('referenceData.title')} subtitle={t('referenceData.subtitle')} actions={<button className="button button--primary" disabled={!typesReady} onClick={() => setEditor({ ...emptyEditor })}>{t('referenceData.add')}</button>} />
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('referenceData.type')}><select disabled={!typesReady} value={typesReady ? selectedCode : ''} onChange={event => { setSelectedCode(event.target.value); setEditor(null) }}>{typesReady ? allTypes.map(type => <option key={type.code} value={type.code}>{english ? type.nameEn : type.nameFa} — {type.code}</option>) : <option value="">{t('common.loading')}</option>}</select></FormField>
      <FormField label={t('common.search')}><input value={search} onChange={event => setSearch(event.target.value)} placeholder={t('common.code')} /></FormField>
      <label className="checkbox-label"><input type="checkbox" checked={includeInactive} onChange={event => setIncludeInactive(event.target.checked)} />{t('common.includeInactive')}</label>
    </FilterBar>
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}{error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <section className="ui-table-surface">
      <TableToolbar title={typesReady ? english ? selectedType?.nameEn : selectedType?.nameFa : t('common.loading')} description={t(hasStandardCode ? 'referenceData.duplicateHint' : 'referenceData.generatedCodeHint')} count={t('common.count', { count: items.length })} />
      <DataTable headers={[t('common.code'), t('common.nameFa'), t('common.nameEn'), selectedCode === 'CURRENCY' ? t('referenceData.precision') : t('referenceData.sortOrder'), t('common.status'), t('common.actions')]} loading={loading} empty={!items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} emptyDescription={t('common.emptyDescription')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
        {items.map(item => <tr key={item.id}>
          <td className="ui-technical">{item.code ?? item.isoCode}</td><td>{item.nameFa}</td><td dir="ltr">{item.nameEn}</td><td>{selectedCode === 'CURRENCY' ? item.decimalPrecision : item.sortOrder}</td>
          <td><StatusBadge text={item.isActive ? t('common.active') : t('common.inactive')} tone={item.isActive ? 'positive' : 'neutral'} /></td>
          <td className="actions"><button className="button button--quiet" onClick={() => beginEdit(item)}>{t('common.edit')}</button><button className="button button--quiet" onClick={() => setConfirmItem(item)}>{item.isActive ? t('common.deactivate') : t('common.activate')}</button></td>
        </tr>)}
      </DataTable>
    </section>
    {editor && <Section title={editor.id ? t('referenceData.editTitle') : t('referenceData.createTitle')}>
      <form className="ui-object-form" onSubmit={event => void submit(event)}><div className="ui-form-grid ui-form-grid--3">
        <FormField label={t('common.code')} hint={editor.id ? t('common.immutable') : hasStandardCode ? undefined : t('referenceData.generatedCodeHint')} required={hasStandardCode}>
          <input dir="ltr" required={hasStandardCode} maxLength={selectedCode === 'CURRENCY' ? 3 : 50} disabled={Boolean(editor.id) || !hasStandardCode} value={!editor.id && !hasStandardCode ? t('referenceData.generatedOnSave') : editor.code} onChange={event => setEditor({ ...editor, code: event.target.value })} />
        </FormField>
        <FormField label={t('common.nameFa')} required><input required maxLength={200} value={editor.nameFa} onChange={event => setEditor({ ...editor, nameFa: event.target.value })} /></FormField>
        <FormField label={t('common.nameEn')} required><input dir="ltr" required maxLength={200} value={editor.nameEn} onChange={event => setEditor({ ...editor, nameEn: event.target.value })} /></FormField>
        {(selectedCode === 'CURRENCY' || selectedCode === 'UNIT') && <FormField label={t('referenceData.symbol')}><input value={editor.symbol} onChange={event => setEditor({ ...editor, symbol: event.target.value })} /></FormField>}
        {selectedCode === 'CURRENCY' ? <FormField label={t('referenceData.precision')} required><input type="number" min="0" max="9" required value={editor.decimalPrecision} onChange={event => setEditor({ ...editor, decimalPrecision: Number(event.target.value) })} /></FormField> : <FormField label={t('referenceData.sortOrder')} required><input type="number" min="0" required value={editor.sortOrder} onChange={event => setEditor({ ...editor, sortOrder: Number(event.target.value) })} /></FormField>}
      </div><div className="ui-action-row"><button className="button button--primary">{t('common.save')}</button><button type="button" className="button" onClick={() => setEditor(null)}>{t('common.cancel')}</button></div></form>
    </Section>}
    <ConfirmationDialog open={Boolean(confirmItem)} title={t('common.stateConfirmTitle')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} onConfirm={() => void changeState()} onCancel={() => setConfirmItem(null)} />
  </main>
}
