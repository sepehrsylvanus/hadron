namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed record CreateMrpRunRequest(DateOnly AsOfDate, DateOnly? HorizonEndDate,
    IReadOnlyList<MrpRouteDecisionInput>? RouteDecisions);
public sealed record MrpRouteDecisionInput(Guid ItemId, string Route);
public sealed record MrpRouteCandidate(Guid Id, string Code, string NameFa, string? NameEn);
public sealed record MrpRouteCandidateListResponse(IReadOnlyList<MrpRouteCandidate> Items,
    int Page, int PageSize, int TotalCount);
public sealed record MrpRunListResponse(IReadOnlyList<MrpRunListItem> Items, int Page, int PageSize, int TotalCount);
public sealed record MrpRunListItem(Guid Id, string RunNumber, DateOnly AsOfDate, DateOnly HorizonEndDate,
    string EngineVersion, string Status, string Completeness, DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    int ExceptionCount);
public sealed record MrpRunResponse(Guid Id, string RunNumber, DateOnly AsOfDate, DateOnly HorizonEndDate,
    string EngineVersion, string Status, string Completeness, string Assumptions, string? ActorIdentifier,
    DateTime StartedAtUtc, DateTime? SnapshotCapturedAtUtc, DateTime? CompletedAtUtc,
    string? FailureCode, string? FailureMessage, int ItemCount, int DailyResultCount, int ExceptionCount);
public sealed record MrpItemRef(Guid Id, string Code, string NameFa, string? NameEn, string ItemType,
    string UnitCode, string UnitNameFa, string? UnitNameEn);
public sealed record MrpItemSummaryResponse(MrpItemRef Item, decimal StartingOnHand, decimal TotalGrossDemand,
    decimal TotalFirmIncoming, decimal TotalNetRequirement, decimal OverdueFirmQty, decimal MissingEtaFirmQty,
    DateOnly? StockoutDate, DateOnly? SafetyBreachDate, string? StockCoverageState,
    DateOnly? StockCoverageDate, int? StockCoverageDays, string? FirmCoverageState,
    DateOnly? FirmCoverageDate, int? FirmCoverageDays, string Completeness);
public sealed record MrpItemSummaryListResponse(IReadOnlyList<MrpItemSummaryResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record MrpDailyResultResponse(DateOnly Date, decimal GrossDemand, decimal FirmIncoming,
    decimal FirmProjected, decimal PlanningBeforeNeed, decimal NetRequirement, decimal PlanningBalance,
    bool IsRootProduct, string Completeness);
public sealed record MrpTraceResponse(DateOnly Date, Guid ParentItemId, Guid ChildItemId,
    Guid? BomRevisionId, Guid? BomLineId, decimal ParentNetRequirement, decimal BaseRequirement,
    decimal ScrapPercent, string ScrapSource, decimal ChildGrossRequirement);
public sealed record MrpItemTimelineResponse(MrpItemRef Item, IReadOnlyList<MrpDailyResultResponse> Daily,
    IReadOnlyList<MrpTraceResponse> Trace);
public sealed record MrpTraceOriginResponse(Guid ProductId, string ProductCode, Guid BusinessPeriodId,
    Guid? ForecastSeriesId, Guid? ForecastRevisionId, DateOnly PeriodStartDate, DateOnly PeriodEndDate,
    DateOnly DemandDate, decimal AllocatedDemandQty, decimal? OfficialForecastQty, decimal ActualMtdQty,
    decimal? RemainingForecastQty, string AllocationState);
public sealed record MrpTraceBomResponse(Guid RevisionId, string RevisionCode, Guid LineId,
    Guid ParentItemId, string ParentItemCode, Guid ChildItemId, string ChildItemCode,
    decimal UsageQty, decimal BaseOutputQty, string ScrapSource, decimal ScrapPercent,
    decimal GrossContribution);
public sealed record MrpTraceInventoryResponse(Guid WarehouseId, string WarehouseCode,
    bool WarehouseIsActive, Guid UnitId, decimal OnHandQty);
public sealed record MrpTraceFirmSupplyResponse(Guid PurchaseOrderId, string PurchaseOrderNumber,
    Guid PurchaseOrderLineId, Guid DeliveryScheduleId, Guid? SupplierId, string? SupplierNumber,
    string? SupplierNameFa, string? SupplierNameEn, decimal OpenQty, DateOnly? Eta, string EtaSource,
    string EtaStatus, string PurchaseOrderStatus, bool AppliedByDemandDate);
public sealed record MrpTraceNettingResponse(decimal GrossRequirement, decimal FirmIncoming,
    decimal FirmProjectedBefore, decimal FirmProjectedAfter, decimal SafetyStock,
    decimal PlanningBeforeNeed, decimal NetRequirement, decimal PlanningBalance);
public sealed record MrpTraceRouteResponse(string? SupplyMethod, string? ChosenRoute,
    string? DecisionSource, IReadOnlyList<string> ExceptionCodes);
public sealed record MrpRequirementTraceResponse(Guid RunId, MrpItemRef Item, DateOnly DemandDate,
    IReadOnlyList<MrpTraceOriginResponse> Origins, IReadOnlyList<MrpTraceBomResponse> Bom,
    IReadOnlyList<MrpTraceInventoryResponse> Inventory, decimal CompanyStartingOnHand,
    bool IncludesInactiveWarehouseStock, IReadOnlyList<MrpTraceFirmSupplyResponse> FirmSupply,
    MrpTraceNettingResponse? Netting, MrpTraceRouteResponse Route);
public sealed record MrpExceptionResponse(Guid Id, Guid? ItemId, string? ItemCode, DateOnly? Date,
    string Code, string Severity, bool AffectsDemand, string Message, string? SourceReference);
public sealed record MrpExceptionListResponse(IReadOnlyList<MrpExceptionResponse> Items,
    int Page, int PageSize, int TotalCount);

public sealed class MrpValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public static MrpValidationException Single(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed class MrpConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
