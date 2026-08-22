using Hadron.Erp.Api.Modules.ReferenceData.Application;
using Microsoft.AspNetCore.Mvc;

namespace Hadron.Erp.Api.Modules.ReferenceData.Api;

public static class ReferenceDataEndpoints
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/reference-data").WithTags("Reference Data");

        root.MapGet("/types", (ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.GetTypes(token)));

        root.MapGet("/types/{typeCode}/values", (string typeCode, bool? includeInactive, string? search,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.GetValues(typeCode, includeInactive ?? false, search, token)));
        root.MapGet("/values/{id:guid}", async (Guid id, ReferenceDataService service, CancellationToken token) =>
            await ExecuteFound(() => service.GetValue(id, token)));
        root.MapPost("/types/{typeCode}/values", (string typeCode, CreateReferenceValueRequest request,
                ReferenceDataService service, CancellationToken token) =>
            ExecuteCreated(() => service.CreateValue(typeCode, request, token), "/api/reference-data/values"));
        root.MapPut("/values/{id:guid}", (Guid id, UpdateReferenceValueRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.UpdateValue(id, request, token)));
        root.MapPost("/values/{id:guid}/deactivate", (Guid id, ChangeActiveStateRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.ChangeValueState(id, false, request, token)));
        root.MapPost("/values/{id:guid}/reactivate", (Guid id, ChangeActiveStateRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.ChangeValueState(id, true, request, token)));

        root.MapGet("/currencies", (bool? includeInactive, string? search, ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.GetCurrencies(includeInactive ?? false, search, token)));
        root.MapGet("/currencies/{id:guid}", async (Guid id, ReferenceDataService service, CancellationToken token) =>
            await ExecuteFound(() => service.GetCurrency(id, token)));
        root.MapPost("/currencies", (CreateCurrencyRequest request, ReferenceDataService service, CancellationToken token) =>
            ExecuteCreated(() => service.CreateCurrency(request, token), "/api/reference-data/currencies"));
        root.MapPut("/currencies/{id:guid}", (Guid id, UpdateCurrencyRequest request, ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.UpdateCurrency(id, request, token)));
        root.MapPost("/currencies/{id:guid}/deactivate", (Guid id, ChangeActiveStateRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.ChangeCurrencyState(id, false, request, token)));
        root.MapPost("/currencies/{id:guid}/reactivate", (Guid id, ChangeActiveStateRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.ChangeCurrencyState(id, true, request, token)));

        root.MapGet("/units", (bool? includeInactive, string? search, ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.GetUnits(includeInactive ?? false, search, token)));
        root.MapGet("/units/{id:guid}", async (Guid id, ReferenceDataService service, CancellationToken token) =>
            await ExecuteFound(() => service.GetUnit(id, token)));
        root.MapPost("/units", (CreateUnitRequest request, ReferenceDataService service, CancellationToken token) =>
            ExecuteCreated(() => service.CreateUnit(request, token), "/api/reference-data/units"));
        root.MapPut("/units/{id:guid}", (Guid id, UpdateUnitRequest request, ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.UpdateUnit(id, request, token)));
        root.MapPost("/units/{id:guid}/deactivate", (Guid id, ChangeActiveStateRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.ChangeUnitState(id, false, request, token)));
        root.MapPost("/units/{id:guid}/reactivate", (Guid id, ChangeActiveStateRequest request,
                ReferenceDataService service, CancellationToken token) =>
            Execute(() => service.ChangeUnitState(id, true, request, token)));

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return ToProblem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, string basePath)
    {
        try
        {
            var result = await action();
            var id = result?.GetType().GetProperty("Id")?.GetValue(result);
            return Results.Created($"{basePath}/{id}", result);
        }
        catch (Exception exception) { return ToProblem(exception); }
    }

    private static async Task<IResult> ExecuteFound<T>(Func<Task<T?>> action) where T : class
    {
        try
        {
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (Exception exception) { return ToProblem(exception); }
    }

    private static IResult ToProblem(Exception exception) => exception switch
    {
        ReferenceDataValidationException validation => Results.ValidationProblem(validation.Errors),
        ReferenceDataConflictException conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Reference data conflict",
            detail: conflict.Message,
            extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
