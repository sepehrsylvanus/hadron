import { FormEvent, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { ConfirmationDialog, DataTable, Definition, DefinitionGrid, DisplayEditActions, FilterBar, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'
import { currentListContext, useListQuery, useListReturn } from './ui/routeContext'
import { UnsavedChangesGuard } from './ui/UnsavedChangesGuard'
import ProductStructure from './ProductStructure'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { supplyMethods, type SupplyMethod } from './materialItemSupport'

type Ref = { id: string; code: string; nameFa: string; nameEn: string; isActive: boolean }
export type Product = { id: string; code: string; nameFa: string; nameEn?: string | null; productCategory: Ref; baseUnit: Ref; businessUnit?: Ref | null; supplyMethod?: SupplyMethod | null; safetyStockQty: number; criticalDays: number; safeStartDays: number; excessStartDays: number; isActive: boolean; notes?: string | null; version: number }
type Editor = { nameFa: string; nameEn: string; productCategoryId: string; baseUnitId: string; businessUnitId: string; supplyMethod: SupplyMethod | ''; safetyStockQty: number; criticalDays: number; safeStartDays: number; excessStartDays: number; notes: string; version?: number }
const empty: Editor = { nameFa: '', nameEn: '', productCategoryId: '', baseUnitId: '', businessUnitId: '', supplyMethod: '', safetyStockQty: 0, criticalDays: 0, safeStartDays: 0, excessStartDays: 0, notes: '' }
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
    <section className="ui-table-surface"><TableToolbar title={t('products.list')} count={t('common.count', { count: total })} /><DataTable headers={[t('common.code'), t('common.name'), t('common.category'), t('common.unit'), t('common.status'), t('common.actions')]} loading={loading} empty={!items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('common.emptyTitle')} emptyDescription={t('common.emptyDescription')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
      {items.map(item => <tr key={item.id}><td className="ui-technical">{item.code}</td><td>{item.nameFa}</td><td>{english ? item.productCategory.nameEn : item.productCategory.nameFa}</td><td>{english ? item.baseUnit.nameEn : item.baseUnit.nameFa}</td><td><StatusBadge text={item.isActive ? t('common.active') : t('common.inactive')} tone={item.isActive ? 'positive' : 'neutral'} /></td><td className="actions"><Link className="button button--quiet" to={`/mrp/products/${item.id}?from=${from}`}>{t('common.details')}</Link></td></tr>)}
    </DataTable><Pagination page={query.page} pageSize={query.pageSize} totalCount={total} onPageChange={page => query.update({ page })} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page: query.page, pages: Math.max(1, Math.ceil(total / query.pageSize)), count: total })} /></section>
  </main>
}

export function ProductObjectPage() {
  const { t, i18n } = useTranslation(); const english = i18n.resolvedLanguage === 'en'; const { id } = useParams(); const location = useLocation(); const navigate = useNavigate(); const back = useListReturn(listPath, ['/mrp/engineering/bom'])
  const createMode = !id; const routeEdit = createMode || location.pathname.endsWith('/edit')
  const [product, setProduct] = useState<Product | null>(null); const [editor, setEditor] = useState<Editor>({ ...empty }); const [initial, setInitial] = useState(''); const frontendSupplyMethods = useRef(new Map<string, SupplyMethod>())
  const [categories, setCategories] = useState<Ref[]>([]); const [units, setUnits] = useState<Ref[]>([])
  const [loading, setLoading] = useState(Boolean(id)); const [saving, setSaving] = useState(false); const [error, setError] = useState(''); const [message, setMessage] = useState(''); const [confirmState, setConfirmState] = useState(false); const [discard, setDiscard] = useState(false)
  const dirty = routeEdit && JSON.stringify(editor) !== initial
  const load = useCallback(async () => {
    if (!id) { setInitial(JSON.stringify(empty)); return }
    setLoading(true)
    try { const response = await fetch(`${api}/products/${id}`); if (!response.ok) throw new Error(await problem(response)); const responseValue = await response.json() as Product; const value = { ...responseValue, supplyMethod: responseValue.supplyMethod ?? frontendSupplyMethods.current.get(responseValue.id) }; setProduct(value); const next = toEditor(value); setEditor(next); setInitial(JSON.stringify(next)) }
    catch (caught) { setError(caught instanceof Error ? caught.message : t('common.error')) } finally { setLoading(false) }
  }, [id, t])
  useEffect(() => { void Promise.all([fetch(`${api}/reference-data/types/PRODUCT_CATEGORY/values?includeInactive=true`).then(r => r.json()), fetch(`${api}/reference-data/units?includeInactive=true`).then(r => r.json())]).then(([c, u]) => { setCategories(c); setUnits(u) }).catch(() => setError(t('common.error'))) }, [t])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load])
  async function save(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError('')
    const { supplyMethod: selectedSupplyMethod, ...apiEditor } = editor
    const payload = { ...apiEditor, nameEn: editor.nameEn || null, businessUnitId: editor.businessUnitId || null, notes: editor.notes || null, expectedVersion: editor.version }
    try { const response = await fetch(`${api}/products${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (!response.ok) throw new Error(await problem(response)); const result = await response.json(); const responseProduct = (result.product ?? result.item ?? result) as Product; const savedId = responseProduct.id ?? id; const frontendSupplyMethod = selectedSupplyMethod || undefined; if (savedId && frontendSupplyMethod) frontendSupplyMethods.current.set(savedId, frontendSupplyMethod); const saved = { ...responseProduct, supplyMethod: responseProduct.supplyMethod ?? frontendSupplyMethod }; const next = toEditor(saved); setProduct(saved); setEditor(next); setInitial(JSON.stringify(next)); setMessage(t('common.saved')); navigate(`/mrp/products/${savedId}?from=${encodeURIComponent(back)}`, { replace: true }) }
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
    <PageHeader sticky title={title} subtitle={product?.code ?? t('products.subtitle')} backTo={back} backLabel={t('common.backToList')} status={product && <StatusBadge text={product.isActive ? t('common.active') : t('common.inactive')} tone={product.isActive ? 'positive' : 'neutral'} />} actions={<><DisplayEditActions editing={editing} dirty={dirty} editLabel={t('common.edit')} saveLabel={t('common.save')} cancelLabel={t('common.cancel')} formId="product-form" saving={saving} onEdit={() => navigate(`/mrp/products/${id}/edit?from=${encodeURIComponent(back)}`)} onCancel={cancel} />{product && !editing && <Link className="button" to={`/mrp/engineering/bom?parentItemId=${product.id}&parentType=PRODUCT&rootItemId=${product.id}&rootItemType=PRODUCT`}>{t('bom.manageStructure')}</Link>}{product && !editing && <button className="button button--quiet" onClick={() => setConfirmState(true)}>{product.isActive ? t('common.deactivate') : t('common.activate')}</button>}</>} />
    {message && <MessageStrip tone="positive">{message}</MessageStrip>}{error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <form id="product-form" className="ui-object-form" onSubmit={event => void save(event)}>
      <Section title={t('common.overview')}>{editing ? <div className="ui-form-grid ui-form-grid--3">
        {product && <FormField label={t('common.code')} hint={t('common.immutable')}><input className="ui-technical" dir="ltr" value={product.code} disabled /></FormField>}
        <FormField label={t('common.name')} required><input required maxLength={200} value={editor.nameFa} onChange={e => setEditor({ ...editor, nameFa: e.target.value })} /></FormField>
        <FormField label={t('common.category')} required><select required value={editor.productCategoryId} onChange={e => setEditor({ ...editor, productCategoryId: e.target.value })}><option value="">{t('common.notSet')}</option>{categories.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>
        <FormField label={t('common.unit')} required><select required value={editor.baseUnitId} onChange={e => setEditor({ ...editor, baseUnitId: e.target.value })}><option value="">{t('common.notSet')}</option>{units.map(x => <option key={x.id} value={x.id}>{english ? x.nameEn : x.nameFa}{!x.isActive ? ` — ${t('common.inactive')}` : ''}</option>)}</select></FormField>
        <FormField label={t('products.supplyMethod')} hint={t('products.supplyMethodUiOnly')} required><select required value={editor.supplyMethod} onChange={e => setEditor({ ...editor, supplyMethod: e.target.value as SupplyMethod | '' })}><option value="">{t('common.notSet')}</option>{supplyMethods.map(method => <option key={method} value={method}>{t(`materials.${method}`)}</option>)}</select></FormField>
      </div> : product && <DefinitionGrid>
        <Definition label={t('common.name')}>{product.nameFa}<small className="product-object-code">{t('common.code')}: <bdi className="ui-technical">{product.code}</bdi></small></Definition>
        <Definition label={t('common.category')}>{english ? product.productCategory.nameEn : product.productCategory.nameFa}</Definition>
        <Definition label={t('common.unit')}>{english ? product.baseUnit.nameEn : product.baseUnit.nameFa}</Definition>
        <Definition label={t('products.supplyMethod')}>{product.supplyMethod ? t(`materials.${product.supplyMethod}`) : t('common.notSet')}</Definition>
      </DefinitionGrid>}</Section>
      <Section title={t('products.planning')}>{editing ? <div className="ui-form-grid ui-form-grid--3"><NumberField label={t('products.safetyStock')} value={editor.safetyStockQty} disabled={false} decimal onChange={value => setEditor({ ...editor, safetyStockQty: value })} /><NumberField label={t('products.criticalDays')} value={editor.criticalDays} disabled={false} onChange={value => setEditor({ ...editor, criticalDays: value })} /><NumberField label={t('products.safeStartDays')} value={editor.safeStartDays} disabled={false} onChange={value => setEditor({ ...editor, safeStartDays: value })} /><NumberField label={t('products.excessStartDays')} value={editor.excessStartDays} disabled={false} onChange={value => setEditor({ ...editor, excessStartDays: value })} /></div> : product && <DefinitionGrid><Definition label={t('products.safetyStock')} technical>{formatOperationalNumber(product.safetyStockQty, i18n.resolvedLanguage)}</Definition><Definition label={t('products.criticalDays')} technical>{formatOperationalNumber(product.criticalDays, i18n.resolvedLanguage)}</Definition><Definition label={t('products.safeStartDays')} technical>{formatOperationalNumber(product.safeStartDays, i18n.resolvedLanguage)}</Definition><Definition label={t('products.excessStartDays')} technical>{formatOperationalNumber(product.excessStartDays, i18n.resolvedLanguage)}</Definition></DefinitionGrid>}</Section>
      {product && !editing && <Section title={t('products.lifecycle')}><DefinitionGrid><Definition label={t('common.status')}>{product.isActive ? t('common.active') : t('common.inactive')}</Definition><Definition label={t('common.version')} technical>{product.version}</Definition></DefinitionGrid></Section>}
    </form>
    {product && !editing && <MessageStrip tone="information">{t('products.supplyMethodUiOnly')}</MessageStrip>}
    {product && !editing && <ProductStructure key={product.id} itemId={product.id} itemType="PRODUCT" />}
    <ConfirmationDialog open={confirmState} title={t('common.stateConfirmTitle')} confirmLabel={t('common.confirm')} cancelLabel={t('common.cancel')} onConfirm={() => void changeState()} onCancel={() => setConfirmState(false)} />
    <ConfirmationDialog open={discard} title={t('common.discardTitle')} description={t('common.discardDescription')} confirmLabel={t('common.discard')} cancelLabel={t('common.cancel')} onConfirm={() => navigate(id ? `/mrp/products/${id}?from=${encodeURIComponent(back)}` : back)} onCancel={() => setDiscard(false)} />
    <UnsavedChangesGuard when={dirty} />
  </main>
}

function NumberField({ label, value, disabled, decimal, onChange }: { label: string; value: number; disabled: boolean; decimal?: boolean; onChange: (value: number) => void }) { return <FormField label={label}><input className="ui-technical" type="number" min="0" step={decimal ? '0.000001' : '1'} disabled={disabled} value={value} onChange={event => onChange(Number(event.target.value))} /></FormField> }
function toEditor(product: Product): Editor { return { nameFa: product.nameFa, nameEn: product.nameEn ?? '', productCategoryId: product.productCategory.id, baseUnitId: product.baseUnit.id, businessUnitId: product.businessUnit?.id ?? '', safetyStockQty: product.safetyStockQty, criticalDays: product.criticalDays, safeStartDays: product.safeStartDays, excessStartDays: product.excessStartDays, notes: product.notes ?? '', supplyMethod: product.supplyMethod ?? '', version: product.version } }
async function problem(response: Response) { const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null; return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}` }
