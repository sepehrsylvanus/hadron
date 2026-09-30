using System.Security.Claims;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hadron.Erp.Api.Modules.Identity.Application;

public sealed class HadronClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options,
    HadronDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new(SecurityClaimTypes.AccessVersion, user.AccessVersion.ToString()));
        identity.AddClaim(new(SecurityClaimTypes.MustChangePassword, user.MustChangePassword ? "true" : "false"));
        identity.AddClaim(new(SecurityClaimTypes.FullName, user.FullName));

        var roleNames = await UserManager.GetRolesAsync(user);
        HashSet<string> permissions;
        var deniedPermissions = new HashSet<string>(StringComparer.Ordinal);
        if (roleNames.Contains(SystemRoleNames.SystemAdministrator, StringComparer.Ordinal))
        {
            permissions = PermissionCatalog.Codes.ToHashSet(StringComparer.Ordinal);
        }
        else
        {
            var roleIds = await db.Set<IdentityUserRole<Guid>>().AsNoTracking()
                .Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToListAsync();
            permissions = (await db.Set<IdentityRoleClaim<Guid>>().AsNoTracking()
                    .Where(x => roleIds.Contains(x.RoleId) && x.ClaimType == SecurityClaimTypes.Permission)
                    .Select(x => x.ClaimValue!).ToListAsync())
                .Where(PermissionCatalog.Codes.Contains).ToHashSet(StringComparer.Ordinal);

            var overrides = await db.UserPermissionOverrides.AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync();
            foreach (var value in overrides.Where(x => x.Effect == PermissionEffects.Grant)) permissions.Add(value.PermissionCode);
            foreach (var value in overrides.Where(x => x.Effect == PermissionEffects.Deny))
            {
                permissions.Remove(value.PermissionCode);
                deniedPermissions.Add(value.PermissionCode);
            }
        }

        foreach (var permission in permissions.Order(StringComparer.Ordinal))
            identity.AddClaim(new(SecurityClaimTypes.Permission, permission));
        foreach (var permission in deniedPermissions.Order(StringComparer.Ordinal))
            identity.AddClaim(new(SecurityClaimTypes.PermissionDeny, permission));
        return identity;
    }
}
