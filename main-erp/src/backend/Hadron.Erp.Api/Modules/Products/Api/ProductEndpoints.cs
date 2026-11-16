using Hadron.Erp.Api.Modules.Products.Application;
using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Products.Api;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/products").WithTags("Products");
        root.MapGet("/", (string? search, bool? includeInactive, Guid? productCategoryId, Guid? businessUnitId,
            int? page, int? pageSize, ProductService service, CancellationToken token) =>
            Execute(() => service.GetList(search, includeInactive ?? false, productCategoryId, businessUnitId, page ?? 1, pageSize ?? 25, token)))
            .RequirePermission(Permissions.ProductView);
        root.MapGet("/{id:guid}", async (Guid id, ProductService service, CancellationToken token) =>
        {
            var result = await service.Get(id, token); return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission(Permissions.ProductView);
        root.MapPost("/", (CreateProductRequest request, ProductService service, CancellationToken token) =>
            ExecuteCreated(() => service.Create(request, token)))
            .RequirePermission(Permissions.ProductManage);
        root.MapPut("/{id:guid}", (Guid id, UpdateProductRequest request, ProductService service, CancellationToken token) =>
            Execute(() => service.Update(id, request, token)))
            .RequirePermission(Permissions.ProductManage);
        root.MapPost("/{id:guid}/deactivate", (Guid id, ChangeProductStateRequest request, ProductService service, CancellationToken token) =>
            Execute(() => service.ChangeState(id, false, request, token)))
            .RequirePermission(Permissions.ProductManage);
        root.MapPost("/{id:guid}/reactivate", (Guid id, ChangeProductStateRequest request, ProductService service, CancellationToken token) =>
            Execute(() => service.ChangeState(id, true, request, token)))
            .RequirePermission(Permissions.ProductManage);
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> ExecuteCreated(Func<Task<ProductMutationResponse>> action)
    { try { var result = await action(); return Results.Created($"/api/products/{result.Product.Id}", result); } catch (Exception ex) { return Problem(ex); } }
    private static IResult Problem(Exception ex) => ex switch
    {
        ProductValidationException validation => Results.ValidationProblem(validation.Errors),
        ItemBaseUnitChangeBlockedException blocked => Results.Problem(statusCode: 409, title: "Product conflict", detail: blocked.Message,
            extensions: new Dictionary<string, object?> { ["code"] = blocked.Code, ["details"] = blocked.Details }),
        ProductConflictException conflict => Results.Problem(statusCode: 409, title: "Product conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
