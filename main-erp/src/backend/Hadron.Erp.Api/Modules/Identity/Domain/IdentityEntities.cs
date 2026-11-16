using Microsoft.AspNetCore.Identity;

namespace Hadron.Erp.Api.Modules.Identity.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid OrganizationalUnitId { get; set; }
    public OrganizationalUnit OrganizationalUnit { get; set; } = null!;
    public string? ProfilePhotoObjectKey { get; set; }
    public string? ProfilePhotoContentType { get; set; }
    public long? ProfilePhotoSize { get; set; }
    public string? ProfilePhotoSha256 { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public long AccessVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public string? DescriptionFa { get; set; }
    public string? DescriptionEn { get; set; }
    public bool IsSystemRole { get; set; }
}

public sealed class OrganizationalUnit
{
    private OrganizationalUnit() { }
    public OrganizationalUnit(string nameFa, string? nameEn)
    {
        Id = Guid.NewGuid();
        NameFa = nameFa.Trim();
        NameEn = Normalize(nameEn);
        IsActive = true;
        Version = 1;
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string NameFa { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string nameFa, string? nameEn)
    {
        NameFa = nameFa.Trim();
        NameEn = Normalize(nameEn);
        Touch();
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        Touch();
    }

    private void Touch() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class UserPermissionOverride
{
    private UserPermissionOverride() { }
    public UserPermissionOverride(Guid userId, string permissionCode, string effect, Guid actorUserId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        PermissionCode = permissionCode;
        Effect = effect;
        ActorUserId = actorUserId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ApplicationUser User { get; private set; } = null!;
    public string PermissionCode { get; private set; } = string.Empty;
    public string Effect { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class UserSession
{
    private UserSession() { }
    public UserSession(Guid userId, string? userAgent)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent[..Math.Min(500, userAgent.Length)];
        CreatedAtUtc = LastSeenAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ApplicationUser User { get; private set; } = null!;
    public string? UserAgent { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public void Seen() => LastSeenAtUtc = DateTime.UtcNow;
    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;
}

public sealed class UserDataScope
{
    private UserDataScope() { }
    public UserDataScope(Guid userId, string scopeType, string? scopeValue = null)
    {
        Id = Guid.NewGuid(); UserId = userId; ScopeType = scopeType; ScopeValue = scopeValue; CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ApplicationUser User { get; private set; } = null!;
    public string ScopeType { get; private set; } = string.Empty;
    public string? ScopeValue { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class SecurityAuditEvent
{
    private SecurityAuditEvent() { }
    public SecurityAuditEvent(Guid? actorUserId, Guid? targetUserId, string action, string? detail,
        string? correlationId, string source)
    {
        Id = Guid.NewGuid();
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        Action = action;
        Detail = detail;
        CorrelationId = correlationId;
        Source = source;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? TargetUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string? CorrelationId { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}

public static class PermissionEffects
{
    public const string Grant = "GRANT";
    public const string Deny = "DENY";
}

public static class DataScopeTypes
{
    public const string All = "ALL";
    public const string Warehouse = "WAREHOUSE";
}
