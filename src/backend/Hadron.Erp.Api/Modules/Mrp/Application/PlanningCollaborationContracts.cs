namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed record CreatePlannerWorkItemRequest(Guid ExceptionId, Guid? AssignedUserId);
public sealed record AssignPlannerWorkItemRequest(Guid? AssignedUserId, long ExpectedVersion);
public sealed record AcknowledgePlannerWorkItemRequest(long ExpectedVersion);
public sealed record PlannerAssigneeResponse(Guid Id, string UserName, string FullName);
public sealed record PlannerAssigneeListResponse(IReadOnlyList<PlannerAssigneeResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record PlannerWorkItemResponse(Guid Id, Guid RunId, Guid ExceptionId, string RunNumber,
    Guid? ItemId, string? ItemCode, DateOnly? Date, string Code, string Severity, bool AffectsDemand,
    string Message, Guid? AssignedUserId, string? AssignedUserName, string? AssignedUserFullName,
    string Status, DateTime? AcknowledgedAtUtc, DateTime UpdatedAtUtc, long Version);
public sealed record PlannerWorkItemListResponse(IReadOnlyList<PlannerWorkItemResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record CreatePlannerSavedViewRequest(string Name, string FiltersJson);
public sealed record PlannerSavedViewResponse(Guid Id, string Name, string FiltersJson, DateTime UpdatedAtUtc, long Version);

public sealed record WhatIfForecastOverrideRequest(Guid ProductId, Guid BusinessPeriodId, decimal Quantity);
public sealed record WhatIfInventoryOverrideRequest(Guid ItemId, decimal OnHandQty);
public sealed record WhatIfFirmSupplyOverrideRequest(Guid DeliveryScheduleId, decimal OpenQty, DateOnly? Eta);
public sealed record CreateWhatIfScenarioRequest(string Name, Guid BaseRunId,
    IReadOnlyList<WhatIfForecastOverrideRequest>? ForecastOverrides,
    IReadOnlyList<WhatIfInventoryOverrideRequest>? InventoryOverrides,
    IReadOnlyList<WhatIfFirmSupplyOverrideRequest>? FirmSupplyOverrides);
public sealed record WhatIfScenarioResponse(Guid Id, Guid BaseRunId, string BaseRunNumber, string Name,
    string Status, string Completeness, DateTime CreatedAtUtc, int OverrideCount);
public sealed record WhatIfScenarioListResponse(IReadOnlyList<WhatIfScenarioResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record WhatIfCandidateResponse(Guid Id, string Code, string NameFa, string? NameEn, string Kind);
public sealed record WhatIfCandidateListResponse(IReadOnlyList<WhatIfCandidateResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record WhatIfForecastPeriodResponse(Guid ProductId, Guid BusinessPeriodId,
    DateOnly PeriodStartDate, DateOnly PeriodEndDate, decimal? OfficialForecastQty, decimal ActualMtdQty,
    string AllocationState);
public sealed record WhatIfFirmSupplyCandidateResponse(Guid DeliveryScheduleId, Guid ItemId, string ItemCode,
    string ItemNameFa, string? ItemNameEn, string PurchaseOrderNumber, decimal OpenQty, DateOnly? Eta,
    string EtaStatus);
public sealed record WhatIfFirmSupplyCandidateListResponse(IReadOnlyList<WhatIfFirmSupplyCandidateResponse> Items,
    int Page, int PageSize, int TotalCount);
public sealed record WhatIfResultResponse(Guid ItemId, string ItemCode, string NameFa, string? NameEn,
    string ItemType, decimal OfficialStartingOnHand, decimal ScenarioStartingOnHand,
    decimal OfficialNetRequirement, decimal ScenarioNetRequirement, decimal NetRequirementDelta,
    DateOnly? OfficialStockoutDate, DateOnly? ScenarioStockoutDate, string Completeness);
public sealed record WhatIfResultListResponse(IReadOnlyList<WhatIfResultResponse> Items,
    int Page, int PageSize, int TotalCount);

