using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.Planning.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.CashFlow.Api;

public static class PurchasePriceHistoryEndpoints
{
    public static IEndpointRouteBuilder MapPurchasePriceHistoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/v1/cash-flow")
            .WithTags("Cash Flow Purchase Price History v1");

        root.MapGet("/purchase-price-history/resolve-jalali-date",
                (string? jalaliDate, IPlanningCalendar calendar) =>
                    CashFlowJalaliDateResolver.Resolve(jalaliDate, calendar))
            .RequirePermission(Permissions.CashFlowPurchasePriceHistoryView);

        root.MapGet("/purchase-price-history/filter-options/items", (
                string? search, int? page, int? pageSize,
                PurchasePriceHistoryService service, CancellationToken ct) =>
                Execute(() => service.GetItemFilterOptions(
                    search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.CashFlowPurchasePriceHistoryView);

        root.MapGet("/purchase-price-history/filter-options/suppliers", (
                string? search, int? page, int? pageSize,
                PurchasePriceHistoryService service, CancellationToken ct) =>
                Execute(() => service.GetSupplierFilterOptions(
                    search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.CashFlowPurchasePriceHistoryView);

        root.MapGet("/purchase-price-history/filter-options/values", (
                PurchasePriceHistoryService service, CancellationToken ct) =>
                Execute(() => service.GetFilterValues(ct)))
            .RequirePermission(Permissions.CashFlowPurchasePriceHistoryView);

        root.MapGet("/purchase-price-history", (
                string? search,
                Guid? itemId,
                Guid? supplierId,
                string? currencyCode,
                string? purchaseOrderStatus,
                DateOnly? fromOrderDate,
                DateOnly? toOrderDate,
                Guid? valuationFxRateSetId,
                DateOnly? valuationAsOfDate,
                int? page,
                int? pageSize,
                PurchasePriceHistoryService service,
                CancellationToken cancellationToken) => Execute(() => service.Get(
                    search,
                    itemId,
                    supplierId,
                    currencyCode,
                    purchaseOrderStatus,
                    fromOrderDate,
                    toOrderDate,
                    valuationFxRateSetId,
                    valuationAsOfDate,
                    page ?? 1,
                    pageSize ?? 25,
                    cancellationToken)))
            .RequirePermission(Permissions.CashFlowPurchasePriceHistoryView);

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        IResult? problem = null;
        try
        {
            return Results.Ok(await action());
        }
        catch (Exception exception) when (TryCreatePublicProblem(exception, out problem))
        {
            return problem!;
        }
    }

    private static bool TryCreatePublicProblem(Exception exception, out IResult? problem)
    {
        switch (exception)
        {
            case CashFlowValidationException validation:
                problem = Results.ValidationProblem(validation.Errors);
                return true;
            case CashFlowConflictException conflict:
                problem = Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Cash-flow purchase-price-history conflict",
                    detail: conflict.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = conflict.Code });
                return true;
            default:
                problem = null;
                return false;
        }
    }
}
