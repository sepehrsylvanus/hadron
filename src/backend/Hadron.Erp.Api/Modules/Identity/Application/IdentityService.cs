using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Hadron.Erp.Api.Platform.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Identity.Application;

public sealed class IdentityService(
    HadronDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IProfilePhotoStore photoStore,
    ILogger<IdentityService> logger)
{
    public async Task EnsureSystemRole(CancellationToken ct)
    {
        if (await roleManager.RoleExistsAsync(SystemRoleNames.SystemAdministrator)) return;
        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = SystemRoleNames.SystemAdministrator,
            DescriptionFa = "مدیر سامانه",
            DescriptionEn = "System administrator",
            IsSystemRole = true
        };
        EnsureIdentity(await roleManager.CreateAsync(role));
    }

    public async Task<PagedResponse<UserSummaryResponse>> Users(string? search, bool includeInactive,
        bool? isActive, bool? mustChangePassword, Guid? organizationalUnitId, Guid? roleId,
        string? sort, bool descending, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking().Include(x => x.OrganizationalUnit).AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        if (mustChangePassword.HasValue) query = query.Where(x => x.MustChangePassword == mustChangePassword.Value);
        if (organizationalUnitId.HasValue) query = query.Where(x => x.OrganizationalUnitId == organizationalUnitId.Value);
        if (roleId.HasValue)
        {
            var selectedRoleId = roleId.Value;
            query = query.Where(x => db.Set<IdentityUserRole<Guid>>().Any(link => link.UserId == x.Id && link.RoleId == selectedRoleId));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.UserName!.Contains(term) || x.FirstName.Contains(term) ||
                x.LastName.Contains(term) || x.Email!.Contains(term) || x.PhoneNumber!.Contains(term) ||
                x.OrganizationalUnit.NameFa.Contains(term) || (x.OrganizationalUnit.NameEn != null && x.OrganizationalUnit.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var ordered = sort?.Trim().ToUpperInvariant() switch
        {
            "USERNAME" => descending ? query.OrderByDescending(x => x.UserName) : query.OrderBy(x => x.UserName),
            "UNIT" => descending ? query.OrderByDescending(x => x.OrganizationalUnit.NameFa).ThenByDescending(x => x.LastName) : query.OrderBy(x => x.OrganizationalUnit.NameFa).ThenBy(x => x.LastName),
            "STATUS" => descending ? query.OrderByDescending(x => x.IsActive).ThenBy(x => x.LastName) : query.OrderBy(x => x.IsActive).ThenBy(x => x.LastName),
            _ => descending ? query.OrderByDescending(x => x.LastName).ThenByDescending(x => x.FirstName) : query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
        };
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var rolePairs = await (from link in db.Set<IdentityUserRole<Guid>>().AsNoTracking()
                               join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
                               where ids.Contains(link.UserId)
                               select new { link.UserId, RoleName = role.Name! }).ToListAsync(ct);
        var roleNames = rolePairs.GroupBy(x => x.UserId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<string>)x.Select(value => value.RoleName).Order().ToList());
        var lastSeen = await db.UserSessions.AsNoTracking().Where(x => ids.Contains(x.UserId))
            .GroupBy(x => x.UserId).Select(group => new { UserId = group.Key, LastSeenAtUtc = group.Max(x => x.LastSeenAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => (DateTime?)x.LastSeenAtUtc, ct);
        var adminIds = await SystemAdministratorIds(ct);
        return new(rows.Select(x => Summary(x, adminIds.Contains(x.Id),
            roleNames.GetValueOrDefault(x.Id, []), lastSeen.GetValueOrDefault(x.Id))).ToList(), total, page, pageSize);
    }

    public async Task<UserDetailResponse?> User(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.OrganizationalUnit)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return null;
        var roleIds = await db.Set<IdentityUserRole<Guid>>().AsNoTracking().Where(x => x.UserId == id)
            .Select(x => x.RoleId).ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking().Where(x => roleIds.Contains(x.Id)).OrderBy(x => x.Name)
            .ToListAsync(ct);
        var roleDtos = new List<RoleResponse>();
        foreach (var role in roles) roleDtos.Add(await RoleResponse(role, ct));
        var overrides = await db.UserPermissionOverrides.AsNoTracking().Where(x => x.UserId == id)
            .OrderBy(x => x.PermissionCode).Select(x => new PermissionOverrideResponse(x.PermissionCode, x.Effect))
            .ToListAsync(ct);
        var isAdmin = roles.Any(x => x.Name == SystemRoleNames.SystemAdministrator);
        var effective = await EffectivePermissions(id, isAdmin, ct);
        var scopes = await db.UserDataScopes.AsNoTracking().Where(x => x.UserId == id)
            .OrderBy(x => x.ScopeType).Select(x => new DataScopeResponse(x.ScopeType, x.ScopeValue)).ToListAsync(ct);
        var lastSeen = await db.UserSessions.AsNoTracking().Where(x => x.UserId == id)
            .Select(x => (DateTime?)x.LastSeenAtUtc).MaxAsync(ct);
        return new(Summary(user, isAdmin, roles.Select(x => x.Name!).ToList(), lastSeen), roleDtos, overrides, scopes,
            effective, await EffectiveAccess(id, isAdmin, ct));
    }

    public async Task<ApplicationUser> CreateUser(IFormCollection form, Guid actorId, CancellationToken ct)
    {
        var userName = Required(form, "userName");
        var password = Required(form, "temporaryPassword");
        var firstName = Required(form, "firstName");
        var lastName = Required(form, "lastName");
        var email = Required(form, "emailAddress");
        var cellphone = Required(form, "cellphoneNumber");
        if (!Guid.TryParse(Required(form, "organizationalUnitId"), out var unitId))
            throw Validation("organizationalUnitId", "واحد سازمانی معتبر نیست.");
        var unit = await db.OrganizationalUnits.SingleOrDefaultAsync(x => x.Id == unitId, ct);
        if (unit is null || !unit.IsActive) throw Conflict("ORGANIZATIONAL_UNIT_INACTIVE", "واحد سازمانی باید فعال باشد.");

        var roleIds = form["roleIds"].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value =>
            Guid.TryParse(value, out var parsed) ? parsed : throw Validation("roleIds", "یک یا چند نقش معتبر نیست.")).Distinct().ToList();
        var requestedRoles = roleIds.Count == 0
            ? []
            : await db.Roles.Where(x => roleIds.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
        if (requestedRoles.Count != roleIds.Count) throw Validation("roleIds", "یک یا چند نقش معتبر نیست.");
        await EnsureSystemAdministratorAssignmentAuthorized(requestedRoles, actorId, ct);
        await EnsurePermissionAssignmentAuthorized(actorId, roleIds.Count > 0, ct);

        ProfilePhotoContent? photo = null;
        var file = form.Files.GetFile("photo");
        if (file is not null) photo = await ProfilePhotoValidator.Read(file, ct);
        var requestedActive = bool.TryParse(form["isActive"], out var active) && active;
        if (requestedActive && photo is null) throw Conflict("PROFILE_PHOTO_REQUIRED", "برای فعال‌سازی کاربر، عکس پرسنلی الزامی است.");

        var user = NewUser(userName, firstName, lastName, email, cellphone, unitId, false);
        await using var photoLease = photo is null ? null : CreatePhotoWriteLease(user, photo);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        EnsureIdentity(await userManager.CreateAsync(user, password));
        if (requestedRoles.Any(x => x.Name == SystemRoleNames.SystemAdministrator))
            db.UserDataScopes.Add(new(user.Id, DataScopeTypes.All));
        if (photo is not null && photoLease is not null) await SavePhoto(user, photo, photoLease, ct);
        if (requestedRoles.Count > 0) EnsureIdentity(await userManager.AddToRolesAsync(user, requestedRoles.Select(x => x.Name!).ToArray()));
        user.IsActive = requestedActive;
        await Audit(actorId, user.Id, "USER_CREATED", new { user.UserName, user.IsActive, user.OrganizationalUnitId, RoleIds = roleIds }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        photoLease?.Complete();
        return user;
    }

    public async Task<ApplicationUser> Bootstrap(IFormCollection form, string? suppliedToken,
        string? expectedToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedToken) || !CryptographicEquals(suppliedToken, expectedToken))
            throw new UnauthorizedAccessException("BOOTSTRAP_TOKEN_INVALID");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(5206517764525541700)", ct);
        if (await db.Users.AnyAsync(ct)) throw Conflict("BOOTSTRAP_ALREADY_COMPLETED", "راه‌اندازی اولیه قبلاً انجام شده است.");
        var photoFile = form.Files.GetFile("photo") ?? throw Conflict("PROFILE_PHOTO_REQUIRED", "عکس پرسنلی مدیر سامانه الزامی است.");
        var photo = await ProfilePhotoValidator.Read(photoFile, ct);
        var unit = new OrganizationalUnit(Required(form, "organizationalUnitNameFa"), Optional(form, "organizationalUnitNameEn"));
        db.OrganizationalUnits.Add(unit);
        var user = NewUser(Required(form, "userName"), Required(form, "firstName"), Required(form, "lastName"),
            Required(form, "emailAddress"), Required(form, "cellphoneNumber"), unit.Id, false);
        await using var photoLease = CreatePhotoWriteLease(user, photo);
        EnsureIdentity(await userManager.CreateAsync(user, Required(form, "temporaryPassword")));
        db.UserDataScopes.Add(new(user.Id, DataScopeTypes.All));
        await EnsureSystemRole(ct);
        EnsureIdentity(await userManager.AddToRoleAsync(user, SystemRoleNames.SystemAdministrator));
        await SavePhoto(user, photo, photoLease, ct);
        user.IsActive = true;
        await Audit(null, user.Id, "SYSTEM_BOOTSTRAPPED", new { user.UserName, unit.NameFa }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        photoLease.Complete();
        return user;
    }

    public async Task UpdateUser(Guid id, UpdateUserRequest request, Guid actorId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var unit = await db.OrganizationalUnits.SingleOrDefaultAsync(x => x.Id == request.OrganizationalUnitId, ct);
        if (unit is null || !unit.IsActive) throw Conflict("ORGANIZATIONAL_UNIT_INACTIVE", "واحد سازمانی باید فعال باشد.");
        RequiredText(request.FirstName, "firstName"); RequiredText(request.LastName, "lastName");
        RequiredText(request.EmailAddress, "emailAddress"); RequiredText(request.CellphoneNumber, "cellphoneNumber");
        user.FirstName = request.FirstName.Trim(); user.LastName = request.LastName.Trim();
        user.Email = request.EmailAddress.Trim(); user.PhoneNumber = request.CellphoneNumber.Trim();
        user.OrganizationalUnitId = request.OrganizationalUnitId; user.UpdatedAtUtc = DateTime.UtcNow;
        EnsureIdentity(await userManager.UpdateAsync(user));
        await Audit(actorId, id, "USER_PROFILE_UPDATED", new { user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.OrganizationalUnitId }, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplacePhoto(Guid id, IFormFile file, Guid actorId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var photo = await ProfilePhotoValidator.Read(file, ct);
        var oldKey = user.ProfilePhotoObjectKey;
        await using var photoLease = CreatePhotoWriteLease(user, photo);
        await SavePhoto(user, photo, photoLease, ct);
        user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow;
        RevokeSessions(user.Id);
        await Audit(actorId, id, "USER_PHOTO_UPDATED", new { photo.ContentType, Size = photo.Bytes.LongLength, photo.Sha256 }, ct);
        await db.SaveChangesAsync(ct);
        photoLease.Complete();
        if (oldKey is not null && oldKey != user.ProfilePhotoObjectKey)
            await DeletePhotoBestEffort(oldKey);
    }

    public async Task ChangeState(Guid id, bool active, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockSystemAdministratorInvariant(ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (active)
        {
            if (user.ProfilePhotoObjectKey is null) throw Conflict("PROFILE_PHOTO_REQUIRED", "برای فعال‌سازی کاربر، عکس پرسنلی الزامی است.");
            var unitActive = await db.OrganizationalUnits.Where(x => x.Id == user.OrganizationalUnitId).Select(x => x.IsActive).SingleAsync(ct);
            if (!unitActive) throw Conflict("ORGANIZATIONAL_UNIT_INACTIVE", "واحد سازمانی باید فعال باشد.");
        }
        else if (await IsSystemAdministrator(user.Id, ct) && await ActiveSystemAdministratorCount(ct) <= 1)
            throw Conflict("LAST_SYSTEM_ADMINISTRATOR", "آخرین مدیر فعال سامانه را نمی‌توان غیرفعال کرد.");
        user.IsActive = active; user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow;
        if (!active) RevokeSessions(user.Id);
        await Audit(actorId, id, active ? "USER_ACTIVATED" : "USER_DEACTIVATED", null, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ResetPassword(Guid id, string temporaryPassword, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        EnsureIdentity(await userManager.ResetPasswordAsync(user, token, temporaryPassword));
        user.MustChangePassword = true; user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow;
        RevokeSessions(id);
        await Audit(actorId, id, "PASSWORD_RESET", null, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ChangeOwnPassword(Guid id, string currentPassword, string newPassword,
        string? currentSessionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        EnsureIdentity(await userManager.ChangePasswordAsync(user, currentPassword, newPassword));
        user.MustChangePassword = false; user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var session in db.UserSessions.Where(x => x.UserId == id && x.Id.ToString() != currentSessionId && x.RevokedAtUtc == null))
            session.Revoke();
        await Audit(id, id, "PASSWORD_CHANGED", null, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReplaceRoles(Guid id, IReadOnlyList<Guid> roleIds, Guid actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockSystemAdministratorInvariant(ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var roles = await db.Roles.Where(x => roleIds.Contains(x.Id)).ToListAsync(ct);
        if (roles.Count != roleIds.Distinct().Count()) throw Validation("roleIds", "یک یا چند نقش معتبر نیست.");
        var wasAdmin = await IsSystemAdministrator(id, ct);
        var willBeAdmin = roles.Any(x => x.Name == SystemRoleNames.SystemAdministrator);
        if ((wasAdmin || willBeAdmin) && !await IsSystemAdministrator(actorId, ct))
            throw new IdentityAuthorizationException("SYSTEM_ADMINISTRATOR_ASSIGNMENT_REQUIRES_SYSTEM_ADMINISTRATOR",
                "فقط مدیر سامانه می‌تواند نقش مدیر سامانه را تخصیص دهد یا حذف کند.");
        if (wasAdmin && !willBeAdmin && user.IsActive && await ActiveSystemAdministratorCount(ct) <= 1)
            throw Conflict("LAST_SYSTEM_ADMINISTRATOR", "نقش آخرین مدیر فعال سامانه قابل حذف نیست.");
        var clearedPermissionOverrideCount = 0;
        if (willBeAdmin)
        {
            var existingOverrides = await db.UserPermissionOverrides.Where(x => x.UserId == id).ToListAsync(ct);
            clearedPermissionOverrideCount = existingOverrides.Count;
            db.UserPermissionOverrides.RemoveRange(existingOverrides);
        }
        var current = await userManager.GetRolesAsync(user);
        if (current.Count > 0) EnsureIdentity(await userManager.RemoveFromRolesAsync(user, current));
        if (roles.Count > 0) EnsureIdentity(await userManager.AddToRolesAsync(user, roles.Select(x => x.Name!).ToArray()));
        if (willBeAdmin || wasAdmin)
        {
            db.UserDataScopes.RemoveRange(db.UserDataScopes.Where(x => x.UserId == id));
            if (willBeAdmin) db.UserDataScopes.Add(new UserDataScope(id, DataScopeTypes.All));
        }
        user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow; RevokeSessions(id);
        await Audit(actorId, id, "USER_ROLES_REPLACED",
            new { RoleIds = roleIds, ClearedPermissionOverrideCount = clearedPermissionOverrideCount }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReplaceOverrides(Guid id, IReadOnlyList<PermissionOverrideInput> inputs, Guid actorId, CancellationToken ct)
    {
        if (await IsSystemAdministrator(id, ct) && inputs.Count > 0)
            throw Conflict("SYSTEM_ADMINISTRATOR_NOT_NARROWABLE", "دسترسی نقش مدیر سامانه قابل محدودکردن نیست.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var normalized = inputs.Select(x => new PermissionOverrideInput(x.PermissionCode.Trim(), x.Effect.Trim().ToUpperInvariant())).ToList();
        if (normalized.Any(x => !PermissionCatalog.Codes.Contains(x.PermissionCode) || x.Effect is not (PermissionEffects.Grant or PermissionEffects.Deny)) ||
            normalized.Select(x => x.PermissionCode).Distinct().Count() != normalized.Count)
            throw Validation("overrides", "دسترسی یا نوع تخصیص معتبر نیست.");
        db.UserPermissionOverrides.RemoveRange(db.UserPermissionOverrides.Where(x => x.UserId == id));
        db.UserPermissionOverrides.AddRange(normalized.Select(x => new UserPermissionOverride(id, x.PermissionCode, x.Effect, actorId)));
        user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow; RevokeSessions(id);
        await Audit(actorId, id, "USER_PERMISSION_OVERRIDES_REPLACED", new { Overrides = normalized }, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<DataScopeResponse>> DataScopes(Guid userId, CancellationToken ct) =>
        db.UserDataScopes.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.ScopeType).ThenBy(x => x.ScopeValue)
            .Select(x => new DataScopeResponse(x.ScopeType, x.ScopeValue)).ToListAsync(ct);

    public async Task ReplaceDataScopes(Guid id, IReadOnlyList<DataScopeInput> inputs,
        Guid actorId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var isAdmin = await IsSystemAdministrator(id, ct);
        var normalized = inputs.Select(x => new DataScopeInput(x.ScopeType.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(x.ScopeValue) ? null : x.ScopeValue.Trim())).ToList();
        if (normalized.Select(x => $"{x.ScopeType}:{x.ScopeValue}").Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != normalized.Count)
            throw Validation("scopes", "محدوده داده تکراری است.");
        if (normalized.Any(x => x.ScopeType is not (DataScopeTypes.All or DataScopeTypes.Warehouse)))
            throw Validation("scopes", "نوع محدوده داده معتبر نیست.");
        var all = normalized.Where(x => x.ScopeType == DataScopeTypes.All).ToList();
        if (all.Any(x => x.ScopeValue is not null) || all.Count > 1 ||
            (all.Count == 1 && normalized.Count != 1))
            throw Validation("scopes", "محدوده ALL مقدار ندارد و با محدوده انبار ترکیب نمی‌شود.");
        if (isAdmin && (normalized.Count != 1 || normalized[0].ScopeType != DataScopeTypes.All))
            throw Conflict("SYSTEM_ADMINISTRATOR_SCOPE_NOT_NARROWABLE",
                "مدیر سامانه باید محدوده کامل ALL داشته باشد.");
        var warehouseValues = normalized.Where(x => x.ScopeType == DataScopeTypes.Warehouse).ToList();
        var warehouseIds = warehouseValues.Select(x => Guid.TryParse(x.ScopeValue, out var value)
            ? value : throw Validation("scopes", "شناسه انبار در محدوده داده معتبر نیست."))
            .Distinct().ToArray();
        if (warehouseIds.Length > 0 && await db.Warehouses.CountAsync(x => warehouseIds.Contains(x.Id), ct)
            != warehouseIds.Length)
            throw Validation("scopes", "یک یا چند انبار محدوده داده وجود ندارد.");
        db.UserDataScopes.RemoveRange(db.UserDataScopes.Where(x => x.UserId == id));
        db.UserDataScopes.AddRange(normalized.Select(x => new UserDataScope(id, x.ScopeType, x.ScopeValue)));
        user.AccessVersion++;
        user.UpdatedAtUtc = DateTime.UtcNow;
        RevokeSessions(id);
        await Audit(actorId, id, "USER_DATA_SCOPES_REPLACED", new { Scopes = normalized }, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RoleResponse>> Roles(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var result = new List<RoleResponse>();
        foreach (var role in roles) result.Add(await RoleResponse(role, ct));
        return result;
    }

    public async Task<PagedResponse<RoleWorklistResponse>> RoleWorklist(string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Roles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name!.Contains(term) || (x.DescriptionFa != null && x.DescriptionFa.Contains(term)) ||
                (x.DescriptionEn != null && x.DescriptionEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.IsSystemRole).ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var knownPermissionCodes = PermissionCatalog.Codes.ToArray();
        var permissionCounts = await db.Set<IdentityRoleClaim<Guid>>().AsNoTracking()
            .Where(x => ids.Contains(x.RoleId) && x.ClaimType == SecurityClaimTypes.Permission &&
                knownPermissionCodes.Contains(x.ClaimValue!))
            .GroupBy(x => x.RoleId).Select(group => new
            {
                RoleId = group.Key,
                Count = group.Select(claim => claim.ClaimValue).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);
        var userCounts = await db.Set<IdentityUserRole<Guid>>().AsNoTracking().Where(x => ids.Contains(x.RoleId))
            .GroupBy(x => x.RoleId).Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);
        return new(rows.Select(role => new RoleWorklistResponse(role.Id, role.Name!, role.DescriptionFa, role.DescriptionEn,
            role.IsSystemRole, role.IsSystemRole ? PermissionCatalog.Codes.Count : permissionCounts.GetValueOrDefault(role.Id),
            userCounts.GetValueOrDefault(role.Id))).ToList(), total, page, pageSize);
    }

    public async Task<RoleResponse> CreateRole(CreateRoleRequest request, Guid actorId, CancellationToken ct)
    {
        RequiredText(request.Name, "name");
        var codes = (request.PermissionCodes ?? []).Select(x => x.Trim()).Distinct().ToList();
        if (codes.Any(x => !PermissionCatalog.Codes.Contains(x))) throw Validation("permissionCodes", "کد دسترسی ناشناخته است.");
        await EnsurePermissionAssignmentAuthorized(actorId, codes.Count > 0, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = request.Name.Trim(), DescriptionFa = Normalize(request.DescriptionFa), DescriptionEn = Normalize(request.DescriptionEn) };
        EnsureIdentity(await roleManager.CreateAsync(role));
        db.AddRange(codes.Select(x => new IdentityRoleClaim<Guid> { RoleId = role.Id, ClaimType = SecurityClaimTypes.Permission, ClaimValue = x }));
        await Audit(actorId, null, "ROLE_CREATED",
            new { role.Id, role.Name, Permissions = SummarizeAuditValues(codes, "permission codes") }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await RoleResponse(role, ct);
    }

    public async Task<RoleResponse> UpdateRole(Guid id, UpdateRoleRequest request, Guid actorId, CancellationToken ct)
    {
        RequiredText(request.Name, "name");
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (role.IsSystemRole) throw Conflict("SYSTEM_ROLE_IMMUTABLE", "نقش سیستمی قابل ویرایش نیست.");
        role.Name = request.Name.Trim(); role.DescriptionFa = Normalize(request.DescriptionFa); role.DescriptionEn = Normalize(request.DescriptionEn);
        EnsureIdentity(await roleManager.UpdateAsync(role));
        await Audit(actorId, null, "ROLE_UPDATED", new { role.Id, role.Name, role.DescriptionFa, role.DescriptionEn }, ct);
        await db.SaveChangesAsync(ct);
        return await RoleResponse(role, ct);
    }

    public async Task<RoleResponse> UpdateRoleDefinition(Guid id, UpdateRoleDefinitionRequest request, Guid actorId, CancellationToken ct)
    {
        RequiredText(request.Name, "name");
        var codes = request.PermissionCodes.Select(x => x.Trim()).Distinct().ToList();
        if (codes.Any(x => !PermissionCatalog.Codes.Contains(x))) throw Validation("permissionCodes", "کد دسترسی ناشناخته است.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (role.IsSystemRole) throw Conflict("SYSTEM_ROLE_IMMUTABLE", "نقش سیستمی قابل ویرایش نیست.");
        role.Name = request.Name.Trim(); role.DescriptionFa = Normalize(request.DescriptionFa); role.DescriptionEn = Normalize(request.DescriptionEn);
        EnsureIdentity(await roleManager.UpdateAsync(role));
        var claims = await db.Set<IdentityRoleClaim<Guid>>().Where(x => x.RoleId == id && x.ClaimType == SecurityClaimTypes.Permission).ToListAsync(ct);
        db.RemoveRange(claims);
        db.AddRange(codes.Select(x => new IdentityRoleClaim<Guid> { RoleId = id, ClaimType = SecurityClaimTypes.Permission, ClaimValue = x }));
        var users = await db.Set<IdentityUserRole<Guid>>().Where(x => x.RoleId == id).Select(x => x.UserId).ToListAsync(ct);
        await BumpAccessVersions(users, ct);
        await Audit(actorId, null, "ROLE_DEFINITION_UPDATED",
            new { role.Id, role.Name, Permissions = SummarizeAuditValues(codes, "permission codes") }, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await RoleResponse(role, ct);
    }

    public async Task ReplaceRolePermissions(Guid id, IReadOnlyList<string> permissionCodes, Guid actorId, CancellationToken ct)
    {
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (role.IsSystemRole) throw Conflict("SYSTEM_ROLE_IMMUTABLE", "دسترسی نقش سیستمی پویا و غیرقابل ویرایش است.");
        var codes = permissionCodes.Select(x => x.Trim()).Distinct().ToList();
        if (codes.Any(x => !PermissionCatalog.Codes.Contains(x))) throw Validation("permissionCodes", "کد دسترسی ناشناخته است.");
        var claims = await db.Set<IdentityRoleClaim<Guid>>().Where(x => x.RoleId == id && x.ClaimType == SecurityClaimTypes.Permission).ToListAsync(ct);
        db.RemoveRange(claims);
        db.AddRange(codes.Select(x => new IdentityRoleClaim<Guid> { RoleId = id, ClaimType = SecurityClaimTypes.Permission, ClaimValue = x }));
        var users = await db.Set<IdentityUserRole<Guid>>().Where(x => x.RoleId == id).Select(x => x.UserId).ToListAsync(ct);
        await BumpAccessVersions(users, ct);
        await Audit(actorId, null, "ROLE_PERMISSIONS_REPLACED",
            new { RoleId = id, Permissions = SummarizeAuditValues(codes, "permission codes") }, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OrganizationalUnitResponse>> OrganizationalUnits(bool includeInactive, string? search, CancellationToken ct)
    {
        var q = db.OrganizationalUnits.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(x => x.NameFa.Contains(s) || (x.NameEn != null && x.NameEn.Contains(s))); }
        return await q.OrderBy(x => x.NameFa).Select(x => Unit(x)).ToListAsync(ct);
    }

    public async Task<PagedResponse<OrganizationalUnitWorklistResponse>> OrganizationalUnitWorklist(bool includeInactive,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.OrganizationalUnits.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.NameFa.Contains(term) || (x.NameEn != null && x.NameEn.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.IsActive).ThenBy(x => x.NameFa)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(unit => new OrganizationalUnitWorklistResponse(unit.Id, unit.NameFa, unit.NameEn, unit.IsActive, unit.Version,
                db.Users.Count(user => user.OrganizationalUnitId == unit.Id),
                db.Users.Count(user => user.OrganizationalUnitId == unit.Id && user.IsActive))).ToListAsync(ct);
        return new(rows, total, page, pageSize);
    }

    public async Task<OrganizationalUnitResponse> CreateOrganizationalUnit(CreateOrganizationalUnitRequest request, Guid actorId, CancellationToken ct)
    {
        RequiredText(request.NameFa, "nameFa");
        var unit = new OrganizationalUnit(request.NameFa, request.NameEn); db.OrganizationalUnits.Add(unit);
        await Audit(actorId, null, "ORGANIZATIONAL_UNIT_CREATED", new { unit.Id, unit.NameFa }, ct); await db.SaveChangesAsync(ct);
        return Unit(unit);
    }

    public async Task<OrganizationalUnitResponse> UpdateOrganizationalUnit(Guid id, UpdateOrganizationalUnitRequest request, Guid actorId, CancellationToken ct)
    {
        var unit = await db.OrganizationalUnits.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (unit.Version != request.ExpectedVersion) throw Conflict("STALE_VERSION", "نسخه رکورد تغییر کرده است.");
        RequiredText(request.NameFa, "nameFa"); unit.Update(request.NameFa, request.NameEn);
        await Audit(actorId, null, "ORGANIZATIONAL_UNIT_UPDATED", new { unit.Id, unit.NameFa }, ct); await db.SaveChangesAsync(ct); return Unit(unit);
    }

    public async Task<OrganizationalUnitResponse> ChangeOrganizationalUnitState(Guid id, bool active,
        ChangeOrganizationalUnitStateRequest request, Guid actorId, CancellationToken ct)
    {
        var unit = await db.OrganizationalUnits.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (unit.Version != request.ExpectedVersion) throw Conflict("STALE_VERSION", "نسخه رکورد تغییر کرده است.");
        if (!active && await db.Users.AnyAsync(x => x.OrganizationalUnitId == id && x.IsActive, ct))
            throw Conflict("ORGANIZATIONAL_UNIT_HAS_ACTIVE_USERS", "واحد دارای کاربر فعال است.");
        unit.SetActive(active); await Audit(actorId, null, active ? "ORGANIZATIONAL_UNIT_ACTIVATED" : "ORGANIZATIONAL_UNIT_DEACTIVATED", new { unit.Id }, ct);
        await db.SaveChangesAsync(ct); return Unit(unit);
    }

    public async Task<IReadOnlyList<SessionResponse>> Sessions(Guid id, Guid? currentSessionId, CancellationToken ct) =>
        await db.UserSessions.AsNoTracking().Where(x => x.UserId == id).OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new SessionResponse(x.Id, x.CreatedAtUtc, x.LastSeenAtUtc, x.RevokedAtUtc, x.UserAgent, x.Id == currentSessionId)).ToListAsync(ct);

    public async Task RevokeSession(Guid userId, Guid sessionId, Guid actorId, CancellationToken ct)
    {
        var session = await db.UserSessions.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == sessionId, ct) ?? throw NotFound();
        session.Revoke(); await Audit(actorId, userId, "SESSION_REVOKED", new { SessionId = sessionId }, ct); await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResponse<SecurityAuditResponse>> SecurityAudit(Guid? targetUserId, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.SecurityAuditEvents.AsNoTracking().AsQueryable(); if (targetUserId.HasValue) q = q.Where(x => x.TargetUserId == targetUserId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.Action.Contains(term) || x.Source.Contains(term) || (x.Detail != null && x.Detail.Contains(term)));
        }
        var total = await q.CountAsync(ct); var rows = await q.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new SecurityAuditResponse(x.Id, x.ActorUserId, x.TargetUserId, x.Action, x.Detail, x.CorrelationId, x.Source, x.CreatedAtUtc)).ToListAsync(ct);
        return new(rows, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> EffectivePermissions(Guid userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin) return PermissionCatalog.Codes.Order().ToList();
        var roleIds = await db.Set<IdentityUserRole<Guid>>().AsNoTracking().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync(ct);
        var values = (await db.Set<IdentityRoleClaim<Guid>>().AsNoTracking().Where(x => roleIds.Contains(x.RoleId) && x.ClaimType == SecurityClaimTypes.Permission)
            .Select(x => x.ClaimValue!).ToListAsync(ct)).Where(PermissionCatalog.Codes.Contains).ToHashSet();
        var overrides = await db.UserPermissionOverrides.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var x in overrides.Where(x => x.Effect == PermissionEffects.Grant)) values.Add(x.PermissionCode);
        foreach (var x in overrides.Where(x => x.Effect == PermissionEffects.Deny)) values.Remove(x.PermissionCode);
        return values.Order().ToList();
    }

    public async Task<IReadOnlyList<EffectiveAccessResponse>> EffectiveAccess(Guid userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin)
            return PermissionCatalog.All.Select(permission => new EffectiveAccessResponse(permission.Code, permission.Area,
                permission.NameFa, permission.NameEn, true, "SYSTEM_ROLE", [SystemRoleNames.SystemAdministrator])).ToList();

        var roleSources = await (from userRole in db.Set<IdentityUserRole<Guid>>().AsNoTracking()
                                 join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                 join claim in db.Set<IdentityRoleClaim<Guid>>().AsNoTracking() on role.Id equals claim.RoleId
                                 where userRole.UserId == userId && claim.ClaimType == SecurityClaimTypes.Permission && claim.ClaimValue != null
                                 select new { Code = claim.ClaimValue!, RoleName = role.Name! }).ToListAsync(ct);
        var rolesByCode = roleSources.Where(x => PermissionCatalog.Codes.Contains(x.Code)).GroupBy(x => x.Code)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(x => x.RoleName).Distinct().Order().ToList());
        var overrides = await db.UserPermissionOverrides.AsNoTracking().Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.PermissionCode, x => x.Effect, ct);
        return PermissionCatalog.All.Select(permission =>
        {
            if (overrides.GetValueOrDefault(permission.Code) == PermissionEffects.Deny)
                return new EffectiveAccessResponse(permission.Code, permission.Area, permission.NameFa, permission.NameEn,
                    false, "DIRECT_DENY", ["DIRECT_DENY"]);
            if (overrides.GetValueOrDefault(permission.Code) == PermissionEffects.Grant)
                return new EffectiveAccessResponse(permission.Code, permission.Area, permission.NameFa, permission.NameEn,
                    true, "DIRECT_GRANT", ["DIRECT_GRANT"]);
            if (rolesByCode.TryGetValue(permission.Code, out var sources))
                return new EffectiveAccessResponse(permission.Code, permission.Area, permission.NameFa, permission.NameEn,
                    true, "ROLE", sources);
            return new EffectiveAccessResponse(permission.Code, permission.Area, permission.NameFa, permission.NameEn,
                false, "NONE", []);
        }).ToList();
    }

    private ProfilePhotoWriteLease CreatePhotoWriteLease(ApplicationUser user, ProfilePhotoContent photo)
    {
        var extension = photo.ContentType switch { "image/jpeg" => "jpg", "image/png" => "png", _ => "webp" };
        var key = $"users/{user.Id:N}/{Guid.NewGuid():N}.{extension}";
        return new ProfilePhotoWriteLease(photoStore, key, logger);
    }

    private static async Task SavePhoto(ApplicationUser user, ProfilePhotoContent photo,
        ProfilePhotoWriteLease lease, CancellationToken ct)
    {
        await lease.Upload(photo, ct);
        user.ProfilePhotoObjectKey = lease.ObjectKey; user.ProfilePhotoContentType = photo.ContentType;
        user.ProfilePhotoSize = photo.Bytes.LongLength; user.ProfilePhotoSha256 = photo.Sha256;
    }

    private async Task DeletePhotoBestEffort(string objectKey)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await photoStore.Delete(objectKey, cleanupTimeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deletion failed for superseded profile photo {ObjectKey}.", objectKey);
        }
    }

    private static ApplicationUser NewUser(string userName, string firstName, string lastName, string email,
        string cellphone, Guid unitId, bool active) => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName.Trim(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.Trim(),
            PhoneNumber = cellphone.Trim(),
            OrganizationalUnitId = unitId,
            IsActive = active,
            MustChangePassword = true,
            AccessVersion = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private async Task<RoleResponse> RoleResponse(ApplicationRole role, CancellationToken ct)
    {
        var knownPermissionCodes = PermissionCatalog.Codes.ToArray();
        IReadOnlyList<string> codes = role.IsSystemRole ? PermissionCatalog.Codes.Order().ToList() :
            await db.Set<IdentityRoleClaim<Guid>>().AsNoTracking()
                .Where(x => x.RoleId == role.Id && x.ClaimType == SecurityClaimTypes.Permission &&
                    knownPermissionCodes.Contains(x.ClaimValue!))
                .Select(x => x.ClaimValue!).Distinct().OrderBy(x => x).ToListAsync(ct);
        return new(role.Id, role.Name!, role.DescriptionFa, role.DescriptionEn, role.IsSystemRole, codes);
    }

    private async Task<HashSet<Guid>> SystemAdministratorIds(CancellationToken ct)
    {
        var roleId = await db.Roles.Where(x => x.Name == SystemRoleNames.SystemAdministrator).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return roleId is null ? [] : (await db.Set<IdentityUserRole<Guid>>().Where(x => x.RoleId == roleId).Select(x => x.UserId).ToListAsync(ct)).ToHashSet();
    }
    private async Task<bool> IsSystemAdministrator(Guid id, CancellationToken ct) => (await SystemAdministratorIds(ct)).Contains(id);
    private async Task EnsureSystemAdministratorAssignmentAuthorized(
        IReadOnlyCollection<ApplicationRole> roles, Guid actorId, CancellationToken ct)
    {
        if (roles.Any(x => x.Name == SystemRoleNames.SystemAdministrator) &&
            !await IsSystemAdministrator(actorId, ct))
            throw new IdentityAuthorizationException("SYSTEM_ADMINISTRATOR_ASSIGNMENT_REQUIRES_SYSTEM_ADMINISTRATOR",
                "فقط مدیر سامانه می‌تواند نقش مدیر سامانه را تخصیص دهد.");
    }
    private async Task EnsurePermissionAssignmentAuthorized(Guid actorId, bool hasAssignments,
        CancellationToken ct)
    {
        if (!hasAssignments || await IsSystemAdministrator(actorId, ct)) return;
        var permissions = await EffectivePermissions(actorId, false, ct);
        if (!permissions.Contains(Permissions.IdentityPermissionAssign, StringComparer.Ordinal))
            throw new IdentityAuthorizationException("PERMISSION_ASSIGN_REQUIRED",
                "برای تخصیص نقش یا دسترسی، مجوز مدیریت تخصیص دسترسی لازم است.");
    }
    private async Task<int> ActiveSystemAdministratorCount(CancellationToken ct)
    {
        var ids = await SystemAdministratorIds(ct); return await db.Users.CountAsync(x => x.IsActive && ids.Contains(x.Id), ct);
    }
    private void RevokeSessions(Guid userId) { foreach (var s in db.UserSessions.Where(x => x.UserId == userId && x.RevokedAtUtc == null)) s.Revoke(); }
    private async Task BumpAccessVersions(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var users = await db.Users.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        foreach (var user in users) { user.AccessVersion++; user.UpdatedAtUtc = DateTime.UtcNow; RevokeSessions(user.Id); }
    }
    private Task LockSystemAdministratorInvariant(CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1212236882, 1330528580)", ct);
    private async Task Audit(Guid? actor, Guid? target, string action, object? detail, CancellationToken ct) =>
        await db.SecurityAuditEvents.AddAsync(new(actor, target, action,
            SerializeAuditDetail(action, detail), null, "UI"), ct);

    private static AuditCollectionSummary SummarizeAuditValues(IEnumerable<string> values, string subject)
    {
        var canonicalValues = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(
            $"{canonicalValues.Length} {subject}",
            canonicalValues.Length,
            Sha256(string.Join('\n', canonicalValues)),
            canonicalValues.Take(10).ToArray(),
            canonicalValues.Length > 10);
    }

    private static string? SerializeAuditDetail(string action, object? detail)
    {
        if (detail is null) return null;
        const int maxLength = 4000;
        var serialized = JsonSerializer.Serialize(detail);
        if (serialized.Length <= maxLength) return serialized;

        // Audit storage is deliberately bounded. The transactional source tables retain the full
        // assignment; this fallback keeps a deterministic integrity fingerprint instead of writing
        // truncated, invalid JSON or failing the business transaction.
        var summary = JsonSerializer.Serialize(new
        {
            Summary = "Audit detail exceeded the bounded storage limit; source records contain the full values.",
            Action = action,
            OriginalCharacterCount = serialized.Length,
            Sha256 = Sha256(serialized)
        });
        return summary;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record AuditCollectionSummary(string Summary, int Count, string Sha256,
        IReadOnlyList<string> Sample, bool IsSampleTruncated);
    private static UserSummaryResponse Summary(ApplicationUser u, bool admin, IReadOnlyList<string>? roleNames = null,
        DateTime? lastSeenAtUtc = null) => new(u.Id, u.UserName!, u.FirstName, u.LastName, u.FullName, u.Email!,
        u.PhoneNumber!, Unit(u.OrganizationalUnit), $"/api/auth/users/{u.Id}/photo", u.IsActive,
        u.MustChangePassword, admin, roleNames ?? [], lastSeenAtUtc);
    private static OrganizationalUnitResponse Unit(OrganizationalUnit u) => new(u.Id, u.NameFa, u.NameEn, u.IsActive, u.Version);
    private static string Required(IFormCollection form, string key) => RequiredText(form[key].ToString(), key);
    private static string? Optional(IFormCollection form, string key) => Normalize(form[key].ToString());
    private static string RequiredText(string? value, string key) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw Validation(key, "این فیلد الزامی است.");
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IdentityValidationException Validation(string key, string message) => new(new() { [key] = [message] });
    private static IdentityConflictException Conflict(string code, string message) => new(code, message);
    private static KeyNotFoundException NotFound() => new("رکورد یافت نشد.");
    private static void EnsureIdentity(IdentityResult result)
    {
        if (result.Succeeded) return;
        throw new IdentityValidationException(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray()));
    }
    private static bool CryptographicEquals(string? left, string right)
    {
        if (left is null) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(left); var b = System.Text.Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}
