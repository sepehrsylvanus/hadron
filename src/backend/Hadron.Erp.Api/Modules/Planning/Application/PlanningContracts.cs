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
public sealed record BusinessDateResponse(DateOnly TehranBusinessDate, int JalaliYear, int JalaliMonth);

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
