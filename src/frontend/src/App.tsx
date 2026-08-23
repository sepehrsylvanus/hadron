import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import hadronLogo from './assets/brand/hadron-logo-red.svg'
import ProductMaster from './ProductMaster'
import SupplierMaster from './SupplierMaster'
import MaterialItemMaster from './MaterialItemMaster'
import SourcingMaster from './SourcingMaster'

type ReferenceType = { id: string; code: string; nameFa: string; nameEn: string }
type ReferenceItem = {
  id: string; code?: string; isoCode?: string; nameFa: string; nameEn: string; symbol?: string | null
  decimalPrecision?: number; isActive: boolean; sortOrder?: number; version: number
}
type EditorState = {
  id?: string; code: string; nameFa: string; nameEn: string; symbol: string
  decimalPrecision: number; sortOrder: number; version?: number
}

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const currencyType: ReferenceType = { id: 'currency', code: 'CURRENCY', nameFa: 'ارز', nameEn: 'Currency' }
const unitType: ReferenceType = { id: 'unit', code: 'UNIT', nameFa: 'واحد اندازه‌گیری', nameEn: 'Unit' }
const emptyEditor: EditorState = { code: '', nameFa: '', nameEn: '', symbol: '', decimalPrecision: 0, sortOrder: 0 }

function ReferenceDataPage() {
  const [types, setTypes] = useState<ReferenceType[]>([])
  const [selectedCode, setSelectedCode] = useState('COUNTRY')
  const [items, setItems] = useState<ReferenceItem[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [search, setSearch] = useState('')
  const [editor, setEditor] = useState<EditorState | null>(null)
  const [loading, setLoading] = useState(true)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  const allTypes = useMemo(() => [...types, currencyType, unitType], [types])
  const selectedType = allTypes.find((type) => type.code === selectedCode) ?? allTypes[0]

  const resourcePath = useCallback(() => {
    if (selectedCode === 'CURRENCY') return 'reference-data/currencies'
    if (selectedCode === 'UNIT') return 'reference-data/units'
    return `reference-data/types/${selectedCode}/values`
  }, [selectedCode])

  const loadItems = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const query = new URLSearchParams({ includeInactive: String(includeInactive) })
      if (search.trim()) query.set('search', search.trim())
      const response = await fetch(`${apiBaseUrl}/${resourcePath()}?${query}`, { credentials: 'include' })
      if (!response.ok) throw new Error('دریافت داده‌های مرجع ناموفق بود.')
      setItems(await response.json() as ReferenceItem[])
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'خطای پیش‌بینی‌نشده')
    } finally {
      setLoading(false)
    }
  }, [includeInactive, resourcePath, search])

  useEffect(() => {
    async function loadTypes() {
      try {
        const response = await fetch(`${apiBaseUrl}/reference-data/types`, { credentials: 'include' })
        if (!response.ok) throw new Error('دریافت انواع داده مرجع ناموفق بود.')
        setTypes(await response.json() as ReferenceType[])
      } catch (caught) {
        setError(caught instanceof Error ? caught.message : 'خطای پیش‌بینی‌نشده')
        setLoading(false)
      }
    }
    void loadTypes()
  }, [])

  useEffect(() => {
    const timeoutId = window.setTimeout(() => void loadItems(), 0)
    return () => window.clearTimeout(timeoutId)
  }, [loadItems])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!editor) return
    setError('')
    setMessage('')
    const isEdit = Boolean(editor.id)
    const payload = selectedCode === 'CURRENCY'
      ? isEdit
        ? { nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, decimalPrecision: editor.decimalPrecision, expectedVersion: editor.version }
        : { isoCode: editor.code, nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, decimalPrecision: editor.decimalPrecision }
      : selectedCode === 'UNIT'
        ? isEdit
          ? { nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, sortOrder: editor.sortOrder, expectedVersion: editor.version }
          : { code: editor.code, nameFa: editor.nameFa, nameEn: editor.nameEn, symbol: editor.symbol || null, sortOrder: editor.sortOrder }
        : isEdit
          ? { nameFa: editor.nameFa, nameEn: editor.nameEn, sortOrder: editor.sortOrder, expectedVersion: editor.version }
          : { code: editor.code, nameFa: editor.nameFa, nameEn: editor.nameEn, sortOrder: editor.sortOrder }
    try {
      const base = resourcePath().replace(/types\/[^/]+\/values$/, 'values')
      const response = await fetch(`${apiBaseUrl}/${isEdit ? `${base}/${editor.id}` : resourcePath()}`, {
        method: isEdit ? 'PUT' : 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setEditor(null)
      setMessage(isEdit ? 'تغییرات ذخیره شد.' : 'مقدار جدید ایجاد شد.')
      await loadItems()
    } catch (caught) { setError(caught instanceof Error ? caught.message : 'خطای پیش‌بینی‌نشده') }
  }

  async function changeState(item: ReferenceItem) {
    setError('')
    setMessage('')
    const action = item.isActive ? 'deactivate' : 'reactivate'
    const base = resourcePath().replace(/types\/[^/]+\/values$/, 'values')
    try {
      const response = await fetch(`${apiBaseUrl}/${base}/${item.id}/${action}`, {
        method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: item.version }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setMessage(item.isActive ? 'مقدار غیرفعال شد.' : 'مقدار دوباره فعال شد.')
      await loadItems()
    } catch (caught) { setError(caught instanceof Error ? caught.message : 'خطای پیش‌بینی‌نشده') }
  }

  function beginEdit(item: ReferenceItem) {
    setEditor({ id: item.id, code: item.code ?? item.isoCode ?? '', nameFa: item.nameFa, nameEn: item.nameEn,
      symbol: item.symbol ?? '', decimalPrecision: item.decimalPrecision ?? 0, sortOrder: item.sortOrder ?? 0, version: item.version })
  }

  return (
    <main className="app-shell" dir="rtl">
      <header className="app-header">
        <img className="brand-logo" src={hadronLogo} alt="هادرون توان" />
        <div><p className="eyebrow">Hadron ERP</p><h1>مدیریت داده‌های مرجع</h1></div>
      </header>
      <section className="toolbar" aria-label="فیلتر داده‌های مرجع">
        <label>نوع داده<select value={selectedCode} onChange={(event) => { setSelectedCode(event.target.value); setEditor(null) }}>
          {allTypes.map((type) => <option key={type.code} value={type.code}>{type.nameFa} — {type.nameEn}</option>)}</select></label>
        <label>جست‌وجو<input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="کد یا نام" /></label>
        <label className="checkbox-label"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} />نمایش غیرفعال‌ها</label>
        <button className="button button--primary" type="button" onClick={() => setEditor({ ...emptyEditor })}>افزودن مقدار</button>
      </section>
      {message && <p className="notice notice--success" role="status">{message}</p>}
      {error && <p className="notice notice--error" role="alert">{error}</p>}
      <section className="data-panel" aria-labelledby="list-title">
        <div className="panel-title"><div><h2 id="list-title">{selectedType?.nameFa ?? 'داده مرجع'}</h2><p>کد پس از ایجاد قابل تغییر نیست.</p></div><span>{items.length} مورد</span></div>
        <div className="table-wrap"><table><thead><tr><th>کد</th><th>نام فارسی</th><th>نام انگلیسی</th>{selectedCode !== 'CURRENCY' && <th>ترتیب</th>}<th>وضعیت</th><th>عملیات</th></tr></thead>
          <tbody>{loading ? <tr><td colSpan={6}>در حال دریافت…</td></tr> : items.length === 0 ? <tr><td colSpan={6}>موردی ثبت نشده است.</td></tr> : items.map((item) =>
            <tr key={item.id}><td dir="ltr">{item.code ?? item.isoCode}</td><td>{item.nameFa}</td><td dir="ltr">{item.nameEn}</td>{selectedCode !== 'CURRENCY' && <td>{item.sortOrder}</td>}
              <td><span className={`status ${item.isActive ? 'status--active' : 'status--inactive'}`}>{item.isActive ? 'فعال' : 'غیرفعال'}</span></td>
              <td className="actions"><button type="button" onClick={() => beginEdit(item)}>ویرایش</button><button type="button" onClick={() => void changeState(item)}>{item.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}</button></td></tr>)}</tbody>
        </table></div>
      </section>
      {editor && <div className="dialog-backdrop" role="presentation"><section className="dialog" role="dialog" aria-modal="true" aria-labelledby="editor-title">
        <h2 id="editor-title">{editor.id ? 'ویرایش مقدار' : 'افزودن مقدار'}</h2><form onSubmit={(event) => void submit(event)}>
          <label>کد<input dir="ltr" required maxLength={selectedCode === 'CURRENCY' ? 3 : 50} disabled={Boolean(editor.id)} value={editor.code} onChange={(event) => setEditor({ ...editor, code: event.target.value })} /></label>
          <label>نام فارسی<input required maxLength={200} value={editor.nameFa} onChange={(event) => setEditor({ ...editor, nameFa: event.target.value })} /></label>
          <label>نام انگلیسی<input dir="ltr" required maxLength={200} value={editor.nameEn} onChange={(event) => setEditor({ ...editor, nameEn: event.target.value })} /></label>
          {(selectedCode === 'CURRENCY' || selectedCode === 'UNIT') && <label>نماد (اختیاری)<input maxLength={20} value={editor.symbol} onChange={(event) => setEditor({ ...editor, symbol: event.target.value })} /></label>}
          {selectedCode === 'CURRENCY' ? <label>دقت اعشار<input type="number" min="0" max="9" required value={editor.decimalPrecision} onChange={(event) => setEditor({ ...editor, decimalPrecision: Number(event.target.value) })} /></label>
            : <label>ترتیب نمایش<input type="number" min="0" required value={editor.sortOrder} onChange={(event) => setEditor({ ...editor, sortOrder: Number(event.target.value) })} /></label>}
          <div className="dialog-actions"><button className="button button--primary" type="submit">ذخیره</button><button className="button" type="button" onClick={() => setEditor(null)}>انصراف</button></div>
        </form></section></div>}
    </main>
  )
}

async function problemMessage(response: Response) {
  const body = await response.json().catch(() => null) as { detail?: string; errors?: Record<string, string[]> } | null
  return body?.detail ?? (Object.values(body?.errors ?? {}).flat().join(' ') || 'عملیات انجام نشد.')
}

function App() {
  const [page, setPage] = useState<'products' | 'parts' | 'components' | 'suppliers' | 'sourcing' | 'reference'>('products')
  return <><nav className="main-nav" dir="rtl"><button className={page === 'products' ? 'active' : ''} onClick={() => setPage('products')}>محصولات</button><button className={page === 'parts' ? 'active' : ''} onClick={() => setPage('parts')}>قطعات</button><button className={page === 'components' ? 'active' : ''} onClick={() => setPage('components')}>کامپوننت‌ها</button><button className={page === 'suppliers' ? 'active' : ''} onClick={() => setPage('suppliers')}>تأمین‌کنندگان</button><button className={page === 'sourcing' ? 'active' : ''} onClick={() => setPage('sourcing')}>تأمین‌یابی</button><button className={page === 'reference' ? 'active' : ''} onClick={() => setPage('reference')}>داده‌های مرجع</button></nav>{page === 'products' ? <ProductMaster /> : page === 'parts' ? <MaterialItemMaster itemType="PART" /> : page === 'components' ? <MaterialItemMaster itemType="COMPONENT" /> : page === 'suppliers' ? <SupplierMaster /> : page === 'sourcing' ? <SourcingMaster /> : <ReferenceDataPage />}</>
}

export default App
