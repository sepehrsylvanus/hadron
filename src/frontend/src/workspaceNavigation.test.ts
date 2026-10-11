import assert from 'node:assert/strict'
import test from 'node:test'
import {
  analyticsRoutes, canManageRoute, canNavigate, canViewWorkspaceOverview, cashFlowNavigationGroups, findWorkspace, findWorkspaceRoute, mrpNavigationGroups,
  productionNavigationGroups, resolveIdentityAdminTab, routePermission, workspaces,
} from './workspaceNavigation.ts'
import en from './i18n/locales/en.ts'
import fa from './i18n/locales/fa.ts'

test('BOM sidebar label identifies the product BOM in both languages', () => {
  assert.equal(en.navigation.items.bom, 'BOM of Product')
  assert.equal(fa.navigation.items.bom, 'ساختار محصول (BOM)')
})

test('ERP launchpad exposes the approved business and administration workspaces', () => {
  assert.deepEqual(workspaces.map(workspace => [workspace.id, workspace.rootPath]), [
    ['mrp', '/mrp'],
    ['production', '/production'],
    ['inventory', '/inventory'],
    ['trading', '/trading'],
    ['cashFlow', '/cash-flow'],
    ['collaboration', '/collaboration'],
    ['administration', '/administration'],
  ])
})

test('Forecast stays in MRP while production execution belongs only to Production', () => {
  const mrpItems = mrpNavigationGroups.flatMap(group => group.items)
  const productionItems = productionNavigationGroups.flatMap(group => group.items)

  assert.ok(mrpItems.some(item => item.path === '/mrp/planning/production-forecast'))
  assert.ok(!mrpItems.some(item => item.path.includes('daily-production')))
  assert.deepEqual(productionItems.map(item => item.path), [
    '/production/routing',
    '/production/orders',
    '/production/daily-production',
  ])
  assert.ok(!productionItems.some(item => item.path.includes('forecast')))
  assert.ok(![...mrpItems, ...productionItems].some(item => item.path === '/mrp/planning/forecast-actual'))
})

test('workspace and active-route resolution preserve separate navigation contexts', () => {
  assert.equal(findWorkspace('/mrp/planning/production-forecast')?.id, 'mrp')
  assert.equal(findWorkspaceRoute('/mrp/planning/production-forecast')?.item.labelKey,
    'navigation.items.productionForecast')
  assert.equal(findWorkspace('/production/daily-production')?.id, 'production')
  assert.equal(findWorkspaceRoute('/production/daily-production')?.item.labelKey,
    'navigation.items.dailyProduction')
  assert.equal(findWorkspace('/production/orders')?.id, 'production')
  assert.equal(findWorkspaceRoute('/production/orders')?.item.labelKey,
    'navigation.items.productionOrders')
  assert.equal(findWorkspace('/'), null)
})

test('all user-facing navigation paths are unique', () => {
  const paths = workspaces.flatMap(workspace => workspace.navigation.flatMap(group =>
    group.items.map(item => item.path)))
  assert.equal(new Set(paths).size, paths.length)
})

test('Trading is isolated as its own workspace and each operational perspective has a specific permission', () => {
  assert.equal(findWorkspace('/trading/projects')?.id, 'trading')
  assert.equal(routePermission('/trading/projects')?.view, 'trading.project.view')
  assert.equal(routePermission('/trading/shipments')?.view, 'trading.shipment.view')
  assert.equal(routePermission('/trading/customs')?.view, 'trading.customs.view')
  assert.equal(routePermission('/trading/costs')?.view, 'trading.cost.view')
  assert.equal(routePermission('/trading/reports')?.view, 'trading.report.view')
  assert.equal(routePermission('/trading/references')?.view, 'trading.reference.manage')
  assert.equal(canNavigate('/trading/shipments', permission => permission === 'trading.project.view'), false)
})

test('Cash Flow is a private workspace and legacy financial analytics cannot leak through analytics.view', () => {
  assert.equal(findWorkspace('/cash-flow/obligations')?.id, 'cashFlow')
  assert.deepEqual(cashFlowNavigationGroups.flatMap(group => group.items).map(item => item.path), [
    '/cash-flow/obligations', '/cash-flow/material-cost-books', '/cash-flow/purchase-price-history', '/cash-flow/costed-boms', '/cash-flow/reporting-rates',
  ])
  assert.equal(routePermission('/cash-flow')?.view, 'cash_flow.dashboard.view')
  assert.equal(routePermission('/cash-flow/obligations')?.view, 'cash_flow.obligation.view')
  assert.equal(routePermission('/cash-flow/costed-boms/product-1')?.view, 'cash_flow.costing.view')
  assert.equal(routePermission('/cash-flow/material-cost-books/revisions/revision-1/edit')?.view, 'cash_flow.material_cost_book.view')
  assert.equal(routePermission('/cash-flow/material-cost-books/revisions/revision-1/edit')?.manage, 'cash_flow.material_cost_book.manage')
  assert.equal(routePermission('/cash-flow/purchase-price-history')?.view, 'cash_flow.purchase_price_history.view')
  assert.equal(routePermission('/cash-flow/purchase-price-history')?.manage, 'cash_flow.purchase_price_history.view')
  assert.deepEqual(routePermission('/cash-flow/reporting-rates/rate-set-1/edit'), {
    prefix: '/cash-flow/reporting-rates',
    view: 'cash_flow.reporting_rate.view',
    manage: 'cash_flow.reporting_rate.manage',
  })
  assert.equal(routePermission('/mrp/analytics/commercial')?.view, 'cash_flow.obligation.view')
  assert.equal(canNavigate('/cash-flow/obligations', permission => permission === 'analytics.view'), false)
  assert.equal(canNavigate('/cash-flow/reporting-rates', permission => permission === 'cash_flow.reporting_rate.view'), true)
  assert.equal(canNavigate('/cash-flow/material-cost-books', permission => permission === 'cash_flow.material_cost_book.view'), true)
  assert.equal(canNavigate('/cash-flow/purchase-price-history', permission => permission === 'cash_flow.purchase_price_history.view'), true)
  assert.equal(canManageRoute('/cash-flow/reporting-rates/rate-set-1/edit', permission => permission === 'cash_flow.reporting_rate.view'), false)
  assert.equal(canManageRoute('/cash-flow/reporting-rates/rate-set-1/edit', permission => permission === 'cash_flow.reporting_rate.manage'), true)
})

test('Cash Flow overview is hidden from users who only hold a sub-area permission', () => {
  const cashFlow = workspaces.find(workspace => workspace.id === 'cashFlow')!
  assert.equal(canViewWorkspaceOverview(cashFlow, permission => permission === 'cash_flow.dashboard.view'), true)
  assert.equal(canViewWorkspaceOverview(cashFlow, permission => permission === 'cash_flow.obligation.view'), false)
  assert.equal(canViewWorkspaceOverview(cashFlow, permission => permission === 'cash_flow.costing.view'), false)
  assert.equal(canViewWorkspaceOverview(cashFlow, permission => permission === 'cash_flow.reporting_rate.view'), false)
})

test('Analytics navigation and route guards mirror each specialized backend read contract', () => {
  assert.deepEqual(analyticsRoutes.map(route => [route.path, route.viewPermission]), [
    ['/mrp/analytics/lead-time', 'analytics.lead_time.view'],
    ['/mrp/analytics/forecast-accuracy', 'analytics.forecast_accuracy.view'],
    ['/mrp/analytics/inventory', 'analytics.view'],
    ['/mrp/analytics/reports', 'analytics.export'],
  ])
  assert.deepEqual(mrpNavigationGroups.find(group => group.labelKey === 'navigation.groups.analytics')?.items
    .map(item => item.path), analyticsRoutes.map(route => route.path))
  for (const route of analyticsRoutes) {
    assert.equal(routePermission(route.path)?.view, route.viewPermission)
    assert.equal(canNavigate(route.path, permission => permission === route.viewPermission), true)
    for (const other of analyticsRoutes.filter(candidate => candidate.viewPermission !== route.viewPermission)) {
      assert.equal(canNavigate(route.path, permission => permission === other.viewPermission), false)
    }
  }
  assert.equal(routePermission('/mrp/analytics'), null)
})

test('every user-facing module route has explicit view and manage permission metadata', () => {
  for (const workspace of workspaces) {
    for (const item of workspace.navigation.flatMap(group => group.items)) {
      const permission = routePermission(item.path)
      assert.ok(permission, `Missing permission for ${item.path}`)
      assert.match(permission.view, /^[a-z0-9_.]+$/)
      assert.match(permission.manage, /^[a-z0-9_.]+$/)
    }
  }
})

test('view permission exposes a module but does not authorize its direct edit route', () => {
  const granted = new Set(['catalog.product.view'])
  const has = (permission: string) => granted.has(permission)
  assert.equal(canNavigate('/mrp/master-data/products', has), true)
  assert.equal(canNavigate('/mrp/products/11111111-1111-1111-1111-111111111111/edit', has), true)
  assert.equal(canManageRoute('/mrp/products/11111111-1111-1111-1111-111111111111/edit', has), false)
  granted.add('catalog.product.manage')
  assert.equal(canManageRoute('/mrp/products/11111111-1111-1111-1111-111111111111/edit', has), true)
})

test('Identity administration defaults to the first tab the current user may actually view', () => {
  assert.equal(resolveIdentityAdminTab('users', permission => permission === 'identity.role.view'), 'roles')
  assert.equal(resolveIdentityAdminTab('users', permission => permission === 'organization.unit.view'), 'units')
  assert.equal(resolveIdentityAdminTab('roles', permission => permission === 'identity.role.view'), 'roles')
  assert.equal(resolveIdentityAdminTab('users', () => false), null)
})

test('ten representative access profiles expose only their permitted menus and keep view separate from manage', () => {
  const profiles: Array<{ name: string; permissions: string[]; visible: string[]; hidden: string[]; viewOnly?: string }> = [
    { name: 'reference-viewer', permissions: ['reference_data.view'], visible: ['/mrp/master-data/reference-data'], hidden: ['/mrp/master-data/products'], viewOnly: '/mrp/master-data/reference-data' },
    { name: 'master-editor', permissions: ['reference_data.view', 'reference_data.manage', 'catalog.product.view', 'catalog.product.manage', 'catalog.part.view', 'catalog.part.manage', 'catalog.component.view', 'catalog.component.manage'], visible: ['/mrp/master-data/products', '/mrp/master-data/parts', '/mrp/master-data/components'], hidden: ['/mrp/engineering/bom'] },
    { name: 'engineering-viewer', permissions: ['engineering.bom.view'], visible: ['/mrp/engineering/bom'], hidden: ['/mrp/planning/mrp-runs'], viewOnly: '/mrp/engineering/bom' },
    { name: 'engineering-lead', permissions: ['engineering.bom.view', 'engineering.bom_draft.manage', 'engineering.bom.approve'], visible: ['/mrp/engineering/bom'], hidden: ['/inventory'] },
    { name: 'production-operator', permissions: ['production.actual.view', 'production.actual.manage'], visible: ['/production/daily-production'], hidden: ['/mrp/planning/production-forecast'] },
    { name: 'planning-viewer', permissions: ['planning.forecast.view', 'planning.mrp_run.view', 'planning.material.view'], visible: ['/mrp/planning/production-forecast', '/mrp/planning/mrp-runs', '/mrp/planning/material-planning'], hidden: ['/mrp/supply/order-planning'], viewOnly: '/mrp/planning/production-forecast' },
    { name: 'mrp-planner', permissions: ['planning.forecast.view', 'planning.forecast.manage', 'planning.mrp_run.view', 'planning.mrp_run.execute', 'planning.material.view'], visible: ['/mrp/planning/production-forecast', '/mrp/planning/mrp-runs'], hidden: ['/mrp/supply/purchase-orders'] },
    { name: 'procurement-viewer', permissions: ['procurement.supplier.view', 'procurement.sourcing.view', 'procurement.order_planning.view', 'procurement.purchase_order.view'], visible: ['/mrp/supply/suppliers', '/mrp/supply/sourcing', '/mrp/supply/order-planning', '/mrp/supply/purchase-orders'], hidden: ['/inventory'], viewOnly: '/mrp/supply/purchase-orders' },
    { name: 'procurement-buyer', permissions: ['procurement.supplier.view', 'procurement.sourcing.view', 'procurement.sourcing.manage', 'procurement.order_planning.view', 'procurement.order_planning.decide', 'procurement.purchase_order.view', 'procurement.purchase_order_draft.manage'], visible: ['/mrp/supply/sourcing', '/mrp/supply/order-planning', '/mrp/supply/purchase-orders'], hidden: ['/mrp/engineering/bom'] },
    { name: 'inventory-analyst', permissions: ['inventory.stock.view', 'inventory.adjustment.post', 'analytics.view'], visible: ['/inventory', '/inventory/operations/adjustment', '/mrp/analytics/inventory'], hidden: ['/inventory/operations/receipt', '/mrp/analytics/lead-time', '/administration/users'] },
  ]
  for (const profile of profiles) {
    const granted = new Set(profile.permissions); const has = (permission: string) => granted.has(permission)
    for (const path of profile.visible) assert.equal(canNavigate(path, has), true, `${profile.name} should see ${path}`)
    for (const path of profile.hidden) assert.equal(canNavigate(path, has), false, `${profile.name} should not see ${path}`)
    if (profile.viewOnly) assert.equal(canManageRoute(profile.viewOnly, has), false, `${profile.name} should be view-only at ${profile.viewOnly}`)
  }
})
