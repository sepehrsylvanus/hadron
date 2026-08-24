using Hadron.Erp.Api.Modules.Inventory.Application;

namespace Hadron.Erp.Api.Modules.Inventory.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/inventory").WithTags("Inventory");

        root.MapGet("/warehouses", (string? search, bool? includeInactive, int? page, int? pageSize,
            InventoryService service, CancellationToken ct) => Execute(() => service.ListWarehouses(
                search, includeInactive ?? false, page ?? 1, pageSize ?? 25, ct)));
        root.MapGet("/warehouses/{id:guid}", async (Guid id, InventoryService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetWarehouse(id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        });
        root.MapPost("/warehouses", (CreateWarehouseRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.CreateWarehouse(request, ct),
                response => $"/api/inventory/warehouses/{response.Id}"));
        root.MapPut("/warehouses/{id:guid}", (Guid id, UpdateWarehouseRequest request,
            InventoryService service, CancellationToken ct) => Execute(() => service.UpdateWarehouse(id, request, ct)));
        root.MapPost("/warehouses/{id:guid}/deactivate", (Guid id, ChangeWarehouseStateRequest request,
            InventoryService service, CancellationToken ct) => Execute(() => service.ChangeWarehouseState(id, false, request, ct)));
        root.MapPost("/warehouses/{id:guid}/reactivate", (Guid id, ChangeWarehouseStateRequest request,
            InventoryService service, CancellationToken ct) => Execute(() => service.ChangeWarehouseState(id, true, request, ct)));

        root.MapGet("/items", (string? search, string? itemType, bool? includeInactive,
            int? page, int? pageSize, InventoryService service, CancellationToken ct) =>
            Execute(() => service.ListItems(search, itemType, includeInactive ?? false,
                page ?? 1, pageSize ?? 25, ct)));
        root.MapGet("/balances", (DateOnly? asOfDate, string? search, Guid? itemId,
            Guid? warehouseId, string? itemType, int? page, int? pageSize,
            InventoryService service, CancellationToken ct) => Execute(() => service.ListBalances(
                asOfDate, search, itemId, warehouseId, itemType, page ?? 1, pageSize ?? 25, ct)));
        root.MapGet("/items/{id:guid}/position", async (Guid id, DateOnly? asOfDate,
            InventoryService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetItemInventory(id, asOfDate, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        });

        root.MapGet("/transactions", (Guid? itemId, Guid? warehouseId, string? transactionType,
            DateOnly? from, DateOnly? to, int? page, int? pageSize, InventoryService service,
            CancellationToken ct) => Execute(() => service.ListTransactions(itemId, warehouseId,
                transactionType, from, to, page ?? 1, pageSize ?? 25, ct)));
        root.MapGet("/transactions/{id:guid}", async (Guid id, InventoryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetTransaction(id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        });

        root.MapPost("/openings", (PostInventoryRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostOpening(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"));
        root.MapPost("/receipts", (PostInventoryRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostReceipt(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"));
        root.MapPost("/issues", (PostInventoryRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostIssue(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"));
        root.MapPost("/adjustments", (PostAdjustmentRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostAdjustment(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"));
        root.MapPost("/transfers", (PostTransferRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostTransfer(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"));
        root.MapPost("/transactions/{id:guid}/reverse", (Guid id,
            ReverseInventoryTransactionRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.Reverse(id, request, ct),
                response => $"/api/inventory/transactions/{response.Id}"));

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception exception) { return Problem(exception); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, Func<T, string> location)
    {
        try
        {
            var response = await action();
            return Results.Created(location(response), response);
        }
        catch (Exception exception) { return Problem(exception); }
    }

    private static IResult Problem(Exception exception) => exception switch
    {
        InventoryValidationException validation => Results.ValidationProblem(validation.Errors),
        InventoryConflictException conflict => Results.Problem(statusCode: 409,
            title: "Inventory conflict", detail: conflict.Message,
            extensions: new Dictionary<string, object?>
            { ["code"] = conflict.Code, ["details"] = conflict.Details }),
        KeyNotFoundException => Results.NotFound(),
        _ => throw exception
    };
}
