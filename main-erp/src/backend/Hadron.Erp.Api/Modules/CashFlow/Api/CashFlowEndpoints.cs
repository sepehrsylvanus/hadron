using System.Globalization;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.CashFlow.Api;

public static class CashFlowEndpoints
{
    public static IEndpointRouteBuilder MapCashFlowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/v1/cash-flow").WithTags("Cash Flow v1");

        root.MapGet("/dashboard", (DateOnly? asOfDate, DateOnly? horizonEnd, bool? includePlanned,
            CashFlowQueryService service, CancellationToken ct) => Execute(() => service.Dashboard(
                RequiredDate(asOfDate, "asOfDate"), RequiredDate(horizonEnd, "horizonEnd"),
                includePlanned ?? false, ct)))
            .RequirePermission(Permissions.CashFlowDashboardView);
        root.MapGet("/dashboard/resolve-jalali-date", ResolveJalaliDate)
            .RequirePermission(Permissions.CashFlowDashboardView);

        root.MapGet("/obligations", (DateOnly? asOfDate, DateOnly? fromDate, DateOnly? toDate,
            bool? includePlanned, string? search, Guid? supplierId, string? currencyCode,
            string? stage, string? state, int? page, int? pageSize,
            CashFlowQueryService service, CancellationToken ct) => Execute(() => service.Obligations(
                RequiredDate(asOfDate, "asOfDate"), fromDate, toDate, includePlanned ?? false,
                search, supplierId, currencyCode, stage, state, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.CashFlowObligationView);
        root.MapGet("/obligations/resolve-jalali-date", ResolveJalaliDate)
            .RequirePermission(Permissions.CashFlowObligationView);

        root.MapGet("/costed-boms", (DateOnly? asOfDate, string? search, int? page, int? pageSize,
            string? costBasisMode, Guid? priceBookRevisionId,
            CashFlowQueryService service, CancellationToken ct) => Execute(() => service.CostedBoms(
                RequiredDate(asOfDate, "asOfDate"), search, page ?? 1, pageSize ?? 25,
                costBasisMode, priceBookRevisionId, ct)))
            .RequirePermission(Permissions.CashFlowCostingView);
        root.MapGet("/costed-boms/resolve-jalali-date", ResolveJalaliDate)
            .RequirePermission(Permissions.CashFlowCostingView);

        root.MapGet("/costed-boms/{parentItemId:guid}", (Guid parentItemId, DateOnly? asOfDate,
            string? costBasisMode, Guid? priceBookRevisionId,
            CashFlowQueryService service, CancellationToken ct) => Execute(() => service.CostedBom(
                parentItemId, RequiredDate(asOfDate, "asOfDate"), costBasisMode,
                priceBookRevisionId, ct)))
            .RequirePermission(Permissions.CashFlowCostingView);

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
                throw new CashFlowValidationException(new Dictionary<string, string[]>
                {
                    ["jalaliDate"] = ["تاریخ باید با قالب YYYYMMDD وارد شود؛ مانند 14050608."]
                });
            }

            return Results.Ok(new CashFlowJalaliDateResponse(normalized,
                calendar.GetGregorianDate(year, month, day)));
        }
        catch (CashFlowValidationException validation)
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
        throw new CashFlowValidationException(new Dictionary<string, string[]>
        {
            [field] = ["تاریخ صریح الزامی است."]
        });

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (Exception ex)
        {
            return Problem(ex);
        }
    }

    private static IResult Problem(Exception ex) => ex switch
    {
        CashFlowValidationException validation => Results.ValidationProblem(validation.Errors),
        CashFlowConflictException conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Cash-flow conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
