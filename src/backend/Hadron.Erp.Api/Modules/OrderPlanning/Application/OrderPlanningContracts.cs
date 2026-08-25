namespace Hadron.Erp.Api.Modules.OrderPlanning.Application;

public sealed record GenerateOrderPlanningRequest(Guid RunId);
public sealed record SaveExpertDecisionRequest(string DecisionType, long ExpectedVersion, string? Reason,
    DateOnly? ReviewDate, Guid? SourcingOptionId, decimal? FinalQty, DateOnly? TargetReceiptDate);

public sealed record OrderPlanningGenerationResponse(Guid Id, Guid RunId, string RunNumber,
    int GenerationSequence, DateOnly EvaluationDate, DateTime GeneratedAtUtc, string Status,
    bool IsCurrent, DateTime? CompletedAtUtc, string? FailureCode, int RecommendationCount);
public sealed record OrderPlanningRunListItem(Guid Id, string RunNumber, DateOnly AsOfDate,
    DateTime? CompletedAtUtc, string Status);
public sealed record OrderPlanningRunListResponse(IReadOnlyList<OrderPlanningRunListItem> Items,
    int Page, int PageSize, int TotalCount);
public sealed record OrderPlanningCandidateResponse(Guid Id, Guid SourcingOptionId, string SourcingNumber,
    Guid SupplierId, string SupplierNumber, string SupplierNameFa, string? SupplierNameEn,
    bool IsPreferred, bool IsEligible, IReadOnlyList<string> ReasonCodes, decimal? MinimumOrderQuantity,
    int? TotalLeadTimeDays, DateOnly? EarliestFeasibleReceiptDate, bool CanMeetNeedBy,
    Guid? PriceId, decimal? UnitPrice, Guid? CurrencyId, string? CurrencyCode,
    DateOnly? PriceEffectiveFrom, DateOnly? PriceEffectiveTo,
    string? ShippingModeCode, string? IncotermCode, string? CreditAnchor,
    int? SupplierLeadTimeDays, int? ForwarderToShipmentDays, int? ShippingPeriodDays,
    int? DestinationCustomsDays);
public sealed record OrderPlanningDecisionResponse(Guid Id, long DecisionVersion, string DecisionType,
    string? Reason, DateOnly DecisionDate, DateTime DecidedAtUtc, string? ActorIdentifier,
    DateOnly? ReviewDate, Guid? FinalSourcingOptionId, string? FinalSourcingNumber,
    string? FinalSupplierNumber, string? FinalSupplierNameFa, decimal? FinalQty,
    DateOnly? FinalTargetReceiptDate, DateOnly? FinalOrderByDate, decimal? FinalUnitPrice,
    string? FinalCurrencyCode, string WarningCodes, string? FinalSourceSnapshot);
public sealed record OrderRecommendationResponse(Guid Id, Guid GenerationId, Guid RunId, string RunNumber,
    int GenerationSequence, DateOnly RunAsOfDate, DateOnly EvaluationDate, DateTime GeneratedAtUtc,
    bool IsCurrentGeneration, bool IsOutdatedRun,
    Guid ItemId, string ItemCode, string ItemNameFa, string? ItemNameEn, string ItemType,
    string UnitCode, DateOnly NeedByDate, string RouteOrigin, decimal NetRequirementQty,
    decimal SafetyStockQty, string State, string TimingState, DateOnly? EarliestFeasibleReceiptDate,
    DateOnly? TargetReceiptDate, DateOnly? OrderByDate, DateOnly? DecisionHorizonDate,
    int? LateDays, decimal? MinimumBaseline, decimal? BaseRecommendedQty,
    decimal? SystemRecommendedQty, string? Priority, string NextAction,
    decimal? EstimatedValue, IReadOnlyList<string> ReasonCodes, string SourceSelectionReason,
    string CalculationTrace, string AlgorithmVersion,
    long DecisionVersion, OrderPlanningCandidateResponse? SelectedCandidate,
    IReadOnlyList<OrderPlanningCandidateResponse> Candidates,
    IReadOnlyList<OrderPlanningDecisionResponse> Decisions);
public sealed record OrderRecommendationListItem(Guid Id, Guid GenerationId, Guid RunId, string RunNumber,
    int GenerationSequence, DateOnly RunAsOfDate, DateOnly EvaluationDate, bool IsCurrentGeneration, bool IsOutdatedRun,
    Guid ItemId, string ItemCode, string ItemNameFa, string? ItemNameEn, string ItemType,
    DateOnly NeedByDate, DateOnly? OrderByDate, DateOnly? TargetReceiptDate,
    decimal? SystemRecommendedQty, string UnitCode, string State, string TimingState,
    string? Priority, string NextAction, string? SourcingNumber, string? SupplierNumber,
    string? SupplierNameFa, string? SupplierNameEn, string? CurrencyCode, decimal? EstimatedValue,
    long DecisionVersion, string? LatestDecisionType, DateOnly? ReviewDate, IReadOnlyList<string> ReasonCodes);
public sealed record OrderRecommendationListResponse(IReadOnlyList<OrderRecommendationListItem> Items,
    int Page, int PageSize, int TotalCount);

public sealed class OrderPlanningValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Order planning validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public static OrderPlanningValidationException Single(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });
}
public sealed class OrderPlanningConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
