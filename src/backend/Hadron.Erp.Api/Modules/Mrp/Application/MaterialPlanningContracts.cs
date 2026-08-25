namespace Hadron.Erp.Api.Modules.Mrp.Application;

public static class MaterialRiskBands
{
    public const string Incomplete = "INCOMPLETE";
    public const string Stockout = "STOCKOUT";
    public const string NoFutureDemand = "NO_FUTURE_DEMAND";
    public const string BeyondHorizon = "BEYOND_HORIZON";
    public const string Critical = "CRITICAL";
    public const string Low = "LOW";
    public const string Safe = "SAFE";
    public const string Excess = "EXCESS";
}

public sealed record MaterialPlanningRunItem(Guid Id, string RunNumber, DateOnly AsOfDate,
    DateOnly HorizonEndDate, string EngineVersion, string Status, string Completeness,
    DateTime CompletedAtUtc, int ExceptionCount);
public sealed record MaterialPlanningRunListResponse(IReadOnlyList<MaterialPlanningRunItem> Items,
    int Page, int PageSize, int TotalCount);
public sealed record MaterialPlanningRunHeader(Guid Id, string RunNumber, DateOnly AsOfDate,
    DateOnly HorizonEndDate, string EngineVersion, string Status, string Completeness,
    DateTime StartedAtUtc, DateTime? SnapshotCapturedAtUtc, DateTime CompletedAtUtc,
    bool IsLatest, int MaterialCount, int ExceptionCount);
public sealed record MaterialPlanningCategoryFacet(Guid Id, string Code, string NameFa, string? NameEn);
public sealed record MaterialPlanningFacetsResponse(IReadOnlyList<MaterialPlanningCategoryFacet> Categories,
    IReadOnlyList<string> SupplyMethods);

public sealed record MaterialPlanningItemRef(Guid Id, string Code, string NameFa, string? NameEn,
    string ItemType, string UnitCode, string UnitNameFa, string? UnitNameEn,
    Guid? CategoryId, string? CategoryCode, string? CategoryNameFa, string? CategoryNameEn,
    string? SupplyMethod);
public sealed record MaterialPlanningSummaryRow(MaterialPlanningItemRef Item, decimal StartingOnHand,
    decimal TotalGrossDemand, decimal TotalFirmIncoming, decimal TotalNetRequirement,
    decimal OverdueFirmQty, decimal MissingEtaFirmQty, DateOnly? StockoutDate,
    DateOnly? SafetyBreachDate, string? StockCoverageState, DateOnly? StockCoverageDate,
    int? StockCoverageDays, string? FirmCoverageState, DateOnly? FirmCoverageDate,
    int? FirmCoverageDays, string Completeness, string RiskBand, string RiskReason,
    int ExceptionCount, string? PrimaryExceptionCode);
public sealed record MaterialPlanningSummaryListResponse(IReadOnlyList<MaterialPlanningSummaryRow> Items,
    int Page, int PageSize, int TotalCount);

public sealed record MaterialPlanningTimelineBucket(DateOnly StartDate, DateOnly EndDate,
    decimal GrossDemand, decimal FirmIncoming, decimal NetRequirement, decimal FirmProjected,
    decimal PlanningBalance, string Completeness, bool IncludesStockoutEvent,
    bool IncludesSafetyBreachEvent);
public sealed record MaterialPlanningTimelineResponse(MaterialPlanningItemRef Item, string Grain,
    decimal SafetyStockQty,
    string? StockCoverageState, DateOnly? StockCoverageDate, int? StockCoverageDays,
    string? FirmCoverageState, DateOnly? FirmCoverageDate, int? FirmCoverageDays,
    IReadOnlyList<MaterialPlanningTimelineBucket> Buckets);

public sealed record MaterialPlanningExceptionRow(Guid Id, Guid? ItemId, string? ItemCode,
    DateOnly? Date, string Code, string Severity, bool AffectsDemand, string Message,
    string SourceModule, string? SourceReference);
public sealed record MaterialPlanningExceptionListResponse(IReadOnlyList<MaterialPlanningExceptionRow> Items,
    int Page, int PageSize, int TotalCount);
