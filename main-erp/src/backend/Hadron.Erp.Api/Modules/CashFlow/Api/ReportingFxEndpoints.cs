using System.Globalization;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.CashFlow.Api;

public static class ReportingFxEndpoints
{
    public static IEndpointRouteBuilder MapReportingFxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/v1/cash-flow").WithTags("Cash Flow Reporting Rates v1");

        root.MapGet("/reporting-rate-currencies",
                (ReportingFxRateService service, CancellationToken ct) =>
                    Execute(() => service.GetCurrencies(ct)))
            .RequirePermission(Permissions.CashFlowReportingRateView);

        root.MapGet("/reporting-rate-sets",
                (int? page, int? pageSize, string? status,
                    ReportingFxRateService service, CancellationToken ct) =>
                    Execute(() => service.GetRateSets(page ?? 1, pageSize ?? 25, status, ct)))
            .RequirePermission(Permissions.CashFlowReportingRateView);

        root.MapGet("/reporting-rate-sets/current",
                (DateOnly? asOfDate, ReportingFxRateService service, CancellationToken ct) =>
                    ExecuteFound(() => service.GetCurrentPublished(
                        RequiredDate(asOfDate, "asOfDate"), ct)))
            .RequirePermission(Permissions.CashFlowReportingRateView);

        root.MapGet("/reporting-rate-sets/resolve-jalali-date", ResolveJalaliDate)
            .RequirePermission(Permissions.CashFlowReportingRateView);

        root.MapGet("/reporting-rate-sets/{id:guid}",
                (Guid id, ReportingFxRateService service, CancellationToken ct) =>
                    ExecuteFound(() => service.GetRateSet(id, ct)))
            .RequirePermission(Permissions.CashFlowReportingRateView);

        root.MapPost("/reporting-rate-sets",
                (CreateReportingFxRateSetRequest request,
                    ReportingFxRateService service, CancellationToken ct) =>
                    ExecuteCreated(() => service.Create(request, ct), "/api/v1/cash-flow/reporting-rate-sets"))
            .RequirePermission(Permissions.CashFlowReportingRateManage);

        root.MapPut("/reporting-rate-sets/{id:guid}",
                (Guid id, UpdateReportingFxRateSetRequest request,
                    ReportingFxRateService service, CancellationToken ct) =>
                    Execute(() => service.UpdateDraft(id, request, ct)))
            .RequirePermission(Permissions.CashFlowReportingRateManage);

        root.MapPost("/reporting-rate-sets/{id:guid}/publish",
                (Guid id, PublishReportingFxRateSetRequest request,
                    ReportingFxRateService service, CancellationToken ct) =>
                    Execute(() => service.Publish(id, request, ct)))
            .RequirePermission(Permissions.CashFlowReportingRateManage);

        root.MapPost("/reporting-rate-sets/{id:guid}/corrections",
                (Guid id, CreateReportingFxCorrectionRequest request,
                    ReportingFxRateService service, CancellationToken ct) =>
                    ExecuteCreated(() => service.CreateCorrection(id, request, ct),
                        "/api/v1/cash-flow/reporting-rate-sets"))
            .RequirePermission(Permissions.CashFlowReportingRateManage);

        return endpoints;
    }

    private static IResult ResolveJalaliDate(string? jalaliDate, IPlanningCalendar calendar)
    {
        try
        {
            var normalized = NormalizeDigits(jalaliDate ?? string.Empty).Trim();
            if (normalized.Length != 8 || normalized.Any(x => !char.IsAsciiDigit(x)) ||
                !int.TryParse(normalized.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var year) ||
                !int.TryParse(normalized.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var month) ||
                !int.TryParse(normalized.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var day))
            {
                throw new ReportingFxValidationException(new Dictionary<string, string[]>
                {
                    ["jalaliDate"] = ["تاریخ باید با قالب YYYYMMDD وارد شود؛ مانند 14050616."]
                });
            }

            return Results.Ok(new ReportingFxJalaliDateResponse(
                normalized, calendar.GetGregorianDate(year, month, day)));
        }
        catch (ReportingFxValidationException validation)
        {
            return Results.ValidationProblem(validation.Errors);
        }
        catch (PlanningValidationException validation)
        {
            return Results.ValidationProblem(validation.Errors);
        }
    }

    private static string NormalizeDigits(string value)
    {
        Span<char> normalized = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            normalized[index] = value[index] switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + value[index] - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + value[index] - '\u0660'),
                _ => value[index]
            };
        }

        return new string(normalized);
    }

    private static DateOnly RequiredDate(DateOnly? value, string field) => value ??
        throw new ReportingFxValidationException(new Dictionary<string, string[]>
        {
            [field] = ["تاریخ صریح الزامی است."]
        });

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        IResult? problem = null;
        try
        {
            return Results.Ok(await action());
        }
        catch (Exception exception) when (TryCreatePublicProblem(exception, out problem))
        {
            return problem!;
        }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, string basePath)
    {
        IResult? problem = null;
        try
        {
            var result = await action();
            var id = result?.GetType().GetProperty("Id")?.GetValue(result);
            return Results.Created($"{basePath}/{id}", result);
        }
        catch (Exception exception) when (TryCreatePublicProblem(exception, out problem))
        {
            return problem!;
        }
    }

    private static async Task<IResult> ExecuteFound<T>(Func<Task<T?>> action) where T : class
    {
        IResult? problem = null;
        try
        {
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception exception) when (TryCreatePublicProblem(exception, out problem))
        {
            return problem!;
        }
    }

    // The exception filter deliberately catches only domain failures that are safe for clients.
    // Unknown exceptions bypass this catch entirely, so ASP.NET handles them with the original
    // stack trace instead of resetting it via `throw exception`.
    private static bool TryCreatePublicProblem(Exception exception, out IResult? problem)
    {
        switch (exception)
        {
            case ReportingFxValidationException validation:
                problem = Results.ValidationProblem(validation.Errors);
                return true;
            case ReportingFxConflictException conflict:
            {
                var extensions = new Dictionary<string, object?> { ["code"] = conflict.Code };
                if (conflict.Details is not null) extensions["details"] = conflict.Details;
                problem = Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Cash-flow reporting-rate conflict",
                    detail: conflict.Message,
                    extensions: extensions);
                return true;
            }
            case KeyNotFoundException:
                problem = Results.NotFound();
                return true;
            default:
                problem = null;
                return false;
        }
    }
}
