using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hadron.Erp.Api.Platform.Errors;

public sealed class HadronExceptionHandler(ILogger<HadronExceptionHandler> logger) : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.RequestAborted.IsCancellationRequested && exception is OperationCanceledException)
            return true;

        var (status, code) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "INVALID_REQUEST"),
            JsonException => (StatusCodes.Status400BadRequest, "INVALID_REQUEST"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "PERMISSION_DENIED"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR")
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled API exception. TraceId: {TraceId}", httpContext.TraceIdentifier);
        else
            logger.LogWarning(exception, "Rejected API request. Code: {Code}; TraceId: {TraceId}",
                code, httpContext.TraceIdentifier);

        var persian = httpContext.Request.Headers.AcceptLanguage.Any(value =>
            value?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true);
        var (title, detail) = Message(status, persian);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://tools.ietf.org/html/rfc9110#section-15.{(status == 400 ? "5.1" : status == 403 ? "5.4" : "6.1")}",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, JsonOptions, cancellationToken);
        return true;
    }

    private static (string Title, string Detail) Message(int status, bool persian) => (status, persian) switch
    {
        (StatusCodes.Status400BadRequest, true) =>
            ("درخواست نامعتبر است", "اطلاعات ارسال‌شده قابل پردازش نیست. لطفاً ورودی‌ها را بررسی و دوباره تلاش کنید."),
        (StatusCodes.Status403Forbidden, true) =>
            ("دسترسی مجاز نیست", "شما مجوز انجام این عملیات را ندارید."),
        (StatusCodes.Status500InternalServerError, true) =>
            ("خطای داخلی سامانه", "عملیات تکمیل نشد. لطفاً دوباره تلاش کنید و در صورت تکرار، شناسه رهگیری را به پشتیبانی اعلام کنید."),
        (StatusCodes.Status400BadRequest, false) =>
            ("Invalid request", "The submitted data could not be processed. Check the input and try again."),
        (StatusCodes.Status403Forbidden, false) =>
            ("Access denied", "You do not have permission to perform this operation."),
        _ => ("Internal server error",
            "The operation could not be completed. Try again and provide the trace ID to support if it persists.")
    };
}
