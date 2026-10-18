namespace Hadron.Erp.Api.Modules.Mrp.Domain;

public static class PlannerWorkItemStatuses
{
    public const string Open = "OPEN";
    public const string Acknowledged = "ACKNOWLEDGED";
}

public static class WhatIfScenarioStatuses
{
    public const string Calculated = "CALCULATED";
}

public sealed class PlannerWorkItem
{
    private PlannerWorkItem() { }

    public PlannerWorkItem(Guid runId, Guid exceptionId, Guid? assignedUserId, string actorIdentifier,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid(); RunId = runId; ExceptionId = exceptionId; AssignedUserId = assignedUserId;
        Status = PlannerWorkItemStatuses.Open; CreatedByIdentifier = actorIdentifier;
        CreatedAtUtc = UpdatedAtUtc = createdAtUtc; Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ExceptionId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public string CreatedByIdentifier { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Assign(Guid? userId, DateTime timestampUtc)
    {
        AssignedUserId = userId; Touch(timestampUtc);
    }

    public void Acknowledge(Guid? actorUserId, DateTime timestampUtc)
    {
        if (Status == PlannerWorkItemStatuses.Acknowledged) return;
        Status = PlannerWorkItemStatuses.Acknowledged; AcknowledgedByUserId = actorUserId;
        AcknowledgedAtUtc = timestampUtc; Touch(timestampUtc);
    }

    private void Touch(DateTime timestampUtc) { Version++; UpdatedAtUtc = timestampUtc; }
}

public sealed class PlannerWorkItemEvent
{
    private PlannerWorkItemEvent() { }
    public PlannerWorkItemEvent(Guid workItemId, string eventType, string actorIdentifier,
        Guid? assignedUserId, DateTime occurredAtUtc)
    {
        Id = Guid.NewGuid(); WorkItemId = workItemId; EventType = eventType;
        ActorIdentifier = actorIdentifier; AssignedUserId = assignedUserId; OccurredAtUtc = occurredAtUtc;
    }
    public Guid Id { get; private set; }
    public Guid WorkItemId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string ActorIdentifier { get; private set; } = string.Empty;
    public Guid? AssignedUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

public sealed class PlannerSavedView
{
    private PlannerSavedView() { }
    public PlannerSavedView(string ownerIdentifier, string name, string filtersJson, DateTime timestampUtc)
    {
        Id = Guid.NewGuid(); OwnerIdentifier = ownerIdentifier; Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant(); FiltersJson = filtersJson;
        CreatedAtUtc = UpdatedAtUtc = timestampUtc; Version = 1;
    }
    public Guid Id { get; private set; }
    public string OwnerIdentifier { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string FiltersJson { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
}

public sealed class WhatIfScenario
{
    private WhatIfScenario() { }
    public WhatIfScenario(Guid baseRunId, string name, string ownerIdentifier, string completeness,
        DateTime timestampUtc)
    {
        Id = Guid.NewGuid(); BaseRunId = baseRunId; Name = name.Trim(); OwnerIdentifier = ownerIdentifier;
        Status = WhatIfScenarioStatuses.Calculated; Completeness = completeness;
        CreatedAtUtc = CompletedAtUtc = timestampUtc;
    }
    public Guid Id { get; private set; }
    public Guid BaseRunId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string OwnerIdentifier { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string Completeness { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }
}

public sealed class WhatIfForecastOverride
{
    private WhatIfForecastOverride() { }
    public WhatIfForecastOverride(Guid scenarioId, Guid productId, Guid businessPeriodId, decimal quantity)
    { Id = Guid.NewGuid(); ScenarioId = scenarioId; ProductId = productId; BusinessPeriodId = businessPeriodId; Quantity = quantity; }
    public Guid Id { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BusinessPeriodId { get; private set; }
    public decimal Quantity { get; private set; }
}

public sealed class WhatIfInventoryOverride
{
    private WhatIfInventoryOverride() { }
    public WhatIfInventoryOverride(Guid scenarioId, Guid itemId, decimal onHandQty)
    { Id = Guid.NewGuid(); ScenarioId = scenarioId; ItemId = itemId; OnHandQty = onHandQty; }
    public Guid Id { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal OnHandQty { get; private set; }
}

public sealed class WhatIfFirmSupplyOverride
{
    private WhatIfFirmSupplyOverride() { }
    public WhatIfFirmSupplyOverride(Guid scenarioId, Guid deliveryScheduleId, decimal openQty, DateOnly? eta)
    { Id = Guid.NewGuid(); ScenarioId = scenarioId; DeliveryScheduleId = deliveryScheduleId; OpenQty = openQty; Eta = eta; }
    public Guid Id { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid DeliveryScheduleId { get; private set; }
    public decimal OpenQty { get; private set; }
    public DateOnly? Eta { get; private set; }
}

public sealed class WhatIfDailyResult
{
    private WhatIfDailyResult() { }
    public WhatIfDailyResult(Guid scenarioId, Guid itemId, DateOnly date, decimal grossDemand,
        decimal firmIncoming, decimal firmProjected, decimal planningBeforeNeed, decimal netRequirement,
        decimal planningBalance, bool isRootProduct, string completeness)
    {
        Id = Guid.NewGuid(); ScenarioId = scenarioId; ItemId = itemId; Date = date;
        GrossDemand = grossDemand; FirmIncoming = firmIncoming; FirmProjected = firmProjected;
        PlanningBeforeNeed = planningBeforeNeed; NetRequirement = netRequirement;
        PlanningBalance = planningBalance; IsRootProduct = isRootProduct; Completeness = completeness;
    }
    public Guid Id { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid ItemId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal GrossDemand { get; private set; }
    public decimal FirmIncoming { get; private set; }
    public decimal FirmProjected { get; private set; }
    public decimal PlanningBeforeNeed { get; private set; }
    public decimal NetRequirement { get; private set; }
    public decimal PlanningBalance { get; private set; }
    public bool IsRootProduct { get; private set; }
    public string Completeness { get; private set; } = string.Empty;
}

public sealed class WhatIfItemSummary
{
    private WhatIfItemSummary() { }
    public WhatIfItemSummary(Guid scenarioId, Guid itemId, decimal startingOnHand, decimal totalGrossDemand,
        decimal totalFirmIncoming, decimal totalNetRequirement, DateOnly? stockoutDate,
        DateOnly? safetyBreachDate, string completeness)
    {
        Id = Guid.NewGuid(); ScenarioId = scenarioId; ItemId = itemId; StartingOnHand = startingOnHand;
        TotalGrossDemand = totalGrossDemand; TotalFirmIncoming = totalFirmIncoming;
        TotalNetRequirement = totalNetRequirement; StockoutDate = stockoutDate;
        SafetyBreachDate = safetyBreachDate; Completeness = completeness;
    }
    public Guid Id { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal StartingOnHand { get; private set; }
    public decimal TotalGrossDemand { get; private set; }
    public decimal TotalFirmIncoming { get; private set; }
    public decimal TotalNetRequirement { get; private set; }
    public DateOnly? StockoutDate { get; private set; }
    public DateOnly? SafetyBreachDate { get; private set; }
    public string Completeness { get; private set; } = string.Empty;
}

public sealed class WhatIfException
{
    private WhatIfException() { }
    public WhatIfException(Guid scenarioId, Guid? itemId, DateOnly? date, string code, string severity,
        bool affectsDemand, string message, string? sourceReference)
    {
        Id = Guid.NewGuid(); ScenarioId = scenarioId; ItemId = itemId; Date = date; Code = code;
        Severity = severity; AffectsDemand = affectsDemand; Message = message; SourceReference = sourceReference;
    }
    public Guid Id { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid? ItemId { get; private set; }
    public DateOnly? Date { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public bool AffectsDemand { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? SourceReference { get; private set; }
}
