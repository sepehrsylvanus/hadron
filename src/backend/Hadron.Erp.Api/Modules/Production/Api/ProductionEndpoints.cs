using Hadron.Erp.Api.Modules.Production.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Production.Api;

public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/production-orders")
            .WithTags("Production")
            .RequireAuthorization();

        root.MapGet("/", (string? search, string? status, int? page, int? pageSize,
            ProductionService service, CancellationToken ct) => Execute(() =>
                service.List(search, status, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.ProductionOrderView);

        root.MapGet("/{id:guid}", async (Guid id, ProductionService service,
            CancellationToken ct) =>
        {
            try
            {
                var value = await service.Get(id, ct);
                return value is null ? Results.NotFound() : Results.Ok(value);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.ProductionOrderView);

        root.MapPost("/", (CreateProductionOrderRequest request, ProductionService service,
            CancellationToken ct) => ExecuteCreated(() => service.Create(request, ct),
                value => $"/api/production-orders/{value.Id}"))
            .RequirePermission(Permissions.ProductionOrderManage);

        root.MapPut("/{id:guid}", (Guid id, UpdateProductionOrderRequest request,
            ProductionService service, CancellationToken ct) => Execute(() =>
                service.Update(id, request, ct)))
            .RequirePermission(Permissions.ProductionOrderManage);

        root.MapPost("/{id:guid}/approve", (Guid id, ProductionLifecycleRequest request,
            ProductionService service, CancellationToken ct) => Execute(() =>
                service.Approve(id, request, ct)))
            .RequirePermission(Permissions.ProductionOrderApprove);

        root.MapPost("/{id:guid}/release", (Guid id, ProductionLifecycleRequest request,
            ProductionService service, CancellationToken ct) => Execute(() =>
                service.Release(id, request, ct)))
            .RequirePermission(Permissions.ProductionOrderRelease);

        root.MapPost("/{id:guid}/materials/{materialId:guid}/stage", (Guid id,
            Guid materialId, StageProductionMaterialRequest request,
            ProductionService service, CancellationToken ct) => Execute(() =>
                service.Stage(id, materialId, request, ct)))
            .RequirePermission(Permissions.ProductionMaterialStage);

        root.MapPost("/{id:guid}/reports", (Guid id, PostProductionReportRequest request,
            ProductionService service, CancellationToken ct) => ExecuteCreated(() =>
                service.PostReport(id, request, ct), value => $"/api/production-orders/{value.Id}"))
            .RequirePermission(Permissions.ProductionReportPost);

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action,
        Func<T, string> location)
    {
        try
        {
            var value = await action();
            return Results.Created(location(value), value);
        }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        ProductionValidationException validation => Results.ValidationProblem(validation.Errors),
        ProductionConflictException conflict => Results.Problem(statusCode: 409,
            title: "Production conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?>
            { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
