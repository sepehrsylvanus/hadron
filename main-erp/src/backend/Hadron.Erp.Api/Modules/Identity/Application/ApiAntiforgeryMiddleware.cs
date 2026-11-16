using Microsoft.AspNetCore.Antiforgery;

namespace Hadron.Erp.Api.Modules.Identity.Application;

public sealed class ApiAntiforgeryMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var unsafeMethod = !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method));
        if (!environment.IsEnvironment("Testing") && !environment.IsEnvironment("IdentityTesting") &&
            unsafeMethod && context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Path.StartsWithSegments("/api/auth/bootstrap"))
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { title = "درخواست امنیتی معتبر نیست.", status = 400, code = "CSRF_VALIDATION_FAILED" });
                return;
            }
        }
        await next(context);
    }
}
