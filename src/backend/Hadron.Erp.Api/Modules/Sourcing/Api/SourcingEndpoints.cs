using Hadron.Erp.Api.Modules.Sourcing.Application;

namespace Hadron.Erp.Api.Modules.Sourcing.Api;

public static class SourcingEndpoints
{
    public static IEndpointRouteBuilder MapSourcingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/sourcing-options").WithTags("Sourcing");
        root.MapGet("/", (DateOnly? evaluationDate, string? search, bool? includeInactive, Guid? itemId,
            Guid? supplierId, string? itemType, bool? preferred, string? readiness, int? page, int? pageSize, SourcingService service,
            CancellationToken token) => Execute(() => service.List(RequireDate(evaluationDate), search,
                includeInactive ?? false, itemId, supplierId, itemType, preferred, readiness, page ?? 1, pageSize ?? 25, token)));
        root.MapGet("/{id:guid}", async (Guid id, DateOnly? evaluationDate, SourcingService service,
            CancellationToken token) =>
        {
            try
            {
                var result = await service.Get(id, RequireDate(evaluationDate), token);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception ex) { return Problem(ex); }
        });
        root.MapPost("/", (DateOnly? evaluationDate, CreateSourcingOptionRequest request, SourcingService service,
            CancellationToken token) => ExecuteCreated(() => service.Create(request, RequireDate(evaluationDate), token)));
        root.MapPut("/{id:guid}", (Guid id, DateOnly? evaluationDate, UpdateSourcingOptionRequest request,
            SourcingService service, CancellationToken token) => Execute(() => service.Update(id, request,
                RequireDate(evaluationDate), token)));
        root.MapPost("/{id:guid}/deactivate", (Guid id, DateOnly? evaluationDate,
            ChangeSourcingOptionStateRequest request, SourcingService service, CancellationToken token) =>
            Execute(() => service.ChangeState(id, false, request, RequireDate(evaluationDate), token)));
        root.MapPost("/{id:guid}/reactivate", (Guid id, DateOnly? evaluationDate,
            ChangeSourcingOptionStateRequest request, SourcingService service, CancellationToken token) =>
            Execute(() => service.ChangeState(id, true, request, RequireDate(evaluationDate), token)));
        root.MapPost("/{id:guid}/set-preferred", (Guid id, DateOnly? evaluationDate,
            SetPreferredSourcingOptionRequest request, SourcingService service, CancellationToken token) =>
            Execute(() => service.SetPreferred(id, request, RequireDate(evaluationDate), token)));
        root.MapPost("/{id:guid}/prices", (Guid id, DateOnly? evaluationDate,
            CreateSourcingPriceRequest request, SourcingService service, CancellationToken token) =>
            Execute(() => service.AddPrice(id, request, RequireDate(evaluationDate), token)));
        root.MapPost("/{id:guid}/prices/{priceId:guid}/close", (Guid id, Guid priceId, DateOnly? evaluationDate,
            CloseSourcingPriceRequest request, SourcingService service, CancellationToken token) =>
            Execute(() => service.ClosePrice(id, priceId, request, RequireDate(evaluationDate), token)));
        return endpoints;
    }

    private static DateOnly RequireDate(DateOnly? value) => value ?? throw new SourcingValidationException(
        new Dictionary<string, string[]> { ["evaluationDate"] = ["تاریخ ارزیابی باید صریح ارسال شود."] });
    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    { try { return Results.Ok(await action()); } catch (Exception ex) { return Problem(ex); } }
    private static async Task<IResult> ExecuteCreated(Func<Task<SourcingOptionResponse>> action)
    {
        try { var result = await action(); return Results.Created($"/api/sourcing-options/{result.Id}", result); }
        catch (Exception ex) { return Problem(ex); }
    }
    private static IResult Problem(Exception ex) => ex switch
    {
        SourcingValidationException validation => Results.ValidationProblem(validation.Errors),
        SourcingConflictException conflict => Results.Problem(statusCode: 409, title: "Sourcing conflict",
            detail: conflict.Message, extensions: new Dictionary<string, object?>
            { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw ex
    };
}
