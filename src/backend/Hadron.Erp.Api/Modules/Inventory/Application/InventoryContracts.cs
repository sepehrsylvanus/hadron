namespace Hadron.Erp.Api.Modules.Inventory.Application;

public sealed record InventoryUnitRef(Guid Id, string Code, string NameFa, string? NameEn,
    string? Symbol, bool IsActive);
public sealed record InventoryItemRef(Guid Id, string Code, string ItemType, string NameFa,
    string? NameEn, bool IsActive, InventoryUnitRef BaseUnit);
public sealed record WarehouseRef(Guid Id, string Code, string NameFa, string? NameEn,
    string WarehouseType, bool IncludeInMrpAvailable, bool IsActive, long Version);
public sealed record WarehouseResponsibleUserRef(Guid Id, string UserName, string FullName,
    string PhotoUrl, bool IsActive);
public sealed record WarehouseResponsibleUserListResponse(
    IReadOnlyList<WarehouseResponsibleUserRef> Items, int Page, int PageSize, int TotalCount);

public sealed record WarehouseResponse(Guid Id, string Code, string NameFa, string? NameEn,
    string? AddressFa, string? AddressEn, WarehouseResponsibleUserRef? PrimaryResponsibleUser,
    string WarehouseType, bool IncludeInMrpAvailable, bool IsActive, string? Notes,
    long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record WarehouseListResponse(IReadOnlyList<WarehouseResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateWarehouseRequest(string NameFa, string? NameEn, string AddressFa,
    string? AddressEn, Guid PrimaryResponsibleUserId, string? Notes,
    string? WarehouseType = null);
public sealed record UpdateWarehouseRequest(string NameFa, string? NameEn, string AddressFa,
    string? AddressEn, Guid PrimaryResponsibleUserId, string? Notes, long ExpectedVersion,
    string? WarehouseType = null);
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
    decimal UsablePhysicalTotal, decimal ProductionStagingTotal, decimal InTransitTotal,
    decimal CompanyOwnedTotal,
    bool HasInactiveWarehouseStockWarning,
    IReadOnlyList<ItemWarehouseBalance> Warehouses);

public sealed record InventoryOverviewResponse(DateOnly AsOfDate, int ActiveWarehouseCount,
    int InactiveWarehouseCount, int ItemPositionCount, decimal TotalInTransit,
    int OpenTransferCount, int NegativeBalanceCount);

public sealed record InventoryTransactionResponse(Guid Id, string TransactionType,
    InventoryItemRef Item, WarehouseRef Warehouse, WarehouseRef? DestinationWarehouse,
    decimal Quantity, InventoryUnitRef Unit, decimal EnteredQuantity,
    InventoryUnitRef TransactionUnit, decimal ConversionFactorToBase,
    DateOnly EffectiveDate, string? Reason,
    Guid? OriginalTransactionId, string? SourceDocumentType, Guid? SourceDocumentId,
    string? ActorIdentifier, DateTime CreatedAtUtc);
public sealed record InventoryTransactionListResponse(IReadOnlyList<InventoryTransactionResponse> Items,
    int Page, int PageSize, int TotalCount);

public sealed record PostInventoryRequest(Guid ItemId, Guid WarehouseId, decimal Quantity,
    DateOnly EffectiveDate, string? Reason, Guid? TransactionUnitId = null);
public sealed record PostAdjustmentRequest(Guid ItemId, Guid WarehouseId, string Direction,
    decimal Quantity, DateOnly EffectiveDate, string? Reason, Guid? TransactionUnitId = null);
public sealed record PostTransferRequest(Guid ItemId, Guid SourceWarehouseId,
    Guid DestinationWarehouseId, decimal Quantity, DateOnly EffectiveDate, string? Reason);
public sealed record ReverseInventoryTransactionRequest(string? Reason);

public sealed record CreateInventoryTransferRequest(Guid ItemId, Guid SourceWarehouseId,
    Guid DestinationWarehouseId, decimal Quantity, string? Reason,
    Guid? TransactionUnitId = null);
public sealed record UpdateInventoryTransferRequest(Guid ItemId, Guid SourceWarehouseId,
    Guid DestinationWarehouseId, decimal Quantity, string? Reason, long ExpectedVersion,
    Guid? TransactionUnitId = null);
public sealed record DispatchInventoryTransferRequest(DateOnly EffectiveDate, long ExpectedVersion,
    string? Reason);
public sealed record ReceiveInventoryTransferRequest(decimal Quantity, DateOnly EffectiveDate,
    long ExpectedVersion, string? Reason, Guid? TransactionUnitId = null);
public sealed record CancelInventoryTransferRequest(long ExpectedVersion, string Reason);
public sealed record ReverseInventoryTransferReceiptRequest(long ExpectedVersion, string Reason);
public sealed record InventoryTransferReceiptResponse(Guid Id, decimal Quantity,
    decimal EnteredQuantity, InventoryUnitRef TransactionUnit, decimal ConversionFactorToBase,
    DateOnly EffectiveDate, string? Reason, string? ActorIdentifier, Guid InventoryTransactionId,
    bool IsReversed, DateTime CreatedAtUtc);
public sealed record InventoryTransferResponse(Guid Id, string Code, string Status,
    InventoryItemRef Item, InventoryUnitRef Unit, WarehouseRef SourceWarehouse,
    WarehouseRef DestinationWarehouse, decimal RequestedQuantity, decimal EnteredRequestedQuantity,
    InventoryUnitRef TransactionUnit, decimal ConversionFactorToBase, decimal DispatchedQuantity,
    decimal ReceivedQuantity, decimal InTransitQuantity, DateOnly? DispatchDate,
    string? Reason, string? CancellationReason, string? ActorIdentifier, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    IReadOnlyList<InventoryTransferReceiptResponse> Receipts);
public sealed record InventoryTransferListResponse(IReadOnlyList<InventoryTransferResponse> Items,
    int Page, int PageSize, int TotalCount);

public sealed record InventoryItemUnitConversionResponse(Guid? Id, InventoryUnitRef TransactionUnit,
    InventoryUnitRef BaseUnit, decimal FactorToBase, bool IsBaseUnit, bool IsActive, long Version);
public sealed record InventoryItemUnitConversionListResponse(InventoryItemRef Item,
    IReadOnlyList<InventoryItemUnitConversionResponse> Items);
public sealed record CreateInventoryItemUnitConversionRequest(Guid TransactionUnitId,
    decimal FactorToBase);
public sealed record UpdateInventoryItemUnitConversionRequest(decimal FactorToBase,
    long ExpectedVersion);
public sealed record ChangeInventoryItemUnitConversionStateRequest(long ExpectedVersion,
    string? Reason);

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
