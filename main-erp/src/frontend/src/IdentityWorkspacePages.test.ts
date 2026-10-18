import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { identityLookupAccess, isRoleAssignmentDisabled, roleAssignmentCoverage, shouldPersistAccessDetails, systemRoleChange } from './identityAccessSupport.ts'

const source = readFileSync(new URL('./IdentityWorkspacePages.tsx', import.meta.url), 'utf8')

test('identity wizard primary actions keep distinct React keys and explicit button semantics', () => {
  for (const wizard of ['create-user', 'create-role']) {
    assert.match(source, new RegExp(`<button key="${wizard}-next" type="button"`))
    assert.match(source, new RegExp(`<button key="${wizard}-submit" type="submit"`))
  }
})

test('identity wizard step navigation never submits its form', () => {
  const stepBars = Array.from(source.matchAll(/<div className="admin-wizard-steps"[^>]*>(.*?)<\/div>/gs), match => match[1])
  assert.equal(stepBars.length, 2)

  for (const stepBar of stepBars) {
    const buttons = Array.from(stepBar.matchAll(/<button\b([^>]*)>/g), match => match[1])
    assert.equal(buttons.length, 3)
    for (const attributes of buttons) assert.match(attributes, /\btype="button"/)
  }
})

test('only a current system administrator may assign the dynamic system role', () => {
  const ordinaryRole = { id: 'planner', isSystemRole: false }
  const systemRole = { id: 'system-admin', isSystemRole: true }

  assert.equal(isRoleAssignmentDisabled(ordinaryRole, false, false), false)
  assert.equal(isRoleAssignmentDisabled(systemRole, false, false), true)
  assert.equal(isRoleAssignmentDisabled(systemRole, false, true), false)
  assert.equal(isRoleAssignmentDisabled(systemRole, true, true), true)
})

test('selecting every ordinary role is not presented as complete dynamic access', () => {
  const roles = [
    { id: 'system-admin', isSystemRole: true, permissions: [] },
    { id: 'planner', isSystemRole: false, permissions: ['planning.forecast.view', 'stale.permission'] },
    { id: 'buyer', isSystemRole: false, permissions: ['procurement.purchase_order.view'] },
  ]
  const catalog = ['planning.forecast.view', 'procurement.purchase_order.view', 'inventory.stock.view']

  assert.deepEqual(roleAssignmentCoverage(roles, ['planner', 'buyer'], catalog), {
    systemRoleSelected: false,
    allOrdinaryRolesSelected: true,
    selectedPermissionCount: 2,
    catalogPermissionCount: 3,
  })
  assert.deepEqual(roleAssignmentCoverage(roles, ['system-admin'], catalog), {
    systemRoleSelected: true,
    allOrdinaryRolesSelected: false,
    selectedPermissionCount: 3,
    catalogPermissionCount: 3,
  })
})

test('system role changes require confirmation and suppress subordinate access writes', () => {
  const roles = [
    { id: 'system-admin', isSystemRole: true },
    { id: 'planner', isSystemRole: false },
  ]

  assert.equal(systemRoleChange(false, roles, ['system-admin']), 'PROMOTION')
  assert.equal(systemRoleChange(true, roles, ['planner']), 'DEMOTION')
  assert.equal(systemRoleChange(false, roles, ['planner']), null)
  assert.equal(systemRoleChange(true, roles, ['system-admin']), null)
  assert.equal(shouldPersistAccessDetails(roles, ['system-admin']), false)
  assert.equal(shouldPersistAccessDetails(roles, ['planner']), true)
})

test('system-role save flow is explicit and does not persist overrides or scopes', () => {
  assert.match(source, /const persistDetails = shouldPersistAccessDetails\(roles, roleIds\)/)
  assert.match(source, /if \(persistDetails\) \{[\s\S]*permission-overrides[\s\S]*data-scopes[\s\S]*\}/)
  assert.match(source, /open=\{accessConfirm !== null\}/)
})

test('metadata creation stays manage-only while initial assignment remains separately guarded', () => {
  assert.match(source, /const canCreateUser = has\('identity\.user\.manage'\)/)
  assert.match(source, /const canCreateRole = canManageRole/)
  assert.match(source, /mode === 'create' && !has\('identity\.user\.manage'\)/)
  assert.match(source, /disabled=\{!has\('identity\.permission\.assign'\)\}/)
})

test('role metadata editing is independent from permission-matrix editing', () => {
  assert.match(source, /const permissionEditing = editing && canAssignPermissions && !role\?\.isSystemRole/)
  assert.match(source, /const endpoint = canAssignPermissions \? `\/admin\/roles\/\$\{id\}\/definition` : `\/admin\/roles\/\$\{id\}`/)
  assert.match(source, /<input type="checkbox" disabled=\{!permissionEditing\}/)
  assert.match(source, /\{permissionEditing && <button type="button" className="button button--quiet"/)
})

test('identity lookup catalogs follow their own read permissions', () => {
  const access = identityLookupAccess(permission => new Set([
    'identity.user.view',
    'organization.unit.view',
  ]).has(permission))

  assert.deepEqual(access, {
    canViewRoleCatalog: false,
    canViewPermissionCatalog: false,
    canViewUnitCatalog: true,
  })
})

test('optional identity lookup failures cannot become a page-level error', () => {
  assert.doesNotMatch(source, /Promise\.all\(\[request<Role\[\]>\('\/admin\/roles'\), request<Unit\[\]>\('\/organization\/units\?includeInactive=true'\)/)
  assert.match(source, /if \(canViewRoleCatalog\) void request<Role\[\]>\('\/admin\/roles'\)/)
  assert.match(source, /if \(canViewUnitCatalog\) void request<Unit\[\]>\('\/organization\/units\?includeInactive=true'\)/)
  assert.match(source, /if \(canViewPermissionCatalog\) void request<Permission\[\]>\('\/admin\/permissions'\)/)
})
