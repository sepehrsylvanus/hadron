using Hadron.Erp.Api.Modules.Inventory.Application;

namespace Hadron.Erp.Api.Modules.Production.Application;

public sealed record ProductionItemRef(Guid Id, string Code, string ItemType, string NameFa,
    string? NameEn, bool IsActive, InventoryUnitRef BaseUnit);
public sealed record ProductionForecastRef(Guid SeriesId, Guid RevisionId, int RevisionNumber,
    decimal ForecastQuantity, string PeriodCode, DateOnly PeriodStart, DateOnly PeriodEnd);
public sealed record ProductionMaterialStagingRef(Guid Id, Guid InventoryTransferId,
    string TransferCode, string TransferStatus, decimal BaseQuantity, decimal EffectiveReceivedQuantity);
public sealed record ProductionOrderMaterialResponse(Guid Id, Guid BomLineId,
    ProductionItemRef Item, InventoryUnitRef Unit, decimal RequiredQuantity,
    decimal StagedQuantity, decimal ConsumedQuantity, decimal RemainingToStage,
    decimal UsageQty, decimal BaseOutputQty, int SortOrder,
    IReadOnlyList<ProductionMaterialStagingRef> Stagings);
public sealed record ProductionReportMaterialResponse(Guid Id, Guid ProductionOrderMaterialId,
    decimal StandardQuantity, decimal ActualQuantity, string? VarianceReason,
    Guid InventoryTransactionId);
public sealed record ProductionReportResponse(Guid Id, DateOnly ProductionDate,
    decimal GoodQuantity, decimal RejectedQuantity, Guid DestinationWarehouseId,
    Guid FinishedGoodsTransactionId, Guid DailyActualId, Guid DailyActualRevisionId,
    string? Notes, string? ActorIdentifier, DateTime CreatedAtUtc,
    IReadOnlyList<ProductionReportMaterialResponse> Materials);
public sealed record ProductionOrderResponse(Guid Id, string Code, string Status,
    ProductionItemRef Product, decimal PlannedQuantity, decimal ReportedGoodQuantity,
    decimal ReportedRejectedQuantity, DateOnly PlannedStartDate, DateOnly PlannedEndDate,
    WarehouseRef DestinationWarehouse, ProductionForecastRef Forecast,
    Guid? BomRevisionId, string? BomRevisionCode, string? Notes, string? ActorIdentifier,
    long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    IReadOnlyList<ProductionOrderMaterialResponse> Materials,
    IReadOnlyList<ProductionReportResponse> Reports);
public sealed record ProductionOrderListResponse(IReadOnlyList<ProductionOrderResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateProductionOrderRequest(Guid ProductId, decimal PlannedQuantity,
    DateOnly PlannedStartDate, DateOnly PlannedEndDate, Guid DestinationWarehouseId,
    string? Notes);
public sealed record UpdateProductionOrderRequest(decimal PlannedQuantity,
    DateOnly PlannedStartDate, DateOnly PlannedEndDate, Guid DestinationWarehouseId,
    string? Notes, long ExpectedVersion);
public sealed record ProductionLifecycleRequest(long ExpectedVersion, string? Reason);
public sealed record StageProductionMaterialRequest(Guid SourceWarehouseId,
    Guid StagingWarehouseId, decimal Quantity, string? Reason, long ExpectedOrderVersion);
public sealed record ProductionMaterialConsumptionRequest(Guid ProductionOrderMaterialId,
    decimal? ActualQuantity, string? VarianceReason);
public sealed record PostProductionReportRequest(DateOnly ProductionDate,
    Guid StagingWarehouseId, decimal GoodQuantity, decimal RejectedQuantity,
    string? Notes, long ExpectedVersion,
    IReadOnlyList<ProductionMaterialConsumptionRequest>? Materials);

public sealed record ProductionOrganizationalUnitRef(Guid Id, string NameFa, string? NameEn, bool IsActive);
public sealed record ProductionWorkCenterResponse(Guid Id, string Code, string NameFa, string? NameEn,
    ProductionOrganizationalUnitRef OrganizationalUnit, int WorkingWeekMask, bool IsActive,
    long Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record ProductionWorkCenterListResponse(IReadOnlyList<ProductionWorkCenterResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateProductionWorkCenterRequest(string NameFa, string? NameEn,
    Guid OrganizationalUnitId, int WorkingWeekMask);
public sealed record UpdateProductionWorkCenterRequest(string NameFa, string? NameEn,
    Guid OrganizationalUnitId, int WorkingWeekMask, long ExpectedVersion);
public sealed record ChangeProductionWorkCenterStateRequest(long ExpectedVersion, string? Reason);

public sealed record ManufacturingRouteOperationInput(int Sequence, Guid WorkCenterId,
    int PreparationWorkingDays, int ExecutionWorkingDays, string? Notes);
public sealed record ManufacturingRouteOperationResponse(Guid Id, int Sequence,
    ProductionWorkCenterResponse WorkCenter, int PreparationWorkingDays,
    int ExecutionWorkingDays, string? Notes);
public sealed record ManufacturingRouteResponse(Guid Id, string Code, ProductionItemRef Item,
    int RevisionNumber, string Status, string? Notes, int TotalWorkingDays, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    IReadOnlyList<ManufacturingRouteOperationResponse> Operations);
public sealed record ManufacturingRouteListResponse(IReadOnlyList<ManufacturingRouteResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record ManufacturingRouteItemCandidateListResponse(IReadOnlyList<ProductionItemRef> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateManufacturingRouteRequest(Guid ItemId, string? Notes,
    IReadOnlyList<ManufacturingRouteOperationInput> Operations);
public sealed record UpdateManufacturingRouteRequest(string? Notes,
    IReadOnlyList<ManufacturingRouteOperationInput> Operations, long ExpectedVersion);
public sealed record ManufacturingRouteLifecycleRequest(long ExpectedVersion, string? Reason);
public sealed record ManufacturingOperationScheduleResponse(Guid OperationId, int Sequence,
    Guid WorkCenterId, string WorkCenterCode, string WorkCenterNameFa,
    DateOnly PlannedStartDate, DateOnly PlannedEndDate, int PreparationWorkingDays,
    int ExecutionWorkingDays);
public sealed record ManufacturingRouteScheduleResponse(Guid RouteId, string RouteCode,
    Guid ItemId, DateOnly CompletionDate, DateOnly MaterialRequiredDate,
    IReadOnlyList<ManufacturingOperationScheduleResponse> Operations);

public sealed class ProductionValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Production validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public static ProductionValidationException Single(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed class ProductionConflictException(string code, string message, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
