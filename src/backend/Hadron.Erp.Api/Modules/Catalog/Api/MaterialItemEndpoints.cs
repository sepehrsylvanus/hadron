using Hadron.Erp.Api.Modules.Catalog.Application;
using Hadron.Erp.Api.Modules.Catalog.Domain;

namespace Hadron.Erp.Api.Modules.Catalog.Api;

public static class MaterialItemEndpoints
{
    public static IEndpointRouteBuilder MapMaterialItemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Map(endpoints, "/api/parts", "Parts", ItemTypes.Part);
        Map(endpoints, "/api/components", "Components", ItemTypes.Component);
        return endpoints;
    }

    private static void Map(IEndpointRouteBuilder endpoints, string path, string tag, string itemType)
    {
        var root = endpoints.MapGroup(path).WithTags(tag);
        root.MapGet("/", (string? search, bool? includeInactive, Guid? categoryId, string? supplyMethod,
            int? page, int? pageSize, MaterialItemService service, CancellationToken token) =>
            Execute(() => service.GetList(itemType, search, includeInactive ?? false, categoryId, supplyMethod,
                page ?? 1, pageSize ?? 25, token)));
        root.MapGet("/{id:guid}", async (Guid id, MaterialItemService service, CancellationToken token) =>
        {
            var result = await service.Get(itemType, id, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        root.MapPost("/", (CreateMaterialItemRequest request, MaterialItemService service, CancellationToken token) =>
            ExecuteCreated(path, () => service.Create(itemType, request, token)));
        root.MapPut("/{id:guid}", (Guid id, UpdateMaterialItemRequest request, MaterialItemService service,
            CancellationToken token) => Execute(() => service.Update(itemType, id, request, token)));
        root.MapPost("/{id:guid}/deactivate", (Guid id, ChangeMaterialItemStateRequest request,
            MaterialItemService service, CancellationToken token) =>
            Execute(() => service.ChangeState(itemType, id, false, request, token)));
        root.MapPost("/{id:guid}/reactivate", (Guid id, ChangeMaterialItemStateRequest request,
            MaterialItemService service, CancellationToken token) =>
            Execute(() => service.ChangeState(itemType, id, true, request, token)));
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> ExecuteCreated(string path, Func<Task<MaterialItemMutationResponse>> action)
    {
        try
        {
            var result = await action();
            return Results.Created($"{path}/{result.Item.Id}", result);
        }
        catch (Exception ex) { return Problem(ex); }
    }
    private static IResult Problem(Exception ex) => ex switch
    {
        MaterialItemValidationException validation => Results.ValidationProblem(validation.Errors),
        MaterialItemConflictException conflict => Results.Problem(statusCode: 409, title: "Material Item conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?>
            { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
