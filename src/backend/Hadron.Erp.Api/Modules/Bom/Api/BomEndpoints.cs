using Hadron.Erp.Api.Modules.Bom.Application;

namespace Hadron.Erp.Api.Modules.Bom.Api;

public static class BomEndpoints
{
    public static IEndpointRouteBuilder MapBomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/bom-revisions").WithTags("BOM");
        root.MapGet("/", (string? search, Guid? parentItemId, string? itemType, string? status,
            DateOnly? effectiveDate, int? page, int? pageSize, BomService service, CancellationToken token) =>
            Execute(() => service.List(search, parentItemId, itemType, status, effectiveDate, page ?? 1,
                pageSize ?? 25, token)));
        root.MapGet("/{id:guid}", async (Guid id, BomService service, CancellationToken token) =>
        {
            try
            {
                var result = await service.Get(id, token);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception ex) { return Problem(ex); }
        });
        root.MapPost("/", (CreateBomRevisionRequest request, BomService service, CancellationToken token) =>
            ExecuteCreated(() => service.Create(request, token)));
        root.MapPost("/{id:guid}/copy", (Guid id, CopyBomRevisionRequest request, BomService service,
            CancellationToken token) => ExecuteCreated(() => service.Copy(id, request, token)));
        root.MapPut("/{id:guid}", (Guid id, UpdateBomRevisionRequest request, BomService service,
            CancellationToken token) => Execute(() => service.Update(id, request, token)));
        root.MapPost("/{id:guid}/lines", (Guid id, AddBomLineRequest request, BomService service,
            CancellationToken token) => Execute(() => service.AddLine(id, request, token)));
        root.MapPut("/{id:guid}/lines/{lineId:guid}", (Guid id, Guid lineId, UpdateBomLineRequest request,
            BomService service, CancellationToken token) => Execute(() => service.UpdateLine(id, lineId, request, token)));
        root.MapPost("/{id:guid}/lines/{lineId:guid}/remove", (Guid id, Guid lineId, BomMutationRequest request,
            BomService service, CancellationToken token) => Execute(() => service.RemoveLine(id, lineId, request, token)));
        root.MapGet("/{id:guid}/validation", (Guid id, BomService service, CancellationToken token) =>
            Execute(() => service.Validate(id, token)));
        root.MapPost("/{id:guid}/approve", (Guid id, ApproveBomRevisionRequest request, BomService service,
            CancellationToken token) => Execute(() => service.Approve(id, request, token)));
        root.MapPost("/{id:guid}/obsolete", (Guid id, ObsoleteBomRevisionRequest request, BomService service,
            CancellationToken token) => Execute(() => service.Obsolete(id, request, token)));
        root.MapGet("/resolve", async (Guid parentItemId, DateOnly businessDate, bool? historical,
            BomService service, CancellationToken token) =>
        {
            try
            {
                var result = await service.Resolve(parentItemId, businessDate, historical ?? false, token);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception ex) { return Problem(ex); }
        });
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> ExecuteCreated(Func<Task<BomRevisionResponse>> action)
    {
        try { var result = await action(); return Results.Created($"/api/bom-revisions/{result.Id}", result); }
        catch (Exception ex) { return Problem(ex); }
    }
    private static IResult Problem(Exception ex) => ex switch
    {
        BomValidationException validation => Results.ValidationProblem(validation.Errors),
        BomConflictException conflict => Results.Problem(statusCode: 409, title: "BOM conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?>
            { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
