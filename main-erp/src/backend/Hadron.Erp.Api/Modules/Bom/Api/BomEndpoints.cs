using Hadron.Erp.Api.Modules.Bom.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Bom.Api;

public static class BomEndpoints
{
    public static IEndpointRouteBuilder MapBomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/bom-revisions").WithTags("BOM");
        root.MapGet("/", (string? search, Guid? parentItemId, string? itemType, string? status,
            int? page, int? pageSize, BomService service, CancellationToken token) =>
            Execute(() => service.List(search, parentItemId, itemType, status, page ?? 1,
                pageSize ?? 25, token)))
            .RequirePermission(Permissions.BomView);
        root.MapGet("/{id:guid}", async (Guid id, BomService service, CancellationToken token) =>
        {
            try
            {
                var result = await service.Get(id, token);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception ex) { return Problem(ex); }
        }).RequirePermission(Permissions.BomView);
        root.MapPost("/", (CreateBomRevisionRequest request, BomService service, CancellationToken token) =>
            ExecuteCreated(() => service.Create(request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPost("/{id:guid}/copy", (Guid id, CopyBomRevisionRequest request, BomService service,
            CancellationToken token) => ExecuteCreated(() => service.Copy(id, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPost("/{id:guid}/discard", (Guid id, BomMutationRequest request, BomService service,
            CancellationToken token) => Execute(() => service.DiscardDraft(id, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPut("/{id:guid}", (Guid id, UpdateBomRevisionRequest request, BomService service,
            CancellationToken token) => Execute(() => service.Update(id, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPost("/{id:guid}/lines", (Guid id, AddBomLineRequest request, BomService service,
            CancellationToken token) => Execute(() => service.AddLine(id, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPut("/{id:guid}/lines/{lineId:guid}", (Guid id, Guid lineId, UpdateBomLineRequest request,
            BomService service, CancellationToken token) => Execute(() => service.UpdateLine(id, lineId, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPost("/{id:guid}/lines/{lineId:guid}/remove", (Guid id, Guid lineId, BomMutationRequest request,
            BomService service, CancellationToken token) => Execute(() => service.RemoveLine(id, lineId, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapGet("/{id:guid}/validation", (Guid id, BomService service, CancellationToken token) =>
            Execute(() => service.Validate(id, token)))
            .RequirePermission(Permissions.BomView);
        root.MapPost("/{id:guid}/approve", (Guid id, ApproveBomRevisionRequest request, BomService service,
            CancellationToken token) => Execute(() => service.Approve(id, request, token)))
            .RequirePermission(Permissions.BomApprove);
        root.MapPost("/{id:guid}/obsolete", (Guid id, ObsoleteBomRevisionRequest request, BomService service,
            CancellationToken token) => Execute(() => service.Obsolete(id, request, token)))
            .RequirePermission(Permissions.BomObsolete);
        root.MapGet("/current", async (Guid parentItemId,
            BomService service, CancellationToken token) =>
        {
            try
            {
                var result = await service.Current(parentItemId, token);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception ex) { return Problem(ex); }
        }).RequirePermission(Permissions.BomView);
        root.MapGet("/product-tree", (Guid rootItemId, BomStructureService service, CancellationToken token) =>
            Execute(() => service.GetProductTree(rootItemId, token)))
            .RequirePermission(Permissions.BomView);
        root.MapGet("/builder-tree", (Guid rootItemId, Guid? rootRevisionId,
            BomStructureService service, CancellationToken token) =>
            Execute(() => service.GetBuilderTree(rootItemId, rootRevisionId, token)))
            .RequirePermission(Permissions.BomView);
        root.MapGet("/where-used", (Guid itemId, BomStructureService service, CancellationToken token) =>
            Execute(() => service.GetWhereUsed(itemId, token)))
            .RequirePermission(Permissions.BomView);
        root.MapGet("/applicable-items", (Guid parentItemId, bool? showAll, string? search, int? page,
            int? pageSize, BomApplicabilityService service, CancellationToken token) =>
            Execute(() => service.Candidates(parentItemId, showAll ?? false, search, page ?? 1,
                pageSize ?? 25, token)))
            .RequirePermission(Permissions.BomView);
        root.MapGet("/applicabilities", (Guid childItemId, bool? includeInactive, string? search,
            int? page, int? pageSize, BomApplicabilityService service, CancellationToken token) =>
            Execute(() => service.ListForChild(childItemId, includeInactive ?? false, search, page ?? 1,
                pageSize ?? 25, token)))
            .RequirePermission(Permissions.BomView);
        root.MapPost("/applicabilities", (CreateBomItemApplicabilityRequest request,
            BomApplicabilityService service, CancellationToken token) =>
            ExecuteCreatedApplicability(() => service.Create(request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPost("/applicabilities/{id:guid}/deactivate", (Guid id,
            ChangeBomItemApplicabilityStateRequest request, BomApplicabilityService service,
            CancellationToken token) => Execute(() => service.ChangeState(id, false, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        root.MapPost("/applicabilities/{id:guid}/reactivate", (Guid id,
            ChangeBomItemApplicabilityStateRequest request, BomApplicabilityService service,
            CancellationToken token) => Execute(() => service.ChangeState(id, true, request, token)))
            .RequirePermission(Permissions.BomDraftManage);
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> ExecuteCreated(Func<Task<BomRevisionResponse>> action)
    {
        try { var result = await action(); return Results.Created($"/api/bom-revisions/{result.Id}", result); }
        catch (Exception ex) { return Problem(ex); }
    }
    private static async Task<IResult> ExecuteCreatedApplicability(
        Func<Task<BomItemApplicabilityResponse>> action)
    {
        try
        {
            var result = await action();
            return Results.Created($"/api/bom-revisions/applicabilities/{result.Id}", result);
        }
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
