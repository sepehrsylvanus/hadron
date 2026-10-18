import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { dirname, extname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { canNavigate, routePermission, routePermissionRules, workspaces } from './workspaceNavigation.ts'

const sourceDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(sourceDirectory, '..', '..', '..')
const permissionCatalogSource = readFileSync(join(repositoryRoot,
  'src', 'backend', 'Hadron.Erp.Api', 'Platform', 'Security', 'PermissionCatalog.cs'), 'utf8')
const identityEndpointsSource = readFileSync(join(repositoryRoot,
  'src', 'backend', 'Hadron.Erp.Api', 'Modules', 'Identity', 'Api', 'IdentityEndpoints.cs'), 'utf8')
const identityServiceSource = readFileSync(join(repositoryRoot,
  'src', 'backend', 'Hadron.Erp.Api', 'Modules', 'Identity', 'Application', 'IdentityService.cs'), 'utf8')
const identityAdminSource = readFileSync(join(sourceDirectory, 'IdentityWorkspacePages.tsx'), 'utf8')
const appSource = readFileSync(join(sourceDirectory, 'App.tsx'), 'utf8')
const permissionAuthority = readFileSync(join(repositoryRoot, 'docs', 'PERMISSIONS.md'), 'utf8')

function collectFrontendSources(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return collectFrontendSources(path)
    return ['.ts', '.tsx'].includes(extname(entry.name)) ? [readFileSync(path, 'utf8')] : []
  })
}

function catalogContract() {
  const constants = new Map(Array.from(permissionCatalogSource.matchAll(
    /public const string (\w+) = "([a-z0-9_.]+)";/g), match => [match[1], match[2]]))
  const registrations = Array.from(permissionCatalogSource.matchAll(
    /D\(Permissions\.(\w+),\s*"([A-Z0-9_]+)",\s*"([^"]+)",\s*"([^"]+)"\)/g), match => ({
      symbol: match[1], area: match[2], nameFa: match[3], nameEn: match[4],
    }))
  return { constants, registrations, codes: new Set(constants.values()) }
}

function backendEndpointPermissions(startMarker: string, endMarker: string) {
  const { constants } = catalogContract()
  const start = identityEndpointsSource.indexOf(startMarker)
  const end = identityEndpointsSource.indexOf(endMarker, start + startMarker.length)
  assert.notEqual(start, -1, `Missing backend endpoint marker ${startMarker}`)
  assert.notEqual(end, -1, `Missing backend endpoint boundary ${endMarker}`)
  return new Set(Array.from(identityEndpointsSource.slice(start, end).matchAll(
    /\.RequirePermission\(Permissions\.(\w+)\)/g), match => constants.get(match[1])!))
}

function frontendGuardPermissions(variableName: string) {
  const declaration = identityAdminSource.match(new RegExp(
    `const ${variableName} = ([^\\r\\n]+)`))
  assert.ok(declaration, `Missing frontend guard ${variableName}`)
  return new Set(Array.from(declaration[1].matchAll(
    /has\(['"]([a-z0-9_.]+)['"]\)/g), match => match[1]))
}

test('immutable backend permissions, the admin matrix, and frontend permission checks share one catalog', () => {
  const { constants, registrations, codes } = catalogContract()
  assert.ok(codes.size > 0)
  assert.deepEqual(registrations.map(item => item.symbol).sort(), [...constants.keys()].sort())
  assert.equal(new Set(registrations.map(item => item.symbol)).size, registrations.length)
  for (const definition of registrations) {
    assert.ok(definition.area)
    assert.ok(definition.nameFa)
    assert.ok(definition.nameEn)
    const code = constants.get(definition.symbol)!
    assert.ok(permissionAuthority.includes(`\`${code}\``),
      `Canonical permission authority omits ${code}`)
  }

  assert.match(identityEndpointsSource,
    /MapGet\("\/api\/admin\/permissions",\s*\(\)\s*=>\s*PermissionCatalog\.All\)/)
  assert.match(identityAdminSource, /request<Permission\[]>\('\/admin\/permissions'\)/)
  assert.match(identityAdminSource, /permissions\.map\(permission\s*=>/)

  const permissionReferences = new Set(collectFrontendSources(sourceDirectory).flatMap(source =>
    Array.from(source.matchAll(/\bhas\(\s*['"]([a-z0-9_.]+)['"]\s*\)/g), match => match[1])
      .filter(code => code.includes('.'))))
  for (const permission of permissionReferences) {
    assert.ok(codes.has(permission), `Frontend checks unknown permission ${permission}`)
  }
  for (const rule of routePermissionRules) {
    assert.ok(codes.has(rule.view), `${rule.prefix} uses unknown view permission ${rule.view}`)
    assert.ok(codes.has(rule.manage), `${rule.prefix} uses unknown manage permission ${rule.manage}`)
  }
  for (const workspace of workspaces) {
    if (workspace.entryPermission) {
      assert.ok(codes.has(workspace.entryPermission),
        `${workspace.rootPath} uses unknown entry permission ${workspace.entryPermission}`)
    }
  }
})

test('a complete effective-permission catalog exposes every implemented workspace menu and guarded route', () => {
  const { codes } = catalogContract()
  const hasEveryPermission = (permission: string) => codes.has(permission)
  const navigationItems = workspaces.flatMap(workspace =>
    workspace.navigation.flatMap(group => group.items))

  assert.equal(new Set(routePermissionRules.map(rule => rule.prefix)).size, routePermissionRules.length)
  for (const item of navigationItems) {
    const rule = routePermission(item.path)
    assert.ok(rule, `Navigation item ${item.path} has no route guard`)
    assert.ok(canNavigate(item.path, hasEveryPermission),
      `Complete catalog cannot navigate to ${item.path}`)
    assert.ok(canNavigate(item.path, permission => permission === rule.view),
      `${item.path} is not exposed by its declared view permission`)
    assert.match(appSource, new RegExp(`<Route\\s+path="${item.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}"`),
      `Navigation item ${item.path} has no React route`)
  }

  const intentionallyIndirectRoutes = new Set([
    '/', '/mrp', '/administration', '/administration/identity',
    '/mrp/analytics', '/mrp/planning/forecast-actual', '/mrp/inventory/*', '*',
  ])
  const applicationRoutes = Array.from(appSource.matchAll(/<Route\s+path="([^"]+)"/g), match => match[1])
  for (const path of applicationRoutes) {
    if (intentionallyIndirectRoutes.has(path)) continue
    const representativePath = path.replace(/:[^/]+/g, 'contract-id').replace(/\/\*$/, '/contract-child')
    assert.ok(routePermission(representativePath), `React route ${path} has no frontend permission guard`)
  }
})

test('legacy identity route redirects to the canonical permission-aware users worklist', () => {
  assert.match(appSource,
    /<Route path="\/administration\/identity" element=\{<Navigate to="\/administration\/users" replace \/>\} \/>/)
  assert.doesNotMatch(appSource, /import\(['"]\.\/IdentityAdminPage['"]\)/)
})

test('deep application routes are guarded by their permission rule even without a navigation alias', () => {
  assert.match(appSource, /const currentPermission = routePermission\(location\.pathname\)/)
  assert.match(appSource, /currentPermission && !canNavigate\(location\.pathname, has\)/)
  assert.match(appSource,
    /currentPermission && \(location\.pathname\.endsWith\('\/new'\) \|\| location\.pathname\.endsWith\('\/edit'\)\) && !canManageRoute/)
  assert.doesNotMatch(appSource, /currentRoute && !canNavigate\(location\.pathname, has\)/)
})

test('identity create controls and conditional assignment match backend authorization', () => {
  const createUserEndpoint = backendEndpointPermissions(
    'usersApi.MapPost("/"', 'usersApi.MapPut("/{id:guid}"')
  const createRoleEndpoint = backendEndpointPermissions(
    'roles.MapPost("/"', 'roles.MapPut("/{id:guid}"')

  assert.deepEqual(frontendGuardPermissions('canCreateUser'), createUserEndpoint)
  assert.deepEqual(frontendGuardPermissions('canDefineRole'), createRoleEndpoint)
  assert.deepEqual(createUserEndpoint, new Set(['identity.user.manage']))
  assert.deepEqual(createRoleEndpoint, new Set(['identity.role.manage']))
  assert.match(identityServiceSource,
    /EnsurePermissionAssignmentAuthorized\(actorId, roleIds\.Count > 0, ct\)/)
  assert.match(identityServiceSource,
    /EnsurePermissionAssignmentAuthorized\(actorId, codes\.Count > 0, ct\)/)
})
