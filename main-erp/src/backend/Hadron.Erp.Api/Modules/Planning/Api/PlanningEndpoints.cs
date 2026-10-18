using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Planning.Api;

public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/planning").WithTags("Planning");
        root.MapGet("/business-date", (PlanningService service) => Results.Ok(service.GetBusinessDate()))
            .RequirePermission(Permissions.ForecastView);
        root.MapPost("/resolve-jalali-date", (ResolveJalaliDateRequest request, PlanningService service) =>
        {
            try { return Results.Ok(service.ResolveJalaliDate(request)); }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MrpRunExecute);

        root.MapGet("/periods", (int? page, int? pageSize, PlanningService service, CancellationToken ct) =>
            Execute(() => service.ListPeriods(page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ForecastView);
        root.MapPost("/periods", (CreateBusinessPeriodRequest request, PlanningService service, CancellationToken ct) =>
            ExecuteCreated(() => service.CreatePeriod(request, ct), response => $"/api/planning/periods/{response.Id}"))
            .RequirePermission(Permissions.ForecastManage);
        root.MapPost("/periods/{id:guid}/close", (Guid id, ChangeBusinessPeriodStateRequest request,
            PlanningService service, CancellationToken ct) => Execute(() => service.ChangePeriodState(id, true, request, ct)))
            .RequirePermission(Permissions.PeriodClose);
        root.MapPost("/periods/{id:guid}/reopen", (Guid id, ChangeBusinessPeriodStateRequest request,
            PlanningService service, CancellationToken ct) => Execute(() => service.ChangePeriodState(id, false, request, ct)))
            .RequirePermission(Permissions.PeriodReopen);

        root.MapGet("/forecast", (Guid? periodId, DateOnly? evaluationDate, string? search,
            bool? includeInactive, Guid? productCategoryId, Guid? businessUnitId, int? page, int? pageSize,
            PlanningService service, CancellationToken ct) => Execute(() => service.GetPlanningRows(
                Require(periodId, "periodId", "Business Period الزامی است."),
                Require(evaluationDate, "evaluationDate", "تاریخ ارزیابی باید صریح ارسال شود."), search,
                includeInactive ?? false, productCategoryId, businessUnitId, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ForecastView);
        root.MapPost("/forecast", (CreateForecastRequest request, PlanningService service, CancellationToken ct) =>
            ExecuteCreated(() => service.CreateForecast(request, ct), response => $"/api/planning/forecast/{response.SeriesId}"))
            .RequirePermission(Permissions.ForecastManage);
        root.MapPost("/forecast/{seriesId:guid}/revisions", (Guid seriesId,
            CreateForecastRevisionRequest request, PlanningService service, CancellationToken ct) =>
            Execute(() => service.AddForecastRevision(seriesId, request, ct)))
            .RequirePermission(Permissions.ForecastManage);
        root.MapGet("/forecast-entry-grid", (string? periodCode, string? search,
            bool? includeInactive, Guid? productCategoryId, Guid? businessUnitId, int? page, int? pageSize,
            PlanningService service, CancellationToken ct) => Execute(() => service.GetForecastEntryGrid(
                periodCode ?? string.Empty, search, includeInactive ?? false, productCategoryId,
                businessUnitId, page ?? 1, pageSize ?? 50, ct)))
            .RequirePermission(Permissions.ForecastView);
        root.MapPost("/forecast-entry-grid", (SaveForecastBatchRequest request,
            PlanningService service, CancellationToken ct) => Execute(() => service.SaveForecastBatch(request, ct)))
            .RequirePermission(Permissions.ForecastManage);
        root.MapGet("/forecast-products", (string? search, int? page, int? pageSize,
            PlanningService service, CancellationToken ct) => Execute(() => service.ListActiveForecastProducts(
                search, page ?? 1, pageSize ?? 20, ct)))
            .RequirePermission(Permissions.ForecastView);
        root.MapPost("/forecast-plans/preview", (ForecastPlanPreviewRequest request,
            PlanningService service, CancellationToken ct) => Execute(() => service.PreviewForecastPlan(request, ct)))
            .RequirePermission(Permissions.ForecastView);
        root.MapPost("/forecast-plans", (SaveForecastPlanRequest request,
            PlanningService service, CancellationToken ct) => ExecuteCreated(() => service.SaveForecastPlan(request, ct),
                response => $"/api/planning/forecast-plans/{response.PlanId}"))
            .RequirePermission(Permissions.ForecastManage);
        root.MapGet("/forecast-plans", (string? search, int? page, int? pageSize,
            PlanningService service, CancellationToken ct) => Execute(() => service.ListForecastPlans(
                search, page ?? 1, pageSize ?? 25, ct))).RequirePermission(Permissions.ForecastView);
        root.MapGet("/forecast-plans/{planId:guid}", async (Guid planId, int? page, int? pageSize,
            PlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetForecastPlan(planId, page ?? 1, pageSize ?? 25, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.ForecastView);
        root.MapPost("/forecast-plans/{planId:guid}/entries/{entryId:guid}/corrections", (Guid planId,
            Guid entryId, CorrectForecastPlanEntryRequest request, PlanningService service,
            CancellationToken ct) => Execute(() => service.CorrectForecastPlanEntry(planId, entryId,
                request, ct))).RequirePermission(Permissions.ForecastManage);
        root.MapGet("/forecast/{seriesId:guid}/revisions", async (Guid seriesId, PlanningService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetForecastHistory(seriesId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.ForecastView);

        root.MapGet("/actuals", (Guid? periodId, Guid? productId, DateOnly? from, DateOnly? to,
            string? search, int? page, int? pageSize, PlanningService service, CancellationToken ct) =>
            Execute(() => service.ListActuals(periodId, productId, from, to, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ActualView);
        root.MapPost("/actuals", (CreateDailyActualRequest request, PlanningService service, CancellationToken ct) =>
            ExecuteCreated(() => service.CreateActual(request, ct), response => $"/api/planning/actuals/{response.Id}"))
            .RequirePermission(Permissions.ActualManage);
        root.MapPost("/actuals/{id:guid}/corrections", (Guid id, CorrectDailyActualRequest request,
            PlanningService service, CancellationToken ct) => Execute(() => service.CorrectActual(id, request, ct)))
            .RequirePermission(Permissions.ActualManage);
        root.MapGet("/actuals/{id:guid}/revisions", async (Guid id, PlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetActualHistory(id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.ActualView);
        return endpoints;
    }

    private static T Require<T>(T? value, string key, string message) where T : struct =>
        value ?? throw PlanningValidationException.Single(key, message);

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, Func<T, string> location)
    {
        try
        {
            var response = await action();
            return Results.Created(location(response), response);
        }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        PlanningValidationException validation => Results.ValidationProblem(validation.Errors),
        PlanningConflictException conflict => Results.Problem(statusCode: 409, title: "Planning conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?>
            { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
