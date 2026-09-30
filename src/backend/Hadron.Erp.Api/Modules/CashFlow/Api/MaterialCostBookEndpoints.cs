using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.CashFlow.Api;

public static class MaterialCostBookEndpoints
{
    public static IEndpointRouteBuilder MapMaterialCostBookEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/v1/cash-flow")
            .WithTags("Cash Flow Material Cost Books v1");

        root.MapGet("/material-cost-books", (int? page, int? pageSize, string? search,
                string? status, MaterialCostBookService service, CancellationToken ct) =>
                Execute(() => service.GetBooks(page ?? 1, pageSize ?? 25, search, status, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-books/item-options", (int? page, int? pageSize,
                string? search, MaterialCostBookService service, CancellationToken ct) =>
                Execute(() => service.GetItemOptions(page ?? 1, pageSize ?? 25, search, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-books/currency-options",
                (MaterialCostBookService service, CancellationToken ct) =>
                    Execute(() => service.GetCurrencyOptions(ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-books/supplier-options", (int? page, int? pageSize,
                string? search, Guid? selectedSupplierId, MaterialCostBookService service,
                CancellationToken ct) =>
                Execute(() => service.GetSupplierOptions(
                    page ?? 1, pageSize ?? 25, search, selectedSupplierId, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-books/resolve-jalali-date",
                (string? jalaliDate, IPlanningCalendar calendar) =>
                    CashFlowJalaliDateResolver.Resolve(jalaliDate, calendar))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-books/effective-selections", (DateOnly? asOfDate,
                int? page, int? pageSize, string? search, MaterialCostBookService service,
                CancellationToken ct) => Execute(() => service.GetEffectiveSelections(
                    RequiredDate(asOfDate), page ?? 1, pageSize ?? 100, search, ct)))
            .RequirePermission(Permissions.CashFlowCostingView);

        root.MapGet("/material-cost-books/{bookId:guid}", (Guid bookId,
                MaterialCostBookService service, CancellationToken ct) =>
                ExecuteFound(() => service.GetBook(bookId, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-book-revisions/{revisionId:guid}", (Guid revisionId,
                MaterialCostBookService service, CancellationToken ct) =>
                ExecuteFound(() => service.GetRevision(revisionId, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapGet("/material-cost-book-revisions/{revisionId:guid}/lines", (Guid revisionId,
                int? page, int? pageSize, string? search, MaterialCostBookService service,
                CancellationToken ct) => ExecuteFound(() => service.GetLines(
                    revisionId, page ?? 1, pageSize ?? 50, search, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookView);

        root.MapPost("/material-cost-books", (CreateMaterialCostBookRequest request,
                MaterialCostBookService service, CancellationToken ct) =>
                ExecuteCreated(() => service.Create(request, ct),
                    "/api/v1/cash-flow/material-cost-book-revisions"))
            .RequirePermission(Permissions.CashFlowMaterialCostBookManage);

        root.MapPut("/material-cost-book-revisions/{revisionId:guid}", (Guid revisionId,
                UpdateMaterialCostBookDraftRequest request, MaterialCostBookService service,
                CancellationToken ct) => Execute(() => service.UpdateDraft(revisionId, request, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookManage);

        root.MapPut("/material-cost-book-revisions/{revisionId:guid}/lines/{itemId:guid}",
                (Guid revisionId, Guid itemId, UpsertMaterialCostBookLineRequest request,
                    MaterialCostBookService service, CancellationToken ct) =>
                    Execute(() => service.UpsertLine(revisionId, itemId, request, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookManage);

        root.MapPost("/material-cost-book-revisions/{revisionId:guid}/lines/{itemId:guid}/remove",
                (Guid revisionId, Guid itemId, RemoveMaterialCostBookLineRequest request,
                    MaterialCostBookService service, CancellationToken ct) =>
                    Execute(() => service.RemoveLine(revisionId, itemId, request, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookManage);

        root.MapPost("/material-cost-book-revisions/{revisionId:guid}/publish",
                (Guid revisionId, PublishMaterialCostBookRequest request,
                    MaterialCostBookService service, CancellationToken ct) =>
                    Execute(() => service.Publish(revisionId, request, ct)))
            .RequirePermission(Permissions.CashFlowMaterialCostBookPublish);

        root.MapPost("/material-cost-book-revisions/{revisionId:guid}/corrections",
                (Guid revisionId, CreateMaterialCostBookCorrectionRequest request,
                    MaterialCostBookService service, CancellationToken ct) =>
                    ExecuteCreated(() => service.CreateCorrection(revisionId, request, ct),
                        "/api/v1/cash-flow/material-cost-book-revisions"))
            .RequirePermission(Permissions.CashFlowMaterialCostBookManage);

        return endpoints;
    }

    private static DateOnly RequiredDate(DateOnly? value) => value ??
        throw new MaterialCostBookValidationException(new Dictionary<string, string[]>
        {
            ["asOfDate"] = ["تاریخ مبنای صریح الزامی است."]
        });

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        IResult? problem = null;
        try { return Results.Ok(await action()); }
        catch (Exception exception) when (TryProblem(exception, out problem)) { return problem!; }
    }

    private static async Task<IResult> ExecuteFound<T>(Func<Task<T?>> action) where T : class
    {
        IResult? problem = null;
        try
        {
            var value = await action();
            return value is null ? Results.NotFound() : Results.Ok(value);
        }
        catch (Exception exception) when (TryProblem(exception, out problem)) { return problem!; }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, string basePath)
    {
        IResult? problem = null;
        try
        {
            var value = await action();
            var id = value?.GetType().GetProperty("RevisionId")?.GetValue(value);
            return Results.Created($"{basePath}/{id}", value);
        }
        catch (Exception exception) when (TryProblem(exception, out problem)) { return problem!; }
    }

    private static bool TryProblem(Exception exception, out IResult? problem)
    {
        switch (exception)
        {
            case MaterialCostBookValidationException validation:
                problem = Results.ValidationProblem(validation.Errors);
                return true;
            case MaterialCostBookConflictException conflict:
            {
                var extensions = new Dictionary<string, object?> { ["code"] = conflict.Code };
                if (conflict.Details is not null) extensions["details"] = conflict.Details;
                problem = Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Material cost-book conflict", detail: conflict.Message,
                    extensions: extensions);
                return true;
            }
            case KeyNotFoundException:
                problem = Results.NotFound();
                return true;
            default:
                problem = null;
                return false;
        }
    }
}
