import { FormEvent, useCallback, useEffect, useState } from 'react'
import hadronLogo from './assets/brand/hadron-logo-red.svg'

type ItemType = 'PART' | 'COMPONENT'
type Ref = { id: string; code: string; nameFa: string; nameEn: string; isActive: boolean; symbol?: string | null }
type MaterialItem = { id: string; itemType: ItemType; number: string; code?: string | null; nameFa: string; nameEn?: string | null; category: Ref; baseUnit: Ref; supplyMethod: string; netWeightKg?: number | null; defaultPlanningScrapPercent: number; safetyStockQty: number; criticalDays: number; safeStartDays: number; excessStartDays: number; isActive: boolean; notes?: string | null; version: number }
type Editor = { id?: string; number?: string; code: string; nameFa: string; nameEn: string; categoryId: string; baseUnitId: string; supplyMethod: string; netWeightKg: string; defaultPlanningScrapPercent: number; safetyStockQty: number; criticalDays: number; safeStartDays: number; excessStartDays: number; notes: string; version?: number }
type ProblemBody = { title?: string; detail?: string; code?: string; details?: Record<string, string>; errors?: Record<string, string[]> }

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const supplyLabels: Record<string, string> = { BUY: 'خرید', MAKE: 'ساخت داخل', OUTSOURCE: 'برون‌سپاری', HYBRID: 'ترکیبی' }
const empty: Editor = { code: '', nameFa: '', nameEn: '', categoryId: '', baseUnitId: '', supplyMethod: 'BUY', netWeightKg: '', defaultPlanningScrapPercent: 0, safetyStockQty: 0, criticalDays: 0, safeStartDays: 0, excessStartDays: 0, notes: '' }

export default function MaterialItemMaster({ itemType }: { itemType: ItemType }) {
  const resource = itemType === 'PART' ? 'parts' : 'components'
  const categoryType = itemType === 'PART' ? 'PART_CATEGORY' : 'COMPONENT_CATEGORY'
  const title = itemType === 'PART' ? 'قطعات' : 'کامپوننت‌ها'
  const singular = itemType === 'PART' ? 'قطعه' : 'کامپوننت'
  const [items, setItems] = useState<MaterialItem[]>([]), [categories, setCategories] = useState<Ref[]>([]), [units, setUnits] = useState<Ref[]>([])
  const [search, setSearch] = useState(''), [includeInactive, setIncludeInactive] = useState(false), [categoryFilter, setCategoryFilter] = useState(''), [supplyFilter, setSupplyFilter] = useState('')
  const [editor, setEditor] = useState<Editor | null>(null), [page, setPage] = useState(1), [total, setTotal] = useState(0)
  const [error, setError] = useState(''), [message, setMessage] = useState(''), [warnings, setWarnings] = useState<string[]>([])

  const load = useCallback(async () => {
    const query = new URLSearchParams({ search, includeInactive: String(includeInactive), page: String(page), pageSize: '25' })
    if (categoryFilter) query.set('categoryId', categoryFilter)
    if (supplyFilter) query.set('supplyMethod', supplyFilter)
    const response = await fetch(`${api}/${resource}?${query}`)
    if (!response.ok) throw new Error(await problem(response))
    const body = await response.json()
    setItems(body.items); setTotal(body.totalCount)
  }, [resource, search, includeInactive, categoryFilter, supplyFilter, page])

  useEffect(() => {
    void Promise.all([
      fetch(`${api}/reference-data/types/${categoryType}/values`).then(response => response.json()),
      fetch(`${api}/reference-data/units`).then(response => response.json()),
    ]).then(([categoryRows, unitRows]) => { setCategories(categoryRows); setUnits(unitRows) }).catch(caught => setError(String(caught)))
  }, [categoryType])
  useEffect(() => { const timer = setTimeout(() => void load().catch(caught => setError(caught.message)), 150); return () => clearTimeout(timer) }, [load])

  async function save(event: FormEvent) {
    event.preventDefault(); if (!editor) return
    setError(''); setWarnings([])
    const payload = { ...editor, code: editor.code || null, nameEn: editor.nameEn || null, netWeightKg: editor.netWeightKg === '' ? null : Number(editor.netWeightKg), notes: editor.notes || null, expectedVersion: editor.version }
    const response = await fetch(`${api}/${resource}${editor.id ? `/${editor.id}` : ''}`, { method: editor.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
    if (!response.ok) { setError(await problem(response)); return }
    const result = await response.json(); setWarnings(result.warnings ?? []); setMessage(`${singular} با موفقیت ذخیره شد.`); setEditor(null); await load()
  }
  async function changeState(item: MaterialItem) {
    const action = item.isActive ? 'deactivate' : 'reactivate'
    const response = await fetch(`${api}/${resource}/${item.id}/${action}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: item.version }) })
    if (!response.ok) { setError(await problem(response)); return }
    await load()
  }
  const edit = (item: MaterialItem): Editor => ({ id: item.id, number: item.number, code: item.code ?? '', nameFa: item.nameFa, nameEn: item.nameEn ?? '', categoryId: item.category.id, baseUnitId: item.baseUnit.id, supplyMethod: item.supplyMethod, netWeightKg: item.netWeightKg == null ? '' : String(item.netWeightKg), defaultPlanningScrapPercent: item.defaultPlanningScrapPercent, safetyStockQty: item.safetyStockQty, criticalDays: item.criticalDays, safeStartDays: item.safeStartDays, excessStartDays: item.excessStartDays, notes: item.notes ?? '', version: item.version })

  return <main className="app-shell" dir="rtl"><header className="app-header"><img className="brand-logo" src={hadronLogo} alt="هادرون توان"/><div><p className="eyebrow">Hadron ERP</p><h1>مدیریت {title}</h1></div></header>
    <section className="toolbar material-filters"><label>جست‌وجو<input value={search} onChange={event=>{setSearch(event.target.value);setPage(1)}} placeholder="شماره، کد یا نام"/></label><label>گروه<select value={categoryFilter} onChange={event=>{setCategoryFilter(event.target.value);setPage(1)}}><option value="">همه گروه‌ها</option>{categories.map(item=><option key={item.id} value={item.id}>{item.nameFa}</option>)}</select></label><label>روش تأمین<select value={supplyFilter} onChange={event=>{setSupplyFilter(event.target.value);setPage(1)}}><option value="">همه روش‌ها</option>{Object.entries(supplyLabels).map(([code,label])=><option key={code} value={code}>{label}</option>)}</select></label><label className="checkbox-label"><input type="checkbox" checked={includeInactive} onChange={event=>{setIncludeInactive(event.target.checked);setPage(1)}}/>نمایش غیرفعال‌ها</label><button className="button button--primary" onClick={()=>setEditor({...empty})}>{singular} جدید</button></section>
    {message&&<p className="notice notice--success">{message}</p>}{error&&<p className="notice notice--error">{error}</p>}{warnings.length>0&&<p className="notice">هشدار نام تکراری: {warnings.join('، ')}</p>}
    <section className="data-panel"><div className="panel-title"><h2>فهرست {title}</h2><span>{total} مورد</span></div><div className="table-wrap"><table><thead><tr><th>شماره</th><th>کد</th><th>نام فارسی</th><th>گروه</th><th>روش تأمین</th><th>واحد</th><th>وضعیت</th><th>عملیات</th></tr></thead><tbody>{items.map(item=><tr key={item.id}><td dir="ltr">{item.number}</td><td dir="ltr">{item.code ?? '—'}</td><td>{item.nameFa}</td><td>{item.category.nameFa}{!item.category.isActive&&' (غیرفعال)'}</td><td>{supplyLabels[item.supplyMethod]}</td><td>{item.baseUnit.nameFa}{!item.baseUnit.isActive&&' (غیرفعال)'}</td><td>{item.isActive?'فعال':'غیرفعال'}</td><td className="actions"><button onClick={()=>setEditor(edit(item))}>جزئیات / ویرایش</button><button onClick={()=>void changeState(item)}>{item.isActive?'غیرفعال‌سازی':'فعال‌سازی'}</button></td></tr>)}</tbody></table></div><div className="pager"><button disabled={page===1} onClick={()=>setPage(page-1)}>قبلی</button><span>صفحه {page}</span><button disabled={page*25>=total} onClick={()=>setPage(page+1)}>بعدی</button></div></section>
    {editor&&<div className="dialog-backdrop"><section className="dialog product-dialog" role="dialog" aria-modal="true"><h2>{editor.id?`ویرایش ${singular}`:`ایجاد ${singular}`}</h2><form onSubmit={event=>void save(event)}>{editor.id&&<label>شماره<input value={editor.number} disabled/></label>}<label>کد اختیاری<input dir="ltr" maxLength={64} value={editor.code} onChange={event=>setEditor({...editor,code:event.target.value})}/></label><label>نام فارسی<input required maxLength={200} value={editor.nameFa} onChange={event=>setEditor({...editor,nameFa:event.target.value})}/></label><label>نام انگلیسی<input dir="ltr" maxLength={200} value={editor.nameEn} onChange={event=>setEditor({...editor,nameEn:event.target.value})}/></label><label>گروه<select required value={editor.categoryId} onChange={event=>setEditor({...editor,categoryId:event.target.value})}><option value="">انتخاب کنید</option>{selectorOptions(categories,items.find(item=>item.id===editor.id)?.category).map(item=><option key={item.id} value={item.id}>{item.nameFa}{!item.isActive?' (غیرفعال)':''}</option>)}</select></label><label>واحد پایه<select required value={editor.baseUnitId} onChange={event=>setEditor({...editor,baseUnitId:event.target.value})}><option value="">انتخاب کنید</option>{selectorOptions(units,items.find(item=>item.id===editor.id)?.baseUnit).map(item=><option key={item.id} value={item.id}>{item.nameFa}{!item.isActive?' (غیرفعال)':''}</option>)}</select></label><label>روش تأمین<select required value={editor.supplyMethod} onChange={event=>setEditor({...editor,supplyMethod:event.target.value})}>{Object.entries(supplyLabels).map(([code,label])=><option key={code} value={code}>{label}</option>)}</select></label><label>وزن خالص کیلوگرم<input type="number" min="0" step="0.000001" value={editor.netWeightKg} onChange={event=>setEditor({...editor,netWeightKg:event.target.value})}/></label><label>ضایعات پیش‌فرض ٪<input type="number" min="0" max="100" step="0.01" value={editor.defaultPlanningScrapPercent} onChange={event=>setEditor({...editor,defaultPlanningScrapPercent:Number(event.target.value)})}/></label><label>موجودی اطمینان<input type="number" min="0" step="0.000001" value={editor.safetyStockQty} onChange={event=>setEditor({...editor,safetyStockQty:Number(event.target.value)})}/></label><label>روز بحرانی<input type="number" min="0" step="1" value={editor.criticalDays} onChange={event=>setEditor({...editor,criticalDays:Number(event.target.value)})}/></label><label>شروع محدوده امن<input type="number" min="0" step="1" value={editor.safeStartDays} onChange={event=>setEditor({...editor,safeStartDays:Number(event.target.value)})}/></label><label>شروع محدوده مازاد<input type="number" min="0" step="1" value={editor.excessStartDays} onChange={event=>setEditor({...editor,excessStartDays:Number(event.target.value)})}/></label><label>یادداشت<textarea maxLength={2000} value={editor.notes} onChange={event=>setEditor({...editor,notes:event.target.value})}/></label><div className="dialog-actions"><button className="button button--primary">ذخیره</button><button className="button" type="button" onClick={()=>setEditor(null)}>انصراف</button></div></form></section></div>}
  </main>
}

function selectorOptions(active: Ref[], current?: Ref | null) { return current&&!current.isActive&&!active.some(item=>item.id===current.id)?[...active,current]:active }
async function problem(response: Response) { const body=await response.json().catch(()=>null) as ProblemBody|null; const validation=Object.values(body?.errors??{}).flat(); const details=Object.entries(body?.details??{}).map(([key,value])=>`${key}: ${value}`); return [`HTTP ${response.status}`,body?.code,body?.detail??body?.title,...validation,...details].filter(Boolean).join(' — ')||'عملیات ناموفق بود.' }
