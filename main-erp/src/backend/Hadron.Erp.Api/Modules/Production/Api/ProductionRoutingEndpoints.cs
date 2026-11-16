using Hadron.Erp.Api.Modules.Production.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Production.Api;

public static class ProductionRoutingEndpoints
{
    public static IEndpointRouteBuilder MapProductionRoutingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var centers = endpoints.MapGroup("/api/production-work-centers")
            .WithTags("Production Work Centers").RequireAuthorization();
        centers.MapGet("/", (string? search, bool? includeInactive, int? page, int? pageSize,
            ProductionRoutingService service, CancellationToken ct) => Execute(() => service.ListWorkCenters(
                search, includeInactive ?? false, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ProductionWorkCenterView);
        centers.MapGet("/organizational-unit-candidates", (ProductionRoutingService service,
            CancellationToken ct) => Execute(() => service.ListActiveOrganizationalUnits(ct)))
            .RequirePermission(Permissions.ProductionWorkCenterView);
        centers.MapGet("/{id:guid}", async (Guid id, ProductionRoutingService service, CancellationToken ct) =>
        {
            try { var value = await service.GetWorkCenter(id, ct); return value is null ? Results.NotFound() : Results.Ok(value); }
            catch (Exception ex) { return Problem(ex); }
        }).RequirePermission(Permissions.ProductionWorkCenterView);
        centers.MapPost("/", (CreateProductionWorkCenterRequest request, ProductionRoutingService service,
            CancellationToken ct) => Created(() => service.CreateWorkCenter(request, ct),
                value => $"/api/production-work-centers/{value.Id}"))
            .RequirePermission(Permissions.ProductionWorkCenterManage);
        centers.MapPut("/{id:guid}", (Guid id, UpdateProductionWorkCenterRequest request,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.UpdateWorkCenter(id, request, ct)))
            .RequirePermission(Permissions.ProductionWorkCenterManage);
        centers.MapPost("/{id:guid}/deactivate", (Guid id, ChangeProductionWorkCenterStateRequest request,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.ChangeWorkCenterState(id, false, request, ct)))
            .RequirePermission(Permissions.ProductionWorkCenterManage);
        centers.MapPost("/{id:guid}/reactivate", (Guid id, ChangeProductionWorkCenterStateRequest request,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.ChangeWorkCenterState(id, true, request, ct)))
            .RequirePermission(Permissions.ProductionWorkCenterManage);

        var routes = endpoints.MapGroup("/api/manufacturing-routes")
            .WithTags("Manufacturing Routes").RequireAuthorization();
        routes.MapGet("/", (string? search, string? status, Guid? itemId, int? page, int? pageSize,
            ProductionRoutingService service, CancellationToken ct) => Execute(() => service.ListRoutes(
                search, status, itemId, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ProductionRoutingView);
        routes.MapGet("/item-candidates", (string? search, int? page, int? pageSize,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.ListRouteItemCandidates(search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ProductionRoutingView);
        routes.MapGet("/{id:guid}", async (Guid id, ProductionRoutingService service, CancellationToken ct) =>
        {
            try { var value = await service.GetRoute(id, ct); return value is null ? Results.NotFound() : Results.Ok(value); }
            catch (Exception ex) { return Problem(ex); }
        }).RequirePermission(Permissions.ProductionRoutingView);
        routes.MapGet("/{id:guid}/schedule-preview", (Guid id, DateOnly completionDate,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.Preview(id, completionDate, ct)))
            .RequirePermission(Permissions.ProductionRoutingView);
        routes.MapPost("/", (CreateManufacturingRouteRequest request, ProductionRoutingService service,
            CancellationToken ct) => Created(() => service.CreateRoute(request, ct),
                value => $"/api/manufacturing-routes/{value.Id}"))
            .RequirePermission(Permissions.ProductionRoutingManage);
        routes.MapPut("/{id:guid}", (Guid id, UpdateManufacturingRouteRequest request,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.UpdateRoute(id, request, ct)))
            .RequirePermission(Permissions.ProductionRoutingManage);
        routes.MapPost("/{id:guid}/approve", (Guid id, ManufacturingRouteLifecycleRequest request,
            ProductionRoutingService service, CancellationToken ct) => Execute(() =>
                service.ApproveRoute(id, request, ct)))
            .RequirePermission(Permissions.ProductionRoutingApprove);
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> Created<T>(Func<Task<T>> action, Func<T, string> location)
    { try { var value = await action(); return Results.Created(location(value), value); } catch (Exception ex) { return Problem(ex); } }
    private static IResult Problem(Exception exception) => exception switch
    {
        ProductionValidationException validation => Results.ValidationProblem(validation.Errors),
        ProductionConflictException conflict => Results.Problem(statusCode: 409,
            title: "Production routing conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
