using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.OrderPlanning.Api;

public static class OrderPlanningEndpoints
{
    public static IEndpointRouteBuilder MapOrderPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/order-planning").WithTags("Order Planning");
        group.MapPost("/generations", (GenerateOrderPlanningRequest request, OrderPlanningService service, CancellationToken ct) =>
            ExecuteCreated(() => service.Generate(request, ct)))
            .RequirePermission(Permissions.OrderPlanningGenerate);
        group.MapGet("/generations", (Guid runId, OrderPlanningService service, CancellationToken ct) =>
            Execute(() => service.Generations(runId, ct)))
            .RequirePermission(Permissions.OrderPlanningView);
        group.MapGet("/runs", (string? search, int? page, int? pageSize,
            OrderPlanningService service, CancellationToken ct) =>
            Execute(() => service.Runs(search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.OrderPlanningView);
        group.MapGet("/recommendations", (Guid? runId, Guid? generationId, string? search,
            string? state, string? priority, string? decisionType, DateOnly? needByFrom, DateOnly? needByTo,
            string? sortBy, string? sortDirection, int? page, int? pageSize,
            OrderPlanningService service, CancellationToken ct) => Execute(() =>
                service.List(runId, generationId, search, state, priority, decisionType, needByFrom, needByTo,
                    sortBy, sortDirection, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.OrderPlanningView);
        group.MapGet("/recommendations/{id:guid}", async (Guid id, OrderPlanningService service, CancellationToken ct) =>
        {
            try { var value = await service.Get(id, ct); return value is null ? Results.NotFound() : Results.Ok(value); }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.OrderPlanningView);
        group.MapPost("/recommendations/{id:guid}/decisions", (Guid id, SaveExpertDecisionRequest request,
            OrderPlanningService service, CancellationToken ct) => Execute(() => service.Decide(id, request, ct)))
            .RequirePermission(Permissions.OrderPlanningDecide);
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception exception) { return Problem(exception); } }
    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action)
    {
        try { var value = await action(); return Results.Created($"/api/order-planning/generations/{value}", value); }
        catch (Exception exception) { return Problem(exception); }
    }
    private static IResult Problem(Exception exception) => exception switch
    {
        OrderPlanningValidationException validation => Results.ValidationProblem(validation.Errors),
        OrderPlanningConflictException conflict => Results.Problem(statusCode: 409, title: "Order planning conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
