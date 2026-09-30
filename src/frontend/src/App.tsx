import { lazy, ReactNode, Suspense, useEffect, useState } from 'react'
import { BrowserRouter, Link, Navigate, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ArchiveRestore, ArrowLeftRight, BarChart3, BookOpenText, Boxes, BriefcaseBusiness, Building2, CalendarRange, Calculator, ChartNoAxesCombined, CircleDollarSign, ClipboardCheck, ClipboardList, ClipboardPlus, Component, Container, Database, Factory, FileClock, FileSearch, History, House, Languages, Landmark, LogOut, Mail, MessageSquareText, Network, PackageMinus, PackageOpen, PackagePlus, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, PhoneCall, ReceiptText, Route as SourcingRoute, Scale, Settings, ShieldCheck, ShoppingCart, UserRoundCog, UsersRound, Warehouse, Wrench } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import officialHadronLogo from './assets/brand/hadron-logo-red.svg'
import hadronCompanionWelcome from './assets/brand/character/hadron-companion-welcome.webp'
import { AuthProvider, useAuth } from './AuthContext'
import { apiRoot } from './apiClient'
import { languageDirection, normalizeLanguage, type SupportedLanguage } from './i18n'
import { MeetingCallProvider } from './MeetingCallContext'
import { analyticsRoutes, canManageRoute, canNavigate, canViewWorkspaceOverview, findWorkspace, findWorkspaceRoute, routePermission, type NavigationIconKey, workspaces } from './workspaceNavigation'

const ProductMaster = lazy(() => import('./ProductMaster'))
const ProductObjectPage = lazy(() => import('./ProductMaster').then(module => ({ default: module.ProductObjectPage })))
const SupplierMaster = lazy(() => import('./SupplierMaster'))
const InternalPeoplePage = lazy(() => import('./SupplierMaster').then(module => ({ default: module.InternalPeoplePage })))
const SupplierObjectPage = lazy(() => import('./SupplierMaster').then(module => ({ default: module.SupplierObjectPage })))
const MaterialItemMaster = lazy(() => import('./MaterialItemMaster'))
const MaterialItemObjectPage = lazy(() => import('./MaterialItemMaster').then(module => ({ default: module.MaterialItemObjectPage })))
const SourcingMaster = lazy(() => import('./SourcingMaster'))
const SourcingObjectPage = lazy(() => import('./SourcingMaster').then(module => ({ default: module.SourcingObjectPage })))
const BomMaster = lazy(() => import('./BomMaster'))
const ReferenceDataPage = lazy(() => import('./ReferenceDataPage'))
const ForecastActualPage = lazy(() => import('./ForecastActualPage'))
const ForecastBatchEntryPage = lazy(() => import('./ForecastBatchEntryPage'))
const ForecastPlanDetailPage = lazy(() => import('./ForecastPlanPages').then(module => ({ default: module.ForecastPlanDetailPage })))
const InventoryPage = lazy(() => import('./InventoryPage'))
const PurchaseOrderPage = lazy(() => import('./PurchaseOrderPage'))
const MrpRunsPage = lazy(() => import('./MrpRunsPage'))
const MaterialPlanningPage = lazy(() => import('./MaterialPlanningPage'))
const OrderPlanningPage = lazy(() => import('./OrderPlanningPage'))
const AnalyticsPage = lazy(() => import('./AnalyticsPage'))
const ProductionOrdersPage = lazy(() => import('./ProductionOrdersPage'))
const ProductionRoutingPage = lazy(() => import('./ProductionRoutingPage'))
const TradeProjectCreatePage = lazy(() => import('./TradingPage').then(module => ({ default: module.TradeProjectCreatePage })))
const TradeProjectListPage = lazy(() => import('./TradingPage').then(module => ({ default: module.TradeProjectListPage })))
const TradeProjectObjectPage = lazy(() => import('./TradingPage').then(module => ({ default: module.TradeProjectObjectPage })))
const TradingOverviewPage = lazy(() => import('./TradingPage').then(module => ({ default: module.TradingOverviewPage })))
const TradingPerspectivePage = lazy(() => import('./TradingPage').then(module => ({ default: module.TradingPerspectivePage })))
const TradingReferencePage = lazy(() => import('./TradingPage').then(module => ({ default: module.TradingReferencePage })))
const CashFlowPage = lazy(() => import('./CashFlowPage'))
const CashFlowReportingRatesPage = lazy(() => import('./CashFlowReportingRatesPage'))
const CashFlowMaterialCostBooksPage = lazy(() => import('./CashFlowMaterialCostBooksPage'))
const CashFlowPurchasePriceHistoryPage = lazy(() => import('./CashFlowPurchasePriceHistoryPage'))
const IdentityRolePage = lazy(() => import('./IdentityWorkspacePages').then(module => ({ default: module.IdentityRolePage })))
const IdentityRolesPage = lazy(() => import('./IdentityWorkspacePages').then(module => ({ default: module.IdentityRolesPage })))
const IdentitySecurityAuditPage = lazy(() => import('./IdentityWorkspacePages').then(module => ({ default: module.IdentitySecurityAuditPage })))
const IdentityUserPage = lazy(() => import('./IdentityWorkspacePages').then(module => ({ default: module.IdentityUserPage })))
const IdentityUsersPage = lazy(() => import('./IdentityWorkspacePages').then(module => ({ default: module.IdentityUsersPage })))
const OrganizationalUnitsPage = lazy(() => import('./IdentityWorkspacePages').then(module => ({ default: module.OrganizationalUnitsPage })))
const CollaborationMessagesPage = lazy(() => import('./CollaborationPage').then(module => ({ default: module.CollaborationMessagesPage })))
const CollaborationLettersPage = lazy(() => import('./CollaborationPage').then(module => ({ default: module.CollaborationLettersPage })))
const CollaborationNotificationCenter = lazy(() => import('./CollaborationNotificationCenter'))
const CollaborationMeetingsPage = lazy(() => import('./CollaborationMeetingsPage'))
const PublicMeetingGuestPage = lazy(() => import('./PublicMeetingGuestPage'))

const navigationIcons: Record<NavigationIconKey, LucideIcon> = {
  adminRoles: ShieldCheck, adminUnits: Building2, adminUsers: UserRoundCog, securityAudit: FileClock,
  analytics: BarChart3, bom: Network, components: Component, dailyProduction: Factory,
  inventory: Boxes, inventoryAdjustment: Scale, inventoryIssue: PackageMinus,
  inventoryLedger: ArchiveRestore, inventoryOpening: ClipboardPlus, inventoryReceipt: PackagePlus,
  inventoryTransfer: ArrowLeftRight, warehouses: Warehouse,
  materialPlanning: ChartNoAxesCombined, mrpRuns: Calculator,
  orderPlanning: ClipboardCheck, parts: Wrench, products: PackageOpen, productionOrders: ClipboardList, productionRouting: Network,
  productionForecast: CalendarRange, purchaseOrders: ShoppingCart, referenceData: Database,
  sourcing: SourcingRoute, suppliers: UsersRound,
  tradeProjects: BriefcaseBusiness, tradeShipments: Container, tradeCustoms: Landmark,
  tradeCosts: ReceiptText, tradeReports: FileSearch, tradeReferences: Database,
  cashFlowObligations: CalendarRange, cashFlowCosting: Calculator, cashFlowMaterialBooks: BookOpenText,
  cashFlowPurchaseHistory: History, cashFlowReportingRates: CircleDollarSign,
  collaborationMessages: MessageSquareText, collaborationLetters: Mail, collaborationCalls: PhoneCall,
}

function AppShell({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation()
  const location = useLocation()
  const navigate = useNavigate()
  const currentWorkspace = findWorkspace(location.pathname)
  const currentRoute = findWorkspaceRoute(location.pathname)
  const currentPermission = routePermission(location.pathname)
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const direction = languageDirection(language)
  const { user, has, logout } = useAuth()
  const workspaceAccessible = !currentWorkspace || Boolean(currentWorkspace.entryPermission && has(currentWorkspace.entryPermission)) ||
    currentWorkspace.navigation.some(group => group.items.some(item => canNavigate(item.path, has)))
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => window.localStorage.getItem('hadron.erp.sidebar.collapsed') === 'true')
  useEffect(() => { window.localStorage.setItem('hadron.erp.sidebar.collapsed', String(sidebarCollapsed)) }, [sidebarCollapsed])
  useEffect(() => {
    const pageTitle = currentRoute ? t(currentRoute.item.labelKey) : currentWorkspace ? t(currentWorkspace.workspaceTitleKey) : t('shell.home')
    document.title = `${t('productName')} — ${pageTitle}`
  }, [currentRoute, currentWorkspace, language, t])
  const ExpandedToggleIcon = direction === 'rtl' ? PanelRightClose : PanelLeftClose
  const CollapsedToggleIcon = direction === 'rtl' ? PanelRightOpen : PanelLeftOpen
  return <div className="erp-root" dir={direction} lang={language}>
    <header className="erp-shellbar">
      <Link className="shell-logo-link" to="/" aria-label={t('shell.logoHomeAria')}><img className="shell-logo-image" src={officialHadronLogo} alt="" /></Link>
      <div className="shell-identity"><strong>{t('productName')}</strong><span>{currentWorkspace ? t(currentWorkspace.shellContextKey) : t('shell.systemDescription')}</span></div>
      <div className="shell-actions"><label className="language-switcher"><Languages aria-hidden="true" /><span>{t('language.label')}</span><select value={language} aria-label={t('language.selectAria')} onChange={event => void i18n.changeLanguage(event.target.value as SupportedLanguage)}><option value="fa-IR">{t('language.persian')}</option><option value="en">{t('language.english')}</option></select></label><div className="shell-context" aria-label={t('shell.workspace')}><span>{t('shell.workspace')}</span><strong>{currentWorkspace ? t(currentWorkspace.titleKey) : t('shell.home')}</strong></div><Suspense fallback={null}><CollaborationNotificationCenter /></Suspense>{(has('identity.user.view') || has('identity.role.view') || has('organization.unit.view') || has('identity.security_audit.view')) && <Link className="shell-icon-action" to="/administration" title={t('identity.adminTitle')}><Settings /></Link>}<div className="signed-in-user"><img src={`${apiRoot}${user.photoUrl.replace('/api', '')}`} alt="" /><span><strong>{user.fullName}</strong><small>{language === 'en' ? user.organizationalUnit.nameEn || user.organizationalUnit.nameFa : user.organizationalUnit.nameFa}</small></span><button onClick={() => void logout()} title={t('identity.logout')} aria-label={t('identity.logout')}><LogOut /></button></div></div>
    </header>
    {currentWorkspace ? <div className={`erp-shell-body${sidebarCollapsed ? ' sidebar-collapsed' : ''}`}>
      <aside className="mrp-sidebar" aria-label={t(currentWorkspace.sidebarAriaKey)}>
        <div className="sidebar-heading"><div><span>{t('shell.workspace')}</span><strong>{t(currentWorkspace.titleKey)}</strong></div><button type="button" className="sidebar-toggle" aria-expanded={!sidebarCollapsed} aria-label={sidebarCollapsed ? t(currentWorkspace.expandKey) : t(currentWorkspace.collapseKey)} onClick={() => setSidebarCollapsed(value => !value)}>{sidebarCollapsed ? <CollapsedToggleIcon aria-hidden="true" /> : <ExpandedToggleIcon aria-hidden="true" />}</button></div>
        {canViewWorkspaceOverview(currentWorkspace, has) && <NavLink className={({ isActive }) => `sidebar-overview${isActive ? ' active' : ''}`} to={currentWorkspace.rootPath} end title={sidebarCollapsed ? t('navigation.overview') : undefined}><House className="sidebar-icon" aria-hidden="true" /><span className="sidebar-label">{t('navigation.overview')}</span></NavLink>}
        {currentWorkspace.navigation.map(group => ({ ...group, items: group.items.filter(item => canNavigate(item.path, has)) })).filter(group => group.items.length > 0).map(group => { const groupActive = group.items.some(item => item === currentRoute?.item); const groupLabel = t(group.labelKey); return <section className={`sidebar-group${groupActive ? ' active' : ''}`} key={group.labelKey}>{!sidebarCollapsed && <h2>{groupLabel}</h2>}<nav aria-label={groupLabel}>{group.items.map(item => { const itemLabel = t(item.labelKey); const isActive = item === currentRoute?.item; const Icon = navigationIcons[item.icon]; const resetBom = item.path === '/mrp/engineering/bom'; return <Link key={item.path} to={item.path} onClick={resetBom ? event => { event.preventDefault(); navigate(item.path) } : undefined} title={sidebarCollapsed ? itemLabel : undefined} aria-label={sidebarCollapsed ? itemLabel : undefined} className={isActive ? 'active' : undefined}><Icon className="sidebar-icon" aria-hidden="true" /><span className="sidebar-label">{itemLabel}</span></Link> })}</nav></section> })}
      </aside>
      <div className="erp-workspace-content"><Breadcrumbs /><div className="module-content">{!workspaceAccessible || currentPermission && !canNavigate(location.pathname, has) ? <section className="ui-message ui-message--critical" role="alert">{t('identity.noPermission')}</section> : currentPermission && (location.pathname.endsWith('/new') || location.pathname.endsWith('/edit')) && !canManageRoute(location.pathname, has) ? <section className="ui-message ui-message--critical" role="alert">{t('identity.noPermission')}</section> : children}</div></div>
    </div> : <div className="erp-home-content">{children}</div>}
  </div>
}

function Breadcrumbs() {
  const { t } = useTranslation(); const location = useLocation(); const currentWorkspace = findWorkspace(location.pathname); const currentRoute = findWorkspaceRoute(location.pathname)
  const objectMode = currentRoute && location.pathname !== currentRoute.item.path && currentRoute.item.aliases?.some(alias => location.pathname.startsWith(`${alias}/`))
  const secondaryLeaf = location.pathname === '/mrp/supply/internal-people' ? t('suppliers.internalPeople') : null
  const leaf = secondaryLeaf ?? (location.pathname.endsWith('/new') ? t('common.create') : location.pathname.endsWith('/edit') ? t('common.edit') : objectMode ? t('common.details') : null)
  if (!currentWorkspace) return null
  const workspaceTitle = t(currentWorkspace.workspaceTitleKey)
  return <nav className="breadcrumbs" aria-label={t('breadcrumbs.aria')}><Link to="/">{t('productName')}</Link><span aria-hidden="true">/</span>{location.pathname === currentWorkspace.rootPath ? <strong>{workspaceTitle}</strong> : <Link to={currentWorkspace.rootPath}>{workspaceTitle}</Link>}{currentRoute && <><span aria-hidden="true">/</span><span>{t(currentRoute.group.labelKey)}</span><span aria-hidden="true">/</span>{leaf ? <Link to={currentRoute.item.path}>{t(currentRoute.item.labelKey)}</Link> : <strong>{t(currentRoute.item.labelKey)}</strong>}</>}{leaf && <><span aria-hidden="true">/</span><strong>{leaf}</strong></>}</nav>
}

function ErpHome() {
  const { t } = useTranslation()
  const { has } = useAuth(); const available = workspaces.filter(workspace => Boolean(workspace.entryPermission && has(workspace.entryPermission)) || workspace.navigation.some(group => group.items.some(item => canNavigate(item.path, has))))
  return <main className="launchpad-page"><section className="launchpad-hero"><div className="launchpad-hero__copy"><div className="home-logo-stage"><img src={officialHadronLogo} alt="Hadron — The Intelligent Innovation" /></div><p className="eyebrow">{t('home.eyebrow')}</p><h1>{t('productName')}</h1><p>{t('home.description')}</p></div><img className="launchpad-companion" src={hadronCompanionWelcome} alt="" width="240" height="300" loading="eager" aria-hidden="true" /></section><section className="workspace-launcher" aria-labelledby="workspace-title"><div className="section-heading"><div><p className="eyebrow">{t('home.workspacesEyebrow')}</p><h2 id="workspace-title">{t('home.workspaces')}</h2></div><span>{t('home.activeWorkspaceCount', { count: available.length })}</span></div>{available.map(workspace => <Link className="workspace-card" to={workspace.rootPath} key={workspace.id}><span className="workspace-code">{workspace.code}</span><div><h3>{t(workspace.titleKey)}</h3><p>{t(workspace.descriptionKey)}</p></div><strong>{t('home.enterWorkspace')}</strong></Link>)}</section></main>
}

function WorkspaceHome({ workspaceId }: { workspaceId: 'mrp' | 'production' | 'inventory' | 'trading' | 'cashFlow' | 'collaboration' | 'administration' }) {
  const { t } = useTranslation()
  const { has } = useAuth()
  const workspace = workspaces.find(candidate => candidate.id === workspaceId)!
  return <main className="workspace-home"><header className="workspace-home-header"><p className="eyebrow">{t(workspace.eyebrowKey)}</p><h1>{t(workspace.workspaceTitleKey)}</h1><p>{t(workspace.workspaceDescriptionKey)}</p></header><div className="workspace-group-grid">{workspace.navigation.map(group => ({ ...group, items: group.items.filter(item => canNavigate(item.path, has)) })).filter(group => group.items.length > 0).map(group => <section className="workspace-group-card" key={group.labelKey}><h2>{t(group.labelKey)}</h2><div>{group.items.map(item => <Link key={item.path} to={item.path}>{t(item.labelKey)}</Link>)}</div></section>)}</div></main>
}

function CashFlowEntryRoute() {
  const { has } = useAuth()
  if (has('cash_flow.dashboard.view')) return <CashFlowPage view="dashboard" />
  if (has('cash_flow.obligation.view')) return <Navigate to="/cash-flow/obligations" replace />
  if (has('cash_flow.material_cost_book.view')) return <Navigate to="/cash-flow/material-cost-books" replace />
  if (has('cash_flow.purchase_price_history.view')) return <Navigate to="/cash-flow/purchase-price-history" replace />
  if (has('cash_flow.costing.view')) return <Navigate to="/cash-flow/costed-boms" replace />
  if (has('cash_flow.reporting_rate.view')) return <Navigate to="/cash-flow/reporting-rates" replace />
  return null
}

function AnalyticsEntryRoute() {
  const { has } = useAuth()
  const target = analyticsRoutes.find(route => canNavigate(route.path, has))?.path
  return target ? <Navigate to={target} replace /> : null
}

function LegacyForecastActualRedirect() {
  const location = useLocation()
  return <Navigate to={{ pathname: '/mrp/planning/production-forecast', search: location.search }} replace />
}

function LegacyInventoryRedirect() {
  const location = useLocation()
  const suffix = location.pathname.slice('/mrp/inventory'.length)
    .replace(/^\/transactions/, '/ledger')
    .replace(/^\/post\/OPENING/i, '/operations/opening')
    .replace(/^\/post\/RECEIPT/i, '/operations/receipt')
    .replace(/^\/post\/ISSUE/i, '/operations/issue')
    .replace(/^\/post\/ADJ_(IN|OUT)/i, '/operations/adjustment')
  return <Navigate to={{ pathname: `/inventory${suffix}`, search: location.search }} replace />
}

function ForecastCreatePage() {
  const { has } = useAuth()
  return <ForecastBatchEntryPage canManage={has('planning.forecast.manage')} />
}

function ForecastPlanDetailRoute() {
  const { has } = useAuth()
  return <ForecastPlanDetailPage canManage={has('planning.forecast.manage')} />
}

function RouteLoading() {
  const { t } = useTranslation()
  return <div className="route-loading" role="status" aria-live="polite"><span aria-hidden="true" />{t('common.loading')}</div>
}

function AuthenticatedApplication() {
  return <MeetingCallProvider><AppShell><Suspense fallback={<RouteLoading />}><Routes>
    <Route path="/" element={<ErpHome />} /><Route path="/mrp" element={<WorkspaceHome workspaceId="mrp" />} /><Route path="/production" element={<WorkspaceHome workspaceId="production" />} /><Route path="/inventory" element={<InventoryPage view="overview" />} /><Route path="/trading" element={<TradingOverviewPage />} /><Route path="/cash-flow" element={<CashFlowEntryRoute />} /><Route path="/collaboration" element={<WorkspaceHome workspaceId="collaboration" />} /><Route path="/administration" element={<WorkspaceHome workspaceId="administration" />} />
    <Route path="/collaboration/messages" element={<CollaborationMessagesPage />} /><Route path="/collaboration/messages/:conversationId" element={<CollaborationMessagesPage />} />
    <Route path="/collaboration/correspondence" element={<CollaborationLettersPage />} /><Route path="/collaboration/correspondence/:letterId" element={<CollaborationLettersPage />} />
    <Route path="/collaboration/meetings" element={<CollaborationMeetingsPage />} />
    <Route path="/collaboration/meetings/new" element={<CollaborationMeetingsPage />} />
    <Route path="/collaboration/meetings/:meetingId" element={<CollaborationMeetingsPage />} />
    <Route path="/collaboration/calls" element={<Navigate to="/collaboration/meetings" replace />} />
    <Route path="/trading/projects" element={<TradeProjectListPage />} /><Route path="/trading/projects/new" element={<TradeProjectCreatePage />} /><Route path="/trading/projects/:id" element={<TradeProjectObjectPage />} />
    <Route path="/trading/shipments" element={<TradingPerspectivePage perspective="shipments" />} /><Route path="/trading/customs" element={<TradingPerspectivePage perspective="customs" />} /><Route path="/trading/costs" element={<TradingPerspectivePage perspective="costs" />} /><Route path="/trading/reports" element={<TradingPerspectivePage perspective="reports" />} />
    <Route path="/trading/references" element={<TradingReferencePage />} />
    <Route path="/cash-flow/obligations" element={<CashFlowPage view="obligations" />} />
    <Route path="/cash-flow/costed-boms" element={<CashFlowPage view="costed-boms" />} />
    <Route path="/cash-flow/costed-boms/:parentItemId" element={<CashFlowPage view="costed-bom" />} />
    <Route path="/cash-flow/material-cost-books" element={<CashFlowMaterialCostBooksPage view="list" />} />
    <Route path="/cash-flow/material-cost-books/new" element={<CashFlowMaterialCostBooksPage view="create" />} />
    <Route path="/cash-flow/material-cost-books/revisions/:revisionId" element={<CashFlowMaterialCostBooksPage view="view" />} />
    <Route path="/cash-flow/material-cost-books/revisions/:revisionId/edit" element={<CashFlowMaterialCostBooksPage view="edit" />} />
    <Route path="/cash-flow/purchase-price-history" element={<CashFlowPurchasePriceHistoryPage />} />
    <Route path="/cash-flow/reporting-rates" element={<CashFlowReportingRatesPage view="list" />} />
    <Route path="/cash-flow/reporting-rates/new" element={<CashFlowReportingRatesPage view="create" />} />
    <Route path="/cash-flow/reporting-rates/:id" element={<CashFlowReportingRatesPage view="view" />} />
    <Route path="/cash-flow/reporting-rates/:id/edit" element={<CashFlowReportingRatesPage view="edit" />} />
    <Route path="/administration/identity" element={<Navigate to="/administration/users" replace />} />
    <Route path="/administration/users" element={<IdentityUsersPage />} /><Route path="/administration/users/new" element={<IdentityUserPage mode="create" />} /><Route path="/administration/users/:id" element={<IdentityUserPage mode="view" />} /><Route path="/administration/users/:id/edit" element={<IdentityUserPage mode="edit" />} />
    <Route path="/administration/roles" element={<IdentityRolesPage />} /><Route path="/administration/roles/new" element={<IdentityRolePage mode="create" />} /><Route path="/administration/roles/:id" element={<IdentityRolePage mode="view" />} /><Route path="/administration/roles/:id/edit" element={<IdentityRolePage mode="edit" />} />
    <Route path="/administration/units" element={<OrganizationalUnitsPage />} /><Route path="/administration/security-audit" element={<IdentitySecurityAuditPage />} />
    <Route path="/mrp/master-data/products" element={<ProductMaster />} /><Route path="/mrp/products/new" element={<ProductObjectPage />} /><Route path="/mrp/products/:id" element={<ProductObjectPage />} /><Route path="/mrp/products/:id/edit" element={<ProductObjectPage />} />
    <Route path="/mrp/master-data/parts" element={<MaterialItemMaster itemType="PART" />} /><Route path="/mrp/parts/new" element={<MaterialItemObjectPage itemType="PART" />} /><Route path="/mrp/parts/:id" element={<MaterialItemObjectPage itemType="PART" />} /><Route path="/mrp/parts/:id/edit" element={<MaterialItemObjectPage itemType="PART" />} />
    <Route path="/mrp/master-data/components" element={<MaterialItemMaster itemType="COMPONENT" />} /><Route path="/mrp/components/new" element={<MaterialItemObjectPage itemType="COMPONENT" />} /><Route path="/mrp/components/:id" element={<MaterialItemObjectPage itemType="COMPONENT" />} /><Route path="/mrp/components/:id/edit" element={<MaterialItemObjectPage itemType="COMPONENT" />} />
    <Route path="/mrp/master-data/reference-data" element={<ReferenceDataPage />} />
    <Route path="/mrp/supply/suppliers" element={<SupplierMaster />} /><Route path="/mrp/supply/internal-people" element={<InternalPeoplePage />} /><Route path="/mrp/suppliers/new" element={<SupplierObjectPage />} /><Route path="/mrp/suppliers/:id" element={<SupplierObjectPage />} /><Route path="/mrp/suppliers/:id/edit" element={<SupplierObjectPage />} />
    <Route path="/mrp/supply/sourcing" element={<SourcingMaster />} /><Route path="/mrp/supply/sourcing/new" element={<SourcingObjectPage />} /><Route path="/mrp/supply/sourcing/:id" element={<SourcingObjectPage />} /><Route path="/mrp/supply/sourcing/:id/edit" element={<SourcingObjectPage />} />
    <Route path="/mrp/sourcing/new" element={<SourcingObjectPage />} /><Route path="/mrp/sourcing/:id" element={<SourcingObjectPage />} /><Route path="/mrp/sourcing/:id/edit" element={<SourcingObjectPage />} />
    <Route path="/mrp/supply/purchase-orders" element={<PurchaseOrderPage view="list" />} /><Route path="/mrp/purchase-orders/new" element={<PurchaseOrderPage view="create" />} /><Route path="/mrp/purchase-orders/:id" element={<PurchaseOrderPage view="object" />} />
    <Route path="/mrp/engineering/bom" element={<BomMaster />} /><Route path="/mrp/planning/production-forecast" element={<ForecastActualPage view="forecast" />} /><Route path="/mrp/planning/production-forecast/new" element={<ForecastCreatePage />} /><Route path="/mrp/planning/production-forecast/plans/:id" element={<ForecastPlanDetailRoute />} /><Route path="/mrp/planning/forecast-actual" element={<LegacyForecastActualRedirect />} />
    <Route path="/production/daily-production" element={<ForecastActualPage view="actual" />} />
    <Route path="/production/routing" element={<ProductionRoutingPage />} />
    <Route path="/production/orders" element={<ProductionOrdersPage view="list" />} /><Route path="/production/orders/new" element={<ProductionOrdersPage view="create" />} /><Route path="/production/orders/:id" element={<ProductionOrdersPage view="object" />} /><Route path="/production/orders/:id/edit" element={<ProductionOrdersPage view="edit" />} />
    <Route path="/mrp/planning/mrp-runs" element={<MrpRunsPage view="list" />} /><Route path="/mrp/planning/mrp-runs/new" element={<MrpRunsPage view="create" />} /><Route path="/mrp/planning/mrp-runs/:id" element={<MrpRunsPage view="object" />} />
    <Route path="/mrp/planning/material-planning" element={<MaterialPlanningPage />} />
    <Route path="/mrp/supply/order-planning" element={<OrderPlanningPage view="list" />} />
    <Route path="/mrp/order-planning/:id" element={<OrderPlanningPage view="object" />} />
    <Route path="/inventory/items" element={<InventoryPage view="balances" />} /><Route path="/inventory/items/:id" element={<InventoryPage view="item" />} />
    <Route path="/inventory/warehouses" element={<InventoryPage view="warehouses" />} /><Route path="/inventory/warehouses/new" element={<InventoryPage view="warehouse" />} /><Route path="/inventory/warehouses/:id" element={<InventoryPage view="warehouse" />} /><Route path="/inventory/warehouses/:id/edit" element={<InventoryPage view="warehouse" />} />
    <Route path="/inventory/ledger" element={<InventoryPage view="transactions" />} /><Route path="/inventory/ledger/:id" element={<InventoryPage view="transaction" />} />
    <Route path="/inventory/operations/opening" element={<InventoryPage view="operation" operation="OPENING" />} /><Route path="/inventory/operations/receipt" element={<InventoryPage view="operation" operation="RECEIPT" />} /><Route path="/inventory/operations/issue" element={<InventoryPage view="operation" operation="ISSUE" />} /><Route path="/inventory/operations/adjustment" element={<InventoryPage view="operation" operation="ADJUSTMENT" />} />
    <Route path="/inventory/transfers" element={<InventoryPage view="transfers" />} /><Route path="/inventory/transfers/new" element={<InventoryPage view="transferCreate" />} /><Route path="/inventory/transfers/:id" element={<InventoryPage view="transfer" />} /><Route path="/inventory/transfers/:id/edit" element={<InventoryPage view="transferCreate" />} />
    <Route path="/mrp/inventory/*" element={<LegacyInventoryRedirect />} />
    <Route path="/mrp/analytics" element={<AnalyticsEntryRoute />} /><Route path="/mrp/analytics/lead-time" element={<AnalyticsPage view="lead-time" />} /><Route path="/mrp/analytics/forecast-accuracy" element={<AnalyticsPage view="forecast" />} /><Route path="/mrp/analytics/commercial" element={<Navigate to="/cash-flow/obligations" replace />} /><Route path="/mrp/analytics/inventory" element={<AnalyticsPage view="inventory" />} /><Route path="/mrp/analytics/reports" element={<AnalyticsPage view="reports" />} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></Suspense></AppShell></MeetingCallProvider>
}

function ApplicationRoutes() {
  const location = useLocation()
  if (location.pathname === '/meeting/guest') {
    return <Suspense fallback={<div className="route-loading" role="status" aria-live="polite" />}><PublicMeetingGuestPage /></Suspense>
  }
  return <AuthProvider><AuthenticatedApplication /></AuthProvider>
}

function App() {
  return <BrowserRouter><ApplicationRoutes /></BrowserRouter>
}

export default App
