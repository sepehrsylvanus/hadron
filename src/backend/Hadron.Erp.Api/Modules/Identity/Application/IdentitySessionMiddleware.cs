using System.Security.Claims;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Identity.Domain;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Identity.Application;

public sealed class IdentitySessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HadronDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            context.User.HasClaim(SecurityClaimTypes.TestAuthentication, "true"))
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(context.User.FindFirstValue(SecurityClaimTypes.SessionId), out var sessionId) ||
            !long.TryParse(context.User.FindFirstValue(SecurityClaimTypes.AccessVersion), out var accessVersion))
        {
            await Reject(context, "INVALID_SESSION");
            return;
        }

        var state = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.IsActive, x.AccessVersion, x.MustChangePassword }).SingleOrDefaultAsync(context.RequestAborted);
        var session = await db.UserSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId,
            context.RequestAborted);
        if (state is null || !state.IsActive || state.AccessVersion != accessVersion || session is null || session.RevokedAtUtc is not null)
        {
            await Reject(context, "SESSION_REVOKED");
            return;
        }

        if (session.LastSeenAtUtc < DateTime.UtcNow.AddMinutes(-5))
        {
            session.Seen();
            await db.SaveChangesAsync(context.RequestAborted);
        }

        if (state.MustChangePassword && context.Request.Path.StartsWithSegments("/api") &&
            !IsPasswordChangePath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Password change required",
                status = 403,
                code = "PASSWORD_CHANGE_REQUIRED"
            }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsPasswordChangePath(PathString path) =>
        path.StartsWithSegments("/api/auth/me") || path.StartsWithSegments("/api/auth/csrf") ||
        path.StartsWithSegments("/api/auth/change-password") || path.StartsWithSegments("/api/auth/logout") ||
        path.StartsWithSegments("/api/auth/profile-photo");

    private static async Task Reject(HttpContext context, string code)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Authentication required", status = 401, code });
    }
}
