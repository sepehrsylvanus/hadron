import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link2, Plus, Power } from 'lucide-react'
import { useAuth } from './AuthContext'
import { localizedName } from './i18n/localizedName'
import { DataTable, EmptyState, FormField, MessageStrip, Pagination, Section, StatusBadge } from './ui'
import { supplierLinkPayload } from './materialRelationshipSupport'

type ItemType = 'PART' | 'COMPONENT'
type ItemRef = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; isActive: boolean }
type Applicability = { id: string; parentItem: ItemRef; childItem: ItemRef; isActive: boolean; version: number }
type Supplier = { id: string; supplierNumber: string; name: string; isActive?: boolean }
type SupplierLink = { id: string; sourcingNumber: string; supplier: Supplier; isPreferred: boolean; isActive: boolean; version: number }
type Candidate = { id: string; code?: string; supplierNumber?: string; nameFa?: string; nameEn?: string | null; name?: string; itemType?: string }

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const pageSize = 10

export default function MaterialRelationships({ itemId, itemType }: { itemId: string; itemType: ItemType }) {
  const { has } = useAuth()
  return <>
    {has('engineering.bom.view') && <ApplicabilityPanel itemId={itemId} itemType={itemType} canManage={has('engineering.bom_draft.manage')} />}
    {has('procurement.sourcing.view') && <SupplierPanel itemId={itemId} canManage={has('procurement.sourcing.manage')} />}
  </>
}

function ApplicabilityPanel({ itemId, itemType, canManage }: { itemId: string; itemType: ItemType; canManage: boolean }) {
  const { t, i18n } = useTranslation()
  const [rows, setRows] = useState<Applicability[]>([])
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [adding, setAdding] = useState(false)
  const [parentType, setParentType] = useState(itemType === 'PART' ? 'PRODUCT' : 'PART')
  const [selected, setSelected] = useState<Candidate | null>(null)
  const [error, setError] = useState('')
  const load = useCallback(async () => {
    const q = new URLSearchParams({ childItemId: itemId, includeInactive: 'true', page: String(page), pageSize: String(pageSize) })
    const response = await fetch(`${api}/bom-revisions/applicabilities?${q}`)
    if (!response.ok) throw new Error(await problem(response))
    const body = await response.json() as { items: Applicability[]; totalCount: number }
    setRows(body.items); setTotal(body.totalCount)
  }, [itemId, page])
  useEffect(() => { const timer = window.setTimeout(() => void load().catch(caught => setError(caught instanceof Error ? caught.message : t('common.error'))), 0); return () => window.clearTimeout(timer) }, [load, t])

  async function save(event: FormEvent) {
    event.preventDefault(); if (!selected) return; setError('')
    const response = await fetch(`${api}/bom-revisions/applicabilities`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ parentItemId: selected.id, childItemId: itemId }) })
    if (!response.ok) { setError(await problem(response)); return }
    setSelected(null); setAdding(false); setPage(1); await load()
  }
  async function state(row: Applicability) {
    setError('')
    const response = await fetch(`${api}/bom-revisions/applicabilities/${row.id}/${row.isActive ? 'deactivate' : 'reactivate'}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: row.version, reason: null }) })
    if (!response.ok) { setError(await problem(response)); return }
    await load()
  }
  return <Section title={t('materials.applicability')} description={t('materials.applicabilityHint')} actions={canManage && <button type="button" className="button button--primary" onClick={() => setAdding(value => !value)}><Plus aria-hidden="true" />{t('materials.addApplicability')}</button>}>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {adding && <form className="material-relationship-editor" onSubmit={event => void save(event)}>
      {itemType === 'COMPONENT' && <FormField label={t('materials.parentType')}><select value={parentType} onChange={event => { setParentType(event.target.value); setSelected(null) }}><option value="PART">{t('materials.part')}</option><option value="COMPONENT">{t('materials.component')}</option></select></FormField>}
      <RelationshipPicker kind={resourceFor(parentType)} itemType={parentType} selected={selected} onSelect={setSelected} label={t('materials.allowedParent')} />
      <div className="ui-action-row"><button className="button button--primary" disabled={!selected}>{t('common.save')}</button><button type="button" className="button" onClick={() => { setAdding(false); setSelected(null) }}>{t('common.cancel')}</button></div>
    </form>}
    {rows.length === 0 ? <EmptyState title={t('materials.noApplicability')} description={t('materials.noApplicabilityHint')} compact /> : <>
      <DataTable headers={[t('materials.allowedParent'), t('common.code'), t('common.status'), t('common.actions')]} loading={false} empty={false} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}>{rows.map(row => <tr key={row.id}><td>{localizedName(row.parentItem, i18n.resolvedLanguage)}</td><td className="ui-technical">{row.parentItem.code}</td><td><StatusBadge text={row.isActive ? t('common.active') : t('common.inactive')} tone={row.isActive ? 'positive' : 'neutral'} /></td><td>{canManage && <button type="button" className="button button--quiet" onClick={() => void state(row)}><Power aria-hidden="true" />{row.isActive ? t('common.deactivate') : t('common.activate')}</button>}</td></tr>)}</DataTable>
      <Pagination page={page} pageSize={pageSize} totalCount={total} onPageChange={setPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(total / pageSize)), count: total })} />
    </>}
  </Section>
}

function SupplierPanel({ itemId, canManage }: { itemId: string; canManage: boolean }) {
  const { t } = useTranslation()
  const [rows, setRows] = useState<SupplierLink[]>([])
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [adding, setAdding] = useState(false)
  const [selected, setSelected] = useState<Candidate | null>(null)
  const [error, setError] = useState('')
  const load = useCallback(async () => {
    const q = new URLSearchParams({ itemId, includeInactive: 'true', page: String(page), pageSize: String(pageSize) })
    const response = await fetch(`${api}/sourcing-options/item-suppliers?${q}`)
    if (!response.ok) throw new Error(await problem(response))
    const body = await response.json() as { items: SupplierLink[]; totalCount: number }
    setRows(body.items); setTotal(body.totalCount)
  }, [itemId, page])
  useEffect(() => { const timer = window.setTimeout(() => void load().catch(caught => setError(caught instanceof Error ? caught.message : t('common.error'))), 0); return () => window.clearTimeout(timer) }, [load, t])

  async function save(event: FormEvent) {
    event.preventDefault(); if (!selected) return; setError('')
    const response = await fetch(`${api}/sourcing-options?evaluationDate=${todayTehran()}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(supplierLinkPayload(itemId, selected.id)) })
    if (!response.ok) { setError(await problem(response)); return }
    setSelected(null); setAdding(false); setPage(1); await load()
  }
  async function state(row: SupplierLink) {
    setError('')
    const response = await fetch(`${api}/sourcing-options/${row.id}/${row.isActive ? 'deactivate' : 'reactivate'}?evaluationDate=${todayTehran()}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: row.version, reason: null }) })
    if (!response.ok) { setError(await problem(response)); return }
    await load()
  }
  return <Section title={t('materials.suppliers')} description={t('materials.suppliersHint')} actions={canManage && <button type="button" className="button button--primary" onClick={() => setAdding(value => !value)}><Link2 aria-hidden="true" />{t('materials.linkSupplier')}</button>}>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {adding && <form className="material-relationship-editor" onSubmit={event => void save(event)}><RelationshipPicker kind="supplier-candidates" itemId={itemId} selected={selected} onSelect={setSelected} label={t('sourcing.supplier')} /><div className="ui-action-row"><button className="button button--primary" disabled={!selected}>{t('common.save')}</button><button type="button" className="button" onClick={() => { setAdding(false); setSelected(null) }}>{t('common.cancel')}</button></div></form>}
    {rows.length === 0 ? <EmptyState title={t('materials.noSuppliers')} description={t('materials.noSuppliersHint')} compact /> : <>
      <DataTable headers={[t('sourcing.supplier'), t('common.number'), t('sourcing.option'), t('sourcing.preferred'), t('common.status'), t('common.actions')]} loading={false} empty={false} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} noResultTitle={t('common.noResultsTitle')}>{rows.map(row => <tr key={row.id}><td>{row.supplier.name}</td><td className="ui-technical">{row.supplier.supplierNumber}</td><td className="ui-technical">{row.sourcingNumber}</td><td>{row.isPreferred ? t('common.yes') : t('common.no')}</td><td><StatusBadge text={row.isActive ? t('common.active') : t('common.inactive')} tone={row.isActive ? 'positive' : 'neutral'} /></td><td>{canManage && <button type="button" className="button button--quiet" onClick={() => void state(row)}><Power aria-hidden="true" />{row.isActive ? t('common.deactivate') : t('common.activate')}</button>}</td></tr>)}</DataTable>
      <Pagination page={page} pageSize={pageSize} totalCount={total} onPageChange={setPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(total / pageSize)), count: total })} />
    </>}
  </Section>
}

function RelationshipPicker({ kind, itemType, itemId, selected, onSelect, label }: { kind: string; itemType?: string; itemId?: string; selected: Candidate | null; onSelect: (value: Candidate) => void; label: string }) {
  const { t, i18n } = useTranslation()
  const [rows, setRows] = useState<Candidate[]>([])
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  useEffect(() => {
    const timer = window.setTimeout(() => {
      const q = new URLSearchParams({ search, page: String(page), pageSize: String(pageSize) })
      if (kind === 'supplier-candidates') q.set('itemId', itemId ?? '')
      else q.set('includeInactive', 'false')
      const url = kind === 'supplier-candidates' ? `${api}/sourcing-options/supplier-candidates?${q}` : `${api}/${kind}?${q}`
      void fetch(url).then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() }).then(body => { setRows(body.items); setTotal(body.totalCount) }).catch(() => { setRows([]); setTotal(0) })
    }, 120)
    return () => window.clearTimeout(timer)
  }, [itemId, itemType, kind, page, search])
  const labelFor = (row: Candidate) => row.name ?? localizedName({ nameFa: row.nameFa ?? '', nameEn: row.nameEn }, i18n.resolvedLanguage)
  const codeFor = (row: Candidate) => row.supplierNumber ?? row.code ?? ''
  return <fieldset className="ui-search-picker"><legend>{label}</legend>{selected && <div className="bom-picker-selection"><strong>{labelFor(selected)}</strong><bdi className="ui-technical">{codeFor(selected)}</bdi></div>}<FormField label={t('common.search')}><input value={search} onChange={event => { setSearch(event.target.value); setPage(1) }} /></FormField><div className="ui-search-picker__options">{rows.map(row => <button type="button" className={row.id === selected?.id ? 'selected' : ''} key={row.id} onClick={() => onSelect(row)}><span>{labelFor(row)}</span><span className="ui-technical">{codeFor(row)}</span></button>)}</div><div className="ui-search-picker__pager"><button type="button" className="button button--quiet" disabled={page === 1} onClick={() => setPage(page - 1)}>{t('common.previous')}</button><span>{t('common.page', { page, pages: Math.max(1, Math.ceil(total / pageSize)), count: total })}</span><button type="button" className="button button--quiet" disabled={page * pageSize >= total} onClick={() => setPage(page + 1)}>{t('common.next')}</button></div></fieldset>
}

function resourceFor(itemType: string) { return itemType === 'PRODUCT' ? 'products' : itemType === 'PART' ? 'parts' : 'components' }
function todayTehran() { const parts = new Intl.DateTimeFormat('en', { timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date()); const values = Object.fromEntries(parts.map(x => [x.type, x.value])); return `${values.year}-${values.month}-${values.day}` }
async function problem(response: Response) { const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null; return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}` }
