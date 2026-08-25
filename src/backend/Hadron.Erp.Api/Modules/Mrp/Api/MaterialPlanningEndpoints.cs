using Hadron.Erp.Api.Modules.Mrp.Application;

namespace Hadron.Erp.Api.Modules.Mrp.Api;

public static class MaterialPlanningEndpoints
{
    public static IEndpointRouteBuilder MapMaterialPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/material-planning").WithTags("Material Planning");
        group.MapGet("/runs", (int? page, int? pageSize, MaterialPlanningService service, CancellationToken ct) =>
            Execute(() => service.Runs(page ?? 1, pageSize ?? 25, ct)));
        group.MapGet("/runs/{runId:guid}", async (Guid runId, MaterialPlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.Header(runId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        });
        group.MapGet("/runs/{runId:guid}/summary", (Guid runId, string? search, string? itemType,
            Guid? categoryId, string? supplyMethod, string? riskBand, string? completeness,
            bool? hasExceptions, DateOnly? stockoutFrom, DateOnly? stockoutTo,
            string? sortBy, string? sortDirection, int? page, int? pageSize,
            MaterialPlanningService service, CancellationToken ct) => Execute(() => service.Summary(runId,
                search, itemType, categoryId, supplyMethod, riskBand, completeness, hasExceptions,
                stockoutFrom, stockoutTo, sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)));
        group.MapGet("/runs/{runId:guid}/facets", (Guid runId, MaterialPlanningService service,
            CancellationToken ct) => Execute(() => service.Facets(runId, ct)));
        group.MapGet("/runs/{runId:guid}/items/{itemId:guid}/timeline", async (Guid runId, Guid itemId,
            string? grain, MaterialPlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.Timeline(runId, itemId, grain, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        });
        group.MapGet("/runs/{runId:guid}/items/{itemId:guid}/trace", async (Guid runId, Guid itemId,
            DateOnly demandDate, MaterialPlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.Trace(runId, itemId, demandDate, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        });
        group.MapGet("/runs/{runId:guid}/exceptions", (Guid runId, string? severity, Guid? itemId,
            DateOnly? date, string? code, bool? affectsDemand, string? sourceModule, string? sortBy, string? sortDirection,
            int? page, int? pageSize, MaterialPlanningService service, CancellationToken ct) =>
            Execute(() => service.Exceptions(runId, severity, itemId, date, code, affectsDemand, sourceModule,
                sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)));
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        MrpValidationException validation => Results.ValidationProblem(validation.Errors),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
