using Microsoft.AspNetCore.Authorization;

namespace Hadron.Erp.Api.Platform.Security;

/// <summary>
/// Fails closed when a business API endpoint has not declared its immutable permission contract.
/// ASP.NET Core authorization evaluates the policy added by
/// <see cref="PermissionEndpointExtensions.RequirePermission{TBuilder}"/>.
/// </summary>
public sealed class ApiPermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        // Let ASP.NET Core preserve the canonical 404/405 contract for paths or
        // HTTP methods that do not resolve to an endpoint.  The fail-closed rule
        // applies to a real mapped business endpoint, not to an absent route.
        if (endpoint is null ||
            string.Equals(endpoint.DisplayName, "405 HTTP Method Not Supported", StringComparison.Ordinal) ||
            endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>() is not null ||
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            context.Request.Path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            title = "API permission metadata is missing",
            status = StatusCodes.Status403Forbidden,
            code = "UNREGISTERED_API_PERMISSION"
        });
    }
}
