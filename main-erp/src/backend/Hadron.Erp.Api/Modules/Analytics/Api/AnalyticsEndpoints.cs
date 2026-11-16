using Hadron.Erp.Api.Modules.Analytics.Application;
using Hadron.Erp.Api.Modules.Sourcing.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Analytics.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/v1/analytics").WithTags("Analytics v1");
        root.MapGet("/lead-time", (DateOnly? evaluationDate, DateTime? dataCutoffUtc, string? search,
            int? page, int? pageSize, AnalyticsService service, CancellationToken ct) => Execute(() =>
                service.LeadTime(RequiredDate(evaluationDate), dataCutoffUtc, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.AnalyticsLeadTimeView);
        root.MapPost("/lead-time/{sourcingOptionId:guid}/apply", (Guid sourcingOptionId,
            ApplyLeadTimeAnalyticsRequest request, AnalyticsService service, CancellationToken ct) => Execute(() =>
                service.ApplyLeadTime(sourcingOptionId, request, ct)))
            .RequirePermission(Permissions.SourcingLeadTimeApply);
        root.MapGet("/lead-time/{sourcingOptionId:guid}/applications", (Guid sourcingOptionId,
            AnalyticsService service, CancellationToken ct) => Execute(() => service.LeadTimeHistory(sourcingOptionId, ct)))
            .RequirePermission(Permissions.AnalyticsLeadTimeView);
        root.MapGet("/forecast-accuracy", (DateTime? dataCutoffUtc, string? search, int? page, int? pageSize,
            AnalyticsService service, CancellationToken ct) => Execute(() => service.ForecastAccuracy(dataCutoffUtc,
                search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.AnalyticsForecastAccuracyView);
        root.MapGet("/inventory/positions", (DateOnly? asOfDate, DateTime? dataCutoffUtc, string? search,
            int? page, int? pageSize,
            AnalyticsService service, CancellationToken ct) => Execute(() => service.InventoryPositions(
                RequiredDate(asOfDate), dataCutoffUtc, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.AnalyticsView);
        root.MapGet("/inventory/movements", (DateOnly? fromDate, DateOnly? toDate, DateTime? dataCutoffUtc,
            string? search, int? page, int? pageSize, AnalyticsService service, CancellationToken ct) => Execute(() =>
                service.InventoryMovements(RequiredDate(fromDate), RequiredDate(toDate), dataCutoffUtc, search,
                    page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.AnalyticsView);
        root.MapGet("/exports/{report}/{format}", async (string report, string format, DateOnly? evaluationDate,
            DateTime? dataCutoffUtc, string? language, int? maximumRows, string? search, Guid? runId,
            Guid? generationId, DateOnly? fromDate, DateOnly? toDate,
            AnalyticsExportService service, CancellationToken ct) =>
        {
            try
            {
                var file = await service.Export(new(report, format, RequiredDate(evaluationDate), dataCutoffUtc,
                    language ?? "fa-IR", maximumRows ?? AnalyticsService.MaximumExportRows,
                    search, runId, generationId, fromDate, toDate), ct);
                return Results.File(file.Content, file.ContentType, file.FileName);
            }
            catch (Exception ex) { return Problem(ex); }
        }).RequirePermission(Permissions.AnalyticsExport);
        return endpoints;
    }

    private static DateOnly RequiredDate(DateOnly? value) => value ?? throw new AnalyticsValidationException(
        new Dictionary<string, string[]> { ["evaluationDate"] = ["Explicit evaluation date is required."] });
    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static IResult Problem(Exception ex) => ex switch
    {
        AnalyticsValidationException validation => Results.ValidationProblem(validation.Errors),
        AnalyticsConflictException conflict => Results.Problem(statusCode: 409, title: "Analytics conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        SourcingValidationException validation => Results.ValidationProblem(validation.Errors),
        SourcingConflictException conflict => Results.Problem(statusCode: 409, title: "Sourcing conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
