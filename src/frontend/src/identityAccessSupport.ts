export type RoleAssignmentOption = {
  id: string
  isSystemRole: boolean
  permissions?: readonly string[]
}

export type SystemRoleChange = 'PROMOTION' | 'DEMOTION' | null

export type PermissionCheck = (permission: string) => boolean

export function identityLookupAccess(has: PermissionCheck) {
  const canViewRoleCatalog = has('identity.role.view')
  return {
    canViewRoleCatalog,
    canViewPermissionCatalog: canViewRoleCatalog,
    canViewUnitCatalog: has('organization.unit.view'),
  }
}

export function isRoleAssignmentDisabled(
  role: RoleAssignmentOption,
  assignmentDisabled: boolean,
  canAssignSystemRole: boolean,
) {
  return assignmentDisabled || (role.isSystemRole && !canAssignSystemRole)
}

export function roleAssignmentCoverage(
  roles: readonly RoleAssignmentOption[],
  selectedRoleIds: readonly string[],
  catalogPermissionCodes: readonly string[] = [],
) {
  const selected = new Set(selectedRoleIds)
  const systemRoleSelected = roles.some(role => role.isSystemRole && selected.has(role.id))
  const ordinaryRoles = roles.filter(role => !role.isSystemRole)
  const allOrdinaryRolesSelected = ordinaryRoles.length > 0 && ordinaryRoles.every(role => selected.has(role.id))
  const catalog = new Set(catalogPermissionCodes)
  const selectedPermissions = new Set(
    roles
      .filter(role => selected.has(role.id))
      .flatMap(role => role.permissions ?? [])
      .filter(code => catalog.size === 0 || catalog.has(code)),
  )
  const catalogPermissionCount = catalog.size
  const selectedPermissionCount = systemRoleSelected && catalogPermissionCount > 0
    ? catalogPermissionCount
    : selectedPermissions.size

  return { systemRoleSelected, allOrdinaryRolesSelected, selectedPermissionCount, catalogPermissionCount }
}

export function systemRoleChange(
  wasSystemAdministrator: boolean,
  roles: readonly RoleAssignmentOption[],
  selectedRoleIds: readonly string[],
): SystemRoleChange {
  const willBeSystemAdministrator = roleAssignmentCoverage(roles, selectedRoleIds).systemRoleSelected
  if (!wasSystemAdministrator && willBeSystemAdministrator) return 'PROMOTION'
  if (wasSystemAdministrator && !willBeSystemAdministrator) return 'DEMOTION'
  return null
}

export function shouldPersistAccessDetails(
  roles: readonly RoleAssignmentOption[],
  selectedRoleIds: readonly string[],
) {
  return !roleAssignmentCoverage(roles, selectedRoleIds).systemRoleSelected
}
