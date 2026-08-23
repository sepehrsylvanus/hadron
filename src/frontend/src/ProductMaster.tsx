import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { ConfirmationDialog, DataTable, Definition, DefinitionGrid, DisplayEditActions, FilterBar, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'
import { currentListContext, useListQuery, useListReturn } from './ui/routeContext'

type Ref = { id: string; code: string; nameFa: string; nameEn: string; isActive: boolean }
export type Product = { id: string; productNumber: string; code: string; nameFa: string; nameEn?: string | null; productCategory: Ref; baseUnit: Ref; businessUnit?: Ref | null; safetyStockQty: number; criticalDays: number; safeStartDays: number; excessStartDays: number; isActive: boolean; notes?: string | null; version: number }
type Editor = { code: string; nameFa: string; nameEn: string; productCategoryId: string; baseUnitId: string; businessUnitId: string; safetyStockQty: number; criticalDays: number; safeStartDays: number; excessStartDays: number; notes: string; version?: number }
const empty: Editor = { code: '', nameFa: '', nameEn: '', productCategoryId: '', baseUnitId: '', businessUnitId: '', safetyStockQty: 0, criticalDays: 0, safeStartDays: 0, excessStartDays: 0, notes: '' }
const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const listPath = '/mrp/master-data/products'

export default function ProductMaster() {
  const { t, i18n } = useTranslation(); const english = i18n.resolvedLanguage === 'en'; const location = useLocation()
  const query = useListQuery(); const search = query.value('search'); const category = query.value('category'); const business = query.value('business'); const includeInactive = query.flag('includeInactive')
  const [items, setItems] = useState<Product[]>([]); const [categories, setCategories] = useState<Ref[]>([]); const [businessUnits, setBusinessUnits] = useState<Ref[]>([])
  const [total, setTotal] = useState(0); const [loading, setLoading] = useState(true); const [error, setError] = useState('')
  const load = useCallback(async () => {
    setLoading(true); setError('')
    const q = new URLSearchParams({ search, includeInactive: String(includeInactive), page: String(query.page), pageSize: String(query.pageSize) }); if (category) q.set('productCategoryId', category); if (business) q.set('businessUnitId', business)
    try { const response = await fetch(`${api}/products?${q}`); if (!response.ok) throw new Error(await problem(response)); const body = await response.json(); setItems(body.items); setTotal(body.totalCount) }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setLoading(false) }
  }, [business, category, includeInactive, query.page, query.pageSize, search, t])
  useEffect(() => { void Promise.all([fetch(`${api}/reference-data/types/PRODUCT_CATEGORY/values`).then(r => r.json()), fetch(`${api}/reference-data/types/BUSINESS_UNIT/values`).then(r => r.json())]).then(([c, b]) => { setCategories(c); setBusinessUnits(b) }).catch(() => setError(t('common.error'))) }, [t])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 120); return () => window.clearTimeout(timer) }, [load])
  const from = currentListContext(location.pathname, location.search)
  const filtered = Boolean(search || category || business || includeInactive)
  return <main className="app-shell">
    <PageHeader title={t('products.title')} subtitle={t('products.subtitle')} actions={<Link className="button button--primary" to={`/mrp/products/new?from=${from}`}>{t('products.new')}</Link>} />
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('common.search')}><input value={search} onChange={e => query.update({ search: e.target.value }, true)} placeholder={t('products.searchPlaceholder')} /></FormField>
      <FormField label={t('common.category')}><select value={category} onChange={e => query.update({ category: e.target.value }, true)}><option value="">{t('common.all')}</option>{categories.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}</option>)}</select></FormField>
      <FormField label={t('products.businessUnit')}><select value={business} onChange={e => query.update({ business: e.target.value }, true)}><option value="">{t('common.all')}</option>{businessUnits.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}</option>)}</select></FormField>
      <label className="checkbox-label"><input type="checkbox" checked={includeInactive} onChange={e => query.update({ includeInactive: e.target.checked }, true)} />{t('common.includeInactive')}</label>
    </FilterBar>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <section className="ui-table-surface"><TableToolbar title={t('products.list')} count={t('common.count', { count: total })} /><DataTable headers={[t('common.number'), t('common.code'), t('common.nameFa'), t('common.category'), t('common.unit'), t('common.status'), t('common.actions')]} loading={loading} empty={!items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} emptyDescription={t('common.emptyDescription')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
      {items.map(item => <tr key={item.id}><td className="ui-technical">{item.productNumber}</td><td className="ui-technical">{item.code}</td><td>{item.nameFa}</td><td>{english ? item.productCategory.nameEn : item.productCategory.nameFa}</td><td>{english ? item.baseUnit.nameEn : item.baseUnit.nameFa}</td><td><StatusBadge text={item.isActive ? t('common.active') : t('common.inactive')} tone={item.isActive ? 'positive' : 'neutral'} /></td><td className="actions"><Link className="button button--quiet" to={`/mrp/products/${item.id}?from=${from}`}>{t('common.details')}</Link></td></tr>)}
    </DataTable><Pagination page={query.page} pageSize={query.pageSize} totalCount={total} onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(total / query.pageSize)), count: total })} /></section>
  </main>
}

export function ProductObjectPage() {
  const { t, i18n } = useTranslation(); const english = i18n.resolvedLanguage === 'en'; const { id } = useParams(); const location = useLocation(); const navigate = useNavigate(); const back = useListReturn(listPath)
  const createMode = !id; const routeEdit = createMode || location.pathname.endsWith('/edit')
  const [product, setProduct] = useState<Product | null>(null); const [editor, setEditor] = useState<Editor>({ ...empty }); const [initial, setInitial] = useState('')
  const [categories, setCategories] = useState<Ref[]>([]); const [units, setUnits] = useState<Ref[]>([]); const [businessUnits, setBusinessUnits] = useState<Ref[]>([])
  const [loading, setLoading] = useState(Boolean(id)); const [saving, setSaving] = useState(false); const [error, setError] = useState(''); const [message, setMessage] = useState(''); const [confirmState, setConfirmState] = useState(false); const [discard, setDiscard] = useState(false)
  const dirty = routeEdit && JSON.stringify(editor) !== initial
  const load = useCallback(async () => {
    if (!id) { setInitial(JSON.stringify(empty)); return }
    setLoading(true)
    try { const response = await fetch(`${api}/products/${id}`); if (!response.ok) throw new Error(await problem(response)); const value = await response.json() as Product; setProduct(value); const next = toEditor(value); setEditor(next); setInitial(JSON.stringify(next)) }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setLoading(false) }
  }, [id, t])
  useEffect(() => { void Promise.all([fetch(`${api}/reference-data/types/PRODUCT_CATEGORY/values?includeInactive=true`).then(r => r.json()), fetch(`${api}/reference-data/units?includeInactive=true`).then(r => r.json()), fetch(`${api}/reference-data/types/BUSINESS_UNIT/values?includeInactive=true`).then(r => r.json())]).then(([c, u, b]) => { setCategories(c); setUnits(u); setBusinessUnits(b) }).catch(() => setError(t('common.error'))) }, [t])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load])
  useEffect(() => { const before = (event: BeforeUnloadEvent) => { if (dirty) event.preventDefault() }; window.addEventListener('beforeunload', before); return () => window.removeEventListener('beforeunload', before) }, [dirty])
  async function save(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError('')
    const payload = { ...editor, nameEn: editor.nameEn || null, businessUnitId: editor.businessUnitId || null, notes: editor.notes || null, expectedVersion: editor.version }
    try { const response = await fetch(`${api}/products${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (!response.ok) throw new Error(await problem(response)); const result = await response.json(); const saved = (result.product ?? result.item ?? result) as Product; const savedId = saved.id ?? id; setMessage(t('common.saved')); navigate(`/mrp/products/${savedId}?from=${encodeURIComponent(back)}`, { replace: true }) }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setSaving(false) }
  }
  async function changeState() {
    if (!product) return
    try { const response = await fetch(`${api}/products/${product.id}/${product.isActive ? 'deactivate' : 'reactivate'}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: product.version }) }); if (!response.ok) throw new Error(await problem(response)); setConfirmState(false); await load() }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) }
  }
  function cancel() { if (dirty) setDiscard(true); else navigate(id ? `/mrp/products/${id}?from=${encodeURIComponent(back)}` : back) }
  if (loading) return <main className="app-shell"><LoadingState label={t('common.loading')} /></main>
  const editing = routeEdit
  const title = product?.nameFa || (createMode ? t('products.new') : t('products.object'))
  return <main className="app-shell">
    <PageHeader title={title} subtitle={product?.productNumber ?? t('products.subtitle')} backTo={back} backLabel={t('common.backToList')} status={product && <StatusBadge text={product.isActive ? t('common.active') : t('common.inactive')} tone={product.isActive ? 'positive' : 'neutral'} />} actions={<><DisplayEditActions editing={editing} dirty={dirty} editLabel={t('common.edit')} saveLabel={t('common.save')} cancelLabel={t('common.cancel')} formId="product-form" saving={saving} onEdit={() => navigate(`/mrp/products/${id}/edit?from=${encodeURIComponent(back)}`)} onCancel={cancel} />{product && !editing && <button className="button" onClick={() => setConfirmState(true)}>{product.isActive ? t('common.deactivate') : t('common.activate')}</button>}</>} />
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}{error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <form id="product-form" className="ui-object-form" onSubmit={event => void save(event)}>
      <Section title={t('common.overview')}><div className="ui-form-grid ui-form-grid--3">
        {product && <FormField label={t('common.number')}><input className="ui-technical" value={product.productNumber} disabled /></FormField>}
        <FormField label={t('common.code')} hint={product ? t('common.immutable') : undefined} required><input dir="ltr" required maxLength={64} disabled={!editing || Boolean(product)} value={editor.code} onChange={e => setEditor({ ...editor, code: e.target.value })} /></FormField>
        <FormField label={t('common.nameFa')} required><input required maxLength={200} disabled={!editing} value={editor.nameFa} onChange={e => setEditor({ ...editor, nameFa: e.target.value })} /></FormField>
        <FormField label={t('common.nameEn')}><input dir="ltr" maxLength={200} disabled={!editing} value={editor.nameEn} onChange={e => setEditor({ ...editor, nameEn: e.target.value })} /></FormField>
        <FormField label={t('common.category')} required><select required disabled={!editing} value={editor.productCategoryId} onChange={e => setEditor({ ...editor, productCategoryId: e.target.value })}><option value="">{t('common.notSet')}</option>{categories.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>
        <FormField label={t('common.unit')} required><select required disabled={!editing} value={editor.baseUnitId} onChange={e => setEditor({ ...editor, baseUnitId: e.target.value })}><option value="">{t('common.notSet')}</option>{units.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>
        <FormField label={t('products.businessUnit')}><select disabled={!editing} value={editor.businessUnitId} onChange={e => setEditor({ ...editor, businessUnitId: e.target.value })}><option value="">{t('common.notSet')}</option>{businessUnits.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>
        <FormField className="span-2" label={t('common.notes')}><textarea disabled={!editing} maxLength={2000} value={editor.notes} onChange={e => setEditor({ ...editor, notes: e.target.value })} /></FormField>
      </div></Section>
      <Section title={t('products.planning')}><div className="ui-form-grid ui-form-grid--3"><NumberField label={t('products.safetyStock')} value={editor.safetyStockQty} disabled={!editing} decimal onChange={value => setEditor({ ...editor, safetyStockQty: value })} /><NumberField label={t('products.criticalDays')} value={editor.criticalDays} disabled={!editing} onChange={value => setEditor({ ...editor, criticalDays: value })} /><NumberField label={t('products.safeStartDays')} value={editor.safeStartDays} disabled={!editing} onChange={value => setEditor({ ...editor, safeStartDays: value })} /><NumberField label={t('products.excessStartDays')} value={editor.excessStartDays} disabled={!editing} onChange={value => setEditor({ ...editor, excessStartDays: value })} /></div></Section>
      {product && !editing && <Section title={t('products.lifecycle')}><DefinitionGrid><Definition label={t('common.status')}>{product.isActive ? t('common.active') : t('common.inactive')}</Definition><Definition label={t('common.version')} technical>{product.version}</Definition></DefinitionGrid></Section>}
    </form>
    <ConfirmationDialog open={confirmState} title={t('common.stateConfirmTitle')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} onConfirm={() => void changeState()} onCancel={() => setConfirmState(false)} />
    <ConfirmationDialog open={discard} title={t('common.discardTitle')} description={t('common.discardDescription')} confirmLabel={t('common.discard')} cancelLabel={t('common.cancel')} onConfirm={() => navigate(id ? `/mrp/products/${id}?from=${encodeURIComponent(back)}` : back)} onCancel={() => setDiscard(false)} />
  </main>
}

function NumberField({ label, value, disabled, decimal, onChange }: { label: string; value: number; disabled: boolean; decimal?: boolean; onChange: (value: number) => void }) { return <FormField label={label}><input className="ui-technical" type="number" min="0" step={decimal ? '0.000001' : '1'} disabled={disabled} value={value} onChange={event => onChange(Number(event.target.value))} /></FormField> }
function toEditor(product: Product): Editor { return { code: product.code, nameFa: product.nameFa, nameEn: product.nameEn ?? '', productCategoryId: product.productCategory.id, baseUnitId: product.baseUnit.id, businessUnitId: product.businessUnit?.id ?? '', safetyStockQty: product.safetyStockQty, criticalDays: product.criticalDays, safeStartDays: product.safeStartDays, excessStartDays: product.excessStartDays, notes: product.notes ?? '', version: product.version } }
async function problem(response: Response) { const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null; return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}` }
