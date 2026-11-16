using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Mrp.Api;

public static class MaterialPlanningEndpoints
{
    public static IEndpointRouteBuilder MapMaterialPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/material-planning").WithTags("Material Planning");
        group.MapGet("/refresh-status", (MaterialPlanningService service, CancellationToken ct) =>
            Execute(() => service.RefreshStatus(ct)))
            .RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs", (int? page, int? pageSize, MaterialPlanningService service, CancellationToken ct) =>
            Execute(() => service.Runs(page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}", async (Guid runId, MaterialPlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.Header(runId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/summary", (Guid runId, string? search, string? itemType,
            string? categoryId, string? supplyMethod, string? riskBand, string? completeness,
            bool? hasExceptions, string? stockoutFrom, string? stockoutTo,
            string? sortBy, string? sortDirection, int? page, int? pageSize,
            MaterialPlanningService service, CancellationToken ct) => Execute(() => service.Summary(runId,
                search, itemType, OptionalGuid(categoryId, nameof(categoryId)), supplyMethod, riskBand,
                completeness, hasExceptions, OptionalDate(stockoutFrom, nameof(stockoutFrom)),
                OptionalDate(stockoutTo, nameof(stockoutTo)), sortBy, sortDirection,
                page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/facets", (Guid runId, MaterialPlanningService service,
            CancellationToken ct) => Execute(() => service.Facets(runId, ct)))
            .RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/products", (Guid runId, string? search, int? page,
            int? pageSize, MaterialPlanningService service, CancellationToken ct) =>
            Execute(() => service.Products(runId, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/products/{productId:guid}/materials", (Guid runId,
            Guid productId, int? page, int? pageSize, MaterialPlanningService service,
            CancellationToken ct) => Execute(() => service.ProductMaterials(runId, productId,
                page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/items/{itemId:guid}/timeline", async (Guid runId, Guid itemId,
            string? grain, MaterialPlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.Timeline(runId, itemId, grain, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/items/{itemId:guid}/trace", async (Guid runId, Guid itemId,
            DateOnly demandDate, MaterialPlanningService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.Trace(runId, itemId, demandDate, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MaterialPlanningView);
        group.MapGet("/runs/{runId:guid}/exceptions", (Guid runId, string? severity, string? itemId,
            string? date, string? code, bool? affectsDemand, string? sourceModule, string? sortBy, string? sortDirection,
            int? page, int? pageSize, MaterialPlanningService service, CancellationToken ct) =>
            Execute(() => service.Exceptions(runId, severity, OptionalGuid(itemId, nameof(itemId)),
                OptionalDate(date, nameof(date)), code, affectsDemand, sourceModule, sortBy, sortDirection,
                page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.PlanningExceptionView);

        group.MapGet("/planner-inbox", (Guid? runId, string? status, string? assignedUserId,
            string? search, int? page, int? pageSize, PlanningCollaborationService service,
            CancellationToken ct) => Execute(() => service.WorkItems(runId, status,
                OptionalGuid(assignedUserId, nameof(assignedUserId)),
                search, page ?? 1, pageSize ?? 25, ct))).RequirePermission(Permissions.PlannerWorkItemView);
        group.MapGet("/planner-assignees", (string? search, int? page, int? pageSize,
            PlanningCollaborationService service, CancellationToken ct) => Execute(() =>
                service.Assignees(search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.PlannerWorkItemManage);
        group.MapPost("/planner-work-items", (CreatePlannerWorkItemRequest request,
            PlanningCollaborationService service, CancellationToken ct) => ExecuteCreated(() =>
                service.CreateWorkItem(request, ct))).RequirePermission(Permissions.PlannerWorkItemManage);
        group.MapPost("/planner-work-items/{id:guid}/assign", (Guid id, AssignPlannerWorkItemRequest request,
            PlanningCollaborationService service, CancellationToken ct) => Execute(() => service.Assign(id, request, ct)))
            .RequirePermission(Permissions.PlannerWorkItemManage);
        group.MapPost("/planner-work-items/{id:guid}/acknowledge", (Guid id,
            AcknowledgePlannerWorkItemRequest request, PlanningCollaborationService service,
            CancellationToken ct) => Execute(() => service.Acknowledge(id, request, ct)))
            .RequirePermission(Permissions.PlannerWorkItemAcknowledge);
        group.MapGet("/saved-views", (PlanningCollaborationService service, CancellationToken ct) =>
            Execute(() => service.SavedViews(ct))).RequirePermission(Permissions.MaterialPlanningView);
        group.MapPost("/saved-views", (CreatePlannerSavedViewRequest request,
            PlanningCollaborationService service, CancellationToken ct) => ExecuteCreated(() =>
                service.CreateSavedView(request, ct))).RequirePermission(Permissions.MaterialSavedViewManage);
        group.MapDelete("/saved-views/{id:guid}", (Guid id, PlanningCollaborationService service,
            CancellationToken ct) => ExecuteNoContent(() => service.DeleteSavedView(id, ct)))
            .RequirePermission(Permissions.MaterialSavedViewManage);

        group.MapGet("/what-if-scenarios", (int? page, int? pageSize,
            PlanningCollaborationService service, CancellationToken ct) => Execute(() =>
                service.Scenarios(page ?? 1, pageSize ?? 25, ct))).RequirePermission(Permissions.WhatIfView);
        group.MapPost("/what-if-scenarios", (CreateWhatIfScenarioRequest request,
            PlanningCollaborationService service, CancellationToken ct) => ExecuteCreated(() =>
                service.CreateScenario(request, ct))).RequirePermission(Permissions.WhatIfManage);
        group.MapGet("/what-if-scenarios/{id:guid}/results", (Guid id, string? search,
            int? page, int? pageSize, PlanningCollaborationService service, CancellationToken ct) =>
            Execute(() => service.ScenarioResults(id, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.WhatIfView);
        group.MapGet("/runs/{runId:guid}/what-if/items", (Guid runId, string? search,
            string? itemType, int? page, int? pageSize, PlanningCollaborationService service,
            CancellationToken ct) => Execute(() => service.Candidates(runId, search, itemType,
                page ?? 1, pageSize ?? 25, ct))).RequirePermission(Permissions.WhatIfView);
        group.MapGet("/runs/{runId:guid}/what-if/forecast-periods", (Guid runId, Guid productId,
            PlanningCollaborationService service, CancellationToken ct) => Execute(() =>
                service.ForecastPeriods(runId, productId, ct))).RequirePermission(Permissions.WhatIfView);
        group.MapGet("/runs/{runId:guid}/what-if/firm-supplies", (Guid runId, string? search,
            int? page, int? pageSize, PlanningCollaborationService service, CancellationToken ct) =>
            Execute(() => service.FirmSupplyCandidates(runId, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.WhatIfView);
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action)
    {
        try { return Results.Json(await action(), statusCode: StatusCodes.Status201Created); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteNoContent(Func<Task> action)
    {
        try { await action(); return Results.NoContent(); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        MrpValidationException validation => Results.ValidationProblem(validation.Errors),
        MrpConflictException conflict => Results.Conflict(new { code = conflict.Code, message = conflict.Message }),
        UnauthorizedAccessException => Results.Unauthorized(),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };

    private static Guid? OptionalGuid(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw MrpValidationException.Single(field, $"{field.ToUpperInvariant()}_INVALID");
    }

    private static DateOnly? OptionalDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParse(value, out var parsed)
            ? parsed
            : throw MrpValidationException.Single(field, $"{field.ToUpperInvariant()}_INVALID");
    }
}
