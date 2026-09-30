using System.Security.Claims;
using System.Text.Encodings.Web;
using Hadron.Erp.Api.Platform.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hadron.Erp.Api.Modules.Identity.Application;

public sealed class TestingAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Guid.TryParse(Request.Headers["X-Hadron-Test-User-Id"].ToString(), out var requestedUserId)
            ? requestedUserId
            : Guid.Parse("00000000-0000-0000-0000-000000000001");
        var fullName = Request.Headers["X-Hadron-Test-Full-Name"].ToString();
        if (string.IsNullOrWhiteSpace(fullName)) fullName = "کاربر آزمون Hadron";
        var claims = new List<Claim>
        {
            new(SecurityClaimTypes.TestAuthentication, "true"),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(SecurityClaimTypes.FullName, fullName)
        };
        claims.AddRange(Request.Headers["X-Hadron-Test-Roles"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim(ClaimTypes.Role, role)));
        var requested = Request.Headers["X-Hadron-Test-Permissions"].ToString();
        var denied = Request.Headers["X-Hadron-Test-Denied-Permissions"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> permissions = string.IsNullOrWhiteSpace(requested)
            ? PermissionCatalog.Codes.AsEnumerable()
            : requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(PermissionCatalog.Codes.Contains)
                .ToArray();
        claims.AddRange(permissions.Where(x => !denied.Contains(x))
            .Select(x => new Claim(SecurityClaimTypes.Permission, x)));
        claims.AddRange(denied.Select(x => new Claim(SecurityClaimTypes.PermissionDeny, x)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
