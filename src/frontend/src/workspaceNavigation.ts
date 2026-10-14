export type NavigationIconKey =
  | 'analytics'
  | 'adminUsers'
  | 'adminRoles'
  | 'adminUnits'
  | 'securityAudit'
  | 'bom'
  | 'components'
  | 'dailyProduction'
  | 'productionOrders'
  | 'productionRouting'
  | 'inventory'
  | 'inventoryAdjustment'
  | 'inventoryIssue'
  | 'inventoryLedger'
  | 'inventoryOpening'
  | 'inventoryReceipt'
  | 'inventoryTransfer'
  | 'warehouses'
  | 'materialPlanning'
  | 'mrpRuns'
  | 'orderPlanning'
  | 'parts'
  | 'products'
  | 'purchaseOrders'
  | 'referenceData'
  | 'sourcing'
  | 'suppliers'
  | 'supplierBalances'
  | 'productionForecast'
  | 'tradeProjects'
  | 'tradeShipments'
  | 'tradeCustoms'
  | 'tradeCosts'
  | 'tradeReferences'
  | 'tradeReports'
  | 'cashFlowObligations'
  | 'cashFlowCosting'
  | 'cashFlowMaterialBooks'
  | 'cashFlowPurchaseHistory'
  | 'cashFlowReportingRates'
  | 'collaborationMessages'
  | 'collaborationLetters'
  | 'collaborationCalls'

export type NavigationItemDefinition = {
  labelKey: string
  path: string
  icon: NavigationIconKey
  aliases?: string[]
}

export type NavigationGroupDefinition = {
  labelKey: string
  items: NavigationItemDefinition[]
}

export type WorkspaceDefinition = {
  id: 'mrp' | 'production' | 'inventory' | 'trading' | 'cashFlow' | 'collaboration' | 'administration'
  rootPath: string
  code: string
  titleKey: string
  descriptionKey: string
  eyebrowKey: string
  workspaceTitleKey: string
  workspaceDescriptionKey: string
  shellContextKey: string
  sidebarAriaKey: string
  expandKey: string
  collapseKey: string
  entryPermission?: string
  navigation: NavigationGroupDefinition[]
}

export const analyticsRoutes = [
  {
    view: 'lead-time', labelKey: 'analytics.leadTime', path: '/mrp/analytics/lead-time', icon: 'analytics',
    viewPermission: 'analytics.lead_time.view', managePermission: 'procurement.sourcing_lead_time.apply',
  },
  {
    view: 'forecast', labelKey: 'analytics.forecast', path: '/mrp/analytics/forecast-accuracy', icon: 'analytics',
    viewPermission: 'analytics.forecast_accuracy.view', managePermission: 'analytics.export',
  },
  {
    view: 'inventory', labelKey: 'analytics.inventory', path: '/mrp/analytics/inventory', icon: 'analytics',
    viewPermission: 'analytics.view', managePermission: 'analytics.export',
  },
  {
    view: 'reports', labelKey: 'analytics.reports', path: '/mrp/analytics/reports', icon: 'analytics',
    viewPermission: 'analytics.export', managePermission: 'analytics.export',
  },
] as const satisfies ReadonlyArray<{
  view: 'lead-time' | 'forecast' | 'inventory' | 'reports'
  labelKey: string
  path: string
  icon: NavigationIconKey
  viewPermission: string
  managePermission: string
}>

export const mrpNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.masterData', items: [
    { labelKey: 'navigation.items.products', path: '/mrp/master-data/products', aliases: ['/mrp/products'], icon: 'products' },
    { labelKey: 'navigation.items.parts', path: '/mrp/master-data/parts', aliases: ['/mrp/parts'], icon: 'parts' },
    { labelKey: 'navigation.items.components', path: '/mrp/master-data/components', aliases: ['/mrp/components'], icon: 'components' },
    { labelKey: 'navigation.items.referenceData', path: '/mrp/master-data/reference-data', icon: 'referenceData' },
  ] },
  { labelKey: 'navigation.groups.suppliersAndSourcing', items: [
    { labelKey: 'navigation.items.suppliers', path: '/mrp/supply/suppliers', aliases: ['/mrp/suppliers', '/mrp/supply/internal-people'], icon: 'suppliers' },
    { labelKey: 'navigation.items.sourcing', path: '/mrp/supply/sourcing', aliases: ['/mrp/sourcing'], icon: 'sourcing' },
    { labelKey: 'navigation.items.purchaseOrders', path: '/mrp/supply/purchase-orders', aliases: ['/mrp/purchase-orders'], icon: 'purchaseOrders' },
    { labelKey: 'navigation.items.orderPlanning', path: '/mrp/supply/order-planning', aliases: ['/mrp/order-planning'], icon: 'orderPlanning' },
    { labelKey: 'navigation.items.supplierBalances', path: '/mrp/supply/supplier-balances', icon: 'supplierBalances' },
  ] },
  { labelKey: 'navigation.groups.engineering', items: [
    { labelKey: 'navigation.items.bom', path: '/mrp/engineering/bom', icon: 'bom' },
  ] },
  { labelKey: 'navigation.groups.planning', items: [
    { labelKey: 'navigation.items.productionForecast', path: '/mrp/planning/production-forecast', aliases: ['/mrp/planning/production-forecast'], icon: 'productionForecast' },
    { labelKey: 'navigation.items.mrpRuns', path: '/mrp/planning/mrp-runs', icon: 'mrpRuns' },
    { labelKey: 'navigation.items.materialPlanning', path: '/mrp/planning/material-planning', icon: 'materialPlanning' },
  ] },
  { labelKey: 'navigation.groups.analytics', items: [
    ...analyticsRoutes.map(({ labelKey, path, icon }) => ({ labelKey, path, icon })),
  ] },
]

export const productionNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.productionSetup', items: [
    { labelKey: 'navigation.items.productionRouting', path: '/production/routing', aliases: ['/production/routing'], icon: 'productionRouting' },
  ] },
  { labelKey: 'navigation.groups.productionOperations', items: [
    { labelKey: 'navigation.items.productionOrders', path: '/production/orders', aliases: ['/production/orders'], icon: 'productionOrders' },
    { labelKey: 'navigation.items.dailyProduction', path: '/production/daily-production', icon: 'dailyProduction' },
  ] },
]

export const inventoryNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.inventoryControl', items: [
    { labelKey: 'navigation.items.inventoryPositions', path: '/inventory/items', aliases: ['/inventory/items'], icon: 'inventory' },
    { labelKey: 'navigation.items.inventoryLedger', path: '/inventory/ledger', aliases: ['/inventory/ledger'], icon: 'inventoryLedger' },
  ] },
  { labelKey: 'navigation.groups.inventoryOperations', items: [
    { labelKey: 'navigation.items.inventoryReceipt', path: '/inventory/operations/receipt', icon: 'inventoryReceipt' },
    { labelKey: 'navigation.items.inventoryIssue', path: '/inventory/operations/issue', icon: 'inventoryIssue' },
    { labelKey: 'navigation.items.inventoryTransfer', path: '/inventory/transfers', aliases: ['/inventory/transfers'], icon: 'inventoryTransfer' },
    { labelKey: 'navigation.items.inventoryAdjustment', path: '/inventory/operations/adjustment', icon: 'inventoryAdjustment' },
  ] },
  { labelKey: 'navigation.groups.inventorySetup', items: [
    { labelKey: 'navigation.items.warehouses', path: '/inventory/warehouses', aliases: ['/inventory/warehouses'], icon: 'warehouses' },
    { labelKey: 'navigation.items.inventoryOpening', path: '/inventory/operations/opening', icon: 'inventoryOpening' },
  ] },
]

export const administrationNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.identityAccess', items: [
    { labelKey: 'navigation.items.adminUsers', path: '/administration/users', aliases: ['/administration/users'], icon: 'adminUsers' },
    { labelKey: 'navigation.items.adminRoles', path: '/administration/roles', aliases: ['/administration/roles'], icon: 'adminRoles' },
  ] },
  { labelKey: 'navigation.groups.organization', items: [
    { labelKey: 'navigation.items.adminUnits', path: '/administration/units', icon: 'adminUnits' },
  ] },
  { labelKey: 'navigation.groups.security', items: [
    { labelKey: 'navigation.items.securityAudit', path: '/administration/security-audit', icon: 'securityAudit' },
  ] },
]

export const tradingNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.tradeOperations', items: [
    { labelKey: 'navigation.items.tradeProjects', path: '/trading/projects', aliases: ['/trading/projects'], icon: 'tradeProjects' },
    { labelKey: 'navigation.items.tradeShipments', path: '/trading/shipments', icon: 'tradeShipments' },
    { labelKey: 'navigation.items.tradeCustoms', path: '/trading/customs', icon: 'tradeCustoms' },
  ] },
  { labelKey: 'navigation.groups.tradeControl', items: [
    { labelKey: 'navigation.items.tradeCosts', path: '/trading/costs', icon: 'tradeCosts' },
    { labelKey: 'navigation.items.tradeReports', path: '/trading/reports', icon: 'tradeReports' },
    { labelKey: 'navigation.items.tradeReferences', path: '/trading/references', icon: 'tradeReferences' },
  ] },
]

export const cashFlowNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.cashPlanning', items: [
    { labelKey: 'navigation.items.cashObligations', path: '/cash-flow/obligations', icon: 'cashFlowObligations' },
  ] },
  { labelKey: 'navigation.groups.cashCosting', items: [
    { labelKey: 'navigation.items.materialCostBooks', path: '/cash-flow/material-cost-books', aliases: ['/cash-flow/material-cost-books'], icon: 'cashFlowMaterialBooks' },
    { labelKey: 'navigation.items.purchasePriceHistory', path: '/cash-flow/purchase-price-history', aliases: ['/cash-flow/purchase-price-history'], icon: 'cashFlowPurchaseHistory' },
    { labelKey: 'navigation.items.costedBoms', path: '/cash-flow/costed-boms', aliases: ['/cash-flow/costed-boms'], icon: 'cashFlowCosting' },
  ] },
  { labelKey: 'navigation.groups.cashSetup', items: [
    { labelKey: 'navigation.items.cashReportingRates', path: '/cash-flow/reporting-rates', aliases: ['/cash-flow/reporting-rates'], icon: 'cashFlowReportingRates' },
  ] },
]

export const collaborationNavigationGroups: NavigationGroupDefinition[] = [
  { labelKey: 'navigation.groups.teamCommunication', items: [
    { labelKey: 'navigation.items.collaborationMessages', path: '/collaboration/messages', aliases: ['/collaboration/messages'], icon: 'collaborationMessages' },
    { labelKey: 'navigation.items.collaborationCalls', path: '/collaboration/meetings', aliases: ['/collaboration/calls'], icon: 'collaborationCalls' },
  ] },
  { labelKey: 'navigation.groups.formalCorrespondence', items: [
    { labelKey: 'navigation.items.collaborationLetters', path: '/collaboration/correspondence', aliases: ['/collaboration/correspondence'], icon: 'collaborationLetters' },
  ] },
]

export const workspaces: WorkspaceDefinition[] = [
  {
    id: 'mrp', rootPath: '/mrp', code: 'MRP', titleKey: 'home.mrpTitle',
    descriptionKey: 'home.mrpDescription', eyebrowKey: 'mrpWorkspace.eyebrow',
    workspaceTitleKey: 'mrpWorkspace.title', workspaceDescriptionKey: 'mrpWorkspace.description',
    shellContextKey: 'shell.mrpContext', sidebarAriaKey: 'navigation.mrpSidebarAria',
    expandKey: 'navigation.expandMrp', collapseKey: 'navigation.collapseMrp',
    navigation: mrpNavigationGroups,
  },
  {
    id: 'production', rootPath: '/production', code: 'PROD', titleKey: 'home.productionTitle',
    descriptionKey: 'home.productionDescription', eyebrowKey: 'productionWorkspace.eyebrow',
    workspaceTitleKey: 'productionWorkspace.title', workspaceDescriptionKey: 'productionWorkspace.description',
    shellContextKey: 'shell.productionContext', sidebarAriaKey: 'navigation.productionSidebarAria',
    expandKey: 'navigation.expandProduction', collapseKey: 'navigation.collapseProduction',
    navigation: productionNavigationGroups,
  },
  {
    id: 'inventory', rootPath: '/inventory', code: 'INV', titleKey: 'home.inventoryTitle',
    descriptionKey: 'home.inventoryDescription', eyebrowKey: 'inventoryWorkspace.eyebrow',
    workspaceTitleKey: 'inventoryWorkspace.title', workspaceDescriptionKey: 'inventoryWorkspace.description',
    shellContextKey: 'shell.inventoryContext', sidebarAriaKey: 'navigation.inventorySidebarAria',
    expandKey: 'navigation.expandInventory', collapseKey: 'navigation.collapseInventory',
    navigation: inventoryNavigationGroups,
  },
  {
    id: 'trading', rootPath: '/trading', code: 'TRD', titleKey: 'home.tradingTitle',
    descriptionKey: 'home.tradingDescription', eyebrowKey: 'tradingWorkspace.eyebrow',
    workspaceTitleKey: 'tradingWorkspace.title', workspaceDescriptionKey: 'tradingWorkspace.description',
    shellContextKey: 'shell.tradingContext', sidebarAriaKey: 'navigation.tradingSidebarAria',
    expandKey: 'navigation.expandTrading', collapseKey: 'navigation.collapseTrading',
    navigation: tradingNavigationGroups,
  },
  {
    id: 'cashFlow', rootPath: '/cash-flow', code: 'CF', titleKey: 'home.cashFlowTitle',
    descriptionKey: 'home.cashFlowDescription', eyebrowKey: 'cashFlowWorkspace.eyebrow',
    workspaceTitleKey: 'cashFlowWorkspace.title', workspaceDescriptionKey: 'cashFlowWorkspace.description',
    shellContextKey: 'shell.cashFlowContext', sidebarAriaKey: 'navigation.cashFlowSidebarAria',
    expandKey: 'navigation.expandCashFlow', collapseKey: 'navigation.collapseCashFlow',
    entryPermission: 'cash_flow.dashboard.view', navigation: cashFlowNavigationGroups,
  },
  {
    id: 'collaboration', rootPath: '/collaboration', code: 'COM', titleKey: 'home.collaborationTitle',
    descriptionKey: 'home.collaborationDescription', eyebrowKey: 'collaborationWorkspace.eyebrow',
    workspaceTitleKey: 'collaborationWorkspace.title', workspaceDescriptionKey: 'collaborationWorkspace.description',
    shellContextKey: 'shell.collaborationContext', sidebarAriaKey: 'navigation.collaborationSidebarAria',
    expandKey: 'navigation.expandCollaboration', collapseKey: 'navigation.collapseCollaboration',
    entryPermission: 'collaboration.workspace.view', navigation: collaborationNavigationGroups,
  },
  {
    id: 'administration', rootPath: '/administration', code: 'ADMIN', titleKey: 'home.administrationTitle',
    descriptionKey: 'home.administrationDescription', eyebrowKey: 'administrationWorkspace.eyebrow',
    workspaceTitleKey: 'administrationWorkspace.title', workspaceDescriptionKey: 'administrationWorkspace.description',
    shellContextKey: 'shell.administrationContext', sidebarAriaKey: 'navigation.administrationSidebarAria',
    expandKey: 'navigation.expandAdministration', collapseKey: 'navigation.collapseAdministration',
    navigation: administrationNavigationGroups,
  },
]

export type RoutePermissionRule = { prefix: string; view: string; manage: string }

export const routePermissionRules: RoutePermissionRule[] = [
  { prefix: '/collaboration/correspondence', view: 'correspondence.letter.view', manage: 'correspondence.letter.manage' },
  { prefix: '/collaboration/meetings', view: 'collaboration.meeting.view', manage: 'collaboration.meeting.schedule' },
  { prefix: '/collaboration/calls', view: 'collaboration.meeting.view', manage: 'collaboration.meeting.schedule' },
  { prefix: '/collaboration/messages', view: 'collaboration.workspace.view', manage: 'collaboration.message.send' },
  { prefix: '/collaboration', view: 'collaboration.workspace.view', manage: 'collaboration.message.send' },
  { prefix: '/cash-flow/material-cost-books', view: 'cash_flow.material_cost_book.view', manage: 'cash_flow.material_cost_book.manage' },
  { prefix: '/cash-flow/purchase-price-history', view: 'cash_flow.purchase_price_history.view', manage: 'cash_flow.purchase_price_history.view' },
  { prefix: '/cash-flow/reporting-rates', view: 'cash_flow.reporting_rate.view', manage: 'cash_flow.reporting_rate.manage' },
  { prefix: '/cash-flow/obligations', view: 'cash_flow.obligation.view', manage: 'cash_flow.obligation.view' },
  { prefix: '/cash-flow/costed-boms', view: 'cash_flow.costing.view', manage: 'cash_flow.costing.view' },
  { prefix: '/cash-flow', view: 'cash_flow.dashboard.view', manage: 'cash_flow.dashboard.view' },
  { prefix: '/mrp/analytics/commercial', view: 'cash_flow.obligation.view', manage: 'cash_flow.obligation.view' },
  { prefix: '/trading/references', view: 'trading.reference.manage', manage: 'trading.reference.manage' },
  { prefix: '/trading/shipments', view: 'trading.shipment.view', manage: 'trading.shipment.manage' },
  { prefix: '/trading/customs', view: 'trading.customs.view', manage: 'trading.customs.manage' },
  { prefix: '/trading/costs', view: 'trading.cost.view', manage: 'trading.cost.manage' },
  { prefix: '/trading/reports', view: 'trading.report.view', manage: 'trading.report.view' },
  { prefix: '/trading', view: 'trading.project.view', manage: 'trading.project.manage' },
  { prefix: '/administration/security-audit', view: 'identity.security_audit.view', manage: 'identity.security_audit.view' },
  { prefix: '/administration/users', view: 'identity.user.view', manage: 'identity.user.manage' },
  { prefix: '/administration/roles', view: 'identity.role.view', manage: 'identity.role.manage' },
  { prefix: '/administration/units', view: 'organization.unit.view', manage: 'organization.unit.manage' },
  { prefix: '/production/routing', view: 'production.routing.view', manage: 'production.routing.manage' },
  { prefix: '/production/orders', view: 'production.order.view', manage: 'production.order.manage' },
  { prefix: '/production/daily-production', view: 'production.actual.view', manage: 'production.actual.manage' },
  { prefix: '/production', view: 'production.order.view', manage: 'production.order.manage' },
  { prefix: '/mrp/master-data/products', view: 'catalog.product.view', manage: 'catalog.product.manage' },
  { prefix: '/mrp/products', view: 'catalog.product.view', manage: 'catalog.product.manage' },
  { prefix: '/mrp/master-data/parts', view: 'catalog.part.view', manage: 'catalog.part.manage' },
  { prefix: '/mrp/parts', view: 'catalog.part.view', manage: 'catalog.part.manage' },
  { prefix: '/mrp/master-data/components', view: 'catalog.component.view', manage: 'catalog.component.manage' },
  { prefix: '/mrp/components', view: 'catalog.component.view', manage: 'catalog.component.manage' },
  { prefix: '/mrp/master-data/reference-data', view: 'reference_data.view', manage: 'reference_data.manage' },
  { prefix: '/mrp/supply/supplier-balances', view: 'procurement.supplier.view', manage: 'procurement.supplier.view' },
  { prefix: '/mrp/supply/suppliers', view: 'procurement.supplier.view', manage: 'procurement.supplier.manage' },
  { prefix: '/mrp/supply/internal-people', view: 'organization.internal_person.view', manage: 'organization.internal_person.manage' },
  { prefix: '/mrp/suppliers', view: 'procurement.supplier.view', manage: 'procurement.supplier.manage' },
  { prefix: '/mrp/supply/sourcing', view: 'procurement.sourcing.view', manage: 'procurement.sourcing.manage' },
  { prefix: '/mrp/sourcing', view: 'procurement.sourcing.view', manage: 'procurement.sourcing.manage' },
  { prefix: '/mrp/supply/purchase-orders', view: 'procurement.purchase_order.view', manage: 'procurement.purchase_order_draft.manage' },
  { prefix: '/mrp/purchase-orders', view: 'procurement.purchase_order.view', manage: 'procurement.purchase_order_draft.manage' },
  { prefix: '/mrp/supply/order-planning', view: 'procurement.order_planning.view', manage: 'procurement.order_planning.generate' },
  { prefix: '/mrp/order-planning', view: 'procurement.order_planning.view', manage: 'procurement.order_planning.decide' },
  { prefix: '/mrp/engineering/bom', view: 'engineering.bom.view', manage: 'engineering.bom_draft.manage' },
  { prefix: '/mrp/planning/production-forecast', view: 'planning.forecast.view', manage: 'planning.forecast.manage' },
  { prefix: '/mrp/planning/mrp-runs', view: 'planning.mrp_run.view', manage: 'planning.mrp_run.execute' },
  { prefix: '/mrp/planning/material-planning', view: 'planning.material.view', manage: 'planning.material.view' },
  { prefix: '/inventory/warehouses', view: 'inventory.stock.view', manage: 'inventory.warehouse.manage' },
  { prefix: '/inventory/operations/opening', view: 'inventory.opening.post', manage: 'inventory.opening.post' },
  { prefix: '/inventory/operations/receipt', view: 'inventory.receipt.post', manage: 'inventory.receipt.post' },
  { prefix: '/inventory/operations/issue', view: 'inventory.issue.post', manage: 'inventory.issue.post' },
  { prefix: '/inventory/operations/adjustment', view: 'inventory.adjustment.post', manage: 'inventory.adjustment.post' },
  { prefix: '/inventory/transfers', view: 'inventory.stock.view', manage: 'inventory.transfer.post' },
  { prefix: '/inventory/ledger', view: 'inventory.stock.view', manage: 'inventory.transaction.reverse' },
  { prefix: '/inventory/items', view: 'inventory.stock.view', manage: 'inventory.stock.view' },
  { prefix: '/inventory', view: 'inventory.stock.view', manage: 'inventory.stock.view' },
  ...analyticsRoutes.map(route => ({
    prefix: route.path, view: route.viewPermission, manage: route.managePermission,
  })),
]

export function routePermission(pathname: string) {
  return [...routePermissionRules].sort((left, right) => right.prefix.length - left.prefix.length)
    .find(rule => pathname === rule.prefix || pathname.startsWith(`${rule.prefix}/`)) ?? null
}

export function canNavigate(pathname: string, has: (permission: string) => boolean) {
  const permission = routePermission(pathname)
  return permission !== null && has(permission.view)
}

export function canViewWorkspaceOverview(workspace: WorkspaceDefinition, has: (permission: string) => boolean) {
  return !workspace.entryPermission || has(workspace.entryPermission)
}

export function canManageRoute(pathname: string, has: (permission: string) => boolean) {
  const permission = routePermission(pathname)
  return permission !== null && has(permission.manage)
}

export type IdentityAdminTab = 'users' | 'roles' | 'units'

const identityAdminTabPermissions: ReadonlyArray<readonly [IdentityAdminTab, string]> = [
  ['users', 'identity.user.view'],
  ['roles', 'identity.role.view'],
  ['units', 'organization.unit.view'],
]

export function resolveIdentityAdminTab(requested: IdentityAdminTab,
  has: (permission: string) => boolean): IdentityAdminTab | null {
  const available = identityAdminTabPermissions.filter(([, permission]) => has(permission)).map(([tab]) => tab)
  return available.includes(requested) ? requested : available[0] ?? null
}

export function findWorkspace(pathname: string) {
  return workspaces.find(workspace => pathname === workspace.rootPath || pathname.startsWith(`${workspace.rootPath}/`)) ?? null
}

export function findWorkspaceRoute(pathname: string) {
  const workspace = findWorkspace(pathname)
  if (!workspace) return null
  for (const group of workspace.navigation) {
    const item = group.items.find(candidate => candidate.path === pathname ||
      candidate.aliases?.some(alias => pathname === alias || pathname.startsWith(`${alias}/`)))
    if (item) return { workspace, group, item }
  }
  return null
}
