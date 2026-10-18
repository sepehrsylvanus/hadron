using Hadron.Erp.Api.Modules.Inventory.Application;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.Api.Modules.Inventory.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup("/api/inventory").WithTags("Inventory").RequireAuthorization();

        root.MapGet("/overview", (DateOnly? asOfDate, InventoryService service, CancellationToken ct) =>
            Execute(() => service.Overview(asOfDate, ct))).RequirePermission(Permissions.InventoryView);

        root.MapGet("/warehouses", (string? search, bool? includeInactive, string? warehouseType,
            int? page, int? pageSize,
            InventoryService service, CancellationToken ct) => Execute(() => service.ListWarehouses(
                search, includeInactive ?? false, warehouseType, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.InventoryView);
        root.MapGet("/warehouses/{id:guid}", async (Guid id, InventoryService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetWarehouse(id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.InventoryView);
        root.MapGet("/responsible-users", (string? search, int? page, int? pageSize,
            InventoryService service, CancellationToken ct) => Execute(() =>
                service.ListResponsibleUsers(search, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.WarehouseManage);
        root.MapPost("/warehouses", (CreateWarehouseRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.CreateWarehouse(request, ct),
                response => $"/api/inventory/warehouses/{response.Id}"))
            .RequirePermission(Permissions.WarehouseManage);
        root.MapPut("/warehouses/{id:guid}", (Guid id, UpdateWarehouseRequest request,
            InventoryService service, CancellationToken ct) => Execute(() => service.UpdateWarehouse(id, request, ct)))
            .RequirePermission(Permissions.WarehouseManage);
        root.MapPost("/warehouses/{id:guid}/deactivate", (Guid id, ChangeWarehouseStateRequest request,
            InventoryService service, CancellationToken ct) => Execute(() => service.ChangeWarehouseState(id, false, request, ct)))
            .RequirePermission(Permissions.WarehouseManage);
        root.MapPost("/warehouses/{id:guid}/reactivate", (Guid id, ChangeWarehouseStateRequest request,
            InventoryService service, CancellationToken ct) => Execute(() => service.ChangeWarehouseState(id, true, request, ct)))
            .RequirePermission(Permissions.WarehouseManage);

        root.MapGet("/items", (string? search, string? itemType, bool? includeInactive,
            int? page, int? pageSize, InventoryService service, CancellationToken ct) =>
            Execute(() => service.ListItems(search, itemType, includeInactive ?? false,
                page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.InventoryView);
        root.MapGet("/balances", (DateOnly? asOfDate, string? search, Guid? itemId,
            Guid? warehouseId, string? itemType, int? page, int? pageSize,
            InventoryService service, CancellationToken ct) => Execute(() => service.ListBalances(
                asOfDate, search, itemId, warehouseId, itemType, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.InventoryView);
        root.MapGet("/items/{id:guid}/position", async (Guid id, DateOnly? asOfDate,
            InventoryService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetItemInventory(id, asOfDate, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.InventoryView);
        root.MapGet("/items/{id:guid}/units", async (Guid id, bool? includeInactive,
            InventoryService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.ItemUnitConversions(id, includeInactive ?? false, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.InventoryView);
        root.MapPost("/items/{id:guid}/units", (Guid id,
            CreateInventoryItemUnitConversionRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.CreateItemUnitConversion(id,
                request, ct), response => $"/api/inventory/items/{id}/units/{response.Id}"))
            .RequirePermission(Permissions.InventoryItemUnitManage);
        root.MapPut("/items/{id:guid}/units/{conversionId:guid}", (Guid id, Guid conversionId,
            UpdateInventoryItemUnitConversionRequest request, InventoryService service,
            CancellationToken ct) => Execute(() => service.UpdateItemUnitConversion(id,
                conversionId, request, ct)))
            .RequirePermission(Permissions.InventoryItemUnitManage);
        root.MapPost("/items/{id:guid}/units/{conversionId:guid}/deactivate", (Guid id,
            Guid conversionId, ChangeInventoryItemUnitConversionStateRequest request,
            InventoryService service, CancellationToken ct) => Execute(() =>
                service.ChangeItemUnitConversionState(id, conversionId, false, request, ct)))
            .RequirePermission(Permissions.InventoryItemUnitManage);
        root.MapPost("/items/{id:guid}/units/{conversionId:guid}/reactivate", (Guid id,
            Guid conversionId, ChangeInventoryItemUnitConversionStateRequest request,
            InventoryService service, CancellationToken ct) => Execute(() =>
                service.ChangeItemUnitConversionState(id, conversionId, true, request, ct)))
            .RequirePermission(Permissions.InventoryItemUnitManage);

        root.MapGet("/transactions", (Guid? itemId, Guid? warehouseId, string? transactionType,
            DateOnly? from, DateOnly? to, int? page, int? pageSize, InventoryService service,
            CancellationToken ct) => Execute(() => service.ListTransactions(itemId, warehouseId,
                transactionType, from, to, page ?? 1, pageSize ?? 25, ct)))
            .RequirePermission(Permissions.InventoryView);
        root.MapGet("/transactions/{id:guid}", async (Guid id, InventoryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetTransaction(id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.InventoryView);

        root.MapGet("/transfers", (string? search, string? status, Guid? warehouseId,
            int? page, int? pageSize, InventoryService service, CancellationToken ct) =>
            Execute(() => service.ListTransfers(search, status, warehouseId, page ?? 1,
                pageSize ?? 25, ct))).RequirePermission(Permissions.InventoryView);
        root.MapGet("/transfers/{id:guid}", async (Guid id, InventoryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetTransfer(id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (Exception exception) { return Problem(exception); }
        }).RequirePermission(Permissions.InventoryView);
        root.MapPost("/transfers", (CreateInventoryTransferRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.CreateTransfer(request, ct),
                response => $"/api/inventory/transfers/{response.Id}"))
            .RequirePermission(Permissions.InventoryTransferPost);
        root.MapPut("/transfers/{id:guid}", (Guid id, UpdateInventoryTransferRequest request,
            InventoryService service, CancellationToken ct) => Execute(() =>
                service.UpdateTransfer(id, request, ct)))
            .RequirePermission(Permissions.InventoryTransferPost);
        root.MapPost("/transfers/{id:guid}/dispatch", (Guid id,
            DispatchInventoryTransferRequest request, InventoryService service,
            CancellationToken ct) => Execute(() => service.DispatchTransfer(id, request, ct)))
            .RequirePermission(Permissions.InventoryTransferPost);
        root.MapPost("/transfers/{id:guid}/receive", (Guid id,
            ReceiveInventoryTransferRequest request, InventoryService service,
            CancellationToken ct) => Execute(() => service.ReceiveTransfer(id, request, ct)))
            .RequirePermission(Permissions.InventoryTransferPost);
        root.MapPost("/transfers/{id:guid}/cancel", (Guid id,
            CancelInventoryTransferRequest request, InventoryService service,
            CancellationToken ct) => Execute(() => service.CancelTransfer(id, request, ct)))
            .RequirePermission(Permissions.InventoryTransferPost);
        root.MapPost("/transfers/{id:guid}/receipts/{receiptId:guid}/reverse", (Guid id,
            Guid receiptId, ReverseInventoryTransferReceiptRequest request, InventoryService service,
            CancellationToken ct) => Execute(() => service.ReverseTransferReceipt(id, receiptId, request, ct)))
            .RequirePermission(Permissions.InventoryTransactionReverse);

        root.MapPost("/openings", (PostInventoryRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostOpening(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"))
            .RequirePermission(Permissions.InventoryOpeningPost);
        root.MapPost("/receipts", (PostInventoryRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostReceipt(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"))
            .RequirePermission(Permissions.InventoryReceiptPost);
        root.MapPost("/issues", (PostInventoryRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostIssue(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"))
            .RequirePermission(Permissions.InventoryIssuePost);
        root.MapPost("/adjustments", (PostAdjustmentRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.PostAdjustment(request, ct),
                response => $"/api/inventory/transactions/{response.Id}"))
            .RequirePermission(Permissions.InventoryAdjustmentPost);
        root.MapPost("/transactions/{id:guid}/reverse", (Guid id,
            ReverseInventoryTransactionRequest request, InventoryService service,
            CancellationToken ct) => ExecuteCreated(() => service.Reverse(id, request, ct),
                response => $"/api/inventory/transactions/{response.Id}"))
            .RequirePermission(Permissions.InventoryTransactionReverse);

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
