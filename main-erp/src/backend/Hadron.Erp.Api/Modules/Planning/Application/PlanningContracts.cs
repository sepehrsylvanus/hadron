namespace Hadron.Erp.Api.Modules.Planning.Application;

public sealed record PlanningUnitRef(Guid Id, string Code, string NameFa, string NameEn,
    string? Symbol, bool IsActive);
public sealed record PlanningReferenceRef(Guid Id, string Code, string NameFa, string NameEn,
    bool IsActive);
public sealed record PlanningProductRef(Guid Id, string Code, string NameFa, string? NameEn,
    bool IsActive, PlanningReferenceRef ProductCategory, PlanningReferenceRef? BusinessUnit,
    PlanningUnitRef BaseUnit);

public sealed record BusinessPeriodResponse(Guid Id, int JalaliYear, int JalaliMonth,
    DateOnly StartDate, DateOnly EndDate, string Status, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record BusinessPeriodListResponse(IReadOnlyList<BusinessPeriodResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateBusinessPeriodRequest(int JalaliYear, int JalaliMonth);
public sealed record ChangeBusinessPeriodStateRequest(long ExpectedVersion, string? Reason);
public sealed record BusinessDateResponse(DateOnly TehranBusinessDate, int JalaliYear, int JalaliMonth,
    int JalaliDay, string JalaliDate);
public sealed record ResolveJalaliDateRequest(string JalaliDate);
public sealed record ResolveJalaliDateResponse(string JalaliDate, DateOnly GregorianDate);

public sealed record ForecastRevisionResponse(Guid Id, int RevisionNumber, decimal ForecastQty,
    string? Reason, string? ActorIdentifier, DateTime CreatedAtUtc);
public sealed record ForecastMutationResponse(Guid SeriesId, Guid ProductId, Guid BusinessPeriodId,
    PlanningUnitRef ProductBaseUnit, ForecastRevisionResponse CurrentRevision, long Version);
public sealed record CreateForecastRequest(Guid ProductId, Guid BusinessPeriodId, decimal ForecastQty,
    string? Reason);
public sealed record CreateForecastRevisionRequest(decimal ForecastQty, string? Reason,
    long ExpectedVersion);
public sealed record ForecastHistoryResponse(Guid SeriesId, PlanningProductRef Product,
    BusinessPeriodResponse Period, PlanningUnitRef ProductBaseUnit, Guid CurrentRevisionId,
    long Version, IReadOnlyList<ForecastRevisionResponse> Revisions);

public sealed record ForecastEntryPeriodResponse(string PeriodCode, int JalaliYear, int JalaliMonth,
    DateOnly StartDate, DateOnly EndDate, Guid? BusinessPeriodId, string Status, long? Version,
    bool Exists);
public sealed record ForecastEntryRow(PlanningProductRef Product, Guid? SeriesId,
    int? CurrentRevisionNumber, decimal? CurrentForecastQty, long? SeriesVersion);
public sealed record ForecastEntryGridResponse(ForecastEntryPeriodResponse Period,
    IReadOnlyList<ForecastEntryRow> Items, int Page, int PageSize, int TotalCount);
public sealed record ForecastBatchEntryRequest(Guid ProductId, decimal ForecastQty,
    long? ExpectedVersion);
public sealed record SaveForecastBatchRequest(string PeriodCode, string? Reason,
    IReadOnlyList<ForecastBatchEntryRequest> Entries);
public sealed record SaveForecastBatchResponse(BusinessPeriodResponse Period, int CreatedCount,
    int RevisedCount, int UnchangedCount);

public sealed record ForecastProductOptionListResponse(IReadOnlyList<PlanningProductRef> Items,
    int Page, int PageSize, int TotalCount);
public sealed record ForecastPlanMonthResponse(string PeriodCode, int JalaliYear, int JalaliMonth,
    DateOnly StartDate, DateOnly EndDate, Guid? BusinessPeriodId, string Status);
public sealed record ForecastPlanCellResponse(Guid ProductId, string PeriodCode,
    decimal? CurrentForecastQty, int? CurrentRevisionNumber, long? SeriesVersion);
public sealed record ForecastPlanPreviewRequest(string Name, string StartJalaliDate,
    string EndJalaliDate, IReadOnlyList<Guid> ProductIds);
public sealed record ForecastPlanPreviewResponse(string Name, string StartJalaliDate,
    string EndJalaliDate, DateOnly StartDate, DateOnly EndDate,
    IReadOnlyList<ForecastPlanMonthResponse> Months, IReadOnlyList<PlanningProductRef> Products,
    IReadOnlyList<ForecastPlanCellResponse> Cells);
public sealed record ForecastPlanEntryRequest(Guid ProductId, string PeriodCode,
    decimal ForecastQty, long? ExpectedVersion);
public sealed record SaveForecastPlanRequest(string Name, string StartJalaliDate,
    string EndJalaliDate, string? Reason, bool ConfirmDifferences,
    IReadOnlyList<ForecastPlanEntryRequest> Entries);
public sealed record ForecastValueConflict(Guid ProductId, string ProductCode, string ProductNameFa,
    string? ProductNameEn, string PeriodCode, decimal ExistingQty, decimal ProposedQty,
    long SeriesVersion);
public sealed record SaveForecastPlanResponse(Guid PlanId, string Name, DateOnly StartDate,
    DateOnly EndDate, int CreatedCount, int RevisedCount, int UnchangedCount, int EntryCount);
public sealed record ForecastPlanSummaryResponse(Guid Id, string Name, string StartJalaliDate,
    string EndJalaliDate, DateOnly StartDate, DateOnly EndDate, int ProductCount, int MonthCount,
    int EntryCount, string? ActorIdentifier, DateTime CreatedAtUtc);
public sealed record ForecastPlanListResponse(IReadOnlyList<ForecastPlanSummaryResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record ForecastPlanEntryHistoryResponse(int RevisionNumber, decimal ForecastQty,
    string? Reason, string? ActorIdentifier, DateTime CreatedAtUtc);
public sealed record ForecastPlanDetailCellResponse(Guid EntryId, Guid ProductId, string PeriodCode,
    decimal OriginalForecastQty, decimal ForecastQty, int PlanRevisionNumber,
    Guid ForecastRevisionId, int ForecastRevisionNumber, long SeriesVersion,
    decimal OfficialCurrentForecastQty, int OfficialCurrentRevisionNumber, bool IsOfficialCurrent,
    decimal MonthlyActualQty, int DailyActualEntryCount,
    IReadOnlyList<ForecastPlanEntryHistoryResponse> History);
public sealed record ForecastPlanDetailResponse(ForecastPlanSummaryResponse Plan,
    IReadOnlyList<ForecastPlanMonthResponse> Months, IReadOnlyList<PlanningProductRef> Products,
    IReadOnlyList<ForecastPlanDetailCellResponse> Cells, int Page, int PageSize, int TotalProducts);
public sealed record CorrectForecastPlanEntryRequest(decimal ForecastQty, string? Reason,
    long ExpectedSeriesVersion, int ExpectedPlanRevisionNumber, bool ConfirmDifference);
public sealed record ForecastPlanCorrectionConflict(Guid ProductId, string ProductCode,
    string ProductNameFa, string? ProductNameEn, string PeriodCode, decimal PlanForecastQty,
    decimal OfficialForecastQty, decimal ProposedQty, long SeriesVersion,
    int OfficialRevisionNumber);
public sealed record CorrectForecastPlanEntryResponse(Guid PlanId, Guid EntryId,
    decimal ForecastQty, int PlanRevisionNumber, Guid ForecastRevisionId,
    int ForecastRevisionNumber, long SeriesVersion, bool ForecastRevisionCreated,
    ForecastPlanEntryHistoryResponse History);

public sealed record BomReadinessResponse(string State, IReadOnlyList<string> ReasonCodes,
    DateOnly? EvaluationStart, DateOnly? EvaluationEnd);
public sealed record ForecastPlanningRow(PlanningProductRef Product, Guid? SeriesId,
    Guid? CurrentRevisionId, int? CurrentRevisionNumber, decimal? ForecastQty,
    long? SeriesVersion, decimal MonthlyActual, decimal ActualMtd, decimal? RemainingForecast,
    decimal? PlanningQty, string PeriodPosition, string ForecastState,
    DateOnly? EligibleAllocationStart, string? PlanningExceptionCode,
    BomReadinessResponse BomReadiness);
public sealed record ForecastPlanningListResponse(BusinessPeriodResponse Period,
    DateOnly EvaluationDate, IReadOnlyList<ForecastPlanningRow> Items, int Page, int PageSize,
    int TotalCount);

public sealed record DailyActualRevisionResponse(Guid Id, int RevisionNumber, decimal ActualQty,
    string? Reason, string? ActorIdentifier, DateTime CreatedAtUtc);
public sealed record DailyActualResponse(Guid Id, PlanningProductRef Product,
    BusinessPeriodResponse Period, DateOnly ProductionDate, PlanningUnitRef Unit,
    DailyActualRevisionResponse CurrentRevision, long Version, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
public sealed record DailyActualListResponse(IReadOnlyList<DailyActualResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreateDailyActualRequest(Guid ProductId, DateOnly ProductionDate,
    decimal ActualQty);
public sealed record CorrectDailyActualRequest(decimal ActualQty, string? Reason,
    long ExpectedVersion);
public sealed record DailyActualHistoryResponse(DailyActualResponse Actual,
    IReadOnlyList<DailyActualRevisionResponse> Revisions);

public sealed class PlanningValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Planning validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public static PlanningValidationException Single(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed class PlanningConflictException(string code, string message, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
