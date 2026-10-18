namespace Hadron.Erp.Api.Modules.OrderPlanning.Domain;

public static class OrderPlanningGenerationStatuses
{
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}

public static class RecommendationStates
{
    public const string Ready = "READY";
    public const string Blocked = "BLOCKED";
}

public static class RecommendationTimingStates
{
    public const string OnTime = "ON_TIME";
    public const string Late = "LATE";
}

public static class RecommendationPriorities
{
    public const string Emergency = "P0_EMERGENCY";
    public const string DueNow = "P1_DUE_NOW";
    public const string Upcoming = "P2_UPCOMING";
    public const string Planned = "P3_PLANNED";
}

public static class RecommendationNextActions
{
    public const string ResolveData = "RESOLVE_DATA";
    public const string EmergencyReview = "EMERGENCY_REVIEW";
    public const string OrderNow = "ORDER_NOW";
    public const string OrderByDate = "ORDER_BY_DATE";
}

public static class ExpertDecisionTypes
{
    public const string Accept = "ACCEPT";
    public const string Modify = "MODIFY";
    public const string Reject = "REJECT";
    public const string Defer = "DEFER";
}

public sealed class OrderPlanningGeneration
{
    private OrderPlanningGeneration() { }
    public OrderPlanningGeneration(Guid runId, int generationSequence, DateOnly evaluationDate,
        DateTime generatedAtUtc, string? actorIdentifier)
    {
        Id = Guid.NewGuid(); RunId = runId; GenerationSequence = generationSequence;
        EvaluationDate = evaluationDate; GeneratedAtUtc = generatedAtUtc;
        ActorIdentifier = actorIdentifier; Status = OrderPlanningGenerationStatuses.Running;
    }

    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public int GenerationSequence { get; private set; }
    public DateOnly EvaluationDate { get; private set; }
    public DateTime GeneratedAtUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public bool IsCurrent { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public string? ActorIdentifier { get; private set; }

    public void Complete(DateTime atUtc, bool isCurrent = true)
    {
        Status = OrderPlanningGenerationStatuses.Completed;
        IsCurrent = isCurrent;
        CompletedAtUtc = atUtc;
    }
    public void Supersede() => IsCurrent = false;
    public void Fail(string code, string message, DateTime atUtc)
    { Status = OrderPlanningGenerationStatuses.Failed; IsCurrent = false; FailureCode = code; FailureMessage = message; CompletedAtUtc = atUtc; }
}

public sealed class OrderRecommendation
{
    private OrderRecommendation() { }
    public OrderRecommendation(Guid generationId, Guid itemId, string itemCode, string itemType,
        string itemNameFa, string? itemNameEn, Guid itemBaseUnitId, string unitCode,
        DateOnly needByDate, string routeOrigin, decimal netRequirementQty, decimal safetyStockQty,
        int criticalDays, int safeStartDays, int excessStartDays, string state, string timingState,
        DateOnly? earliestFeasibleReceiptDate, DateOnly? targetReceiptDate, DateOnly? orderByDate,
        DateOnly? decisionHorizonDate, int? lateDays, decimal? minimumBaseline,
        decimal? baseRecommendedQty, decimal? systemRecommendedQty, string? priority,
        string nextAction, Guid? selectedCandidateId, decimal? estimatedValue,
        string reasonCodes, string sourceSelectionReason, string calculationTrace,
        string algorithmVersion)
    {
        Id = Guid.NewGuid(); GenerationId = generationId; ItemId = itemId; ItemCode = itemCode;
        ItemType = itemType; ItemNameFa = itemNameFa; ItemNameEn = itemNameEn;
        ItemBaseUnitId = itemBaseUnitId; UnitCode = unitCode; NeedByDate = needByDate;
        RouteOrigin = routeOrigin; NetRequirementQty = netRequirementQty; SafetyStockQty = safetyStockQty;
        CriticalDays = criticalDays; SafeStartDays = safeStartDays; ExcessStartDays = excessStartDays;
        State = state; TimingState = timingState; EarliestFeasibleReceiptDate = earliestFeasibleReceiptDate;
        TargetReceiptDate = targetReceiptDate; OrderByDate = orderByDate; DecisionHorizonDate = decisionHorizonDate;
        LateDays = lateDays; MinimumBaseline = minimumBaseline; BaseRecommendedQty = baseRecommendedQty;
        SystemRecommendedQty = systemRecommendedQty; Priority = priority; NextAction = nextAction;
        SelectedCandidateId = selectedCandidateId; EstimatedValue = estimatedValue;
        ReasonCodes = reasonCodes; SourceSelectionReason = sourceSelectionReason;
        CalculationTrace = calculationTrace; AlgorithmVersion = algorithmVersion;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid GenerationId { get; private set; }
    public Guid ItemId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public string ItemType { get; private set; } = string.Empty;
    public string ItemNameFa { get; private set; } = string.Empty;
    public string? ItemNameEn { get; private set; }
    public Guid ItemBaseUnitId { get; private set; }
    public string UnitCode { get; private set; } = string.Empty;
    public DateOnly NeedByDate { get; private set; }
    public string RouteOrigin { get; private set; } = string.Empty;
    public decimal NetRequirementQty { get; private set; }
    public decimal SafetyStockQty { get; private set; }
    public int CriticalDays { get; private set; }
    public int SafeStartDays { get; private set; }
    public int ExcessStartDays { get; private set; }
    public string State { get; private set; } = string.Empty;
    public string TimingState { get; private set; } = string.Empty;
    public DateOnly? EarliestFeasibleReceiptDate { get; private set; }
    public DateOnly? TargetReceiptDate { get; private set; }
    public DateOnly? OrderByDate { get; private set; }
    public DateOnly? DecisionHorizonDate { get; private set; }
    public int? LateDays { get; private set; }
    public decimal? MinimumBaseline { get; private set; }
    public decimal? BaseRecommendedQty { get; private set; }
    public decimal? SystemRecommendedQty { get; private set; }
    public string? Priority { get; private set; }
    public string NextAction { get; private set; } = string.Empty;
    public Guid? SelectedCandidateId { get; private set; }
    public decimal? EstimatedValue { get; private set; }
    public string ReasonCodes { get; private set; } = string.Empty;
    public string SourceSelectionReason { get; private set; } = string.Empty;
    public string CalculationTrace { get; private set; } = string.Empty;
    public string AlgorithmVersion { get; private set; } = string.Empty;
    public long DecisionVersion { get; private set; } = 0;
    public DateTime CreatedAtUtc { get; private set; }
    public void AdvanceDecisionVersion() => DecisionVersion++;
}

public sealed class OrderRecommendationCandidate
{
    private OrderRecommendationCandidate() { }
    public OrderRecommendationCandidate(Guid generationId, Guid itemId, Guid sourcingOptionId,
        string sourcingNumber, Guid supplierId, string supplierNumber, string supplierNameFa,
        string? supplierNameEn, bool isPreferred, bool isEligible, string reasonCodes,
        Guid itemBaseUnitId, decimal? minimumOrderQuantity, Guid? shippingModeId,
        string? shippingModeCode, string? shippingModeNameFa, string? shippingModeNameEn,
        Guid? incotermId, string? incotermCode, string? incotermNameFa, string? incotermNameEn,
        int? supplierLeadTimeDays, int? forwarderToShipmentDays, int? shippingPeriodDays,
        int? destinationCustomsDays, int? totalLeadTimeDays, DateOnly? earliestFeasibleReceiptDate,
        bool canMeetNeedBy, Guid? priceId, decimal? unitPrice, Guid? currencyId,
        string? currencyCode, string? currencyNameFa, string? currencyNameEn,
        DateOnly? priceEffectiveFrom, DateOnly? priceEffectiveTo,
        Guid? paymentTermId, string? paymentTermCode, string? paymentTermNameFa,
        string? paymentTermNameEn, decimal? advancePercent, decimal? beforeShipmentPercent,
        decimal? creditPercent, int? creditDays, string? creditAnchor)
    {
        Id = Guid.NewGuid(); GenerationId = generationId; ItemId = itemId; SourcingOptionId = sourcingOptionId;
        SourcingNumber = sourcingNumber; SupplierId = supplierId; SupplierNumber = supplierNumber;
        SupplierNameFa = supplierNameFa; SupplierNameEn = supplierNameEn; IsPreferred = isPreferred;
        IsEligible = isEligible; ReasonCodes = reasonCodes; ItemBaseUnitId = itemBaseUnitId;
        MinimumOrderQuantity = minimumOrderQuantity; ShippingModeId = shippingModeId;
        ShippingModeCode = shippingModeCode; ShippingModeNameFa = shippingModeNameFa;
        ShippingModeNameEn = shippingModeNameEn; IncotermId = incotermId; IncotermCode = incotermCode;
        IncotermNameFa = incotermNameFa; IncotermNameEn = incotermNameEn;
        SupplierLeadTimeDays = supplierLeadTimeDays; ForwarderToShipmentDays = forwarderToShipmentDays;
        ShippingPeriodDays = shippingPeriodDays; DestinationCustomsDays = destinationCustomsDays;
        TotalLeadTimeDays = totalLeadTimeDays; EarliestFeasibleReceiptDate = earliestFeasibleReceiptDate;
        CanMeetNeedBy = canMeetNeedBy; PriceId = priceId; UnitPrice = unitPrice; CurrencyId = currencyId;
        CurrencyCode = currencyCode; CurrencyNameFa = currencyNameFa; CurrencyNameEn = currencyNameEn;
        PriceEffectiveFrom = priceEffectiveFrom; PriceEffectiveTo = priceEffectiveTo;
        PaymentTermId = paymentTermId; PaymentTermCode = paymentTermCode;
        PaymentTermNameFa = paymentTermNameFa; PaymentTermNameEn = paymentTermNameEn; AdvancePercent = advancePercent;
        BeforeShipmentPercent = beforeShipmentPercent; CreditPercent = creditPercent;
        CreditDays = creditDays; CreditAnchor = creditAnchor;
    }
    public Guid Id { get; private set; }
    public Guid GenerationId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid SourcingOptionId { get; private set; }
    public string SourcingNumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public string SupplierNumber { get; private set; } = string.Empty;
    public string SupplierNameFa { get; private set; } = string.Empty;
    public string? SupplierNameEn { get; private set; }
    public bool IsPreferred { get; private set; }
    public bool IsEligible { get; private set; }
    public string ReasonCodes { get; private set; } = string.Empty;
    public Guid ItemBaseUnitId { get; private set; }
    public decimal? MinimumOrderQuantity { get; private set; }
    public Guid? ShippingModeId { get; private set; }
    public string? ShippingModeCode { get; private set; }
    public string? ShippingModeNameFa { get; private set; }
    public string? ShippingModeNameEn { get; private set; }
    public Guid? IncotermId { get; private set; }
    public string? IncotermCode { get; private set; }
    public string? IncotermNameFa { get; private set; }
    public string? IncotermNameEn { get; private set; }
    public int? SupplierLeadTimeDays { get; private set; }
    public int? ForwarderToShipmentDays { get; private set; }
    public int? ShippingPeriodDays { get; private set; }
    public int? DestinationCustomsDays { get; private set; }
    public int? TotalLeadTimeDays { get; private set; }
    public DateOnly? EarliestFeasibleReceiptDate { get; private set; }
    public bool CanMeetNeedBy { get; private set; }
    public Guid? PriceId { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public Guid? CurrencyId { get; private set; }
    public string? CurrencyCode { get; private set; }
    public string? CurrencyNameFa { get; private set; }
    public string? CurrencyNameEn { get; private set; }
    public DateOnly? PriceEffectiveFrom { get; private set; }
    public DateOnly? PriceEffectiveTo { get; private set; }
    public Guid? PaymentTermId { get; private set; }
    public string? PaymentTermCode { get; private set; }
    public string? PaymentTermNameFa { get; private set; }
    public string? PaymentTermNameEn { get; private set; }
    public decimal? AdvancePercent { get; private set; }
    public decimal? BeforeShipmentPercent { get; private set; }
    public decimal? CreditPercent { get; private set; }
    public int? CreditDays { get; private set; }
    public string? CreditAnchor { get; private set; }
}

public sealed class OrderRecommendationDecision
{
    private OrderRecommendationDecision() { }
    public OrderRecommendationDecision(Guid recommendationId, long decisionVersion, string decisionType,
        string? reason, DateOnly decisionDate, DateTime decidedAtUtc, string? actorIdentifier,
        DateOnly? reviewDate, Guid? finalSourcingOptionId, string? finalSourcingNumber,
        Guid? finalSupplierId, string? finalSupplierNumber, string? finalSupplierNameFa,
        decimal? finalQty, DateOnly? finalTargetReceiptDate, DateOnly? finalOrderByDate,
        Guid? finalPriceId, decimal? finalUnitPrice, Guid? finalCurrencyId, string? finalCurrencyCode,
        decimal? finalMinimumOrderQuantity, int? finalTotalLeadTimeDays, Guid? finalBaseUnitId,
        string warningCodes, string? finalSourceSnapshot)
    {
        Id = Guid.NewGuid(); RecommendationId = recommendationId; DecisionVersion = decisionVersion;
        DecisionType = decisionType; Reason = reason; DecisionDate = decisionDate; DecidedAtUtc = decidedAtUtc;
        ActorIdentifier = actorIdentifier; ReviewDate = reviewDate; FinalSourcingOptionId = finalSourcingOptionId;
        FinalSourcingNumber = finalSourcingNumber; FinalSupplierId = finalSupplierId;
        FinalSupplierNumber = finalSupplierNumber; FinalSupplierNameFa = finalSupplierNameFa;
        FinalQty = finalQty; FinalTargetReceiptDate = finalTargetReceiptDate; FinalOrderByDate = finalOrderByDate;
        FinalPriceId = finalPriceId; FinalUnitPrice = finalUnitPrice; FinalCurrencyId = finalCurrencyId;
        FinalCurrencyCode = finalCurrencyCode; FinalMinimumOrderQuantity = finalMinimumOrderQuantity;
        FinalTotalLeadTimeDays = finalTotalLeadTimeDays; FinalBaseUnitId = finalBaseUnitId;
        WarningCodes = warningCodes; FinalSourceSnapshot = finalSourceSnapshot;
    }
    public Guid Id { get; private set; }
    public Guid RecommendationId { get; private set; }
    public long DecisionVersion { get; private set; }
    public string DecisionType { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public DateOnly DecisionDate { get; private set; }
    public DateTime DecidedAtUtc { get; private set; }
    public string? ActorIdentifier { get; private set; }
    public DateOnly? ReviewDate { get; private set; }
    public Guid? FinalSourcingOptionId { get; private set; }
    public string? FinalSourcingNumber { get; private set; }
    public Guid? FinalSupplierId { get; private set; }
    public string? FinalSupplierNumber { get; private set; }
    public string? FinalSupplierNameFa { get; private set; }
    public decimal? FinalQty { get; private set; }
    public DateOnly? FinalTargetReceiptDate { get; private set; }
    public DateOnly? FinalOrderByDate { get; private set; }
    public Guid? FinalPriceId { get; private set; }
    public decimal? FinalUnitPrice { get; private set; }
    public Guid? FinalCurrencyId { get; private set; }
    public string? FinalCurrencyCode { get; private set; }
    public decimal? FinalMinimumOrderQuantity { get; private set; }
    public int? FinalTotalLeadTimeDays { get; private set; }
    public Guid? FinalBaseUnitId { get; private set; }
    public string WarningCodes { get; private set; } = string.Empty;
    public string? FinalSourceSnapshot { get; private set; }
}
