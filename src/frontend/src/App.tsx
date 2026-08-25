import { ReactNode, useEffect, useState } from 'react'
import { BrowserRouter, Link, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Boxes, CalendarRange, Calculator, ChartNoAxesCombined, Component, Database, House, Languages, Network, PackageOpen, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, Route as SourcingRoute, ShoppingCart, UsersRound, Wrench } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import officialHadronLogo from '../../../docs/brand/Hadron Logo new-01.png'
import ProductMaster, { ProductObjectPage } from './ProductMaster'
import SupplierMaster, { SupplierObjectPage } from './SupplierMaster'
import MaterialItemMaster, { MaterialItemObjectPage } from './MaterialItemMaster'
import SourcingMaster, { SourcingObjectPage } from './SourcingMaster'
import BomMaster from './BomMaster'
import ReferenceDataPage from './ReferenceDataPage'
import ForecastActualPage from './ForecastActualPage'
import InventoryPage from './InventoryPage'
import PurchaseOrderPage from './PurchaseOrderPage'
import MrpRunsPage from './MrpRunsPage'
import MaterialPlanningPage from './MaterialPlanningPage'
import { languageDirection, normalizeLanguage, type SupportedLanguage } from './i18n'

type NavigationItem = { labelKey: string; path: string; Icon: LucideIcon; aliases?: string[] }
type NavigationGroup = { labelKey: string; items: NavigationItem[] }
const navigationGroups: NavigationGroup[] = [
  { labelKey: 'navigation.groups.masterData', items: [
    { labelKey: 'navigation.items.products', path: '/mrp/master-data/products', aliases: ['/mrp/products'], Icon: PackageOpen },
    { labelKey: 'navigation.items.parts', path: '/mrp/master-data/parts', aliases: ['/mrp/parts'], Icon: Wrench },
    { labelKey: 'navigation.items.components', path: '/mrp/master-data/components', aliases: ['/mrp/components'], Icon: Component },
    { labelKey: 'navigation.items.referenceData', path: '/mrp/master-data/reference-data', Icon: Database },
  ] },
  { labelKey: 'navigation.groups.suppliersAndSourcing', items: [
    { labelKey: 'navigation.items.suppliers', path: '/mrp/supply/suppliers', aliases: ['/mrp/suppliers'], Icon: UsersRound },
    { labelKey: 'navigation.items.sourcing', path: '/mrp/supply/sourcing', aliases: ['/mrp/sourcing'], Icon: SourcingRoute },
    { labelKey: 'navigation.items.purchaseOrders', path: '/mrp/supply/purchase-orders', aliases: ['/mrp/purchase-orders'], Icon: ShoppingCart },
  ] },
  { labelKey: 'navigation.groups.engineering', items: [{ labelKey: 'navigation.items.bom', path: '/mrp/engineering/bom', Icon: Network }] },
  { labelKey: 'navigation.groups.planning', items: [
    { labelKey: 'navigation.items.forecastActual', path: '/mrp/planning/forecast-actual', Icon: CalendarRange },
    { labelKey: 'navigation.items.mrpRuns', path: '/mrp/planning/mrp-runs', Icon: Calculator },
    { labelKey: 'navigation.items.materialPlanning', path: '/mrp/planning/material-planning', Icon: ChartNoAxesCombined },
  ] },
  { labelKey: 'navigation.groups.inventory', items: [{ labelKey: 'navigation.items.inventory', path: '/mrp/inventory', aliases: ['/mrp/inventory'], Icon: Boxes }] },
]

function findRoute(pathname: string) {
  for (const group of navigationGroups) {
    const item = group.items.find(candidate => candidate.path === pathname || candidate.aliases?.some(alias => pathname === alias || pathname.startsWith(`${alias}/`)))
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
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => window.localStorage.getItem('hadron.erp.sidebar.collapsed') === 'true')
  useEffect(() => { window.localStorage.setItem('hadron.erp.sidebar.collapsed', String(sidebarCollapsed)) }, [sidebarCollapsed])
  useEffect(() => {
    const pageTitle = currentRoute ? t(currentRoute.item.labelKey) : inMrpWorkspace ? 'MRP' : t('shell.home')
    document.title = `${t('productName')} — ${pageTitle}`
  }, [currentRoute, inMrpWorkspace, language, t])
  const ExpandedToggleIcon = direction === 'rtl' ? PanelRightClose : PanelLeftClose
  const CollapsedToggleIcon = direction === 'rtl' ? PanelRightOpen : PanelLeftOpen
  return <div className="erp-root" dir={direction} lang={language}>
    <header className="erp-shellbar">
      <Link className="shell-logo-link" to="/" aria-label={t('shell.logoHomeAria')}><img className="shell-logo-image" src={officialHadronLogo} alt="" /></Link>
      <div className="shell-identity"><strong>{t('productName')}</strong><span>{inMrpWorkspace ? t('shell.mrpContext') : t('shell.systemDescription')}</span></div>
      <div className="shell-actions"><label className="language-switcher"><Languages aria-hidden="true" /><span>{t('language.label')}</span><select value={language} aria-label={t('language.selectAria')} onChange={event => void i18n.changeLanguage(event.target.value as SupportedLanguage)}><option value="fa-IR">{t('language.persian')}</option><option value="en">{t('language.english')}</option></select></label><div className="shell-context" aria-label={t('shell.workspace')}><span>{t('shell.workspace')}</span><strong>{inMrpWorkspace ? 'MRP' : t('shell.home')}</strong></div></div>
    </header>
    {inMrpWorkspace ? <div className={`erp-shell-body${sidebarCollapsed ? ' sidebar-collapsed' : ''}`}>
      <aside className="mrp-sidebar" aria-label={t('navigation.sidebarAria')}>
        <div className="sidebar-heading"><div><span>{t('shell.workspace')}</span><strong>MRP</strong></div><button type="button" className="sidebar-toggle" aria-expanded={!sidebarCollapsed} aria-label={sidebarCollapsed ? t('navigation.expand') : t('navigation.collapse')} onClick={() => setSidebarCollapsed(value => !value)}>{sidebarCollapsed ? <CollapsedToggleIcon aria-hidden="true" /> : <ExpandedToggleIcon aria-hidden="true" />}</button></div>
        <NavLink className={({ isActive }) => `sidebar-overview${isActive ? ' active' : ''}`} to="/mrp" end title={sidebarCollapsed ? t('navigation.overview') : undefined}><House className="sidebar-icon" aria-hidden="true" /><span className="sidebar-label">{t('navigation.overview')}</span></NavLink>
        {navigationGroups.map(group => { const groupActive = group.items.some(item => item === currentRoute?.item); const groupLabel = t(group.labelKey); return <section className={`sidebar-group${groupActive ? ' active' : ''}`} key={group.labelKey}>{!sidebarCollapsed && <h2>{groupLabel}</h2>}<nav aria-label={groupLabel}>{group.items.map(item => { const itemLabel = t(item.labelKey); const isActive = item === currentRoute?.item; return <Link key={item.path} to={item.path} title={sidebarCollapsed ? itemLabel : undefined} aria-label={sidebarCollapsed ? itemLabel : undefined} className={isActive ? 'active' : undefined}><item.Icon className="sidebar-icon" aria-hidden="true" /><span className="sidebar-label">{itemLabel}</span></Link> })}</nav></section> })}
      </aside>
      <div className="erp-workspace-content"><Breadcrumbs /><div className="module-content">{children}</div></div>
    </div> : <div className="erp-home-content">{children}</div>}
  </div>
}

function Breadcrumbs() {
  const { t } = useTranslation(); const location = useLocation(); const currentRoute = findRoute(location.pathname)
  const objectMode = currentRoute && location.pathname !== currentRoute.item.path && currentRoute.item.aliases?.some(alias => location.pathname.startsWith(`${alias}/`))
  const leaf = location.pathname.endsWith('/new') ? t('common.create') : location.pathname.endsWith('/edit') ? t('common.edit') : objectMode ? t('common.details') : null
  return <nav className="breadcrumbs" aria-label={t('breadcrumbs.aria')}><Link to="/">{t('productName')}</Link><span aria-hidden="true">/</span>{location.pathname === '/mrp' ? <strong>MRP</strong> : <Link to="/mrp">MRP</Link>}{currentRoute && <><span aria-hidden="true">/</span><span>{t(currentRoute.group.labelKey)}</span><span aria-hidden="true">/</span>{leaf ? <Link to={currentRoute.item.path}>{t(currentRoute.item.labelKey)}</Link> : <strong>{t(currentRoute.item.labelKey)}</strong>}</>}{leaf && <><span aria-hidden="true">/</span><strong>{leaf}</strong></>}</nav>
}

function ErpHome() {
  const { t } = useTranslation()
  return <main className="launchpad-page"><section className="launchpad-hero"><div className="home-logo-stage"><img src={officialHadronLogo} alt="Hadron — The Intelligent Innovation" /></div><p className="eyebrow">{t('home.eyebrow')}</p><h1>{t('productName')}</h1><p>{t('home.description')}</p></section><section className="workspace-launcher" aria-labelledby="workspace-title"><div className="section-heading"><div><p className="eyebrow">{t('home.workspacesEyebrow')}</p><h2 id="workspace-title">{t('home.workspaces')}</h2></div><span>{t('home.activeWorkspaceCount')}</span></div><Link className="workspace-card" to="/mrp"><span className="workspace-code">MRP</span><div><h3>{t('home.mrpTitle')}</h3><p>{t('home.mrpDescription')}</p></div><strong>{t('home.enterWorkspace')}</strong></Link></section></main>
}

function MrpWorkspaceHome() {
  const { t } = useTranslation()
  return <main className="workspace-home"><header className="workspace-home-header"><p className="eyebrow">{t('mrpWorkspace.eyebrow')}</p><h1>{t('mrpWorkspace.title')}</h1><p>{t('mrpWorkspace.description')}</p></header><div className="workspace-group-grid">{navigationGroups.map(group => <section className="workspace-group-card" key={group.labelKey}><h2>{t(group.labelKey)}</h2><div>{group.items.map(item => <Link key={item.path} to={item.path}>{t(item.labelKey)}</Link>)}</div></section>)}</div></main>
}

function App() {
  return <BrowserRouter><AppShell><Routes>
    <Route path="/" element={<ErpHome />} /><Route path="/mrp" element={<MrpWorkspaceHome />} />
    <Route path="/mrp/master-data/products" element={<ProductMaster />} /><Route path="/mrp/products/new" element={<ProductObjectPage />} /><Route path="/mrp/products/:id" element={<ProductObjectPage />} /><Route path="/mrp/products/:id/edit" element={<ProductObjectPage />} />
    <Route path="/mrp/master-data/parts" element={<MaterialItemMaster itemType="PART" />} /><Route path="/mrp/parts/new" element={<MaterialItemObjectPage itemType="PART" />} /><Route path="/mrp/parts/:id" element={<MaterialItemObjectPage itemType="PART" />} /><Route path="/mrp/parts/:id/edit" element={<MaterialItemObjectPage itemType="PART" />} />
    <Route path="/mrp/master-data/components" element={<MaterialItemMaster itemType="COMPONENT" />} /><Route path="/mrp/components/new" element={<MaterialItemObjectPage itemType="COMPONENT" />} /><Route path="/mrp/components/:id" element={<MaterialItemObjectPage itemType="COMPONENT" />} /><Route path="/mrp/components/:id/edit" element={<MaterialItemObjectPage itemType="COMPONENT" />} />
    <Route path="/mrp/master-data/reference-data" element={<ReferenceDataPage />} />
    <Route path="/mrp/supply/suppliers" element={<SupplierMaster />} /><Route path="/mrp/suppliers/new" element={<SupplierObjectPage />} /><Route path="/mrp/suppliers/:id" element={<SupplierObjectPage />} /><Route path="/mrp/suppliers/:id/edit" element={<SupplierObjectPage />} />
    <Route path="/mrp/supply/sourcing" element={<SourcingMaster />} /><Route path="/mrp/sourcing/new" element={<SourcingObjectPage />} /><Route path="/mrp/sourcing/:id" element={<SourcingObjectPage />} /><Route path="/mrp/sourcing/:id/edit" element={<SourcingObjectPage />} />
    <Route path="/mrp/supply/purchase-orders" element={<PurchaseOrderPage view="list" />} /><Route path="/mrp/purchase-orders/new" element={<PurchaseOrderPage view="create" />} /><Route path="/mrp/purchase-orders/:id" element={<PurchaseOrderPage view="object" />} />
    <Route path="/mrp/engineering/bom" element={<BomMaster />} /><Route path="/mrp/planning/forecast-actual" element={<ForecastActualPage />} />
    <Route path="/mrp/planning/mrp-runs" element={<MrpRunsPage view="list" />} /><Route path="/mrp/planning/mrp-runs/new" element={<MrpRunsPage view="create" />} /><Route path="/mrp/planning/mrp-runs/:id" element={<MrpRunsPage view="object" />} />
    <Route path="/mrp/planning/material-planning" element={<MaterialPlanningPage />} />
    <Route path="/mrp/inventory" element={<InventoryPage view="balances" />} /><Route path="/mrp/inventory/items/:id" element={<InventoryPage view="item" />} />
    <Route path="/mrp/inventory/warehouses" element={<InventoryPage view="warehouses" />} /><Route path="/mrp/inventory/warehouses/new" element={<InventoryPage view="warehouse" />} /><Route path="/mrp/inventory/warehouses/:id" element={<InventoryPage view="warehouse" />} /><Route path="/mrp/inventory/warehouses/:id/edit" element={<InventoryPage view="warehouse" />} />
    <Route path="/mrp/inventory/transactions" element={<InventoryPage view="transactions" />} /><Route path="/mrp/inventory/transactions/:id" element={<InventoryPage view="transaction" />} /><Route path="/mrp/inventory/post/:type" element={<InventoryPage view="post" />} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></AppShell></BrowserRouter>
}

export default App
