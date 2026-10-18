using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Mrp.Api;

public static class MrpEndpoints
{
    public static IEndpointRouteBuilder MapMrpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/mrp-runs").WithTags("MRP Runs");
        group.MapGet("/", (int? page, int? pageSize, MrpService service, CancellationToken ct) =>
            Execute(() => service.List(page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MrpRunView);
        group.MapPost("/", (CreateMrpRunRequest request, MrpService service, CancellationToken ct) =>
            ExecuteCreated(() => service.Execute(request, ct), response => $"/api/mrp-runs/{response.Id}"))
            .RequirePermission(Permissions.MrpRunExecute);
        group.MapGet("/default-parameters", (DateOnly? asOfDate, MrpService service) =>
            Results.Ok(service.DefaultParameters(asOfDate)))
            .RequirePermission(Permissions.MrpRunView);
        group.MapGet("/route-candidates", (string? search, int? page, int? pageSize,
            MrpService service, CancellationToken ct) => Execute(() => service.RouteCandidates(
                search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MrpRunView);
        group.MapGet("/{id:guid}", async (Guid id, MrpService service, CancellationToken ct) =>
        {
            try { var result = await service.Get(id, ct); return result is null ? Results.NotFound() : Results.Ok(result); }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MrpRunView);
        group.MapGet("/{id:guid}/items", (Guid id, string? search, int? page, int? pageSize,
            MrpService service, CancellationToken ct) =>
            Execute(() => service.ListItems(id, search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.MrpRunView);
        group.MapGet("/{id:guid}/items/{itemId:guid}/timeline", async (Guid id, Guid itemId,
            MrpService service, CancellationToken ct) =>
        {
            try { var result = await service.Timeline(id, itemId, ct); return result is null ? Results.NotFound() : Results.Ok(result); }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MrpRunView);
        group.MapGet("/{id:guid}/items/{itemId:guid}/trace", async (Guid id, Guid itemId,
            DateOnly demandDate, MrpService service, CancellationToken ct) =>
        {
            try { var result = await service.Trace(id, itemId, demandDate, ct); return result is null ? Results.NotFound() : Results.Ok(result); }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.MrpRunView);
        group.MapGet("/{id:guid}/exceptions", (Guid id, string? severity, int? page, int? pageSize,
            MrpService service, CancellationToken ct) =>
            Execute(() => service.Exceptions(id, severity, page ?? 1, pageSize ?? 50, ct)))
            .RequirePermission(Permissions.PlanningExceptionView);
        group.MapGet("/{id:guid}/exception-summary", (Guid id, string? severity, int? page, int? pageSize,
            MrpService service, CancellationToken ct) =>
            Execute(() => service.ExceptionSummary(id, severity, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.PlanningExceptionView);
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, Func<T, string> location)
    {
        try { var response = await action(); return Results.Created(location(response), response); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        MrpValidationException validation => Results.ValidationProblem(validation.Errors),
        MrpConflictException conflict => Results.Problem(statusCode: 409, title: "MRP conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
