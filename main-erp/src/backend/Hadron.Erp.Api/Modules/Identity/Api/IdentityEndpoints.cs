using System.Security.Claims;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Identity.Application;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Hadron.Erp.Api.Platform.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Identity.Api;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").WithTags("Authentication");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();
        auth.MapGet("/bootstrap-status", async (HadronDbContext db, CancellationToken ct) =>
            Results.Ok(new { initialized = await db.Users.AnyAsync(ct) })).AllowAnonymous();

        auth.MapPost("/bootstrap", async (HttpContext context, IdentityService service, IConfiguration configuration, CancellationToken ct) =>
            await Run(async () =>
            {
                var form = await context.Request.ReadFormAsync(ct);
                var user = await service.Bootstrap(form, context.Request.Headers["X-Hadron-Bootstrap-Token"],
                    configuration["IDENTITY_BOOTSTRAP_TOKEN"], ct);
                return Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.UserName });
            })).DisableAntiforgery().AllowAnonymous().RequireRateLimiting("authentication-bootstrap");

        auth.MapPost("/login", async (LoginRequest request, HttpContext context,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, HadronDbContext db,
            IdentityService service, CancellationToken ct) => await Run(async () =>
        {
            var user = await users.Users.Include(x => x.OrganizationalUnit)
                .SingleOrDefaultAsync(x => x.NormalizedUserName == users.NormalizeName(request.UserName), ct);
            if (user is null || !user.IsActive)
                return Results.Json(new { title = "نام کاربری یا رمز عبور نادرست است.", status = 401, code = "INVALID_CREDENTIALS" }, statusCode: 401);
            var passwordCheck = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (passwordCheck.IsLockedOut)
                return Results.Json(new { title = "حساب موقتاً قفل شده است.", status = 423, code = "ACCOUNT_LOCKED" }, statusCode: 423);
            if (!passwordCheck.Succeeded)
                return Results.Json(new { title = "نام کاربری یا رمز عبور نادرست است.", status = 401, code = "INVALID_CREDENTIALS" }, statusCode: 401);
            var session = new UserSession(user.Id, context.Request.Headers.UserAgent.ToString());
            db.UserSessions.Add(session); await db.SaveChangesAsync(ct);
            await signIn.SignInWithClaimsAsync(user, false, [new(SecurityClaimTypes.SessionId, session.Id.ToString())]);
            var isAdmin = await users.IsInRoleAsync(user, SystemRoleNames.SystemAdministrator);
            var permissions = await service.EffectivePermissions(user.Id, isAdmin, ct);
            return Results.Ok(Current(user, isAdmin, permissions, await service.DataScopes(user.Id, ct)));
        })).AllowAnonymous().RequireRateLimiting("authentication-login");

        auth.MapPost("/logout", async (HttpContext context, SignInManager<ApplicationUser> signIn,
            HadronDbContext db, CancellationToken ct) =>
        {
            if (Guid.TryParse(context.User.FindFirstValue(SecurityClaimTypes.SessionId), out var sessionId))
            {
                var session = await db.UserSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
                session?.Revoke(); await db.SaveChangesAsync(ct);
            }
            await signIn.SignOutAsync(); return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (HttpContext context, HadronDbContext db, UserManager<ApplicationUser> users,
            IdentityService service, CancellationToken ct) =>
        {
            var id = UserId(context.User);
            var user = await db.Users.AsNoTracking().Include(x => x.OrganizationalUnit).SingleAsync(x => x.Id == id, ct);
            var isAdmin = await users.IsInRoleAsync(user, SystemRoleNames.SystemAdministrator);
            return Results.Ok(Current(user, isAdmin, await service.EffectivePermissions(id, isAdmin, ct),
                await service.DataScopes(id, ct)));
        }).RequirePermission(Permissions.IdentityProfileViewOwn);

        auth.MapPost("/change-password", async (ChangeOwnPasswordRequest request, HttpContext context,
            SignInManager<ApplicationUser> signIn, IdentityService service, CancellationToken ct) => await Run(async () =>
        {
            var currentSession = context.User.FindFirstValue(SecurityClaimTypes.SessionId);
            await service.ChangeOwnPassword(UserId(context.User), request.CurrentPassword, request.NewPassword, currentSession, ct);
            await signIn.SignOutAsync();
            return Results.Ok(new { code = "PASSWORD_CHANGED", reauthenticationRequired = true });
        })).RequirePermission(Permissions.IdentityPasswordChangeOwn);

        auth.MapGet("/users/{id:guid}/photo", async (Guid id, HttpContext context, HadronDbContext db,
            IProfilePhotoStore store, CancellationToken ct) =>
        {
            var caller = UserId(context.User);
            if (caller != id && !context.User.HasClaim(SecurityClaimTypes.Permission, Permissions.IdentityUserView)) return Results.Forbid();
            var photo = await db.Users.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.ProfilePhotoObjectKey, x.ProfilePhotoContentType, x.ProfilePhotoSha256 }).SingleOrDefaultAsync(ct);
            if (photo?.ProfilePhotoObjectKey is null) return Results.NotFound();
            var stream = await store.Get(photo.ProfilePhotoObjectKey, ct);
            context.Response.Headers.CacheControl = "private, max-age=300";
            context.Response.Headers.ETag = $"\"{photo.ProfilePhotoSha256}\"";
            return Results.Stream(stream, photo.ProfilePhotoContentType);
        }).RequirePermission(Permissions.IdentityProfileViewOwn);

        var usersApi = endpoints.MapGroup("/api/admin/users").WithTags("Identity Users").RequireAuthorization();
        usersApi.MapGet("/", (string? search, bool? includeInactive, bool? isActive, bool? mustChangePassword,
            Guid? organizationalUnitId, Guid? roleId, string? sort, bool? descending, int? page, int? pageSize,
            IdentityService service, CancellationToken ct) => service.Users(search, includeInactive ?? true, isActive,
            mustChangePassword, organizationalUnitId, roleId, sort, descending ?? false, page ?? 1, pageSize ?? 25, ct))
            .RequirePermission(Permissions.IdentityUserView);
        usersApi.MapGet("/{id:guid}", async (Guid id, IdentityService service, CancellationToken ct) =>
            await service.User(id, ct) is { } value ? Results.Ok(value) : Results.NotFound()).RequirePermission(Permissions.IdentityUserView);
        usersApi.MapPost("/", async (HttpContext context, IdentityService service, CancellationToken ct) => await Run(async () =>
        {
            var form = await context.Request.ReadFormAsync(ct); var user = await service.CreateUser(form, UserId(context.User), ct);
            return Results.Created($"/api/admin/users/{user.Id}", await service.User(user.Id, ct));
        })).RequirePermission(Permissions.IdentityUserManage);
        usersApi.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            await NoContent(() => service.UpdateUser(id, request, UserId(context.User), ct))).RequirePermission(Permissions.IdentityUserManage);
        usersApi.MapPost("/{id:guid}/photo", async (Guid id, HttpContext context, IdentityService service, CancellationToken ct) => await Run(async () =>
        {
            var form = await context.Request.ReadFormAsync(ct); var photo = form.Files.GetFile("photo") ?? throw new ProfilePhotoValidationException("PHOTO_REQUIRED", "عکس الزامی است.");
            await service.ReplacePhoto(id, photo, UserId(context.User), ct); return Results.NoContent();
        })).RequirePermission(Permissions.IdentityUserManage);
        usersApi.MapPost("/{id:guid}/activate", (Guid id, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ChangeState(id, true, UserId(context.User), ct))).RequirePermission(Permissions.IdentityUserManage);
        usersApi.MapPost("/{id:guid}/deactivate", (Guid id, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ChangeState(id, false, UserId(context.User), ct))).RequirePermission(Permissions.IdentityUserManage);
        usersApi.MapPost("/{id:guid}/reset-password", (Guid id, ResetPasswordRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ResetPassword(id, request.TemporaryPassword, UserId(context.User), ct))).RequirePermission(Permissions.IdentityPasswordReset);
        usersApi.MapPut("/{id:guid}/roles", (Guid id, ReplaceUserRolesRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ReplaceRoles(id, request.RoleIds, UserId(context.User), ct))).RequirePermission(Permissions.IdentityPermissionAssign);
        usersApi.MapPut("/{id:guid}/permission-overrides", (Guid id, ReplaceUserPermissionOverridesRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ReplaceOverrides(id, request.Overrides, UserId(context.User), ct))).RequirePermission(Permissions.IdentityPermissionAssign);
        usersApi.MapPut("/{id:guid}/data-scopes", (Guid id, ReplaceUserDataScopesRequest request,
            HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ReplaceDataScopes(id, request.Scopes, UserId(context.User), ct)))
            .RequirePermission(Permissions.IdentityPermissionAssign);
        usersApi.MapGet("/{id:guid}/sessions", (Guid id, HttpContext context, IdentityService service, CancellationToken ct) =>
            service.Sessions(id, Guid.TryParse(context.User.FindFirstValue(SecurityClaimTypes.SessionId), out var sid) ? sid : null, ct)).RequirePermission(Permissions.IdentityUserView);
        usersApi.MapPost("/{id:guid}/sessions/{sessionId:guid}/revoke", (Guid id, Guid sessionId, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.RevokeSession(id, sessionId, UserId(context.User), ct))).RequirePermission(Permissions.IdentitySessionRevoke);

        var roles = endpoints.MapGroup("/api/admin/roles").WithTags("Identity Roles").RequireAuthorization();
        roles.MapGet("/", (IdentityService service, CancellationToken ct) => service.Roles(ct)).RequirePermission(Permissions.IdentityRoleView);
        roles.MapGet("/worklist", (string? search, int? page, int? pageSize, IdentityService service, CancellationToken ct) =>
            service.RoleWorklist(search, page ?? 1, pageSize ?? 25, ct)).RequirePermission(Permissions.IdentityRoleView);
        roles.MapPost("/", async (CreateRoleRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            await Run(async () => Results.Created("/api/admin/roles",
                await service.CreateRole(request, UserId(context.User), ct))))
            .RequirePermission(Permissions.IdentityRoleManage);
        roles.MapPut("/{id:guid}", async (Guid id, UpdateRoleRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateRole(id, request, UserId(context.User), ct))).RequirePermission(Permissions.IdentityRoleManage);
        roles.MapPut("/{id:guid}/definition", async (Guid id, UpdateRoleDefinitionRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateRoleDefinition(id, request, UserId(context.User), ct)))
            .RequirePermission(Permissions.IdentityRoleManage).RequirePermission(Permissions.IdentityPermissionAssign);
        roles.MapPut("/{id:guid}/permissions", (Guid id, ReplaceRolePermissionsRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            NoContent(() => service.ReplaceRolePermissions(id, request.PermissionCodes, UserId(context.User), ct))).RequirePermission(Permissions.IdentityPermissionAssign);

        endpoints.MapGet("/api/admin/permissions", () => PermissionCatalog.All).RequirePermission(Permissions.IdentityRoleView);
        endpoints.MapGet("/api/admin/security-audit", (Guid? targetUserId, string? search, int? page, int? pageSize, IdentityService service, CancellationToken ct) =>
            service.SecurityAudit(targetUserId, search, page ?? 1, pageSize ?? 25, ct)).RequirePermission(Permissions.IdentitySecurityAuditView);

        var units = endpoints.MapGroup("/api/organization/units").WithTags("Organizational Units").RequireAuthorization();
        units.MapGet("/", (bool? includeInactive, string? search, IdentityService service, CancellationToken ct) =>
            service.OrganizationalUnits(includeInactive ?? false, search, ct)).RequirePermission(Permissions.OrganizationUnitView);
        units.MapGet("/worklist", (bool? includeInactive, string? search, int? page, int? pageSize, IdentityService service, CancellationToken ct) =>
            service.OrganizationalUnitWorklist(includeInactive ?? true, search, page ?? 1, pageSize ?? 25, ct))
            .RequirePermission(Permissions.OrganizationUnitView);
        units.MapPost("/", async (CreateOrganizationalUnitRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            Results.Created("/api/organization/units", await service.CreateOrganizationalUnit(request, UserId(context.User), ct))).RequirePermission(Permissions.OrganizationUnitManage);
        units.MapPut("/{id:guid}", (Guid id, UpdateOrganizationalUnitRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            Run(async () => Results.Ok(await service.UpdateOrganizationalUnit(id, request, UserId(context.User), ct)))).RequirePermission(Permissions.OrganizationUnitManage);
        units.MapPost("/{id:guid}/activate", (Guid id, ChangeOrganizationalUnitStateRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            Run(async () => Results.Ok(await service.ChangeOrganizationalUnitState(id, true, request, UserId(context.User), ct)))).RequirePermission(Permissions.OrganizationUnitManage);
        units.MapPost("/{id:guid}/deactivate", (Guid id, ChangeOrganizationalUnitStateRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
            Run(async () => Results.Ok(await service.ChangeOrganizationalUnitState(id, false, request, UserId(context.User), ct)))).RequirePermission(Permissions.OrganizationUnitManage);
    }

    private static CurrentUserResponse Current(ApplicationUser u, bool admin,
        IReadOnlyList<string> permissions, IReadOnlyList<DataScopeResponse> scopes) =>
        new(u.Id, u.UserName!, u.FullName, u.Email!, u.PhoneNumber!, new(u.OrganizationalUnit.Id, u.OrganizationalUnit.NameFa,
            u.OrganizationalUnit.NameEn, u.OrganizationalUnit.IsActive, u.OrganizationalUnit.Version), $"/api/auth/users/{u.Id}/photo", u.MustChangePassword, admin,
            scopes, permissions);
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<IResult> NoContent(Func<Task> action) => await Run(async () => { await action(); return Results.NoContent(); });
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (IdentityValidationException ex) { return IdentityValidationProblem(ex); }
        catch (IdentityAuthorizationException ex) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: ex.Message, extensions: new Dictionary<string, object?> { ["code"] = ex.Code }); }
        catch (IdentityConflictException ex) { return Results.Problem(statusCode: 409, title: ex.Message, extensions: new Dictionary<string, object?> { ["code"] = ex.Code }); }
        catch (ProfilePhotoValidationException ex) { return Results.Problem(statusCode: 400, title: ex.Message, extensions: new Dictionary<string, object?> { ["code"] = ex.Code }); }
        catch (UnauthorizedAccessException ex) { return Results.Problem(statusCode: 401, title: "راه‌اندازی مجاز نیست.", extensions: new Dictionary<string, object?> { ["code"] = ex.Message }); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 409, title: "نسخه رکورد تغییر کرده است.", extensions: new Dictionary<string, object?> { ["code"] = "STALE_VERSION" }); }
    }

    private static IResult IdentityValidationProblem(IdentityValidationException exception)
    {
        var firstError = exception.Errors
            .SelectMany(entry => entry.Value.Select(message => $"{IdentityFieldLabel(entry.Key)}: {message}"))
            .FirstOrDefault() ?? "اطلاعات کاربر ناقص یا نامعتبر است.";

        return Results.ValidationProblem(
            exception.Errors,
            statusCode: StatusCodes.Status400BadRequest,
            title: firstError,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "IDENTITY_VALIDATION_FAILED"
            });
    }

    private static string IdentityFieldLabel(string field) => field switch
    {
        "userName" => "نام کاربری",
        "temporaryPassword" => "رمز موقت",
        "firstName" => "نام",
        "lastName" => "نام خانوادگی",
        "emailAddress" => "ایمیل سازمانی",
        "cellphoneNumber" => "شماره موبایل",
        "organizationalUnitId" => "واحد سازمانی",
        "roleIds" => "نقش‌ها",
        _ => field
    };
}
