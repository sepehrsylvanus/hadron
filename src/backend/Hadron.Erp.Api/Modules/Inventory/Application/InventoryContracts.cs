namespace Hadron.Erp.Api.Modules.Inventory.Application;

public sealed record InventoryUnitRef(Guid Id, string Code, string NameFa, string? NameEn,
    string? Symbol, bool IsActive);
public sealed record InventoryItemRef(Guid Id, string Code, string ItemType, string NameFa,
    string? NameEn, bool IsActive, InventoryUnitRef BaseUnit);
public sealed record WarehouseRef(Guid Id, string Code, string NameFa, string? NameEn,
    bool IsActive, long Version);

public sealed record WarehouseResponse(Guid Id, string Code, string NameFa, string? NameEn,
    bool IsActive, string? Notes, long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record WarehouseListResponse(IReadOnlyList<WarehouseResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateWarehouseRequest(string NameFa, string? NameEn, string? Notes);
public sealed record UpdateWarehouseRequest(string NameFa, string? NameEn, string? Notes,
    long ExpectedVersion);
public sealed record ChangeWarehouseStateRequest(long ExpectedVersion, string? Reason);

public sealed record InventoryItemListResponse(IReadOnlyList<InventoryItemRef> Items,
    int Page, int PageSize, int TotalCount);
public sealed record InventoryBalanceRow(InventoryItemRef Item, WarehouseRef Warehouse,
    decimal ActualOnHand, bool InactiveWarehouseStockWarning);
public sealed record InventoryBalanceListResponse(DateOnly AsOfDate,
    IReadOnlyList<InventoryBalanceRow> Items, int Page, int PageSize, int TotalCount);
public sealed record ItemWarehouseBalance(WarehouseRef Warehouse, decimal ActualOnHand,
    bool InactiveWarehouseStockWarning);
public sealed record ItemInventoryDetailResponse(DateOnly AsOfDate, InventoryItemRef Item,
    decimal CompanyTotal, bool HasInactiveWarehouseStockWarning,
    IReadOnlyList<ItemWarehouseBalance> Warehouses);

public sealed record InventoryTransactionResponse(Guid Id, string TransactionType,
    InventoryItemRef Item, WarehouseRef Warehouse, WarehouseRef? DestinationWarehouse,
    decimal Quantity, InventoryUnitRef Unit, DateOnly EffectiveDate, string? Reason,
    Guid? OriginalTransactionId, string? SourceDocumentType, Guid? SourceDocumentId,
    string? ActorIdentifier, DateTime CreatedAtUtc);
public sealed record InventoryTransactionListResponse(IReadOnlyList<InventoryTransactionResponse> Items,
    int Page, int PageSize, int TotalCount);

public sealed record PostInventoryRequest(Guid ItemId, Guid WarehouseId, decimal Quantity,
    DateOnly EffectiveDate, string? Reason);
public sealed record PostAdjustmentRequest(Guid ItemId, Guid WarehouseId, string Direction,
    decimal Quantity, DateOnly EffectiveDate, string? Reason);
public sealed record PostTransferRequest(Guid ItemId, Guid SourceWarehouseId,
    Guid DestinationWarehouseId, decimal Quantity, DateOnly EffectiveDate, string? Reason);
public sealed record ReverseInventoryTransactionRequest(string? Reason);

public sealed class InventoryValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Inventory validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public static InventoryValidationException Single(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed class InventoryConflictException(string code, string message, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
