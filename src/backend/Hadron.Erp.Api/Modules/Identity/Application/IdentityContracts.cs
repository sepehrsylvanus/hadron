namespace Hadron.Erp.Api.Modules.Identity.Application;

public sealed record LoginRequest(string UserName, string Password);
public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ResetPasswordRequest(string TemporaryPassword);
public sealed record UpdateUserRequest(string FirstName, string LastName, string EmailAddress,
    string CellphoneNumber, Guid OrganizationalUnitId);
public sealed record ChangeUserStateRequest(bool IsActive);
public sealed record ReplaceUserRolesRequest(IReadOnlyList<Guid> RoleIds);
public sealed record PermissionOverrideInput(string PermissionCode, string Effect);
public sealed record ReplaceUserPermissionOverridesRequest(IReadOnlyList<PermissionOverrideInput> Overrides);
public sealed record DataScopeInput(string ScopeType, string? ScopeValue);
public sealed record ReplaceUserDataScopesRequest(IReadOnlyList<DataScopeInput> Scopes);
public sealed record CreateRoleRequest(string Name, string? DescriptionFa, string? DescriptionEn,
    IReadOnlyList<string>? PermissionCodes = null);
public sealed record UpdateRoleRequest(string Name, string? DescriptionFa, string? DescriptionEn);
public sealed record UpdateRoleDefinitionRequest(string Name, string? DescriptionFa, string? DescriptionEn,
    IReadOnlyList<string> PermissionCodes);
public sealed record ReplaceRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);
public sealed record CreateOrganizationalUnitRequest(string NameFa, string? NameEn);
public sealed record UpdateOrganizationalUnitRequest(string NameFa, string? NameEn, long ExpectedVersion);
public sealed record ChangeOrganizationalUnitStateRequest(long ExpectedVersion);

public sealed record OrganizationalUnitResponse(Guid Id, string NameFa, string? NameEn, bool IsActive, long Version);
public sealed record UserSummaryResponse(Guid Id, string UserName, string FirstName, string LastName, string FullName, string EmailAddress,
    string CellphoneNumber, OrganizationalUnitResponse OrganizationalUnit, string PhotoUrl,
    bool IsActive, bool MustChangePassword, bool IsSystemAdministrator,
    IReadOnlyList<string> RoleNames, DateTime? LastSeenAtUtc);
public sealed record UserDetailResponse(UserSummaryResponse User, IReadOnlyList<RoleResponse> Roles,
    IReadOnlyList<PermissionOverrideResponse> PermissionOverrides, IReadOnlyList<DataScopeResponse> DataScopes,
    IReadOnlyList<string> EffectivePermissions, IReadOnlyList<EffectiveAccessResponse> EffectiveAccess);
public sealed record CurrentUserResponse(Guid Id, string UserName, string FullName, string EmailAddress,
    string CellphoneNumber, OrganizationalUnitResponse OrganizationalUnit, string PhotoUrl,
    bool MustChangePassword, bool IsSystemAdministrator, IReadOnlyList<DataScopeResponse> DataScopes,
    IReadOnlyList<string> Permissions);
public sealed record RoleResponse(Guid Id, string Name, string? DescriptionFa, string? DescriptionEn,
    bool IsSystemRole, IReadOnlyList<string> Permissions);
public sealed record RoleWorklistResponse(Guid Id, string Name, string? DescriptionFa, string? DescriptionEn,
    bool IsSystemRole, int PermissionCount, int UserCount);
public sealed record OrganizationalUnitWorklistResponse(Guid Id, string NameFa, string? NameEn, bool IsActive,
    long Version, int AssignedUserCount, int ActiveUserCount);
public sealed record EffectiveAccessResponse(string Code, string Area, string NameFa, string NameEn,
    bool IsGranted, string Resolution, IReadOnlyList<string> Sources);
public sealed record PermissionOverrideResponse(string PermissionCode, string Effect);
public sealed record DataScopeResponse(string ScopeType, string? ScopeValue);
public sealed record SessionResponse(Guid Id, DateTime CreatedAtUtc, DateTime LastSeenAtUtc,
    DateTime? RevokedAtUtc, string? UserAgent, bool IsCurrent);
public sealed record SecurityAuditResponse(Guid Id, Guid? ActorUserId, Guid? TargetUserId, string Action,
    string? Detail, string? CorrelationId, string Source, DateTime CreatedAtUtc);
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed class IdentityValidationException(Dictionary<string, string[]> errors) : Exception("Identity validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class IdentityConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class IdentityAuthorizationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
