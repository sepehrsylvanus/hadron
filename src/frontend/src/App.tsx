import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useState } from 'react'
import { BrowserRouter, Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  Component,
  Database,
  House,
  Languages,
  Network,
  PackageOpen,
  PanelLeftClose,
  PanelLeftOpen,
  PanelRightClose,
  PanelRightOpen,
  Route as SourcingRoute,
  UsersRound,
  Wrench,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import officialHadronLogo from '../../../docs/brand/Hadron Logo new-01.png'
import ProductMaster from './ProductMaster'
import SupplierMaster from './SupplierMaster'
import MaterialItemMaster from './MaterialItemMaster'
import SourcingMaster from './SourcingMaster'
import BomMaster from './BomMaster'
import { languageDirection, normalizeLanguage, type SupportedLanguage } from './i18n'

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
    <main className="app-shell">
      <header className="app-header">
        <div><h1>مدیریت داده‌های مرجع</h1></div>
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

type NavigationItem = { labelKey: string; path: string; Icon: LucideIcon }
type NavigationGroup = { labelKey: string; items: NavigationItem[] }

const navigationGroups: NavigationGroup[] = [
  {
    labelKey: 'navigation.groups.masterData',
    items: [
      { labelKey: 'navigation.items.products', path: '/mrp/master-data/products', Icon: PackageOpen },
      { labelKey: 'navigation.items.parts', path: '/mrp/master-data/parts', Icon: Wrench },
      { labelKey: 'navigation.items.components', path: '/mrp/master-data/components', Icon: Component },
      { labelKey: 'navigation.items.referenceData', path: '/mrp/master-data/reference-data', Icon: Database },
    ],
  },
  {
    labelKey: 'navigation.groups.suppliersAndSourcing',
    items: [
      { labelKey: 'navigation.items.suppliers', path: '/mrp/supply/suppliers', Icon: UsersRound },
      { labelKey: 'navigation.items.sourcing', path: '/mrp/supply/sourcing', Icon: SourcingRoute },
    ],
  },
  {
    labelKey: 'navigation.groups.engineering',
    items: [{ labelKey: 'navigation.items.bom', path: '/mrp/engineering/bom', Icon: Network }],
  },
]

function findRoute(pathname: string) {
  for (const group of navigationGroups) {
    const item = group.items.find((candidate) => candidate.path === pathname)
    if (item) return { group, item }
  }
  return null
}

function AppShell({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation()
  const location = useLocation()
  const inMrpWorkspace = location.pathname === '/mrp' || location.pathname.startsWith('/mrp/')
  const currentRoute = findRoute(location.pathname)
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const direction = languageDirection(language)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(
    () => window.localStorage.getItem('hadron.erp.sidebar.collapsed') === 'true',
  )

  useEffect(() => {
    window.localStorage.setItem('hadron.erp.sidebar.collapsed', String(sidebarCollapsed))
  }, [sidebarCollapsed])

  useEffect(() => {
    const pageTitle = currentRoute ? t(currentRoute.item.labelKey) : (inMrpWorkspace ? 'MRP' : t('shell.home'))
    document.title = `${t('productName')} — ${pageTitle}`
  }, [currentRoute, inMrpWorkspace, language, t])

  function changeLanguage(nextLanguage: SupportedLanguage) {
    void i18n.changeLanguage(nextLanguage)
  }

  const ExpandedToggleIcon = direction === 'rtl' ? PanelRightClose : PanelLeftClose
  const CollapsedToggleIcon = direction === 'rtl' ? PanelRightOpen : PanelLeftOpen

  return (
    <div className="erp-root" dir={direction} lang={language}>
      <header className="erp-shellbar">
        <Link className="shell-logo-link" to="/" aria-label={t('shell.logoHomeAria')}>
          <img className="shell-logo-image" src={officialHadronLogo} alt="" />
        </Link>
        <div className="shell-identity">
          <strong>{t('productName')}</strong>
          <span>{inMrpWorkspace ? t('shell.mrpContext') : t('shell.systemDescription')}</span>
        </div>
        <div className="shell-actions">
          <label className="language-switcher">
            <Languages aria-hidden="true" />
            <span>{t('language.label')}</span>
            <select
              value={language}
              aria-label={t('language.selectAria')}
              onChange={(event) => changeLanguage(event.target.value as SupportedLanguage)}
            >
              <option value="fa-IR">{t('language.persian')}</option>
              <option value="en">{t('language.english')}</option>
            </select>
          </label>
          <div className="shell-context" aria-label={t('shell.workspace')}>
            <span>{t('shell.workspace')}</span>
            <strong>{inMrpWorkspace ? 'MRP' : t('shell.home')}</strong>
          </div>
        </div>
      </header>

      {inMrpWorkspace ? (
        <div className={`erp-shell-body${sidebarCollapsed ? ' sidebar-collapsed' : ''}`}>
          <aside className="mrp-sidebar" aria-label={t('navigation.sidebarAria')}>
            <div className="sidebar-heading">
              <div><span>{t('shell.workspace')}</span><strong>MRP</strong></div>
              <button
                type="button"
                className="sidebar-toggle"
                aria-expanded={!sidebarCollapsed}
                aria-label={sidebarCollapsed ? t('navigation.expand') : t('navigation.collapse')}
                onClick={() => setSidebarCollapsed((value) => !value)}
              >
                {sidebarCollapsed
                  ? <CollapsedToggleIcon aria-hidden="true" />
                  : <ExpandedToggleIcon aria-hidden="true" />}
              </button>
            </div>
            <NavLink
              className={({ isActive }) => `sidebar-overview${isActive ? ' active' : ''}`}
              to="/mrp"
              end
              title={sidebarCollapsed ? t('navigation.overview') : undefined}
              aria-label={sidebarCollapsed ? t('navigation.overview') : undefined}
            >
              <House className="sidebar-icon" aria-hidden="true" />
              <span className="sidebar-label">{t('navigation.overview')}</span>
            </NavLink>
            {navigationGroups.map((group) => {
              const groupActive = group.items.some((item) => item.path === location.pathname)
              const groupLabel = t(group.labelKey)
              return (
                <section className={`sidebar-group${groupActive ? ' active' : ''}`} key={group.labelKey}>
                  {!sidebarCollapsed && <h2>{groupLabel}</h2>}
                  <nav aria-label={groupLabel}>
                    {group.items.map((item) => {
                      const itemLabel = t(item.labelKey)
                      return (
                        <NavLink
                          key={item.path}
                          to={item.path}
                          title={sidebarCollapsed ? itemLabel : undefined}
                          aria-label={sidebarCollapsed ? itemLabel : undefined}
                          className={({ isActive }) => isActive ? 'active' : undefined}
                        >
                          <item.Icon className="sidebar-icon" aria-hidden="true" />
                          <span className="sidebar-label">{itemLabel}</span>
                        </NavLink>
                      )
                    })}
                  </nav>
                </section>
              )
            })}
          </aside>
          <div className="erp-workspace-content">
            <Breadcrumbs />
            <div className="module-content">{children}</div>
          </div>
        </div>
      ) : (
        <div className="erp-home-content">{children}</div>
      )}
    </div>
  )
}

function Breadcrumbs() {
  const { t } = useTranslation()
  const location = useLocation()
  const currentRoute = findRoute(location.pathname)
  return (
    <nav className="breadcrumbs" aria-label={t('breadcrumbs.aria')}>
      <Link to="/">{t('productName')}</Link>
      <span aria-hidden="true">/</span>
      {location.pathname === '/mrp' ? <strong>MRP</strong> : <Link to="/mrp">MRP</Link>}
      {currentRoute && <>
        <span aria-hidden="true">/</span>
        <span>{t(currentRoute.group.labelKey)}</span>
        <span aria-hidden="true">/</span>
        <strong>{t(currentRoute.item.labelKey)}</strong>
      </>}
    </nav>
  )
}

function ErpHome() {
  const { t } = useTranslation()
  return (
    <main className="launchpad-page">
      <section className="launchpad-hero">
        <div className="home-logo-stage">
          <img src={officialHadronLogo} alt="Hadron — The Intelligent Innovation" />
        </div>
        <p className="eyebrow">{t('home.eyebrow')}</p>
        <h1>{t('productName')}</h1>
        <p>{t('home.description')}</p>
      </section>
      <section className="workspace-launcher" aria-labelledby="workspace-title">
        <div className="section-heading">
          <div><p className="eyebrow">{t('home.workspacesEyebrow')}</p><h2 id="workspace-title">{t('home.workspaces')}</h2></div>
          <span>{t('home.activeWorkspaceCount')}</span>
        </div>
        <Link className="workspace-card" to="/mrp">
          <span className="workspace-code">MRP</span>
          <div>
            <h3>{t('home.mrpTitle')}</h3>
            <p>{t('home.mrpDescription')}</p>
          </div>
          <strong>{t('home.enterWorkspace')}</strong>
        </Link>
      </section>
    </main>
  )
}

function MrpWorkspaceHome() {
  const { t } = useTranslation()
  return (
    <main className="workspace-home">
      <header className="workspace-home-header">
        <p className="eyebrow">{t('mrpWorkspace.eyebrow')}</p>
        <h1>{t('mrpWorkspace.title')}</h1>
        <p>{t('mrpWorkspace.description')}</p>
      </header>
      <div className="workspace-group-grid">
        {navigationGroups.map((group) => (
          <section className="workspace-group-card" key={group.labelKey}>
            <h2>{t(group.labelKey)}</h2>
            <div>{group.items.map((item) => <Link key={item.path} to={item.path}>{t(item.labelKey)}</Link>)}</div>
          </section>
        ))}
      </div>
    </main>
  )
}

function App() {
  return (
    <BrowserRouter>
      <AppShell>
        <Routes>
          <Route path="/" element={<ErpHome />} />
          <Route path="/mrp" element={<MrpWorkspaceHome />} />
          <Route path="/mrp/master-data/products" element={<ProductMaster />} />
          <Route path="/mrp/master-data/parts" element={<MaterialItemMaster itemType="PART" />} />
          <Route path="/mrp/master-data/components" element={<MaterialItemMaster itemType="COMPONENT" />} />
          <Route path="/mrp/master-data/reference-data" element={<ReferenceDataPage />} />
          <Route path="/mrp/supply/suppliers" element={<SupplierMaster />} />
          <Route path="/mrp/supply/sourcing" element={<SourcingMaster />} />
          <Route path="/mrp/engineering/bom" element={<BomMaster />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AppShell>
    </BrowserRouter>
  )
}

export default App
